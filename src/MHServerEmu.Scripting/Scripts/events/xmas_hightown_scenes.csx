// Christmas Hightown scenes: little Christmas street scenes around Christmas Hightown, rebuilt from the unused Midtown
// Christmas discoveries (XMasCivsSnowman*, XMasCivWarmingHands*). Scenery only: nothing here can be fought or killed.
//
// Those were open missions that never shipped; the server never starts them (they have no region set), and Hightown
// lacks some of their spawn spots. Instead, this reads each scene's encounter file (who stands where) and spawns it at a
// random street spot in Hightown, then starts the scene's animation, like the decor scripts do for props.
//
//  - Snowman scenes: civilians packing snow next to a Norway snowman. Friendly scenery.
//  - Old lady warming her hands. Friendly scenery.
//  - Snowman groups: 2-3 snowmen standing together, mixed from every snowman the client can draw (four Norway snowmen
//    and the top-hat / space-helmet snowman vanity pets). Snowman scenes also get a random visible snowman: the encounter
//    files sometimes use the "Smiley" snowmen, which have no model in the 1.52 client (invisible).
//
// Only Hightown instances (area UpperMadripoorAreaA) get scenes, the first time a player enters. Meant for Christmas
// Hightown (PatchDataXmasHightownTest.json); with that patch off, set Enabled = false or the scenes show up in plain Hightown.
//
//   !xmasscenes redo    (admin) clear this Hightown's scenes and place new ones
//   !xmasscenes clear   (admin) remove this Hightown's scenes
//   !xmasscenes here    (admin) spawn one scene of each kind right next to you (tests spawning without the spot search)
//   !xmasscenes debug   (admin) say whether this region counts as Hightown, its areas, and which area you are in
//   !xmasscenes         (admin) how many scene entities this Hightown has

using System.Collections.Concurrent;
using MHServerEmu.Core.Helpers;
using MHServerEmu.Games.Powers;
using MHServerEmu.Games.GameData.Prototypes.Markers;

//------------------------------------------------------------------------------
// Settings
//------------------------------------------------------------------------------

const bool Enabled = true;

const string HightownArea = "Regions/Story/CH10SecretInvasion/UpperMadripoor/UpperMadripoorAreaA.prototype";

const string Encounters   = "Resource/Encounters/Discoveries/";
const string SnowPower    = "Powers/MissionPowers/NPCPowers/Ambients/AmbientUnloadingCratesLow.prototype";   // what the snowman missions play (packing snow)
const string WarmPower    = "Powers/MissionPowers/NPCPowers/Ambients/AmbientWarmingHands.prototype";

// Scene kinds: encounter files (one is picked at random each time), animation, hostile, how many per Hightown
var Kinds = new (string Name, string[] Files, string Power, bool Hostile, int Count)[]
{
    ("snowman", new[] { "XMas_Civ_Snowman_Solo_V1", "XMas_Civ_Snowman_Solo_V2", "XMas_Civ_Snowman_Solo_V3",
                        "XMas_Civ_Snowman_Trio_V1", "XMas_Civ_Snowman_Trio_V2", "XMas_Civ_Snowman_Trio_V3" }, SnowPower, false, 4),
    ("old lady", new[] { "Manhattan_OldWomanWarmingHands" },                                              WarmPower, false, 3),
};

// Snowmen the client can draw. The three "Smiley" Norway snowmen have no model in the 1.52 client, so they are left out.
var SnowmanProps = new[]
{
    "Entity/Props/Destructibles/NorwaySnowmanGaz.prototype",
    "Entity/Props/Destructibles/NorwaySnowmanSif.prototype",
    "Entity/Props/Destructibles/NorwaySnowmanSnowGoon.prototype",
    "Entity/Props/Destructibles/NorwaySnowmanStacked.prototype",
};
var SnowmanPets = new[]   // the snowman vanity pets, standing on their own
{
    "Entity/Characters/PetsAndSummons/VanityPets/Pet054Snowman.prototype",        // top hat
    "Entity/Characters/PetsAndSummons/VanityPets/Pet055SnowmanSpace.prototype",   // space helmet
};
const int SnowmanGroups       = 4;     // free-standing groups per Hightown
const int SnowmanGroupLayouts = 24;    // random mixes prepared at load (each group picks one)

