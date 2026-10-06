using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Gazillion;
using MHServerEmu.Core.System.Random;
using MHServerEmu.Core.Collisions;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.Behavior;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Loot;
using MHServerEmu.Games.Navi;
using MHServerEmu.Games.Network;
using MHServerEmu.Games.Populations;
using MHServerEmu.Games.Properties;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Spawns hostile agents for script-driven encounters (boss rushes, invasions). Call from the game thread that owns the region.
    /// </summary>
    public static class ScriptSpawner
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private const string BossPathPrefix = "Entity/Characters/Bosses/";
        private const float AggroRangeHostile = 12000.0f;
        private const float AggroRangeAlly = 6000.0f;

        // Boss prototypes that are not real fights (holograms, test / cut content, cinematic props)
        private static readonly string[] ExcludedBossPathFragments =
        [
            "zzz", "Deprecated", "Test", "Debug", "Hologram", "Cinematic", "Dummy", "Summon", "Clone", "Illusion", "Decoy", "Prop"
        ];

        private static readonly ConcurrentDictionary<string, PrototypeId> _findCache = new(StringComparer.OrdinalIgnoreCase);

        // Tags of entities spawned by SpawnInteractable (entries vanish with the entity)
        private static readonly ConditionalWeakTable<WorldEntity, string> _tags = new();

        // Ground items placed as scenery: the pickup handler ignores them
        private static readonly ConditionalWeakTable<WorldEntity, object> _sceneryItems = new();

        /// <summary>
        /// Resolves an agent prototype. <paramref name="pathOrNames"/> is either a full prototype path
        /// (Entity/Characters/Bosses/.../X.prototype) or one or more '|'-separated name fragments ("DoctorOctopus|DocOck")
        /// matched against boss prototype paths, ignoring case, spaces and punctuation.
        /// </summary>
        /// <param name="preferPathContains">Optional '|'-separated words; matches whose path contains one are preferred (e.g. "Patrol|Midtown").</param>
        public static PrototypeId FindAgent(string pathOrNames, string preferPathContains = null)
        {
            if (string.IsNullOrWhiteSpace(pathOrNames))
                return PrototypeId.Invalid;

            string cacheKey = $"{pathOrNames}#{preferPathContains}";
            return _findCache.GetOrAdd(cacheKey, _ => FindAgentUncached(pathOrNames, preferPathContains));
        }

        /// <summary>
        /// Spawns <paramref name="agentRef"/> as a hostile, aggroed agent at a free spot <paramref name="minDistance"/>-<paramref name="maxDistance"/>
        /// from <paramref name="near"/>. If <paramref name="target"/> is set, the agent goes straight for it. Returns <see langword="null"/> on failure.
        /// With <paramref name="aggroed"/> = <see langword="false"/> it guards its spot with normal aggro range instead of
        /// hunting players across the map.
        /// With <paramref name="ignoreCrowds"/> = <see langword="true"/> players and other characters never push the spawn away: it lands
        /// within <paramref name="maxDistance"/> even if the spot is packed (overlapping characters push apart on their own).
        /// </summary>
        public static Agent SpawnHostile(Region region, PrototypeId agentRef, Vector3 near, float minDistance, float maxDistance,
            Avatar target = null, bool dropLoot = true, bool giveExperience = true, bool aggroed = true, bool ignoreCrowds = false)
        {
            AgentPrototype agentProto = agentRef.As<AgentPrototype>();
            if (region == null || agentProto == null)
                return null;

            if (TryFindSpawnPosition(region, agentProto, near, minDistance, maxDistance, out Vector3 position, out Cell cell, ignoreCrowds) == false)
                return Logger.WarnReturn<Agent>(null, $"SpawnHostile(): No free spot for [{agentRef.GetName()}] in [{region.PrototypeName}]");

            if (agentProto.Bounds != null)
                position.Z += agentProto.Bounds.GetBoundHalfHeight();

            PrototypeId allianceRef = ResolveHostileAlliance(agentProto);

            using EntitySettings settings = ObjectPoolManager.Instance.Get<EntitySettings>();
            settings.EntityRef = agentRef;
            settings.Position = position;
            settings.Orientation = Orientation.Zero;
            settings.RegionId = region.Id;
            settings.Cell = cell;
            settings.IsPopulation = true;

            using PropertyCollection properties = ObjectPoolManager.Instance.Get<PropertyCollection>();
            int level = cell.Area.GetCharacterLevel(agentProto);
            properties[PropertyEnum.CharacterLevel] = level;
            properties[PropertyEnum.CombatLevel] = level;
            properties[PropertyEnum.DifficultyTier] = region.DifficultyTierRef;
            properties[PropertyEnum.AllianceOverride] = allianceRef;
            properties[PropertyEnum.Rank] = agentProto.Rank;
            properties[PropertyEnum.NoLootDrop] = dropLoot == false;
            properties[PropertyEnum.NoExpOnDeath] = giveExperience == false;
            properties[PropertyEnum.Dormant] = false;
            properties[PropertyEnum.IgnoreMissionOwnerForTargeting] = true;
            properties[PropertyEnum.Visible] = true;
            settings.Properties = properties;

            if (region.Game.EntityManager.CreateEntity(settings) is not Agent agent)
                return Logger.WarnReturn<Agent>(null, $"SpawnHostile(): Failed to create [{agentRef.GetName()}]");

            if (agentProto.ModifiersGuaranteed != null)
            {
                foreach (PrototypeId boost in agentProto.ModifiersGuaranteed)
                    agent.Properties[PropertyEnum.EnemyBoost, boost] = true;
            }

            agent.Properties[PropertyEnum.AllianceOverride] = allianceRef;
            agent.Properties.RemoveProperty(PropertyEnum.MissionPrototype);
            agent.Properties[PropertyEnum.Untargetable] = false;
            agent.Properties[PropertyEnum.Unaffectable] = false;
            agent.Properties[PropertyEnum.Invulnerable] = false;
            agent.SetDormant(false);
            agent.SetSimulated(true);
            agent.ActivateAI();

            if (aggroed)
                PrimeCombat(agent, target);

            PlaySpawnVisual(agent);
            return agent;
        }

        /// <summary>
        /// EXPERIMENTAL. Spawns a mirror image of <paramref name="owner"/>: an allied agent that every client draws as the
        /// owner's own hero and costume. <paramref name="bodyRef"/> is the agent that really exists on the server and supplies
        /// the AI and attacks (a summoned ally, e.g. an item illusion); it fights for the owner and vanishes after
        /// <paramref name="lifespanSeconds"/> (0 = stays until despawned). Returns <see langword="null"/> on failure.
        /// </summary>
        /// <remarks>
        /// The client plays the body's attacks on the hero's model, so attacks whose animations the hero does not have show
        /// no animation.
        /// </remarks>
        public static Agent SpawnMirrorImage(Avatar owner, PrototypeId bodyRef, float lifespanSeconds = 0f, string name = "")
        {
            AgentPrototype bodyProto = bodyRef.As<AgentPrototype>();
            Region region = owner?.Region;
            if (region == null || owner.IsInWorld == false || bodyProto == null || bodyProto is AvatarPrototype)
                return null;

            Vector3 ownerPosition = owner.RegionLocation.Position;
            if (TryFindSpawnPosition(region, bodyProto, ownerPosition, 80f, 300f, out Vector3 position, out Cell cell, true) == false)
                return Logger.WarnReturn<Agent>(null, $"SpawnMirrorImage(): No free spot near [{owner}]");

            if (bodyProto.Bounds != null)
                position.Z += bodyProto.Bounds.GetBoundHalfHeight();

            using EntitySettings settings = ObjectPoolManager.Instance.Get<EntitySettings>();
            settings.EntityRef = bodyRef;
            settings.Position = position;
            settings.Orientation = owner.RegionLocation.Orientation;
            settings.RegionId = region.Id;
            settings.Cell = cell;
            settings.ClientAvatarPrototypeRef = owner.PrototypeDataRef;
            settings.ClientAvatarName = name;

            if (lifespanSeconds > 0f)
                settings.Lifespan = TimeSpan.FromSeconds(lifespanSeconds);

            using PropertyCollection properties = ObjectPoolManager.Instance.Get<PropertyCollection>();
            properties[PropertyEnum.CharacterLevel] = owner.CharacterLevel;
            properties[PropertyEnum.CombatLevel] = owner.CombatLevel;
            properties[PropertyEnum.DifficultyTier] = region.DifficultyTierRef;
            properties[PropertyEnum.AllianceOverride] = owner.Alliance != null ? owner.Alliance.DataRef : PrototypeId.Invalid;
            properties[PropertyEnum.PowerUserOverrideID] = owner.Id;     // its damage counts as the owner's, and its AI assists the owner
            properties[PropertyEnum.CostumeCurrent] = owner.EquippedCostumeRef;

            // The body's own rank would give it that rank's stats (a boss body: damage x6, health x75). A mirror image is an ally:
            // the team-up rank carries no stat changes.
            PrototypeId allyRankRef = GameDatabase.GetPrototypeRefByName("Mods/Ranks/TeamUp.prototype");
            if (allyRankRef != PrototypeId.Invalid)
                properties[PropertyEnum.Rank] = allyRankRef;
            properties[PropertyEnum.NoLootDrop] = true;
            properties[PropertyEnum.NoExpOnDeath] = true;
            properties[PropertyEnum.Dormant] = false;
            properties[PropertyEnum.Visible] = true;
            settings.Properties = properties;

            if (region.Game.EntityManager.CreateEntity(settings) is not Agent agent)
                return Logger.WarnReturn<Agent>(null, $"SpawnMirrorImage(): Failed to create [{bodyRef.GetName()}]");

            agent.Properties.RemoveProperty(PropertyEnum.MissionPrototype);
            agent.SetDormant(false);
            agent.SetSimulated(true);
            agent.ActivateAI();
            return agent;
        }

        /// <summary>
        /// Finds a free walkable spot <paramref name="minDistance"/>-<paramref name="maxDistance"/> from <paramref name="near"/>
        /// (same relaxed placement as <see cref="SpawnHostile"/>), e.g. for placing an NPC next to a region's start.
        /// </summary>
        public static bool TryFindSpotNear(Region region, Vector3 near, float minDistance, float maxDistance, out Vector3 position)
        {
            position = Vector3.Zero;
            if (region == null)
                return false;

            return TryFindSpawnPosition(region, null, near, minDistance, maxDistance, out position, out _);
        }

        /// <summary>
        /// Spawns a friendly, clickable entity (an NPC or an object) at <paramref name="position"/>, facing <paramref name="yawDegrees"/>.
        /// Clicking it raises <see cref="ScriptHooks.EntityInteracted"/> with <see cref="EntityInteractedArgs.ScriptTag"/> = <paramref name="tag"/>.
        /// It cannot be damaged and never drops loot. Returns <see langword="null"/> on failure.
        /// </summary>
        public static WorldEntity SpawnInteractable(Region region, PrototypeId entityRef, Vector3 position, float yawDegrees, string tag)
        {
            WorldEntityPrototype entityProto = entityRef.As<WorldEntityPrototype>();
            if (region == null || entityProto == null)
                return Logger.WarnReturn<WorldEntity>(null, $"SpawnInteractable(): [{entityRef.GetName()}] is not a world entity prototype");

            if (TryGetCell(region, position, out Vector3 floorPosition, out Cell cell) == false)
                return Logger.WarnReturn<WorldEntity>(null, $"SpawnInteractable(): {position} is outside the cells of [{region.PrototypeName}]");

            if (entityProto.Bounds != null)
                floorPosition.Z += entityProto.Bounds.GetBoundHalfHeight();

            using EntitySettings settings = ObjectPoolManager.Instance.Get<EntitySettings>();
            settings.EntityRef = entityRef;
            settings.Position = floorPosition;
            settings.Orientation = new(MathHelper.ToRadians(yawDegrees), 0f, 0f);
            settings.RegionId = region.Id;
            settings.Cell = cell;

            using PropertyCollection properties = ObjectPoolManager.Instance.Get<PropertyCollection>();
            properties[PropertyEnum.Invulnerable] = true;
            properties[PropertyEnum.NoLootDrop] = true;
            properties[PropertyEnum.NoExpOnDeath] = true;
            properties[PropertyEnum.Dormant] = false;
            properties[PropertyEnum.Visible] = true;

            if (entityProto is AgentPrototype agentProto)
            {
                int level = cell.Area.GetCharacterLevel(agentProto);
                properties[PropertyEnum.CharacterLevel] = level;
                properties[PropertyEnum.CombatLevel] = level;
                properties[PropertyEnum.AllianceOverride] = ResolveFriendlyAlliance(agentProto);
            }

            settings.Properties = properties;

            if (region.Game.EntityManager.CreateEntity(settings) is not WorldEntity entity)
                return Logger.WarnReturn<WorldEntity>(null, $"SpawnInteractable(): Failed to create [{entityRef.GetName()}]");

            // The same flag the game's own scripted NPCs use: the client shows the talk / use interaction for it
            entity.Properties[PropertyEnum.EntSelActHasInteractOption] = true;
            entity.Properties[PropertyEnum.EntSelActInteractOptDisabled] = false;
            entity.Properties.RemoveProperty(PropertyEnum.MissionPrototype);

            if (entityProto is AgentPrototype)
                entity.Properties[PropertyEnum.AllianceOverride] = properties[PropertyEnum.AllianceOverride];

            _tags.AddOrUpdate(entity, tag ?? string.Empty);
            return entity;
        }

        /// <summary>
        /// Puts an item on the ground at <paramref name="position"/>, like a drop that belongs to nobody: every player sees
        /// it and anyone can pick it up. It stays until it is picked up or despawned. Useful for items whose only model is
        /// their dropped form (event currencies such as the Halloween Pumpkin or the Christmas tree). Tagged like
        /// <see cref="SpawnInteractable"/> entities. Returns <see langword="null"/> on failure.
        /// </summary>
        /// <remarks>
        /// With <paramref name="canPickUp"/> = false the item is scenery: the server ignores every attempt to pick it up.
        /// The client still treats it as an item (it shows its name and lets players click it), nothing happens when they do.
        /// </remarks>
        public static WorldEntity SpawnGroundItem(Region region, PrototypeId itemRef, Vector3 position, string tag, bool canPickUp = true)
        {
            if (region == null || itemRef.As<ItemPrototype>() == null)
                return Logger.WarnReturn<WorldEntity>(null, $"SpawnGroundItem(): [{itemRef.GetName()}] is not an item prototype");

            if (TryGetCell(region, position, out Vector3 floorPosition, out Cell cell) == false)
                return Logger.WarnReturn<WorldEntity>(null, $"SpawnGroundItem(): {position} is outside the cells of [{region.PrototypeName}]");

            // The loot roller needs a player to roll the item for (it picks a hero from them); any player in the region will do,
            // the item does not belong to them
            Player rollFor = null;
            foreach (Player player in new PlayerIterator(region))
            {
                rollFor = player;
                break;
            }

            if (rollFor == null)
                return Logger.WarnReturn<WorldEntity>(null, $"SpawnGroundItem(): No player in [{region.PrototypeName}] to roll [{itemRef.GetName()}] for");

            ItemSpec itemSpec;
            try
            {
                itemSpec = region.Game.LootManager.CreateItemSpec(itemRef, LootContext.Drop, rollFor);
            }
            catch (Exception e)
            {
                return Logger.WarnReturn<WorldEntity>(null, $"SpawnGroundItem(): Failed to create an item spec for [{itemRef.GetName()}]: {e.Message}");
            }

            if (itemSpec == null)
                return Logger.WarnReturn<WorldEntity>(null, $"SpawnGroundItem(): Failed to create an item spec for [{itemRef.GetName()}]");

            using EntitySettings settings = ObjectPoolManager.Instance.Get<EntitySettings>();
            settings.EntityRef = itemRef;
            settings.Position = floorPosition;
            settings.RegionId = region.Id;
            settings.Cell = cell;
            settings.ItemSpec = itemSpec;

            using PropertyCollection properties = ObjectPoolManager.Instance.Get<PropertyCollection>();
            properties[PropertyEnum.InventoryStackCount] = Math.Max(itemSpec.StackCount, 1);
            settings.Properties = properties;

            if (region.Game.EntityManager.CreateEntity(settings) is not WorldEntity entity)
                return Logger.WarnReturn<WorldEntity>(null, $"SpawnGroundItem(): Failed to create [{itemRef.GetName()}]");

            _tags.AddOrUpdate(entity, tag ?? string.Empty);
            if (canPickUp == false)
                _sceneryItems.AddOrUpdate(entity, null);

            return entity;
        }

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="entity"/> is a ground item placed as scenery
        /// (<see cref="SpawnGroundItem"/> with canPickUp = false), which must not be picked up.
        /// </summary>
        public static bool IsSceneryItem(WorldEntity entity)
        {
            return entity != null && _sceneryItems.TryGetValue(entity, out _);
        }

        /// <summary>
        /// Returns the tag an entity was spawned with by <see cref="SpawnInteractable"/>, or an empty string.
        /// </summary>
        public static string GetTag(WorldEntity entity)
        {
            return entity != null && _tags.TryGetValue(entity, out string tag) ? tag : string.Empty;
        }

        /// <summary>
        /// Finds a random walkable spot anywhere in <paramref name="region"/> (a random cell, then a random spot in it).
        /// With <paramref name="reachableFrom"/>, the spot must be reachable on foot from there (skips closed-off islands).
        /// With <paramref name="minDistanceFrom"/> (> 0, needs <paramref name="reachableFrom"/>), the spot must be at least that far away.
        /// </summary>
        public static bool TryFindRandomSpot(Region region, out Vector3 position, Vector3? reachableFrom = null, float minDistanceFrom = 0f,
            float radius = 48f, int maxCellTries = 40)
        {
            position = Vector3.Zero;
            if (region == null)
                return false;

            using var cellsHandle = ListPool<Cell>.Instance.Get(out List<Cell> cells);
            foreach (Area area in region.IterateAreas())
                cells.AddRange(area.Cells.Values);

            if (cells.Count == 0)
                return false;

            GRandom random = region.Game.Random;
            float minDistanceSq = minDistanceFrom * minDistanceFrom;
            Vector3 from = reachableFrom.HasValue ? RegionLocation.ProjectToFloor(region, reachableFrom.Value) : Vector3.Zero;

            for (int attempt = 0; attempt < maxCellTries; attempt++)
            {
                Cell cell = cells[random.Next(0, cells.Count)];
                Aabb cellBounds = cell.RegionBounds;
                Vector3 center = cellBounds.Center;

                if (reachableFrom.HasValue && minDistanceSq > 0f && Vector3.DistanceSquared2D(center, from) < minDistanceSq)
                    continue;

                Bounds bounds = new();
                bounds.InitializeCapsule(radius, 48.0f, BoundsCollisionType.Blocking, BoundsFlags.None);
                bounds.Center = center;

                float halfSize = Math.Min(cellBounds.Width, cellBounds.Length) * 0.5f;
                if (region.ChooseRandomPositionNearPoint(ref bounds, PathFlags.Walk, PositionCheckFlags.None, BlockingCheckFlags.None,
                    0f, halfSize, out Vector3 candidate, null, null, 16) == false)
                    continue;

                if (TryGetCell(region, candidate, out Vector3 floorPosition, out _) == false)
                    continue;

                if (reachableFrom.HasValue)
                {
                    if (minDistanceSq > 0f && Vector3.DistanceSquared2D(floorPosition, from) < minDistanceSq)
                        continue;

                    if (NaviPath.CheckCanPathTo(region.NaviMesh, from, floorPosition, radius, PathFlags.Walk) != NaviPathResult.Success)
                        continue;
                }

                position = floorPosition;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Finds a walkable spot within <paramref name="maxDistance"/> of <paramref name="near"/> that players can walk to from
        /// <paramref name="reachableFrom"/>. Unlike <see cref="TryFindSpotNear"/> it never falls back to <paramref name="near"/> itself,
        /// so it fails instead of returning lava, a drop or a cut-off ledge.
        /// </summary>
        public static bool TryFindReachableSpotNear(Region region, Vector3 near, float maxDistance, Vector3 reachableFrom, out Vector3 position,
            int tries = 48)
        {
            position = Vector3.Zero;
            if (region?.NaviMesh == null)
                return false;

            Vector3 from = RegionLocation.ProjectToFloor(region, reachableFrom);

            for (int i = 0; i < tries; i++)
            {
                // Grow the search ring so spots near the middle are preferred
                float radius = Math.Max(150f, maxDistance * (i + 1) / tries);

                Bounds bounds = new();
                bounds.InitializeCapsule(48f, 48f, BoundsCollisionType.Blocking, BoundsFlags.None);
                bounds.Center = RegionLocation.ProjectToFloor(region, near);

                if (region.ChooseRandomPositionNearPoint(ref bounds, PathFlags.Walk, PositionCheckFlags.None, BlockingCheckFlags.None,
                    0f, radius, out Vector3 candidate, null, null, 16) == false)
                    continue;

                if (TryGetCell(region, candidate, out Vector3 floor, out _) == false)
                    continue;

                if (NaviPath.CheckCanPathTo(region.NaviMesh, from, floor, 48f, PathFlags.Walk) != NaviPathResult.Success)
                    continue;

                position = floor;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Finds a walkable spot reachable from <paramref name="from"/> that is as far from it as possible (out of
        /// <paramref name="samples"/> random spots). Good for a boss at the far end of a random map.
        /// </summary>
        public static bool TryFindFarSpot(Region region, Vector3 from, out Vector3 position, int samples = 24)
        {
            position = Vector3.Zero;
            float bestDistanceSq = -1f;

            for (int i = 0; i < samples; i++)
            {
                if (TryFindRandomSpot(region, out Vector3 candidate, from, 0f, 48f, 8) == false)
                    continue;

                float distanceSq = Vector3.DistanceSquared2D(candidate, from);
                if (distanceSq <= bestDistanceSq)
                    continue;

                bestDistanceSq = distanceSq;
                position = candidate;
            }

            return bestDistanceSq >= 0f;
        }

        /// <summary>
        /// Returns where players arrive in <paramref name="region"/> (its start target), for placing NPCs and measuring distances.
        /// </summary>
        public static bool TryGetStartPosition(Region region, out Vector3 position)
        {
            position = Vector3.Zero;

            RegionConnectionTargetPrototype targetProto = region?.Prototype?.StartTarget.As<RegionConnectionTargetPrototype>();
            if (targetProto == null)
                return false;

            Orientation orientation = Orientation.Zero;
            PrototypeId cellRef = GameDatabase.GetDataRefByAsset(targetProto.Cell);
            return region.FindTargetLocation(ref position, ref orientation, targetProto.Area, cellRef, targetProto.Entity);
        }

        /// <summary>
        /// Gives <paramref name="agent"/> up to <paramref name="count"/> random enemy affixes, skipping names containing any of the
        /// comma-separated words in <paramref name="excludeNameContains"/> and affixes that knock players around. Returns the number added.
        /// </summary>
        public static int AddRandomAffixes(Agent agent, int count, string excludeNameContains = "")
        {
            if (agent == null || count <= 0)
                return 0;

            List<PrototypeId> affixes = new();
            EnemyBoostCatalog.AddRandom(affixes, agent.Game.Random, count, excludeNameContains, false);

            foreach (PrototypeId affix in affixes)
                agent.Properties[PropertyEnum.EnemyBoost, affix] = true;

            return affixes.Count;
        }

        /// <summary>
        /// Removes a spawned entity from the world if it still exists.
        /// </summary>
        public static void Despawn(Game game, ulong entityId)
        {
            WorldEntity entity = game?.EntityManager.GetEntity<WorldEntity>(entityId);
            if (entity != null && entity.IsDestroyed == false)
                entity.Destroy();
        }

        /// <summary>
        /// Returns <see langword="true"/> if the entity exists, is in the world and is not dead.
        /// </summary>
        public static bool IsAlive(Game game, ulong entityId)
        {
            WorldEntity entity = game?.EntityManager.GetEntity<WorldEntity>(entityId);
            return entity != null && entity.IsAliveInWorld;
        }

        #region Helpers

        private static PrototypeId FindAgentUncached(string pathOrNames, string preferPathContains)
        {
            if (pathOrNames.Contains(".prototype", StringComparison.OrdinalIgnoreCase))
            {
                PrototypeId exactRef = GameDatabase.GetPrototypeRefByName(pathOrNames);
                if (exactRef != PrototypeId.Invalid && exactRef.As<AgentPrototype>() != null)
                    return exactRef;

                return Logger.WarnReturn(PrototypeId.Invalid, $"FindAgent(): [{pathOrNames}] is not an agent prototype");
            }

            string[] names = SplitWords(pathOrNames).Select(Normalize).Where(name => name.Length > 0).ToArray();
            string[] preferWords = SplitWords(preferPathContains);

            PrototypeId bestRef = PrototypeId.Invalid;
            int bestScore = int.MinValue;

            foreach (PrototypeId agentRef in DataDirectory.Instance.IteratePrototypesInHierarchy<AgentPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
            {
                string path = agentRef.GetName()?.Replace('\\', '/');
                if (string.IsNullOrEmpty(path) || path.StartsWith(BossPathPrefix, StringComparison.OrdinalIgnoreCase) == false)
                    continue;

                if (ExcludedBossPathFragments.Any(fragment => path.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                    continue;

                string normalizedPath = Normalize(path);
                int nameIndex = Array.FindIndex(names, name => normalizedPath.Contains(name));
                if (nameIndex < 0)
                    continue;

                AgentPrototype agentProto = agentRef.As<AgentPrototype>();
                Rank rank = agentProto?.RankPrototype?.Rank ?? Rank.Max;

                // Earlier names win, real bosses beat minibosses, preferred words beat the rest, shorter (base) paths beat variants
                int score = -nameIndex * 10000;
                if (rank == Rank.Boss || rank == Rank.GroupBoss) score += 5000;
                else if (rank == Rank.MiniBoss) score += 1000;
                if (preferWords.Any(word => path.Contains(word, StringComparison.OrdinalIgnoreCase))) score += 2000;
                if (Normalize(Path.GetFileNameWithoutExtension(path)).Contains(names[nameIndex])) score += 500;
                score -= path.Length;

                if (score <= bestScore)
                    continue;

                bestScore = score;
                bestRef = agentRef;
            }

            if (bestRef == PrototypeId.Invalid)
                return Logger.WarnReturn(PrototypeId.Invalid, $"FindAgent(): No boss prototype matches [{pathOrNames}]");

            Logger.Info($"FindAgent(): [{pathOrNames}] => {bestRef.GetName()}");
            return bestRef;
        }

        private static string[] SplitWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Array.Empty<string>();

            return text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static string Normalize(string text)
        {
            return new string(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private static bool TryFindSpawnPosition(Region region, AgentPrototype agentProto, Vector3 near, float minDistance, float maxDistance,
            out Vector3 position, out Cell cell, bool ignoreCrowds = false)
        {
            position = Vector3.Zero;
            cell = null;

            // agentProto null = a generic character-sized spot
            string name = agentProto != null ? agentProto.DataRef.GetName() : "spot";

            PathFlags pathFlags = agentProto != null ? Region.GetPathFlagsForEntity(agentProto) : PathFlags.Walk;
            if (pathFlags == PathFlags.None)
                pathFlags = PathFlags.Walk;

            Bounds bounds = agentProto?.Bounds != null ? new Bounds(agentProto.Bounds, near) : new Bounds();
            if (bounds.Geometry == GeometryType.None || bounds.Radius < 16.0f)
                bounds.InitializeCapsule(24.0f, 48.0f, BoundsCollisionType.Blocking, BoundsFlags.None);
            bounds.Center = RegionLocation.ProjectToFloor(region, near);
            bounds.CollisionType = BoundsCollisionType.Blocking;

            // Crowds (players, summons, team-ups) block the strict check, so each pass relaxes it further:
            //  1. clear spot in the ring
            //  2. characters no longer block, ring twice as wide (the spot must still be walkable and reachable)
            //  3. walkable ground only, ring three times as wide, more tries
            const PositionCheckFlags Strict = PositionCheckFlags.CanBeBlockedEntity | PositionCheckFlags.CanPathTo | PositionCheckFlags.PreferNoEntity | PositionCheckFlags.InRadius;
            const PositionCheckFlags IgnoreEntities = PositionCheckFlags.CanPathTo | PositionCheckFlags.InRadius;
            const BlockingCheckFlags StrictBlocking = BlockingCheckFlags.CheckSpawns | BlockingCheckFlags.CheckGroundMovementPowers | BlockingCheckFlags.CheckLanding;

            // ignoreCrowds: characters never block, and the ring never widens (the spawn stays at the requested spot)
            var passes = ignoreCrowds
                ? new (PositionCheckFlags PosFlags, BlockingCheckFlags BlockFlags, float RangeMult, int Tests)[]
                {
                    (IgnoreEntities,               BlockingCheckFlags.CheckLanding, 1f, 128),
                    (PositionCheckFlags.InRadius,  BlockingCheckFlags.None,         1f, 256),
                }
                : new (PositionCheckFlags PosFlags, BlockingCheckFlags BlockFlags, float RangeMult, int Tests)[]
                {
                    (Strict,                       StrictBlocking,                  1f, 64),
                    (IgnoreEntities,               BlockingCheckFlags.CheckLanding, 2f, 128),
                    (PositionCheckFlags.InRadius,  BlockingCheckFlags.None,         3f, 256),
                };

            for (int pass = 0; pass < passes.Length; pass++)
            {
                var (posFlags, blockFlags, rangeMult, tests) = passes[pass];
                bounds.Center = RegionLocation.ProjectToFloor(region, near);

                if (region.ChooseRandomPositionNearPoint(ref bounds, pathFlags, posFlags, blockFlags, minDistance, maxDistance * rangeMult,
                    out Vector3 candidate, null, null, tests) == false)
                    continue;

                if (TryGetCell(region, candidate, out position, out cell))
                {
                    if (pass > 0)
                        Logger.Info($"TryFindSpawnPosition(): [{name}] placed on relaxed pass {pass + 1} (crowded area)");
                    return true;
                }
            }

            // 4. Last resort: the anchor itself (a player standing there means it is valid ground); overlapping agents push apart
            if (TryGetCell(region, near, out position, out cell))
            {
                Logger.Info($"TryFindSpawnPosition(): [{name}] placed at the anchor (no free spot around it)");
                return true;
            }

            return false;
        }

        private static bool TryGetCell(Region region, Vector3 candidate, out Vector3 position, out Cell cell)
        {
            position = RegionLocation.ProjectToFloor(region, candidate);
            cell = region.GetCellAtPosition(position);
            return cell != null;
        }

        private static PrototypeId ResolveFriendlyAlliance(AgentPrototype agentProto)
        {
            if (agentProto.Alliance != PrototypeId.Invalid && AlliancePrototype.IsHostileToPlayerAlliance(agentProto.AlliancePrototype) == false)
                return agentProto.Alliance;

            return GameDatabase.GlobalsPrototype.PlayerAlliance;
        }

        private static PrototypeId ResolveHostileAlliance(AgentPrototype agentProto)
        {
            if (AlliancePrototype.IsHostileToPlayerAlliance(agentProto.AlliancePrototype) && agentProto.Alliance != PrototypeId.Invalid)
                return agentProto.Alliance;

            return GameDatabase.GlobalsPrototype.AnyHostileAlliancePrototype;
        }

        private static void PrimeCombat(Agent agent, Avatar target)
        {
            AIController aiController = agent.AIController;
            PropertyCollection blackboard = aiController?.Blackboard?.PropertyCollection;
            if (blackboard == null)
                return;

            long now = (long)agent.Game.CurrentTime.TotalMilliseconds;
            foreach (PropertyCollection collection in new[] { blackboard, agent.Properties })
            {
                collection[PropertyEnum.AIAggroRangeOverrideHostile] = AggroRangeHostile;
                collection[PropertyEnum.AIAggroRangeOverrideAlly] = AggroRangeAlly;
                collection[PropertyEnum.AIAlwaysAggroed] = true;
                collection[PropertyEnum.AIAggroState] = true;
                collection[PropertyEnum.AIAggroTime] = now;
                collection[PropertyEnum.AIStartsEnabled] = true;
                collection.RemoveProperty(PropertyEnum.AINextSensoryUpdate);
                collection.RemoveProperty(PropertyEnum.AINextHostileSense);
            }

            if (target != null && target.IsAliveInWorld && target.Region == agent.Region)
            {
                aiController.SetTargetEntity(target);
                blackboard[PropertyEnum.AIPendingTargetId] = target.Id;
                aiController.Senses.Interrupt |= BehaviorInterruptType.Alerted | BehaviorInterruptType.TargetSighted | BehaviorInterruptType.Override;
            }

            aiController.SetIsEnabled(true);
            aiController.ScheduleAIThinkEvent(TimeSpan.Zero, useGlobalThinkVariance: false, ignoreActivePower: true);
        }

        private static void PlaySpawnVisual(WorldEntity entity)
        {
            if (entity.IsInWorld == false)
                return;

            AssetId visual = GameDatabase.PowerVisualsGlobalsPrototype?.AvatarLeashTeleportClass ?? AssetId.Invalid;
            if (visual == AssetId.Invalid)
                return;

            NetMessagePlayPowerVisuals message = NetMessagePlayPowerVisuals.CreateBuilder()
                .SetEntityId(entity.Id)
                .SetPowerAssetRef((ulong)visual)
                .Build();

            entity.Game.NetworkManager.SendMessageToInterested(message, entity, AOINetworkPolicyValues.AOIChannelProximity);
        }

        #endregion
    }
}
