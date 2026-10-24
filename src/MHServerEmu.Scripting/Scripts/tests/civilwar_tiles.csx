// Admin test: which tiles of the Civil War Bazaar can the client load?
//
// The Bazaar (CivilWarBazaarRegion*) freezes the client on the loading screen: its map files reference art packages the
// 1.52 client doesn't have. This builds the real Bazaar region with only the tiles you pick (the others are left out of
// the district) and sends you into a fresh copy, so each tile can be tried on its own. If the entry tile is left out,
// you arrive in the middle of the first remaining tile.
//
//   !cwtiles 3          only tile 3
//   !cwtiles 3,5,8      tiles 3, 5 and 8
//   !cwtiles 3-8        tiles 3 to 8
//   !cwtiles all        every tile (the original Bazaar)
//   !cwtiles lowtown    the Bazaar rebuilt from Madripoor Lowtown tiles (same path, Lowtown art)
//   !cwtiles list       the tiles and their numbers
//   !cwtiles airport [cap | ironman]         the Airport scenario in the Secret Invasion helicarrier hangar (default);
//                                            no side given: your own Civil War side (!civilwar), Team Cap if none
//   ... edge                                 close off the outer edge with helicopter wrecks / missile launchers
//   ... nobackdrop                           don't show the helicarrier's sky / deck backdrop around the hangar
//   ... hangar | helicarrier | plaza | playground | shipping | army | courtyard | projects | rooftop | lowtown   which tiles
//   ... <cell path>                          any cell: a normal tile four times, a DistrictCells piece once at its own spot
//   !cwtiles airport cap | ironman real      ... on its real map (crashes the 1.52 client)
//   ... skip | skip1 | skip1,3               (add to either) no cutscenes sent, or only those (1-3 entrances, 4 victory)
//   ... hold                                 no cutscenes sent and none finished, so no bosses ever spawn
//
// The choice stays for every Bazaar built afterwards, until the next !cwtiles or a server restart.
// Needs the engine change (StaticAreaCellGenerator empty substitution, Region.ArrivalFallbackFor).

using MHServerEmu.Core.Collisions;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Games.DRAG.Generators.Areas;

const string Region  = "CivilWarBazaarRegion60";
const string Folder  = "Resource/Cells/DistrictCells/CivilWar_Bazaar/";

var Tiles = new[]
{
    "CivilWar_Bazaar_1_Entry.cell",
    "CivilWar_Bazaar_2_Market.cell",
    "CivilWar_Bazaar_3_Market.cell",
    "CivilWar_Bazaar_4_Boss1.cell",
    "CivilWar_Bazaar_5_Roof.cell",
    "CivilWar_Bazaar_6_Roof.cell",
    "CivilWar_Bazaar_7_Boss2.cell",
    "CivilWar_Bazaar_8_FinalBoss.cell",
};

MHServerEmu.Games.Regions.Region.ArrivalFallbackFor["CivilWarBazaar"] = true;
MHServerEmu.Games.Regions.Region.ArrivalFallbackFor["CivilWarAirport"] = true;

// The helicarrier hangar's doorway to the rest of the helicarrier (back wall): not placed when the hangar stands in for
// another cell, so nobody walks out mid-fight. The hangar's own exit (DefaultEND) stays and is the Airport's exit.
MHServerEmu.Games.Regions.Cell.StandInSkippedEntities["Entity/Transitions/OpenTransitionSmlSoftFat2.prototype"] = true;

// The Airport (one 4608x4608 tile; its map breaks the 1.52 client) rebuilt as a 2x2 square of tiles, the same size, as an
// open concrete field: four copies of a tile open on all sides, the outer edge closed off with props. Centers are relative
// to the Airport's center. Each tile takes the Airport's boss / chest markers and its exit on its quarter.
const string AirportCell = "Resource/Cells/EndGame/CivilWarScenario/CivilWarScenario_Airport_A.cell";