const float MinSpacing      = 900f;   // scenes stay at least this far apart
const float KeepClearRadius = 800f;   // and this far from where players arrive
const int   TriesPerScene   = 25;
const float ReplaySeconds   = 20f;    // restart the animations this often, in case they stop on their own
const bool  Verbose         = true;   // detailed log of every step (turn off once the scenes work)

//------------------------------------------------------------------------------
// State
//------------------------------------------------------------------------------

const string Tag = "xmas_hightown_scene";

var placed  = new ConcurrentDictionary<ulong, bool>();          // region id => scenes placed
var spawned = new ConcurrentDictionary<ulong, List<ulong>>();   // region id => ids of everything this script spawned there

PrototypeId areaRef  = GameDatabase.GetPrototypeRefByName(HightownArea);
PrototypeId snowRef  = GameDatabase.GetPrototypeRefByName(SnowPower);
PrototypeId warmRef  = GameDatabase.GetPrototypeRefByName(WarmPower);

if (areaRef == PrototypeId.Invalid)
    Log.Warn($"Christmas Hightown scenes: area not found: {HightownArea}");


void Debug(string message)
{
    if (Verbose) Log.Info("[xmas scenes] " + message);
}

// One spawnable piece of a scene: what, where relative to the scene centre, which way it faces
record Piece(PrototypeId Ref, Vector3 Offset, float Yaw, bool IsAgent);

// Reads an encounter file into its pieces (spawn-marker placeholders are skipped)
List<Piece> LoadScene(string file)
{
    var pieces = new List<Piece>();
    string path = Encounters + file + ".encounter";
    PrototypeId encounterRef = GameDatabase.GetPrototypeRefByName(path);
    if (encounterRef == PrototypeId.Invalid)
    {
        Log.Warn($"[xmas scenes] encounter not found: {path}");
        return pieces;
    }

    var encounter = encounterRef.As<EncounterResourcePrototype>();
    if (encounter?.MarkerSet?.Markers == null)
    {
        Log.Warn($"[xmas scenes] encounter {file} has no markers (type {encounterRef.As<Prototype>()?.GetType().Name ?? "null"})");
        return pieces;
    }

    var skipped = new List<string>();
    foreach (var marker in encounter.MarkerSet.Markers)
    {
        if (marker is not EntityMarkerPrototype entityMarker)
            continue;

        PrototypeId entityRef = GameDatabase.GetDataRefByPrototypeGuid(entityMarker.EntityGuid);
        var entityProto = entityRef.As<WorldEntityPrototype>();
        string name = entityRef == PrototypeId.Invalid ? $"guid {entityMarker.EntityGuid} (unresolved)" : entityRef.GetName();
        if (entityProto == null || name.StartsWith("SpawnMarkers/"))
        {
            skipped.Add(name);
            continue;
        }

        pieces.Add(new Piece(entityRef, entityMarker.Position, entityMarker.Rotation.Yaw, entityProto is AgentPrototype));
    }

    Debug($"layout {file}: {pieces.Count} piece(s) [{string.Join(", ", pieces.Select(p => p.Ref.GetName().Split('/').Last()))}]" +
        (skipped.Count > 0 ? $", skipped [{string.Join(", ", skipped.Select(s => s.Split('/').Last()))}]" : ""));
    return pieces;
}

var scenes = Kinds.Select(kind => (kind.Name, Scenes: kind.Files.Select(LoadScene).Where(s => s.Count > 0).ToList(),
    PowerRef: GameDatabase.GetPrototypeRefByName(kind.Power), kind.Hostile, kind.Count)).ToList();

