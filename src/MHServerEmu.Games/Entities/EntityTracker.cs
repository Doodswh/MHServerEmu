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
        private readonly Region _region;
        private readonly Dictionary<PrototypeId, EntityTrackingData> _contextTrackingDataMap = new();

        private readonly List<Iterator> _activeIterators = new();
        private readonly Stack<Iterator> _inactiveIterators = new();

        public EntityTracker(Region region)
        {
            _region = region;
        }

        public void ConsiderForTracking(WorldEntity entity)
        {
            if (!Verify.IsNotNull(entity)) return;

            if (entity.IsTrackable == false)
                return;

            EntityTrackingContextMap entityTracking = entity.TrackingContextMap;
            bool hasOldTracking = entityTracking.Count > 0;

            using var interactionTrackingHandle = EntityTrackingContextMapPool.Get(out EntityTrackingContextMap interactionTracking);
            bool hasNewTracking = GameDatabase.InteractionManager.GetEntityContextInvolvement(entity, interactionTracking);

            using var insertMapHandle = EntityTrackingContextMapPool.Get(out EntityTrackingContextMap insertMap);
            using var removeMapHandle = EntityTrackingContextMapPool.Get(out EntityTrackingContextMap removeMap);

            if (hasNewTracking)
            {
                foreach (var kvp in interactionTracking)
                    insertMap[kvp.Key] = kvp.Value;

                if (hasOldTracking)
                {
                    foreach (var kvp in entityTracking)
                    {
                        if (interactionTracking.ContainsKey(kvp.Key) == false)
                            removeMap[kvp.Key] = kvp.Value;
                    }
                }
            }
            else if (hasOldTracking)
            {
                foreach (var kvp in entityTracking)
                    removeMap[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in insertMap)
            {
                PrototypeId contextRef = kvp.Key;
                if (!Verify.IsTrue(contextRef != PrototypeId.Invalid))
                    continue;

                if (ShouldTrackContext(contextRef))
                {
                    InsertEntityIntoContextMap(contextRef, entity, kvp.Value);
                    entity.ModifyTrackingContext(contextRef, kvp.Value);
                }
            }

            foreach (var kvp in removeMap)
            {
                PrototypeId contextRef = kvp.Key;
                if (!Verify.IsTrue(contextRef != PrototypeId.Invalid))
                    continue;

                RemoveEntityFromContextMap(contextRef, entity);
                entity.ModifyTrackingContext(contextRef, EntityTrackingFlag.None);
            }
        }

        public void RemoveFromTracking(WorldEntity entity)
        {
            if (!Verify.IsNotNull(entity)) return;

            foreach (var kvp in entity.TrackingContextMap)
            {
                PrototypeId contextRef = kvp.Key;
                if (!Verify.IsTrue(contextRef != PrototypeId.Invalid))
                    continue;

                RemoveEntityFromContextMap(contextRef, entity);
            }

            entity.TrackingContextMap.Clear();
        }

        private bool ShouldTrackContext(PrototypeId contextRef)
        {
            if (!Verify.IsNotNull(_region)) return false;

            OpenMissionPrototype openProto = contextRef.As<OpenMissionPrototype>();
            if (openProto != null && openProto.IsActiveInRegion(_region.Prototype) == false)
                return false;
            
            return true;
        }

        public SortedVector<ulong> HotspotsForContext(PrototypeId contextRef)
        {
            if ( _contextTrackingDataMap.TryGetValue(contextRef, out var data))
                return data.Hotspots;
            return null;
        }

        public void ModifyTrackingContext(WorldEntity entity, PrototypeId contextRef, EntityTrackingFlag flags)
        {
            if (!Verify.IsNotNull(entity)) return;

            if (flags != EntityTrackingFlag.None)
                InsertEntityIntoContextMap(contextRef, entity, flags);
            else
                RemoveEntityFromContextMap(contextRef, entity);

            entity.ModifyTrackingContext(contextRef, flags);
        }

        private void InsertEntityIntoContextMap(PrototypeId contextRef, WorldEntity entity, EntityTrackingFlag flags)
        {
            if (!Verify.IsNotNull(entity)) return;
            if (!Verify.IsTrue(flags != EntityTrackingFlag.None)) return;
            
            if (_contextTrackingDataMap.TryGetValue(contextRef, out EntityTrackingData data) == false)
            {
                data = new();
                _contextTrackingDataMap.Add(contextRef, data);
            }
            else
            {
                // There can be invalid iterators to invalidate only if tracking data already exists.
                InvalidateIterators(contextRef);
            }

            ulong entityId = entity.Id;
            data.Entities[entityId] = flags;

            if (entity is Hotspot hotspot && hotspot.IsMissionHotspot)
                data.Hotspots.Add(entityId);
        }

        private void RemoveEntityFromContextMap(PrototypeId contextRef, WorldEntity entity)
        {
            if (!Verify.IsNotNull(entity)) return;
            if (!Verify.IsTrue(_contextTrackingDataMap.TryGetValue(contextRef, out EntityTrackingData data))) return;

            ulong entityId = entity.Id;
            if (!Verify.IsTrue(data.Entities.ContainsKey(entityId), $"Unable to find entity to remove. ENTITYID={entityId} CONTEXT={contextRef.GetNameFormatted()} TRACKER={this}"))
                return;

            InvalidateIterators(contextRef);

            data.Entities.Remove(entityId);
            data.Hotspots.Remove(entityId);
        }

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

            // We keep a sorted snapshot of entity ids to mimic the original std::map based implementation.
            // When an entity is added or removed, this snapshot gets invalidated and rebuilt.
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
                if (!Verify.IsTrue(_isActive == false)) return;

                _tracker._activeIterators.Add(this);
                _isActive = true;

                if (!Verify.IsTrue(contextRef != PrototypeId.Invalid)) return;

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
                if (Verify.IsTrue(_isActive))
                {
                    ContextRef = default;
                    _entities = default;
                    _flags = default;
                    _options = default;

                    Reset();

                    Verify.IsTrue(_tracker._activeIterators.Remove(this));
                    _tracker._inactiveIterators.Push(this);
                    _isActive = false;
                }
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

                    // This verify firing would mean our snapshot somehow got out of date without us noticing.
                    if (!Verify.IsTrue(_entities.TryGetValue(entityId, out EntityTrackingFlag itFlags)))
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
                        // Point to the id before next if the last current entity was removed
                        _index = i - 1;
                        break;
                    }
                }
            }
        }
    }
}