(string Cell, Vector3 Center)[] Four(string cell) => new[]
{
    (cell, new Vector3(-1152f, -1152f, 0f)), (cell, new Vector3(-1152f, 1152f, 0f)),
    (cell, new Vector3( 1152f, -1152f, 0f)), (cell, new Vector3( 1152f, 1152f, 0f)),
};

// "!cwtiles airport cap <set>": which tile to use (the open center tiles to compare in game)
const string Cells = "Resource/Cells/";
var AirportTileSets = new Dictionary<string, (string Cell, Vector3 Center)[]>(StringComparer.OrdinalIgnoreCase)
{
    ["shipping"]  = Four(Cells + "Brooklyn/Shipping_B/Shipping_B_NESW_C.cell"),
    ["army"]      = Four(Cells + "Fort_Stryker/Army_Base_C/Army_Base_C_NESW_B.cell"),
    ["courtyard"] = Four(Cells + "Latveria/Courtyard_A/Courtyard_A_NESW_D.cell"),
    ["projects"]  = Four(Cells + "MutantTown/Projects_C/Projects_C_NESW_A.cell"),
    ["rooftop"]   = Four(Cells + "Hells_Kitchen_02/Police_Rooftop_A/Police_Rooftop_A_NESW_A.cell"),
    // Secret Invasion helicarrier: district pieces, kept at their own spot (the hangar alone is the Airport's 4608 size)
    ["hangar"]      = new[] { (Cells + "DistrictCells/Helicarrier/Helicarrier_Invasion/Helicarrier_Invasion_X2Y0_Hngr.cell", Vector3.Zero) },
    ["helicarrier"] = new[]
    {
        (Cells + "DistrictCells/Helicarrier/Helicarrier_Invasion/Helicarrier_Invasion_X0Y0.cell", Vector3.Zero),
        (Cells + "DistrictCells/Helicarrier/Helicarrier_Invasion/Helicarrier_Invasion_X1Y0.cell", Vector3.Zero),
        (Cells + "DistrictCells/Helicarrier/Helicarrier_Invasion/Helicarrier_Invasion_X1Y1.cell", Vector3.Zero),
        (Cells + "DistrictCells/Helicarrier/Helicarrier_Invasion/Helicarrier_Invasion_X2Y0_Hngr.cell", Vector3.Zero),
    },
    ["plaza"]     = Four(Cells + "Madripoor/Madripoor_Lower_C/Madripoor_Lower_Boss_A_NESW_A.cell"),
    ["playground"] = Four(Cells + "MutantTown/Slums_C/Slums_Playground_NESW_B.cell"),
    ["lowtown"]   = new[]
    {
        (Cells + "Madripoor/Madripoor_Lower_A/Madripoor_Lower_A_NE_A.cell", new Vector3(-1152f, -1152f, 0f)),
        (Cells + "Madripoor/Madripoor_Lower_A/Madripoor_Lower_A_NW_A.cell", new Vector3(-1152f,  1152f, 0f)),
        (Cells + "Madripoor/Madripoor_Lower_A/Madripoor_Lower_A_ES_A.cell", new Vector3( 1152f, -1152f, 0f)),
        (Cells + "Madripoor/Madripoor_Lower_A/Madripoor_Lower_A_SW_A.cell", new Vector3( 1152f,  1152f, 0f)),
    },
};
const string DefaultAirportTiles = "hangar";
const string AirportArea = "Regions/EndGame/StaticScenarios/CivilWar/Airport/CivilWarAirportArea.prototype";
const string HelicarrierArea = "Regions/Story/CH10SecretInvasion/HelicarrierInvasion/HelicarrierInvasionArea1.prototype";
bool airportEdge = false;   // "edge": close off the outer edge with props (off: the hangar is walled already)

// The outer edge of the rebuilt Airport: helicopter wrecks and missile launchers close together, the middle left empty
// (props the client has models for; placed invulnerable, no loot)
var EdgeProps = new[] { "Entity/Props/Discoveries/HelicopterCrash", "Entity/Props/MultiStageDestructibles/DBAIMFactoryMissileRackA",
                        "Entity/Props/MultiStageDestructibles/DBAIMFactoryMissileRackA" };
