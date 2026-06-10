using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Logging;

namespace MHServerEmu.Games.GameData.Calligraphy
{
    public readonly struct CalligraphyReader : IDisposable
    {
        // This combines Gazillion's implementation of BinaryReader with the CalligraphyReader subclass.
        // We can potentially separate parts of this to use for no allocation reading in other contexts.

        private readonly Stream _stream;
        
        public string SectionName { get; }

        public CalligraphyReader(Stream stream, string sectionName = "Unknown")
        {
            _stream = stream;
            SectionName = sectionName;
        }

        public override string ToString()
        {
            return SectionName;
        }

        public void Dispose()
        {
            _stream?.Dispose();
        }

        #region Gazillion::BinaryReader

        public bool Read<T>(out T dest) where T: struct
        {
            Span<byte> buffer = stackalloc byte[Unsafe.SizeOf<T>()];

            if (_stream.Read(buffer) != buffer.Length)
            {
                dest = default;
                return false;
            }

            dest = MemoryMarshal.Cast<byte, T>(buffer)[0];
            return true;
        }

        public bool ReadBytes(Span<byte> dest)
        {
            return _stream.Read(dest) == dest.Length;
        }

        public long Seek(SeekOrigin origin, long offset)
        {
            return _stream.Seek(offset, origin);
        }

        #endregion

        public bool ReadStringUTF8(Span<byte> dest, int destSize, short count = -1)
        {
            if (count <= -1)
            {
                if (!Verify.IsTrue(Read(out count))) return false;
                if (!Verify.IsTrue(count < destSize)) return false;
            }

            if (!Verify.IsTrue(ReadBytes(dest[..count]))) return false;

            if (count < destSize)
                dest[count] = 0;

            return true;
        }

        public bool ReadFilePath(Span<byte> dest, int destSize, short count = -1)
        {
            if (ReadStringUTF8(dest, destSize, count) == false)
                return false;

            int nullIndex = dest.IndexOf((byte)0);
            if (nullIndex > 0)
                dest = dest[..nullIndex];

            dest.Replace((byte)'\\', (byte)'/');

            return true;
        }

        public bool ReadHeader(string magic)
        {
            return ReadHeader(magic, DataDirectory.CalligraphyExportVersion);
        }

        public bool ReadHeader(string magic, byte expectedVersion)
        {
            const int MagicMax = 3;

            Span<byte> magicBuffer = stackalloc byte[MagicMax + 1];
            if (!Verify.IsTrue(ReadStringUTF8(magicBuffer, magicBuffer.Length, MagicMax), $"Unable to read magic header in data file {SectionName}"))
                return false;

            if (!Verify.IsTrue(string.Equals(magic, magicBuffer.GetCString()), $"Data file magic identifier not found.  Do you have the latest build?  Data file {SectionName}"))
                return false;

            if (!Verify.IsTrue(Read(out byte fileVersion), $"Unable to read version in {SectionName}"))
                return false;

            if (!Verify.IsTrue(expectedVersion == fileVersion, $"Version mismatch in {SectionName}.  Do you have the latest build?"))
                return false;

            return true;
        }
    }
}
