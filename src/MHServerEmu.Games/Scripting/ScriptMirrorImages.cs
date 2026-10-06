using System.Runtime.CompilerServices;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.PowerCollections;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Powers;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// How a mirror image looks and what it copies from its owner. Scripts create one, change what they need and pass it to
    /// <see cref="ScriptMirrorImages.Spawn"/>. How it fights is in <see cref="AI"/>.
    /// </summary>
    public sealed class MirrorImageOptions
    {
        /// <summary>Seconds before the image vanishes (0 = until its owner leaves the region).</summary>
        public float LifespanSeconds = 60f;

        /// <summary>Name shown above the image (empty = none).</summary>
        public string Name = "";

        /// <summary>Multiplies every hit the image deals (5 = five times the damage). 1 = the powers' own damage at the owner's level, without the owner's gear.</summary>
        public float DamageScale = 1f;

        /// <summary>How many of the owner's activated powers the image gets (0 = all of them).</summary>
        public int MaxPowers = 0;

        /// <summary>The image gets the talents the owner has switched on.</summary>
        public bool CopyTalents = true;

        /// <summary>The image gets the owner's traits (passive powers), at the owner's ranks.</summary>
        public bool CopyTraits = true;

        /// <summary>
        /// Writes to the server log: at spawn, the image's powers and every damage-related stat it has; then for every hit,
        /// how the damage was calculated step by step.
        /// </summary>
        public bool LogDamage = false;

        /// <summary>The agent that really exists on the server (empty = the default hero-sized body).</summary>
        public string BodyPath = "";

        /// <summary>How the image fights: ranges, movement speed, which kinds of powers it uses (see <see cref="CombatAIOptions"/>).</summary>
        public CombatAIOptions AI = new();
    }

    /// <summary>
    /// EXPERIMENTAL. Mirror images: allies that every client draws as a player's own hero and costume, and that fight with that
    /// hero's own powers (so the client has the animations for them). Call from the game thread that owns the avatar.
    /// </summary>
    /// <remarks>
    /// An image is a "body" agent on the server (see <see cref="ScriptSpawner.SpawnMirrorImage"/>) that is given its owner's
    /// powers, talents and traits and a <see cref="ScriptCombatAI"/> brain with the owner as its leader.
    /// </remarks>
    public static class ScriptMirrorImages
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        // The body: a hero-sized enemy clone with ordinary movement (the same one the Incursion mod uses)
        private const string DefaultBodyPath = "Entity/Characters/Mobs/SpiderClones/SpidermanCloneSuperiorBase.prototype";

        // Images per owner, and each image's settings (entries vanish with the entity)
        private static readonly ConditionalWeakTable<Avatar, List<ulong>> _images = new();
        private static readonly ConditionalWeakTable<WorldEntity, MirrorImageOptions> _imageOptions = new();

        // Images alive on the whole server: while there are none, the damage calculation skips the image lookup entirely
        private static int _liveImages;

        // Avatars that get an image automatically when they hit an enemy (entries vanish with the avatar), and how many
        // there are on the whole server: while there are none, hits skip the lookup entirely
        private static readonly ConditionalWeakTable<Avatar, AutoSpawn> _autoSpawns = new();
        private static int _autoSpawnCount;

        private sealed class AutoSpawn
        {
            public MirrorImageOptions Options;
            public float CooldownSeconds;
            public TimeSpan NextTime;       // game time the next image is allowed
        }

        /// <summary>
        /// Spawns a mirror image of <paramref name="owner"/> next to them (<paramref name="options"/> = null for the defaults).
        /// Returns <see langword="null"/> on failure.
        /// </summary>
        public static Agent Spawn(Avatar owner, MirrorImageOptions options = null)
        {
            if (owner == null || owner.IsInWorld == false)
                return null;

            options ??= new();
            options.AI ??= new();

            PrototypeId bodyRef = GameDatabase.GetPrototypeRefByName(string.IsNullOrEmpty(options.BodyPath) ? DefaultBodyPath : options.BodyPath);
            Agent agent = ScriptSpawner.SpawnMirrorImage(owner, bodyRef, options.LifespanSeconds, options.Name);
            if (agent == null)
                return null;

            agent.Properties[PropertyEnum.Untargetable] = false;
            agent.Properties[PropertyEnum.Unaffectable] = false;
            agent.Properties[PropertyEnum.Invulnerable] = false;
            agent.Properties[PropertyEnum.NoEntityCollide] = true;   // never blocks its owner or other players


            _imageOptions.AddOrUpdate(agent, options);

            List<PrototypeId> powers = CopyPowers(agent, owner, options);

            if (options.LogDamage)
                LogStats(agent, owner);

            _images.GetOrCreateValue(owner).Add(agent.Id);

            // The brain is the only thing driving it: it follows the owner and fights around them
            Interlocked.Increment(ref _liveImages);
            if (ScriptCombatAI.Attach(agent, owner, options.AI, powers, static () => Interlocked.Decrement(ref _liveImages)) == false)
                Interlocked.Decrement(ref _liveImages);

            return agent;
        }

        /// <summary>
        /// From now on <paramref name="owner"/> gets a mirror image automatically when they hit an enemy: one at a time, and
        /// the next one no sooner than <paramref name="cooldownSeconds"/> after the previous one's lifespan is over. Stays on
        /// until <see cref="DisableAutoSpawn"/> or until the avatar is gone (a hero swap or region change makes a new avatar:
        /// enable it again from <see cref="ScriptHooks.EquipmentChanged"/> or <see cref="ScriptHooks.AvatarEnteredWorld"/>).
        /// </summary>
        public static void EnableAutoSpawn(Avatar owner, MirrorImageOptions options, float cooldownSeconds)
        {
            if (owner == null)
                return;

            options ??= new();

            if (_autoSpawns.TryGetValue(owner, out AutoSpawn autoSpawn))
            {
                // Already on: new settings, the running cooldown stays
                autoSpawn.Options = options;
                autoSpawn.CooldownSeconds = cooldownSeconds;
                return;
            }

            _autoSpawns.Add(owner, new AutoSpawn { Options = options, CooldownSeconds = cooldownSeconds });
            Interlocked.Increment(ref _autoSpawnCount);
        }

        /// <summary>
        /// Stops the automatic mirror image of <paramref name="owner"/> (see <see cref="EnableAutoSpawn"/>). An image that is
        /// already out stays until its time is up.
        /// </summary>
        public static void DisableAutoSpawn(Avatar owner)
        {
            if (owner != null && _autoSpawns.Remove(owner))
                Interlocked.Decrement(ref _autoSpawnCount);
        }

        /// <summary>
        /// Called by the damage code whenever <paramref name="attacker"/> (a player's hero, or something acting for them)
        /// damages <paramref name="target"/>. Spawns the attacker's automatic mirror image if one is due.
        /// </summary>
        internal static void OnAvatarHitEnemy(Avatar attacker, WorldEntity target)
        {
            if (Volatile.Read(ref _autoSpawnCount) <= 0)
                return;

            if (attacker == null || attacker.IsInWorld == false || attacker.IsDead || _autoSpawns.TryGetValue(attacker, out AutoSpawn autoSpawn) == false)
                return;

            Game game = attacker.Game;
            TimeSpan now = game.CurrentTime;
            if (now < autoSpawn.NextTime)
                return;

            // A real enemy: not a barrel, not the hero's own image
            if (target is not Agent || target.IsDestructible || target == attacker || attacker.IsHostileTo(target) == false)
                return;

            if (Count(attacker) > 0)
                return;

            // Claim the slot now, spawn right after the current hit is done being processed
            MirrorImageOptions options = autoSpawn.Options;
            autoSpawn.NextTime = now + TimeSpan.FromSeconds(Math.Max(options.LifespanSeconds, 0f) + Math.Max(autoSpawn.CooldownSeconds, 1f));

            ulong attackerId = attacker.Id;
            ScriptTimer.After(game, 0f, () =>
            {
                Avatar owner = game.EntityManager.GetEntity<Avatar>(attackerId);
                if (owner != null && owner.IsInWorld && owner.IsDead == false)
                    Spawn(owner, options);
            });
        }

        /// <summary>
        /// Returns how many mirror images <paramref name="owner"/> has in the world.
        /// </summary>
        public static int Count(Avatar owner)
        {
            if (owner == null || _images.TryGetValue(owner, out List<ulong> ids) == false)
                return 0;

            ids.RemoveAll(id => ScriptSpawner.IsAlive(owner.Game, id) == false);
            return ids.Count;
        }

        /// <summary>
        /// Removes every mirror image of <paramref name="owner"/>. Returns how many were removed.
        /// </summary>
        public static int Clear(Avatar owner)
        {
            int count = Count(owner);
            if (count == 0)
                return 0;

            List<ulong> ids = _images.GetOrCreateValue(owner);
            foreach (ulong id in ids)
                ScriptSpawner.Despawn(owner.Game, id);

            ids.Clear();
            return count;
        }

        /// <summary>
        /// The settings of the mirror image behind a hit made by <paramref name="powerOwnerId"/> (the image itself, or
        /// something it created), or <see langword="null"/> for every other hit. Called by the damage calculation for every
        /// hit in the game, so it returns at once while no image is alive.
        /// </summary>
        internal static MirrorImageOptions FindOptionsForHit(Game game, ulong powerOwnerId)
        {
            if (Volatile.Read(ref _liveImages) <= 0)
                return null;

            return FindOptions(game, powerOwnerId);
        }

        // The settings of the mirror image behind a power user: the image itself, or something it created (a hotspot, a missile)
        private static MirrorImageOptions FindOptions(Game game, ulong powerOwnerId)
        {
            WorldEntity entity = game?.EntityManager.GetEntity<WorldEntity>(powerOwnerId);

            // One step up is enough: an image acts for its owner, who is never an image
            for (int i = 0; i < 2 && entity != null; i++)
            {
                if (_imageOptions.TryGetValue(entity, out MirrorImageOptions options))
                    return options;

                ulong creatorId = entity.Properties[PropertyEnum.PowerUserOverrideID];
                entity = creatorId != Entity.InvalidId && creatorId != entity.Id ? game.EntityManager.GetEntity<WorldEntity>(creatorId) : null;
            }

            return null;
        }

        #region Copying the owner's kit

        // Gives the image its owner's talents, traits and activated powers. Returns the powers its brain may use.
        private static List<PrototypeId> CopyPowers(Agent agent, Avatar owner, MirrorImageOptions options)
        {
            List<PrototypeId> powers = new();

            AvatarPrototype avatarProto = owner.AvatarPrototype;
            if (avatarProto == null)
                return powers;

            List<PowerProgressionEntryPrototype> entries = new();
            avatarProto.GetPowersUnlockedAtLevel(entries, owner.CharacterLevel, true);

            // The owner's talent choices first, then their traits (passives): powers assigned afterwards pick up their effects
            if (options.CopyTalents)
                CopyTalents(agent, owner);

            if (options.CopyTraits)
            {
                foreach (PowerProgressionEntryPrototype entry in entries)
                {
                    PrototypeId powerRef = entry?.PowerAssignment?.Ability ?? PrototypeId.Invalid;
                    PowerPrototype powerProto = powerRef.As<PowerPrototype>();
                    if (powerProto == null || powerProto.Activation != PowerActivationType.Passive || powerProto.PowerCategory != PowerCategoryType.NormalPower)
                        continue;

                    // Only the traits the owner really has, at the owner's rank
                    Power ownerPower = owner.GetPower(powerRef);
                    if (ownerPower != null)
                        Assign(agent, powerRef, Math.Max(ownerPower.Rank, 1));
                }
            }

            // Every activated power the owner has unlocked: attacks, buffs, summons, movement powers, signature, ultimate.
            // The brain decides which of them it uses and when.
            foreach (PowerProgressionEntryPrototype entry in entries)
            {
                if (options.MaxPowers > 0 && powers.Count >= options.MaxPowers)
                    break;

                PrototypeId powerRef = entry?.PowerAssignment?.Ability ?? PrototypeId.Invalid;
                if (powerRef == PrototypeId.Invalid)
                    continue;

                // The rank the owner has in the power (rank 1 if they do not have it assigned)
                int rank = Math.Max(owner.GetPower(powerRef)?.Rank ?? 1, 1);

                // Some talents replace a power with another version of it: use the version the owner has right now
                if (options.CopyTalents)
                {
                    PrototypeId mappedRef = owner.GetMappedPowerFromOriginalPower(powerRef);
                    if (mappedRef != PrototypeId.Invalid)
                        powerRef = mappedRef;
                }

                PowerPrototype powerProto = powerRef.As<PowerPrototype>();
                if (powerProto == null || powers.Contains(powerRef) || powerProto.PowerCategory != PowerCategoryType.NormalPower ||
                    powerProto.Activation == PowerActivationType.Passive || powerProto.IsTravelPower)
                    continue;

                if (Assign(agent, powerRef, Math.Max(owner.GetPower(powerRef)?.Rank ?? rank, 1)) != null)
                    powers.Add(powerRef);
            }

            if (powers.Count == 0)
                Logger.Warn($"CopyPowers(): no usable powers found for [{avatarProto}]");

            return powers;
        }

        private static Power Assign(Agent agent, PrototypeId powerRef, int rank)
        {
            Power power = agent.GetPower(powerRef);
            if (power != null)
                return power;

            PowerIndexProperties indexProps = new(rank, agent.CharacterLevel, agent.CombatLevel);
            return agent.AssignPower(powerRef, indexProps);
        }

        // Gives the image the talents the owner has switched on in their current spec
        private static void CopyTalents(Agent agent, Avatar owner)
        {
            int specIndex = owner.GetPowerSpecIndexActive();
            agent.Properties[PropertyEnum.PowerSpecIndexActive] = specIndex;

            List<PrototypeId> talents = new();
            owner.GetTalentPowersForSpec(specIndex, talents);

            foreach (PrototypeId talentRef in talents)
            {
                if (talentRef.As<SpecializationPowerPrototype>() == null)
                    continue;

                // Same steps as Avatar.AssignTalentPower(): talents are always rank 1
                Power talent = Assign(agent, talentRef, 1);
                if (talent == null)
                    continue;

                talent.HandleTriggerPowerEventOnSpecializationPowerAssigned();
                agent.Properties[PropertyEnum.AvatarSpecializationPower, specIndex, talentRef] = true;
            }
        }

        #endregion

        // Writes the image's powers and its damage-related stats to the log (MirrorImageOptions.LogDamage)
        private static void LogStats(Agent agent, Avatar owner)
        {
            System.Text.StringBuilder sb = new();
            sb.Append($"[MirrorImage] spawned for [{owner}]: body [{agent.PrototypeName}], level {agent.CharacterLevel}, rank [{agent.GetRankPrototype()}]");

            sb.Append("\n  Powers:");
            if (agent.PowerCollection != null)
            {
                foreach (var kvp in agent.PowerCollection)
                {
                    Power power = kvp.Value.Power;
                    if (power != null)
                        sb.Append($"\n    {power.Prototype} (rank {power.Rank}, {power.Prototype.Activation})");
                }
            }

            sb.Append("\n  Damage-related stats on the image:");
            sb.Append(DescribeDamageProperties(agent.Properties, "\n    "));

            Logger.Info(sb.ToString());
        }

        /// <summary>
        /// Lists the damage-related properties in <paramref name="properties"/> (bonuses, multipliers, ratings, crit; not the
        /// damage amounts themselves), each on its own line starting with <paramref name="linePrefix"/>. For the damage log.
        /// </summary>
        internal static string DescribeDamageProperties(PropertyCollection properties, string linePrefix)
        {
            System.Text.StringBuilder sb = new();
            if (properties == null)
                return string.Empty;

            foreach (var kvp in properties)
            {
                PropertyEnum propertyEnum = kvp.Key.Enum;
                if (propertyEnum == PropertyEnum.Damage || propertyEnum == PropertyEnum.DamageBase || propertyEnum == PropertyEnum.DamageBaseUnmodified)
                    continue;

                string name = propertyEnum.ToString();
                if (name.Contains("Damage") == false && name.Contains("Crit") == false && propertyEnum != PropertyEnum.Rank)
                    continue;

                PropertyInfo info = GameDatabase.PropertyInfoTable.LookupPropertyInfo(propertyEnum);
                sb.Append($"{linePrefix}{kvp.Key} = {kvp.Value.Print(info.DataType)}");
            }

            return sb.ToString();
        }
    }
}
