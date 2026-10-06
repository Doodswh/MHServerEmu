// Age of MOO: a raid-style event in the spirit of Age of Ultron. The "cows" were Skrulls all along. A S.H.I.E.L.D. agent in
// Avengers Tower sends parties into a fresh Cosmic terminal overrun by High Commander Brevik's herd.
//
//  1. Click the agent in the hub (a different agent from the Terminal Breach one). "Enter" sends you and your party members
//     standing in the hub into a brand-new instance of a random terminal from EventRegions.
//  2. The Herd: HerdWaves waves of Extremely Normal Cows stampede in around the party. Clear every wave.
//  3. Skrull Commanders: CommanderCount "King Cows" (Skrull commanders in disguise) hold spots spread across the map, each with
//     a few cow bodyguards. Find and defeat them (!moo shows the distance to the nearest one; hints also arrive in chat).
//  4. High Commander Brevik arrives. At 66% and 33% health he calls in more of the herd. When he falls he rises again as
//     All-Father Brevik for the final fight.
//  5. Rewards: PhaseRewards for everyone who damaged the herd in a phase, CompletionRewards when All-Father Brevik falls. After
//     ReturnDelay everyone is sent back to town. The herd wins if TimeLimit runs out.
//
// The terminal's own enemies and boss are still there.
//
//   !moo               status, time left and distance to the nearest target
//   !moo open          (admin) start without the NPC
//   !moo npc here      (admin) move the hub agent to you and print the position to paste into NpcPosition
//
// NOTE: on-screen text registered here reaches players the next time they connect (client limitation).

using System.Collections.Concurrent;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Events;

//------------------------------------------------------------------------------
// Settings
//------------------------------------------------------------------------------

const string HubRegion     = "Regions/HUBRevamp/NPEAvengersTowerHUBRegion.prototype";   // the current Avengers Tower
const string NpcPath       = "Entity/Characters/NPCs/HubNPCs/SHIELDAgentFemale.prototype";
Vector3? NpcPosition       = null;   // null = near where players arrive in the hub; use "!moo npc here" to pick a spot
float NpcYawDegrees        = 0f;     // only used with NpcPosition

// Where the herd strikes: one is picked at random for each run (fresh private instance every time)
const string Terminals = "Regions/EndGame/Terminals/Cosmic/";
const string EventDifficulty = "Difficulty/Tiers/Tier3Superheroic.prototype";   // Cosmic, whatever the party / player difficulty is
var EventRegions = new List<Place>
{
    new("A.I.M. Facility",     Terminals + "AIMFacility/DailyCAIMFacilityRegion.prototype"),
    new("Hood's Ship",         Terminals + "HoodsShip/DailyCHoodsShipRegion.prototype"),
    new("HYDRA Island",        Terminals + "HYDRAIsland/DailyCHYDRAIslandRegion.prototype"),
    new("Kingpin's Warehouse", Terminals + "KingpinsWarehouse/DailyCKingpinRegion.prototype"),
    new("Stryker's Bunker",    Terminals + "MagnetoBunker/DailyCStrykerBunkerRegion.prototype"),
    new("Shocker's Subway",    Terminals + "ShockerSubway/DailyCShockerSubwayRegion.prototype"),
    new("Sinister's Lab",      Terminals + "SinistersLab/DailyCSinisterLabRegion.prototype"),
    new("Taskmaster's Base",   Terminals + "Taskmaster/DailyCTaskmasterRegion.prototype"),
    new("Castle Doom",         Terminals + "CastleDoom/DailyCDoomCastleRegion.prototype"),
};

// The Herd: cows per wave (plus CowsPerExtraPlayer for each player after the first) and elite cows per wave
var HerdWaves = new (int Cows, int Elites)[] { (8, 0), (10, 1), (12, 2) };
const int   CowsPerExtraPlayer = 2;
const float WaveSpawnMin       = 500f;
const float WaveSpawnMax       = 1100f;
const float NextWaveDelay      = 6f;

// Skrull Commanders
const int   CommanderCount     = 3;
const int   CommanderAffixes   = 2;
const int   CommanderGuards    = 3;       // cows guarding each commander
const float MinCommanderDistance = 1500f; // from where the party started
const float MinCommanderSpacing  = 1000f;

// Brevik
const int   BrevikAffixes      = 3;
var BrevikReinforceAt          = new[] { 0.66f, 0.33f };   // health fractions that call in the herd
const int   ReinforceCows      = 6;
const int   ReinforceElites    = 1;
const float SecondFormDelay    = 3f;      // seconds between High Commander Brevik falling and All-Father Brevik rising

const float TimeLimit          = 1200f;
const string TimerWidget       = "UI/MetaGame/DangerRoom/DangerRoomTimer.prototype";   // on-screen "Time:" countdown
const bool   ShowBossHealth    = true;   // HUD health bar (boss icon + health %) for High Commander / All-Father Brevik
const string BossHealthWidget  = "UI/MetaGame/SurturRaid/FiveMan/SlagHealth.prototype";
const float StartDelay         = 6f;
const float LoadInTimeout      = 90f;
const float PendingTimeout     = 180f;
const float ReturnDelay        = 60f;     // 0 = stay
const float EmptyRegionTimeout = 120f;
const float HintInterval       = 90f;
const float TickSeconds        = 2f;
const float OverheadSeconds    = 5f;
const int   TauntMs            = 6000;    // how long Brevik's portrait taunts stay up
const string AffixExclude      = "";