const float EdgeInset = 220f;     // from the outer edge
const float EdgeSpacing = 520f;   // between props along the edge
var decorated = new System.Collections.Concurrent.ConcurrentDictionary<ulong, bool>();

void DecorateAirport(Region region)
{
    Aabb box = region.Aabb;
    float minX = box.Min.X + EdgeInset, maxX = box.Max.X - EdgeInset;
    float minY = box.Min.Y + EdgeInset, maxY = box.Max.Y - EdgeInset;
    Vector3 center = box.Center;
    int placed = 0, failed = 0, n = 0;

    void Place(float x, float y)
    {
        string path = EdgeProps[n++ % EdgeProps.Length];
        PrototypeId protoRef = GameDatabase.GetPrototypeRefByName(path + ".prototype");
        if (protoRef == PrototypeId.Invalid) { Log.Warn($"[cwtiles] no prop {path}"); failed++; return; }

        // stay on the edge line: only small nudges onto walkable ground
        if (ScriptSpawner.TryFindSpotNear(region, new Vector3(x, y, center.Z), 0f, 160f, out Vector3 spot) == false) { failed++; return; }

        float yaw = MathHelper.ToDegrees(MathF.Atan2(center.Y - spot.Y, center.X - spot.X));   // facing the middle
        if (ScriptSpawner.SpawnInteractable(region, protoRef, spot, yaw, "cwairport") != null) placed++; else failed++;
    }

    // walk the four sides
    for (float x = minX; x <= maxX; x += EdgeSpacing) { Place(x, minY); Place(x, maxY); }
    for (float y = minY + EdgeSpacing; y <= maxY - EdgeSpacing * 0.5f; y += EdgeSpacing) { Place(minX, y); Place(maxX, y); }

    Log.Info($"[cwtiles] airfield edge: {placed} placed, {failed} failed");
}

// (the visible exit when the Airport / Bazaar is done is placed by events/civil_war_event.csx)

Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    Region region = e.Region;
    if (region == null || region.PrototypeName.Contains("CivilWarAirport", StringComparison.OrdinalIgnoreCase) == false) return;
    if (airportEdge == false) return;
    bool rebuilt = false;
    foreach (var cell in region.Cells)
        if (cell.Settings != null && cell.Settings.StandInSharesOriginal) { rebuilt = true; break; }
    if (rebuilt == false) return;   // real map
    if (decorated.TryAdd(region.Id, true) == false) return;

    ulong regionId = region.Id;
    Game game = region.Game;
    ScriptTimer.After(game, 2f, () =>
    {
        Region current = game.RegionManager.GetRegion(regionId);
        if (current != null) DecorateAirport(current);
    });
});

// The Airport's cutscenes are still sent; if the client never reports one finished (the rebuilt map doesn't have them),
// the server finishes it so the next bosses still spawn
var AirportScenes = new[] { "CivilWar1WarMachineFalconEntrance", "CivilWar2BlackPantherWinterSoldierEntrance",
                            "CivilWar3CapIronManEntrance", "CivilWar4TeamCapVictory", "CivilWar4TeamIronManVictory" };
foreach (string scene in AirportScenes)
    MHServerEmu.Games.Entities.Player.KismetSeqFallbackSeconds[scene] = 30f;

