using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.GameData.Resources;
using MHServerEmu.Games.Navi;

namespace MHServerEmu.Games.GameData.Prototypes
{
    public class NaviPatchSourcePrototype : Prototype, IBinaryResource
    {
        public NaviPatchFragmentPrototype[] PatchFragments { get; protected set; }  // eFlagDontCook
        public int NaviPatchCRC { get; protected set; }
        public NaviPatchPrototype NaviPatch { get; protected set; } = new();
        public NaviPatchPrototype PropPatch { get; protected set; } = new();
        public float PlayableArea { get; protected set; }
        public float SpawnableArea { get; protected set; }

        //---

        public void Deserialize(BinaryReader reader)
        {
            // PatchFragments = BinaryResourceSerializer.ReadPrototypeContainer<NaviPatchFragmentPrototype>(reader);
            NaviPatchCRC = reader.ReadInt32();
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

        //---

        public void Deserialize(BinaryReader reader)
        {
            Points = BinaryResourceSerializer.ReadVectorFromBinaryReader<Vector3>(reader);
            Edges = BinaryResourceSerializer.ReadPrototypeContainer<NaviPatchEdgePrototype>(reader);
        }
    }

    public class NaviPatchEdgePrototype : Prototype, IBinaryResource
    {
        public int Index0 { get; protected set; }
        public int Index1 { get; protected set; }
        public NaviContentFlags[] Flags0 { get; protected set; }
        public NaviContentFlags[] Flags1 { get; protected set; }

        //---

        public void Deserialize(BinaryReader reader)
        {
            Index0 = reader.ReadInt32();
            Index1 = reader.ReadInt32();
            Flags0 = BinaryResourceSerializer.ReadVectorFromBinaryReader<NaviContentFlags>(reader);
            Flags1 = BinaryResourceSerializer.ReadVectorFromBinaryReader<NaviContentFlags>(reader);
        }
    }

    public class NaviPatchFragmentPrototype : Prototype, IBinaryResource
    {
        public Vector3 Position { get; protected set; }
        public Orientation Rotation { get; protected set; }
        public Vector3 Scale { get; protected set; }
        public Vector3 PrePivot { get; protected set; }
        public string FragmentResource { get; protected set; }

        //---

        public void Deserialize(BinaryReader reader)
        {
            Position = reader.Read<Vector3>();
            Rotation = reader.Read<Orientation>();
            Scale = reader.Read<Vector3>();
            PrePivot = reader.Read<Vector3>();
            FragmentResource = reader.ReadFixedString32();
        }
    }
}