// Visible snowman props, to replace whatever snowman an encounter file uses
var snowmanPropRefs = SnowmanProps.Select(path => GameDatabase.GetPrototypeRefByName(path)).Where(r => r != PrototypeId.Invalid).ToList();
var snowmanPetRefs  = SnowmanPets.Select(path => GameDatabase.GetPrototypeRefByName(path)).Where(r => r != PrototypeId.Invalid).ToList();
if (snowmanPropRefs.Count < SnowmanProps.Length || snowmanPetRefs.Count < SnowmanPets.Length)
    Log.Warn($"[xmas scenes] some snowmen were not found ({snowmanPropRefs.Count}/{SnowmanProps.Length} props, {snowmanPetRefs.Count}/{SnowmanPets.Length} pets)");

bool IsSnowmanProp(PrototypeId entityRef) => entityRef.GetName().Contains("/NorwaySnowman", StringComparison.OrdinalIgnoreCase);

// Snowman groups: 2-3 different snowmen in a small ring, every combination of props and pets
List<Piece> MakeSnowmanGroup(Random random)
{
    var pool = snowmanPropRefs.Select(r => (Ref: r, IsAgent: false)).Concat(snowmanPetRefs.Select(r => (Ref: r, IsAgent: true)))
        .OrderBy(_ => random.Next()).ToList();
    int count = Math.Min(pool.Count, random.Next(2, 4));
    var group = new List<Piece>();
    float start = random.NextSingle() * MathF.PI * 2f;
    for (int i = 0; i < count; i++)
    {
        float angle = start + i * MathF.PI * 2f / count;
        float distance = 70f + random.NextSingle() * 40f;
        var offset = new Vector3(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance, 0f);
        // face roughly outward, with some variation
        group.Add(new Piece(pool[i].Ref, offset, angle + (random.NextSingle() - 0.5f), pool[i].IsAgent));
    }
    return group;
}

if (snowmanPropRefs.Count + snowmanPetRefs.Count >= 2)
{
    var random = new Random();
    var groups = Enumerable.Range(0, SnowmanGroupLayouts).Select(_ => MakeSnowmanGroup(random)).ToList();
    scenes.Add(("snowman group", groups, PrototypeId.Invalid, false, SnowmanGroups));
    Debug($"snowman groups: {groups.Count} random mixes of {snowmanPropRefs.Count} snowmen + {snowmanPetRefs.Count} snowman pets");
}

foreach (var kind in scenes)
{
    if (kind.PowerRef == PrototypeId.Invalid && kind.Name != "snowman group")
        Log.Warn($"[xmas scenes] {kind.Name}: animation power not found, they will stand still");
    if (kind.Scenes.Count == 0)
        Log.Warn($"[xmas scenes] {kind.Name}: no usable layouts, this kind is skipped");
}

bool IsHightown(Region region) => region != null && areaRef != PrototypeId.Invalid
    && region.IterateAreas().Any(area => area.PrototypeDataRef == areaRef);

List<ulong> Spawned(Region region) => spawned.GetOrAdd(region.Id, _ => new List<ulong>());

List<WorldEntity> Tagged(Region region)
{
    var ids = Spawned(region);
    lock (ids)
    {
        ids.RemoveAll(id => ScriptSpawner.IsAlive(region.Game, id) == false);
        return ids.Select(id => region.Game.EntityManager.GetEntity<WorldEntity>(id)).Where(entity => entity != null).ToList();
    }
}

void Clear(Region region)
{
    foreach (WorldEntity entity in Tagged(region))
        ScriptSpawner.Despawn(region.Game, entity.Id);
    lock (Spawned(region)) Spawned(region).Clear();
}

// Spot rules, strictest first. If no spot passes, the next (looser) stage is tried:
//  0: Hightown streets, away from the arrival point and other scenes
//  1: Hightown streets, half the spacing, arrival point allowed
//  2: anywhere in Hightown that is walkable
//  3: next to the hero (always works if a hero is in the streets)
string[] StageNames = { "strict", "relaxed", "any Hightown spot", "near arrival" };

