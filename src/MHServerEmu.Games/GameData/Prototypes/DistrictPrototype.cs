using MHServerEmu.Games.GameData.Prototypes.Markers;
using MHServerEmu.Games.GameData.Resources;

namespace MHServerEmu.Games.GameData.Prototypes
{
    public class DistrictPrototype : Prototype, IBinaryResource
    {
        public MarkerSetPrototype CellMarkerSet { get; protected set; } = new();
        public MarkerSetPrototype MarkerSet { get; protected set; } = new();    // Size is always 0 in all of our files
        public PathCollectionPrototype PathCollection { get; protected set; } = new();

        //---

        public void Deserialize(BinaryReader reader)
        {
            CellMarkerSet.Deserialize(reader);
            MarkerSet.Deserialize(reader);
            PathCollection.Deserialize(reader);
        }
    }
}
