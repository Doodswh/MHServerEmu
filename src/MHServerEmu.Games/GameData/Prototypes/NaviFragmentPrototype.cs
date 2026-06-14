using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.GameData.Resources;
using MHServerEmu.Games.Navi;

namespace MHServerEmu.Games.GameData.Prototypes
{
    public class NaviFragmentPrototype : Prototype, IBinaryResource
    {
        public NaviFragmentPolyPrototype[] FragmentPolys { get; protected set; }
        public NaviFragmentPolyPrototype[] PropFragmentPolys { get; protected set; }

        //---

        public void Deserialize(BinaryReader reader)
        {
            FragmentPolys = BinaryResourceSerializer.ReadPrototypeContainer<NaviFragmentPolyPrototype>(reader);
            PropFragmentPolys = BinaryResourceSerializer.ReadPrototypeContainer<NaviFragmentPolyPrototype>(reader);
        }
    }

    public class NaviFragmentPolyPrototype : Prototype, IBinaryResource
    {
        public NaviContentTags ContentTag { get; protected set; }
        public Vector3[] Points { get; protected set; }

        //---

        public void Deserialize(BinaryReader reader)
        {
            ContentTag = (NaviContentTags)reader.ReadInt32();
            Points = BinaryResourceSerializer.ReadVectorFromBinaryReader<Vector3>(reader);
        }
    }
}
