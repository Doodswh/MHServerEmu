// Terminal Breach: a S.H.I.E.L.D. agent in Avengers Tower opens breaches into random Cosmic terminals.
//
//  1. Click the agent in the hub. A popup offers to open a breach; "Open Breach" sends you and your party members standing
//     in the hub into a random Cosmic terminal from Terminals.
//  2. Every breach is a brand-new instance (it is requested with a fresh layout seed) and its layout is reshuffled:
//     a random share of rooms and connections is cut (RoomCut / ConnectionCut).
//  3. Breach Anchors: AnchorCount villains guard spots spread across the map (AnchorPool). Find and defeat them all.
//     Nearest-target hints arrive in chat every HintInterval; !breach shows them any time.
//  4. The Breach Overlord (FinalPool) then appears at the far end of the map. Defeat it to close the breach.
//  5. Everyone who damaged a breach boss gets AnchorRewards per anchor and CompletionRewards at the end. After ReturnDelay
//     everyone still in the terminal is sent back to town. The breach collapses after TimeLimit.
//
// The terminal's own enemies, missions and boss are still there; the breach adds to them.
//
//   !breach              status, time left and distance to the nearest breach target
//   !breach open         (admin) open a breach without the NPC
//   !breach npc here     (admin) move the hub agent to you and print the position to paste into NpcPosition
//
// NOTE: on-screen text registered here reaches players the next time they connect (client limitation).

using System.Collections.Concurrent;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Events;

//------------------------------------------------------------------------------
// Settings
//------------------------------------------------------------------------------

const string HubRegion     = "Regions/HUBRevamp/NPEAvengersTowerHUBRegion.prototype";   // the current Avengers Tower
const string NpcPath       = "Entity/Characters/NPCs/HubNPCs/SHIELDAgentMale.prototype";
Vector3? NpcPosition       = null;   // null = next to where players arrive in the hub; use "!breach npc here" to pick a spot
float NpcYawDegrees        = 0f;     // only used with NpcPosition

const int   AnchorCount        = 4;
const int   AnchorAffixes      = 1;       // extra random affixes on each anchor villain
const int   LordAffixes        = 3;       // extra random affixes on the Breach Overlord
const float MinAnchorDistance  = 1500f;   // anchors spawn at least this far (walking distance aside) from the entrance
const float MinAnchorSpacing   = 1000f;   // and this far from each other
const float TimeLimit          = 900f;    // seconds; the breach collapses after this
const string TimerWidget       = "UI/MetaGame/DangerRoom/DangerRoomTimer.prototype";   // on-screen "Time:" countdown
const bool   ShowLordHealth    = true;   // HUD health bar (boss icon + health %) for the Breach Overlord
const string LordHealthWidget  = "UI/MetaGame/SurturRaid/FiveMan/SlagHealth.prototype";
const float StartDelay         = 6f;      // seconds after the first player has loaded in
const float LoadInTimeout      = 90f;
const float PendingTimeout     = 180f;    // a breach nobody entered within this is forgotten
const float ReturnDelay        = 60f;     // seconds after the end before players are sent back to town (0 = stay)
const float EmptyRegionTimeout = 120f;    // end the breach if nobody is inside this long
const float HintInterval       = 90f;
const float TickSeconds        = 2f;
const bool  BossesDropLoot     = true;
const float OverheadSeconds    = 5f;
const string AffixExclude      = "";

// Layout reshuffle for each breach: random percentages in these ranges (the terminal's own values are replaced)
const int RoomCutMin       = 10, RoomCutMax       = 35;
const int ConnectionCutMin = 0,  ConnectionCutMax = 40;

// Cosmic terminals with random layouts (areas built by the grid generators)
const string CosmicTerminals = "Regions/EndGame/Terminals/Cosmic/";
const string EventDifficulty = "Difficulty/Tiers/Tier3Superheroic.prototype";   // Cosmic, whatever the party / player difficulty is
var Terminals = new List<Terminal>
{
    new("A.I.M. Facility",        CosmicTerminals + "AIMFacility/DailyCAIMFacilityRegion.prototype"),
    new("Hood's Ship",            CosmicTerminals + "HoodsShip/DailyCHoodsShipRegion.prototype"),
    new("HYDRA Island",           CosmicTerminals + "HYDRAIsland/DailyCHYDRAIslandRegion.prototype"),
    new("Kingpin's Warehouse",    CosmicTerminals + "KingpinsWarehouse/DailyCKingpinRegion.prototype"),
    new("Stryker's Bunker",       CosmicTerminals + "MagnetoBunker/DailyCStrykerBunkerRegion.prototype"),
    new("Shocker's Subway",       CosmicTerminals + "ShockerSubway/DailyCShockerSubwayRegion.prototype"),
    new("Sinister's Lab",         CosmicTerminals + "SinistersLab/DailyCSinisterLabRegion.prototype"),
    new("Taskmaster's Base",      CosmicTerminals + "Taskmaster/DailyCTaskmasterRegion.prototype"),
};

