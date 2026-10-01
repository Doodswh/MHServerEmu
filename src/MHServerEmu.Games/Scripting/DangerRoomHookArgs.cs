using MHServerEmu.Core.Logging;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Shared members of the Endless Danger Room (Cable scenario) hook args.
    /// </summary>
    public abstract class DangerRoomArgsBase
    {
        public Player Player { get; }
        public Region Region { get; }

        public string PlayerName { get => Player?.GetName() ?? string.Empty; }
        public string RegionName { get => Region?.PrototypeName ?? string.Empty; }

        protected DangerRoomArgsBase(Player player, Region region)
        {
            Player = player;
            Region = region;
        }

        /// <summary>
        /// Sends a server chat message to the run's player.
        /// </summary>
        public void SendMessage(string text)
        {
            ScriptHooks.SendChatMessage(Player, text, false);
        }

        /// <summary>
        /// Shows text registered with ui.register / ui.registerRange (Lua) or ScriptText (C#) as an on-screen banner.
        /// </summary>
        public bool ShowBanner(string key) => ScriptText.ShowBanner(Player, key);
        public bool ShowBanner(string key, int number) => ScriptText.ShowBanner(Player, key, number);
        public bool ShowBanner(string key, int number, string style) => ScriptText.ShowBanner(Player, key, number, style);
        public bool ShowBanner(string key, int number, string style, int durationMS) => ScriptText.ShowBanner(Player, key, number, style, durationMS);
    }

    /// <summary>
    /// Args for <see cref="ScriptHooks.DangerRoomWaveStarting"/>. Change the counts to change what spawns.
    /// </summary>
    public sealed class DangerRoomWaveStartingArgs : DangerRoomArgsBase
    {
        public int Wave { get; }
        public int BossCount { get; set; }
        public int MobCount { get; set; }

        public DangerRoomWaveStartingArgs(Player player, Region region, int wave, int bossCount, int mobCount) : base(player, region)
        {
            Wave = wave;
            BossCount = bossCount;
            MobCount = mobCount;
        }
    }

    /// <summary>
    /// Args for <see cref="ScriptHooks.DangerRoomWaveCleared"/>. Controls the pause before the next wave and what is shown.
    /// </summary>
    public sealed class DangerRoomWaveClearedArgs : DangerRoomArgsBase
    {
        public int Wave { get; }

        /// <summary>Seconds until the next wave starts.</summary>
        public float DelaySeconds { get; set; }

        /// <summary>Set to false to suppress the built-in wave clear banner and break message (e.g. to show your own).</summary>
        public bool ShowDefaultMessages { get; set; } = true;

        public DangerRoomWaveClearedArgs(Player player, Region region, int wave, float delaySeconds) : base(player, region)
        {
            Wave = wave;
            DelaySeconds = delaySeconds;
        }
    }

    /// <summary>
    /// Args for <see cref="ScriptHooks.DangerRoomRewards"/>. Raised when the completion rewards are dropped.
    /// The main loot roll, experience orbs and Danger Room merits always drop; scripts control the bonus item rewards.
    /// </summary>
    public sealed class DangerRoomRewardsArgs : DangerRoomArgsBase
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        public readonly struct Reward
        {
            public readonly PrototypeId ItemRef;
            public readonly int Count;
            public readonly int Slot;

            public Reward(PrototypeId itemRef, int count, int slot)
            {
                ItemRef = itemRef;
                Count = count;
                Slot = slot;
            }
        }

        private readonly List<Reward> _rewards = new();

        public int ClearedWaves { get; }

        /// <summary>Set to true to skip the built-in bonus item rewards (use AddReward instead).</summary>
        public bool ReplaceDefaultRewards { get; set; }

        public IReadOnlyList<Reward> Rewards { get => _rewards; }

        public DangerRoomRewardsArgs(Player player, Region region, int clearedWaves) : base(player, region)
        {
            ClearedWaves = clearedWaves;
        }

        /// <summary>
        /// Drops <paramref name="count"/> of the item with the given prototype path. <paramref name="slot"/> picks the drop
        /// spot around the chest (same numbering as the built-in rewards); -1 picks the next free spot.
        /// </summary>
        public bool AddReward(string prototypePath) => AddReward(prototypePath, 1, -1);
        public bool AddReward(string prototypePath, int count) => AddReward(prototypePath, count, -1);
        public bool AddReward(string prototypePath, int count, int slot)
        {
            PrototypeId itemRef = GameDatabase.GetPrototypeRefByName(prototypePath);
            if (itemRef == PrototypeId.Invalid || itemRef.As<ItemPrototype>() == null)
                return Logger.WarnReturn(false, $"AddReward(): [{prototypePath}] is not a valid item prototype");

            if (count <= 0)
                return false;

            _rewards.Add(new(itemRef, count, slot));
            return true;
        }
    }

    /// <summary>
    /// Args for <see cref="ScriptHooks.DangerRoomFinished"/>. Raised when a run ends.
    /// </summary>
    public sealed class DangerRoomFinishedArgs : DangerRoomArgsBase
    {
        public int ClearedWaves { get; }

        /// <summary>Set to false to suppress the built-in "Danger Room Finished" banner.</summary>
        public bool ShowDefaultMessages { get; set; } = true;

        public DangerRoomFinishedArgs(Player player, Region region, int clearedWaves) : base(player, region)
        {
            ClearedWaves = clearedWaves;
        }
    }
}
