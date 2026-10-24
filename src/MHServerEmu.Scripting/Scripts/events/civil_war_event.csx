// The Civil War event (2016), rebuilt for the 1.52 client.
//
// The game's own missions run the story and the dailies (Data/Game/Patches/PatchDataCivilWar.json turns the story back on):
//   1. Maria Hill calls you in a hub; go to the Madripoor Bazaar and beat Crossbones.
//   2. Pick a side (the choose-a-side panel; see civil_war_teams.csx).
//   3. Your side's hideout: talk to your liaison (Sharon Carter / Team Iron Man's liaison), then the Airport.
//   Then the side's daily (one a day, level 60) and weekly influence missions, from the liaison.
//
// What this script adds:
//   - the maps the client can't use, rebuilt: the Bazaar from Lowtown tiles, the Airport in the Secret Invasion helicarrier
//     hangar (with the helicarrier's sky backdrop);
//   - NPCs that warp players, standing where the game's (hidden) SHIELD transport officers stood: in Avengers Tower to
//     the Bazaar and to your side's hideout, in each hideout to that side's Airport;
//   - a visible exit when the Bazaar or the Airport is done (the game's own exit can't be shown on the rebuilt maps).
//
// Needs the engine changes listed in ENGINE_CHANGES.md (cell stand-ins, area display override, public event teams).

using System.Collections.Concurrent;
using MHServerEmu.Core.Collisions;
using MHServerEmu.Games.DRAG.Generators.Areas;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Regions;

const bool Verbose = true;
void Debug(string text) { if (Verbose) Log.Info($"[civil war] {text}"); }

//------------------------------------------------------------------------------
// Sides
//------------------------------------------------------------------------------

const string EventPath = "Events/PublicEvents/Events/CivilWar.prototype";
const string TeamIronManPath = "Events/PublicEvents/Teams/CivilWarProReg.prototype";
const string TeamCapPath = "Events/PublicEvents/Teams/CivilWarAntiReg.prototype";

// 1 = Team Cap, 2 = Team Iron Man, 0 = no side yet
int SideOf(Player player)
{
    PublicEventPrototype eventProto = GameDatabase.GetPrototypeRefByName(EventPath).As<PublicEventPrototype>();
    if (eventProto == null) return 0;
    string team = GameDatabase.GetPrototypeName(player.GetPublicEventTeam(eventProto));
    if (team.Equals(TeamCapPath, StringComparison.OrdinalIgnoreCase)) return 1;
    if (team.Equals(TeamIronManPath, StringComparison.OrdinalIgnoreCase)) return 2;
    return 0;
}

//------------------------------------------------------------------------------
// Rebuilt maps
//------------------------------------------------------------------------------

// The Madripoor Bazaar: its tiles' art isn't in the 1.52 client. Same path from Lowtown tiles (N = +X, E = +Y); each stand-in
// takes over the Bazaar tile's spawn markers, so Crossbones and the bosses still spawn.
const string BazaarFolder = "Resource/Cells/DistrictCells/CivilWar_Bazaar/";
const string Lowtown = "Resource/Cells/Madripoor/";
var BazaarPlan = new (string Tile, string Substitute)[]
{
    ("CivilWar_Bazaar_1_Entry.cell",     "Madripoor_Lower_B/Madripoor_Lower_B_S_A.cell"),
    ("CivilWar_Bazaar_2_Market.cell",    "Madripoor_Lower_B/Madripoor_Lower_B_NS_A.cell"),
    ("CivilWar_Bazaar_3_Market.cell",    "Madripoor_Lower_B/Madripoor_Lower_B_NS_A.cell"),
    ("CivilWar_Bazaar_4_Boss1.cell",     "Madripoor_Lower_B/Madripoor_Lower_B_NS_A.cell"),
    ("CivilWar_Bazaar_5_Roof.cell",      "Madripoor_Lower_A/Madripoor_Lower_A_SW_A.cell"),
    ("CivilWar_Bazaar_6_Roof.cell",      "Madripoor_Lower_A/Madripoor_Lower_A_NE_A.cell"),
    ("CivilWar_Bazaar_7_Boss2.cell",     "Madripoor_Lower_B/Madripoor_Lower_B_NS_A.cell"),
    ("CivilWar_Bazaar_8_FinalBoss.cell", "Madripoor_Lower_B/Madripoor_Lower_B_N_A.cell"),
};

