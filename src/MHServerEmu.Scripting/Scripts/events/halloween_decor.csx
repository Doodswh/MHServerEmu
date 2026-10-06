// Halloween decorations: scatters Halloween props at random spots around the hub. Every hub instance gets its own random
// arrangement the first time a player enters it.
//
//  - Props (candles, skull piles, cauldrons) are scenery: they cannot be clicked or broken.
//  - Pumpkins are the Halloween Pumpkin item lying on the ground (the game has no pumpkin prop, only the item's model).
//    By default they are scenery: the server ignores attempts to pick them up, though the client still shows the item's
//    name and lets players click it. With PumpkinsCanBePickedUp = true they can be taken and grow back after
//    PumpkinRegrowMinutes. Set Pumpkins to 0 to leave them out.
//
//   !decor redo     (admin) clear this hub's decorations and place a new random arrangement
//   !decor clear    (admin) remove this hub's decorations
//   !decor count    (admin) how many decorations this hub has

using System.Collections.Concurrent;

//------------------------------------------------------------------------------
// Settings
//------------------------------------------------------------------------------

const bool Enabled = true;

// Hubs to decorate
var Hubs = new[]
{
    "Regions/HUBRevamp/NPEAvengersTowerHUBRegion.prototype",   // the current Avengers Tower
};

// Scenery: prop path and how many of each
var Props = new (string Path, int Count)[]
{
    ("Entity/Props/MultiStageDestructibles/DestructibleHauntedCandlesA.prototype", 14),
    ("Entity/Props/Destructibles/DestructibleSkullPile.prototype",                  8),
    ("Entity/Props/Missions/DrStrangeCauldronProp.prototype",                       6),
};

const string PumpkinItem      = "Entity/Items/CurrencyItems/SeasonalLE/Seasonal/HalloweenPumpkin.prototype";
const int   Pumpkins          = 0;       // pumpkins lying around (0 = none; tested: the only pumpkin model is the small dropped item, and the size effect does not grow items)
const bool  PumpkinsCanBePickedUp = false;   // false = scenery: the server ignores attempts to pick them up
const bool  GrowPumpkins      = false;   // the Giant-Man size effect: tested, the client does not apply it to items
const string GrowPower        = "Powers/Player/AntMan/GiantManFootGrowConditionEffect.prototype";
const float PumpkinRegrowMinutes = 30f;  // pickable pumpkins only: picked ones are replaced this often (checked when a player enters the hub)

const float MinSpacing        = 350f;    // decorations stay at least this far apart (spreads them through the hub)
const float KeepClearRadius   = 300f;    // and this far from where players arrive
const int   TriesPerItem      = 12;      // random spots tried for each decoration before giving up on it

//------------------------------------------------------------------------------
// State
//------------------------------------------------------------------------------

const string PropTag    = "halloween_decor";
const string PumpkinTag = "halloween_decor_pumpkin";

var decorated   = new ConcurrentDictionary<ulong, bool>();       // hub region id => arrangement placed
var lastRegrow  = new ConcurrentDictionary<ulong, TimeSpan>();   // hub region id => game time pumpkins were last topped up

PrototypeId[] hubRefs = Hubs.Select(path => GameDatabase.GetPrototypeRefByName(path)).ToArray();
var propRefs = Props.Select(prop => (Ref: GameDatabase.GetPrototypeRefByName(prop.Path), prop.Count, prop.Path)).ToList();
PrototypeId pumpkinRef = GameDatabase.GetPrototypeRefByName(PumpkinItem);

foreach (var prop in propRefs)
{
    if (prop.Ref == PrototypeId.Invalid)
        Log.Warn($"Decoration not found: {prop.Path}");
}

bool IsHub(Region region) => region != null && hubRefs.Contains(region.PrototypeDataRef);

List<WorldEntity> Tagged(Region region, string tag)
{
    return region.Entities.OfType<WorldEntity>().Where(entity => ScriptSpawner.GetTag(entity) == tag && entity.IsDestroyed == false).ToList();
}

// A random walkable spot away from the arrival point and from everything in taken
bool TryPickSpot(Region region, Vector3? arrival, List<Vector3> taken, out Vector3 spot)
{
    for (int i = 0; i < TriesPerItem; i++)
    {
        if (ScriptSpawner.TryFindRandomSpot(region, out spot, arrival) == false)
            continue;

        if (arrival.HasValue && Vector3.Distance2D(spot, arrival.Value) < KeepClearRadius)
            continue;

        Vector3 candidate = spot;
        if (taken.Any(other => Vector3.Distance2D(other, candidate) < MinSpacing))
            continue;

        return true;
    }

    spot = Vector3.Zero;
    return false;
}