// Fastest clear leaderboard: the game's Trainyard "Time Trial - Cosmic" board (weekly reset, shown as a time, fastest first, 6 reward
// tiers), taken over (only Age of MOO counts), renamed and fed with the clear time of everyone who fought. Only each player's best
// time counts. "" = off. Needs the board enabled in Data/Leaderboards/LeaderboardSchedule.json. The new name shows after reconnecting.
const string ClearTimeLeaderboard = "Leaderboards/Prototypes/Leaderboards/DangerRoom/DRScenarioTimeTrainyardCosmic.prototype";

if (ClearTimeLeaderboard.Length > 0)
{
    ScriptLeaderboards.TakeOver(ClearTimeLeaderboard);
    ScriptLeaderboards.Rename(ClearTimeLeaderboard, "Age of MOO - Fastest Clear",
        "Fastest Age of MOO clears this week.",
        "Start the Age of MOO with the S.H.I.E.L.D. agent in Avengers Tower: break the stampede, unmask the Skrull Commanders and beat " +
        "Brevik in both his forms as fast as you can. Your best time this week counts. Resets weekly.");
}

const string Cows  = "Entity/Characters/Mobs/CowsEG/";
const string Cows2 = "Entity/Characters/Mobs/CowsEG2/";
var HerdCows = new List<Foe>
{
    new("Extremely Normal Cow", Cows  + "SpearCow.prototype"),
    new("Very Typical Cow",     Cows  + "RangedCow.prototype"),
    new("Extremely Normal Cow", Cows  + "SpearCowD1.prototype"),
    new("Very Typical Cow",     Cows  + "RangedCowD1.prototype"),
    new("Perfectly Normal Cow", Cows2 + "SpearCowEG2D1.prototype"),
    new("Plain Old Cow",        Cows2 + "RangedCowEG2D1.prototype"),
};
var HerdElites = new List<Foe>
{
    new("Even More Normal Cow",    Cows + "SpearKingCowD1.prototype"),
    new("Unbelievably Typical Cow", Cows + "RangedKingCowD1.prototype"),
};
var Commanders = new List<Foe>
{
    new("Protective Skrull Commander", Cows  + "SpearKingCow1.prototype"),
    new("Aggressive Skrull Commander", Cows  + "SpearKingCow2.prototype"),
    new("Tenacious Skrull Commander",  Cows  + "RangedKingCow1.prototype"),
    new("Dangerous Skrull Commander",  Cows  + "RangedKingCow2.prototype"),
    new("Very Cold Skrull Cmdr.",      Cows2 + "SpearKingCow3EG2.prototype"),
    new("Very Bloodthirsty Skrull Cmdr.", Cows2 + "RangedKingCow3EG2.prototype"),
};
var Brevik     = new Foe("High Commander Brevik", Cows  + "SpearCowPresidentandCEO.prototype");
var AllFather  = new Foe("All-Father Brevik",     Cows2 + "SpearCowPresidentandCEOEG2.prototype");

//------------------------------------------------------------------------------
// Rewards (dropped at the feet of each player who damaged the herd, one copy each)
//------------------------------------------------------------------------------

const string FC = "Entity/Items/Consumables/Prototypes/FortuneCard/";

var PhaseRewards = new Dictionary<string, Reward[]>
{
    ["Herd"]       = new Reward[] { new(FC + "SpiderManHomecomingFortuneCard.prototype"), new(FC + "LoganFortuneCard.prototype") },
    ["Commanders"] = new Reward[] { new("Entity/Items/Consumables/Prototypes/CSGrant/CSGrantCrateHeroCommendation25Box.prototype") },
};

var CompletionRewards = new Reward[]
{
    new("Entity/Items/Gems/Gem1.prototype", 1),   // StarkTech Power Cube (only here, at the end)
    new("Entity/Items/Consumables/Prototypes/RandomGiftboxes/RandomCosmicArtifactBox.prototype"),
    new("Entity/Items/Consumables/Prototypes/DailyGift/LargeRunebox.prototype"),
    new(FC + "SpiderManHomecomingFortuneCard.prototype", 2),
    new(FC + "LoganFortuneCard.prototype"),
};

//------------------------------------------------------------------------------
// On-screen text
//------------------------------------------------------------------------------

ScriptText.Register("aom_prompt", "Begin the Age of MOO?\nYou and your party members here head into a terminal overrun by a herd of " +
    "perfectly normal cows. They are definitely not Skrulls. Stop the stampede, unmask the Skrull Commanders, then face Brevik.");
ScriptText.Register("aom_enter", "Moo-ve out");
ScriptText.Register("aom_cancel", "Not now");
ScriptText.Register("aom_commanders", "Unmask the Skrull Commanders!");
ScriptText.Register("aom_commander_down", "Skrull Commander Defeated!");
ScriptText.Register("aom_brevik_arrives", "High Commander Brevik Has Arrived!");
ScriptText.Register("aom_allfather", "All-Father Brevik Rises!");
ScriptText.Register("aom_reinforce", "Brevik Calls the Herd!");
ScriptText.Register("aom_complete", "The Herd Is Broken!");
ScriptText.Register("aom_failed", "The Herd Wins. Moo.");
ScriptText.Register("aom_obj_commanders", "Age of MOO - Unmask the Commanders");
ScriptText.Register("aom_obj_brevik", "Age of MOO - Defeat Brevik");
ScriptText.Register("aom_overhead", "Top Damage!");
ScriptText.Register("aom_top", "All-Father Brevik Defeated! Top damage: $playersource$ ($intargzero$%)");
ScriptText.Register("aom_top2", "All-Father Brevik Defeated! 1st: $playersource$ ($intargzero$%)  2nd: $playertarget$ ($intargone$%)");
ScriptText.RegisterRange("aom_wave", "The Herd - Wave {0}", 1, HerdWaves.Length);
ScriptText.RegisterRange("aom_obj_wave", "Age of MOO - Stampede {0} / " + HerdWaves.Length, 1, HerdWaves.Length);
ScriptText.RegisterRange("aom_wave_clear", "Stampede {0} Stopped", 1, HerdWaves.Length);