void SetUpBazaar()
{
    foreach (var plan in BazaarPlan)
    {
        StaticAreaCellGenerator.CellSubstitutions[BazaarFolder + plan.Tile] = Lowtown + plan.Substitute;
        StaticAreaCellGenerator.CellSubstitutionSpawnMarkers[BazaarFolder + plan.Tile] = true;
    }

    // the final arena is 3456 wide, its stand-in 2304: push the stand-in back against tile 7
    CellPrototype boss2 = GameDatabase.GetPrototypeRefByName(BazaarFolder + "CivilWar_Bazaar_7_Boss2.cell").As<CellPrototype>();
    CellPrototype arena = GameDatabase.GetPrototypeRefByName(BazaarFolder + "CivilWar_Bazaar_8_FinalBoss.cell").As<CellPrototype>();
    if (boss2 != null && arena != null)
    {
        Vector3 offset = boss2.BoundingBox.Center + new Vector3(2304f, 0f, 0f) - arena.BoundingBox.Center;
        StaticAreaCellGenerator.CellSubstitutionOffsets[BazaarFolder + "CivilWar_Bazaar_8_FinalBoss.cell"] = new Vector3(offset.X, offset.Y, 0f);
    }

    Region.ArrivalFallbackFor["CivilWarBazaar"] = true;   // the Bazaar's start point was in its own entry tile

    // the Bazaar mission sends players back to Avengers Tower the moment Crossbones dies (after his ending scene, which
    // lives in the Bazaar's own map and can't play): stay to loot, leave by the exit this script puts up
    MHServerEmu.Games.Missions.Actions.MissionActionPlayerTeleport.SkippedFor["DRMissionChallengeCivilWarBazaarPhase01"] = true;
}

// The Airport: its map crashes the 1.52 client. Rebuilt in the Secret Invasion helicarrier hangar (same 4608 size), shown to
// the client as the helicarrier's area so it loads that backdrop (sky and clouds). The hangar's doorway to the rest of the
// helicarrier is left out; its own exit is the Airport's exit.
const string AirportCell = "Resource/Cells/EndGame/CivilWarScenario/CivilWarScenario_Airport_A.cell";
const string HangarCell = "Resource/Cells/DistrictCells/Helicarrier/Helicarrier_Invasion/Helicarrier_Invasion_X2Y0_Hngr.cell";
const string AirportArea = "Regions/EndGame/StaticScenarios/CivilWar/Airport/CivilWarAirportArea.prototype";
const string HelicarrierArea = "Regions/Story/CH10SecretInvasion/HelicarrierInvasion/HelicarrierInvasionArea1.prototype";
var AirportScenes = new[] { "CivilWar1WarMachineFalconEntrance", "CivilWar2BlackPantherWinterSoldierEntrance",
                            "CivilWar3CapIronManEntrance", "CivilWar4TeamCapVictory", "CivilWar4TeamIronManVictory" };

void SetUpAirport()
{
    SingleCellAreaGenerator.CellReplacements[AirportCell] = new[] { (HangarCell, Vector3.Zero) };
    MHServerEmu.Games.Network.AreaOfInterest.ClientAreaOverrides[AirportArea] = HelicarrierArea;
    MHServerEmu.Games.Regions.Cell.StandInSkippedEntities["Entity/Transitions/OpenTransitionSmlSoftFat2.prototype"] = true;
    Region.ArrivalFallbackFor["CivilWarAirport"] = true;

    // the boss entrance / victory scenes live in the Airport's own map: the hangar doesn't have them, so if the client
    // never reports one finished, the server finishes it and the next bosses still come
    foreach (string scene in AirportScenes)
        MHServerEmu.Games.Entities.Player.KismetSeqFallbackSeconds[scene] = 30f;
}

