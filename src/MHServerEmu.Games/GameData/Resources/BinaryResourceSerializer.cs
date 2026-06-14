using System.Runtime.InteropServices;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.GameData.Prototypes;

namespace MHServerEmu.Games.GameData.Resources
{
    /// <summary>
    /// An implementation of <see cref="GameDataSerializer"/> for resource prototypes.
    /// </summary>
    public sealed class BinaryResourceSerializer : GameDataSerializer
    {
        private const byte INCREMENT_THIS_TO_FORCE_RECOOK = 16;

        public static BinaryResourceSerializer Instance { get; } = new();

        private BinaryResourceSerializer() { }

        public override bool Deserialize(Prototype prototype, PrototypeId prototypeDataRef, Stream stream)
        {
            if (!Verify.IsNotNull(prototype)) return false;
            if (!Verify.IsTrue(prototypeDataRef != PrototypeId.Invalid)) return false;

            prototype.DataRef = prototypeDataRef;

            using BinaryReader reader = new(stream);

            if (!Verify.IsTrue(reader.Read(out Header header))) return false;

            if (!Verify.IsTrue(header.IsLittleEndian, "Endian mismatch"))
                return false;

            if (!Verify.IsTrue(header.CookerVersion == INCREMENT_THIS_TO_FORCE_RECOOK, "Cooker version has changed. This file should be re-cooked"))
                return false;

            // client check: serialized prototype data version does not match classinfo

            // Instead of going through PrototypeFieldSet like the client does, currently we have
            // manual deserialization routines defined in individual resource prototype classes.
            // Doing it Gazillion-style is possible, but it's measurably slower than what we have now.
            try
            {
                IBinaryResource binaryResource = (IBinaryResource)prototype;
                binaryResource.Deserialize(reader);
            }
            catch (Exception e)
            {
                Verify.IsTrue(false, $"Error deserializing resource prototype {prototype}\n{e}");
                return false;
            }

            return true;
        }

        public static T[] ReadPrototypeContainer<T>(BinaryReader reader) where T: Prototype, IBinaryResource
        {
            if (!Verify.IsTrue(reader.Read(out uint size))) return null;

            if (size == 0)
                return Array.Empty<T>();

            PrototypeClassManager classManager = GameDatabase.PrototypeClassManager;

            T[] list = new T[size];

            for (int i = 0; i < size; i++)
            {
                // Binary resources use djb2 hashes of prototype names for polymorphic serialization.
                if (!Verify.IsTrue(reader.Read(out uint protoNameHash))) return null;

                Type classType = classManager.GetPrototypeClassTypeByNameHash(protoNameHash);
                if (!Verify.IsNotNull(classType)) return null;

                T item = classManager.AllocatePrototype(classType) as T;
                if (!Verify.IsNotNull(item)) return null;

                try
                {
                    item.Deserialize(reader);
                }
                catch (Exception e)
                {
                    Verify.IsTrue(false, $"Error deserializing prototype container item {item.GetType().Name}\n{e}");
                    return null;
                }

                list[i] = item;
            }

            return list;
        }

        public static T[] ReadVectorFromBinaryReader<T>(BinaryReader reader) where T: unmanaged
        {
            // Replacement for BinaryResourceSerializer::readVectorFromBinaryReader() from the client.

            if (!Verify.IsTrue(reader.Read(out uint size))) return null;

            if (size == 0)
                return Array.Empty<T>();

            T[] list = new T[size];
            reader.Read(MemoryMarshal.AsBytes(list.AsSpan())); // read the whole thing in one fell swoop
            return list;
        }

#pragma warning disable CS0649
        private struct Header
        {
            public byte CookerVersion;
            public bool IsLittleEndian;
            public ushort Padding;              // alignment garbage data
            public int PrototypeDataVersion;
            public uint PrototypeHash;
        }
#pragma warning restore CS0649
    }
}