const string Villain = "Entity/Characters/Bosses/PatrolMidtown/MidtownEvent";
var AnchorPool = new List<Boss>
{
    new("Bullseye",         Villain + "Bullseye.prototype"),
    new("Rhino",            Villain + "Rhino.prototype"),
    new("Electro",          Villain + "Electro.prototype"),
    new("Taskmaster",       Villain + "Taskmaster.prototype"),
    new("Kraven",           Villain + "Kraven.prototype"),
    new("Tombstone",        Villain + "Tombstone.prototype"),
    new("Lizard",           Villain + "Lizard.prototype"),
    new("Doctor Octopus",   Villain + "DoctorOctopus.prototype"),
    new("Green Goblin",     Villain + "GreenGoblin.prototype"),
    new("Sabretooth",       Villain + "Sabretooth.prototype"),
    new("Lady Deathstrike", Villain + "LadyDeathstrike.prototype"),
};

var FinalPool = new List<Boss>
{
    new("Juggernaut",      Villain + "Juggernaut.prototype"),
    new("Venom",           Villain + "Venom.prototype"),
    new("Magneto",         Villain + "Magneto.prototype"),
    new("Mister Sinister", Villain + "MisterSinister.prototype"),
    new("Doctor Doom",     Villain + "DoctorDoom.prototype"),
};

//------------------------------------------------------------------------------
// Rewards (dropped at the feet of each player who damaged the boss, one copy each)
//------------------------------------------------------------------------------

const string FC = "Entity/Items/Consumables/Prototypes/FortuneCard/";

var AnchorRewards = new Reward[]
{
    new(FC + "SpiderManHomecomingFortuneCard.prototype"),
    new(FC + "LoganFortuneCard.prototype"),
};

var CompletionRewards = new Reward[]
{
    new("Entity/Items/Gems/Gem1.prototype", 1),   // StarkTech Power Cube (only here, at the end)
    new("Entity/Items/Consumables/Prototypes/RandomGiftboxes/RandomCosmicArtifactBox.prototype"),
    new("Entity/Items/Consumables/Prototypes/DailyGift/LargeRunebox.prototype"),
    new(FC + "OdinsBountyFortuneCard.prototype", 3),
};

//------------------------------------------------------------------------------
// On-screen text (registered at load)
//------------------------------------------------------------------------------

var AllBosses = AnchorPool.Concat(FinalPool).ToList();

ScriptText.Register("breach_prompt", "Open a Terminal Breach?\nYou and your party members here enter a random Cosmic terminal with a fresh layout. " +
    "Destroy the Breach Anchors hidden across the map, then defeat the Breach Overlord.");
ScriptText.Register("breach_enter", "Open Breach");
ScriptText.Register("breach_cancel", "Not now");
ScriptText.Register("breach_anchor_down", "Breach Anchor Destroyed!");
ScriptText.Register("breach_complete", "Terminal Breach Closed!");
ScriptText.Register("breach_failed", "The Breach Collapsed");
ScriptText.Register("breach_obj_anchors", "Terminal Breach - Destroy the Breach Anchors");
ScriptText.Register("breach_obj_lord", "Terminal Breach - Defeat the Breach Overlord");
ScriptText.Register("breach_overhead", "Top Damage!");

for (int i = 0; i < Terminals.Count; i++)
    ScriptText.RegisterRange("breach_start", $"Terminal Breach: {Terminals[i].Name}", i, i);

for (int i = 0; i < AllBosses.Count; i++)
{
    string name = AllBosses[i].Name;
    bool isLord = i >= AnchorPool.Count;
    ScriptText.RegisterRange("breach_arrive", isLord ? $"{name} has come through the breach!" : $"{name} guards a Breach Anchor", i, i);
    ScriptText.RegisterRange("breach_lord", $"The Breach Overlord emerges: {name}!", i, i);
    ScriptText.Register($"breach_top_{i}", $"{name} Defeated! Top damage: $playersource$ ($intargzero$%)");
    ScriptText.Register($"breach_top2_{i}", $"{name} Defeated! 1st: $playersource$ ($intargzero$%)  2nd: $playertarget$ ($intargone$%)");
}

//------------------------------------------------------------------------------
// State. A run is created in the hub's game and then only touched on the terminal instance's game thread.
//------------------------------------------------------------------------------