// The two hideouts: their maps (CivilWar_Warehouse_HUB, CivilWar_AvengersTowerRoof_HUB) aren't in a stock client, so stock
// players hang on loading. Rebuilt from stock maps; their NPCs are placed by this script (see PlaceHideoutCast):
//   Team Cap: a closed warehouse room, 2x2 of the warehouse corner tiles (same set as the hideout's own backdrop);
//   Team Iron Man: the S.H.I.E.L.D. helicarrier hub, bare (its own vendors, NPCs and portals left out), with its sky backdrop.
const string CapHubCell = "Resource/Cells/EndGame/CivilWarScenario/CivilWar_Warehouse_HUB.cell";
const string RoofHubCell = "Resource/Cells/EndGame/CivilWarScenario/CivilWar_AvengersTowerRoof_HUB.cell";
const string RoofHubArea = "Regions/HUBS/CivilWarHUBs/CivilWarAvengersTowerRoofHUBArea.prototype";
const string HelicarrierHubCell = "Resource/Cells/DistrictCells/Helicarrier/Helicarrier_HUB.cell";
const string HelicarrierHubArea = "Regions/HUBS/Helicarrier/HelicarrierArea.prototype";
const string WarehouseTiles = "Resource/Cells/ReuseableInstances/Warehouse_A/Warehouse_A_";

void SetUpHideouts()
{
    SingleCellAreaGenerator.CellReplacements[CapHubCell] = new[]
    {
        (WarehouseTiles + "NE_A.cell", new Vector3(-1152f, -1152f, 0f)), (WarehouseTiles + "NW_A.cell", new Vector3(-1152f, 1152f, 0f)),
        (WarehouseTiles + "ES_A.cell", new Vector3( 1152f, -1152f, 0f)), (WarehouseTiles + "SW_A.cell", new Vector3( 1152f, 1152f, 0f)),
    };
    // without the tiles' own breakable crates, shelves, forklifts and prop spawns: the hideout cast was landing inside them
    // (placement only checks the floor). Only while the tiles stand in for the hideout; other warehouse maps keep theirs.
    foreach (string corner in new[] { "NE_A", "NW_A", "ES_A", "SW_A" })
        MHServerEmu.Games.Regions.Cell.StandInBareCells[WarehouseTiles + corner + ".cell"] = true;

    SingleCellAreaGenerator.CellReplacements[RoofHubCell] = new[] { (HelicarrierHubCell, Vector3.Zero) };
    MHServerEmu.Games.Regions.Cell.StandInBareCells[HelicarrierHubCell] = true;
    MHServerEmu.Games.Network.AreaOfInterest.ClientAreaOverrides[RoofHubArea] = HelicarrierHubArea;

    // carried over from the original hideouts otherwise: the hidden transport officer and the waypoints (placed by this script)
    MHServerEmu.Games.Regions.Cell.StandInSkippedEntities["Entity/Transitions/SHIELDTransportOfficerMaleCivilWarV1.prototype"] = true;
    MHServerEmu.Games.Regions.Cell.StandInSkippedEntities["Entity/Transitions/Waypoints/HUBS/CivilWarCaptainAmericaHUBwaypoint.prototype"] = true;
    MHServerEmu.Games.Regions.Cell.StandInSkippedEntities["Entity/Transitions/Waypoints/HUBS/CivilWarIronManHUBwaypoint.prototype"] = true;

    Region.ArrivalFallbackFor["CivilWarHub"] = true;   // the hideouts' start points were in their own maps
}

SetUpBazaar();
SetUpAirport();
SetUpHideouts();

//------------------------------------------------------------------------------
// Exits when a scenario is done
//------------------------------------------------------------------------------

const string ExitPath = "Entity/Transitions/Default.prototype";   // the standard portal, visible to everyone
var exitWatched = new ConcurrentDictionary<ulong, bool>();

