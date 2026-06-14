using System.Runtime.InteropServices;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Logging;

namespace MHServerEmu.Games.GameData.Calligraphy
{
    [Flags]
    public enum PrototypeDataDesc : byte
    {
        None                = 0,
        ReferenceExists     = 1 << 0,
        InstanceDataExists  = 1 << 1,
        PolymorphicData     = 1 << 2,
    }

    public struct PrototypeDataHeader(bool referenceExists, bool instanceDataExists, bool polymorphicData, PrototypeId referenceType)
    {
        public bool ReferenceExists = referenceExists;
        public bool InstanceDataExists = instanceDataExists;
        public bool PolymorphicData = polymorphicData;
        public PrototypeId ReferenceType = referenceType;  // Parent prototype id, invalid (0) for .defaults
    }

    public readonly struct CalligraphyReader : IDisposable
    {
        // This combines Gazillion's implementation of BinaryReader with the CalligraphyReader subclass.
        // We can potentially separate parts of this to use for no allocation reading in other contexts.

        private readonly Stream _stream;
        
        public string SectionName { get; }

        public long BytesRemaining { get => _stream.Length - _stream.Position; }

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

        public bool Read<T>(out T dest) where T: unmanaged
        {
            dest = default;
            Span<byte> buffer = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref dest, 1));
            return _stream.Read(buffer) == buffer.Length;
        }

        public bool ReadBytes(Span<byte> dest)
        {
            return _stream.Read(dest) == dest.Length;
        }

        public bool Read<T>(T[] dest) where T: unmanaged
        {
            Span<byte> buffer = MemoryMarshal.AsBytes(dest.AsSpan());
            return _stream.Read(buffer) == buffer.Length; 
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

        public bool ReadPrototypeHeader(out PrototypeDataHeader header, string filepathBeingLoaded = "<unknown>")
        {
            header = default;

            if (Read(out PrototypeDataDesc flags) == false)
                return false;

            header.ReferenceExists = flags.HasFlag(PrototypeDataDesc.ReferenceExists);
            header.InstanceDataExists = flags.HasFlag(PrototypeDataDesc.InstanceDataExists);
            header.PolymorphicData = flags.HasFlag(PrototypeDataDesc.PolymorphicData);

            if (header.ReferenceExists)
            {
                if (Read(out header.ReferenceType) == false)
                    return false;

                if (header.ReferenceType != PrototypeId.Invalid)
                {
                    bool referenceValid = GameDatabase.PrototypeRefManager.ContainsDataRef(header.ReferenceType);
                    if (!Verify.IsTrue(referenceValid, $"Prototype {filepathBeingLoaded} has hash {(ulong)header.ReferenceType} not found in directory.\n\nPlease make sure that the types of the fields in Calligraphy match the ones defined in the prototype class."))
                        return false;
                }
            }
            else
            {
                header.ReferenceType = PrototypeId.Invalid;
            }

            return true;
        }
    }
}
