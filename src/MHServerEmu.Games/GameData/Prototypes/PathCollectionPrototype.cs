using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.GameData.Resources;

namespace MHServerEmu.Games.GameData.Prototypes
{
    public class PathCollectionPrototype : Prototype, IBinaryResource
    {
        public PathNodeSetPrototype[] PathNodeSets { get; protected set; }

        public void Deserialize(BinaryReader reader)
        {
            if (BinaryResourceSerializer.ReadPrototypeContainer(out PathNodeSetPrototype[] pathNodeSets, reader))
                PathNodeSets = pathNodeSets;
        }
    }

    public class PathNodeSetPrototype : Prototype, IBinaryResource
    {
        public ushort Group { get; protected set; }
        public PathNodePrototype[] PathNodes { get; protected set; }
        public ushort NumNodes { get; protected set; }

        public void Deserialize(BinaryReader reader)
        {
            Group = reader.ReadUInt16();

            if (BinaryResourceSerializer.ReadPrototypeContainer(out PathNodePrototype[] pathNodes, reader))
                PathNodes = pathNodes;

            NumNodes = reader.ReadUInt16();
        }
    }

    public class PathNodePrototype : Prototype, IBinaryResource
    {
        public Vector3 Position { get; protected set; }

        public void Deserialize(BinaryReader reader)
        {
            Position = reader.Read<Vector3>();
        }
    }
}
