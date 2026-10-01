using MHServerEmu.Core.Network;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.Games.Entities;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Shared registry of script hooks. Game simulation code only ever calls Invoke on these hooks and has no knowledge
    /// of any scripting engine. Script bridges (e.g. MHServerEmu.Scripting) register handlers here.
    /// </summary>
    /// <remarks>
    /// Call sites should check HasHandlers before allocating args, so hooks cost nothing when no script uses them.
    /// To add a hook: declare it here, add an args class to ScriptHookArgs.cs, and invoke it from the simulation code.
    /// </remarks>
    public static class ScriptHooks
    {
        /// <summary>
        /// Raised before a power applies a condition to a target. Set Cancel to block the condition.
        /// </summary>
        public static readonly ScriptHook<ConditionApplyingArgs> ConditionApplying = new(nameof(ConditionApplying));

        /// <summary>
        /// Raised after enemy affixes are rolled for a spawn group, before they are assigned. The affix list can be modified.
        /// </summary>
        public static readonly ScriptHook<EnemyAffixesRolledArgs> EnemyAffixesRolled = new(nameof(EnemyAffixesRolled));

        /// <summary>
        /// Raised when a world entity is killed.
        /// </summary>
        public static readonly ScriptHook<EntityKilledArgs> EntityKilled = new(nameof(EntityKilled));

        /// <summary>
        /// Raised when a player enters a region.
        /// </summary>
        public static readonly ScriptHook<PlayerEnteredRegionArgs> PlayerEnteredRegion = new(nameof(PlayerEnteredRegion));

        /// <summary>
        /// Raised before an Endless Danger Room wave spawns. Boss / mob counts can be changed.
        /// </summary>
        public static readonly ScriptHook<DangerRoomWaveStartingArgs> DangerRoomWaveStarting = new(nameof(DangerRoomWaveStarting));

        /// <summary>
        /// Raised when an Endless Danger Room wave is cleared. The delay before the next wave and the messages can be changed.
        /// </summary>
        public static readonly ScriptHook<DangerRoomWaveClearedArgs> DangerRoomWaveCleared = new(nameof(DangerRoomWaveCleared));

        /// <summary>
        /// Raised when Endless Danger Room completion rewards drop. Scripts can add or replace the bonus item rewards.
        /// </summary>
        public static readonly ScriptHook<DangerRoomRewardsArgs> DangerRoomRewards = new(nameof(DangerRoomRewards));

        /// <summary>
        /// Raised when an Endless Danger Room run ends.
        /// </summary>
        public static readonly ScriptHook<DangerRoomFinishedArgs> DangerRoomFinished = new(nameof(DangerRoomFinished));

        /// <summary>
        /// Raised when a player types a chat message starting with '!', before built-in server commands. Set Handled to consume it.
        /// </summary>
        public static readonly ScriptHook<ChatCommandArgs> ChatCommand = new(nameof(ChatCommand));

        /// <summary>
        /// Raised after an item is equipped on or unequipped from an agent (avatar or team-up), including when saved gear
        /// is loaded. Use it to recompute gear-based bonuses (see ScriptBonuses).
        /// </summary>
        public static readonly ScriptHook<EquipmentChangedArgs> EquipmentChanged = new(nameof(EquipmentChanged));

        /// <summary>
        /// Raised for every generated item (drops, vendors, crafting) before its normal affixes are rolled.
        /// Scripts can change rarity and item level and add extra affixes. Runs often: keep handlers cheap (prefer C#).
        /// </summary>
        public static readonly ScriptHook<ItemAffixesRollingArgs> ItemAffixesRolling = new(nameof(ItemAffixesRolling));

        /// <summary>
        /// Raised when a region instance is about to generate its layout. Scripts can set the seed and tune the random grid
        /// generators (room / connection removal).
        /// </summary>
        public static readonly ScriptHook<RegionGeneratingArgs> RegionGenerating = new(nameof(RegionGenerating));

        /// <summary>
        /// Raised when a player interacts with (clicks) a world entity, e.g. an NPC spawned with ScriptSpawner.SpawnInteractable.
        /// </summary>
        public static readonly ScriptHook<EntityInteractedArgs> EntityInteracted = new(nameof(EntityInteracted));

        private static readonly Dictionary<string, IScriptHook> _hooksByName = new(StringComparer.OrdinalIgnoreCase)
        {
            { ConditionApplying.Name,       ConditionApplying },
            { EnemyAffixesRolled.Name,      EnemyAffixesRolled },
            { EntityKilled.Name,            EntityKilled },
            { PlayerEnteredRegion.Name,     PlayerEnteredRegion },
            { DangerRoomWaveStarting.Name,  DangerRoomWaveStarting },
            { DangerRoomWaveCleared.Name,   DangerRoomWaveCleared },
            { DangerRoomRewards.Name,       DangerRoomRewards },
            { DangerRoomFinished.Name,      DangerRoomFinished },
            { ChatCommand.Name,             ChatCommand },
            { EquipmentChanged.Name,        EquipmentChanged },
            { ItemAffixesRolling.Name,      ItemAffixesRolling },
            { RegionGenerating.Name,        RegionGenerating },
            { EntityInteracted.Name,        EntityInteracted },
        };

        public static IEnumerable<IScriptHook> All { get => _hooksByName.Values; }

        public static bool TryGetHook(string name, out IScriptHook hook)
        {
            return _hooksByName.TryGetValue(name, out hook);
        }

        /// <summary>
        /// Removes all handlers registered by the specified owner from every hook. Returns the number of removed handlers.
        /// </summary>
        public static int UnregisterOwner(string owner)
        {
            int removed = 0;
            foreach (IScriptHook hook in _hooksByName.Values)
                removed += hook.UnregisterOwner(owner);
            return removed;
        }

        /// <summary>
        /// Returns <see langword="true"/> if the player's account is Admin or higher (Dev counts too).
        /// </summary>
        public static bool IsAdmin(Player player)
        {
            DBAccount account = player?.PlayerConnection?._dbAccount;
            return account != null && account.UserLevel >= AccountUserLevel.Admin;
        }

        /// <summary>
        /// Sends a server chat message to the specified player.
        /// </summary>
        public static void SendChatMessage(Player player, string text, bool showSender = true)
        {
            if (player == null || string.IsNullOrEmpty(text)) return;

            ServiceMessage.GroupingManagerMetagameMessage message = new(player.DatabaseUniqueId, text, showSender);
            ServerManager.Instance.SendMessageToService(GameServiceType.GroupingManager, message);
        }
    }
}
