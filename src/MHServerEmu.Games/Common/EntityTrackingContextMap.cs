using MHServerEmu.Core.Serialization;
using MHServerEmu.Games.Dialog;
using MHServerEmu.Games.GameData;

namespace MHServerEmu.Games.Common
{
    /// <summary>
    /// A <see cref="Dictionary{TKey, TValue}"/> of <see cref="PrototypeId"/> and <see cref="EntityTrackingFlag"/> that implements <see cref="ISerialize"/>.
    /// </summary>
    public class EntityTrackingContextMap : Dictionary<PrototypeId, EntityTrackingFlag>, ISerialize
    {
        // NOTE: Consider making this a wrapper around Dictionary rather than inherit from it.

        public bool Serialize(Archive archive)
        {
            bool success = true;

            ulong numEntries = (ulong)Count;
            success &= Serializer.Transfer(archive, ref numEntries);

            if (archive.IsPacking)
            {
                foreach (var kvp in this)
                {
                    PrototypeId contextRef = kvp.Key;
                    uint flags = (uint)kvp.Value;
                    success &= Serializer.Transfer(archive, ref contextRef);
                    success &= Serializer.Transfer(archive, ref flags);
                }
            }
            else
            {
                Clear();
                for (ulong i = 0; i < numEntries; i++)
                {
                    PrototypeId contextRef = PrototypeId.Invalid;
                    uint flags = 0;
                    success &= Serializer.Transfer(archive, ref contextRef);
                    success &= Serializer.Transfer(archive, ref flags);
                    Add(contextRef, (EntityTrackingFlag)flags);
                }
            }

            return success;
        }

        // Gazillion::EntityTrackingContextMapInsert()
        public void Insert(PrototypeId contextRef, EntityTrackingFlag flag)
        {
            if (ContainsKey(contextRef))
                this[contextRef] |= flag;
            else
                Add(contextRef, flag);
        }

        #region Pooling

        // Scratch maps are needed every time an entity's tracking is reconsidered, so reuse them instead of allocating.
        // Each game runs on its own thread, so a per-thread pool needs no locking.
        [ThreadStatic]
        private static Stack<EntityTrackingContextMap> _pool;

        private const int MaxPooled = 16;

        /// <summary>
        /// Returns an empty map from the pool. Give it back with <see cref="Return"/> (use try/finally).
        /// </summary>
        public static EntityTrackingContextMap Rent()
        {
            return _pool != null && _pool.Count > 0 ? _pool.Pop() : new();
        }

        /// <summary>
        /// Clears <paramref name="map"/> and returns it to the pool. Do not use it afterwards.
        /// </summary>
        public static void Return(EntityTrackingContextMap map)
        {
            if (map == null)
                return;

            map.Clear();

            _pool ??= new();
            if (_pool.Count < MaxPooled)
                _pool.Push(map);
        }

        #endregion
    }
}