bool TryPickSpot(Region region, Vector3? arrival, List<Vector3> taken, out Vector3 spot, out int usedStage)
{
    for (int stage = 0; stage < 4; stage++)
    {
        usedStage = stage;
        int noSpot = 0, wrongArea = 0, nearArrival = 0, tooClose = 0;

        if (stage == 3)
        {
            if (arrival.HasValue && ScriptSpawner.TryFindSpotNear(region, arrival.Value, 300f, 1200f, out spot))
                return true;

            Debug($"  spot: stage 3 failed ({(arrival.HasValue ? "no free spot near arrival" : "arrival point unknown")})");
            break;
        }

        float spacing = stage == 0 ? MinSpacing : stage == 1 ? MinSpacing * 0.5f : 0f;
        float clear   = stage == 0 ? KeepClearRadius : 0f;

        for (int i = 0; i < TriesPerScene; i++)
        {
            // Stage 2 drops the radius and the reachability test too
            bool found = stage < 2
                ? ScriptSpawner.TryFindRandomSpot(region, out spot, arrival, 0f, 160f)
                : ScriptSpawner.TryFindRandomSpot(region, out spot);
            if (found == false) { noSpot++; continue; }

            if (region.GetAreaAtPosition(spot)?.PrototypeDataRef != areaRef) { wrongArea++; continue; }

            if (clear > 0f && arrival.HasValue && Vector3.Distance2D(spot, arrival.Value) < clear) { nearArrival++; continue; }

            Vector3 candidate = spot;
            if (spacing > 0f && taken.Any(other => Vector3.Distance2D(other, candidate) < spacing)) { tooClose++; continue; }

            return true;
        }

        Debug($"  spot: stage {stage} ({StageNames[stage]}) failed: {noSpot} no walkable spot, {wrongArea} outside Hightown, " +
            $"{nearArrival} too near arrival, {tooClose} too near another scene");
    }

    spot = Vector3.Zero;
    usedStage = -1;
    return false;
}

Vector3 Rotate(Vector3 offset, float yaw)
{
    float cos = MathF.Cos(yaw), sin = MathF.Sin(yaw);
    return new Vector3(offset.X * cos - offset.Y * sin, offset.X * sin + offset.Y * cos, 0f);
}

bool Animate(Agent agent, PrototypeId powerRef, bool logResult = false)
{
    if (agent == null || agent.IsDestroyed || agent.IsInWorld == false || powerRef == PrototypeId.Invalid)
        return false;

    // Hostile ones stop once they are fighting
    if (agent.AIController?.TargetEntity != null)
        return false;

    agent.SetSimulated(true);
    var result = agent.ActivatePerformPower(powerRef);
    if (logResult)
        Debug($"    animation {powerRef.GetName().Split('/').Last()} on {agent.PrototypeName.Split('/').Last()}: {result}");
    return result == PowerUseResult.Success;
}

// Keeps the scene's animations going while its people exist
void KeepAnimating(Game game, List<Agent> agents, PrototypeId powerRef)
{
    ScriptTimer.After(game, ReplaySeconds, () =>
    {
        foreach (Agent agent in agents)
            Animate(agent, powerRef);

        KeepAnimating(game, agents, powerRef);
    }, () => agents.Any(agent => agent.IsDestroyed == false && agent.IsInWorld));
}

WorldEntity SpawnPiece(Region region, Piece piece, Vector3 position, float yawDegrees, bool hostile)
{
    WorldEntity entity;
    if (piece.IsAgent && hostile)
    {
        // Guards its spot (normal aggro range) instead of hunting players
        entity = ScriptSpawner.SpawnHostile(region, piece.Ref, position, 0f, 40f, null, true, true, false, true);
    }
    else
    {
        entity = ScriptSpawner.SpawnInteractable(region, piece.Ref, position, yawDegrees, Tag);
        if (entity != null)
            entity.Properties[PropertyEnum.EntSelActHasInteractOption] = false;   // scenery: no "use" prompt
    }

    return entity;
}