// "!cwtiles lowtown": the Bazaar rebuilt from Madripoor Lowtown tiles (the client has them; the Bazaar's own art is missing).
// The Bazaar is a path along +X: 1-2-3-4-5, then 6 beside 5, then 7 and the big final arena 8 further along +X.
// Openings: N = +X, S = -X, E = +Y, W = -Y.
const string Lowtown = "Resource/Cells/Madripoor/";
var LowtownPlan = new (string Tile, string Substitute)[]
{
    ("CivilWar_Bazaar_1_Entry.cell",     "Madripoor_Lower_B/Madripoor_Lower_B_S_A.cell"),           // dead end, opens N
    ("CivilWar_Bazaar_2_Market.cell",    "Madripoor_Lower_B/Madripoor_Lower_B_NS_A.cell"),          // straight street
    ("CivilWar_Bazaar_3_Market.cell",    "Madripoor_Lower_B/Madripoor_Lower_B_NS_A.cell"),
    ("CivilWar_Bazaar_4_Boss1.cell",     "Madripoor_Lower_B/Madripoor_Lower_B_NS_A.cell"),
    ("CivilWar_Bazaar_5_Roof.cell",      "Madripoor_Lower_A/Madripoor_Lower_A_SW_A.cell"),          // corner: back (S) and side (W)
    ("CivilWar_Bazaar_6_Roof.cell",      "Madripoor_Lower_A/Madripoor_Lower_A_NE_A.cell"),          // corner: side (E) and on (N)
    ("CivilWar_Bazaar_7_Boss2.cell",     "Madripoor_Lower_B/Madripoor_Lower_B_NS_A.cell"),
    ("CivilWar_Bazaar_8_FinalBoss.cell", "Madripoor_Lower_B/Madripoor_Lower_B_N_A.cell"),           // dead end, opens S (Lowtown's boss
};                                                                                                      // square opens all 4 ways: black edges)

bool ApplyLowtown(Action<string> reply)
{
    foreach (var plan in LowtownPlan)
    {
        if (GameDatabase.GetPrototypeRefByName(Lowtown + plan.Substitute) == PrototypeId.Invalid)
        {
            reply($"Lowtown tile not found: {plan.Substitute}");
            return false;
        }
    }

    // The scenario puts Crossbones and the other bosses on the Bazaar tiles' own spawn markers: the stand-ins take them over
    foreach (var plan in LowtownPlan)
    {
        StaticAreaCellGenerator.CellSubstitutions[Folder + plan.Tile] = Lowtown + plan.Substitute;
        StaticAreaCellGenerator.CellSubstitutionSpawnMarkers[Folder + plan.Tile] = true;
    }

    // The final arena is 3456 wide, the stand-in 2304: push the stand-in back against tile 7 (one tile along +X from it)
    CellPrototype boss2 = GameDatabase.GetPrototypeRefByName(Folder + "CivilWar_Bazaar_7_Boss2.cell").As<CellPrototype>();
    CellPrototype arena = GameDatabase.GetPrototypeRefByName(Folder + "CivilWar_Bazaar_8_FinalBoss.cell").As<CellPrototype>();
    if (boss2 != null && arena != null)
    {
        Vector3 wanted = boss2.BoundingBox.Center + new Vector3(2304f, 0f, 0f);
        Vector3 offset = wanted - arena.BoundingBox.Center;
        StaticAreaCellGenerator.CellSubstitutionOffsets[Folder + "CivilWar_Bazaar_8_FinalBoss.cell"] = new Vector3(offset.X, offset.Y, 0f);
        Log.Info($"[cwtiles] final arena stand-in nudged by {offset}");
    }

    return true;
}

void ClearLowtown()
{
    foreach (var plan in LowtownPlan)
    {
        StaticAreaCellGenerator.CellSubstitutionOffsets.TryRemove(Folder + plan.Tile, out _);
        StaticAreaCellGenerator.CellSubstitutionSpawnMarkers.TryRemove(Folder + plan.Tile, out _);
    }
}

// "3", "3,5,8", "3-8", "all" -> tile numbers (1-based); null if it doesn't parse
HashSet<int> Parse(string spec)
{
    var result = new HashSet<int>();
    if (spec.Equals("all", StringComparison.OrdinalIgnoreCase))
    {
        for (int i = 1; i <= Tiles.Length; i++) result.Add(i);
        return result;
    }

    foreach (string part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        string[] range = part.Split('-');
        if (range.Length == 2 && int.TryParse(range[0], out int from) && int.TryParse(range[1], out int to))
        {
            for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) result.Add(i);
        }
        else if (int.TryParse(part, out int single))
            result.Add(single);
        else
            return null;
    }

    return result.All(i => i >= 1 && i <= Tiles.Length) && result.Count > 0 ? result : null;
}

