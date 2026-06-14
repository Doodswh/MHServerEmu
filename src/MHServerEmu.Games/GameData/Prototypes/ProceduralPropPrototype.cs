using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.GameData.Prototypes.Markers;
using MHServerEmu.Games.GameData.Resources;

namespace MHServerEmu.Games.GameData.Prototypes
{
    public class PropPackagePrototype : Prototype, IBinaryResource
    {
        public ProceduralPropGroupPrototype[] PropGroups { get; protected set; }

        //---

        private readonly Dictionary<uint, ProceduralPropGroupPrototype> _propGroupMap = new();

        public void Deserialize(BinaryReader reader)
        {
            PropGroups = BinaryResourceSerializer.ReadPrototypeContainer<ProceduralPropGroupPrototype>(reader);
        }

        public override void PostProcess()
        {
            base.PostProcess();
            //if (GameDatabase.DataDirectory.PrototypeIsAbstract(GetDataRef())){ return;}

            if (PropGroups.HasValue())
            {
                foreach (ProceduralPropGroupPrototype propGroup in PropGroups)
                {
                    if (propGroup != null && propGroup.NameId != null)
                    {
                        string str = propGroup.NameId.ToLower();
                        _propGroupMap.Add(HashHelper.Djb2(str), propGroup);   // str.Hash()
                    }
                }
            }
        }

        public ProceduralPropGroupPrototype GetPropGroupFromName(string nameId)
        {
            string name = nameId.ToLower();
            if (_propGroupMap.TryGetValue(HashHelper.Djb2(name), out var value))  // name.Hash()
            {
                if (value is ProceduralPropGroupPrototype proto) return proto;
            }
            return null;
        }
    }

    public class ProceduralPropGroupPrototype : Prototype, IBinaryResource
    {
        public string NameId { get; protected set; }
        public string PrefabPath { get; protected set; }
        public Vector3 MarkerPosition { get; protected set; }
        public Vector3 MarkerRotation { get; protected set; }
        public MarkerSetPrototype Objects { get; protected set; } = new();
        public NaviPatchSourcePrototype NaviPatchSource { get; protected set; } = new();
        public short RandomRotationDegrees { get; protected set; }
        public short RandomPosition { get; protected set; }

        //---

        public void Deserialize(BinaryReader reader)
        {
            NameId = reader.ReadFixedString32();
            PrefabPath = reader.ReadFixedString32();
            MarkerPosition = reader.Read<Vector3>();
            MarkerRotation = reader.Read<Vector3>();
            Objects.Deserialize(reader);
            NaviPatchSource.Deserialize(reader);
            RandomRotationDegrees = reader.ReadInt16();
            RandomPosition = reader.ReadInt16();
        }
    }
}
