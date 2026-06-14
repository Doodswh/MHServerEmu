using MHServerEmu.Core.Logging;

namespace MHServerEmu.Games.GameData.Calligraphy
{
    public sealed class ReplacementDirectory
    {
        private readonly Dictionary<ulong, ReplacementRecord> _replacements = new();

        public static ReplacementDirectory Instance { get; } = new();

        private ReplacementDirectory() { }

        public bool AddReplacementRecord(ulong guid, ulong replacement, string name)
        {
            if (!Verify.IsTrue(guid != 0)) return false;
            if (!Verify.IsTrue(_replacements.ContainsKey(guid) == false)) return false; // client message: Replacement record already exists, returning existing record

            ReplacementRecord record = new(guid, replacement, name);
            _replacements.Add(guid, record);

            return true;
        }

        public ReplacementRecord GetReplacementRecord(ulong guid)
        {
            if (_replacements.TryGetValue(guid, out ReplacementRecord record) == false)
                return null;

            return record;
        }

        public class ReplacementRecord
        {
            public ulong Guid { get; }
            public ulong Replacement { get; }
            public string Name { get; }

            public ReplacementRecord(ulong oldGuid, ulong newGuid, string name)
            {
                Guid = oldGuid;
                Replacement = newGuid;
                Name = name;
            }
        }
    }
}