var pendingBySeed   = new ConcurrentDictionary<int, Run>();    // layout seed => breach not entered yet
var pendingByPlayer = new ConcurrentDictionary<ulong, Run>();  // player db id => breach they were sent to
var runs            = new ConcurrentDictionary<Region, Run>(); // terminal instance => running breach
var hubNpcs         = new ConcurrentDictionary<Region, ulong>(); // hub instance => agent entity id

PrototypeId hubRef = GameDatabase.GetPrototypeRefByName(HubRegion);
PrototypeId npcRef = GameDatabase.GetPrototypeRefByName(NpcPath);
const string NpcTag = "terminal_breach";

foreach (Terminal terminal in Terminals)
{
    terminal.Ref = GameDatabase.GetPrototypeRefByName(terminal.Path);
    if (terminal.Ref == PrototypeId.Invalid)
        Log.Warn($"Terminal not found: {terminal.Path}");
}

foreach (Boss boss in AllBosses)
{
    boss.Ref = ScriptSpawner.FindAgent(boss.Match);
    if (boss.Ref == PrototypeId.Invalid)
        Log.Warn($"Boss not found: {boss.Match}");
}

if (hubRef == PrototypeId.Invalid) Log.Warn($"Hub region not found: {HubRegion}");
if (npcRef == PrototypeId.Invalid) Log.Warn($"NPC not found: {NpcPath}");

bool IsAdmin(Player player) => ScriptHooks.IsAdmin(player);   // Admin or Dev accounts

IEnumerable<Player> PlayersIn(Region region)
{
    foreach (Player player in new PlayerIterator(region))
    {
        Avatar avatar = player.CurrentAvatar;
        if (avatar != null && avatar.IsInWorld && avatar.Region == region)
            yield return player;
    }
}

void Announce(Region region, string text)
{
    foreach (Player player in PlayersIn(region))
        ScriptHooks.SendChatMessage(player, "[Terminal Breach] " + text, false);
}

string FormatTime(float seconds)
{
    int total = Math.Max((int)Math.Ceiling(seconds), 0);
    if (total < 60) return $"{total} seconds";
    int minutes = total / 60, rest = total % 60;
    string text = minutes == 1 ? "1 minute" : $"{minutes} minutes";
    return rest == 0 ? text : $"{text} {rest} seconds";
}

//------------------------------------------------------------------------------
// Hub agent
//------------------------------------------------------------------------------

void EnsureHubNpc(Region hub, Vector3? positionOverride = null, float? yawOverride = null)
{
    if (npcRef == PrototypeId.Invalid)
        return;

    if (positionOverride == null && hubNpcs.TryGetValue(hub, out ulong existingId) && ScriptSpawner.IsAlive(hub.Game, existingId))
        return;

    if (hubNpcs.TryRemove(hub, out ulong oldId))
        ScriptSpawner.Despawn(hub.Game, oldId);

    Vector3 position;
    float yaw;

    if (positionOverride.HasValue || NpcPosition.HasValue)
    {
        position = positionOverride ?? NpcPosition.Value;
        yaw = yawOverride ?? NpcYawDegrees;
    }
    else
    {
        // Next to where players arrive, facing them
        if (ScriptSpawner.TryGetStartPosition(hub, out Vector3 start) == false ||
            ScriptSpawner.TryFindSpotNear(hub, start, 250f, 500f, out position) == false)
        {
            Log.Warn("Could not find a spot for the breach agent in the hub; set NpcPosition (use !breach npc here)");
            return;
        }

        yaw = MathF.Atan2(start.Y - position.Y, start.X - position.X) * 180f / MathF.PI;
    }

    WorldEntity npc = ScriptSpawner.SpawnInteractable(hub, npcRef, position, yaw, NpcTag);
    if (npc != null)
    {
        hubNpcs[hub] = npc.Id;
        Log.Info($"Breach agent placed in the hub at {position}");
    }
}

//------------------------------------------------------------------------------
// Opening a breach
//------------------------------------------------------------------------------

void ForgetExpiredPending()
{
    DateTime now = DateTime.UtcNow;
    foreach (var pair in pendingBySeed)
    {
        if ((now - pair.Value.CreatedAt).TotalSeconds > PendingTimeout)
            pendingBySeed.TryRemove(pair.Key, out _);
    }

    foreach (var pair in pendingByPlayer)
    {
        if ((now - pair.Value.CreatedAt).TotalSeconds > PendingTimeout)
            pendingByPlayer.TryRemove(pair.Key, out _);
    }
}

