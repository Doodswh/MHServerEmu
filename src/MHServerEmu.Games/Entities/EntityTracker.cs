using System.Collections;
using MHServerEmu.Core.Collections;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.Common;
using MHServerEmu.Games.Dialog;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Games.Entities
{
    public enum EntityTrackerOptions
    {
        None,
        IncludeDestroyed
    }

    public readonly struct EntityTrackingData
    {
        public readonly Dictionary<ulong, EntityTrackingFlag> Entities;
        public readonly SortedVector<ulong> Hotspots;

        public EntityTrackingData()
        {
            Entities = new();
            Hotspots = new();
        }
    }

    public class EntityTracker
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private readonly Region _region;
        private readonly Dictionary<PrototypeId, EntityTrackingData> _contextTrackingDataMap = new();

        // Iterators are reused instead of allocated for every Iterate() call
        private readonly List<Iterator> _activeIterators = new();
        private readonly Stack<Iterator> _inactiveIterators = new();

        public EntityTracker(Region region)
        {
            _region = region;
        }

        public void ConsiderForTracking(WorldEntity entity)
        {
            if (entity == null || entity.IsTrackable == false) return;

            var entityTracking = entity.TrackingContextMap;
            bool hasOldTracking = entityTracking.Count > 0;

            // Scratch maps come from a pool: this runs every time an entity's tracking may have changed
            EntityTrackingContextMap interactionTracking = EntityTrackingContextMap.Rent();
            EntityTrackingContextMap newTracking = EntityTrackingContextMap.Rent();
            EntityTrackingContextMap oldTracking = EntityTrackingContextMap.Rent();

            try
            {
                bool hasNewTracking = GameDatabase.InteractionManager.GetEntityContextInvolvement(entity, interactionTracking);

                if (hasNewTracking)
                {
                    foreach (var kvp in interactionTracking)
                        newTracking[kvp.Key] = kvp.Value;

                    if (hasOldTracking)
                        foreach (var kvp in entityTracking)
                            if (interactionTracking.ContainsKey(kvp.Key) == false)
                                oldTracking[kvp.Key] = kvp.Value;
                }
                else if (hasOldTracking)
                {
                    foreach (var kvp in entityTracking)
                        oldTracking[kvp.Key] = kvp.Value;
                }

                foreach (var kvp in newTracking)
                {
                    var contextRef = kvp.Key;
                    if (contextRef == PrototypeId.Invalid) continue;

                    if (ShouldTrackContext(contextRef))
                    {
                        InsertEntityIntoContextMap(contextRef, entity, kvp.Value);
                        entity.ModifyTrackingContext(contextRef, kvp.Value);
                    }
                }

                foreach (var kvp in oldTracking)
                {
                    var contextRef = kvp.Key;
                    if (contextRef == PrototypeId.Invalid) continue;

                    RemoveEntityFromContextMap(contextRef, entity);
                    entity.ModifyTrackingContext(contextRef, EntityTrackingFlag.None);
                }
            }
            finally
            {
                EntityTrackingContextMap.Return(interactionTracking);
                EntityTrackingContextMap.Return(newTracking);
                EntityTrackingContextMap.Return(oldTracking);
            }
        }

        public void RemoveFromTracking(WorldEntity entity)
        {
            foreach (var kvp in entity.TrackingContextMap)
            {
                var contextRef = kvp.Key;
                if (contextRef == PrototypeId.Invalid) continue;
                RemoveEntityFromContextMap(contextRef, entity);
            }
            entity.TrackingContextMap.Clear();
        }

        private bool ShouldTrackContext(PrototypeId contextRef)
        {
            if (_region == null) return false;
            var openProto = GameDatabase.GetPrototype<OpenMissionPrototype>(contextRef);
            if (openProto != null && openProto.IsActiveInRegion(_region.Prototype) == false) return false;
            return true;
        }

        public SortedVector<ulong> HotspotsForContext(PrototypeId contextRef)
        {
            if (_contextTrackingDataMap.TryGetValue(contextRef, out var data))
                return data.Hotspots;
            return null;
        }

        public void ModifyTrackingContext(WorldEntity entity, PrototypeId contextRef, EntityTrackingFlag flags)
        {
            if (entity == null) return;

            if (flags != EntityTrackingFlag.None)
                InsertEntityIntoContextMap(contextRef, entity, flags);
            else
                RemoveEntityFromContextMap(contextRef, entity);

            entity.ModifyTrackingContext(contextRef, flags);
        }

        private void InsertEntityIntoContextMap(PrototypeId contextRef, WorldEntity entity, EntityTrackingFlag flags)
        {
            if (entity == null || flags == EntityTrackingFlag.None) return;

            if (_contextTrackingDataMap.TryGetValue(contextRef, out var data) == false)
            {
                data = new();
                _contextTrackingDataMap.Add(contextRef, data);
            }
            else
            {
                // There can be iterators to invalidate only if tracking data already exists
                InvalidateIterators(contextRef);
            }

            ulong entityId = entity.Id;
            data.Entities[entityId] = flags;

            if (entity is Hotspot hotspot && hotspot.IsMissionHotspot)
                data.Hotspots.Add(entityId);
        }

        private void RemoveEntityFromContextMap(PrototypeId contextRef, WorldEntity entity)
        {
            if (entity == null) return;
            if (_contextTrackingDataMap.TryGetValue(contextRef, out var data) == false) return;

            var entityId = entity.Id;
            if (data.Entities.ContainsKey(entityId) == false)
            {
                Logger.Warn($"Unable to find entity to remove. ENTITYID={entityId} CONTEXT={GameDatabase.GetFormattedPrototypeName(contextRef)} TRACKER={contextRef}");
                return;
            }

            InvalidateIterators(contextRef);

            data.Entities.Remove(entityId);
            data.Hotspots.Remove(entityId);
        }

        /// <summary>
        /// Returns a pooled iterator over the entities tracked for <paramref name="contextRef"/>. Use it in a foreach
        /// (which disposes it and returns it to the pool). Entities may be added or removed while iterating.
        /// </summary>
        public Iterator Iterate(PrototypeId contextRef, EntityTrackingFlag flags = EntityTrackingFlag.None,
            EntityTrackerOptions options = EntityTrackerOptions.None)
        {
            Iterator iterator = _inactiveIterators.Count > 0 ? _inactiveIterators.Pop() : new(this);
            iterator.Initialize(contextRef, flags, options);
            return iterator;
        }

        private void InvalidateIterators(PrototypeId contextRef)
        {
            if (_activeIterators.Count == 0)
                return;

            foreach (Iterator iterator in _activeIterators)
            {
                if (iterator.ContextRef == contextRef)
                    iterator.IsOutOfDate = true;
            }
        }

        public sealed class Iterator : IEnumerator<WorldEntity>
        {
            private readonly EntityTracker _tracker;
            private readonly EntityManager _entityManager;

            private Dictionary<ulong, EntityTrackingFlag> _entities;
            private EntityTrackingFlag _flags;
            private EntityTrackerOptions _options;

            // A sorted snapshot of entity ids that mimics the original std::map based implementation.
            // When an entity is added or removed, the snapshot is marked out of date and rebuilt.
            private readonly List<ulong> _entityIds = new();
            private int _index;
            private ulong _lastEntityId;

            private bool _isActive;

            public WorldEntity Current { get; private set; }
            object IEnumerator.Current { get => Current; }

            public PrototypeId ContextRef { get; private set; }
            public bool IsOutOfDate { get; set; }

            public Iterator(EntityTracker tracker)
            {
                _tracker = tracker;
                _entityManager = tracker._region.Game.EntityManager;
            }

            public Iterator GetEnumerator()
            {
                return this;
            }

            public void Initialize(PrototypeId contextRef, EntityTrackingFlag flags, EntityTrackerOptions options)
            {
                if (_isActive)
                {
                    Logger.Warn("Initialize(): Iterator is already active");
                    return;
                }

                _tracker._activeIterators.Add(this);
                _isActive = true;

                if (contextRef == PrototypeId.Invalid)
                    return;

                ContextRef = contextRef;
                _flags = flags;
                _options = options;

                if (_tracker._contextTrackingDataMap.TryGetValue(contextRef, out EntityTrackingData trackingData) == false)
                    return;

                _entities = trackingData.Entities;

                Reset();
            }

            public void Dispose()
            {
                if (_isActive == false)
                    return;

                ContextRef = default;
                _entities = default;
                _flags = default;
                _options = default;
                IsOutOfDate = false;

                Reset();

                _tracker._activeIterators.Remove(this);
                _tracker._inactiveIterators.Push(this);
                _isActive = false;
            }

            public bool MoveNext()
            {
                if (_entities == null)
                    return false;

                if (IsOutOfDate)
                {
                    Reset();
                    RestoreIndex();
                    IsOutOfDate = false;
                }

                while (++_index < _entityIds.Count)
                {
                    ulong entityId = _entityIds[_index];

                    if (_entities.TryGetValue(entityId, out EntityTrackingFlag itFlags) == false)
                        continue;

                    if (_flags != EntityTrackingFlag.None && ((_flags & itFlags) == 0))
                        continue;

                    WorldEntity entity = _entityManager.GetEntity<WorldEntity>(entityId);
                    if (entity == null)
                        continue;

                    if (_options.HasFlag(EntityTrackerOptions.IncludeDestroyed) == false && entity.IsDestroyed)
                        continue;

                    _lastEntityId = entityId;
                    Current = entity;
                    return true;
                }

                _lastEntityId = 0;
                Current = null;
                return false;
            }

            public void Reset()
            {
                _index = -1;
                Current = null;

                _entityIds.Clear();
                if (_entities != null)
                {
                    _entityIds.AddRange(_entities.Keys);
                    _entityIds.Sort();
                }
            }

            private void RestoreIndex()
            {
                if (_lastEntityId == 0)
                    return;

                _index = -1;

                for (int i = 0; i < _entityIds.Count; i++)
                {
                    ulong entityId = _entityIds[i];

                    if (entityId == _lastEntityId)
                    {
                        // Point to the same id if it's still here
                        _index = i;
                        break;
                    }
                    else if (entityId > _lastEntityId)
                    {
                        // Point to the id before the next one if the last current entity was removed
                        _index = i - 1;
                        break;
                    }
                }

                // Every remaining id is smaller than the last one: we are past the end
                if (_index == -1 && _entityIds.Count > 0 && _entityIds[^1] < _lastEntityId)
                    _index = _entityIds.Count - 1;
            }
        }
    }
}
