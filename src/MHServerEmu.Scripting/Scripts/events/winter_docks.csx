// Winter Docks: makes the unused winter Brooklyn Docks patrol (BrooklynPatrolRegionWinterTest) buildable.
//
// The region is a random grid filled from two winter tile folders, but those folders are missing several tile shapes
// (Dockyard 5 of 10, Shipping 3 of 10), and the grid generator gives up when it needs a shape it doesn't have. This
// lends the normal Docks Patrol folders as a backup: only the missing shapes are taken from them, everything else
// stays winter.
//
// The bridge (and the spawn point on it) has no client maps at all; that is fixed with a data patch, not here:
// Data/Game/Patches/PatchDataWinterDocksTest.json (server restart).
//
// Takes effect for instances created after it loads. Removing the script keeps the fallbacks until a restart.
//   Test: !region warp BrooklynPatrolRegionWinterTest

using MHServerEmu.Games.DRAG;

var fallbacks = new (string Winter, string Normal)[]
{
    ("EndGame/BrooklynDocksPatrol_Winter/DocksPatrol_Dockyard_A", "EndGame/BrooklynDocksPatrol/DocksPatrol_Dockyard_A"),
    ("EndGame/BrooklynDocksPatrol_Winter/DocksPatrol_Shipping_A", "EndGame/BrooklynDocksPatrol/DocksPatrol_Shipping_A"),
};

foreach (var fallback in fallbacks)
    CellSetRegistry.CellSetFallbacks[fallback.Winter] = fallback.Normal;

// TEMP: detailed map generation log for this region, to see why the Shipping area fails. Remove once it builds.
MHServerEmu.Games.Regions.RegionManager.GenerationLogFor["BrooklynPatrolRegionWinterTest"] = true;
MHServerEmu.Games.Regions.RegionManager.GenerationLogFor["BrooklynPatrolRegionL60"] = true; // the normal docks, for comparison

Log.Info($"Winter Docks: {fallbacks.Length} winter tile folders will borrow missing shapes from the normal Docks Patrol");