Vector3? HiddenExitSpot(Region region)
{
    foreach (Entity entity in region.Entities)
        if (entity is WorldEntity world && world.IsInWorld && world.PrototypeName.EndsWith("DefaultEND", StringComparison.OrdinalIgnoreCase))
            return world.RegionLocation.Position;
    return null;
}

void PlaceExit(Region region, Player finisher, bool atHiddenExit)
{
    Vector3? spot = atHiddenExit ? HiddenExitSpot(region) : null;

    Avatar avatar = finisher?.CurrentAvatar;
    if (spot == null && avatar != null && avatar.IsInWorld &&
        ScriptSpawner.TryFindSpotNear(region, avatar.RegionLocation.Position, 150f, 400f, out Vector3 near))
        spot = near;

    if (spot == null) { Log.Warn($"[civil war] no spot for the exit in {region.PrototypeName}"); return; }

    var exit = ScriptSpawner.SpawnInteractable(region, GameDatabase.GetPrototypeRefByName(ExitPath), spot.Value, 0f, "civilwar-exit");
    Debug($"{region.PrototypeName}: exit {(exit != null ? "placed" : "FAILED")} at {spot.Value}");
}

void WatchForCompletion(Region region, string missionName, bool atHiddenExit)
{
    if (exitWatched.TryAdd(region.Id, true) == false) return;

    bool placed = false;
    region.PlayerCompletedMissionEvent.AddActionBack((in PlayerCompletedMissionGameEvent evt) =>
    {
        if (placed || GameDatabase.GetPrototypeName(evt.MissionRef).Contains(missionName) == false) return;
        placed = true;
        PlaceExit(region, evt.Player, atHiddenExit);
    });
}

//------------------------------------------------------------------------------
// Warp NPCs
//------------------------------------------------------------------------------

const string TagHideoutCap = "civilwar-to-hideout-cap";
const string TagHideoutIronMan = "civilwar-to-hideout-ironman";
const string TagBazaar = "civilwar-to-bazaar";
const string TagLiaison = "civilwar-hideout-liaison";
const string TagAirportCap = "civilwar-to-airport-cap";
const string TagAirportIronMan = "civilwar-to-airport-ironman";

const string HubCap = "CivilWarHubCaptainAmericaWarehouseRegion";
const string HubIronMan = "CivilWarHubIronManAvengersRoofRegion";
// The Bazaar and Airport come in level bands (Region01, 05, 10 ... 55, 60), each admitting only its own levels (Region60 is
// 60 only: everyone below got "FAILED CanEnterRegion check ... AccessChecks"). Players go to the band for their level.
const string BazaarRegion = "Regions/EndGame/StaticScenarios/CivilWar/Bazaar/AltRegions/CivilWarBazaarRegion";
const string AirportCap = "Regions/EndGame/StaticScenarios/CivilWar/Airport/AltRegions/AirportCap/CivilWarAirportCaptainAmericaRegion";
const string AirportIronMan = "Regions/EndGame/StaticScenarios/CivilWar/Airport/AltRegions/AirportIronMan/CivilWarAirportIronManRegion";

string ForLevel(string regionBase, Player player)
{
    int level = player.CurrentAvatar?.CharacterLevel ?? 60;
    int band = level >= 60 ? 60 : level < 5 ? 1 : level / 5 * 5;
    return $"{regionBase}{band:00}.prototype";
}