for (int i = 0; i < EventRegions.Count; i++)
    ScriptText.RegisterRange("aom_start", $"Age of MOO: {EventRegions[i].Name}", i, i);

// Brevik's portrait taunts (story notification popup)
ScriptText.Register("aom_taunt_start", "Moo. MOO. ...Ahem. Heroes of Earth, you face the herd of High Commander Brevik. Surrender your pastures.");
ScriptText.Register("aom_taunt_commanders", "You have scattered my herd. My commanders will not be so easily... milked.");
ScriptText.Register("aom_taunt_arrive", "Enough! I will deal with you myself. Moo-hahaha!");
ScriptText.Register("aom_taunt_reinforce", "To me, my herd! Trample them!");
ScriptText.Register("aom_taunt_allfather", "You thought a High Commander could fall so easily? Behold... the ALL-FATHER!");
ScriptText.Register("aom_taunt_win", "This is not over, heroes. The herd... remembers. Moo.");

//------------------------------------------------------------------------------
// State. A run is created in the hub's game and then only touched on the event instance's game thread.
//------------------------------------------------------------------------------

var pendingBySeed   = new ConcurrentDictionary<int, Run>();
var pendingByPlayer = new ConcurrentDictionary<ulong, Run>();
var runs            = new ConcurrentDictionary<Region, Run>();
var hubNpcs         = new ConcurrentDictionary<Region, ulong>();

PrototypeId hubRef = GameDatabase.GetPrototypeRefByName(HubRegion);
PrototypeId npcRef = GameDatabase.GetPrototypeRefByName(NpcPath);
const string NpcTag = "age_of_moo";

foreach (Place place in EventRegions)
{
    place.Ref = GameDatabase.GetPrototypeRefByName(place.Path);
    if (place.Ref == PrototypeId.Invalid)
        Log.Warn($"Event region not found: {place.Path}");
}

foreach (Foe foe in HerdCows.Concat(HerdElites).Concat(Commanders).Append(Brevik).Append(AllFather))
{
    foe.Ref = ScriptSpawner.FindAgent(foe.Path);
    if (foe.Ref == PrototypeId.Invalid)
        Log.Warn($"Enemy not found: {foe.Path}");
}

if (hubRef == PrototypeId.Invalid) Log.Warn($"Hub region not found: {HubRegion}");
if (npcRef == PrototypeId.Invalid) Log.Warn($"NPC not found: {NpcPath}");

bool IsAdmin(Player player) => ScriptHooks.IsAdmin(player);   // Admin or Dev accounts

bool IsEventRegion(Region region) => EventRegions.Any(place => place.Ref == region.PrototypeDataRef);

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
        ScriptHooks.SendChatMessage(player, "[Age of MOO] " + text, false);
}

// Brevik's portrait in the corner with a line of text
void Taunt(Region region, string key, Foe speaker = null)
{
    ScriptPresentation.StoryNotificationToRegion(region, key, (speaker ?? Brevik).Path, TauntMs);
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

    hubNpcs.TryRemove(hub, out _);

    // Remove every copy of the NPC in this hub, not just the one this script instance spawned: after a script reload
    // the old instance's NPC is still standing there
    foreach (WorldEntity old in hub.Entities.OfType<WorldEntity>().Where(entity => ScriptSpawner.GetTag(entity) == NpcTag).ToList())
        ScriptSpawner.Despawn(hub.Game, old.Id);

    Vector3 position;
    float yaw;

    if (positionOverride.HasValue || NpcPosition.HasValue)
    {
        position = positionOverride ?? NpcPosition.Value;
        yaw = yawOverride ?? NpcYawDegrees;
    }
    else
    {
        // A bit further out than the Terminal Breach agent so they don't stand on each other
        if (ScriptSpawner.TryGetStartPosition(hub, out Vector3 start) == false ||
            ScriptSpawner.TryFindSpotNear(hub, start, 550f, 850f, out position) == false)
        {
            Log.Warn("Could not find a spot for the Age of MOO agent in the hub; set NpcPosition (use !moo npc here)");
            return;
        }

        yaw = MathF.Atan2(start.Y - position.Y, start.X - position.X) * 180f / MathF.PI;
    }

    WorldEntity npc = ScriptSpawner.SpawnInteractable(hub, npcRef, position, yaw, NpcTag);
    if (npc != null)
    {
        hubNpcs[hub] = npc.Id;
        Log.Info($"Age of MOO agent placed in the hub at {position}");
    }
}

//------------------------------------------------------------------------------
// Entering
//------------------------------------------------------------------------------

void ForgetExpiredPending()
{
    DateTime now = DateTime.UtcNow;
    foreach (var pair in pendingBySeed)
        if ((now - pair.Value.CreatedAt).TotalSeconds > PendingTimeout) pendingBySeed.TryRemove(pair.Key, out _);
    foreach (var pair in pendingByPlayer)
        if ((now - pair.Value.CreatedAt).TotalSeconds > PendingTimeout) pendingByPlayer.TryRemove(pair.Key, out _);
}

