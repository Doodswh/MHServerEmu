using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.VectorMath;

namespace MHServerEmu.Games.GameData.Prototypes.Markers
{
    public class CellConnectorMarkerPrototype : MarkerPrototype
    {
        public Vector3 Extents { get; protected set; }

        //---

        public override void Deserialize(BinaryReader reader)
        {
            Extents = reader.Read<Vector3>();

            base.Deserialize(reader);
        }
    }
}
