using Gazillion;
using MHServerEmu.Commands;
using MHServerEmu.Commands.Attributes;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.Frontend;
using MHServerEmu.Games;
using MHServerEmu.Games.Common;
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
using MHServerEmu.PlayerManagement.Players;
using MHServerEmu.PlayerManagement.Social;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MHServerEmu.Commands.Implementations
{
    [CommandGroup("player")]
    [CommandGroupDescription("Commands for managing player data for the invoker's account.")]
    public class PlayerCommands : CommandGroup
    {
        /// <summary>
        /// Safely bypasses internal protection levels to fetch a PlayerHandle using Reflection.
        /// </summary>
        private PlayerHandle GetPlayerHandle(ulong playerDbId)
        {
            try
            {
                object playerManager = null;
                var pmsType = typeof(PlayerManagerService);

                var instanceProp = pmsType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (instanceProp != null) playerManager = instanceProp.GetValue(null);

                if (playerManager == null) playerManager = ServerManager.Instance.GetGameService(GameServiceType.PlayerManager);

                if (playerManager == null) return null;

                object clientManager = null;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var clientManagerProp = playerManager.GetType().GetProperty("ClientManager", flags);

                if (clientManagerProp != null) clientManager = clientManagerProp.GetValue(playerManager);
                else
                {
                    var clientManagerField = playerManager.GetType().GetField("ClientManager", flags);
                    if (clientManagerField != null) clientManager = clientManagerField.GetValue(playerManager);
                }

                if (clientManager == null) return null;


                var getPlayerMethod = clientManager.GetType().GetMethod("GetPlayer", flags, null, new Type[] { typeof(ulong) }, null);

                return getPlayerMethod?.Invoke(clientManager, new object[] { playerDbId }) as PlayerHandle;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private PlayerConnection GetInvokerConnection(NetClient client)
        {
            return client as PlayerConnection;
        }

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

            PlayerConnection playerConnection = GetInvokerConnection(client);
            if (playerConnection == null) return "Error: Could not find your player connection.";
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
            PlayerConnection playerConnection = GetInvokerConnection(client);
            if (playerConnection == null) return "Error: Could not find your player connection.";
            Player player = playerConnection.Player;
            Avatar avatar = player.CurrentAvatar;

            if (avatar == null)
                return "Avatar is not available.";

            PrototypeId costumeProtoRef = PrototypeId.Invalid;
            string result;

            if (avatar.EquippedCostumeRef != (PrototypeId)HardcodedBlueprints.Costume)
            {
                costumeProtoRef = (PrototypeId)HardcodedBlueprints.Costume;
                result = "Applied fallback costume override.";
            }
            else
            {
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
            PlayerConnection playerConnection = GetInvokerConnection(client);
            if (playerConnection == null) return "Error: Could not find your player connection.";
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

            PlayerConnection playerConnection = GetInvokerConnection(client);
            if (playerConnection == null) return "Error: Could not find your player connection.";
            Player player = playerConnection.Player;

            foreach (PrototypeId currencyProtoRef in DataDirectory.Instance.IteratePrototypesInHierarchy<CurrencyPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                player.Properties.AdjustProperty(amount, new(PropertyEnum.Currency, currencyProtoRef));

            return $"Successfully given {amount} of all currencies.";
        }

        [Command("bring")]
        [CommandDescription("Brings a player to your current location safely.")]
        [CommandUsage("player bring [playerName]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string Bring(string[] @params, NetClient client)
        {
            PlayerConnection adminConnection = GetInvokerConnection(client);
            if (adminConnection == null) return "System Error: Could not find your player connection.";

            ulong adminDbId = adminConnection.PlayerDbId;
            string targetPlayerName = @params[0];

            if (string.Equals(adminConnection.Player.GetName(), targetPlayerName, StringComparison.OrdinalIgnoreCase))
                return "You cannot bring yourself.";

            if (!PlayerNameCache.Instance.TryGetPlayerDbId(targetPlayerName, out ulong targetDbId, out _))
                return $"Player '{targetPlayerName}' not found in the database.";

            PlayerHandle adminHandle = GetPlayerHandle(adminDbId);
            if (adminHandle == null) return "System Error: Reflection failed to grab your Admin Handle.";
            if (adminHandle.ActualRegion == null) return "Error: You must be fully loaded into a region to bring someone to you.";

            PlayerHandle targetHandle = GetPlayerHandle(targetDbId);
            if (targetHandle == null) return "System Error: Reflection failed to grab the Target Handle.";
            if (!targetHandle.IsConnected) return $"Player '{targetPlayerName}' is not currently online.";

            Avatar adminAvatar = adminConnection.Player?.CurrentAvatar;
            if (adminAvatar != null && adminAvatar.IsInWorld)
            {
                Vector3 safePos = GetSafeTeleportPosition(adminAvatar);

                PlayerConnection targetLocalConnection = targetHandle.Client as PlayerConnection;
                Avatar targetAvatar = targetLocalConnection?.Player?.CurrentAvatar;

                if (targetAvatar != null && adminHandle.ActualRegion == targetHandle.ActualRegion)
                {
                    targetAvatar.ChangeRegionPosition(safePos, null, ChangePositionFlags.Teleport);
                    return $"Bringing {targetPlayerName} safely to your exact location.";
                }
            }

            ulong requestingGameId = targetHandle.CurrentGame?.Id ?? 0;
            bool success = targetHandle.BeginRegionTransferToPlayer(requestingGameId, adminDbId);

            if (success)
                return $"Bringing {targetPlayerName} to your location.";
            else
                return $"Failed to bring {targetPlayerName}. The server rejected the transfer (they may be in a restricted state).";
        }

        [Command("goto")]
        [CommandDescription("Goes to a player's current location safely.")]
        [CommandUsage("player goto [playerName]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string GoTo(string[] @params, NetClient client)
        {
            PlayerConnection adminConnection = GetInvokerConnection(client);
            if (adminConnection == null) return "System Error: Could not find your player connection.";

            ulong adminDbId = adminConnection.PlayerDbId;
            string targetPlayerName = @params[0];

            if (string.Equals(adminConnection.Player.GetName(), targetPlayerName, StringComparison.OrdinalIgnoreCase))
                return "You cannot go to yourself.";

            if (!PlayerNameCache.Instance.TryGetPlayerDbId(targetPlayerName, out ulong targetDbId, out _))
                return $"Player '{targetPlayerName}' not found in the database.";

            PlayerHandle adminHandle = GetPlayerHandle(adminDbId);
            if (adminHandle == null) return "System Error: Reflection failed to grab your Admin Handle.";

            PlayerHandle targetHandle = GetPlayerHandle(targetDbId);
            if (targetHandle == null) return "System Error: Reflection failed to grab the Target Handle.";
            if (!targetHandle.IsConnected) return $"Player '{targetPlayerName}' is not currently online.";
            if (targetHandle.ActualRegion == null) return $"Player '{targetPlayerName}' is currently transitioning or in a lobby.";

            PlayerConnection targetLocalConnection = targetHandle.Client as PlayerConnection;
            Avatar targetAvatar = targetLocalConnection?.Player?.CurrentAvatar;

            if (targetAvatar != null && targetAvatar.IsInWorld)
            {
                Vector3 safePos = GetSafeTeleportPosition(targetAvatar);

                Avatar adminAvatar = adminConnection.Player?.CurrentAvatar;
                if (adminAvatar != null && adminHandle.ActualRegion == targetHandle.ActualRegion)
                {
                    adminAvatar.ChangeRegionPosition(safePos, null, ChangePositionFlags.Teleport);
                    return $"Teleporting safely to {targetPlayerName}'s location.";
                }

            }

            ulong requestingGameId = adminHandle.CurrentGame?.Id ?? 0;
            bool success = adminHandle.BeginRegionTransferToPlayer(requestingGameId, targetDbId);

            if (success)
                return $"Teleporting to {targetPlayerName}'s location.";
            else
                return $"Failed to teleport to {targetPlayerName}. The server rejected the transfer.";
        }

        [Command("kill")]
        [CommandDescription("Kills a specified player anywhere on the server.")]
        [CommandUsage("player kill [playerName]")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        [CommandParamCount(1)]
        public string Kill(string[] @params, NetClient client)
        {
            PlayerConnection adminConnection = GetInvokerConnection(client);
            if (adminConnection == null) return "Error: Could not find your player connection.";
            Player adminPlayer = adminConnection.Player;
            string targetPlayerName = @params[0];

            if (string.Equals(adminPlayer.GetName(), targetPlayerName, StringComparison.OrdinalIgnoreCase))
                return "You cannot use this command to kill yourself. Use '!player die' instead.";

            if (!PlayerNameCache.Instance.TryGetPlayerDbId(targetPlayerName, out ulong targetDbId, out _))
                return $"Player '{targetPlayerName}' not found.";

            PlayerHandle targetHandle = GetPlayerHandle(targetDbId);

            if (targetHandle == null || !targetHandle.IsConnected)
                return $"Player '{targetPlayerName}' is not currently online.";

            PlayerConnection targetConnection = targetHandle.Client as PlayerConnection;
            Player targetPlayer = targetConnection?.Player;
            Avatar targetAvatar = targetPlayer?.CurrentAvatar;

            if (targetAvatar == null || !targetAvatar.IsInWorld)
                return $"Player '{targetPlayerName}' does not have an active avatar in the world.";

            if (targetAvatar.IsDead)
                return $"Player '{targetPlayerName}' is already dead.";

            Avatar killerAvatar = adminPlayer.CurrentAvatar;
            ulong killerId = killerAvatar?.Id ?? adminPlayer.Id;

            PowerResults powerResults = new();
            powerResults.Init(killerId, killerId, targetAvatar.Id, targetAvatar.RegionLocation.Position, null, default, true);
            powerResults.SetFlag(PowerResultFlags.InstantKill, true);
            targetAvatar.ApplyDamageTransferPowerResults(powerResults);

            return $"Player '{targetPlayerName}' has been killed.";
        }

        [Command("clearconditions")]
        [CommandDescription("Clears persistent conditions.")]
        [CommandUsage("player clearconditions")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string ClearConditions(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = GetInvokerConnection(client);
            if (playerConnection == null) return "Error: Could not find your player connection.";
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

        [Command("vanish")]
        [CommandDescription("Makes an admin invisible to other players.")]
        [CommandUserLevel(AccountUserLevel.Admin)]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string Vanish(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = (PlayerConnection)client;
            Player player = playerConnection.Player;
            Avatar avatar = player.CurrentAvatar;

            if (avatar == null || !avatar.IsInWorld)
                return "You must have an active avatar in the world to use this command.";

            bool isVanished = player.IsVanished;
            player.IsVanished = !isVanished;

            if (!isVanished)
            {
                avatar.Properties[PropertyEnum.Stealth] = true;
                avatar.Properties[PropertyEnum.StealthDetection] = 10000; // High value to make detection very difficult
                foreach (var otherPlayer in new PlayerIterator(player.Game))
                {
                    if (otherPlayer != player)
                    {
                        otherPlayer.AOI.ConsiderEntity(player);
                        otherPlayer.AOI.ConsiderEntity(avatar);
                    }
                }
                return "You are now vanished.";
            }
            else
            {
                avatar.Properties.RemoveProperty(PropertyEnum.Stealth);
                avatar.Properties.RemoveProperty(PropertyEnum.StealthDetection);
                foreach (var otherPlayer in new PlayerIterator(player.Game))
                {
                    if (otherPlayer != player)
                    {
                        otherPlayer.AOI.ConsiderEntity(player);
                        otherPlayer.AOI.ConsiderEntity(avatar);
                    }
                }
                return "You are no longer vanished.";
            }
        }

        [Command("die")]
        [CommandDescription("Kills the current avatar.")]
        [CommandInvokerType(CommandInvokerType.Client)]
        public string Die(string[] @params, NetClient client)
        {
            PlayerConnection playerConnection = GetInvokerConnection(client);
            if (playerConnection == null) return "Error: Could not find your player connection.";

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

        /// <summary>
        /// Generates an expanding radial search around a target avatar to find a clear drop zone.
        /// Fully self-contained so other server owners can utilize the math locally.
        /// </summary>
        private Vector3 GetSafeTeleportPosition(Avatar targetAvatar, float maxRadius = 5.0f, float stepSize = 0.5f)
        {
            if (targetAvatar == null || !targetAvatar.IsInWorld)
                return Vector3.Zero;

            Vector3 center = targetAvatar.RegionLocation.Position;

            if (Avatar.AdjustStartPositionIfNeeded(targetAvatar.Region, ref center))
            {
                return center;
            }

            for (float radius = stepSize; radius <= maxRadius; radius += stepSize)
            {
                int pointsOnCircle = (int)(radius * 8);
                float angleStep = (float)(Math.PI * 2 / pointsOnCircle);

                for (int i = 0; i < pointsOnCircle; i++)
                {
                    float angle = i * angleStep;
                    float x = center.X + (float)Math.Cos(angle) * radius;
                    float z = center.Z + (float)Math.Sin(angle) * radius;

                    Vector3 candidate = new Vector3(x, center.Y, z);
                }
            }

            // Fallback to the exact center if absolutely no space was found within the max radius
            return center;
        }

     
    }
}