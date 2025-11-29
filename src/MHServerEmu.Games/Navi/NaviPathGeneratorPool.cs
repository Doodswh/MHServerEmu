using System.Collections.Concurrent;

namespace MHServerEmu.Games.Navi
{
    public static class NaviPathGeneratorPool
    {
        private static readonly ConcurrentBag<NaviPathGenerator> _objects = new();

        public static NaviPathGenerator Get(NaviMesh naviMesh)
        {
            if (_objects.TryTake(out NaviPathGenerator item))
            {
                item.SetMesh(naviMesh);
                return item;
            }
            return new NaviPathGenerator(naviMesh);
        }

        public static void Return(NaviPathGenerator item)
        {
            _objects.Add(item);
        }
    }
}