void OpenBreach(Player opener)
{
    ForgetExpiredPending();

    var available = Terminals.Where(terminal => terminal.Ref != PrototypeId.Invalid).ToList();
    if (available.Count == 0)
    {
        ScriptHooks.SendChatMessage(opener, "[Terminal Breach] No terminals are available.", false);
        return;
    }

    int terminalIndex = Terminals.IndexOf(available[Random.Shared.Next(available.Count)]);
    Terminal terminal = Terminals[terminalIndex];

    var run = new Run
    {
        Seed = Random.Shared.Next(1, int.MaxValue),
        TerminalIndex = terminalIndex,
        OpenerName = opener.GetName(),
        CreatedAt = DateTime.UtcNow,
    };

    List<Player> group = ScriptTeleport.GetPartyMembersInRegion(opener);
    pendingBySeed[run.Seed] = run;
    foreach (Player member in group)
        pendingByPlayer[member.DatabaseUniqueId] = run;

    int sent = 0;
    foreach (Player member in group)
    {
        if (ScriptTeleport.ToRegion(member, terminal.Path, EventDifficulty, run.Seed))
        {
            sent++;
            ScriptHooks.SendChatMessage(member, $"[Terminal Breach] {opener.GetName()} opened a breach into {terminal.Name}!", false);
        }
    }

    if (sent == 0)
    {
        pendingBySeed.TryRemove(run.Seed, out _);
        ScriptHooks.SendChatMessage(opener, "[Terminal Breach] The breach failed to open (see the server log).", false);
        return;
    }

    Log.Info($"{opener.GetName()} opened a breach into {terminal.Name} for {sent} player(s), seed {run.Seed}");
}

//------------------------------------------------------------------------------
// Running a breach (terminal instance game thread)
//------------------------------------------------------------------------------

Run FindRunFor(Player player, Region region)
{
    if (runs.TryGetValue(region, out Run running))
        return running;

    // The instance was created with the breach's seed
    if (pendingBySeed.TryGetValue(region.RandomSeed, out Run run) && Terminals[run.TerminalIndex].Ref == region.PrototypeDataRef)
        return run;

    // Fallback (the server retried generation with another seed): the breach this player was just sent to
    if (pendingByPlayer.TryGetValue(player.DatabaseUniqueId, out run) && run.Region == null &&
        Terminals[run.TerminalIndex].Ref == region.PrototypeDataRef && (DateTime.UtcNow - run.CreatedAt).TotalSeconds <= PendingTimeout)
        return run;

    return null;
}

void BindRun(Run run, Region region, Player player)
{
    if (run.Region != null)
        return;

    run.Region = region;
    runs[region] = run;
    pendingBySeed.TryRemove(run.Seed, out _);
    foreach (var pair in pendingByPlayer.Where(pair => pair.Value == run).ToList())
        pendingByPlayer.TryRemove(pair.Key, out _);

    AttachDamageTracking(run);
    WaitForLoadIn(run, player, 0f);
}

void WaitForLoadIn(Run run, Player player, float waited)
{
    if (run.Ended)
        return;

    Avatar avatar = player.IsDestroyed ? null : player.CurrentAvatar;
    if (avatar == null || avatar.IsInWorld == false || avatar.Region != run.Region)
    {
        // Anyone else who made it in can carry the start
        Player other = PlayersIn(run.Region).FirstOrDefault();
        if (other != null && other != player)
        {
            WaitForLoadIn(run, other, waited);
            return;
        }

        if (waited >= LoadInTimeout)
            EndRun(run, false, "Nobody arrived.", false);
        else
            After(run.Region.Game, 2f, () => WaitForLoadIn(run, player, waited + 2f));
        return;
    }

    if (run.StartScheduled)
        return;

    run.StartScheduled = true;
    run.Entrance = avatar.RegionLocation.Position;
    After(run.Region.Game, StartDelay, () => StartRun(run));
}

void StartRun(Run run)
{
    if (run.Ended)
        return;

    Region region = run.Region;
    run.StartTime = region.Game.CurrentTime;

    ScriptText.ShowBannerToRegion(region, "breach_start", run.TerminalIndex, "large", 4000);
    Announce(region, $"The breach into {Terminals[run.TerminalIndex].Name} is open! Destroy the Breach Anchors hidden across the map, " +
        $"then defeat the Breach Overlord. You have {FormatTime(TimeLimit)}. Type !breach for the distance to the nearest target.");

    SpawnAnchors(run);

    if (run.Targets.Count == 0)
    {
        Log.Warn($"No breach anchors could be placed in {region.PrototypeName}, going straight to the Breach Overlord");
        SpawnLord(run);
    }
    else
    {
        ScriptText.SetObjectiveTitle(region, "breach_obj_anchors");
        ScriptText.SetObjectiveCounter(region, 0, run.AnchorTotal);
    }

    // Visible countdown ("Time:" widget from the Danger Room HUD, the same family as the counter bar)
    if (ScriptPresentation.WidgetTimer(region, TimerWidget, TimeLimit) == false)
        Log.Warn("Could not show the breach timer widget");

    run.NextHintAt = HintInterval;
    After(region.Game, TickSeconds, () => Tick(run));
}