int PlaceScene(Region region, List<Piece> scene, Vector3 centre, PrototypeId powerRef, bool hostile)
{
    float sceneYaw = Random.Shared.NextSingle() * MathF.PI * 2f;
    var agents = new List<Agent>();
    int count = 0;

    foreach (Piece scenePiece in scene)
    {
        Piece piece = scenePiece;
        // Encounter snowmen may be the invisible "Smiley" ones: use a random visible snowman instead
        if (IsSnowmanProp(piece.Ref) && snowmanPropRefs.Count > 0)
            piece = piece with { Ref = snowmanPropRefs[Random.Shared.Next(snowmanPropRefs.Count)] };

        Vector3 position = centre + Rotate(piece.Offset, sceneYaw);
        float yawDegrees = MathHelper.ToDegrees(piece.Yaw + sceneYaw);
        string name = piece.Ref.GetName().Split('/').Last();

        WorldEntity entity = SpawnPiece(region, piece, position, yawDegrees, hostile);

        // Fallback: the exact layout spot can be inside a wall or off the navmesh; try the scene centre instead
        if (entity == null && Vector3.Distance2D(position, centre) > 1f)
        {
            Debug($"    {name}: failed at its layout spot {position}, trying the scene centre");
            entity = SpawnPiece(region, piece, centre, yawDegrees, hostile);
        }

        if (entity == null)
        {
            Log.Warn($"[xmas scenes]    {name}: could not be spawned (see the ScriptSpawner warning above)");
            continue;
        }

        Debug($"    {name}: spawned id {entity.Id} at {entity.RegionLocation.Position}{(hostile && piece.IsAgent ? " (hostile)" : "")}");
        count++;
        lock (Spawned(region)) Spawned(region).Add(entity.Id);
        if (entity is Agent agent)
            agents.Add(agent);
    }

    if (agents.Count > 0)
    {
        foreach (Agent agent in agents)
            Animate(agent, powerRef, true);
        KeepAnimating(region.Game, agents, powerRef);
    }

    return count;
}

// Where a hero actually stands in Hightown streets: scenes are spread over what is reachable from there.
// (The region's own start point is not where players arrive in Hightown; it sits in a cut-off corner.)
Vector3? FindAnchor(Region region, Avatar preferred = null)
{
    bool InStreets(Avatar avatar) => avatar != null && avatar.IsInWorld && avatar.Region == region
        && region.GetAreaAtPosition(avatar.RegionLocation.Position)?.PrototypeDataRef == areaRef;

    if (InStreets(preferred))
        return preferred.RegionLocation.Position;

    foreach (Player player in new PlayerIterator(region))
    {
        if (InStreets(player.CurrentAvatar))
            return player.CurrentAvatar.RegionLocation.Position;
    }

    return null;
}

void PlaceAll(Region region, Avatar preferred = null)
{
    Clear(region);
    placed[region.Id] = true;

    Vector3? arrival = FindAnchor(region, preferred);
    Debug($"placing scenes in [{region.PrototypeName}] (id {region.Id}), anchored on hero at {(arrival.HasValue ? arrival.Value.ToString() : "unknown (no hero in the streets): spots are not checked for reachability")}");

    var taken = new List<Vector3>();
    var summary = new List<string>();

    foreach (var kind in scenes)
    {
        if (kind.Scenes.Count == 0)
        {
            summary.Add($"0 {kind.Name} (no layouts)");
            continue;
        }

        int done = 0;
        for (int i = 0; i < kind.Count; i++)
        {
            // A spot where nothing could be spawned is given up; try up to 3 spots per scene
            bool ok = false;
            for (int attempt = 0; attempt < 3 && ok == false; attempt++)
            {
                if (TryPickSpot(region, arrival, taken, out Vector3 spot, out int stage) == false)
                {
                    Debug($"  {kind.Name} #{i + 1}: no spot found at any stage");
                    break;
                }

                List<Piece> scene = kind.Scenes[Random.Shared.Next(kind.Scenes.Count)];
                Debug($"  {kind.Name} #{i + 1}: spot {spot} ({StageNames[stage]}), {scene.Count} piece(s)");

                if (PlaceScene(region, scene, spot, kind.PowerRef, kind.Hostile) > 0)
                {
                    taken.Add(spot);
                    done++;
                    ok = true;
                }
                else
                {
                    Debug($"  {kind.Name} #{i + 1}: nothing spawned at that spot, trying another");
                }
            }
        }

        summary.Add($"{done}/{kind.Count} {kind.Name}");
    }

    Log.Info($"[xmas scenes] placed in [{region.PrototypeName}]: {string.Join(", ", summary)}, {Tagged(region).Count} entities");
}

