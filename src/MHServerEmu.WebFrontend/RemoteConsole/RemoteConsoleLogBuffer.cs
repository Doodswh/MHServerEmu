using MHServerEmu.Core.Logging;

namespace MHServerEmu.WebFrontend.RemoteConsole
{
    public class RemoteConsoleLogBuffer : LogTarget
    {
        private readonly object _sync = new();
        private readonly List<RemoteConsoleLogEntry> _entries = new();
        private readonly int _maxEntries;

        private long _nextSequence = 1;

        public RemoteConsoleLogBuffer(int maxEntries)
            : base(new LogTargetSettings
            {
                IncludeTimestamps = true,
                MinimumLevel = LoggingLevel.Trace,
                MaximumLevel = LoggingLevel.Fatal,
                Channels = LogChannels.All
            })
        {
            _maxEntries = Math.Max(100, maxEntries);
        }

        public override void ProcessLogMessage(in LogMessage message)
        {
            lock (_sync)
            {
                _entries.Add(new RemoteConsoleLogEntry
                {
                    Sequence = _nextSequence++,
                    Timestamp = message.Timestamp,
                    Level = Enum.GetName(message.Level),
                    Logger = message.Logger,
                    Message = message.Message,
                    Text = message.ToString(IncludeTimestamps)
                });

                if (_entries.Count > _maxEntries)
                    _entries.RemoveRange(0, _entries.Count - _maxEntries);
            }
        }

        public RemoteConsolePollSnapshot GetSnapshot(long sinceSequence, int limit)
        {
            limit = Math.Clamp(limit, 1, 500);

            lock (_sync)
            {
                List<RemoteConsoleLogEntry> matches = _entries
                    .Where(entry => entry.Sequence > sinceSequence)
                    .Take(limit)
                    .Select(entry => entry.Clone())
                    .ToList();

                long latestSequence = _entries.Count > 0 ? _entries[^1].Sequence : 0;
                return new(matches, latestSequence);
            }
        }
    }

    public class RemoteConsoleLogEntry
    {
        public long Sequence { get; set; }
        public DateTime Timestamp { get; set; }
        public string Level { get; set; }
        public string Logger { get; set; }
        public string Message { get; set; }
        public string Text { get; set; }

        public RemoteConsoleLogEntry Clone()
        {
            return (RemoteConsoleLogEntry)MemberwiseClone();
        }
    }

    public readonly struct RemoteConsolePollSnapshot
    {
        public IReadOnlyList<RemoteConsoleLogEntry> Entries { get; }
        public long LatestSequence { get; }

        public RemoteConsolePollSnapshot(IReadOnlyList<RemoteConsoleLogEntry> entries, long latestSequence)
        {
            Entries = entries;
            LatestSequence = latestSequence;
        }
    }
}
