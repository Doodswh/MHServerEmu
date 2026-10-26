// The Statue of Doods in Avengers Tower: an Asgard plaque statue that opens a thank-you panel for the people who made
// this server possible. Credits as in MHServerEmu's CREDITS.md (Crypto137 and AlexBond: developers; MonEll: Discord MVP),
// plus MonEll's role as Project T.A.H.I.T.I. lead and help with code and knowledge (from Doods).

using MHServerEmu.Games.GameData;

const string StatuePath = "Entity/Characters/NPCs/PlaquestatueAsgardOpen.prototype";   // model is in the stock client
const string StatueTag = "statue_of_doods";

// Avengers Tower: north of Adam Warlock (Eternity Splinter vendor at 463, -828), against the edge of the floor by the
// chairs (the floor ends at about X 760 there), facing south toward him
Vector3 StatuePosition = new(690f, -828f, 131f);
const float StatueYaw = 180f;
const float LabelEverySeconds = 8f;

ScriptText.Register("statue.label", "Statue of Doods");
ScriptText.Register("statue.panel",
    "STATUE OF DOODS\n\n" +
    "This server runs on MHServerEmu, the open source server emulator that brought Marvel Heroes back to life. " +
    "None of this exists without the people behind it:\n\n" +
    "Crypto137 - MHServerEmu developer, Lead\n\n" +
    "AlexBond - MHServerEmu developer\n\n" +
    "MonEll - Project Tahiti Director, Friend of the Community!\n\n" +
    "Thank you for giving us our game back.");
ScriptText.Register("statue.close", "Thank you");

// The statue's own name (shown when hovering it). Note: this renames every Asgard plaque statue of this kind.
var statueProto = GameDatabase.GetPrototypeRefByName(StatuePath).As<WorldEntityPrototype>();
if (statueProto != null && statueProto.DisplayName != LocaleStringId.Blank)
    ScriptText.OverrideText(statueProto.DisplayName, "Statue of Doods");
else
    Log.Warn("[statue] couldn't rename the statue");

var placedIn = new System.Collections.Concurrent.ConcurrentDictionary<ulong, bool>();

void PlaceStatue(Region region)
{
    // a script reload forgets what it placed: remove old copies first
    foreach (WorldEntity old in region.Entities.OfType<WorldEntity>().Where(entity => ScriptSpawner.GetTag(entity) == StatueTag).ToList())
        ScriptSpawner.Despawn(region.Game, old.Id);

    WorldEntity statue = ScriptSpawner.SpawnInteractable(region, GameDatabase.GetPrototypeRefByName(StatuePath), StatuePosition, StatueYaw, StatueTag);
    if (statue == null) { Log.Warn("[statue] couldn't place the Statue of Doods"); return; }

    (statue as Agent)?.AIController?.SetIsEnabled(false);   // a statue stands still
    Log.Info($"[statue] Statue of Doods placed in the hub at {StatuePosition}");

    // overhead label, re-shown so players walking up see it (text reaches players when they connect)
    ulong regionId = region.Id, statueId = statue.Id;
    Game game = region.Game;
    void Tick()
    {
        if (game.EntityManager.GetEntity<WorldEntity>(statueId) is not WorldEntity current || current.IsInWorld == false)
        {
            placedIn.TryRemove(regionId, out _);
            return;
        }
        ScriptText.ShowOverheadText(current, "statue.label", 0, LabelEverySeconds + 1.5f);
        ScriptTimer.After(game, LabelEverySeconds, Tick);
    }
    Tick();
}

Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    Region region = e.Region;
    if (region == null || region.PrototypeName.Contains("NPEAvengersTowerHUBRegion", StringComparison.OrdinalIgnoreCase) == false)
        return;

    if (placedIn.TryAdd(region.Id, true) == false) return;
    ulong regionId = region.Id;
    Game game = region.Game;
    ScriptTimer.After(game, 2f, () => { if (game.RegionManager.GetRegion(regionId) is Region current) PlaceStatue(current); });
});

Hooks.On(ScriptHooks.EntityInteracted, e =>
{
    if (e.ScriptTag != StatueTag) return;
    ScriptDialog.Show(e.Player, "statue.panel", "statue.close", "", null, e.Entity);
});

Log.Info("Statue of Doods loaded");