//------------------------------------------------------------------------------
// Hooks
//------------------------------------------------------------------------------

Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    Region region = e.Region;
    if (region == null)
        return;

    if (IsHightown(region) == false)
    {
        // Say why a Madripoor region was not treated as Hightown
        if (Verbose && region.PrototypeName.Contains("Madripoor", StringComparison.OrdinalIgnoreCase))
            Debug($"[{region.PrototypeName}] is not Hightown: its areas are " +
                string.Join(", ", region.IterateAreas().Select(area => area.PrototypeDataRef.GetName().Split('/').Last())));
        return;
    }

    if (Enabled == false)
    {
        if (placed.TryRemove(region.Id, out _) || Tagged(region).Count > 0)
            Clear(region);
        return;
    }

    if (placed.ContainsKey(region.Id))
    {
        Debug($"[{region.PrototypeName}] (id {region.Id}) already has scenes ({Tagged(region).Count} entities alive)");
        return;
    }

    // Players arrive in the safe-zone hub and take a portal to the streets: wait until a hero is in the streets,
    // so the scenes can be spread over what is reachable from there
    placed[region.Id] = true;
    Debug($"[{region.PrototypeName}] (id {region.Id}): waiting for a hero to reach the streets");
    PlaceWhenInStreets(region.Game, region.Id, 0);
});

const float StreetCheckSeconds = 5f;
const int   StreetCheckMax     = 720;   // give up after an hour with nobody in the streets

