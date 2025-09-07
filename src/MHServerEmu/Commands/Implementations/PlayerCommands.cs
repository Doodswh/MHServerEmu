using Gazillion;
using MHServerEmu.Commands.Attributes;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.Frontend;
using MHServerEmu.Games;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Inventories;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Calligraphy;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Network;
using MHServerEmu.Games.Network.InstanceManagement;
using MHServerEmu.Games.Powers;
using MHServerEmu.Games.Powers.Conditions;
using MHServerEmu.Games.Properties;
using MHServerEmu.Games.Regions;
using MHServerEmu.Grouping;
using MHServerEmu.PlayerManagement;
using System.Linq;

namespace MHServerEmu.Commands.Implementations
{
    [CommandGroup("player")]
    [CommandGroupDescription("Commands for managing player data for the invoker's account.")]
    public class PlayerCommands : CommandGroup
    {
        [Command("costume")]
        [CommandDescription("Changes costume for the current avatar.")]
        [CommandUsage("player costume [name|reset]")]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string Costume(string[] @params, NetClient client)
        {
            PrototypeId costumeProtoRef;

            switch (@params[0].ToLower())
            {
                case "reset":
                    costumeProtoRef = PrototypeId.Invalid;
                    break;

                default:
                    var matches = GameDatabase.SearchPrototypes(@params[0], DataFileSearchFlags.SortMatchesByName | DataFileSearchFlags.CaseInsensitive, HardcodedBlueprints.Costume);

                    if (matches.Any() == false)
                        return $"Failed to find any costumes containing {@params[0]}.";

                    if (matches.Count() > 1)
                    {
                        CommandHelper.SendMessage(client, $"Found multiple matches for {@params[0]}:");
                        CommandHelper.SendMessages(client, matches.Select(match => GameDatabase.GetPrototypeName(match)), false);
                        return string.Empty;
                    }

                    costumeProtoRef = matches.First();
                    break;
            }

            PlayerConnection playerConnection = (PlayerConnection)client;
            var player = playerConnection.Player;
            var avatar = player.CurrentAvatar;

            avatar.ChangeCostume(costumeProtoRef);

            if (costumeProtoRef == PrototypeId.Invalid)
                return "Resetting costume.";

            return $"Changing costume to {GameDatabase.GetPrototypeName(costumeProtoRef)}.";
        }

