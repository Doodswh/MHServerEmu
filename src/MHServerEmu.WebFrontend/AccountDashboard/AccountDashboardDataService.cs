using Google.ProtocolBuffers;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Network;
using MHServerEmu.Core.Serialization;
using MHServerEmu.DatabaseAccess;
using MHServerEmu.DatabaseAccess.Models;
using MHServerEmu.Games;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.Locales;
using MHServerEmu.Games.Network;
using MHServerEmu.Games.Network.InstanceManagement;
using MHServerEmu.PlayerManagement.Players;
using System.Reflection;

namespace MHServerEmu.WebFrontend.AccountDashboard
{
    public static class AccountDashboardDataService
    {
        private static long _inspectionGameId = long.MinValue;

        public static bool TryBuildResponse(string email, out object response, out string message)
        {
            response = null;
            message = null;

            if (AccountManager.TryGetAccountByEmail(email, out DBAccount account) == false)
            {
                message = "Account not found.";
                return false;
            }

            if (AccountManager.LoadPlayerDataForAccount(account) == false || account.Player == null)
            {
                message = "Unable to load account data.";
                return false;
            }

            Game game = null;

            try
            {
                game = new Game(GetNextInspectionGameId(), new GameManager(null));
                using (new GameCurrentScope(game))
                {
                    Player player = BuildPlayer(game, account);
                    if (player == null)
                    {
                        message = "Unable to restore player data.";
                        return false;
                    }

                    List<CharacterSummary> characters = new();
                    long totalTimePlayed = 0;
                    int highestLevel = 0;

                    foreach (Avatar avatar in new AvatarIterator(player, AvatarIteratorMode.IncludeArchived))
                    {
                        long timePlayedSeconds = (long)Math.Max(0, Math.Floor(avatar.GetTimePlayed().TotalSeconds));
                        int level = avatar.CharacterLevel;

                        characters.Add(new CharacterSummary
                        {
                            CharacterId = avatar.DatabaseUniqueId.ToString(),
                            Name = GetAvatarName(avatar),
                            PrototypeName = avatar.PrototypeName,
                            Level = level,
                            TimePlayed = timePlayedSeconds,
                        });

                        totalTimePlayed += timePlayedSeconds;
                        highestLevel = Math.Max(highestLevel, level);
                    }

                    List<CharacterSummary> orderedCharacters = characters
                        .OrderByDescending(character => character.TimePlayed)
                        .ThenBy(character => character.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    response = new
                    {
                        account = new
                        {
                            email = account.Email,
                            playerName = account.PlayerName,
                            accountId = account.Id.ToString(),
                            userLevel = account.UserLevel.ToString(),
                            flags = account.Flags.ToString(),
                            lastKnownMachineId = string.IsNullOrWhiteSpace(account.LastKnownMachineId) ? null : account.LastKnownMachineId,
                            lastLogout = FormatUnixMilliseconds(account.Player.LastLogoutTime),
                        },
                        stats = new
                        {
                            totalTimePlayed,
                            totalCharacters = orderedCharacters.Count,
                            highestLevel,
                        },
                        characters = orderedCharacters.Select(character => new
                        {
                            characterId = character.CharacterId,
                            name = character.Name,
                            prototypeName = character.PrototypeName,
                            level = character.Level,
                            timePlayed = character.TimePlayed,
                        }).ToList(),
                    };
                }

                return true;
            }
            finally
            {
                if (game != null)
                    CleanupInspectionGame(game);
            }
        }

        private static Player BuildPlayer(Game game, DBAccount account)
        {
            FakeFrontendClient frontendClient = new(account);
            PlayerConnection playerConnection = new(game, frontendClient);

            if (playerConnection.Initialize() == false)
                return null;

            return playerConnection.Player;
        }

        private static string GetAvatarName(Avatar avatar)
        {
            string localizedName = null;
            Locale locale = LocaleManager.Instance.CurrentLocale;
            if (locale != null && avatar.AvatarPrototype != null)
                localizedName = locale.GetLocaleString(avatar.AvatarPrototype.DisplayName);

            return string.IsNullOrWhiteSpace(localizedName) ? avatar.PrototypeName : localizedName;
        }

        private static string FormatUnixMilliseconds(long value)
        {
            if (value <= 0)
                return null;

            return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime.ToString("O");
        }

        private static ulong GetNextInspectionGameId()
        {
            return unchecked((ulong)Interlocked.Increment(ref _inspectionGameId));
        }

        private static void CleanupInspectionGame(Game game)
        {
            try
            {
                game.EntityManager.ProcessDeferredLists();
            }
            catch
            {
            }
        }

        private sealed class CharacterSummary
        {
            public string CharacterId { get; init; }
            public string Name { get; init; }
            public string PrototypeName { get; init; }
            public int Level { get; init; }
            public long TimePlayed { get; init; }
        }

        private sealed class GameCurrentScope : IDisposable
        {
            private static readonly FieldInfo CurrentField = typeof(Game).GetField("Current", BindingFlags.Static | BindingFlags.NonPublic);

            private readonly object _previousValue;

            public GameCurrentScope(Game game)
            {
                _previousValue = CurrentField?.GetValue(null);
                CurrentField?.SetValue(null, game);
            }

            public void Dispose()
            {
                CurrentField?.SetValue(null, _previousValue);
            }
        }

        private sealed class FakeFrontendClient : IFrontendClient, IDBAccountOwner
        {
            public bool IsConnected { get; private set; } = true;
            public IFrontendSession Session { get; }
            public ulong DbId { get; }
            public DBAccount Account { get; }

            public FakeFrontendClient(DBAccount account)
            {
                Account = account;
                DbId = (ulong)account.Id;
                Session = new FakeFrontendSession(account);
            }

            public void Disconnect()
            {
                IsConnected = false;
            }

            public void SuspendReceiveTimeout() { }
            public bool AssignSession(IFrontendSession session) => true;
            public bool HandleIncomingMessageBuffer(ushort muxId, in MessageBuffer messageBuffer) => true;
            public void SendMuxCommand(ushort muxId, MuxCommand command) { }
            public void SendMessage(ushort muxId, IMessage message) { }
            public void SendMessageList(ushort muxId, List<IMessage> messageList) { }
        }

        private sealed class FakeFrontendSession : IFrontendSession
        {
            public ulong Id { get; }
            public object Account { get; }
            public string Locale { get; } = "en_us";

            public FakeFrontendSession(DBAccount account)
            {
                Id = (ulong)account.Id;
                Account = account;
            }
        }
    }
}
