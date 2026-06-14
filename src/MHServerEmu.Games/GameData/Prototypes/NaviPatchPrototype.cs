using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.GameData.Resources;
using MHServerEmu.Games.Navi;

namespace MHServerEmu.Games.GameData.Prototypes
{
    public class NaviPatchSourcePrototype : Prototype, IBinaryResource
    {
        // PatchFragments "Skipping writing field %s in class %s because it has eFlagDontCook set"
        public uint NaviPatchCrc { get; protected set; }
        public NaviPatchPrototype NaviPatch { get; protected set; } = new();
        public NaviPatchPrototype PropPatch { get; protected set; } = new();
        public float PlayableArea { get; protected set; }
        public float SpawnableArea { get; protected set; }

        public void Deserialize(BinaryReader reader)
        {
            NaviPatchCrc = reader.ReadUInt32();
            NaviPatch.Deserialize(reader);
            PropPatch.Deserialize(reader);
            PlayableArea = reader.ReadSingle();
            SpawnableArea = reader.ReadSingle();
        }
    }

    public class NaviPatchPrototype : Prototype, IBinaryResource
    {
        public Vector3[] Points { get; protected set; }
        public NaviPatchEdgePrototype[] Edges { get; protected set; }

        public void Deserialize(BinaryReader reader)
        {
            if (BinaryResourceSerializer.ReadContainerFromBinaryReader(out Vector3[] points, reader))
                Points = points;

            if (BinaryResourceSerializer.ReadPrototypeContainer(out NaviPatchEdgePrototype[] edges, reader))
                Edges = edges;
        }
    }

    public class NaviPatchEdgePrototype : Prototype, IBinaryResource
    {
        public uint Index0 { get; protected set; }
        public uint Index1 { get; protected set; }
        public NaviContentFlags[] Flags0 { get; protected set; }
        public NaviContentFlags[] Flags1 { get; protected set; }

        public void Deserialize(BinaryReader reader)
        {
            Index0 = reader.ReadUInt32();
            Index1 = reader.ReadUInt32();

            if (BinaryResourceSerializer.ReadContainerFromBinaryReader(out NaviContentFlags[] flags0, reader))
                Flags0 = flags0;

            if (BinaryResourceSerializer.ReadContainerFromBinaryReader(out NaviContentFlags[] flags1, reader))
                Flags1 = flags1;
        }
    }

    public class NaviPatchFragmentPrototype : Prototype, IBinaryResource
    {
        public Vector3 Position { get; protected set; }
        public Vector3 Rotation { get; protected set; }
        public Vector3 Scale { get; protected set; }
        public Vector3 PrePivot { get; protected set; }
        public ulong FragmentResource { get; protected set; }

        public void Deserialize(BinaryReader reader)
        {
            throw new NotImplementedException();
        }
    }
}