// (region name part, cell name part, spot in that cell, facing, NPC, tag). Fixed spots: the Avengers Tower event line, the
// lower walkway east of Hank Pym's raised platform, one NPC every 170 units heading toward the elevator (X -1000, -1170, ...
// at Y 1940, against the walkway's far wall, facing back toward Pym's platform). The Civil War three are the first three; the other events' NPCs carry on down the line.
record Warper(string Region, string Cell, Vector3 Spot, float Yaw, string Npc, string Tag);
var Warpers = new[]
{
    new Warper("NPEAvengersTowerHUB", "AvengersTowerNPE", new Vector3(-1000f, 1940f, 140f), 270f,
               "Entity/Characters/NPCs/HubNPCs/CivilWarCaptainAmericaHubNPCs/CivilWarCloak.prototype", TagHideoutCap),
    new Warper("NPEAvengersTowerHUB", "AvengersTowerNPE", new Vector3(-1170f, 1940f, 140f), 270f,
               "Entity/Characters/NPCs/HubNPCs/CivilWarIronManHubNPCs/CivilWarSHIELDAgentMale.prototype", TagHideoutIronMan),
    new Warper("NPEAvengersTowerHUB", "AvengersTowerNPE", new Vector3(-1340f, 1940f, 140f), 270f,
               "Entity/Characters/NPCs/HubNPCs/CivilWarIronManHubNPCs/CivilWarSHIELDAgentFemale.prototype", TagBazaar),
};

// Labels over the event's NPCs (overhead text, re-sent every few seconds so players walking up see it too). The text
// reaches players when they connect: after a script reload, reconnect to see changed labels.
var Labels = new Dictionary<string, string>
{
    [TagHideoutCap]     = "Team Cap: to Cap's hideout",
    [TagHideoutIronMan] = "Team Iron Man: to the helicarrier",
    [TagBazaar]         = "Madripoor: the Bazaar (Crossbones)",
    [TagAirportCap]     = "Team Cap: to the Airport",
    [TagAirportIronMan] = "Team Iron Man: to the Airport",
    [TagLiaison]        = "Story and daily missions",
};
foreach (var (labelTag, text) in Labels) ScriptText.Register("civilwar.label." + labelTag, text);

const float LabelEverySeconds = 8f;
var labelLoops = new ConcurrentDictionary<ulong, bool>();

void StartLabels(Region region)
{
    if (labelLoops.TryAdd(region.Id, true) == false) return;
    ulong regionId = region.Id;
    Game game = region.Game;

    void Tick()
    {
        Region current = game.RegionManager.GetRegion(regionId);
        if (current == null) { labelLoops.TryRemove(regionId, out _); return; }

        foreach (Entity entity in current.Entities)
            if (entity is WorldEntity world && Labels.ContainsKey(ScriptSpawner.GetTag(world)))
                ScriptText.ShowOverheadText(world, "civilwar.label." + ScriptSpawner.GetTag(world), 0, LabelEverySeconds + 1.5f);

        ScriptTimer.After(game, LabelEverySeconds, Tick);
    }

    Tick();
}

// The rebuilt hideouts: the original hideout's cast (its NPCs, vendors, stash and waypoint, read from the original map's
// data) plus the Airport warper, placed on walkable ground around where players arrive.
record Hideout(string Region, string OriginalCell, string AirportTag, string AirportNpc);
var Hideouts = new[]
{
    new Hideout(HubCap, CapHubCell, TagAirportCap, "Entity/Characters/NPCs/HubNPCs/CivilWarIronManHubNPCs/CivilWarSHIELDAgentMale.prototype"),
    new Hideout(HubIronMan, RoofHubCell, TagAirportIronMan, "Entity/Characters/NPCs/HubNPCs/CivilWarIronManHubNPCs/CivilWarSHIELDAgentFemale.prototype"),
};

// ScriptSpawner.SpawnAsMapEntity (places an entity as the map would) is in the newer engine build; until the server runs
// it, fall back to SpawnInteractable (the NPC shows, but vendors and mission givers don't work)
var spawnAsMapEntity = typeof(ScriptSpawner).GetMethod("SpawnAsMapEntity");
if (spawnAsMapEntity == null) Log.Warn("[civil war] this server build has no ScriptSpawner.SpawnAsMapEntity: hideout vendors / liaisons won't work until the new DLLs are in");