void PlaceWhenInStreets(Game game, ulong regionId, int checks)
{
    ScriptTimer.After(game, StreetCheckSeconds, () =>
    {
        Region region = game.RegionManager.GetRegion(regionId);
        if (region == null || Enabled == false)
            return;

        // Already done meanwhile (e.g. !xmasscenes redo)
        if (Tagged(region).Count > 0)
            return;

        if (FindAnchor(region).HasValue == false)
        {
            if (checks + 1 < StreetCheckMax)
                PlaceWhenInStreets(game, regionId, checks + 1);
            else
                Debug($"[{region.PrototypeName}] (id {regionId}): nobody reached the streets, no scenes placed");
            return;
        }

        try
        {
            PlaceAll(region);
        }
        catch (Exception ex)
        {
            Log.Warn($"[xmas scenes] placing failed: {ex}");
        }
    });
}

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "xmasscenes")
        return;

    e.Handled = true;
    if (ScriptHooks.IsAdmin(e.Player) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    Region region = e.Player.CurrentAvatar?.Region;
    string arg = e.GetArg(0).ToLowerInvariant();

    if (arg == "debug")
    {
        e.Reply($"Region: {region?.PrototypeName ?? "none"} | Hightown: {IsHightown(region)} | layouts: " +
            string.Join(", ", scenes.Select(kind => $"{kind.Name} {kind.Scenes.Count}")));
        if (region != null)
            e.Reply("Areas: " + string.Join(", ", region.IterateAreas().Select(area => area.PrototypeDataRef.GetName().Split('/').Last())));
        Avatar avatar = e.Player.CurrentAvatar;
        if (avatar != null && region != null)
            e.Reply($"You are in area: {region.GetAreaAtPosition(avatar.RegionLocation.Position)?.PrototypeDataRef.GetName().Split('/').Last() ?? "none"}");
        return;
    }

    if (IsHightown(region) == false)
    {
        e.Reply("Go to Hightown first (or use !xmasscenes debug to see why this isn't Hightown).");
        return;
    }

    switch (arg)
    {
        case "redo":
            if (FindAnchor(region, e.Player.CurrentAvatar).HasValue == false)
            {
                e.Reply("Go through the portal to the Hightown streets first: scenes are spread out from where you stand.");
                return;
            }
            try
            {
                PlaceAll(region, e.Player.CurrentAvatar);
            }
            catch (Exception ex)
            {
                Log.Warn($"[xmas scenes] placing failed: {ex}");
                e.Reply($"Failed: {ex.Message}");
                return;
            }
            e.Reply($"Placed new scenes: {Tagged(region).Count} entities. Details in the server console.");
            break;

        case "here":
            // Puts one scene of each kind right next to you, skipping the spot search: tests spawning on its own
            {
                Avatar avatar = e.Player.CurrentAvatar;
                int total = 0;
                float offset = 0f;
                foreach (var kind in scenes.Where(k => k.Scenes.Count > 0))
                {
                    offset += 400f;
                    if (ScriptSpawner.TryFindSpotNear(region, avatar.RegionLocation.Position, offset - 100f, offset + 100f, out Vector3 spot) == false)
                    {
                        e.Reply($"{kind.Name}: no free spot near you");
                        continue;
                    }
                    Debug($"!xmasscenes here: {kind.Name} at {spot}");
                    total += PlaceScene(region, kind.Scenes[0], spot, kind.PowerRef, kind.Hostile);
                }
                e.Reply($"Spawned {total} entities around you. Details in the server console.");
            }
            break;

        case "check":
            // Is each spawned entity still there, and is the server sending it to this player's client?
            {
                Avatar avatar = e.Player.CurrentAvatar;
                Vector3 me = avatar.RegionLocation.Position;
                bool known = spawned.TryGetValue(region.Id, out List<ulong> ids);
                e.Reply($"You: region id {region.Id}, position {me}. Script spawned {(known ? ids.Count : 0)} entities in this region id.");
                if (known == false)
                {
                    e.Reply("Nothing recorded for this region id: the scenes went into a different Hightown instance. " +
                        "Ids with scenes: " + string.Join(", ", spawned.Keys));
                    break;
                }

                List<ulong> copy;
                lock (ids) copy = ids.ToList();
                foreach (ulong id in copy.OrderBy(id => region.Game.EntityManager.GetEntity<WorldEntity>(id) is WorldEntity w
                    ? Vector3.Distance2D(w.RegionLocation.Position, me) : float.MaxValue).Take(12))
                {
                    WorldEntity entity = region.Game.EntityManager.GetEntity<WorldEntity>(id);
                    if (entity == null)
                    {
                        e.Reply($"  id {id}: GONE (destroyed)");
                        continue;
                    }

                    string line = $"  {entity.PrototypeName.Split('/').Last()} id {id}: {Vector3.Distance2D(entity.RegionLocation.Position, me):0} away, " +
                        $"inWorld={entity.IsInWorld}, sentToYou={e.Player.AOI.InterestedInEntity(id)}, " +
                        $"visible={(bool)entity.Properties[PropertyEnum.Visible]}, dormant={(bool)entity.Properties[PropertyEnum.Dormant]}, " +
                        $"cell={entity.Cell?.PrototypeName.Split('/').Last() ?? "none"}";
                    e.Reply(line);
                    Log.Info("[xmas scenes] check:" + line);
                }
            }
            break;

        case "clear":
            Clear(region);
            placed[region.Id] = true;   // stays empty until "!xmasscenes redo" or the next Hightown instance
            e.Reply("Scenes removed from this Hightown.");
            break;

        default:
            e.Reply($"This Hightown has {Tagged(region).Count} scene entities. Use !xmasscenes redo / here / clear / debug.");
            break;
    }
});

Log.Info($"Christmas Hightown scenes loaded ({(Enabled ? "on" : "off")}): " +
    string.Join(", ", scenes.Select(kind => $"{kind.Count} {kind.Name} ({kind.Scenes.Count} layouts)")) +
    (areaRef == PrototypeId.Invalid ? " -- HIGHTOWN AREA NOT FOUND, nothing will spawn" : ""));