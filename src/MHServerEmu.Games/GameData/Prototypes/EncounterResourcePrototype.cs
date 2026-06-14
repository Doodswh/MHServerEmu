using MHServerEmu.Core.Extensions;
using MHServerEmu.Games.GameData.Prototypes.Markers;
using MHServerEmu.Games.GameData.Resources;

namespace MHServerEmu.Games.GameData.Prototypes
{
    public class EncounterResourcePrototype : Prototype, IBinaryResource
    {
        public PrototypeGuid PopulationMarkerGuid { get; protected set; }
        public string ClientMap { get; protected set; }
        public MarkerSetPrototype MarkerSet { get; protected set; } = new();
        public NaviPatchSourcePrototype NaviPatchSource { get; protected set; } = new();

        //---

        public bool HasEdges { get; private set; }

        public void Deserialize(BinaryReader reader)
        {
            PopulationMarkerGuid = (PrototypeGuid)reader.ReadUInt64();
            ClientMap = reader.ReadFixedString32();
            MarkerSet.Deserialize(reader);
            NaviPatchSource.Deserialize(reader);
        }

        public override void PostProcess()
        {
            base.PostProcess();

            HasEdges = NaviPatchSource.NaviPatch.Edges.HasValue() || NaviPatchSource.PropPatch.Edges.HasValue();
        }
    }
}