        [Command("disablevu")]
        [CommandDescription("Forces the fallback costume for the current hero, reverting visual updates in some cases.")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string DisableVU(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;
            Avatar avatar = player.CurrentAvatar;

            if (avatar == null)
                return "Avatar is not available.";

            PrototypeId costumeProtoRef = PrototypeId.Invalid;
            string result;

            if (avatar.EquippedCostumeRef != (PrototypeId)HardcodedBlueprints.Costume)
            {
                // Apply fallback costume override
                costumeProtoRef = (PrototypeId)HardcodedBlueprints.Costume;
                result = "Applied fallback costume override.";
            }
            else
            {
                // Revert fallback costume override if it is currently applied
                Inventory costumeInv = avatar.GetInventory(InventoryConvenienceLabel.Costume);
                if (costumeInv != null && costumeInv.Count > 0)
                {
                    Item costume = avatar.Game.EntityManager.GetEntity<Item>(costumeInv.GetAnyEntity());
                    if (costume != null)
                        costumeProtoRef = costume.PrototypeDataRef;
                }

                result = "Reverted fallback costume override.";
            }

            avatar.ChangeCostume(costumeProtoRef);
            return result;
        }

        [Command("wipe")]
        [CommandDescription("Wipes all progress associated with the current account.")]
        [CommandUsage("player wipe [playerName]")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string Wipe(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            string playerName = playerConnection.Player.GetName();

            if (@params.Length == 0)
                return $"Type '!player wipe {playerName}' to wipe all progress associated with this account.\nWARNING: THIS ACTION CANNOT BE REVERTED.";

            if (string.Equals(playerName, @params[0], StringComparison.OrdinalIgnoreCase) == false)
                return "Incorrect player name.";

            playerConnection.WipePlayerData();
            return string.Empty;
        }

        [Command("givecurrency")]
        [CommandDescription("Gives all currencies.")]
        [CommandUsage("player givecurrency [amount]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string GiveCurrency(string[] @params, NetClient client)
        {
            if (int.TryParse(@params[0], out int amount) == false)
                return $"Failed to parse amount from {@params[0]}.";

            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;

            foreach (PrototypeId currencyProtoRef in DataDirectory.Instance.IteratePrototypesInHierarchy<CurrencyPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                player.Properties.AdjustProperty(amount, new(PropertyEnum.Currency, currencyProtoRef));

            return $"Successfully given {amount} of all currencies.";
        }

        [Command("kill")]
        [CommandDescription("Kills a specified player anywhere on the server.")]
        [CommandUsage("player kill [playerName]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string Kill(string[] @params, NetClient client)
        {
            PlayerConnection adminConnection = (PlayerConnection)client;
            Player adminPlayer = adminConnection.Player;
            GameManager gameManager = adminConnection.Game.GameManager;
            string targetPlayerName = @params[0];

            if (string.Equals(adminPlayer.GetName(), targetPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                return "You cannot use this command to kill yourself. Use '!player die' instead.";
            }

            PlayerConnection targetConnection = null;

            // Find the player's connection across all game instances.
            // We find them first and then act, to avoid modifying a collection while iterating over it.
            foreach (Game game in gameManager.GetGames())
            {
                foreach (var connection in game.NetworkManager)
                {
                    if (connection.Player != null && string.Equals(connection.Player.GetName(), targetPlayerName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetConnection = connection;
                        break; // Found the connection, exit inner loop
                    }
                }
                if (targetConnection != null)
                {
                    break; // Found the connection, exit outer loop
                }
            }

            if (targetConnection == null)
            {
                return $"Player '{targetPlayerName}' not found online on this server instance.";
            }

            // Now that we're outside the loops, it's safe to perform the kill action.
            Player targetPlayer = targetConnection.Player;
            Avatar targetAvatar = targetPlayer.CurrentAvatar;

            if (targetAvatar == null || !targetAvatar.IsInWorld)
            {
                return $"Player '{targetPlayerName}' does not have an active avatar in the world.";
            }

            if (targetAvatar.IsDead)
            {
                return $"Player '{targetPlayerName}' is already dead.";
            }

            Avatar killerAvatar = adminPlayer.CurrentAvatar;
            ulong killerId = killerAvatar?.Id ?? adminPlayer.Id;

            PowerResults powerResults = new();
            powerResults.Init(killerId, killerId, targetAvatar.Id, targetAvatar.RegionLocation.Position, null, default, true);
            powerResults.SetFlag(PowerResultFlags.InstantKill, true);
            targetAvatar.ApplyDamageTransferPowerResults(powerResults);

            return $"Player '{targetPlayerName}' has been killed.";
        }
        [Command("bring")]
        [CommandDescription("Brings a player to your current location or region entry point.")]
        [CommandUsage("player bring [playerName]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string Bring(string[] @params, NetClient client)
        {
            PlayerConnection adminConnection = (PlayerConnection)client;
            Player adminPlayer = adminConnection.Player;
            Avatar adminAvatar = adminPlayer.CurrentAvatar;

            if (adminAvatar == null || !adminAvatar.IsInWorld || adminAvatar.Region == null)
            {
                return "You must be in a valid region to use this command.";
            }

            string targetPlayerName = @params[0];
            if (string.Equals(adminPlayer.GetName(), targetPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                return "You cannot bring yourself.";
            }

            GameManager gameManager = adminConnection.Game.GameManager;
            PlayerConnection targetConnection = null;

            // Find the target player across all game instances
            foreach (var game in gameManager.GetGames())
            {
                foreach (var connection in game.NetworkManager)
                {
                    if (connection.Player != null && string.Equals(connection.Player.GetName(), targetPlayerName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetConnection = connection;
                        break;
                    }
                }
                if (targetConnection != null) break;
            }

            if (targetConnection == null)
            {
                return $"Player '{targetPlayerName}' not found online.";
            }

            Player targetPlayer = targetConnection.Player;
            Avatar targetAvatar = targetPlayer.CurrentAvatar;

            if (targetAvatar == null || !targetAvatar.IsInWorld)
            {
                return $"Player '{targetPlayerName}' is not in a state to be teleported.";
            }

            var adminRegion = adminAvatar.Region;
            var adminPosition = adminAvatar.RegionLocation.Position;

            if (targetAvatar.Region?.Id == adminRegion.Id)
            {
                targetAvatar.ChangeRegionPosition(adminPosition, null, ChangePositionFlags.Teleport);
                return $"Brought {targetPlayerName} to your location.";
            }

            using (var teleporter = ObjectPoolManager.Instance.Get<Teleporter>())
            {
                teleporter.Initialize(targetPlayer, TeleportContextEnum.TeleportContext_Debug);
                teleporter.TeleportToRegionLocation(adminRegion.Id, adminPosition);
            }

            return $"Bringing {targetPlayerName} to your location.";
        }

        [Command("goto")]
        [CommandDescription("Goes to a player's current location.")]
        [CommandUsage("player goto [playerName]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string GoTo(string[] @params, NetClient client)
        {
            PlayerConnection adminConnection = (PlayerConnection)client;
            Player adminPlayer = adminConnection.Player;
            Avatar adminAvatar = adminPlayer.CurrentAvatar;

            if (adminAvatar == null || !adminAvatar.IsInWorld)
            {
                return "You must have an active avatar to use this command.";
            }

            string targetPlayerName = @params[0];
            if (string.Equals(adminPlayer.GetName(), targetPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                return "You cannot go to yourself.";
            }

            GameManager gameManager = adminConnection.Game.GameManager;
            PlayerConnection targetConnection = null;

            // Find the target player across all game instances
            foreach (var game in gameManager.GetGames())
            {
                foreach (var connection in game.NetworkManager)
                {
                    if (connection.Player != null && string.Equals(connection.Player.GetName(), targetPlayerName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetConnection = connection;
                        break;
                    }
                }
                if (targetConnection != null) break;
            }

            if (targetConnection == null)
            {
                return $"Player '{targetPlayerName}' not found online.";
            }

            Player targetPlayer = targetConnection.Player;
            Avatar targetAvatar = targetPlayer.CurrentAvatar;

            if (targetAvatar == null || !targetAvatar.IsInWorld || targetAvatar.Region == null)
            {
                return $"Player '{targetPlayerName}' is not in a location that can be teleported to.";
            }

            var targetRegion = targetAvatar.Region;
            var targetPosition = targetAvatar.RegionLocation.Position;

            if (adminAvatar.Region?.Id == targetRegion.Id)
            {
                adminAvatar.ChangeRegionPosition(targetPosition, null, ChangePositionFlags.Teleport);
                return $"Teleported to {targetPlayerName}'s location.";
            }

            using (var teleporter = ObjectPoolManager.Instance.Get<Teleporter>())
            {
                teleporter.Initialize(adminPlayer, TeleportContextEnum.TeleportContext_Debug);
                teleporter.TeleportToRegionLocation(targetRegion.Id, targetPosition);
            }

            return $"Teleporting to {targetPlayerName}'s location.";
        }

        [Command("clearconditions")]
        [CommandDescription("Clears persistent conditions.")]
        [CommandUsage("player clearconditions")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string ClearConditions(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;
            Avatar avatar = player.CurrentAvatar;

            int count = 0;

            foreach (Condition condition in avatar.ConditionCollection)
            {
                if (condition.IsPersistToDB() == false)
                    continue;

                avatar.ConditionCollection.RemoveCondition(condition.Id);
                count++;
            }

            return $"Cleared {count} persistent conditions.";
        }

        [Command("die")]
        [CommandDescription("Kills the current avatar.")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string Die(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;

            Avatar avatar = playerConnection.Player.CurrentAvatar;
            if (avatar == null || avatar.IsInWorld == false)
                return "Avatar not found.";

            if (avatar.IsDead)
                return "You are already dead.";

            PowerResults powerResults = new();
            powerResults.Init(avatar.Id, avatar.Id, avatar.Id, avatar.RegionLocation.Position, null, default, true);
            powerResults.SetFlag(PowerResultFlags.InstantKill, true);
            avatar.ApplyDamageTransferPowerResults(powerResults);

            return $"You are now dead. Thank you for using Stop-and-Drop.";
        }
    }
}