WorldEntity SpawnAsMapEntity(Region region, PrototypeId protoRef, Vector3 position, float yaw, string tag)
{
    if (spawnAsMapEntity != null)
        return spawnAsMapEntity.Invoke(null, new object[] { region, protoRef, position, yaw, tag }) as WorldEntity;
    return ScriptSpawner.SpawnInteractable(region, protoRef, position, yaw, tag);
}

void PlaceHideoutCast(Region region, Hideout hideout)
{
    RemoveOldNpcs(region);

    CellPrototype original = GameDatabase.GetPrototypeRefByName(hideout.OriginalCell).As<CellPrototype>();
    if (original == null) { Log.Warn($"[civil war] no cell {hideout.OriginalCell}"); return; }

    // who: the original map's characters (NPCs, vendors, stash) and its hub waypoint, then the Airport warper
    var cast = new List<(string Path, string Tag)>();
    foreach (var markerSet in new[] { original.InitializeSet, original.MarkerSet })
    {
        if (markerSet?.Markers == null) continue;
        foreach (var marker in markerSet.Markers)
        {
            if (marker is not MHServerEmu.Games.GameData.Prototypes.Markers.EntityMarkerPrototype entityMarker) continue;
            string path = GameDatabase.GetPrototypeName(GameDatabase.GetDataRefByPrototypeGuid(entityMarker.EntityGuid));
            if (path.StartsWith("Entity/Characters/") || path.StartsWith("Entity/Transitions/Waypoints/"))
                cast.Add((path, path.Contains("Liaison") ? TagLiaison : "civilwar-hideout"));
        }
    }
    cast.Add((hideout.AirportNpc, hideout.AirportTag));

    // where: around the arrival point, each in its own direction, on ground players can walk to from there
    // (TryFindSpotNear isn't used: in a hub its spawn rules fail and it hands back the arrival point every time)
    // the Cap hideout is four tiles with a random arrival point: lay the cast out around the room's middle instead, so it's
    // the same every time
    Vector3 anchor;
    Vector3 sum = Vector3.Zero;
    int cellCount = 0;
    foreach (var cell in region.Cells) { sum += cell.RegionBounds.Center; cellCount++; }
    if (cellCount > 1)
    {
        // the exact middle can be a wall or pillar (nothing is reachable from there), so take the nearest floor around it
        // that players can walk to from where they arrive; failing that, the arrival point itself
        Vector3 middle = sum / cellCount;
        Vector3 arrival = ScriptSpawner.TryGetStartPosition(region, out Vector3 start) ? start : middle;
        anchor = arrival;
        foreach (float radius in new[] { 250f, 500f, 800f, 1200f })
            if (ScriptSpawner.TryFindReachableSpotNear(region, middle, radius, arrival, out Vector3 floor, 24)) { anchor = floor; break; }
    }
    else if (CellSpot(region, "Helicarrier_HUB", new Vector3(-406f, 1274f, 48f)) is Vector3 deckStart)
        anchor = deckStart;   // the helicarrier's own start point (players arrive at random spots on the deck)
    else
        anchor = ScriptSpawner.TryGetStartPosition(region, out Vector3 start) ? start : region.Aabb.Center;
    var used = new List<Vector3>();
    int placed = 0, index = 0;
    foreach (var (path, tag) in cast)
    {
        float angle = MathF.PI * 2f * index++ / cast.Count;
        Vector3? spot = null;
        foreach (float distance in new[] { 400f, 550f, 300f, 700f, 850f })
        {
            Vector3 ring = new(anchor.X + MathF.Cos(angle) * distance, anchor.Y + MathF.Sin(angle) * distance, anchor.Z);
            if (ScriptSpawner.TryFindReachableSpotNear(region, ring, 160f, anchor, out Vector3 candidate, 16) &&
                used.All(other => Vector3.Distance2D(other, candidate) >= 140f))
            { spot = candidate; break; }
        }

        // anywhere in the room players can walk to, as a last resort
        for (int attempt = 0; attempt < 20 && spot == null; attempt++)
            if (ScriptSpawner.TryFindRandomSpot(region, out Vector3 anywhere, anchor) && used.All(other => Vector3.Distance2D(other, anywhere) >= 140f))
                spot = anywhere;
        if (spot == null) { Log.Warn($"[civil war] {region.PrototypeName}: no spot for {path}"); continue; }

        used.Add(spot.Value);
        float yaw = MHServerEmu.Core.Helpers.MathHelper.ToDegrees(MathF.Atan2(anchor.Y - spot.Value.Y, anchor.X - spot.Value.X));   // facing the arrival point

        // the hideout's own cast is placed as the map would (vendors sell, the liaison gives the story talks and dailies, the
        // stash and waypoint work); only the Airport warper is a script prop (its click is handled below)
        PrototypeId protoRef = GameDatabase.GetPrototypeRefByName(path);
        WorldEntity npc = tag == hideout.AirportTag
            ? ScriptSpawner.SpawnInteractable(region, protoRef, spot.Value, yaw, tag)
            : SpawnAsMapEntity(region, protoRef, spot.Value, yaw, tag);
        if (npc != null) placed++;
        else Log.Warn($"[civil war] {region.PrototypeName}: couldn't place {path}");
    }

    Debug($"{region.PrototypeName}: hideout cast {placed}/{cast.Count} placed around {anchor}");
    StartLabels(region);
}