List<Vector3> PickSpreadSpots(Region region, Vector3 entrance, int count)
{
    var spots = new List<Vector3>();

    // Strict spacing first, then relax so small maps still get their anchors
    foreach (float scale in new[] { 1f, 0.6f, 0.3f, 0f })
    {
        for (int attempt = 0; attempt < count * 12 && spots.Count < count; attempt++)
        {
            if (ScriptSpawner.TryFindRandomSpot(region, out Vector3 spot, entrance, MinAnchorDistance * scale) == false)
                continue;

            float spacing = MinAnchorSpacing * scale;
            if (spots.Any(other => Vector3.Distance2D(other, spot) < spacing))
                continue;

            spots.Add(spot);
        }

        if (spots.Count >= count)
            break;
    }

    return spots;
}

void SpawnAnchors(Run run)
{
    Region region = run.Region;
    var bosses = AnchorPool.Where(boss => boss.Ref != PrototypeId.Invalid).OrderBy(_ => Random.Shared.Next()).ToList();
    if (bosses.Count == 0)
        return;

    List<Vector3> spots = PickSpreadSpots(region, run.Entrance, AnchorCount);
    for (int i = 0; i < spots.Count; i++)
    {
        Boss boss = bosses[i % bosses.Count];
        Agent agent = ScriptSpawner.SpawnHostile(region, boss.Ref, spots[i], 0f, 250f, null, BossesDropLoot, true, aggroed: false);
        if (agent == null)
            continue;

        ScriptSpawner.AddRandomAffixes(agent, AnchorAffixes, AffixExclude);
        run.Targets[agent.Id] = new Target { BossIndex = AllBosses.IndexOf(boss), Position = agent.RegionLocation.Position };
    }

    run.AnchorTotal = run.Targets.Count;
    Log.Info($"Breach in {region.PrototypeName}: {run.AnchorTotal} anchor(s) placed");
}

void SpawnLord(Run run)
{
    if (run.Ended || run.LordSpawned)
        return;

    run.LordSpawned = true;
    Region region = run.Region;

    var lords = FinalPool.Where(boss => boss.Ref != PrototypeId.Invalid).ToList();
    if (lords.Count == 0)
    {
        CompleteRun(run);
        return;
    }

    Boss lord = lords[Random.Shared.Next(lords.Count)];
    if (ScriptSpawner.TryFindFarSpot(region, run.Entrance, out Vector3 spot) == false)
        spot = run.Entrance;

    Agent agent = ScriptSpawner.SpawnHostile(region, lord.Ref, spot, 0f, 300f, null, BossesDropLoot, true, aggroed: false);
    if (agent == null)
    {
        Log.Warn($"Breach Overlord {lord.Name} could not be spawned, closing the breach");
        CompleteRun(run);
        return;
    }

    ScriptSpawner.AddRandomAffixes(agent, LordAffixes, AffixExclude);
    int bossIndex = AllBosses.IndexOf(lord);
    run.Targets[agent.Id] = new Target { BossIndex = bossIndex, Position = agent.RegionLocation.Position, IsLord = true };

    if (ShowLordHealth)
        ScriptPresentation.WidgetTrackHealth(region, LordHealthWidget, agent);

    ScriptText.ShowBannerToRegion(region, "breach_lord", bossIndex, "large", 4000);
    ScriptText.ShowPortraitNotificationToRegion(region, lord.Ref, "breach_arrive", bossIndex);
    ScriptText.SetObjectiveTitle(region, "breach_obj_lord");
    ScriptText.SetObjectiveCounter(region, 0, 1);
    Announce(region, $"All anchors are down! The Breach Overlord {lord.Name} waits at the far end of the terminal.");
    SendHints(run);
}

// Damage to breach bosses per player (pets, summons and team-ups count for their owner)
void AttachDamageTracking(Run run)
{
    run.DamageAction = (in AdjustHealthGameEvent evt) =>
    {
        try
        {
            if (IsLoaded == false || run.Ended || evt.Player == null || evt.Damage >= 0 || evt.Dodged)
                return;

            WorldEntity target = evt.Entity;
            if (target == null || run.Targets.ContainsKey(target.Id) == false)
                return;

            ulong dbId = evt.Player.DatabaseUniqueId;
            long damage = -evt.Damage;
            if (run.TargetDamage.TryGetValue(target.Id, out var byPlayer) == false)
                run.TargetDamage[target.Id] = byPlayer = new Dictionary<ulong, long>();
            byPlayer[dbId] = byPlayer.GetValueOrDefault(dbId) + damage;

            run.RunDamage[dbId] = run.RunDamage.GetValueOrDefault(dbId) + damage;
            run.PlayerNames[dbId] = evt.Player.GetName();
        }
        catch (Exception e)
        {
            Log.Error($"Damage tracking failed: {e.Message}");
        }
    };

    run.Region.AdjustHealthEvent.AddActionBack(run.DamageAction);
}