void OpenEvent(Player opener)
{
    ForgetExpiredPending();

    var available = EventRegions.Where(place => place.Ref != PrototypeId.Invalid).ToList();
    if (available.Count == 0)
    {
        ScriptHooks.SendChatMessage(opener, "[Age of MOO] No event regions are available.", false);
        return;
    }

    int placeIndex = EventRegions.IndexOf(available[Random.Shared.Next(available.Count)]);
    Place place = EventRegions[placeIndex];

    var run = new Run { Seed = Random.Shared.Next(1, int.MaxValue), PlaceIndex = placeIndex, OpenerName = opener.GetName(), CreatedAt = DateTime.UtcNow };

    List<Player> group = ScriptTeleport.GetPartyMembersInRegion(opener);
    pendingBySeed[run.Seed] = run;
    foreach (Player member in group)
        pendingByPlayer[member.DatabaseUniqueId] = run;

    int sent = 0;
    foreach (Player member in group)
    {
        if (ScriptTeleport.ToRegion(member, place.Path, EventDifficulty, run.Seed))
        {
            sent++;
            ScriptHooks.SendChatMessage(member, $"[Age of MOO] {opener.GetName()} leads the charge into {place.Name}. Something smells like a farm.", false);
        }
    }

    if (sent == 0)
    {
        pendingBySeed.TryRemove(run.Seed, out _);
        ScriptHooks.SendChatMessage(opener, "[Age of MOO] Could not get there (see the server log; level 60 is required).", false);
        return;
    }

    Log.Info($"{opener.GetName()} started an Age of MOO in {place.Name} for {sent} player(s), seed {run.Seed}");
}

