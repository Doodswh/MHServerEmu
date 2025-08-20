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
    /// <summary>
    /// Defines the structure for a single gift entry in the JSON configuration.
    /// </summary>
    public class GiftItemEntry
    {
        public ulong ItemPrototype { get; set; }
        public int Count { get; set; }
        public DateTime AddedDate { get; set; }
        // Using the Player Name (string) as the key
        public Dictionary<string, DateTime> ClaimedByPlayers { get; set; } = new();
    }

    /// <summary>
    /// A game service checks for online players when they login  and distributes any unclaimed gifts.
    /// </summary>
    public class GiftItemDistributor : IGameService
    {
        public GameServiceState State { get; private set; }
        private static readonly Logger Logger = LogManager.CreateLogger();
        private static readonly string PendingItemsPath = Path.Combine(FileHelper.DataDirectory, "PendingItems.json");
        private static readonly object _ioLock = new();

        private List<GiftItemEntry> _cachedItems;
        private volatile bool _isRunning;
        private volatile bool _isDirty = false;
        private Task _currentSaveTask;

        public void Run()
        {
            State = GameServiceState.Running;
            LoadGiftItems();
            Logger.Info("[GiftDistributor] Service started and is waiting for player gift requests.");
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

            lock (_cachedItems)
            {
                foreach (var entry in _cachedItems)
                {
                    if (entry.AddedDate <= DateTime.UtcNow && !entry.ClaimedByPlayers.ContainsKey(playerName))
                    {
                        giftsToAward.Add(new ServiceMessage.GiftInfo(entry.ItemPrototype, entry.Count));
                        entry.ClaimedByPlayers.Add(playerName, DateTime.UtcNow);
                        _isDirty = true;
                    }
                }
            }

            if (giftsToAward.Count > 0)
            {
                // Create the award message with the critical instance and player identification
                var awardMessage = new ServiceMessage.AwardPlayerGifts(playerDbId, instanceId, giftsToAward);
                ServerManager.Instance.SendMessageToService(GameServiceType.GameInstance, awardMessage);

                SaveChangesAsync().GetAwaiter().GetResult();
            }
        }

        public void Shutdown()
        {
            _isRunning = false;
            // Perform one final save on shutdown, if needed.
            SaveChangesAsync().GetAwaiter().GetResult();

            State = GameServiceState.ShuttingDown;
        }

        public string GetStatus() => $"Loaded Gift Entries: {_cachedItems?.Count ?? 0}, Changes Pending Save: {_isDirty}";

       

       

        private void LoadGiftItems()
        {
            try
            {
                if (File.Exists(PendingItemsPath))
                {
                    _cachedItems = JsonSerializer.Deserialize<List<GiftItemEntry>>(File.ReadAllText(PendingItemsPath)) ?? new List<GiftItemEntry>();
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

        private Task SaveChangesAsync()
        {
            if (!_isDirty || (_currentSaveTask != null && !_currentSaveTask.IsCompleted))
            {
                return Task.CompletedTask;
            }
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
                    lock (_ioLock)
                    {
                        File.Move(tempPath, PendingItemsPath, true);
                        Logger.Info("[GiftDistributor] Saved gift claims to disk.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.ErrorException(ex, "[GiftDistributor] Failed to save pending gift items.");
                    _isDirty = true; // Mark as dirty again to retry on the next cycle.
                }
            });
            return _currentSaveTask;
        }
    }
}