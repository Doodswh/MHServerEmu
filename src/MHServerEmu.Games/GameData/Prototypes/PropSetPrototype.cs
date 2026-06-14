using MHServerEmu.Core.Extensions;
using MHServerEmu.Games.GameData.Resources;

namespace MHServerEmu.Games.GameData.Prototypes
{
    public class PropSetPrototype : Prototype, IBinaryResource
    {
        public PropSetTypeListPrototype[] PropShapeLists { get; protected set; }
        public string PropSetPackage { get; protected set; }

        //---

        public void Deserialize(BinaryReader reader)
        {
            PropShapeLists = BinaryResourceSerializer.ReadPrototypeContainer<PropSetTypeListPrototype>(reader);
            PropSetPackage = reader.ReadFixedString32();
        }
    }

    public class PropSetTypeListPrototype : Prototype, IBinaryResource
    {
        public PropSetTypeEntryPrototype[] PropShapeEntries { get; protected set; }
        public PrototypeGuid PropType { get; protected set; }

        //---

        public void Deserialize(BinaryReader reader)
        {
            PropShapeEntries = BinaryResourceSerializer.ReadPrototypeContainer<PropSetTypeEntryPrototype>(reader);
            PropType = (PrototypeGuid)reader.ReadUInt64();
        }
    }

    public class PropSetTypeEntryPrototype : Prototype, IBinaryResource
    {
        public string NameId { get; protected set; }
        public string ResourcePackage { get; protected set; }

        //---

        public void Deserialize(BinaryReader reader)
        {
            NameId = reader.ReadFixedString32();
            ResourcePackage = reader.ReadFixedString32();
        }
    }
}
