using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.Metrics.Categories;
using MHServerEmu.Core.System.Time;

namespace MHServerEmu.Core.Metrics
{
    public sealed class RegionPhasePerformanceReport
    {
        public int Pending { get; init; }
        public int LastBudget { get; init; }
        public int LastProcessed { get; init; }
        public int LastPending { get; init; }
        public int HotStreak { get; init; }
        public double LastElapsedMilliseconds { get; init; }
        public double AverageElapsedMilliseconds { get; init; }
        public bool IsHot { get; init; }
    }

    public sealed class RegionSchedulerReport
    {
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public ulong RegionId { get; init; }
        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public ulong MatchNumber { get; init; }
        public string PrototypeName { get; init; }
        public int PlayerCount { get; init; }
        public RegionPhasePerformanceReport Transfers { get; init; }
        public RegionPhasePerformanceReport Aoi { get; init; }
        public RegionPhasePerformanceReport Events { get; init; }
    }

    public class PerformanceReport : IPoolable, IDisposable
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private static uint _currentReportId = 0;

        [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
        public ulong Id { get; private set; }
        public MemoryMetrics.Report Memory { get; private set; }
        public Dictionary<ulong, GamePerformanceMetrics.Report> Games { get; } = new();
        public Dictionary<ulong, List<RegionSchedulerReport>> RegionsByGame { get; } = new();

        [JsonIgnore]
        public bool IsInPool { get; set; }

        public PerformanceReport() { }

        public void Initialize(MemoryMetrics memoryMetrics, Dictionary<ulong, GamePerformanceMetrics> gameMetrics)
        {
            Id = (ulong)Clock.UnixTime.TotalSeconds << 32 | ++_currentReportId;

            Memory = memoryMetrics.GetReport();

            foreach (GamePerformanceMetrics metrics in gameMetrics.Values)
            {
                Games.Add(metrics.GameId, metrics.GetReport());
            }
        }

        public void AddRegionReports(ulong gameId, IEnumerable<RegionSchedulerReport> regionReports)
        {
            RegionsByGame[gameId] = regionReports.ToList();
        }

        public override string ToString()
        {
            return ToString(MetricsReportFormat.PlainText);
        }

        public string ToString(MetricsReportFormat format)
        {
            switch (format)
            {
                case MetricsReportFormat.PlainText:
                    return AsPlainText();

                case MetricsReportFormat.Json:
                    return JsonSerializer.Serialize(this);

                default:
                    return Logger.WarnReturn(string.Empty, $"ToString(): Unsupported format {format}");
            }
        }

        public void ResetForPool()
        {
            Memory = default;
            Games.Clear();
            RegionsByGame.Clear();
        }

        public void Dispose()
        {
            ObjectPoolManager.Instance.Return(this);
        }

        private string AsPlainText()
        {
            StringBuilder sb = new();
            sb.AppendLine($"Performance Report 0x{Id:X}");

            sb.AppendLine("Memory:");
            sb.AppendLine(Memory.ToString());

            sb.AppendLine("Games:");
            foreach (var kvp in Games)
            {
                sb.AppendLine($"Game [0x{kvp.Key:X}]:");
                sb.AppendLine(kvp.Value.ToString());

                if (RegionsByGame.TryGetValue(kvp.Key, out List<RegionSchedulerReport> regionReports) && regionReports.Count > 0)
                {
                    sb.AppendLine("Regions:");
                    foreach (RegionSchedulerReport regionReport in regionReports.OrderByDescending(report => report.Aoi.IsHot).ThenByDescending(report => report.Events.IsHot).ThenBy(report => report.RegionId))
                    {
                        sb.AppendLine($"  Region [0x{regionReport.RegionId:X}] {regionReport.PrototypeName}, players={regionReport.PlayerCount}, match={regionReport.MatchNumber}");
                        sb.AppendLine($"    Transfers: hot={regionReport.Transfers.IsHot}, pending={regionReport.Transfers.Pending}, avgMs={regionReport.Transfers.AverageElapsedMilliseconds:0.00}, lastMs={regionReport.Transfers.LastElapsedMilliseconds:0.00}, budget={regionReport.Transfers.LastBudget}, processed={regionReport.Transfers.LastProcessed}");
                        sb.AppendLine($"    Aoi: hot={regionReport.Aoi.IsHot}, pending={regionReport.Aoi.Pending}, avgMs={regionReport.Aoi.AverageElapsedMilliseconds:0.00}, lastMs={regionReport.Aoi.LastElapsedMilliseconds:0.00}, budget={regionReport.Aoi.LastBudget}, processed={regionReport.Aoi.LastProcessed}");
                        sb.AppendLine($"    Events: hot={regionReport.Events.IsHot}, pending={regionReport.Events.Pending}, avgMs={regionReport.Events.AverageElapsedMilliseconds:0.00}, lastMs={regionReport.Events.LastElapsedMilliseconds:0.00}, budget={regionReport.Events.LastBudget}, processed={regionReport.Events.LastProcessed}");
                    }
                }
            }

            return sb.ToString();
        }
    }
}





