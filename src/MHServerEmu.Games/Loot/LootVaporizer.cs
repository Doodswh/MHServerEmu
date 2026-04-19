using Gazillion;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Inventories;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.Entities.Options;
using MHServerEmu.Games.Events;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.LiveTuning;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.GameData.Tables;
using MHServerEmu.Games.Properties;
using MHServerEmu.Games.Regions;
using MHServerEmu.Core.VectorMath;
using System.Collections.Generic;
using MHServerEmu.Games.Loot.Specs;

namespace MHServerEmu.Games.Loot
{
    public static class LootVaporizer
    {
        private static readonly Logger Logger = LogManager.CreateLogger();
        public static readonly List<string> AutoLootWhitelist = new()
        {
            "AncientForgottenDevice", 
             "Cake",
        };

        public static bool ShouldVaporizeLootResult(Player player, in LootResult lootResult, PrototypeId avatarProtoRef)
        {
            if (player == null)
                return false;

            if (LiveTuningManager.GetLiveGlobalTuningVar(GlobalTuningVar.eGTV_LootVaporizationEnabled) == 0f)
                return false;

            switch (lootResult.Type)
            {
                case LootType.Item:
                    ItemPrototype itemProto = lootResult.ItemSpec?.ItemProtoRef.As<ItemPrototype>();
                    if (itemProto == null) return false;

                    //Armor/Weapons Logic (Sell Junk)
                    if (itemProto is ArmorPrototype armorProto)
                    {
                        AvatarPrototype avatarProto = avatarProtoRef.As<AvatarPrototype>();
                        if (avatarProto != null)
                        {
                            EquipmentInvUISlot slot = GameDataTables.Instance.EquipmentSlotTable.EquipmentUISlotForAvatar(armorProto, avatarProto);
                            PrototypeId vaporizeThresholdRarityProtoRef = player.GameplayOptions.GetArmorRarityVaporizeThreshold(slot);

                            if (vaporizeThresholdRarityProtoRef != PrototypeId.Invalid)
                            {
                                RarityPrototype rarityProto = lootResult.ItemSpec.RarityProtoRef.As<RarityPrototype>();
                                RarityPrototype vaporizeThresholdRarityProto = vaporizeThresholdRarityProtoRef.As<RarityPrototype>();

                                if (rarityProto != null && vaporizeThresholdRarityProto != null)
                                {
                                    if (rarityProto.Tier <= vaporizeThresholdRarityProto.Tier)
                                        return true; // Sell it
                                }
                            }
                        }
                    }

                    // Return FALSE for everything else so it flows to ProcessAutoPickup
                    return false;

                case LootType.Credits:
                    return player.GameplayOptions.GetOptionSetting(GameplayOptionSetting.EnableVaporizeCredits) == 1;

                case LootType.Currency:
                    return false;

                default:
                    return false;
            }
        }

        public static bool VaporizeLootResultSummary(Player player, LootResultSummary lootResultSummary, ulong sourceEntityId)
        {
            if (player == null) return false;

            List<ItemSpec> vaporizedItemSpecs = lootResultSummary.VaporizedItemSpecs;
            List<int> vaporizedCredits = lootResultSummary.VaporizedCredits;

            if (vaporizedItemSpecs.Count > 0 || vaporizedCredits.Count > 0)
            {
                NetMessageVaporizedLootResult.Builder resultMessageBuilder = NetMessageVaporizedLootResult.CreateBuilder();

                foreach (ItemSpec itemSpec in vaporizedItemSpecs)
                {
                    VaporizeItemSpec(player, itemSpec);
                    resultMessageBuilder.AddItems(NetStructVaporizedItem.CreateBuilder()
                        .SetItemProtoId((ulong)itemSpec.ItemProtoRef)
                        .SetRarityProtoId((ulong)itemSpec.RarityProtoRef));
                }

                foreach (int credits in vaporizedCredits)
                {
                    player.AcquireCredits(credits);
                    resultMessageBuilder.AddItems(NetStructVaporizedItem.CreateBuilder()
                        .SetCredits(credits));
                }

                resultMessageBuilder.SetSourceEntityId(sourceEntityId);
                player.SendMessage(resultMessageBuilder.Build());
            }

            return lootResultSummary.ItemSpecs.Count > 0 || lootResultSummary.AgentSpecs.Count > 0 || lootResultSummary.Credits.Count > 0 || lootResultSummary.Currencies.Count > 0;
        }

