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
        public Dictionary<string, DateTime> ClaimedByPlayers { get; set; } = new();
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
        }

        private void HandleGiftRequest(in ServiceMessage.PlayerRequestsGifts request)
        {
            string playerName = request.PlayerName;
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
                        if (entry.ClaimedByPlayers.TryGetValue(playerName, out DateTime lastClaimDate))
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
                        if (!entry.ClaimedByPlayers.ContainsKey(playerName))
                        {
                            shouldAward = true;
                        }
                    }
                    if (shouldAward)
                    {
                        giftsToAward.Add(new ServiceMessage.GiftInfo(entry.ItemPrototype, entry.Count));
                        entry.ClaimedByPlayers[playerName] = now;
                        _isDirty = true;
                    }
                }
            }

            if (giftsToAward.Count > 0)
            {
                Logger.Info($"[GiftDistributor] Awarding {giftsToAward.Count} gifts to {playerName}.");
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