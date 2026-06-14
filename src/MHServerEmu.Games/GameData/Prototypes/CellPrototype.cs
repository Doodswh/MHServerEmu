using MHServerEmu.Core.Collisions;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.GameData.Prototypes.Markers;
using MHServerEmu.Games.GameData.Resources;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Games.GameData.Prototypes
{
    public class CellPrototype : Prototype, IBinaryResource
    {
        public Aabb BoundingBox { get; protected set; }
        public Cell.Type Type { get; protected set; }
        public Cell.Walls Walls { get; protected set; }
        public Cell.Filler FillerEdges { get; protected set; }
        public Cell.Type RoadConnections { get; protected set; }
        public string ClientMap { get; protected set; }
        public MarkerSetPrototype InitializeSet { get; protected set; } = new();
        public MarkerSetPrototype MarkerSet { get; protected set; } = new();
        public NaviPatchSourcePrototype NaviPatchSource { get; protected set; } = new();
        public bool IsOffsetInMapFile { get; protected set; }
        public HeightMapPrototype HeightMap { get; protected set; } = new();
        public PrototypeGuid[] HotspotPrototypes { get; protected set; }

        //---

        public bool HasNavigationData { get; private set; }

        public void Deserialize(BinaryReader reader)
        {
            Vector3 max = reader.Read<Vector3>();
            Vector3 min = reader.Read<Vector3>();
            BoundingBox = new(min, max);
            Type = (Cell.Type)reader.ReadUInt32();
            Walls = (Cell.Walls)reader.ReadUInt32();
            FillerEdges = (Cell.Filler)reader.ReadUInt32();
            RoadConnections = (Cell.Type)reader.ReadUInt32();
            ClientMap = reader.ReadFixedString32();
            InitializeSet.Deserialize(reader);
            MarkerSet.Deserialize(reader);
            NaviPatchSource.Deserialize(reader);
            IsOffsetInMapFile = reader.ReadBoolean();
            HeightMap.Deserialize(reader);

            if (BinaryResourceSerializer.ReadContainerFromBinaryReader(out PrototypeGuid[] hotspotPrototypes, reader))
                HotspotPrototypes = hotspotPrototypes;

            HasNavigationData = NaviPatchSource.NaviPatch.Points.HasValue() || NaviPatchSource.PropPatch.Points.HasValue();
        }
    }

    public class HeightMapPrototype : Prototype, IBinaryResource
    {
        public int HeightMapSizeX { get; protected set; }
        public int HeightMapSizeY { get; protected set; }
        public short[] HeightMapData { get; protected set; }
        public byte[] HotspotData { get; protected set; }

        public void Deserialize(BinaryReader reader)
        {
            HeightMapSizeX = reader.ReadInt32();
            HeightMapSizeY = reader.ReadInt32();

            if (BinaryResourceSerializer.ReadContainerFromBinaryReader(out short[] heightMapData, reader))
                HeightMapData = heightMapData;

            if (BinaryResourceSerializer.ReadContainerFromBinaryReader(out byte[] hotspotData, reader))
                HotspotData = hotspotData;
        }
    }
}