        public static void ProcessAutoPickup(Player player, LootResultSummary summary)
        {
            if (player == null || summary == null) return;

            // 1. Explicit Currencies (Specs)
            if (summary.Currencies.Count > 0)
            {
                for (int i = summary.Currencies.Count - 1; i >= 0; i--)
                {
                    CurrencySpec currency = summary.Currencies[i];

                    PrototypeId currencyRef = currency.CurrencyRef;
                    int amount = currency.Amount;

                    player.Properties[PropertyEnum.Currency, currencyRef] += amount;

                    CurrencyPrototype currencyProto = currencyRef.As<CurrencyPrototype>();
                    if (currencyProto != null)
                    {
                        player.GetRegion()?.CurrencyCollectedEvent.Invoke(new(player, currencyRef, player.Properties[PropertyEnum.Currency, currencyRef]));
                        player.OnScoringEvent(new(ScoringEventType.CurrencyCollected, currencyProto, amount));
                    }

                    summary.Currencies.RemoveAt(i);
                }
            }

            if (summary.ItemSpecs.Count > 0)
            {
                for (int i = summary.ItemSpecs.Count - 1; i >= 0; i--)
                {
                    ItemSpec itemSpec = summary.ItemSpecs[i];
                    ItemPrototype itemProto = itemSpec.ItemProtoRef.As<ItemPrototype>();
                    if (itemProto == null) continue;

                    bool hasCurrencyProperty = false;
                    foreach (var _ in itemProto.Properties.IteratePropertyRange(PropertyEnum.ItemCurrency))
                    {
                        hasCurrencyProperty = true;
                        break;
                    }

                    if (hasCurrencyProperty)
                    {
                        if (itemProto.GetCurrency(out PrototypeId currencyType, out int amount))
                        {
                            int totalAmount = amount * itemSpec.StackCount;

                            player.Properties[PropertyEnum.Currency, currencyType] += totalAmount;

                            CurrencyPrototype currencyProto = currencyType.As<CurrencyPrototype>();
                            if (currencyProto != null)
                            {
                                player.GetRegion()?.CurrencyCollectedEvent.Invoke(new(player, currencyType, player.Properties[PropertyEnum.Currency, currencyType]));
                                player.OnScoringEvent(new(ScoringEventType.CurrencyCollected, currencyProto, totalAmount));
                            }

                            summary.ItemSpecs.RemoveAt(i);
                        }
                    }
                }
            }

            if (summary.AgentSpecs.Count > 0)
            {
                for (int i = summary.AgentSpecs.Count - 1; i >= 0; i--)
                {
                    AgentSpec agentSpec = summary.AgentSpecs[i];
                    WorldEntityPrototype agentProto = agentSpec.AgentProtoRef.As<WorldEntityPrototype>();
                    if (agentProto == null) continue;

                    bool hasCurrencyProperty = false;
                    foreach (var _ in agentProto.Properties.IteratePropertyRange(PropertyEnum.ItemCurrency))
                    {
                        hasCurrencyProperty = true;
                        break;
                    }

                    if (hasCurrencyProperty)
                    {
                        if (agentProto.GetCurrency(out PrototypeId currencyType, out int amount))
                        {
                            player.Properties[PropertyEnum.Currency, currencyType] += amount;

                            CurrencyPrototype currencyProto = currencyType.As<CurrencyPrototype>();
                            if (currencyProto != null)
                            {
                                player.GetRegion()?.CurrencyCollectedEvent.Invoke(new(player, currencyType, player.Properties[PropertyEnum.Currency, currencyType]));
                                player.OnScoringEvent(new(ScoringEventType.CurrencyCollected, currencyProto, amount));
                            }

                            summary.AgentSpecs.RemoveAt(i);
                        }
                    }
                }
            }

            if (summary.ItemSpecs.Count > 0)
            {
                for (int i = summary.ItemSpecs.Count - 1; i >= 0; i--)
                {
                    ItemSpec itemSpec = summary.ItemSpecs[i];
                    ItemPrototype itemProto = itemSpec.ItemProtoRef.As<ItemPrototype>();
                    if (itemProto == null) continue;

                    string internalName = itemProto.DataRef.GetName();
                    if (string.IsNullOrEmpty(internalName)) continue;

                    bool isWhitelisted = false;
                    foreach (string whiteListedText in AutoLootWhitelist)
                    {
                        if (internalName.Contains(whiteListedText, StringComparison.OrdinalIgnoreCase))
                        {
                            isWhitelisted = true;
                            break;
                        }
                    }

                    if (isWhitelisted)
                    {
                        Inventory mainInventory = player.GetInventory(InventoryConvenienceLabel.General);
                        if (mainInventory == null) continue;

                        using EntitySettings settings = ObjectPoolManager.Instance.Get<EntitySettings>();
                        settings.EntityRef = itemSpec.ItemProtoRef;
                        settings.ItemSpec = itemSpec;

                        Item newItem = player.Game.EntityManager.CreateEntity(settings) as Item;
                        if (newItem == null) continue;

                        newItem.Properties[PropertyEnum.InventoryStackCount] = itemSpec.StackCount;

                        bool wasAdded = false;
                        ulong? stackEntityId = 0;
                        InventoryResult stackResult = InventoryResult.Invalid;
                        InventoryResult slotResult = InventoryResult.Invalid;

                        uint slot = mainInventory.GetAutoStackSlot(newItem, true);
                        if (slot != Inventory.InvalidSlot)
                        {
                            stackResult = newItem.ChangeInventoryLocation(mainInventory, slot, ref stackEntityId, true);
                            if (stackResult == InventoryResult.Success) wasAdded = true;
                        }

                        if (wasAdded == false)
                        {
                            slotResult = newItem.ChangeInventoryLocation(mainInventory, Inventory.InvalidSlot, ref stackEntityId, true);
                            if (slotResult == InventoryResult.Success) wasAdded = true;
                        }

                        if (wasAdded)
                        {
                            player.OnScoringEvent(new(ScoringEventType.ItemCollected, itemProto, itemSpec.RarityProtoRef.As<Prototype>(), itemSpec.StackCount));

                            if (stackEntityId.HasValue && stackEntityId.Value != 0)
                            {
                                Item stackEntity = player.Game.EntityManager.GetEntity<Item>(stackEntityId.Value);
                                if (stackEntity != null)
                                {
                                    stackEntity.ChangeInventoryLocation(mainInventory, slot);
                                }
                            }
                            else
                            {
                                newItem.SetRecentlyAdded(true);
                            }

                            summary.ItemSpecs.RemoveAt(i);
                        }
                        else
                        {
                            newItem.Destroy();
                        }
                    }
                }
            }
        }