Run FindRunFor(Player player, Region region)
{
    if (IsEventRegion(region) == false)
        return null;

    if (runs.TryGetValue(region, out Run running))
        return running;

    if (pendingBySeed.TryGetValue(region.RandomSeed, out Run run) && EventRegions[run.PlaceIndex].Ref == region.PrototypeDataRef)
        return run;

    // Fallback (the server retried generation with another seed): the event this player was just sent to
    if (pendingByPlayer.TryGetValue(player.DatabaseUniqueId, out run) && run.Region == null &&
        EventRegions[run.PlaceIndex].Ref == region.PrototypeDataRef && (DateTime.UtcNow - run.CreatedAt).TotalSeconds <= PendingTimeout)
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

//------------------------------------------------------------------------------
// Phases
//------------------------------------------------------------------------------

void StartRun(Run run)
{
    if (run.Ended)
        return;

    Region region = run.Region;
    run.StartTime = region.Game.CurrentTime;

    ScriptText.ShowBannerToRegion(region, "aom_start", run.PlaceIndex, "large", 3500);
    Taunt(region, "aom_taunt_start");
    Announce(region, $"A herd of perfectly normal cows has overrun {EventRegions[run.PlaceIndex].Name}. Stop {HerdWaves.Length} stampedes, " +
        $"unmask the Skrull Commanders, then face Brevik. You have {FormatTime(TimeLimit)}. Type !moo for your objective.");

    if (ScriptPresentation.WidgetTimer(region, TimerWidget, TimeLimit) == false)
        Log.Warn("Could not show the Age of MOO timer widget");

    run.NextHintAt = HintInterval;
    After(region.Game, 4f, () => StartWave(run, 0));
    After(region.Game, TickSeconds, () => Tick(run));
}

Avatar PickAnchor(Run run)
{
    var alive = PlayersIn(run.Region).Select(player => player.CurrentAvatar).Where(avatar => avatar.IsDead == false).ToList();
    return alive.Count > 0 ? alive[Random.Shared.Next(alive.Count)] : null;
}

// Spawns count foes around an avatar, all charging it. Returns how many spawned.
int SpawnAround(Run run, Avatar anchor, List<Foe> pool, int count, int affixes, TargetKind kind)
{
    var usable = pool.Where(foe => foe.Ref != PrototypeId.Invalid).ToList();
    if (usable.Count == 0 || anchor == null)
        return 0;

    int spawned = 0;
    for (int i = 0; i < count; i++)
    {
        Foe foe = usable[Random.Shared.Next(usable.Count)];
        Agent agent = ScriptSpawner.SpawnHostile(run.Region, foe.Ref, anchor.RegionLocation.Position, WaveSpawnMin, WaveSpawnMax, anchor, true, true);
        if (agent == null)
            continue;

        if (affixes > 0)
            ScriptSpawner.AddRandomAffixes(agent, affixes, AffixExclude);

        run.Targets[agent.Id] = new Target { Kind = kind, Name = foe.Name, Position = agent.RegionLocation.Position };
        spawned++;
    }

    return spawned;
}

void StartWave(Run run, int waveIndex)
{
    if (run.Ended)
        return;

    Region region = run.Region;
    run.Phase = "Herd";
    run.WaveIndex = waveIndex;
    int number = waveIndex + 1;

    Avatar anchor = PickAnchor(run);
    if (anchor == null)
    {
        // Everyone is dead or loading: try again shortly
        After(region.Game, 3f, () => StartWave(run, waveIndex));
        return;
    }

    int players = PlayersIn(region).Count();
    var (cows, elites) = HerdWaves[waveIndex];
    cows += CowsPerExtraPlayer * Math.Max(players - 1, 0);

    int total = SpawnAround(run, anchor, HerdCows, cows, 0, TargetKind.Herd)
              + SpawnAround(run, anchor, HerdElites, elites, 1, TargetKind.Herd);

    run.CounterTotal = total;
    run.CounterDone = 0;

    if (total == 0)
    {
        Log.Warn("Age of MOO: no cows could be spawned, skipping to the commanders");
        StartCommanders(run);
        return;
    }

    ScriptText.ShowBannerToRegion(region, "aom_wave", number, "large", 3000);
    ScriptText.SetObjectiveTitle(region, "aom_obj_wave", number);
    ScriptText.SetObjectiveCounter(region, 0, total);
    Announce(region, $"Stampede {number}: {total} perfectly normal cows incoming! MOO!");
}

void HerdWaveCleared(Run run)
{
    Region region = run.Region;
    int number = run.WaveIndex + 1;
    ScriptText.ShowBannerToRegion(region, "aom_wave_clear", number, "reward", 2500);

    if (number < HerdWaves.Length)
    {
        int next = run.WaveIndex + 1;
        After(region.Game, NextWaveDelay, () => StartWave(run, next));
        return;
    }

    GivePhaseRewards(run, "Herd");
    After(region.Game, NextWaveDelay, () => StartCommanders(run));
}

List<Vector3> PickSpreadSpots(Region region, Vector3 entrance, int count)
{
    var spots = new List<Vector3>();
    foreach (float scale in new[] { 1f, 0.6f, 0.3f, 0f })
    {
        for (int attempt = 0; attempt < count * 12 && spots.Count < count; attempt++)
        {
            if (ScriptSpawner.TryFindRandomSpot(region, out Vector3 spot, entrance, MinCommanderDistance * scale) == false)
                continue;

            if (spots.Any(other => Vector3.Distance2D(other, spot) < MinCommanderSpacing * scale))
                continue;

            spots.Add(spot);
        }

        if (spots.Count >= count)
            break;
    }

    return spots;
}

void StartCommanders(Run run)
{
    if (run.Ended)
        return;

    Region region = run.Region;
    run.Phase = "Commanders";
    run.PhaseDamage.Clear();

    var commanders = Commanders.Where(foe => foe.Ref != PrototypeId.Invalid).OrderBy(_ => Random.Shared.Next()).ToList();
    int placed = 0;

    if (commanders.Count > 0)
    {
        List<Vector3> spots = PickSpreadSpots(region, run.Entrance, CommanderCount);
        for (int i = 0; i < spots.Count; i++)
        {
            Foe foe = commanders[i % commanders.Count];
            Agent commander = ScriptSpawner.SpawnHostile(region, foe.Ref, spots[i], 0f, 250f, null, true, true, aggroed: false);
            if (commander == null)
                continue;

            ScriptSpawner.AddRandomAffixes(commander, CommanderAffixes, AffixExclude);
            run.Targets[commander.Id] = new Target { Kind = TargetKind.Commander, Name = foe.Name, Position = commander.RegionLocation.Position };
            placed++;

            // Bodyguards stay with their commander (not counted, but they drop loot and count for damage)
            foreach (Foe guard in HerdCows.Where(cow => cow.Ref != PrototypeId.Invalid).OrderBy(_ => Random.Shared.Next()).Take(CommanderGuards))
            {
                Agent agent = ScriptSpawner.SpawnHostile(region, guard.Ref, spots[i], 100f, 400f, null, true, true, aggroed: false);
                if (agent != null)
                    run.Guards.Add(agent.Id);
            }
        }
    }

    run.CounterTotal = placed;
    run.CounterDone = 0;

    if (placed == 0)
    {
        Log.Warn("Age of MOO: no commanders could be placed, Brevik arrives early");
        StartBrevik(run);
        return;
    }

    ScriptText.ShowBannerToRegion(region, "aom_commanders", 0, "large", 3000);
    ScriptText.SetObjectiveTitle(region, "aom_obj_commanders");
    ScriptText.SetObjectiveCounter(region, 0, placed);
    Taunt(region, "aom_taunt_commanders");
    Announce(region, $"The stampede is over! {placed} Skrull Commanders, badly disguised as King Cows, hold the map. Unmask them.");
    SendHints(run);
}

// High Commander Brevik, then All-Father Brevik (secondForm)
void StartBrevik(Run run, bool secondForm = false, Vector3? at = null)
{
    if (run.Ended)
        return;

    string phase = secondForm ? "AllFather" : "Brevik";
    if (run.Phase == phase)
        return;

    Region region = run.Region;
    run.Phase = phase;
    if (secondForm == false)
        run.PhaseDamage.Clear();

    Foe foe = secondForm ? AllFather : Brevik;
    Avatar anchor = PickAnchor(run);
    Agent boss = null;

    if (foe.Ref != PrototypeId.Invalid)
    {
        if (at.HasValue)
            boss = ScriptSpawner.SpawnHostile(region, foe.Ref, at.Value, 0f, 200f, anchor, true, true);
        else if (anchor != null)
            boss = ScriptSpawner.SpawnHostile(region, foe.Ref, anchor.RegionLocation.Position, 600f, 1000f, anchor, true, true);
    }

    if (boss == null)
    {
        if (anchor == null && at.HasValue == false)
        {
            run.Phase = "Commanders";
            After(region.Game, 3f, () => StartBrevik(run, secondForm, at));
            return;
        }

        if (secondForm)
        {
            Log.Warn("Age of MOO: All-Father Brevik could not be spawned, ending the event as a win");
            CompleteRun(run);
        }
        else
        {
            Log.Warn("Age of MOO: High Commander Brevik could not be spawned, going straight to the All-Father");
            StartBrevik(run, true, at);
        }
        return;
    }

    ScriptSpawner.AddRandomAffixes(boss, BrevikAffixes, AffixExclude);
    run.BossId = boss.Id;
    run.ReinforcementsCalled.Clear();
    run.Targets[boss.Id] = new Target { Kind = TargetKind.Boss, Name = foe.Name, Position = boss.RegionLocation.Position, IsFinal = secondForm };

    if (ShowBossHealth)
        ScriptPresentation.WidgetTrackHealth(region, BossHealthWidget, boss);

    ScriptText.ShowBannerToRegion(region, secondForm ? "aom_allfather" : "aom_brevik_arrives", 0, "large", 4000);
    Taunt(region, secondForm ? "aom_taunt_allfather" : "aom_taunt_arrive", foe);
    ScriptText.SetObjectiveTitle(region, "aom_obj_brevik");
    ScriptText.SetObjectiveCounter(region, secondForm ? 1 : 0, 2);
    Announce(region, secondForm
        ? "Brevik isn't done! ALL-FATHER BREVIK rises for the final stand!"
        : "The commanders are down! High Commander Brevik himself charges in.");
}

void CheckBossHealth(Run run)
{
    if ((run.Phase != "Brevik" && run.Phase != "AllFather") || run.BossId == 0)
        return;

    Agent boss = run.Region.Game.EntityManager.GetEntity<Agent>(run.BossId);
    if (boss == null || boss.IsAliveInWorld == false)
        return;

    long health = boss.Properties[PropertyEnum.Health];
    long healthMax = Math.Max((long)boss.Properties[PropertyEnum.HealthMax], 1L);
    float fraction = (float)health / healthMax;

    foreach (float threshold in BrevikReinforceAt)
    {
        if (fraction > threshold || run.ReinforcementsCalled.Add(threshold) == false)
            continue;

        Avatar anchor = PickAnchor(run);
        int spawned = SpawnAround(run, anchor, HerdCows, ReinforceCows, 0, TargetKind.Reinforcement)
                    + SpawnAround(run, anchor, HerdElites, ReinforceElites, 1, TargetKind.Reinforcement);

        ScriptText.ShowBannerToRegion(run.Region, "aom_reinforce", 0, "alert", 2500);
        Taunt(run.Region, "aom_taunt_reinforce", run.Phase == "AllFather" ? AllFather : Brevik);
        Announce(run.Region, $"Brevik calls {spawned} more of the herd!");
    }
}

//------------------------------------------------------------------------------
// Damage, defeats, rewards
//------------------------------------------------------------------------------

void AttachDamageTracking(Run run)
{
    run.DamageAction = (in AdjustHealthGameEvent evt) =>
    {
        try
        {
            if (IsLoaded == false || run.Ended || evt.Player == null || evt.Damage >= 0 || evt.Dodged)
                return;

            WorldEntity target = evt.Entity;
            if (target == null || (run.Targets.ContainsKey(target.Id) == false && run.Guards.Contains(target.Id) == false))
                return;

            ulong dbId = evt.Player.DatabaseUniqueId;
            long damage = -evt.Damage;
            run.PhaseDamage[dbId] = run.PhaseDamage.GetValueOrDefault(dbId) + damage;
            run.RunDamage[dbId] = run.RunDamage.GetValueOrDefault(dbId) + damage;
            run.PlayerNames[dbId] = evt.Player.GetName();

            if (run.Targets.TryGetValue(target.Id, out Target t) && t.IsFinal)
                run.FinalBossDamage[dbId] = run.FinalBossDamage.GetValueOrDefault(dbId) + damage;
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
    run.Guards.Remove(entityId);
    if (run.Targets.Remove(entityId, out Target target) == false)
        return;

    Region region = run.Region;

    switch (target.Kind)
    {
        case TargetKind.Herd:
            run.CounterDone++;
            ScriptText.SetObjectiveCounter(region, run.CounterDone, run.CounterTotal);
            if (run.Targets.Values.Any(t => t.Kind == TargetKind.Herd) == false)
                HerdWaveCleared(run);
            break;

        case TargetKind.Commander:
            run.CounterDone++;
            ScriptText.SetObjectiveCounter(region, run.CounterDone, run.CounterTotal);
            if (killed)
                ScriptText.ShowBannerToRegion(region, "aom_commander_down", 0, "reward", 2500);

            int left = run.Targets.Values.Count(t => t.Kind == TargetKind.Commander);
            if (left > 0)
            {
                Announce(region, $"{target.Name} unmasked! {left} left.");
            }
            else
            {
                GivePhaseRewards(run, "Commanders");
                After(region.Game, 4f, () => StartBrevik(run));
            }
            break;

        case TargetKind.Boss:
            run.BossId = 0;

            // Health bar: defeated for a moment (until the All-Father rises), or off right away if he vanished
            if (ShowBossHealth)
            {
                if (killed)
                {
                    ScriptPresentation.WidgetEntityDefeated(region, BossHealthWidget, entityId);
                    After(region.Game, SecondFormDelay, () => ScriptPresentation.WidgetUntrack(region, BossHealthWidget, entityId));
                }
                else
                {
                    ScriptPresentation.WidgetUntrack(region, BossHealthWidget, entityId);
                }
            }

            if (target.IsFinal == false)
            {
                // High Commander Brevik falls... and rises again where he fell (or respawns if he just vanished)
                Vector3 fellAt = target.Position;
                WorldEntity body = region.Game.EntityManager.GetEntity<WorldEntity>(entityId);
                if (body != null)
                    fellAt = body.RegionLocation.Position;

                After(region.Game, killed ? SecondFormDelay : 3f, () =>
                {
                    // Killed: the All-Father rises where Brevik fell. Vanished: High Commander Brevik comes back.
                    run.Phase = killed ? "Brevik" : "Commanders";
                    StartBrevik(run, killed, killed ? fellAt : null);
                });
            }
            else if (killed)
            {
                AnnounceFinalBossDefeated(run);
                Taunt(region, "aom_taunt_win", AllFather);
                CompleteRun(run);
            }
            else
            {
                // The All-Father vanished (despawned, fell out of the world): bring him back
                After(region.Game, 3f, () => { run.Phase = "Brevik"; StartBrevik(run, true); });
            }
            break;

        case TargetKind.Reinforcement:
            break;
    }
}

void AnnounceFinalBossDefeated(Run run)
{
    long total = Math.Max(run.FinalBossDamage.Values.Sum(), 1);
    var ranking = run.FinalBossDamage.Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value).Take(3)
        .Select(pair => (DbId: pair.Key, Name: run.PlayerNames.GetValueOrDefault(pair.Key, "?"), Percent: pair.Value * 100 / total)).ToList();

    if (ranking.Count >= 2)
        ScriptText.ShowPlayerBannerToRegion(run.Region, "aom_top2", 0, ranking[0].Name, ranking[1].Name, new[] { ranking[0].Percent, ranking[1].Percent });
    else if (ranking.Count == 1)
        ScriptText.ShowPlayerBannerToRegion(run.Region, "aom_top", 0, ranking[0].Name, "", new[] { ranking[0].Percent });

    if (ranking.Count == 0)
        return;

    Announce(run.Region, "All-Father Brevik defeated! Top damage: " +
        string.Join(", ", ranking.Select((entry, i) => $"{i + 1}. {entry.Name} ({entry.Percent}%)")));

    Avatar topAvatar = run.Region.Game.EntityManager.GetEntityByDbGuid<Player>(ranking[0].DbId)?.CurrentAvatar;
    if (topAvatar != null && topAvatar.Region == run.Region)
        ScriptText.ShowOverheadText(topAvatar, "aom_overhead", 0, OverheadSeconds);
}

void DropRewards(Run run, Dictionary<ulong, long> damageByPlayer, Reward[] rewards, string what)
{
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

        ScriptHooks.SendChatMessage(player, $"[Age of MOO] {what} rewards have dropped for you.", false);
    }
}

void GivePhaseRewards(Run run, string phase)
{
    if (PhaseRewards.TryGetValue(phase, out Reward[] rewards))
        DropRewards(run, run.PhaseDamage, rewards, phase == "Herd" ? "Stampede" : "Commander");

    run.PhaseDamage.Clear();
}

void CompleteRun(Run run)
{
    if (run.Ended)
        return;

    DropRewards(run, run.RunDamage, CompletionRewards, "Age of MOO");
    float seconds = (float)(run.Region.Game.CurrentTime - run.StartTime).TotalSeconds;

    // Clear time onto the weekly leaderboard for everyone who fought (time boards take milliseconds; only the best counts)
    if (ClearTimeLeaderboard.Length > 0)
    {
        long milliseconds = (long)(seconds * 1000f);
        foreach (Player player in PlayersIn(run.Region))
        {
            if (run.RunDamage.GetValueOrDefault(player.DatabaseUniqueId) <= 0)
                continue;

            if (ScriptLeaderboards.Submit(player, ClearTimeLeaderboard, milliseconds))
                ScriptHooks.SendChatMessage(player, $"[Age of MOO] Clear time {FormatTime(seconds)} sent to the weekly Fastest Clear leaderboard.", false);
        }
    }
    EndRun(run, true, $"The herd is broken and Brevik is beaten! Cleared in {FormatTime(seconds)}.", true);
}

void EndRun(Run run, bool success, string message, bool announce)
{
    if (run.Ended)
        return;

    run.Ended = true;
    Region region = run.Region;
    DetachDamageTracking(run);
    ScriptPresentation.ClearWidget(region, TimerWidget);
    ScriptPresentation.ClearWidget(region, BossHealthWidget);   // before Brevik despawns

    foreach (ulong entityId in run.Targets.Keys.Concat(run.Guards))
        ScriptSpawner.Despawn(region.Game, entityId);
    run.Targets.Clear();
    run.Guards.Clear();

    if (announce)
    {
        ScriptText.ClearObjective(region);
        ScriptText.ShowBannerToRegion(region, success ? "aom_complete" : "aom_failed", 0, success ? "rewardlarge" : "error", 5000);
        Announce(region, message);

        var top = run.RunDamage.OrderByDescending(pair => pair.Value).Take(3).ToList();
        if (top.Count > 0)
        {
            long total = Math.Max(run.RunDamage.Values.Sum(), 1);
            Announce(region, "Top damage: " + string.Join(", ", top.Select((pair, i) =>
                $"{i + 1}. {run.PlayerNames.GetValueOrDefault(pair.Key, "?")} ({pair.Value * 100 / total}%)")));
        }
    }

    Log.Info($"Age of MOO in {EventRegions[run.PlaceIndex].Name} started by {run.OpenerName} ended: {message}");

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

    foreach (ulong entityId in run.Targets.Keys.ToList())
    {
        if (run.Ended) return;
        if (ScriptSpawner.IsAlive(region.Game, entityId) == false)
            TargetDown(run, entityId, false);
    }

    if (run.Ended)
        return;

    run.Guards.RemoveWhere(id => ScriptSpawner.IsAlive(region.Game, id) == false);
    CheckBossHealth(run);

    run.EmptySeconds = PlayersIn(region).Any() ? 0f : run.EmptySeconds + TickSeconds;
    if (run.EmptySeconds >= EmptyRegionTimeout)
    {
        EndRun(run, false, "Everyone left.", false);
        return;
    }

    float elapsed = (float)(region.Game.CurrentTime - run.StartTime).TotalSeconds;
    float left = TimeLimit - elapsed;
    if (left <= 0f)
    {
        EndRun(run, false, "Time ran out. The herd grazes on, undefeated.", true);
        return;
    }

    foreach (float warning in new[] { 300f, 60f })
    {
        if (left <= warning && run.WarningsGiven.Add(warning))
            Announce(region, $"{FormatTime(warning)} left before the herd takes over for good!");
    }

    if (elapsed >= run.NextHintAt)
    {
        run.NextHintAt = elapsed + HintInterval;
        if (run.Phase == "Commanders")
            SendHints(run);
    }

    After(region.Game, TickSeconds, () => Tick(run));
}

string NearestHint(Run run, Avatar avatar)
{
    var kind = run.Phase switch
    {
        "Commanders" => TargetKind.Commander,
        "Brevik" or "AllFather" => TargetKind.Boss,
        _ => TargetKind.Herd,
    };

    var targets = run.Targets.Values.Where(t => t.Kind == kind).ToList();
    if (targets.Count == 0)
        return string.Empty;

    Vector3 here = avatar.RegionLocation.Position;
    Target nearest = targets.OrderBy(t => Vector3.Distance2D(here, t.Position)).First();
    int meters = (int)(Vector3.Distance2D(here, nearest.Position) / 100f);
    return $"Nearest {nearest.Name}: about {meters} m away.";
}

void SendHints(Run run)
{
    foreach (Player player in PlayersIn(run.Region))
    {
        string hint = NearestHint(run, player.CurrentAvatar);
        if (hint.Length > 0)
            ScriptHooks.SendChatMessage(player, "[Age of MOO] " + hint, false);
    }
}

//------------------------------------------------------------------------------
// Hooks
//------------------------------------------------------------------------------

Hooks.On(ScriptHooks.EntityInteracted, e =>
{
    if (e.ScriptTag != NpcTag)
        return;

    ScriptDialog.Show(e.Player, "aom_prompt", "aom_enter", "aom_cancel", (player, button) =>
    {
        if (button == 1 && player.GetRegion()?.PrototypeDataRef == hubRef)
            OpenEvent(player);
    }, e.Entity);
});

Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    Region region = e.Region;

    if (region.PrototypeDataRef == hubRef)
    {
        EnsureHubNpc(region);
        return;
    }

    if (IsEventRegion(region) == false || runs.ContainsKey(region))
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
    if (region != null && runs.TryGetValue(region, out Run run) && run.Ended == false)
    {
        if (run.Targets.ContainsKey(e.VictimId))
            TargetDown(run, e.VictimId, true);
        else
            run.Guards.Remove(e.VictimId);
    }
});

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "moo")
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
        OpenEvent(e.Player);
        return;
    }

    if (sub == "npc")
    {
        if (IsAdmin(e.Player) == false) { e.Reply("Admin only."); return; }
        if (region.PrototypeDataRef != hubRef) { e.Reply("Go to Avengers Tower first."); return; }

        Vector3 here = avatar.RegionLocation.Position;
        float yaw = MathF.Atan2(avatar.Forward.Y, avatar.Forward.X) * 180f / MathF.PI + 180f;
        Vector3 spot = here + avatar.Forward * 120f;
        EnsureHubNpc(region, spot, yaw);
        e.Reply("Agent moved (until restart). To keep it there, set in age_of_moo.csx:");
        e.Reply($"Vector3? NpcPosition = new Vector3({spot.X:0}f, {spot.Y:0}f, {spot.Z:0}f); float NpcYawDegrees = {yaw:0}f;");
        return;
    }

    if (runs.TryGetValue(region, out Run run) && run.Ended == false)
    {
        if (run.StartTime == TimeSpan.Zero)
        {
            e.Reply("The herd is gathering... moo.");
            return;
        }

        float left = TimeLimit - (float)(region.Game.CurrentTime - run.StartTime).TotalSeconds;
        string stage = run.Phase switch
        {
            "Herd"       => $"Stampede {run.WaveIndex + 1} / {HerdWaves.Length}: {run.CounterDone} / {run.CounterTotal} cows stopped",
            "Commanders" => $"Skrull Commanders {run.CounterDone} / {run.CounterTotal} unmasked",
            "Brevik"     => "Defeat High Commander Brevik",
            "AllFather"  => "Defeat All-Father Brevik",
            _            => "Starting",
        };
        e.Reply($"{EventRegions[run.PlaceIndex].Name}: {stage}, {FormatTime(left)} left. {NearestHint(run, avatar)}");
    }
    else
    {
        e.Reply("No herd here. Talk to the S.H.I.E.L.D. agent in Avengers Tower to begin the Age of MOO.");
    }
});

