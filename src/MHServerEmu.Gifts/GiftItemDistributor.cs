using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Network;
using MHServerEmu.Frontend;
using MHServerEmu.Games;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Loot;
using MHServerEmu.PlayerManagement;
using MHServerEmu.PlayerManagement.Players;
using MHServerEmu.PlayerManagement.Social;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace MHServerEmu.Gifts
{
    public class GiftItemEntry
    {
        public ulong ItemPrototype { get; set; }
        public int Count { get; set; }
        public DateTime AddedDate { get; set; }
        public bool IsDaily { get; set; } = false;
        public Dictionary<ulong, DateTime> ClaimedByPlayers { get; set; } = new();
    }

    public class GiftItemDistributor : IGameService
    {
        public GameServiceState State { get; set; }
        private static readonly Logger Logger = LogManager.CreateLogger();
        private static readonly string PendingItemsPath = Path.Combine(FileHelper.DataDirectory, "PendingItems.json");
        private static readonly object _ioLock = new();

        private List<GiftItemEntry> _cachedItems;
        private volatile bool _isRunning;
        private volatile bool _isDirty = false;
        private Task _currentSaveTask;

        public void Run()
        {
            LoadGiftItems();
            Logger.Info("[GiftDistributor] Service started.");
            State = GameServiceState.Running;
            _isRunning = true;
        }

        public void ReceiveServiceMessage<T>(in T message) where T : struct, IGameServiceMessage
        {
            if (message is ServiceMessage.PlayerRequestsGifts request)
            {
                HandleGiftRequest(in request);
            }
            else if (message is ServiceMessage.AddPlayerGift addGift)
            {
                HandleAddGift(in addGift);
            }
            else if (message is ServiceMessage.ListPlayerGifts listGifts)
            {
                HandleListGifts(in listGifts);
            }
            else if (message is ServiceMessage.RemovePlayerGift removeGift)
            {
                HandleRemoveGift(in removeGift);
            }
        }

        private void HandleAddGift(in ServiceMessage.AddPlayerGift message)
        {
            var newEntry = new GiftItemEntry
            {
                ItemPrototype = message.ItemPrototype,
                Count = message.Count,
                AddedDate = DateTime.UtcNow,
                IsDaily = message.IsDaily,
                ClaimedByPlayers = new Dictionary<ulong, DateTime>()
            };
            
            lock (_cachedItems)
            {
                _cachedItems.Add(newEntry);
                _isDirty = true;
            }

            string itemName = GameDatabase.GetPrototypeName((PrototypeId)message.ItemPrototype);
            string giftType = message.IsDaily ? "daily" : "one-time";
            Logger.Info($"[GiftDistributor] Added {giftType} gift for '{message.PlayerName}': {message.Count}x {itemName}");

            // If player is online, deliver the gift immediately
            if (message.PlayerOnline && message.InstanceId != 0)
            {
                Logger.Info($"[GiftDistributor] Player '{message.PlayerName}' is online. Delivering gift immediately.");

                // Resolve the ID so we can mark it as claimed
                if (PlayerNameCache.Instance.TryGetPlayerDbId(message.PlayerName, out ulong playerDbId, out _))
                {
                    var giftsToAward = new List<ServiceMessage.GiftInfo>
                    {
                        new ServiceMessage.GiftInfo(message.ItemPrototype, message.Count)
                    };

                    // Mark as claimed immediately using the ID
                    DateTime now = DateTime.UtcNow;
                    lock (_cachedItems)
                    {
                        newEntry.ClaimedByPlayers[playerDbId] = now;
                        _isDirty = true;
                    }

                    var awardMessage = new ServiceMessage.AwardPlayerGifts(playerDbId, message.InstanceId, giftsToAward);
                    ServerManager.Instance.SendMessageToService(GameServiceType.GameInstance, awardMessage);
                }
                else
                {
                    Logger.Warn($"[GiftDistributor] Could not resolve DbId for online player '{message.PlayerName}'. Gift added but not auto-delivered.");
                }
            }

            _ = SaveChangesAsync();
        }

        private void HandleListGifts(in ServiceMessage.ListPlayerGifts message)
        {
            // Try to resolve the filter name to an ID if a filter is provided
            ulong filterId = 0;
            bool hasFilter = !string.IsNullOrEmpty(message.FilterPlayerName);

            if (hasFilter)
            {
                if (!PlayerNameCache.Instance.TryGetPlayerDbId(message.FilterPlayerName, out filterId, out _))
                {
                    Logger.Info($"[GiftDistributor] Player '{message.FilterPlayerName}' not found in cache. Cannot filter gifts.");
                    return;
                }
            }

            lock (_cachedItems)
            {
                Logger.Info($"[GiftDistributor] === Pending Gifts ({_cachedItems.Count} total) ===");

                for (int i = 0; i < _cachedItems.Count; i++)
                {
                    var entry = _cachedItems[i];
                    string itemName = GameDatabase.GetPrototypeName((PrototypeId)entry.ItemPrototype);
                    string giftType = entry.IsDaily ? "Daily" : "One-time";

                    if (!hasFilter)
                    {
                        Logger.Info($"[{i}] {giftType}: {entry.Count}x {itemName} | Claimed by {entry.ClaimedByPlayers.Count} players | Added: {entry.AddedDate:yyyy-MM-dd HH:mm}");
                    }
                    else
                    {
                        // Filter by player ID now
                        if (entry.ClaimedByPlayers.ContainsKey(filterId))
                        {
                            DateTime claimedDate = entry.ClaimedByPlayers[filterId];
                            Logger.Info($"[{i}] {giftType}: {entry.Count}x {itemName} | Claimed by '{message.FilterPlayerName}' on {claimedDate:yyyy-MM-dd HH:mm}");
                        }
                        else if (!entry.IsDaily)
                        {
                            Logger.Info($"[{i}] {giftType}: {entry.Count}x {itemName} | NOT claimed by '{message.FilterPlayerName}'");
                        }
                        else
                        {
                            Logger.Info($"[{i}] {giftType}: {entry.Count}x {itemName} | Available for '{message.FilterPlayerName}'");
                        }
                    }
                }
            }
        }

        private void HandleRemoveGift(in ServiceMessage.RemovePlayerGift message)
        {
            lock (_cachedItems)
            {
                if (message.Index < 0 || message.Index >= _cachedItems.Count)
                {
                    Logger.Warn($"[GiftDistributor] Invalid gift index: {message.Index}");
                    return;
                }

                var entry = _cachedItems[message.Index];
                string itemName = GameDatabase.GetPrototypeName((PrototypeId)entry.ItemPrototype);

                _cachedItems.RemoveAt(message.Index);
                _isDirty = true;

                Logger.Info($"[GiftDistributor] Removed gift at index {message.Index}: {entry.Count}x {itemName}");
            }

            _ = SaveChangesAsync();
        }

        private void HandleGiftRequest(in ServiceMessage.PlayerRequestsGifts request)
        {
            ulong playerDbId = request.PlayerDbId;
            ulong instanceId = request.InstanceId;
            var giftsToAward = new List<ServiceMessage.GiftInfo>();

            DateTime now = DateTime.UtcNow;

            lock (_cachedItems)
            {
                for (int i = 0; i < _cachedItems.Count; i++)
                {
                    var entry = _cachedItems[i];
                    bool shouldAward = false;

                    if (entry.AddedDate > now)
                        continue;

                    if (entry.IsDaily)
                    {
                        if (entry.ClaimedByPlayers.TryGetValue(playerDbId, out DateTime lastClaimDate))
                        {
                            if (lastClaimDate.Date < now.Date)
                            {
                                shouldAward = true;
                            }
                        }
                        else
                        {
                            shouldAward = true;
                        }
                    }
                    else
                    {
                        if (!entry.ClaimedByPlayers.ContainsKey(playerDbId))
                        {
                            shouldAward = true;
                        }
                    }

                    if (shouldAward)
                    {
                        giftsToAward.Add(new ServiceMessage.GiftInfo(entry.ItemPrototype, entry.Count));
                        entry.ClaimedByPlayers[playerDbId] = now;
                        _isDirty = true;
                    }
                }
            }

            if (giftsToAward.Count > 0)
            {
                Logger.Info($"[GiftDistributor] Awarding {giftsToAward.Count} gifts to {playerDbId}.");
                var awardMessage = new ServiceMessage.AwardPlayerGifts(playerDbId, instanceId, giftsToAward);
                ServerManager.Instance.SendMessageToService(GameServiceType.GameInstance, awardMessage);
                _ = SaveChangesAsync();
            }
        }

        private Task SaveChangesAsync()
        {
            if (!_isDirty) return Task.CompletedTask;
            if (_currentSaveTask != null && !_currentSaveTask.IsCompleted) return Task.CompletedTask;

            _isDirty = false;
            _currentSaveTask = Task.Run(async () =>
            {
                List<GiftItemEntry> itemsToSave;
                lock (_cachedItems) { itemsToSave = new List<GiftItemEntry>(_cachedItems); }
                try
                {
                    string jsonContent = JsonSerializer.Serialize(itemsToSave, new JsonSerializerOptions { WriteIndented = true });
                    string tempPath = PendingItemsPath + ".tmp";
                    await File.WriteAllTextAsync(tempPath, jsonContent);
                    lock (_ioLock) { File.Move(tempPath, PendingItemsPath, true); }
                    Logger.Info($"[GiftDistributor] Saved claims to disk.");
                }
                catch (Exception ex)
                {
                    Logger.ErrorException(ex, "[GiftDistributor] Failed to save JSON.");
                    _isDirty = true;
                }
            });
            return _currentSaveTask;
        }

        public void Shutdown()
        {
            _isRunning = false;
            SaveChangesAsync().GetAwaiter().GetResult();
            State = GameServiceState.ShuttingDown;
        }

        public void GetStatus(Dictionary<string, long> statusDict)
        {
            statusDict["Loaded Gift Entries"] = _cachedItems?.Count ?? 0;
            statusDict["Changes Pending Save"] = _isDirty ? 1 : 0;
        }

        private void LoadGiftItems()
        {
            try
            {
                if (File.Exists(PendingItemsPath))
                {
                    string json = File.ReadAllText(PendingItemsPath);
                    _cachedItems = JsonSerializer.Deserialize<List<GiftItemEntry>>(json) ?? new List<GiftItemEntry>();
                    Logger.Info($"[GiftDistributor] Loaded {_cachedItems.Count} gift item entries.");
                }
                else
                {
                    _cachedItems = new List<GiftItemEntry>();
                }
            }
            catch (Exception ex)
            {
                Logger.ErrorException(ex, "[GiftDistributor] Failed to load PendingItems.json.");
                _cachedItems = new List<GiftItemEntry>();
            }
        }
    }
}