void DetachDamageTracking(Run run)
{
    if (run.DamageAction == null)
        return;

    run.Region.AdjustHealthEvent.RemoveAction(run.DamageAction);
    run.DamageAction = null;
}

void TargetDown(Run run, ulong entityId, bool killed)
{
    if (run.Targets.Remove(entityId, out Target target) == false)
        return;

    Region region = run.Region;
    run.TargetDamage.TryGetValue(entityId, out var damageByPlayer);
    damageByPlayer ??= new Dictionary<ulong, long>();
    run.TargetDamage.Remove(entityId);

    if (killed)
        AnnounceDefeat(run, target.BossIndex, damageByPlayer);

    if (target.IsLord)
    {
        CompleteRun(run);
        return;
    }

    run.AnchorsDown++;
    if (killed)
    {
        ScriptText.ShowBannerToRegion(region, "breach_anchor_down", 0, "reward", 2500);
        GiveRewards(run, damageByPlayer, AnchorRewards, "Breach Anchor");
    }

    ScriptText.SetObjectiveCounter(region, run.AnchorsDown, run.AnchorTotal);

    int left = run.Targets.Values.Count(t => t.IsLord == false);
    if (left == 0)
        After(region.Game, 3f, () => SpawnLord(run));
    else
        Announce(region, $"Breach Anchor destroyed! {left} left.");
}

void AnnounceDefeat(Run run, int bossIndex, Dictionary<ulong, long> damageByPlayer)
{
    long total = Math.Max(damageByPlayer.Values.Sum(), 1);
    var ranking = damageByPlayer.Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value).Take(3)
        .Select(pair => (DbId: pair.Key, Name: run.PlayerNames.GetValueOrDefault(pair.Key, "?"), Percent: pair.Value * 100 / total)).ToList();

    if (ranking.Count >= 2)
        ScriptText.ShowPlayerBannerToRegion(run.Region, $"breach_top2_{bossIndex}", 0, ranking[0].Name, ranking[1].Name, new[] { ranking[0].Percent, ranking[1].Percent });
    else if (ranking.Count == 1)
        ScriptText.ShowPlayerBannerToRegion(run.Region, $"breach_top_{bossIndex}", 0, ranking[0].Name, "", new[] { ranking[0].Percent });

    if (ranking.Count == 0)
        return;

    Announce(run.Region, $"{AllBosses[bossIndex].Name} defeated! Top damage: " +
        string.Join(", ", ranking.Select((entry, i) => $"{i + 1}. {entry.Name} ({entry.Percent}%)")));

    Avatar topAvatar = run.Region.Game.EntityManager.GetEntityByDbGuid<Player>(ranking[0].DbId)?.CurrentAvatar;
    if (topAvatar != null && topAvatar.Region == run.Region)
        ScriptText.ShowOverheadText(topAvatar, "breach_overhead", 0, OverheadSeconds);
}

void GiveRewards(Run run, Dictionary<ulong, long> damageByPlayer, Reward[] rewards, string what)
{
    if (rewards.Length == 0)
        return;

    foreach (Player player in PlayersIn(run.Region))
    {
        if (damageByPlayer.GetValueOrDefault(player.DatabaseUniqueId) <= 0)
            continue;

        int slot = 1;
        foreach (Reward reward in rewards)
        {
            if (ScriptRewards.DropItem(player, reward.Path, reward.Count, slot++) == false)
                Log.Warn($"Could not drop [{reward.Path}] for {player.GetName()}");
        }

        ScriptHooks.SendChatMessage(player, $"[Terminal Breach] {what} rewards have dropped for you.", false);
    }
}

void CompleteRun(Run run)
{
    if (run.Ended)
        return;

    GiveRewards(run, run.RunDamage, CompletionRewards, "Breach completion");

    float seconds = (float)(run.Region.Game.CurrentTime - run.StartTime).TotalSeconds;
    EndRun(run, true, $"The breach is closed! Cleared in {FormatTime(seconds)}.", true);
}

