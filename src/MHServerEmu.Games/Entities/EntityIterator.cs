using System.Collections;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Games.Entities
{
    public readonly struct EntityIterator : IEnumerable<Entity>
    {
        private readonly EntityManager _entityManager;
        private readonly Area _area;
        private readonly Cell _cell;
        private readonly Region _region;

        public EntityIterator(EntityManager entityManager)
        {
            _entityManager = entityManager;
            _area = null;
            _cell = null;
            _region = null;
        }

        public EntityIterator(EntityManager entityManager, Area area)
        {
            _entityManager = entityManager;
            _area = area;
            _cell = null;
            _region = null;
        }

        public EntityIterator(EntityManager entityManager, Cell cell)
        {
            _entityManager = entityManager;
            _area = null;
            _cell = cell;
            _region = null;
        }

        public EntityIterator(EntityManager entityManager, Region region)
        {
            _entityManager = entityManager;
            _area = null;
            _cell = null;
            _region = region;
        }

        public Enumerator GetEnumerator()
        {
            return new(_entityManager, _area, _cell, _region);
        }

        IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<Entity>
        {
            private readonly EntityManager _entityManager;
            private readonly Area _area;
            private readonly Cell _cell;
            private readonly Region _region;

            private Dictionary<ulong, Entity>.ValueCollection.Enumerator _subEnumerator;

            public Entity Current { get; private set; }
            object IEnumerator.Current { get => Current; }

            public Enumerator(EntityManager entityManager, Area area, Cell cell, Region region)
            {
                _entityManager = entityManager;
                _area = area;
                _cell = cell;
                _region = region;

                _subEnumerator = _entityManager.GetEnumerator();
            }

            public void Dispose()
            {
                _subEnumerator.Dispose();
            }

            public bool MoveNext()
            {
                while (_subEnumerator.MoveNext())
                {
                    Entity entity = _subEnumerator.Current;
                    WorldEntity worldEntity = entity as WorldEntity;

                    if (_area != null)
                    {
                        if (worldEntity == null || worldEntity.Area != _area)
                            continue;
                    }

                    if (_cell != null)
                    {
                        if (worldEntity == null || worldEntity.Cell != _cell)
                            continue;
                    }

                    if (_region != null)
                    {
                        if (worldEntity == null || worldEntity.Region != _region)
                            continue;
                    }

                    Current = entity;
                    return true;
                }

                Current = null;
                return false;
            }

            public void Reset()
            {
                _subEnumerator.Dispose();
                _subEnumerator = _entityManager.GetEnumerator();
            }
        }
    }
}
