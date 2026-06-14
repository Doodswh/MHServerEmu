using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace MHServerEmu.Core.Extensions
{
    public static class BinaryReaderExtensions
    {
        public static bool Read<T>(this BinaryReader reader, out T dest) where T: unmanaged
        {
            Span<byte> buffer = stackalloc byte[Unsafe.SizeOf<T>()];

            if (reader.Read(buffer) != buffer.Length)
            {
                dest = default;
                return false;
            }

            dest = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(buffer));
            return true;
        }

        public static T Read<T>(this BinaryReader reader) where T: unmanaged
        {
            reader.Read(out T value);
            return value;
        }

        /// <summary>
        /// Reads a fixed-length string preceded by its length as a 32-bit signed integer.
        /// </summary>
        public static string ReadFixedString32(this BinaryReader reader)
        {
            int length = reader.ReadInt32();
            Span<byte> bytes = stackalloc byte[length];
            reader.Read(bytes);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