void Apply(HashSet<int> keep)
{
    for (int i = 0; i < Tiles.Length; i++)
    {
        string path = Folder + Tiles[i];
        if (keep.Contains(i + 1))
            StaticAreaCellGenerator.CellSubstitutions.TryRemove(path, out _);
        else
            StaticAreaCellGenerator.CellSubstitutions[path] = string.Empty;   // left out
    }
}

string Describe(HashSet<int> keep) => string.Join(", ", keep.OrderBy(i => i).Select(i => $"{i} ({Tiles[i - 1].Replace("CivilWar_Bazaar_", "").Replace(".cell", "")})"));

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "cwtiles")
        return;

    e.Handled = true;
    if (ScriptHooks.IsAdmin(e.Player) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    string spec = e.GetArg(0);
    if (string.IsNullOrEmpty(spec) || spec.Equals("list", StringComparison.OrdinalIgnoreCase))
    {
        for (int i = 0; i < Tiles.Length; i++)
            e.Reply($"  {i + 1}: {Tiles[i].Replace(".cell", "")}");
        e.Reply("Usage: !cwtiles 3  |  !cwtiles 3,5,8  |  !cwtiles 3-8  |  !cwtiles all");
        return;
    }

    // "!cwtiles airport cap|ironman [real]": the Airport scenario, rebuilt from 4 Lowtown tiles (or the real map with "real")
    if (spec.Equals("airport", StringComparison.OrdinalIgnoreCase))
    {
        // the side: "cap" / "ironman", or (no side given) the player's own Civil War side, Team Cap if they have none
        string side = e.GetArg(1);
        bool ironMan;
        if (side != null && side.StartsWith("iron", StringComparison.OrdinalIgnoreCase)) ironMan = true;
        else if (side != null && side.Equals("cap", StringComparison.OrdinalIgnoreCase)) ironMan = false;
        else
        {
            var team = e.Player.GetPublicEventTeamPrototype();
            ironMan = team != null && GameDatabase.GetPrototypeName(team.DataRef).EndsWith("CivilWarProReg.prototype", StringComparison.OrdinalIgnoreCase);
        }
        string airport = ironMan ? "CivilWarAirportIronManRegion60" : "CivilWarAirportCaptainAmericaRegion60";

        // extra words: a tile set name, or any cell path (four copies of it); "real" = the real map; "skip" = no cutscenes
        // sent; "skip1" / "skip1,3" = only those scenes not sent (1-3 = the three boss entrances, 4 = the victory scene)
        bool sideGiven = side != null && (side.StartsWith("iron", StringComparison.OrdinalIgnoreCase) || side.Equals("cap", StringComparison.OrdinalIgnoreCase));
        var args = new[] { sideGiven ? null : side, e.GetArg(2), e.GetArg(3), e.GetArg(4) }.Where(w => w != null).ToList();
        var words = args.Select(w => w.ToLowerInvariant()).ToList();
        bool real = words.Contains("real");
        string tileSet = words.FirstOrDefault(w => AirportTileSets.ContainsKey(w)) ?? DefaultAirportTiles;

        string tilePath = args.FirstOrDefault(w => w.Contains('/'));
        if (tilePath != null)
        {
            if (tilePath.StartsWith("Resource/", StringComparison.OrdinalIgnoreCase) == false) tilePath = Cells + tilePath;
            if (tilePath.EndsWith(".cell", StringComparison.OrdinalIgnoreCase) == false) tilePath += ".cell";
            CellPrototype tileProto = GameDatabase.GetPrototypeRefByName(tilePath).As<CellPrototype>();
            if (tileProto == null)
            {
                e.Reply($"No cell {tilePath}");
                return;
            }
            // a district piece stays at its own spot (one copy); a normal tile is laid out four times
            AirportTileSets["custom"] = tileProto.IsOffsetInMapFile ? new[] { (tilePath, Vector3.Zero) } : Four(tilePath);
            tileSet = "custom";
        }
        bool hold = words.Contains("hold");   // no scenes sent and none finished: the bosses never spawn
        airportEdge = words.Contains("edge");

        var skipped = new HashSet<int>();
        foreach (string word in words.Where(w => w.StartsWith("skip")))
        {
            string list = word.Substring(4);
            if (list.Length == 0) { for (int i = 1; i <= 4; i++) skipped.Add(i); continue; }
            foreach (string n in list.Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(n, out int scene) && scene >= 1 && scene <= 4) skipped.Add(scene);
        }

        if (real)
            SingleCellAreaGenerator.CellReplacements.TryRemove(AirportCell, out _);
        else
            SingleCellAreaGenerator.CellReplacements[AirportCell] = AirportTileSets[tileSet];

        // The helicarrier pieces: show the area to the client as the helicarrier's own area, so it loads that area's backdrop
        // (sky and deck around the hangar, built at the same spot). "nobackdrop" turns it off.
        bool backdrop = real == false && (tileSet == "hangar" || tileSet == "helicarrier") && words.Contains("nobackdrop") == false;
        if (backdrop)
            MHServerEmu.Games.Network.AreaOfInterest.ClientAreaOverrides[AirportArea] = HelicarrierArea;
        else
            MHServerEmu.Games.Network.AreaOfInterest.ClientAreaOverrides.TryRemove(AirportArea, out _);

        for (int i = 0; i < AirportScenes.Length; i++)
        {
            int number = Math.Min(i + 1, 4);   // both victory scenes are scene 4
            if (hold) MHServerEmu.Games.Entities.Player.SkippedKismetSeqs[AirportScenes[i]] = false;
            else if (skipped.Contains(number)) MHServerEmu.Games.Entities.Player.SkippedKismetSeqs[AirportScenes[i]] = true;
            else MHServerEmu.Games.Entities.Player.SkippedKismetSeqs.TryRemove(AirportScenes[i], out _);
        }

        string sceneText = hold ? "held (no bosses)" : skipped.Count == 0 ? "all played" : $"skipping {string.Join(",", skipped.OrderBy(n => n))}";
        string mapText = real ? "real map" : $"rebuilt from '{tileSet}' tiles";
        Log.Info($"[cwtiles] sending {e.Player.GetName()} to {airport} ({mapText}, cutscenes {sceneText})");
        if (ScriptTeleport.ToRegion(e.Player, airport, null, Random.Shared.Next(1, int.MaxValue)))
            e.Reply($"Building {airport}...");
        else
            e.Reply("Teleport failed (see the server log).");
        return;
    }

    string building;
    if (spec.Equals("lowtown", StringComparison.OrdinalIgnoreCase))
    {
        if (ApplyLowtown(text => e.Reply(text)) == false)
            return;
        building = "the Bazaar rebuilt from Lowtown tiles";
        Log.Info("[cwtiles] Bazaar will be built from Lowtown stand-in tiles");
    }
    else
    {
        HashSet<int> keep = Parse(spec);
        if (keep == null)
        {
            e.Reply($"Couldn't read '{spec}'. Use tile numbers 1-{Tiles.Length}, e.g. 3 or 3,5 or 3-8 or all, or lowtown.");
            return;
        }

        ClearLowtown();
        Apply(keep);
        building = $"the Bazaar with tiles {Describe(keep)}";
        Log.Info($"[cwtiles] Bazaar will be built with tiles: {Describe(keep)}");
    }

    // A fresh copy: a new seed gives a new instance instead of reusing an old one
    int seed = Random.Shared.Next(1, int.MaxValue);
    if (ScriptTeleport.ToRegion(e.Player, Region, null, seed))
        e.Reply($"Building {building}...");
    else
        e.Reply("Teleport failed (see the server log).");
});

Log.Info("Civil War tile test loaded (!cwtiles)");
