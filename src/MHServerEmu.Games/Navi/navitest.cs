using System.Diagnostics;
using MHServerEmu.Core.VectorMath;

namespace MHServerEmu.Games.Navi
{
    public static class NaviTest
    {
        public static void RunStressTest(NaviMesh mesh)
        {
            if (mesh == null || !mesh.IsMeshValid)
            {
                NaviSystem.Logger.Error("Stress test aborted: Invalid mesh.");
                return;
            }

            NaviSystem.Logger.Info("Starting Navi Pathfinding Stress Test...");

            // Settings
            int iterations = 10000;
            float radius = 1.0f;
            PathFlags flags = PathFlags.Walk;

            var bounds = mesh.Bounds;
            Vector3 start = bounds.Center;
            Vector3 end = bounds.Min + (bounds.Max - bounds.Min) * 0.8f; 

            Stopwatch sw = new Stopwatch();

            // Warmup (fills the pools)
            NaviPath.CheckCanPathTo(mesh, start, end, radius, flags);

            GC.Collect();
            long memoryBefore = GC.GetTotalMemory(true);

            sw.Start();
            for (int i = 0; i < iterations; i++)
            {
                // Jiggle the end point slightly to prevent total caching if any exists
                Vector3 target = end + new Vector3(i % 5, i % 5, 0);
                NaviPath.CheckCanPathTo(mesh, start, target, radius, flags);
            }
            sw.Stop();

            long memoryAfter = GC.GetTotalMemory(false);

            NaviSystem.Logger.Info($"Test Complete: {iterations} iterations.");
            NaviSystem.Logger.Info($"Total Time: {sw.ElapsedMilliseconds} ms");
            NaviSystem.Logger.Info($"Average Time: {sw.ElapsedMilliseconds / (float)iterations} ms/path");

 
            NaviSystem.Logger.Info($"Approximate Memory Alloc Diff: {(memoryAfter - memoryBefore) / 1024} KB");
        }
    }
}