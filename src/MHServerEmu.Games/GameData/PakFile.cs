using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.Logging;

namespace MHServerEmu.Games.GameData
{
    /// <summary>
    /// Represents a loaded .sip package file.
    /// </summary>
    public class PakFile
    {
        // PAK / GPAK / .sip files are package files that contain compressed game data files.
        // They consist of a header, an entry table, and data for all stored files compressed using the LZ4 algorithm.

        private const uint Signature = 1196441931;  // KAPG
        private const uint Version = 1;

        private readonly Dictionary<string, PakEntry> _entries = new();
        private readonly byte[] _data;

        public int Count { get => _entries.Count; }

        /// <summary>
        /// Loads a <see cref="PakFile"/> from the specified path.
        /// </summary>
        public PakFile(string pakFilePath)
        {
            if (!Verify.IsTrue(File.Exists(pakFilePath), $"{Path.GetFileName(pakFilePath)} not found"))
                return;

            using FileStream stream = File.OpenRead(pakFilePath);
            using BinaryReader reader = new(stream);

            // Read file header
            uint signature = reader.ReadUInt32();
            if (!Verify.IsTrue(signature == Signature, $"Invalid pak file signature {signature}, expected {Signature}"))
                return;

            uint version = reader.ReadUInt32();
            if (!Verify.IsTrue(version == Version, $"Invalid pak file version {version}, expected {Version}"))
                return;

            // Read all entries
            int numEntries = reader.ReadInt32();

            if (numEntries > 0)
            {
                _entries.EnsureCapacity(numEntries);

                // We make use of the fact that entries are in the same order as their compressed data that follows,
                // so we can get the full size of the compressed data section from the last entry.
                PakEntry entry = null;

                for (int i = 0; i < numEntries; i++)
                {
                    ulong fileHash = reader.ReadUInt64();
                    string filePath = reader.ReadFixedString32();
                    int modTime = reader.ReadInt32();
                    int offset = reader.ReadInt32();
                    int compressedSize = reader.ReadInt32();
                    int uncompressedSize = reader.ReadInt32();

                    entry = new(fileHash, filePath, modTime, offset, compressedSize, uncompressedSize);
                    _entries.Add(entry.FilePath, entry);
                }

                // Read and store compressed data as a single buffer we will slice with spans
                int dataSize = entry.Offset + entry.CompressedSize;
                _data = reader.ReadBytes(dataSize);
            }
            else
            {
                // Empty pak file
                _data = Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Returns a <see cref="Stream"/> of decompressed data for the file stored at the specified path in this <see cref="PakFile"/>.
        /// </summary>
        public Stream LoadFileDataInPak(string filePath)
        {
            if (!Verify.IsTrue(_entries.TryGetValue(filePath, out PakEntry entry))) return null;

            ReadOnlySpan<byte> compressedData = _data.AsSpan(entry.Offset, entry.CompressedSize);
            byte[] uncompressedData = new byte[entry.UncompressedSize];
            CompressionHelper.LZ4Decode(compressedData, uncompressedData);

            return new MemoryStream(uncompressedData);
        }

        /// <summary>
        /// Returns file paths with the specified prefix contained in this <see cref="PakFile"/>.
        /// </summary>
        public void GetFilesFromPak(string prefix, List<string> filePaths)
        {
            foreach (string filePath in _entries.Keys)
            {
                if (filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    filePaths.Add(filePath);
            }
        }

        /// <summary>
        /// Metadata for a file contained in a <see cref="PakFile"/>.
        /// </summary>
        private class PakEntry(ulong fileHash, string filePath, int modTime, int offset, int compressedSize, int uncompressedSize)
        {
            public ulong FileHash { get; } = fileHash;
            public string FilePath { get; } = filePath;
            public int ModTime { get; } = modTime;
            public int Offset { get; } = offset;
            public int CompressedSize { get; } = compressedSize;
            public int UncompressedSize { get; } = uncompressedSize;
        }
    }
}
