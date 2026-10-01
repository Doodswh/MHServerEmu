using System.Text;
using MHServerEmu.Commands.Attributes;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Network;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Inventories;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.Entities.Options;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Network;

namespace MHServerEmu.Commands.Implementations
{
    [CommandGroup("stash")]
    [CommandGroupDescription("Stash management commands.")]
    public class StashCommands : CommandGroup
    {
        private static readonly Logger Logger = LogManager.CreateLogger();
        private static readonly SemaphoreSlim _moveItemsConcurrencyLimit = new(initialCount: 20, maxCount: 20);

        private const string AvatarStashPrefix = "PlayerStashForAvatar";

        /// <summary>
        /// Everything we know about one unlocked stash tab, including every name it can be looked up by.
        /// </summary>
        private sealed class StashInfo
        {
            public PrototypeId Ref;
            public Inventory Inventory;
            public int Number;                  // 1-based, in the same order as the tabs are shown in game
            public string Label;                // what we show in !stash list
            public string AvatarName;           // DrDoom, Wolverine, ... (null for general / crafting tabs)
            public PrototypeId AvatarRef;
            public readonly List<string> Aliases = new();
        }

        #region Commands

        [Command("list")]
        [CommandDescription("Lists your stash tabs with the numbers and names you can use with !stash repair.")]
        [CommandUsage("stash list")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string List(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            if (playerConnection?.Player == null) return "Invalid player connection";

            List<StashInfo> stashes = GetStashInfos(playerConnection.Player);
            if (stashes.Count == 0) return "No stash tabs available";

            StringBuilder sb = new();
            sb.Append("Your stash tabs (use the number or any of the names with !stash repair):\r\n");
            foreach (StashInfo stash in stashes)
                sb.Append($"#{stash.Number} {stash.Label} - {stash.Inventory.Count} items\r\n");

            CommandHelper.SendMessageSplit(client, sb.ToString(), false);
            return string.Empty;
        }

        [Command("moveitems")]
        [CommandDescription("Moves everything from your general inventory into your stash tabs.")]
        [CommandUsage("stash moveitems")]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandUserLevel(AccountUserLevel.Admin)]
        public string MoveItems(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            if (playerConnection?.Player == null) return "Invalid player connection";

            if (!_moveItemsConcurrencyLimit.Wait(0))
                return "Too many players are sorting stashes right now. Please try again in a moment.";

            Player player = playerConnection.Player;
            Logger.Info($"[ItemMove] Starting automatic Move operation for player {player.Id}");

            List<PrototypeId> allStashRefs = ListPool<PrototypeId>.Instance.Get();
            try
            {
                if (!player.GetStashInventoryProtoRefs(allStashRefs, false, true))
                    return "No stash tabs available";

                // Normally items can only be dropped into a stash tab the client has open, which means the server has
                // already revealed that tab to the client. Moving into a tab that was never revealed makes the client
                // destroy the item and rely on a later reveal to get it back, which is what leaves items invisible.
                // Reveal every tab first so each move is sent to the client as a normal location change.
                RevealAllStashes(player, allStashRefs);

                int itemsMoved = SortItems(player, allStashRefs);
                return $"Moved {itemsMoved} items across all stash tabs.";
            }
            finally
            {
                _moveItemsConcurrencyLimit.Release();
                ListPool<PrototypeId>.Instance.Return(allStashRefs);
            }
        }

