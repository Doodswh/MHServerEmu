using MHServerEmu.Core.Extensions;

namespace MHServerEmu.Games.GameData.Prototypes.Markers
{
    public class ResourceMarkerPrototype : MarkerPrototype
    {
        public string Resource { get; protected set; }

        //---

        public override void Deserialize(BinaryReader reader)
        {
            Resource = reader.ReadFixedString32();

            base.Deserialize(reader);
        }
    }
}
