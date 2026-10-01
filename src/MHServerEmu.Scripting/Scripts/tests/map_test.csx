// Admin test for script-built maps (RegionGeneratingArgs.BuildLayout). Sends you into a private copy of an empty test region
// whose map is drawn below from a grid cell set, then lists the marked rooms.
//
//   !maptest [layout] [cellset]   build and enter a map (layouts: cross, ring, arena; cellset: a folder under Resource/Cells)
//   !maptest info                 where the marked rooms are, and how far you are from each
//   !maptest where                the cell you stand in (and its exits) and the arrival spot
//
// Map legend: # = solid, S = arrival, any other character = a room (letters can be looked up afterwards).

using System.Collections.Concurrent;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.Entities.Avatars;

const string HostRegion     = "Regions/ZZZDevelopment/WhiteRoom/BlackRoomRegion.prototype";
const string DefaultCellSet = "EndGame/Limbo/Limbo_A";
const string Difficulty     = "Difficulty/Tiers/Tier3Superheroic.prototype";
const int    MapLevel       = 60;

var Layouts = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
{
    ["cross"] = new[]
    {
        "###P###",
        "###.###",
        "N..K..N",
        "###.###",
        "###S###",
    },
    ["ring"] = new[]
    {
        "P.....P",
        ".##.##.",
        ".#.K.#.",
        ".##.##.",
        "N..S..N",
    },
    ["arena"] = new[]
    {
        "#P...P#",
        "#.....#",
        "N..K..N",
        "#.....#",
        "##.S.##",
    },
};

// seed => (layout, cell set) of a map that is about to be built
var pending = new ConcurrentDictionary<int, (string[] Rows, string CellSet)>();

Hooks.On(ScriptHooks.RegionGenerating, e =>
{
    if (e.RegionName.Contains("BlackRoomRegion", StringComparison.OrdinalIgnoreCase) == false)
        return;

    if (pending.TryRemove(e.Seed, out var map) == false)
        return;

    e.BuildLayout(map.CellSet, map.Rows);
    e.SetLevel(MapLevel);
    Log.Info($"Building a {map.Rows.Length}x{map.Rows.Max(r => r.Length)} map from [{map.CellSet}] (seed {e.Seed})");
});

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "maptest")
        return;

    e.Handled = true;
    if (ScriptHooks.IsAdmin(e.Player) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    Avatar avatar = e.Player.CurrentAvatar;
    Region region = avatar?.Region;
    string arg = e.GetArg(0);

    if (arg.Equals("where", StringComparison.OrdinalIgnoreCase))
    {
        ScriptedLayout map = ScriptTeleport.GetBuiltMap(region);
        Cell cell = avatar?.Cell;
        Vector3 here = avatar.RegionLocation.Position;
        e.Reply($"You: {here}, cell [{cell?.PrototypeName ?? "none"}] exits {cell?.Prototype?.Type}, cell center {cell?.RegionBounds.Center}");

        if (map != null && region.TryGetBuiltMapStartPosition(out Vector3 start))
            e.Reply($"Arrival spot {start} ({Vector3.Distance2D(start, here):0} from you), S room center {map.StartPosition}");
        return;
    }

    if (arg.Equals("info", StringComparison.OrdinalIgnoreCase))
    {
        ScriptedLayout map = ScriptTeleport.GetBuiltMap(region);
        if (map == null)
        {
            e.Reply("This region is not a script-built map.");
            return;
        }

        Vector3 here = avatar.RegionLocation.Position;
        e.Reply($"Built map {map.Width}x{map.Height} from [{map.CellSetPath}], arrival {map.StartPosition}");
        foreach (char c in map.Rows.SelectMany(row => row).Where(char.IsLetterOrDigit).Distinct())
        {
            var spots = map.GetMarkers(c);
            e.Reply($"  '{c}': {spots.Count} room(s), nearest {spots.Min(p => Vector3.Distance2D(p, here)):0} away");
        }
        return;
    }

    string layoutName = string.IsNullOrEmpty(arg) ? "cross" : arg;
    if (Layouts.TryGetValue(layoutName, out string[] rows) == false)
    {
        e.Reply($"Unknown layout. Try: {string.Join(", ", Layouts.Keys)}");
        return;
    }

    string cellSet = string.IsNullOrEmpty(e.GetArg(1)) ? DefaultCellSet : e.GetArg(1);
    int seed = Random.Shared.Next(1, int.MaxValue);
    pending[seed] = (rows, cellSet);

    if (ScriptTeleport.ToBuiltMap(e.Player, HostRegion, Difficulty, seed))
        e.Reply($"Building '{layoutName}' from [{cellSet}]... (then try !maptest info)");
    else
    {
        pending.TryRemove(seed, out _);
        e.Reply("Teleport failed (see the server log).");
    }
});

Log.Info("Map test loaded (!maptest)");
