using MHServerEmu.Games.GameData;

namespace MHServerEmu.Games.Locales
{
    public class LocaleString
    {
        public ushort FlagsProduced { get; }
        public string String { get; }

        public LocaleString(ushort flagsProduced, string text)
        {
            FlagsProduced = flagsProduced;
            String = text;
        }

        public override string ToString()
        {
            return String;
        }
    }

    /// <summary>
    /// Represents a node in an intrusive linked list of <see cref="LocaleString"/>.
    /// </summary>
    public class LocaleEntryString : LocaleString
    {
        protected readonly ulong _flagsConsumed;

        internal LocaleEntryString Next;

        public LocaleEntryString(ulong flagsConsumed, ushort flagsProduced, string text)
            : base(flagsProduced, text)
        {
            _flagsConsumed = flagsConsumed;
        }

        public bool IsMatch(ulong otherFlagsConsumed)
        {
            return (otherFlagsConsumed & _flagsConsumed) == otherFlagsConsumed;
        }
    }

    /// <summary>
    /// Represents the head node in an intrusive linked list of <see cref="LocaleString"/>.
    /// </summary>
    public class LocaleDefaultString : LocaleEntryString
    {
        public LocaleStringId Id { get; }

        public LocaleDefaultString(LocaleStringId localeStringId, ushort flagsProduced, string text)
            : base(ulong.MaxValue, flagsProduced, text)
        {
            Id = localeStringId;
        }

        public void Insert(ulong flagsConsumed, ushort flagsProduced, string text)
        {
            LocaleEntryString prev = this;
            while (prev.Next != null && prev.Next.IsMatch(flagsConsumed) == false)
                prev = prev.Next;

            LocaleEntryString next = prev.Next;
            prev.Next = new(flagsConsumed, flagsProduced, text);
            prev.Next.Next = next;
        }

        public LocaleString Find(ulong flagsConsumed)
        {
            if (_flagsConsumed == flagsConsumed || Next == null)
                return this;

            LocaleEntryString entry = Next;
            while (entry != null)
            {
                if (entry.IsMatch(flagsConsumed))
                    return entry;

                entry = entry.Next;
            }

            return this;
        }
    }
}
