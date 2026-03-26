using MHServerEmu.Commands.Attributes;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.RateLimiting;
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
    [CommandGroupUserLevel(AccountUserLevel.Admin)]
    public class StashCommands : CommandGroup
    {
        private static readonly Logger Logger = LogManager.CreateLogger();
        private readonly Dictionary<string, List<PrototypeId>> _categoryStashMap = new();
        private static readonly SemaphoreSlim _moveItemsConcurrencyLimit = new(initialCount: 20, maxCount: 20);


        private static readonly Dictionary<string, List<string>> AvatarNameVariations = new(StringComparer.OrdinalIgnoreCase)
        {
            { "DrDoom.prototype", new List<string> { "doctordoom", "drdoom", "doom" } },
            { "DrStrange.prototype", new List<string> { "doctorstrange", "drstrange" } },
            { "Spiderman.prototype", new List<string> { "spiderman", "spider-man" } },
            { "StarLord.prototype", new List<string> { "starlord", "star-lord" } },
            { "InvisiWoman.prototype", new List<string> { "invisiblewoman", "invisiwoman" } },
        };

        [Command("Moveitems")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string MoveItems(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            if (playerConnection?.Player == null) return "Invalid player connection";

            if (!_moveItemsConcurrencyLimit.Wait(0))
                return "Too many players are sorting stashes right now. Please try again in a moment.";

            Player player = playerConnection.Player;
            Logger.Info($"[ItemMove] Starting automatic Move operation for player {player.Id}");

            Dictionary<string, List<PrototypeId>> categoryStashMap = new();
            List<PrototypeId> allStashRefs = ListPool<PrototypeId>.Instance.Get();

            try
            {
                if (!player.GetStashInventoryProtoRefs(allStashRefs, false, true))
                    return "No stash tabs available";

                int itemsMoved = SortItems(player, allStashRefs, categoryStashMap);
                return $"Moved {itemsMoved} items across all stash tabs.";
            }
            finally
            {
                _moveItemsConcurrencyLimit.Release();

                foreach (var list in categoryStashMap.Values)
                    ListPool<PrototypeId>.Instance.Return(list);
                categoryStashMap.Clear();

                ListPool<PrototypeId>.Instance.Return(allStashRefs);
            }
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

        [Command("playersort")]
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
                    ulong? stackEntityId = null;
                    InventoryResult result = Inventory.ChangeEntityInventoryLocation(
                        item,
                        generalInventory,
                        Inventory.InvalidSlot,
                        ref stackEntityId,
                        true
                    );

                    if (result == InventoryResult.Success)
                    {
                        itemsRepaired++;
                        Logger.Info($"[STASH-REPAIR] Moved {GameDatabase.GetPrototypeName(item.PrototypeDataRef)} back to general inventory");
                    }
                }
            }

            return $"Repaired {itemsRepaired} items from stash '{stashName}' back to general inventory";
        }

        private int SortItems(Player player, List<PrototypeId> stashRefs, Dictionary<string, List<PrototypeId>> categoryStashMap)
        {
            Inventory generalInventory = player.GetInventory(InventoryConvenienceLabel.General);
            if (generalInventory == null) return 0;

            var entityManager = player.Game.EntityManager;
            using var allItemIdsHandle = ListPool<ulong>.Instance.Get(out List<ulong> allItemIds);

            foreach (var entry in generalInventory)
            {
                allItemIds.Add(entry.Id);
            }

            int itemsMoved = 0;

            foreach (ulong itemId in allItemIds)
            {
                Item item = entityManager.GetEntity<Item>(itemId);
                if (item == null || item.IsEquipped) continue;

                bool moved = false;


                if (TryMoveToCategoryStash(player, item, stashRefs, categoryStashMap))
                {
                    moved = true;
                }
                else
                {
                    foreach (var stashRef in stashRefs)
                    {
                        if (TryMoveItemToStash(player, item, stashRef))
                        {
                            moved = true;
                            break;
                        }
                    }
                }

                if (moved) itemsMoved++;
            }

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
                if (item.InventoryLocation.Slot != nextSlot)
                {
                    ulong? stackEntityId = null;

                    InventoryResult result = Inventory.ChangeEntityInventoryLocation(
                        item,
                        generalInventory,
                        nextSlot,
                        ref stackEntityId,
                        false
                    );

                    if (result == InventoryResult.Success)
                    {
                        itemsCompacted++;
                    }
                    else
                    {
                        Logger.Warn($"[STASH-COMPACT] Failed to move {item.Id} to slot {nextSlot}: {result}");
                    }
                }
                nextSlot++;
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

        private bool TryMoveToCategoryStash(Player player, Item item, List<PrototypeId> stashRefs, Dictionary<string, List<PrototypeId>> categoryStashMap)
        {
            string category = GetItemCategory(item);

            var validStashes = stashRefs.Where(stashRef =>
            {
                var inventoryProto = GameDatabase.GetPrototype<InventoryPrototype>(stashRef);
                return inventoryProto?.Category != InventoryCategory.PlayerStashAvatarSpecific;
            }).ToList();

            if (!categoryStashMap.TryGetValue(category, out List<PrototypeId> categoryStashes))
            {
                categoryStashes = ListPool<PrototypeId>.Instance.Get();
                categoryStashMap[category] = categoryStashes;
            }

            foreach (PrototypeId stashRef in categoryStashes)
            {
                Inventory stash = player.GetInventoryByRef(stashRef);
                // Using built-in CapacityRemaining property
                if (stash != null && stash.CapacityRemaining > 0 && TryMoveItemToStash(player, item, stashRef))
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

                int freeSlots = stash.CapacityRemaining; // Using built-in property
                if (freeSlots == 0) continue;

                if (IsStashEmpty(stash))
                {
                    if (TryMoveItemToStash(player, item, stashRef))
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
                    if (TryMoveItemToStash(player, item, stashRef))
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

        private bool TryMoveItemToStash(Player player, Item item, PrototypeId stashRef)
        {
            Inventory stash = player.GetInventoryByRef(stashRef);

            if (stash == null || stash.CapacityRemaining <= 0) return false;

            ulong? stackEntityId = null;

          
            InventoryResult result = Inventory.ChangeEntityInventoryLocation(
                item,
                stash,
                Inventory.InvalidSlot,
                ref stackEntityId,
                true // Allow stacking
            );

            if (result == InventoryResult.Success)
            {
         
                item.UpdateInterestPolicies(true);

                if (stackEntityId.HasValue)
                    Logger.Info($"[STASH-DEBUG] {item.Id} merged into stack {stackEntityId.Value}.");
                else
                    Logger.Info($"[STASH-DEBUG] {item.Id} moved to new slot in {stashRef}.");

                return true;
            }

         
            Logger.Warn($"[STASH-FAIL] Item {item.Id} rejected by {stashRef}. Reason: {result}");
            return false;
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