void EndRun(Run run, bool success, string message, bool announce)
{
    if (run.Ended)
        return;

    run.Ended = true;
    Region region = run.Region;
    DetachDamageTracking(run);
    ScriptPresentation.ClearWidget(region, TimerWidget);
    ScriptPresentation.ClearWidget(region, LordHealthWidget);   // before the Overlord despawns

    foreach (ulong entityId in run.Targets.Keys)
        ScriptSpawner.Despawn(region.Game, entityId);
    run.Targets.Clear();

    if (announce)
    {
        ScriptText.ClearObjective(region);
        ScriptText.ShowBannerToRegion(region, success ? "breach_complete" : "breach_failed", 0, success ? "rewardlarge" : "error", 5000);
        Announce(region, message);

        var top = run.RunDamage.OrderByDescending(pair => pair.Value).Take(3).ToList();
        if (top.Count > 0)
        {
            long total = Math.Max(run.RunDamage.Values.Sum(), 1);
            Announce(region, "Top damage: " + string.Join(", ", top.Select((pair, i) =>
                $"{i + 1}. {run.PlayerNames.GetValueOrDefault(pair.Key, "?")} ({pair.Value * 100 / total}%)")));
        }
    }

    Log.Info($"Breach into {Terminals[run.TerminalIndex].Name} opened by {run.OpenerName} ended: {message}");

    if (ReturnDelay > 0f && announce)
    {
        Announce(region, $"Returning to town in {FormatTime(ReturnDelay)}.");
        After(region.Game, ReturnDelay, () =>
        {
            foreach (Player player in PlayersIn(region).ToList())
                ScriptTeleport.ToTown(player);
            runs.TryRemove(region, out _);
        });
    }
    else
    {
        runs.TryRemove(region, out _);
    }
}

void Tick(Run run)
{
    if (run.Ended)
        return;

    Region region = run.Region;

    // Targets that vanished without dying still count, so the breach can never get stuck
    foreach (ulong entityId in run.Targets.Keys.ToList())
    {
        if (run.Ended) return;
        if (ScriptSpawner.IsAlive(region.Game, entityId) == false)
            TargetDown(run, entityId, false);
    }

    if (run.Ended)
        return;

    run.EmptySeconds = PlayersIn(region).Any() ? 0f : run.EmptySeconds + TickSeconds;
    if (run.EmptySeconds >= EmptyRegionTimeout)
    {
        EndRun(run, false, "Everyone left the breach.", false);
        return;
    }

    float elapsed = (float)(region.Game.CurrentTime - run.StartTime).TotalSeconds;
    float left = TimeLimit - elapsed;
    if (left <= 0f)
    {
        EndRun(run, false, "Time ran out and the breach collapsed.", true);
        return;
    }

    foreach (float warning in new[] { 300f, 60f })
    {
        if (left <= warning && run.WarningsGiven.Add(warning))
            Announce(region, $"{FormatTime(warning)} until the breach collapses!");
    }

    if (elapsed >= run.NextHintAt)
    {
        run.NextHintAt = elapsed + HintInterval;
        SendHints(run);
    }

    After(region.Game, TickSeconds, () => Tick(run));
}

// Distance from a player to the closest remaining breach target (world units / 100 = meters)
string NearestHint(Run run, Avatar avatar)
{
    if (run.Targets.Count == 0)
        return "No breach targets left.";

    Vector3 here = avatar.RegionLocation.Position;
    Target nearest = run.Targets.Values.OrderBy(target => Vector3.Distance2D(here, target.Position)).First();
    int meters = (int)(Vector3.Distance2D(here, nearest.Position) / 100f);
    string what = nearest.IsLord ? $"The Breach Overlord {AllBosses[nearest.BossIndex].Name}" : $"Nearest Breach Anchor ({AllBosses[nearest.BossIndex].Name})";
    return $"{what}: about {meters} m away.";
}

void SendHints(Run run)
{
    foreach (Player player in PlayersIn(run.Region))
        ScriptHooks.SendChatMessage(player, "[Terminal Breach] " + NearestHint(run, player.CurrentAvatar), false);
}

//------------------------------------------------------------------------------
// Hooks
//------------------------------------------------------------------------------

Hooks.On(ScriptHooks.EntityInteracted, e =>
{
    if (e.ScriptTag != NpcTag)
        return;

    ScriptDialog.Show(e.Player, "breach_prompt", "breach_enter", "breach_cancel", (player, button) =>
    {
        if (button == 1 && player.GetRegion()?.PrototypeDataRef == hubRef)
            OpenBreach(player);
    }, e.Entity);
});

Hooks.On(ScriptHooks.RegionGenerating, e =>
{
    if (pendingBySeed.TryGetValue(e.Seed, out Run run) == false || Terminals[run.TerminalIndex].Ref != e.RegionPrototype.DataRef)
        return;

    int roomCut = Random.Shared.Next(RoomCutMin, RoomCutMax + 1);
    int connectionCut = Random.Shared.Next(ConnectionCutMin, ConnectionCutMax + 1);
    e.SetRoomRemovalChance(roomCut);
    e.SetConnectionRemovalChance(connectionCut);
    Log.Info($"Breach layout for {Terminals[run.TerminalIndex].Name}: seed {e.Seed}, {roomCut}% rooms cut, {connectionCut}% connections closed");
});

Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    Region region = e.Region;

    if (region.PrototypeDataRef == hubRef)
    {
        EnsureHubNpc(region);
        return;
    }

    if (runs.ContainsKey(region))
        return;

    Run run = FindRunFor(e.Player, region);
    if (run != null)
        BindRun(run, region, e.Player);
});

