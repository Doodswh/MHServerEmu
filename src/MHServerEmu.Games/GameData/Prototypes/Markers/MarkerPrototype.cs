using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.GameData.Resources;

namespace MHServerEmu.Games.GameData.Prototypes.Markers
{
    /// <summary>
    /// Base class for all MarkerPrototypes.
    /// </summary>
    public class MarkerPrototype : Prototype, IBinaryResource
    {
        public Vector3 Position { get; protected set; }
        public Orientation Rotation { get; protected set; }

        //---

        public virtual void Deserialize(BinaryReader reader)
        {
            Position = reader.Read<Vector3>();
            Rotation = reader.Read<Orientation>();
        }
    }

    public class MarkerFilterPrototype : Prototype
    {
    }

    public class MarkerSetPrototype : Prototype, IBinaryResource
    {
        public MarkerPrototype[] Markers { get; protected set; }

        //---

        public void Deserialize(BinaryReader reader)
        {
            Markers = BinaryResourceSerializer.ReadPrototypeContainer<MarkerPrototype>(reader);
        }

        public void GetContainedEntities(HashSet<PrototypeId> refs)
        {
            if (Markers.HasValue())
            {
                foreach (var marker in Markers)
                {
                    if ((marker is EntityMarkerPrototype entityMarkerProto) == false) continue;
                    var guid = entityMarkerProto.EntityGuid;
                    if (guid == PrototypeGuid.Invalid) continue;
                    var entityRef = GameDatabase.GetDataRefByPrototypeGuid(guid);
                    if (entityRef == PrototypeId.Invalid) continue;

                    refs.Add(entityRef);
                }
            }
        }
    }
}