var warpersPlaced = new ConcurrentDictionary<ulong, bool>();

// NPCs this script placed in a region before (a script reload forgets what it placed, the NPCs stay): removed before
// placing again, so a reload doesn't leave two of each. Exits are left alone.
void RemoveOldNpcs(Region region)
{
    var old = new List<ulong>();
    foreach (Entity entity in region.Entities)
    {
        if (entity is not WorldEntity world) continue;
        string tag = ScriptSpawner.GetTag(world);
        if (tag.StartsWith("civilwar-hideout") || tag.StartsWith("civilwar-to-")) old.Add(world.Id);
    }

    foreach (ulong id in old) ScriptSpawner.Despawn(region.Game, id);
    if (old.Count > 0) Debug($"{region.PrototypeName}: removed {old.Count} NPCs placed before a script reload");
}

// The story's first step is Maria Hill's call (story 1, objective 0); a Bazaar run before it doesn't count, so the story
// never moves on (no side pick, no hideout). The Bazaar agent waits for the call. Unknown state (story not started): let through.
const string Story1Path = "Missions/Prototypes/PVEEndgame/StaticScenarios/CivilWarEventProgressionMissionPhase01.prototype";

bool TookMariaHillsCall(Player player)
{
    var mission = player.MissionManager?.FindMissionByDataRef(GameDatabase.GetPrototypeRefByName(Story1Path));
    if (mission == null)
    {
        Debug($"{player.GetName()}: story 1 not started, Bazaar allowed");
        return true;
    }

    if (mission.State == MHServerEmu.Games.Missions.MissionState.Completed) return true;

    var call = mission.GetObjectiveByObjectiveIndex(0);
    return call != null && call.State == MHServerEmu.Games.Missions.MissionObjectiveState.Completed;
}

// a spot in a cell's own coordinates -> the region
Vector3? CellSpot(Region region, string cellPart, Vector3 local)
{
    foreach (var cell in region.Cells)
    {
        if (cell.PrototypeName.Contains(cellPart, StringComparison.OrdinalIgnoreCase) == false) continue;
        Vector3 regionPos = cell.RegionBounds.Center + (local - cell.Prototype.BoundingBox.Center);
        return new Vector3(regionPos.X, regionPos.Y, regionPos.Z);
    }
    return null;
}

void PlaceWarpers(Region region)
{
    RemoveOldNpcs(region);

    foreach (Warper warper in Warpers)
    {
        if (region.PrototypeName.Contains(warper.Region, StringComparison.OrdinalIgnoreCase) == false) continue;

        Vector3? target = CellSpot(region, warper.Cell, warper.Spot);
        if (target == null) { Log.Warn($"[civil war] no cell {warper.Cell} in {region.PrototypeName}"); continue; }
        Vector3 spot = target.Value;   // fixed spot, no random nudge

        var npc = ScriptSpawner.SpawnInteractable(region, GameDatabase.GetPrototypeRefByName(warper.Npc), spot, warper.Yaw, warper.Tag);
        Debug($"{region.PrototypeName}: warp NPC {warper.Tag} {(npc != null ? "placed" : "FAILED")} at {spot}");
    }

    StartLabels(region);
}

