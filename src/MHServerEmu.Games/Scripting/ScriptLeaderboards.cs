using System.Collections.Concurrent;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Leaderboards;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Lets scripts run their own leaderboards on top of the game's existing ones (the client can only show leaderboards it has data
    /// for, so a custom leaderboard is an existing one that is renamed and fed by a script).
    /// </summary>
    /// <remarks>
    /// The reused leaderboard keeps everything from its game data: reset cycle, rewards, display (number or time) and ranking order.
    /// How submissions combine depends on its scoring rule: kill / collect style rules add up, completion time rules keep the lowest.
    /// The leaderboard must be enabled in Data/Leaderboards/LeaderboardSchedule.json (!leaderboards reloadschedule).
    /// </remarks>
    public static class ScriptLeaderboards
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        // Leaderboards fed only by scripts: their own game-data scoring rules no longer count anything
        private static readonly ConcurrentDictionary<PrototypeId, byte> _scriptOwned = new();

        /// <summary>
        /// Makes the leaderboard script-only: its original scoring rules (e.g. Daredevil killing Hand ninjas) stop counting, while
        /// <see cref="Submit"/> keeps working. Call it at script load.
        /// </summary>
        public static bool TakeOver(string leaderboardPath)
        {
            LeaderboardPrototype leaderboardProto = Resolve(leaderboardPath);
            if (leaderboardProto == null)
                return false;

            _scriptOwned[leaderboardProto.DataRef] = 0;
            return true;
        }

        /// <summary>
        /// Returns <see langword="true"/> if a script took the leaderboard over (<see cref="TakeOver"/>).
        /// </summary>
        public static bool IsScriptOwned(LeaderboardPrototype leaderboardProto)
        {
            return leaderboardProto != null && _scriptOwned.ContainsKey(leaderboardProto.DataRef);
        }

        /// <summary>
        /// Submits <paramref name="value"/> for <paramref name="player"/> to the leaderboard at <paramref name="leaderboardPath"/>
        /// (e.g. "Leaderboards/Prototypes/Leaderboards/Events/DaredevilVsHand.prototype"), through its scoring rule
        /// <paramref name="ruleIndex"/>. Time leaderboards expect milliseconds. Returns <see langword="false"/> if the leaderboard
        /// or rule doesn't exist or isn't running.
        /// </summary>
        public static bool Submit(Player player, string leaderboardPath, long value, int ruleIndex = 0)
        {
            if (player?.LeaderboardManager == null || value <= 0)
                return false;

            LeaderboardPrototype leaderboardProto = Resolve(leaderboardPath);
            if (leaderboardProto == null)
                return false;

            if (leaderboardProto.ScoringRules == null || ruleIndex < 0 || ruleIndex >= leaderboardProto.ScoringRules.Length)
                return Logger.WarnReturn(false, $"Submit(): [{leaderboardPath}] has no scoring rule {ruleIndex}");

            if (leaderboardProto.ScoringRules[ruleIndex] is not LeaderboardScoringRuleIntPrototype rule)
                return Logger.WarnReturn(false, $"Submit(): scoring rule {ruleIndex} of [{leaderboardPath}] is not an integer rule");

            if (IsActive(leaderboardProto) == false)
                return false;

            int count = (int)Math.Min(value, int.MaxValue);
            player.LeaderboardManager.UpdateEvent(rule, count, Entity.InvalidId);
            return true;
        }

        /// <summary>
        /// Returns <see langword="true"/> if the leaderboard is running right now (enabled in the schedule and inside its time window).
        /// </summary>
        public static bool IsActive(string leaderboardPath)
        {
            LeaderboardPrototype leaderboardProto = Resolve(leaderboardPath);
            return leaderboardProto != null && IsActive(leaderboardProto);
        }

        /// <summary>
        /// Replaces the leaderboard's name and descriptions in the game's text (null = keep). Players see it after they reconnect.
        /// </summary>
        public static bool Rename(string leaderboardPath, string name, string briefDescription = null, string extendedDescription = null)
        {
            LeaderboardPrototype leaderboardProto = Resolve(leaderboardPath);
            if (leaderboardProto == null)
                return false;

            // Some boards (e.g. test ones) have no description texts: those are skipped, there is nothing to replace
            bool changed = false;
            if (name != null && HasText(leaderboardProto.Name)) changed |= ScriptText.OverrideText(leaderboardProto.Name, name);
            if (briefDescription != null && HasText(leaderboardProto.DescriptionBrief)) changed |= ScriptText.OverrideText(leaderboardProto.DescriptionBrief, briefDescription);
            if (extendedDescription != null && HasText(leaderboardProto.DescriptionExtended)) changed |= ScriptText.OverrideText(leaderboardProto.DescriptionExtended, extendedDescription);
            return changed;
        }

        private static bool HasText(LocaleStringId id) => id != LocaleStringId.Invalid && id != LocaleStringId.Blank;

        private static bool IsActive(LeaderboardPrototype leaderboardProto)
        {
            List<LeaderboardPrototype> active = new();
            LeaderboardInfoCache.Instance.GetActiveLeaderboardPrototypes(active);
            return active.Contains(leaderboardProto);
        }

        private static LeaderboardPrototype Resolve(string leaderboardPath)
        {
            PrototypeId leaderboardRef = GameDatabase.GetPrototypeRefByName(leaderboardPath);
            LeaderboardPrototype leaderboardProto = leaderboardRef.As<LeaderboardPrototype>();
            if (leaderboardProto == null)
                Logger.Warn($"Resolve(): [{leaderboardPath}] is not a leaderboard");

            return leaderboardProto;
        }
    }
}
