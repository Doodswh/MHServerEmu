using Gazillion;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Regions;
using MHServerEmu.Games.Social.Parties;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Sends players between regions for script-driven events. Call from the game thread that owns the player.
    /// </summary>
    public static class ScriptTeleport
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        /// <summary>
        /// Resolves a region by full prototype path (Regions/.../X.prototype) or by its file name (X, e.g. "T1L1BambooRegion").
        /// </summary>
        public static PrototypeId FindRegion(string pathOrName)
        {
            if (string.IsNullOrWhiteSpace(pathOrName))
                return PrototypeId.Invalid;

            if (pathOrName.Contains(".prototype", StringComparison.OrdinalIgnoreCase))
            {
                PrototypeId exactRef = GameDatabase.GetPrototypeRefByName(pathOrName);
                return exactRef.As<RegionPrototype>() != null ? exactRef : PrototypeId.Invalid;
            }

            string fileName = pathOrName + ".prototype";
            foreach (PrototypeId regionRef in GameDatabase.DataDirectory.IteratePrototypesInHierarchy<RegionPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
            {
                string name = GameDatabase.GetPrototypeName(regionRef);
                if (name != null && name.EndsWith("/" + fileName, StringComparison.OrdinalIgnoreCase))
                    return regionRef;
            }

            return PrototypeId.Invalid;
        }

        /// <summary>
        /// Sends <paramref name="player"/> to the entrance of <paramref name="regionPathOrName"/>. <paramref name="difficultyPath"/>
        /// (e.g. "Difficulty/Tiers/Tier3Superheroic.prototype") sets the difficulty, overriding the player's and party's difficulty
        /// settings; the region's allowed range still applies. Without it the normal difficulty rules pick one.
        /// </summary>
        /// <remarks>
        /// Private regions (terminals, instances) normally reuse the player's / party's existing instance. A non-zero
        /// <paramref name="seed"/> asks for the instance with that layout seed: a new one is created unless the player's party
        /// already has it. Use a fresh random seed for a guaranteed fresh instance, and the same seed for the whole party so
        /// they land together. The new region's <c>RandomSeed</c> (and <see cref="RegionGeneratingArgs.Seed"/>) is that seed.
        /// </remarks>
        public static bool ToRegion(Player player, string regionPathOrName, string difficultyPath = null, int seed = 0)
        {
            return Teleport(player, regionPathOrName, difficultyPath, seed, false);
        }

        /// <summary>
        /// Sends <paramref name="player"/> into a script-built map (a region whose RegionGenerating handler calls BuildLayout).
        /// Like <see cref="ToRegion"/>, but the region needs no entrance (players arrive in the map's S room) and the difficulty is
        /// used as given, even if the region's own data doesn't list it (test / unused regions usually allow only one).
        /// </summary>
        public static bool ToBuiltMap(Player player, string regionPathOrName, string difficultyPath, int seed)
        {
            return Teleport(player, regionPathOrName, difficultyPath, seed, true);
        }

        private static bool Teleport(Player player, string regionPathOrName, string difficultyPath, int seed, bool builtMap)
        {
            if (player?.CurrentAvatar == null)
                return false;

            PrototypeId regionRef = FindRegion(regionPathOrName);
            RegionPrototype regionProto = regionRef.As<RegionPrototype>();
            if (regionProto == null)
                return Logger.WarnReturn(false, $"ToRegion(): No region [{regionPathOrName}]");

            if (regionProto.StartTarget == PrototypeId.Invalid && builtMap == false)
                return Logger.WarnReturn(false, $"ToRegion(): Region [{regionRef.GetName()}] has no entrance");

            using Teleporter teleporter = ObjectPoolManager.Instance.Get<Teleporter>();
            teleporter.Initialize(player, TeleportContextEnum.TeleportContext_Transition);
            teleporter.Seed = seed;

            if (string.IsNullOrEmpty(difficultyPath) == false)
            {
                PrototypeId difficultyRef = GameDatabase.GetPrototypeRefByName(difficultyPath);
                if (difficultyRef.As<DifficultyTierPrototype>() != null)
                {
                    teleporter.DifficultyTierRef = builtMap ? difficultyRef : RegionPrototype.ConstrainDifficulty(regionRef, difficultyRef);
                    teleporter.ForceDifficultyTier = true;
                    if (teleporter.DifficultyTierRef != difficultyRef)
                        Logger.Warn($"ToRegion(): [{regionRef.GetName()}] does not allow [{difficultyPath}], using [{teleporter.DifficultyTierRef.GetName()}]");
                }
                else
                    Logger.Warn($"ToRegion(): [{difficultyPath}] is not a difficulty tier, using the default");
            }

            // A built map has no entrance marker: target the region itself, the map's S room is used on arrival
            if (regionProto.StartTarget == PrototypeId.Invalid)
                return teleporter.TeleportToTarget(regionRef, PrototypeId.Invalid, PrototypeId.Invalid, PrototypeId.Invalid);

            return teleporter.TeleportToTarget(regionProto.StartTarget);
        }

        /// <summary>
        /// The script-built map of <paramref name="region"/> (null if it has none): room positions by character, arrival point...
        /// </summary>
        public static ScriptedLayout GetBuiltMap(Region region)
        {
            return region?.GenerationOverrides?.Layout;
        }

        /// <summary>
        /// Sends <paramref name="player"/> and every party member standing in the same region to <paramref name="regionPathOrName"/>.
        /// Returns the number of players sent.
        /// </summary>
        public static int ToRegionWithParty(Player player, string regionPathOrName, string difficultyPath = null, int seed = 0)
        {
            int sent = 0;
            foreach (Player member in GetPartyMembersInRegion(player))
            {
                if (ToRegion(member, regionPathOrName, difficultyPath, seed))
                    sent++;
            }
            return sent;
        }

        /// <summary>
        /// Sends <paramref name="player"/> back to their last town (hub).
        /// </summary>
        public static bool ToTown(Player player)
        {
            if (player?.CurrentAvatar == null)
                return false;

            using Teleporter teleporter = ObjectPoolManager.Instance.Get<Teleporter>();
            teleporter.Initialize(player, TeleportContextEnum.TeleportContext_Transition);
            return teleporter.TeleportToLastTown();
        }

        /// <summary>
        /// Returns <paramref name="player"/> plus their party members who are in the same region (the player alone without a party).
        /// </summary>
        public static List<Player> GetPartyMembersInRegion(Player player)
        {
            List<Player> players = new();
            if (player == null)
                return players;

            players.Add(player);

            Region region = player.GetRegion();
            Party party = player.GetParty();
            if (region == null || party == null)
                return players;

            foreach (var kvp in party)
            {
                if (kvp.Key == player.DatabaseUniqueId)
                    continue;

                Player member = player.Game.EntityManager.GetEntityByDbGuid<Player>(kvp.Key);
                if (member != null && member.GetRegion() == region)
                    players.Add(member);
            }

            return players;
        }
    }
}