//------------------------------------------------------------------------------
// Hooks
//------------------------------------------------------------------------------

Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    Region region = e.Region;
    if (region == null) return;
    string name = region.PrototypeName;

    if (name.Contains("CivilWarAirport", StringComparison.OrdinalIgnoreCase))
        WatchForCompletion(region, "DRMissionChallengeCivilWarAirportPhase01", true);
    else if (name.Contains("CivilWarBazaar", StringComparison.OrdinalIgnoreCase))
        WatchForCompletion(region, "DRMissionChallengeCivilWarBazaarPhase01", false);

    if (warpersPlaced.ContainsKey(region.Id)) return;

    foreach (Hideout hideout in Hideouts)
    {
        if (name.Contains(hideout.Region, StringComparison.OrdinalIgnoreCase) == false) continue;
        warpersPlaced.TryAdd(region.Id, true);
        ulong hideoutRegionId = region.Id;
        Game hideoutGame = region.Game;
        ScriptTimer.After(hideoutGame, 2f, () => { Region current = hideoutGame.RegionManager.GetRegion(hideoutRegionId); if (current != null) PlaceHideoutCast(current, hideout); });
        return;
    }

    foreach (Warper warper in Warpers)
    {
        if (name.Contains(warper.Region, StringComparison.OrdinalIgnoreCase) == false) continue;
        warpersPlaced.TryAdd(region.Id, true);
        ulong regionId = region.Id;
        Game game = region.Game;
        ScriptTimer.After(game, 2f, () => { Region current = game.RegionManager.GetRegion(regionId); if (current != null) PlaceWarpers(current); });
        break;
    }
});

Hooks.On(ScriptHooks.EntityInteracted, e =>
{
    string tag = e.ScriptTag;
    if (tag.StartsWith("civilwar-to-") == false) return;

    Player player = e.Player;
    int side = SideOf(player);
    string destination = null;

    switch (tag)
    {
        // each side's hideout warper only takes that side; no side yet: the choose-a-side panel
        case TagHideoutCap:
        case TagHideoutIronMan:
            if (side == 0)
            {
                e.Reply("Pick a side first: Team Cap or Team Iron Man.");
                ScriptPresentation.TeamSelectDialog(player, EventPath);
                return;
            }
            if (tag == TagHideoutCap && side != 1) { e.Reply("You're not with Cap. Team Cap only."); return; }
            if (tag == TagHideoutIronMan && side != 2) { e.Reply("You're not with Stark. Team Iron Man only."); return; }
            destination = side == 1 ? HubCap : HubIronMan;
            break;

        case TagBazaar:
            if (TookMariaHillsCall(player) == false)
            {
                e.Reply("Maria Hill is trying to reach you. Answer her call first, then I can take you to Madripoor.");
                return;
            }
            destination = ForLevel(BazaarRegion, player);
            break;

        // each side's Airport warper only takes that side (someone can still end up in the other hideout, e.g. by joining
        // a party member there)
        case TagAirportCap:
            if (side != 1) { e.Reply("This isn't your side's operation. Team Cap only."); return; }
            destination = ForLevel(AirportCap, player);
            break;

        case TagAirportIronMan:
            if (side != 2) { e.Reply("This isn't your side's operation. Team Iron Man only."); return; }
            destination = ForLevel(AirportIronMan, player);
            break;
    }

    if (destination == null) return;
    Debug($"{player.GetName()} -> {destination}");
    if (ScriptTeleport.ToRegion(player, destination) == false)
        e.Reply("That way is closed right now.");
});

Log.Info("Civil War event loaded");
