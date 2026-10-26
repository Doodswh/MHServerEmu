// Omega difficulty NPC in Avengers Tower: the King Cow (a Skrull cow mob, made friendly and still) on the event NPC line.
// Click him: "Omega" does what !difficulty omega does (needs the player to be Omega certified: any one hero at max
// prestige), "Reset" does what !reset_diff does.
// Both also apply to the party when the player leads one, like the commands.

using MHServerEmu.Games.Entities.Avatars;

const string NpcPath = "Entity/Characters/Mobs/CowsEG/SpearKingCow1.prototype";   // model is in the stock client
const string NpcTag = "omega_difficulty_npc";
const string OmegaTierPath = "Difficulty/Tiers/Tier5Omega1.prototype";

// Avengers Tower event NPC line (walkway east of Hank Pym), the spot after Terminal Breach
Vector3 NpcPosition = new(-2530f, 1940f, 140f);
const float NpcYaw = 270f;

const float LabelEverySeconds = 8f;

ScriptText.Register("omegadiff.label", "Difficulty: Omega / Reset");
ScriptText.Register("omegadiff.msg", "Moo. Want to fight in Omega difficulty? You need to be Omega certified (any one hero at max prestige). Reset puts you back on normal difficulty choice.");
ScriptText.Register("omegadiff.omega", "Omega");
ScriptText.Register("omegadiff.reset", "Reset");

void Tell(Player player, string text) => ScriptHooks.SendChatMessage(player, "[Difficulty] " + text, false);

// Same steps as the !difficulty omega command
void SetOmega(Player player)
{
    Avatar avatar = player.CurrentAvatar;
    if (avatar == null) { Tell(player, "No active hero."); return; }

    // Omega certified: any one of the player's heroes at max prestige (the [Ω] name tag), not necessarily this one
    if (player.IsOmegaCertified() == false)
    {
        Tell(player, "You need at least one hero at maximum prestige (Omega certified) to access Omega difficulty.");
        return;
    }

    PrototypeId omegaRef = GameDatabase.GetPrototypeRefByName(OmegaTierPath);
    if (omegaRef == PrototypeId.Invalid) { Log.Warn($"[omega difficulty] no prototype {OmegaTierPath}"); return; }

    if (player.CanChangeDifficulty(omegaRef) == false)
    {
        Tell(player, "You can't change to that difficulty right now.");
        return;
    }

    avatar.Properties[PropertyEnum.DifficultyTierPreference] = omegaRef;
    player.DifficultyPreferenceLocked = true;
    player.SendDifficultyTierPreferenceToPlayerManager();
    player.UpdatePartyDifficulty(omegaRef);   // party leader only, like the command

    Tell(player, player.GetParty() != null ? "Difficulty set to Omega (party too, if you lead it)." : "Difficulty set to Omega.");
    Log.Info($"[omega difficulty] {player.GetName()} set Omega");
}

// Same steps as the !reset_diff command
void ResetDifficulty(Player player)
{
    Avatar avatar = player.CurrentAvatar;
    if (avatar == null) { Tell(player, "No active hero."); return; }

    avatar.Properties[PropertyEnum.DifficultyTierPreference] = PrototypeId.Invalid;
    player.DifficultyPreferenceLocked = false;
    player.SendDifficultyTierPreferenceToPlayerManager();
    player.UpdatePartyDifficulty(PrototypeId.Invalid);   // party leader only, like the command

    Tell(player, "Difficulty preference reset.");
    Log.Info($"[omega difficulty] {player.GetName()} reset difficulty");
}

var placedIn = new System.Collections.Concurrent.ConcurrentDictionary<ulong, bool>();

void PlaceNpc(Region region)
{
    // a script reload forgets what it placed: remove old copies first
    foreach (WorldEntity old in region.Entities.OfType<WorldEntity>().Where(entity => ScriptSpawner.GetTag(entity) == NpcTag).ToList())
        ScriptSpawner.Despawn(region.Game, old.Id);

    WorldEntity npc = ScriptSpawner.SpawnInteractable(region, GameDatabase.GetPrototypeRefByName(NpcPath), NpcPosition, NpcYaw, NpcTag);
    if (npc == null) { Log.Warn("[omega difficulty] couldn't place the King Cow"); return; }

    // a mob: keep him standing still instead of running his combat AI
    (npc as Agent)?.AIController?.SetIsEnabled(false);
    Log.Info($"[omega difficulty] King Cow placed in the hub at {NpcPosition}");

    // overhead label, re-shown so players walking up see it (text reaches players when they connect)
    ulong regionId = region.Id, npcId = npc.Id;
    Game game = region.Game;
    void Tick()
    {
        if (game.EntityManager.GetEntity<WorldEntity>(npcId) is not WorldEntity current || current.IsInWorld == false)
        {
            placedIn.TryRemove(regionId, out _);
            return;
        }
        ScriptText.ShowOverheadText(current, "omegadiff.label", 0, LabelEverySeconds + 1.5f);
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
    ScriptTimer.After(game, 2f, () => { if (game.RegionManager.GetRegion(regionId) is Region current) PlaceNpc(current); });
});

Hooks.On(ScriptHooks.EntityInteracted, e =>
{
    if (e.ScriptTag != NpcTag) return;

    ScriptDialog.Show(e.Player, "omegadiff.msg", "omegadiff.omega", "omegadiff.reset", (player, button) =>
    {
        if (button == 1) SetOmega(player);
        else if (button == 2) ResetDifficulty(player);
    }, e.Entity);
});

Log.Info("Omega difficulty NPC loaded (King Cow in Avengers Tower)");
