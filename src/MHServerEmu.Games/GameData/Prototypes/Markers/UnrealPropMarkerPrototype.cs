using MHServerEmu.Core.Extensions;

namespace MHServerEmu.Games.GameData.Prototypes.Markers
{
    public class UnrealPropMarkerPrototype : MarkerPrototype
    {
        public string UnrealClassName { get; protected set; }
        public string UnrealQualifiedName { get; protected set; }
        public string UnrealArchetypeName { get; protected set; }

        //---

        public override void Deserialize(BinaryReader reader)
        {
            UnrealClassName = reader.ReadFixedString32();
            UnrealQualifiedName = reader.ReadFixedString32();
            UnrealArchetypeName = reader.ReadFixedString32();

            base.Deserialize(reader);
        }
    }
}
