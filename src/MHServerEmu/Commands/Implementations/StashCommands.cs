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
using MHServerEmu.Games.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MHServerEmu.Commands.Implementations
{
    [CommandGroup("stash")]
    public class StashCommands : CommandGroup
    {
        private static readonly Logger Logger = LogManager.CreateLogger();
        private readonly Dictionary<string, List<PrototypeId>> _categoryStashMap = new();

        private static readonly Dictionary<string, List<string>> AvatarNameVariations = new(StringComparer.OrdinalIgnoreCase)
        {
            { "DrDoom.prototype", new List<string> { "doctordoom", "drdoom", "doom" } },
            { "DrStrange.prototype", new List<string> { "doctorstrange", "drstrange" } },
            { "Spiderman.prototype", new List<string> { "spiderman", "spider-man" } },
            { "StarLord.prototype", new List<string> { "starlord", "star-lord" } },
            { "InvisiWoman.prototype", new List<string> { "invisiblewoman", "invisiwoman" } },
        };

        [Command("sort")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string Sort(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            if (playerConnection?.Player == null) return "Invalid player connection";
            Player player = playerConnection.Player;

            Logger.Info($"[STASH-SORT] Starting automatic sort operation for player {player.Id}");

            List<PrototypeId> allStashRefs = ListPool<PrototypeId>.Instance.Get();
            if (!player.GetStashInventoryProtoRefs(allStashRefs, false, true))
            {
                ListPool<PrototypeId>.Instance.Return(allStashRefs);
                return "No stash tabs available";
            }

            ClearCategoryStashMap();
            int itemsMoved = SortItems(player, allStashRefs);

            ListPool<PrototypeId>.Instance.Return(allStashRefs);

            return $"Sorted {itemsMoved} items across all stash tabs.";
        }

        [Command("repair")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string Repair(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            if (playerConnection?.Player == null) return "Invalid player connection";

            string targetStashName = @params.Length > 0 ? string.Join(" ", @params) : null;
            if (string.IsNullOrEmpty(targetStashName))
            {
                return "Please specify a stash name to repair (e.g., /stash repair DrDoom).";
            }

            return RepairStash(playerConnection.Player, targetStashName);
        }

        [Command("internal")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string Internal(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            if (playerConnection?.Player == null) return "Invalid player connection";

            return CompactInventory(playerConnection.Player);
        }

        private string RepairStash(Player player, string stashName)
        {
            stashName = DecodeUrlString(stashName);

            List<PrototypeId> allStashRefs = ListPool<PrototypeId>.Instance.Get();
            if (!player.GetStashInventoryProtoRefs(allStashRefs, false, true))
            {
                ListPool<PrototypeId>.Instance.Return(allStashRefs);
                return "No stash tabs available";
            }

            PrototypeId targetStashRef = PrototypeId.Invalid;
            foreach (PrototypeId stashRef in allStashRefs)
            {
                Inventory stash = player.GetInventoryByRef(stashRef);
                if (stash != null)
                {
                    string protoName = GameDatabase.GetPrototypeName(stash.PrototypeDataRef);
                    string generatedName = GenerateStashName(stash, protoName);

                    if (DecodeUrlString(generatedName).Equals(stashName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetStashRef = stashRef;
                        break;
                    }
                }
            }

            ListPool<PrototypeId>.Instance.Return(allStashRefs);

            if (targetStashRef == PrototypeId.Invalid)
            {
                return $"Stash '{stashName}' not found";
            }

            Inventory targetStash = player.GetInventoryByRef(targetStashRef);
            if (targetStash == null)
            {
                return $"Could not get inventory for stash '{stashName}'";
            }

            var entityManager = player.Game.EntityManager;
            Inventory generalInventory = player.GetInventory(InventoryConvenienceLabel.General);
            if (generalInventory == null)
            {
                return "Could not access general inventory for repair";
            }

            int itemsRepaired = 0;
            List<ulong> itemsToMove = new List<ulong>();

            foreach (var entry in targetStash)
            {
                Item item = entityManager.GetEntity<Item>(entry.Id);
                if (item != null)
                {
                    itemsToMove.Add(item.Id);
                }
            }

            Logger.Info($"[STASH-REPAIR] Found {itemsToMove.Count} items to repair in {stashName}");

            foreach (ulong itemId in itemsToMove)
            {
                Item item = entityManager.GetEntity<Item>(itemId);
                if (item != null)
                {
                    uint freeSlot = generalInventory.GetFreeSlot(item, true, true);
                    if (freeSlot != Inventory.InvalidSlot)
                    {
                        bool needsBindingSkip = item.IsBoundToCharacter;
                        if (needsBindingSkip)
                            item.SetStatus(EntityStatus.SkipItemBindingCheck, true);

                        try
                        {
                            if (player.TryInventoryMove(item.Id, generalInventory.OwnerId, generalInventory.PrototypeDataRef, freeSlot))
                            {
                                itemsRepaired++;
                                Logger.Info($"[STASH-REPAIR] Moved {GameDatabase.GetPrototypeName(item.PrototypeDataRef)} back to general inventory");
                            }
                        }
                        finally
                        {
                            if (needsBindingSkip)
                                item.SetStatus(EntityStatus.SkipItemBindingCheck, false);
                        }
                    }
                }
            }

            return $"Repaired {itemsRepaired} items from stash '{stashName}' back to general inventory";
        }

        private int SortItems(Player player, List<PrototypeId> stashRefs)
        {
            Inventory generalInventory = player.GetInventory(InventoryConvenienceLabel.General);
            if (generalInventory == null) return 0;

            var entityManager = player.Game.EntityManager;
            List<ulong> allItemIds = ListPool<ulong>.Instance.Get();
            foreach (var entry in generalInventory)
            {
                allItemIds.Add(entry.Id);
            }

            HashSet<ulong> processedItemIds = HashSetPool<ulong>.Instance.Get();
            int itemsMoved = 0;

            // First Pass: Stackable Items
            foreach (ulong itemId in allItemIds)
            {
                Item item = entityManager.GetEntity<Item>(itemId);
                if (item == null || !item.CanStack() || item.IsEquipped) continue;

                if (TryMoveToMatchingStack(player, item, stashRefs))
                {
                    itemsMoved++;
                    processedItemIds.Add(itemId);
                }
            }

            // Item Collection & Sorting
            List<Item> itemsToSort = ListPool<Item>.Instance.Get();
            foreach (ulong itemId in allItemIds)
            {
                if (processedItemIds.Contains(itemId)) continue;

                Item item = entityManager.GetEntity<Item>(itemId);
                if (item != null && !item.IsEquipped)
                {
                    itemsToSort.Add(item);
                }
            }

            itemsToSort.Sort((a, b) =>
            {
                int categoryComparison = string.Compare(GetItemCategory(a), GetItemCategory(b), StringComparison.Ordinal);
                if (categoryComparison != 0) return categoryComparison;
                return string.Compare(GameDatabase.GetPrototypeName(a.PrototypeDataRef), GameDatabase.GetPrototypeName(b.PrototypeDataRef), StringComparison.Ordinal);
            });

            // Stash Categorization
            List<PrototypeId> avatarStashes = ListPool<PrototypeId>.Instance.Get();
            List<PrototypeId> craftingStashes = ListPool<PrototypeId>.Instance.Get();
            foreach (var stashRef in stashRefs)
            {
                var inventoryProto = GameDatabase.GetPrototype<InventoryPrototype>(stashRef);
                if (inventoryProto == null) continue;

                if (inventoryProto.Category == InventoryCategory.PlayerStashAvatarSpecific)
                    avatarStashes.Add(stashRef);
                else if (inventoryProto.Category == InventoryCategory.PlayerCraftingRecipes)
                    craftingStashes.Add(stashRef);
            }

            // Second Pass: Sorted Item Placement
            foreach (Item item in itemsToSort)
            {
                if (processedItemIds.Contains(item.Id)) continue;
                bool moved = false;

                // Try avatar stashes first
                if (avatarStashes.Count > 0)
                {
                    foreach (var avatarStashRef in avatarStashes)
                    {
                        Inventory stash = player.GetInventoryByRef(avatarStashRef);
                        if (stash == null || GetInventoryFreeSlots(stash) == 0) continue;

                        string stashProtoName = GameDatabase.GetPrototypeName(stash.PrototypeDataRef);
                        string avatarName = ExtractAvatarNameFromStash(stashProtoName);

                        if (!string.IsNullOrEmpty(avatarName) && IsItemSuitableForAvatar(item, avatarName))
                        {
                            if (TryMoveItemToStash(player, item, avatarStashRef, item.IsBoundToCharacter))
                            {
                                moved = true;
                                break;
                            }
                        }
                    }
                }

                if (!moved)
                {
                    if (GetItemCategory(item) == "Crafting" && craftingStashes.Count > 0)
                    {
                        foreach (var craftingStashRef in craftingStashes)
                        {
                            if (TryMoveItemToStash(player, item, craftingStashRef, false))
                            {
                                moved = true;
                                break;
                            }
                        }
                    }
                    if (!moved && TryMoveToCategoryStash(player, item, stashRefs))
                    {
                        moved = true;
                    }
                }

                if (moved)
                {
                    itemsMoved++;
                    processedItemIds.Add(item.Id);
                }
            }

            // Cleanup
            ListPool<ulong>.Instance.Return(allItemIds);
            HashSetPool<ulong>.Instance.Return(processedItemIds);
            ListPool<Item>.Instance.Return(itemsToSort);
            ListPool<PrototypeId>.Instance.Return(avatarStashes);
            ListPool<PrototypeId>.Instance.Return(craftingStashes);

            return itemsMoved;
        }

        private string CompactInventory(Player player)
        {
            Inventory generalInventory = player.GetInventory(InventoryConvenienceLabel.General);
            if (generalInventory == null) return "No general inventory found";

            var entityManager = player.Game.EntityManager;
            List<Item> itemsToSort = ListPool<Item>.Instance.Get();
            int itemsCompacted = 0;

            foreach (var entry in generalInventory)
            {
                Item item = entityManager.GetEntity<Item>(entry.Id);
                if (item != null && !item.IsEquipped)
                {
                    itemsToSort.Add(item);
                }
            }

            itemsToSort.Sort((a, b) =>
            {
                int categoryComparison = string.Compare(GetItemCategory(a), GetItemCategory(b), StringComparison.Ordinal);
                if (categoryComparison != 0) return categoryComparison;
                return string.Compare(GameDatabase.GetPrototypeName(a.PrototypeDataRef), GameDatabase.GetPrototypeName(b.PrototypeDataRef), StringComparison.Ordinal);
            });

            uint nextSlot = 0;
            foreach (Item item in itemsToSort)
            {
                bool needsBindingSkip = item.IsBoundToCharacter;

                if (needsBindingSkip)
                    item.SetStatus(EntityStatus.SkipItemBindingCheck, true);

                try
                {
                    // We directly move to the next available slot, not caring about the item's original position
                    if (item.InventoryLocation.Slot != nextSlot)
                    {
                        if (player.TryInventoryMove(item.Id, generalInventory.OwnerId, generalInventory.PrototypeDataRef, nextSlot))
                        {
                            itemsCompacted++;
                        }
                    }
                    nextSlot++;
                }
                finally
                {
                    if (needsBindingSkip)
                        item.SetStatus(EntityStatus.SkipItemBindingCheck, false);
                }
            }

            ListPool<Item>.Instance.Return(itemsToSort);
            return $"Compacted {itemsCompacted} items in your inventory.";
        }

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
                    if (targetItem != null && item.CanStackOnto(targetItem))
                    {
                        bool needsBindingSkip = item.IsBoundToCharacter;
                        if (needsBindingSkip)
                            item.SetStatus(EntityStatus.SkipItemBindingCheck, true);

                        try
                        {
                            if (player.TryInventoryMove(item.Id, stash.OwnerId, stash.PrototypeDataRef, entry.Slot))
                            {
                                return true;
                            }
                            return false;
                        }
                        finally
                        {
                            if (needsBindingSkip)
                                item.SetStatus(EntityStatus.SkipItemBindingCheck, false);
                        }
                    }
                }
            }
            return false;
        }

        private bool TryMoveToCategoryStash(Player player, Item item, List<PrototypeId> stashRefs)
        {
            string category = GetItemCategory(item);

            var validStashes = stashRefs.Where(stashRef =>
            {
                var inventoryProto = GameDatabase.GetPrototype<InventoryPrototype>(stashRef);
                return inventoryProto?.Category != InventoryCategory.PlayerStashAvatarSpecific;
            }).ToList();

            if (!_categoryStashMap.TryGetValue(category, out List<PrototypeId> categoryStashes))
            {
                categoryStashes = ListPool<PrototypeId>.Instance.Get();
                _categoryStashMap[category] = categoryStashes;
            }

            foreach (PrototypeId stashRef in categoryStashes)
            {
                Inventory stash = player.GetInventoryByRef(stashRef);
                if (GetInventoryFreeSlots(stash) > 0 && TryMoveItemToStash(player, item, stashRef, item.IsBoundToCharacter))
                {
                    return true;
                }
            }

            List<(PrototypeId Ref, int FreeSlots)> availableStashes = ListPool<(PrototypeId, int)>.Instance.Get();
            foreach (PrototypeId stashRef in validStashes)
            {
                if (categoryStashes.Contains(stashRef)) continue;

                Inventory stash = player.GetInventoryByRef(stashRef);
                if (stash == null) continue;

                int freeSlots = GetInventoryFreeSlots(stash);
                if (freeSlots == 0) continue;

                if (IsStashEmpty(stash))
                {
                    if (TryMoveItemToStash(player, item, stashRef, item.IsBoundToCharacter))
                    {
                        categoryStashes.Add(stashRef);
                        ListPool<(PrototypeId, int)>.Instance.Return(availableStashes);
                        return true;
                    }
                }
                else
                {
                    availableStashes.Add((stashRef, freeSlots));
                }
            }

            if (availableStashes.Count > 0)
            {
                availableStashes.Sort((a, b) => b.FreeSlots.CompareTo(a.FreeSlots));

                foreach (var (stashRef, _) in availableStashes)
                {
                    if (TryMoveItemToStash(player, item, stashRef, item.IsBoundToCharacter))
                    {
                        if (!categoryStashes.Contains(stashRef))
                            categoryStashes.Add(stashRef);
                        ListPool<(PrototypeId, int)>.Instance.Return(availableStashes);
                        return true;
                    }
                }
            }

            ListPool<(PrototypeId, int)>.Instance.Return(availableStashes);
            return false;
        }

        private bool TryMoveItemToStash(Player player, Item item, PrototypeId stashRef, bool skipBindingCheck)
        {
            Inventory stash = player.GetInventoryByRef(stashRef);
            if (stash == null || GetInventoryFreeSlots(stash) == 0) return false;

            uint targetSlot = stash.GetFreeSlot(item, true, true);
            if (targetSlot == Inventory.InvalidSlot) return false;

            bool needsBindingSkip = skipBindingCheck && item.IsBoundToCharacter;
            if (needsBindingSkip)
                item.SetStatus(EntityStatus.SkipItemBindingCheck, true);

            try
            {
                return player.TryInventoryMove(item.Id, stash.OwnerId, stash.PrototypeDataRef, targetSlot);
            }
            finally
            {
                if (needsBindingSkip)
                    item.SetStatus(EntityStatus.SkipItemBindingCheck, false);
            }
        }

        #region Helper Methods

        private string GetItemCategory(Item item)
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

        private int GetInventoryFreeSlots(Inventory inventory)
        {
            if (inventory == null) return 0;
            return inventory.GetCapacity() - inventory.Count;
        }

        private bool IsStashEmpty(Inventory stash)
        {
            return stash.Count == 0;
        }

        private void ClearCategoryStashMap()
        {
            foreach (var list in _categoryStashMap.Values)
            {
                ListPool<PrototypeId>.Instance.Return(list);
            }
            _categoryStashMap.Clear();
        }

        private string GenerateStashName(Inventory stash, string protoName)
        {
            if (protoName.Contains("PlayerStashForAvatar"))
            {
                return ExtractAvatarNameFromStash(protoName);
            }

            if (protoName.Contains("PlayerStashCrafting"))
            {
                int startIndex = protoName.IndexOf("PlayerStashCrafting") + "PlayerStashCrafting".Length;
                if (startIndex < protoName.Length)
                {
                    return $"Crafting{protoName.Substring(startIndex).Replace("/", "").Trim()}";
                }
                return "Crafting";
            }
            return stash.Category.ToString();
        }

        private string DecodeUrlString(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input.Replace("%20", " ").Replace("%2F", "/").Replace("%3A", ":").Replace("%2D", "-");
        }

        private string ExtractAvatarNameFromStash(string stashProtoName)
        {
            const string prefix = "PlayerStashForAvatar";
            if (stashProtoName.Contains(prefix))
            {
                int startIndex = stashProtoName.IndexOf(prefix) + prefix.Length;
                if (startIndex < stashProtoName.Length)
                {
                    return stashProtoName.Substring(startIndex).Replace("/", "").Trim();
                }
            }
            return string.Empty;
        }

        private bool IsItemSuitableForAvatar(Item item, string avatarName)
        {
            if (item == null || string.IsNullOrEmpty(avatarName)) return false;

            if (item.IsBoundToCharacter) return true;

            string itemProtoName = GameDatabase.GetPrototypeName(item.PrototypeDataRef).ToLowerInvariant();
            string baseAvatarName = avatarName.Replace(".prototype", "").ToLowerInvariant();

            if (itemProtoName.Contains($"/avatars/{baseAvatarName}/"))
            {
                return true;
            }

            if (AvatarNameVariations.TryGetValue(avatarName, out List<string> variations))
            {
                foreach (string variation in variations)
                {
                    if (itemProtoName.Contains(variation))
                    {
                        return true;
                    }
                }
            }

            if (itemProtoName.Contains(baseAvatarName))
            {
                if (baseAvatarName == "doom" && itemProtoName.Contains("drdoom")) return false;
                return true;
            }

            return false;
        }

        #endregion
    }
}