        public static bool VaporizeItemSpec(Player player, ItemSpec itemSpec)
        {
            Avatar avatar = player.CurrentAvatar;
            if (avatar == null) return Logger.WarnReturn(false, "VaporizeItemSpec(): avatar == null");

            ItemPrototype itemProto = itemSpec.ItemProtoRef.As<ItemPrototype>();
            if (itemProto == null) return Logger.WarnReturn(false, "VaporizeItemSpec(): itemProto == null");

            Inventory petItemInv = avatar.GetInventory(InventoryConvenienceLabel.PetItem);
            if (petItemInv == null) return Logger.WarnReturn(false, "VaporizeItemSpec(): petItemInv == null");

            Item petTechItem = player.Game.EntityManager.GetEntity<Item>(petItemInv.GetEntityInSlot(0));
            if (petTechItem != null)
                return ItemPrototype.DonateItemToPetTech(player, petTechItem, itemSpec);

            int sellPrice = itemProto.Cost.GetNoStackSellPriceInCredits(player, itemSpec, null) * itemSpec.StackCount;
            int vaporizeCredits = MathHelper.RoundUpToInt(sellPrice * (float)avatar.Properties[PropertyEnum.VaporizeSellPriceMultiplier]);

            vaporizeCredits += Math.Max(MathHelper.RoundUpToInt(sellPrice * (float)avatar.Properties[PropertyEnum.PetTechDonationMultiplier]), 1);

            player.AcquireCredits(vaporizeCredits);
            player.OnScoringEvent(new(ScoringEventType.ItemCollected, itemSpec.ItemProtoRef.As<Prototype>(), itemSpec.RarityProtoRef.As<Prototype>(), itemSpec.StackCount));
            return true;
        }
    }
}