using System.Collections;

namespace MHServerEmu.Games.GameData
{
    [Flags]
    public enum PrototypeIterateFlags : byte
    {
        None            = 0,
        //Flag0         = 1 << 0,   // Does nothing
        NoAbstract      = 1 << 1,
        ApprovedOnly    = 1 << 2,
        WithEditorOnly  = 1 << 3,   // Records that have EditorOnly set are skipped if this is not set

        NoAbstractApprovedOnly = NoAbstract | ApprovedOnly
    }

    /// <summary>
    /// Iterates through prototype records using specified filters.
    /// </summary>
    public readonly struct PrototypeIterator
    {
        private readonly IReadOnlyList<PrototypeDataRefRecord> _prototypeRecords;
        private readonly PrototypeIterateFlags _flags;

        /// <summary>
        /// Constructs an empty <see cref="PrototypeIterator"/>.
        /// </summary>
        public PrototypeIterator()
        {
            _prototypeRecords = Array.Empty<PrototypeDataRefRecord>();
            _flags = PrototypeIterateFlags.None;
        }

        /// <summary>
        /// Constructs a new <see cref="PrototypeIterator"/> with the provided records and flags.
        /// </summary>
        public PrototypeIterator(IReadOnlyList<PrototypeDataRefRecord> records, PrototypeIterateFlags flags = PrototypeIterateFlags.None)
        {
            _prototypeRecords = records;
            _flags = flags;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_prototypeRecords, _flags);
        }

        public struct Enumerator : IEnumerator<PrototypeId>
        {
            private readonly IReadOnlyList<PrototypeDataRefRecord> _records;
            private readonly PrototypeIterateFlags _flags;

            private int _index = -1;

            public PrototypeId Current { get; private set; }
            object IEnumerator.Current { get => Current; }

            public Enumerator(IReadOnlyList<PrototypeDataRefRecord> recordList, PrototypeIterateFlags flags)
            {
                _records = recordList;
                _flags = flags;
            }

            public bool MoveNext()
            {
                // Based on PrototypeIterator::advanceToValid()

                while (++_index < _records.Count)
                {
                    PrototypeDataRefRecord record = _records[_index];

                    // Skip abstract prototypes if needed
                    if (record.Flags.HasFlag(Calligraphy.PrototypeRecordFlags.Abstract) && _flags.HasFlag(PrototypeIterateFlags.NoAbstract))
                        continue;

                    // Skip editor-only prototypes (which is just NaviFragmentPrototype) unless explicitly requested to include editor-only prototypes
                    if (record.Flags.HasFlag(Calligraphy.PrototypeRecordFlags.EditorOnly) && _flags.HasFlag(PrototypeIterateFlags.WithEditorOnly) == false)
                        continue;

                    // Skip unapproved prototypes if needed (NOTE: PrototypeIsApproved() forces the prototype to load)
                    if (_flags.HasFlag(PrototypeIterateFlags.ApprovedOnly) && GameDatabase.DataDirectory.PrototypeIsApproved(record) == false)
                        continue;

                    // We return PrototypeId instead of Prototype to simplify the implementation.
                    Current = record.PrototypeRef;
                    return true;
                }

                Current = PrototypeId.Invalid;
                return false;
            }

            public void Reset()
            {
                _index = -1;
            }

            public void Dispose()
            {
            }
        }
    }
}
