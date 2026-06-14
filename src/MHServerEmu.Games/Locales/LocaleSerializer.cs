using System.Text;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData;

namespace MHServerEmu.Games.Locales
{
    /// <summary>
    /// Serializes localized strings.
    /// </summary>
    public class LocaleSerializer
    {
        private const uint Magic = 0x02525453;  // STR2

        private const int HeaderSize = sizeof(uint) + sizeof(ushort);                                                   // magic + entry count = 6
        private const int StringMapEntrySize = sizeof(LocaleStringId) + sizeof(ushort) + sizeof(ushort) + sizeof(uint); // key + variant count (0) + flags produced + data offset = 16

        private readonly List<(LocaleStringId, uint)> _stringMapEntries = new();
        private readonly MemoryStream _buffer;
        private readonly BinaryWriter _writer;

        public LocaleSerializer()
        {
            _buffer = new();
            _writer = new(_buffer);
        }

        /// <summary>
        /// Adds a new <see cref="string"/> to be serialized.
        /// </summary>
        public void AddString(LocaleStringId localeStringId, string str)
        {
            long position = _buffer.Position;
            if (!Verify.IsTrue(position <= uint.MaxValue)) return;

            // Remember the position
            uint offset = (uint)position;

            // Write string data to the buffer
            int byteCount = Encoding.UTF8.GetByteCount(str);
            Span<byte> bytes = stackalloc byte[byteCount + 1];
            Encoding.UTF8.GetBytes(str, bytes);
            bytes[byteCount] = 0;   // ensure we don't write garbage data from the stack instead of the null terminator
            _writer.Write(bytes);

            // Add position to the lookup
            _stringMapEntries.Add((localeStringId, offset));
        }

        /// <summary>
        /// Writes locale string data to the provided <see cref="Stream"/>.
        /// </summary>
        public void WriteTo(Stream stream)
        {
            if (!Verify.IsTrue(_stringMapEntries.Count <= ushort.MaxValue)) return;

            using BinaryWriter writer = new(stream);

            writer.Write(Magic);
            writer.Write((ushort)_stringMapEntries.Count);

            // Maximum metadata size is 16 * 65535, so it should not be able to overflow.
            uint metadataSize = (uint)(HeaderSize + (StringMapEntrySize * _stringMapEntries.Count));

            // Write metadata
            foreach ((LocaleStringId localeStringId, uint offset) in _stringMapEntries)
            {
                writer.Write((ulong)localeStringId);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write(metadataSize + offset);
            }

            // Copy actual data from our buffer
            long oldPosition = _buffer.Position;
            _buffer.Position = 0;
            _buffer.WriteTo(stream);
            _buffer.Position = oldPosition;
        }
    }
}
