using System.Collections;
using MHServerEmu.Core.Collisions;

namespace MHServerEmu.Games.Regions
{
    public readonly struct AreaIterator
    {
        private readonly Region _region;
        private readonly Aabb? _bounds;

        public AreaIterator(Region region, Aabb? bounds)
        {
            _region = region;
            _bounds = bounds;
        }

        public Enumerator GetEnumerator()
        {
            return new(_region, _bounds);
        }

        public struct Enumerator : IEnumerator<Area>
        {
            private readonly Region _region;
            private readonly Aabb? _bounds;

            private Dictionary<uint, Area>.ValueCollection.Enumerator _subEnumerator;

            public Area Current { get; private set; }
            object IEnumerator.Current { get => Current;  }

            public Enumerator(Region region, Aabb? bounds)
            {
                _region = region;
                _bounds = bounds;

                _subEnumerator = _region.Areas.Values.GetEnumerator();
            }

            public void Dispose()
            {
                _subEnumerator.Dispose();
            }

            public bool MoveNext()
            {
                while (_subEnumerator.MoveNext())
                {
                    Area area = _subEnumerator.Current;

                    if (_bounds != null && area.RegionBounds.Intersects(_bounds.Value) == false)
                        continue;

                    Current = area;
                    return true;
                }

                Current = null;
                return false;
            }

            public void Reset()
            {
                _subEnumerator.Dispose();
                _subEnumerator = _region.Areas.Values.GetEnumerator();
            }
        }
    }
}