        [Command("repair")]
        [CommandDescription("Fixes invisible items in a stash tab. Add 'pull' to move the items back to your inventory instead.")]
        [CommandUsage("stash repair [tab number | name | all] [pull]")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string Repair(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            if (playerConnection?.Player == null) return "Invalid player connection";

            Player player = playerConnection.Player;

            List<string> args = new(@params);
            bool pull = args.Count > 0 && args[^1].Equals("pull", StringComparison.OrdinalIgnoreCase);
            if (pull)
                args.RemoveAt(args.Count - 1);

            string query = DecodeUrlString(string.Join(" ", args)).Trim();
            if (string.IsNullOrEmpty(query))
                return "Specify a stash tab, e.g. !stash repair 3, !stash repair DrDoom or !stash repair all. Use !stash list to see your tabs.";

            List<StashInfo> stashes = GetStashInfos(player);
            if (stashes.Count == 0) return "No stash tabs available";

            if (query.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                if (pull) return "'pull' only works on a single tab, general inventory space is limited.";

                int total = 0;
                foreach (StashInfo stash in stashes)
                    total += RefreshStash(player, stash);

                return $"Refreshed {total} items across {stashes.Count} stash tabs. Close and reopen your stash to see them.";
            }

            if (FindStash(stashes, query, out StashInfo target, out string error) == false)
                return error;

            if (pull)
                return PullStashToGeneral(player, target);

            int refreshed = RefreshStash(player, target);
            return $"Refreshed {refreshed} items in stash #{target.Number} {target.Label}. Close and reopen your stash to see them. " +
                $"If they are still missing use !stash repair {target.Number} pull";
        }

        [Command("playersort")]
        [CommandDescription("Sorts and compacts your general inventory.")]
        [CommandUsage("stash playersort")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string PlayerSort(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            if (playerConnection?.Player == null) return "Invalid player connection";

            return CompactInventory(playerConnection.Player);
        }

        #endregion

        #region Stash lookup

        /// <summary>
        /// Builds a list of the player's unlocked stash tabs in the order they are displayed in game.
        /// </summary>
        private static List<StashInfo> GetStashInfos(Player player)
        {
            List<StashInfo> stashes = new();

            List<PrototypeId> stashRefs = ListPool<PrototypeId>.Instance.Get();
            try
            {
                if (player.GetStashInventoryProtoRefs(stashRefs, false, true) == false)
                    return stashes;

                List<(StashInfo Info, int SortOrder)> sortable = new();

                foreach (PrototypeId stashRef in stashRefs)
                {
                    Inventory inventory = player.GetInventoryByRef(stashRef);
                    if (inventory == null) continue;

                    StashInfo info = new() { Ref = stashRef, Inventory = inventory };

                    string internalName = CleanPrototypeName(GameDatabase.GetPrototypeName(stashRef));

                    if (inventory.Prototype is PlayerStashInventoryPrototype stashProto && stashProto.ForAvatar != PrototypeId.Invalid)
                    {
                        info.AvatarRef = stashProto.ForAvatar;
                        info.AvatarName = CleanPrototypeName(GameDatabase.GetPrototypeName(stashProto.ForAvatar));
                    }
                    else if (internalName.StartsWith(AvatarStashPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        info.AvatarName = internalName.Substring(AvatarStashPrefix.Length);
                    }

                    string customName = null;
                    int sortOrder = int.MaxValue;
                    if (player.TryGetStashTabOptions(stashRef, out StashTabOptions options))
                    {
                        sortOrder = options.SortOrder;
                        if (string.IsNullOrWhiteSpace(options.DisplayName) == false)
                            customName = options.DisplayName.Trim();
                    }

                    // Every name this tab can be found by
                    AddAlias(info, customName);
                    AddAlias(info, info.AvatarName);
                    AddAlias(info, internalName);
                    AddAlias(info, StripPrefix(internalName, AvatarStashPrefix));
                    AddAlias(info, StripPrefix(internalName, "PlayerStash"));
                    AddAlias(info, inventory.Category.ToString());

                    // What the player sees in !stash list
                    string baseName = info.AvatarName ?? StripPrefix(internalName, "PlayerStash");
                    info.Label = customName != null && Normalize(customName) != Normalize(baseName)
                        ? $"{customName} [{baseName}]"
                        : baseName;

                    sortable.Add((info, sortOrder));
                }

                sortable.Sort((a, b) =>
                {
                    int result = a.SortOrder.CompareTo(b.SortOrder);
                    return result != 0 ? result : string.Compare(a.Info.Label, b.Info.Label, StringComparison.OrdinalIgnoreCase);
                });

                for (int i = 0; i < sortable.Count; i++)
                {
                    sortable[i].Info.Number = i + 1;
                    stashes.Add(sortable[i].Info);
                }

                return stashes;
            }
            finally
            {
                ListPool<PrototypeId>.Instance.Return(stashRefs);
            }
        }

        /// <summary>
        /// Finds a stash tab by number (3 or #3), exact name, or a unique part of a name.
        /// Names ignore case, spaces and punctuation, so "dr doom", "Dr.Doom" and "DrDoom" are the same.
        /// </summary>
        private static bool FindStash(List<StashInfo> stashes, string query, out StashInfo match, out string error)
        {
            match = null;
            error = null;

            string numberText = query.TrimStart('#');
            if (int.TryParse(numberText, out int number))
            {
                if (number >= 1 && number <= stashes.Count)
                {
                    match = stashes[number - 1];
                    return true;
                }

                error = $"There is no stash tab #{number}, you have {stashes.Count}. Use !stash list to see them.";
                return false;
            }

            string normalizedQuery = Normalize(query);
            if (normalizedQuery.Length == 0)
            {
                error = "Specify a stash tab number or name. Use !stash list to see your tabs.";
                return false;
            }

            List<StashInfo> exact = stashes.FindAll(s => s.Aliases.Exists(a => Normalize(a) == normalizedQuery));
            if (exact.Count == 1)
            {
                match = exact[0];
                return true;
            }

            List<StashInfo> candidates = exact.Count > 1
                ? exact
                : stashes.FindAll(s => s.Aliases.Exists(a => Normalize(a).Contains(normalizedQuery)));

            if (candidates.Count == 1)
            {
                match = candidates[0];
                return true;
            }

            if (candidates.Count > 1)
            {
                StringBuilder sb = new($"'{query}' matches more than one tab, use the number instead:");
                int shown = 0;
                foreach (StashInfo candidate in candidates)
                {
                    if (shown++ == 8) { sb.Append(" ..."); break; }
                    sb.Append($" #{candidate.Number} {candidate.Label},");
                }

                error = sb.ToString().TrimEnd(',');
                return false;
            }

            error = $"Stash '{query}' not found. Use !stash list to see your tabs.";
            return false;
        }

        private static void AddAlias(StashInfo info, string alias)
        {
            if (string.IsNullOrWhiteSpace(alias)) return;
            if (info.Aliases.Exists(a => Normalize(a) == Normalize(alias))) return;
            info.Aliases.Add(alias);
        }

        /// <summary>
        /// Turns "Entity/Inventory/.../PlayerStashForAvatarDrDoom.prototype" into "PlayerStashForAvatarDrDoom".
        /// </summary>
        private static string CleanPrototypeName(string prototypeName)
        {
            if (string.IsNullOrEmpty(prototypeName)) return string.Empty;

            string name = prototypeName.Replace('\\', '/');
            if (name.EndsWith(".prototype", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - ".prototype".Length);

            int lastSlash = name.LastIndexOf('/');
            if (lastSlash >= 0)
                name = name.Substring(lastSlash + 1);

            return name.Trim();
        }

        private static string StripPrefix(string value, string prefix)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && value.Length > prefix.Length)
                return value.Substring(prefix.Length);
            return value;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            StringBuilder sb = new(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static string DecodeUrlString(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input.Replace("%20", " ").Replace("%2F", "/").Replace("%3A", ":").Replace("%2D", "-");
        }

        #endregion

        #region Repair

        private static void RevealAllStashes(Player player, List<PrototypeId> stashRefs)
        {
            foreach (PrototypeId stashRef in stashRefs)
            {
                InventoryPrototype stashProto = GameDatabase.GetPrototype<InventoryPrototype>(stashRef);
                if (stashProto != null)
                    player.RevealInventory(stashProto);
            }
        }

        /// <summary>
        /// Makes the client forget every item in the stash tab and then sends them all again,
        /// without moving anything. Doesn't need any free inventory space.
        /// </summary>
        private static int RefreshStash(Player player, StashInfo stash)
        {
            Inventory inventory = stash.Inventory;
            EntityManager entityManager = player.Game.EntityManager;
            AreaOfInterest aoi = player.AOI;

            List<ulong> itemIds = ListPool<ulong>.Instance.Get();
            try
            {
                foreach (var entry in inventory)
                    itemIds.Add(entry.Id);

                if (itemIds.Count == 0)
                    return 0;

                // Stash tabs are only sent to the client once revealed. If this tab isn't gated that way
                // there is nothing to refresh, pulling the items out is the only option.
                if (inventory.Prototype.InventoryRequiresFlaggedVisibility() == false || aoi == null)
                {
                    Logger.Warn($"[STASH-REPAIR] Stash #{stash.Number} {stash.Label} can't be refreshed in place");
                    return 0;
                }

                // Hide the tab: the client drops whatever it currently thinks is in there
                inventory.VisibleToOwner = false;
                foreach (ulong itemId in itemIds)
                {
                    Entity entity = entityManager.GetEntity<Entity>(itemId);
                    if (entity != null)
                        aoi.ConsiderEntity(entity);
                }

                // Show the tab again: every item is sent to the client fresh with its current slot
                player.RevealInventory(inventory.Prototype);

                Logger.Info($"[STASH-REPAIR] Refreshed {itemIds.Count} items in stash #{stash.Number} {stash.Label} for player {player.Id}");
                return itemIds.Count;
            }
            finally
            {
                ListPool<ulong>.Instance.Return(itemIds);
            }
        }

        /// <summary>
        /// Old repair behavior: moves the items out of the stash tab into the general inventory.
        /// </summary>
        private static string PullStashToGeneral(Player player, StashInfo stash)
        {
            Inventory generalInventory = player.GetInventory(InventoryConvenienceLabel.General);
            if (generalInventory == null)
                return "Could not access general inventory for repair";

            EntityManager entityManager = player.Game.EntityManager;

            List<ulong> itemIds = ListPool<ulong>.Instance.Get();
            try
            {
                foreach (var entry in stash.Inventory)
                    itemIds.Add(entry.Id);

                int moved = 0;
                int failed = 0;

                foreach (ulong itemId in itemIds)
                {
                    if (generalInventory.CapacityRemaining <= 0)
                        break;

                    Item item = entityManager.GetEntity<Item>(itemId);
                    if (item == null) continue;

                    ulong? stackEntityId = null;
                    InventoryResult result = Inventory.ChangeEntityInventoryLocation(
                        item, generalInventory, Inventory.InvalidSlot, ref stackEntityId, true);

                    if (result == InventoryResult.Success)
                    {
                        moved++;
                    }
                    else
                    {
                        failed++;
                        Logger.Warn($"[STASH-REPAIR] Failed to move item {itemId} out of stash #{stash.Number} {stash.Label}: {result}");
                    }
                }

                int remaining = stash.Inventory.Count;
                string message = $"Moved {moved} items from stash #{stash.Number} {stash.Label} to your inventory.";
                if (remaining > 0)
                    message += $" {remaining} items are still in the tab" + (generalInventory.CapacityRemaining <= 0 ? " (your inventory is full)." : ".");
                if (failed > 0)
                    message += $" {failed} items could not be moved, see the server log.";

                return message;
            }
            finally
            {
                ListPool<ulong>.Instance.Return(itemIds);
            }
        }

        #endregion

        #region Move items

        private int SortItems(Player player, List<PrototypeId> stashRefs)
        {
            Inventory generalInventory = player.GetInventory(InventoryConvenienceLabel.General);
            if (generalInventory == null) return 0;

            var entityManager = player.Game.EntityManager;

            using var allItemIdsHandle = ListPool<ulong>.Instance.Get(out List<ulong> allItemIds);
            foreach (var entry in generalInventory)
                allItemIds.Add(entry.Id);

            Dictionary<string, List<PrototypeId>> categoryStashMap = new();

            int itemsMoved = 0;

            foreach (ulong itemId in allItemIds)
            {
                Item item = entityManager.GetEntity<Item>(itemId);
                if (item == null || item.IsEquipped) continue;

                if (TryMoveItemToAnyStash(player, item, stashRefs, categoryStashMap))
                    itemsMoved++;
            }

            foreach (var list in categoryStashMap.Values)
                ListPool<PrototypeId>.Instance.Return(list);

            return itemsMoved;
        }

        private bool TryMoveItemToAnyStash(Player player, Item item, List<PrototypeId> stashRefs,
            Dictionary<string, List<PrototypeId>> categoryStashMap)
        {
            if (TryMoveToMatchingStack(player, item, stashRefs))
                return true;

            string category = GetItemCategory(item);
            if (!categoryStashMap.TryGetValue(category, out List<PrototypeId> categoryStashes))
            {
                categoryStashes = ListPool<PrototypeId>.Instance.Get();
                categoryStashMap[category] = categoryStashes;
            }

            // Hero stash tabs only take items that belong to that hero
            foreach (PrototypeId stashRef in stashRefs)
            {
                var proto = GameDatabase.GetPrototype<InventoryPrototype>(stashRef);
                if (proto?.Category != InventoryCategory.PlayerStashAvatarSpecific) continue;

                if (ItemBelongsToAvatarStash(item, proto) == false) continue;

                Inventory stash = player.GetInventoryByRef(stashRef);
                if (stash == null || stash.CapacityRemaining <= 0) continue;

                if (TryMoveItemToStash(item, stash))
                    return true;
            }

            foreach (PrototypeId stashRef in categoryStashes)
            {
                Inventory stash = player.GetInventoryByRef(stashRef);
                if (stash != null && stash.CapacityRemaining > 0 && TryMoveItemToStash(item, stash))
                    return true;
            }

            using var candidatesHandle = ListPool<(PrototypeId Ref, int FreeSlots)>.Instance.Get(out var candidates);
            foreach (PrototypeId stashRef in stashRefs)
            {
                if (categoryStashes.Contains(stashRef)) continue;

                var proto = GameDatabase.GetPrototype<InventoryPrototype>(stashRef);
                if (proto?.Category == InventoryCategory.PlayerStashAvatarSpecific) continue;

                Inventory stash = player.GetInventoryByRef(stashRef);
                if (stash == null || stash.CapacityRemaining <= 0) continue;

                candidates.Add((stashRef, stash.CapacityRemaining));
            }

            candidates.Sort((a, b) => b.FreeSlots.CompareTo(a.FreeSlots));
            foreach (var (stashRef, _) in candidates)
            {
                Inventory stash = player.GetInventoryByRef(stashRef);
                if (stash == null) continue;

                if (TryMoveItemToStash(item, stash))
                {
                    if (!categoryStashes.Contains(stashRef))
                        categoryStashes.Add(stashRef);
                    return true;
                }
            }

            Logger.Warn($"[STASH] Could not find a home for item {item.Id} ({GameDatabase.GetPrototypeName(item.PrototypeDataRef)})");
            return false;
        }

        /// <summary>
        /// Tries to stack <paramref name="item"/> onto an existing matching stack in any stash.
        /// </summary>
        private bool TryMoveToMatchingStack(Player player, Item item, List<PrototypeId> stashRefs)
        {
            var entityManager = player.Game.EntityManager;

            foreach (PrototypeId stashRef in stashRefs)
            {
                Inventory stash = player.GetInventoryByRef(stashRef);
                if (stash == null) continue;

                foreach (var entry in stash)
                {
                    Item targetItem = entityManager.GetEntity<Item>(entry.Id);
                    if (targetItem == null || !item.CanStackOnto(targetItem)) continue;

                    ulong? stackEntityId = null;
                    InventoryResult result = Inventory.ChangeEntityInventoryLocation(
                        item, stash, entry.Slot, ref stackEntityId, false);

                    if (result == InventoryResult.Success)
                    {
                        Logger.Info($"[STASH] Stacked {item.Id} onto existing stack in {stashRef}.");
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Moves <paramref name="item"/> to a free slot in <paramref name="stash"/> using
        /// ChangeEntityInventoryLocation directly (bypasses the open-stash client gate).
        /// Returns false if the item fails the containment filter or any other pipeline check.
        /// </summary>
        private bool TryMoveItemToStash(Item item, Inventory stash)
        {
            ulong? stackEntityId = null;
            InventoryResult result = Inventory.ChangeEntityInventoryLocation(
                item, stash, Inventory.InvalidSlot, ref stackEntityId, false);

            if (result == InventoryResult.Success)
            {
                Logger.Info($"[STASH] Moved {item.Id} to {stash.PrototypeDataRef}.");
                return true;
            }

            Logger.Debug($"[STASH] Rejected {item.Id} for {stash.PrototypeDataRef}: {result}");
            return false;
        }

        /// <summary>
        /// Returns true if <paramref name="item"/> is equippable by or bound to the hero that owns this stash tab,
        /// or its prototype path is under that hero's folder.
        /// </summary>
        private static bool ItemBelongsToAvatarStash(Item item, InventoryPrototype stashProto)
        {
            if (item == null || stashProto == null) return false;

            PrototypeId avatarRef = (stashProto as PlayerStashInventoryPrototype)?.ForAvatar ?? PrototypeId.Invalid;

            if (avatarRef != PrototypeId.Invalid)
            {
                if (item.ItemSpec.EquippableBy == avatarRef)
                    return true;

                if (item.ItemSpec.GetBindingState(out PrototypeId boundAgentRef) && boundAgentRef != PrototypeId.Invalid)
                    return boundAgentRef == avatarRef;
            }

            string avatarName = avatarRef != PrototypeId.Invalid
                ? CleanPrototypeName(GameDatabase.GetPrototypeName(avatarRef))
                : StripPrefix(CleanPrototypeName(GameDatabase.GetPrototypeName(stashProto.DataRef)), AvatarStashPrefix);

            return IsItemSuitableForAvatar(item, avatarName);
        }

        private static bool IsItemSuitableForAvatar(Item item, string avatarName)
        {
            if (item == null || string.IsNullOrEmpty(avatarName)) return false;

            string itemProtoName = GameDatabase.GetPrototypeName(item.PrototypeDataRef).ToLowerInvariant();
            string baseAvatarName = avatarName.ToLowerInvariant();

            if (itemProtoName.Contains($"/avatars/{baseAvatarName}/"))
                return true;

            if (itemProtoName.Contains(baseAvatarName))
            {
                if (baseAvatarName == "doom" && itemProtoName.Contains("drdoom")) return false;
                return true;
            }

            return false;
        }

        #endregion

        #region Compact (playersort)

        private string CompactInventory(Player player)
        {
            Inventory generalInventory = player.GetInventory(InventoryConvenienceLabel.General);
            if (generalInventory == null) return "No general inventory found";

            var entityManager = player.Game.EntityManager;

            using var itemsToSortHandle = ListPool<Item>.Instance.Get(out List<Item> itemsToSort);
            foreach (var entry in generalInventory)
            {
                Item item = entityManager.GetEntity<Item>(entry.Id);
                if (item != null && !item.IsEquipped)
                    itemsToSort.Add(item);
            }

            itemsToSort.Sort((a, b) =>
            {
                int cat = string.Compare(GetItemCategory(a), GetItemCategory(b), StringComparison.Ordinal);
                if (cat != 0) return cat;
                return string.Compare(
                    GameDatabase.GetPrototypeName(a.PrototypeDataRef),
                    GameDatabase.GetPrototypeName(b.PrototypeDataRef),
                    StringComparison.Ordinal);
            });

            int itemsCompacted = 0;
            uint nextSlot = 0;

            foreach (Item item in itemsToSort)
            {
                if (item.InventoryLocation.Slot != nextSlot)
                {
                    ulong? stackEntityId = null;
                    InventoryResult result = Inventory.ChangeEntityInventoryLocation(
                        item, generalInventory, nextSlot, ref stackEntityId, false);

                    if (result == InventoryResult.Success)
                        itemsCompacted++;
                    else
                        Logger.Warn($"[STASH-COMPACT] Failed to move {item.Id} to slot {nextSlot}: {result}");
                }

                nextSlot++;
            }

            return $"Compacted {itemsCompacted} items in your inventory.";
        }

        private static string GetItemCategory(Item item)
        {
            string itemProto = GameDatabase.GetPrototypeName(item.PrototypeDataRef);
            if (itemProto.StartsWith("Entity/Items/"))
            {
                string[] parts = itemProto.Split('/');
                if (parts.Length >= 3)
                {
                    if (parts[2] == "Armor")
                        return itemProto.Contains("Unique") ? "Uniques" : "Gear";

                    switch (parts[2])
                    {
                        case "Crafting":
                        case "CurrencyItems":
                        case "Artifacts":
                        case "Insignias":
                        case "Legendaries":
                        case "Medals":
                        case "Pets":
                        case "Relics":
                        case "Rings":
                        case "Runewords":
                        case "DRScenario":
                            return parts[2];
                    }
                }
            }
            return "Other";
        }

        #endregion
    }
}