Log.Info($"Age of MOO loaded ({EventRegions.Count(p => p.Ref != PrototypeId.Invalid)} regions)");

//------------------------------------------------------------------------------
// Types
//------------------------------------------------------------------------------

enum TargetKind { Herd, Commander, Boss, Reinforcement }

class Place
{
    public string Name { get; }
    public string Path { get; }
    public PrototypeId Ref { get; set; } = PrototypeId.Invalid;

    public Place(string name, string path)
    {
        Name = name;
        Path = path;
    }
}

class Foe
{
    public string Name { get; }
    public string Path { get; }
    public PrototypeId Ref { get; set; } = PrototypeId.Invalid;

    public Foe(string name, string path)
    {
        Name = name;
        Path = path;
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
    public TargetKind Kind;
    public string Name;
    public Vector3 Position;
    public bool IsFinal;
}

class Run
{
    public int Seed;
    public int PlaceIndex;
    public string OpenerName;
    public DateTime CreatedAt;

    public Region Region;
    public Vector3 Entrance;
    public bool StartScheduled;
    public TimeSpan StartTime;
    public bool Ended;
    public string Phase = "";
    public int WaveIndex;
    public int CounterTotal;
    public int CounterDone;
    public ulong BossId;
    public float EmptySeconds;
    public float NextHintAt;
    public HashSet<float> WarningsGiven = new();
    public HashSet<float> ReinforcementsCalled = new();

    public Dictionary<ulong, Target> Targets = new();      // counted enemies: entity id => target
    public HashSet<ulong> Guards = new();                  // commander bodyguards (not counted)
    public Dictionary<ulong, long> PhaseDamage = new();    // player db id => damage to the herd this phase
    public Dictionary<ulong, long> RunDamage = new();
    public Dictionary<ulong, long> FinalBossDamage = new();
    public Dictionary<ulong, string> PlayerNames = new();
    public Event<AdjustHealthGameEvent>.Action DamageAction;
}