Hooks.On(ScriptHooks.EntityKilled, e =>
{
    if (runs.IsEmpty || e.VictimIsAvatar)
        return;

    Region region = e.Victim.Region;
    if (region != null && runs.TryGetValue(region, out Run run) && run.Ended == false && run.Targets.ContainsKey(e.VictimId))
        TargetDown(run, e.VictimId, true);
});

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "breach")
        return;

    e.Handled = true;
    Avatar avatar = e.Player.CurrentAvatar;
    Region region = avatar?.Region;
    if (region == null)
        return;

    string sub = e.GetArg(0).ToLowerInvariant();

    if (sub == "open")
    {
        if (IsAdmin(e.Player) == false) { e.Reply("Admin only. Talk to the S.H.I.E.L.D. agent in Avengers Tower."); return; }
        OpenBreach(e.Player);
        return;
    }

    if (sub == "npc")
    {
        if (IsAdmin(e.Player) == false) { e.Reply("Admin only."); return; }
        if (region.PrototypeDataRef != hubRef) { e.Reply("Go to Avengers Tower first."); return; }

        Vector3 here = avatar.RegionLocation.Position;
        float yaw = MathF.Atan2(avatar.Forward.Y, avatar.Forward.X) * 180f / MathF.PI + 180f;   // facing you
        Vector3 spot = here + avatar.Forward * 120f;
        EnsureHubNpc(region, spot, yaw);
        e.Reply($"Agent moved (until restart). To keep it there, set in terminal_breach.csx:");
        e.Reply($"Vector3? NpcPosition = new Vector3({spot.X:0}f, {spot.Y:0}f, {spot.Z:0}f); float NpcYawDegrees = {yaw:0}f;");
        return;
    }

    if (runs.TryGetValue(region, out Run run) && run.Ended == false)
    {
        if (run.StartTime == TimeSpan.Zero)
        {
            e.Reply("The breach is opening...");
            return;
        }

        float left = TimeLimit - (float)(region.Game.CurrentTime - run.StartTime).TotalSeconds;
        string stage = run.LordSpawned ? "Defeat the Breach Overlord" : $"Breach Anchors {run.AnchorsDown} / {run.AnchorTotal}";
        e.Reply($"{Terminals[run.TerminalIndex].Name}: {stage}, {FormatTime(left)} left. {NearestHint(run, avatar)}");
    }
    else
    {
        e.Reply("No breach here. Talk to the S.H.I.E.L.D. agent in Avengers Tower to open one.");
    }
});

Log.Info($"Terminal Breach loaded ({Terminals.Count(t => t.Ref != PrototypeId.Invalid)} terminals, {AllBosses.Count(b => b.Ref != PrototypeId.Invalid)} bosses)");

//------------------------------------------------------------------------------
// Types
//------------------------------------------------------------------------------

class Terminal
{
    public string Name { get; }
    public string Path { get; }
    public PrototypeId Ref { get; set; } = PrototypeId.Invalid;

    public Terminal(string name, string path)
    {
        Name = name;
        Path = path;
    }
}

class Boss
{
    public string Name { get; }
    public string Match { get; }
    public PrototypeId Ref { get; set; } = PrototypeId.Invalid;

    public Boss(string name, string match)
    {
        Name = name;
        Match = match;
    }
}

class Reward
{
    public string Path { get; }
    public int Count { get; }

    public Reward(string path, int count = 1)
    {
        Path = path;
        Count = count;
    }
}

class Target
{
    public int BossIndex;
    public Vector3 Position;
    public bool IsLord;
}

class Run
{
    public int Seed;
    public int TerminalIndex;
    public string OpenerName;
    public DateTime CreatedAt;

    public Region Region;
    public Vector3 Entrance;
    public bool StartScheduled;
    public TimeSpan StartTime;
    public bool Ended;
    public bool LordSpawned;
    public int AnchorTotal;
    public int AnchorsDown;
    public float EmptySeconds;
    public float NextHintAt;
    public HashSet<float> WarningsGiven = new();

    public Dictionary<ulong, Target> Targets = new();                        // boss entity id => target
    public Dictionary<ulong, long> RunDamage = new();                        // player db id => damage to breach bosses
    public Dictionary<ulong, string> PlayerNames = new();
    public Dictionary<ulong, Dictionary<ulong, long>> TargetDamage = new();  // boss entity id => player db id => damage
    public Event<AdjustHealthGameEvent>.Action DamageAction;
}