int PlacePumpkins(Region region, Vector3? arrival, List<Vector3> taken, int count)
{
    if (pumpkinRef == PrototypeId.Invalid)
        return 0;

    int placed = 0;
    for (int i = 0; i < count; i++)
    {
        if (TryPickSpot(region, arrival, taken, out Vector3 spot) == false)
            continue;

        WorldEntity pumpkin = ScriptSpawner.SpawnGroundItem(region, pumpkinRef, spot, PumpkinTag, PumpkinsCanBePickedUp);
        if (pumpkin != null)
        {
            // The dropped pumpkin is small: grow it with the Giant-Man size effect (4x), if the client applies it to items
            if (GrowPumpkins)
                ScriptConditions.ApplyPowerCondition(pumpkin, "halloween_decor.grow", GrowPower);

            taken.Add(spot);
            placed++;
        }
    }

    return placed;
}

void Clear(Region region)
{
    foreach (WorldEntity entity in Tagged(region, PropTag).Concat(Tagged(region, PumpkinTag)))
        ScriptSpawner.Despawn(region.Game, entity.Id);
}

void Decorate(Region region)
{
    // Start clean: also removes an arrangement left behind by an earlier load of this script
    Clear(region);

    Vector3? arrival = ScriptSpawner.TryGetStartPosition(region, out Vector3 start) ? start : null;
    var taken = new List<Vector3>();
    int placed = 0;

    foreach (var prop in propRefs)
    {
        if (prop.Ref == PrototypeId.Invalid)
            continue;

        for (int i = 0; i < prop.Count; i++)
        {
            if (TryPickSpot(region, arrival, taken, out Vector3 spot) == false)
                continue;

            float yaw = Random.Shared.NextSingle() * 360f;
            WorldEntity entity = ScriptSpawner.SpawnInteractable(region, prop.Ref, spot, yaw, PropTag);
            if (entity == null)
                continue;

            // Scenery only: no "use" prompt when players hover it
            entity.Properties[PropertyEnum.EntSelActHasInteractOption] = false;
            taken.Add(spot);
            placed++;
        }
    }

    decorated[region.Id] = true;   // set before the pumpkins, so a pumpkin failure never makes the hub redecorate

    int pumpkins = 0;
    try
    {
        pumpkins = PlacePumpkins(region, arrival, taken, Pumpkins);
    }
    catch (Exception ex)
    {
        Log.Warn($"Halloween pumpkins could not be placed: {ex.Message}");
    }

    lastRegrow[region.Id] = region.Game.CurrentTime;
    Log.Info($"Halloween decorations placed in [{region.PrototypeName}]: {placed} props, {pumpkins} pumpkins");
}

// Replace pumpkins that were picked up, at most every PumpkinRegrowMinutes
void RegrowPumpkins(Region region)
{
    if (Pumpkins <= 0 || PumpkinsCanBePickedUp == false)
        return;

    TimeSpan now = region.Game.CurrentTime;
    if (lastRegrow.TryGetValue(region.Id, out TimeSpan last) && (now - last).TotalMinutes < PumpkinRegrowMinutes)
        return;

    lastRegrow[region.Id] = now;

    int missing = Pumpkins - Tagged(region, PumpkinTag).Count;
    if (missing <= 0)
        return;

    Vector3? arrival = ScriptSpawner.TryGetStartPosition(region, out Vector3 start) ? start : null;
    List<Vector3> taken = Tagged(region, PropTag).Concat(Tagged(region, PumpkinTag)).Select(entity => entity.RegionLocation.Position).ToList();
    try
    {
        PlacePumpkins(region, arrival, taken, missing);
    }
    catch (Exception ex)
    {
        Log.Warn($"Halloween pumpkins could not be regrown: {ex.Message}");
    }
}

//------------------------------------------------------------------------------
// Hooks
//------------------------------------------------------------------------------

Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    Region region = e.Region;
    if (IsHub(region) == false)
        return;

    if (Enabled == false)
    {
        // Switched off: take down anything an earlier load put up
        if (decorated.TryRemove(region.Id, out _) || Tagged(region, PropTag).Count > 0)
            Clear(region);
        return;
    }

    if (decorated.ContainsKey(region.Id))
        RegrowPumpkins(region);
    else
        Decorate(region);
});

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "decor")
        return;

    e.Handled = true;
    if (ScriptHooks.IsAdmin(e.Player) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    Region region = e.Player.CurrentAvatar?.Region;
    if (IsHub(region) == false)
    {
        e.Reply("Go to a decorated hub first (Avengers Tower).");
        return;
    }

    switch (e.GetArg(0).ToLowerInvariant())
    {
        case "redo":
            Decorate(region);
            e.Reply($"Redecorated: {Tagged(region, PropTag).Count} props, {Tagged(region, PumpkinTag).Count} pumpkins.");
            break;

        case "clear":
            Clear(region);
            decorated[region.Id] = true;   // stays empty until "!decor redo" or the next hub instance
            e.Reply("Decorations removed from this hub.");
            break;

        default:
            e.Reply($"This hub has {Tagged(region, PropTag).Count} props and {Tagged(region, PumpkinTag).Count} pumpkins. Use !decor redo or !decor clear.");
            break;
    }
});

Log.Info($"Halloween decorations loaded ({(Enabled ? "on" : "off")}, {propRefs.Sum(p => p.Count)} props + {Pumpkins} pumpkins per hub)");
