using MHServerEmu.Commands.Attributes;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Network;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Inventories;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Loot;
using MHServerEmu.Games.Network;
using MHServerEmu.Games.Properties;
using System.Diagnostics;
using System.Text;

namespace MHServerEmu.Commands.Implementations
{
    [CommandGroup("item")]
    [CommandGroupDescription("Commands for managing items.")]
    public class ItemCommands : CommandGroup
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

      

        [Command("give")]
        [CommandDescription("Creates and gives the specified item to the current player.")]
        [CommandUsage("item give [pattern] [count]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.ServerConsole)]
        [CommandParamCount(1)]
        public string Give(string[] @params, NetClient client)
        {
            PrototypeId itemProtoRef = CommandHelper.FindPrototype(HardcodedBlueprints.Item, @params[0], client);
            if (itemProtoRef == PrototypeId.Invalid) return string.Empty;

            if (@params.Length == 1 || int.TryParse(@params[1], out int count) == false)
                count = 1;

            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;

            LootManager lootGenerator = playerConnection.Game.LootManager;

            for (int i = 0; i < count; i++)
                lootGenerator.GiveItem(itemProtoRef, LootContext.CashShop, player);
            Logger.Debug($"GiveItem(): {itemProtoRef.GetName()}[{count}] to {player}");

            return string.Empty;
        }
     

        [Command("destroyindestructible")]
        [CommandDescription("Destroys indestructible items contained in the player's general inventory.")]
        [CommandUsage("item destroyindestructible")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string DestroyIndestructible(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;
            Inventory general = player.GetInventory(InventoryConvenienceLabel.General);

            List<Item> indestructibleItemList = new();
            foreach (var entry in general)
            {
                Item item = player.Game.EntityManager.GetEntity<Item>(entry.Id);
                if (item == null) continue;

                if (item.ItemPrototype.CanBeDestroyed == false)
                    indestructibleItemList.Add(item);
            }

            foreach (Item item in indestructibleItemList)
                item.Destroy();

            return $"Destroyed {indestructibleItemList.Count} indestructible items.";
        }

        [Command("roll")]
        [CommandDescription("Rolls the specified loot table.")]
        [CommandUsage("item roll [pattern]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string RollLootTable(string[] @params, NetClient client)
        {
            PrototypeId lootTableProtoRef = CommandHelper.FindPrototype(HardcodedBlueprints.LootTable, @params[0], client);
            if (lootTableProtoRef == PrototypeId.Invalid) return string.Empty;

            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;

            player.Game.LootManager.TestLootTable(lootTableProtoRef, player);

            return $"Finished rolling {lootTableProtoRef.GetName()}, see the server console for results.";
        }

        [Command("rollall")]
        [CommandDescription("Rolls all loot tables.")]
        [CommandUsage("item rollall")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string RollAllLootTables(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;

            int numLootTables = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();

            foreach (PrototypeId lootTableProtoRef in DataDirectory.Instance.IteratePrototypesInHierarchy<LootTablePrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
            {
                player.Game.LootManager.TestLootTable(lootTableProtoRef, player);
                numLootTables++;
            }

            stopwatch.Stop();

            return $"Finished rolling {numLootTables} loot tables in {stopwatch.Elapsed.TotalMilliseconds} ms, see the server console for results.";
        }

        [Command("creditchest")]
        [CommandDescription("Converts credits to a specified number of sellable chest items. Each chest costs 500k.")]
        [CommandUsage("item creditchest [count]")] // Optional count
        [CommandInvokerType(CommandInvokerType.Client)]
        public string CreditChest(string[] @params, NetClient client)
        {
            const PrototypeId CreditItemProtoRef = (PrototypeId)13983056721138685632; // Entity/Items/Crafting/Ingredients/CreditItem500k.prototype
            const int CreditItemPrice = 500000; // Cost per chest

            int requestedChests = 1; // Default to 1 chest

            if (@params.Length > 0)
            {
                if (!int.TryParse(@params[0], out requestedChests) || requestedChests <= 0)
                {
                    return "Invalid count specified. Please provide a positive number or omit for 1 chest.";
                }
            }

            PlayerConnection playerConnection = (PlayerConnection)client;
            if (playerConnection == null)
            {
                Logger.Error("CreditChest: PlayerConnection is null.");
                return "Error: Could not establish player connection.";
            }
            Player player = playerConnection.Player;
            if (player == null)
            {
                Logger.Error("CreditChest: Player entity is null.");
                return "Error: Could not retrieve player information.";
            }

            CurrencyPrototype creditsProto = GameDatabase.CurrencyGlobalsPrototype.CreditsPrototype;
            if (creditsProto == null)
            {
                Logger.Error("CreditChest: CreditsPrototype is null in CurrencyGlobalsPrototype. Cannot proceed.");
                return "Error: Server configuration issue with credits definition.";
            }
            PropertyId creditsProperty = new(PropertyEnum.Currency, creditsProto.DataRef);

            int chestsCreated = 0;
            long totalCreditsSpent = 0;

            for (int i = 0; i < requestedChests; i++)
            {
                PropertyValue currentCreditsPropVal = player.Properties[creditsProperty];
                long currentCredits = 0;

                // Attempt to cast/convert PropertyValue to long.
                // If PropertyValue is a default struct or doesn't hold a long, this will fail.
                try
                {
                    currentCredits = (long)currentCreditsPropVal;
                }
                catch (Exception ex) // Catch potential conversion errors (InvalidCastException or others)
                {
                    Logger.Error($"CreditChest: Could not convert credits PropertyValue to long for player {player.GetName()}. Value: '{currentCreditsPropVal}'. Error: {ex.Message}. Assuming 0 credits.");
                    currentCredits = 0;
                }

                if (currentCredits < CreditItemPrice)
                {
                    if (chestsCreated > 0)
                    {
                        return $"Created {chestsCreated} chest(s). Not enough credits for more (needed {CreditItemPrice:N0}, have {currentCredits:N0}).";
                    }
                    return $"You need at least {CreditItemPrice:N0} credits to create a chest. You have {currentCredits:N0}.";
                }

                player.Properties.AdjustProperty(-CreditItemPrice, creditsProperty);
                totalCreditsSpent += CreditItemPrice;

                player.Game.LootManager.GiveItem(CreditItemProtoRef, LootContext.CashShop, player);
                chestsCreated++;
                Logger.Trace($"CreditChest(): {player.GetName()} created chest #{i + 1}. Credits deducted: {CreditItemPrice}.");
            }

            PropertyValue finalCreditsPropVal = player.Properties[creditsProperty];
            long finalCredits = 0;
            try
            {
                finalCredits = (long)finalCreditsPropVal;
            }
            catch (Exception ex)
            {
                Logger.Error($"CreditChest: Could not convert final credits PropertyValue to long for player {player.GetName()}. Value: '{finalCreditsPropVal}'. Error: {ex.Message}. Assuming 0 credits for final display.");
                finalCredits = 0;
            }

            if (chestsCreated > 0)
            {
                return $"Successfully created {chestsCreated} Credit Chest(s). Total credits spent: {totalCreditsSpent:N0}. (Credits remaining: {finalCredits:N0})";
            }
            else
            {
                return "No credit chests were created (or an error occurred).";
            }
        }

        [Command("cleardeliverybox")]
        [CommandDescription("Destroys all items contained in the delivery box inventory.")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string ClearDeliveryBox(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;

            Inventory deliveryBox = player.GetInventory(InventoryConvenienceLabel.DeliveryBox);
            if (deliveryBox == null)
                return "Delivery box inventory not found.";

            int count = deliveryBox.Count;
            deliveryBox.DestroyContained();

            return $"Destroyed {count} items contained in the delivery box inventory.";
        }
    }
}
