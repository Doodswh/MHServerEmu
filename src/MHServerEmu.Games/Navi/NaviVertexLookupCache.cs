using MHServerEmu.Core.Logging;
using MHServerEmu.Core.VectorMath;

namespace MHServerEmu.Games.Navi
{
    public class NaviVertexLookupCache
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        public readonly struct VertexCacheKey : IEquatable<VertexCacheKey>
        {
            public readonly NaviPoint Point;
            public readonly int X;
            public readonly int Y;

            public VertexCacheKey(NaviPoint point, int x, int y)
            {
                Point = point;
                X = x;
                Y = y;
            }

            public override bool Equals(object obj)
            {
                return obj is VertexCacheKey other && Equals(other);
            }

            public bool Equals(VertexCacheKey other)
            {
                if (X != other.X || Y != other.Y) return false;
                if (Point == null && other.Point == null) return true;
                if (Point == null || other.Point == null) return false;

                return Pred.NaviPointCompare2D(Point.Pos, other.Point.Pos);
            }

            public override int GetHashCode()
            {
                const ulong Magic = 3636507997UL;
                ulong xm = (ulong)X * Magic;
                uint hash = (uint)xm + (uint)(xm >> 32);
                ulong ym = (ulong)Y * Magic;
                hash ^= (uint)ym + (uint)(ym >> 32);
                return (int)hash;
            }

            public static bool operator ==(VertexCacheKey left, VertexCacheKey right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(VertexCacheKey left, VertexCacheKey right)
            {
                return !left.Equals(right);
            }
        }

        private NaviSystem _navi;

        public const float CellSize = 12.0f;
        public const float NaviPointBoxEpsilon = 4.0f;

        public HashSet<VertexCacheKey> _vertexCache;
        private int _maxSize;
        private static readonly ThreadLocal<NaviPoint> _lookupPoint = new(() => new NaviPoint(Vector3.Zero));

        public NaviVertexLookupCache(NaviSystem naviSystem)
        {
            _navi = naviSystem;
            _vertexCache = new();
        }

        public void Clear()
        {
            _vertexCache.Clear();
        }

        public void Initialize(int size)
        {
            size += (size / 10);
            _maxSize = Math.Max(size, 1024);
            int hashSize = (int)(size / 1.0f);
            _vertexCache = new(hashSize);
        }

        public NaviPoint CacheVertex(Vector3 pos, out bool addedOut)
        {
            NaviPoint point = FindVertex(pos);
            if (point != null)
            {
                addedOut = false;
                return point;
            }

            point = new NaviPoint(pos);
            VertexCacheKey entry = new(point, (int)(point.Pos.X / CellSize), (int)(point.Pos.Y / CellSize));

            addedOut = _vertexCache.Add(entry);
            if (addedOut == false)
            {
                if (_navi.Log) Logger.Error($"CacheVertex failed to add point {point} due to existing point {entry.Point}");
                return null;
            }

            return point;
        }

        private VertexCacheKey FindVertexKey(Vector3 pos)
        {
            int x0 = (int)((pos.X - NaviPointBoxEpsilon) / CellSize);
            int x1 = (int)((pos.X + NaviPointBoxEpsilon) / CellSize);
            int y0 = (int)((pos.Y - NaviPointBoxEpsilon) / CellSize);
            int y1 = (int)((pos.Y + NaviPointBoxEpsilon) / CellSize);

            NaviPoint lookupPt = _lookupPoint.Value;
            lookupPt.Pos = pos;

            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                {
                    VertexCacheKey entry = new(lookupPt, x, y);

                    if (_vertexCache.TryGetValue(entry, out var key))
                        return key;
                }

            return default;
        }

        public NaviPoint FindVertex(Vector3 pos)
        {
            VertexCacheKey key = FindVertexKey(pos);
            // key is the struct from the HashSet. key.Point is the REAL point.
            // if key is default, key.Point is null.
            return key.Point;
        }

        public void RemoveVertex(NaviPoint point)
        {
            VertexCacheKey entry = new(point, (int)(point.Pos.X / CellSize), (int)(point.Pos.Y / CellSize));

            if (_vertexCache.TryGetValue(entry, out var key) == false)
            {
                Logger.Warn($"[NaviVertexLookupCache] RemoveVertex failed to find point {point}");
                return;
            }

            if (key.Point != point)
            {
                Logger.Warn($"[NaviVertexLookupCache] RemoveVertex found point {key.Point} which does not match {point}");
                return;
            }

            _vertexCache.Remove(key);
        }

        public void UpdateVertex(NaviPoint point, Vector3 pos)
        {
            RemoveVertex(point);

            point.Pos = pos;
            VertexCacheKey entry = new(point, (int)(point.Pos.X / CellSize), (int)(point.Pos.Y / CellSize));

            bool vertexAdded = _vertexCache.Add(entry);
            if (vertexAdded == false && _vertexCache.TryGetValue(entry, out var key)) // Second try?
            {
                if (key.Point.TestFlag(NaviPointFlags.Attached) == false)
                {
                    Logger.Warn($"[NaviVertexLookupCache] UpdateVertex found old unattached point {key.Point} while updating {point}");
                    return;
                }
                _vertexCache.Remove(key);
                _vertexCache.Add(entry);
            }
        }
    }
}