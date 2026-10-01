// Midtown Boss Rush: a wave-based supervillain gauntlet in Cosmic Midtown Patrol with Danger Room style on-screen text
// (wave intro banners, "Wave N Clear", boss defeated banners with the top damage dealer, and the wave title + counter
// bar on the HUD).
//
// Only runs in Midtown Patrol on the Cosmic difficulty tier (RequiredDifficulty). Starts on its own when the first
// player enters an empty Cosmic Midtown instance (see AutoStart). Everyone in the instance can join the fight. When a
// rush ends (completed or failed) the next one starts NextRushDelay later, if anyone is still there. Every cleared wave
// drops rewards for each player in the instance who damaged one of its bosses (see WaveRewards).
//
//   !bossrush              show the current wave, or the time until the next rush
//   !bossrush here <name>  (admin) print your position as a SpawnSpots line to paste below
//
// Bosses are Midtown's own event bosses (Entity/Characters/Bosses/PatrolMidtown). Match can be a full prototype path
// or '|'-separated name fragments; the chosen prototype is written to the server log.
//
// NOTE: on-screen text registered here reaches players the next time they connect (client limitation).
// A hot reload ends running rushes: bosses already spawned stay as normal enemies.

using System.Collections.Concurrent;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Events;

//------------------------------------------------------------------------------
// Settings
//------------------------------------------------------------------------------

const bool  AutoStart          = true;   // start when the first player enters an empty Midtown instance
const float AutoStartDelay     = 10f;    // seconds after that player has finished loading in
const float LoadInTimeout      = 60f;    // give up if they never finish loading (disconnect, left again)
const float NextRushDelay      = 300f;   // after a rush ends (completed or failed), start the next one this long later (needs a player in the instance)
const float StartDelay         = 2.5f;   // seconds the "Midtown Boss Rush Starting!" banner shows before wave 1
const float NextWaveDelay      = 8f;     // seconds between waves
const float EntranceAvoidDistance = 4000f; // without named spots: bosses spawn at a random spot at least this far from the entrance and every player
const float SpotSpread        = 400f;   // with SpawnSpots: bosses spawn within this distance of the spot
const bool  RandomSpotOrder    = false;  // with SpawnSpots: false = wave 1 at spot 1, wave 2 at spot 2, ... (wrapping); true = random
const bool  BossesHuntPlayers  = false;  // with SpawnSpots: false = bosses hold their spot until players come; true = they chase the starter
const float EmptyRegionTimeout = 30f;   // end the rush if nobody is left in the region for this long
const float TickSeconds        = 2f;
const bool  BossesDropLoot     = true;
const float MinDamagePercent   = 0f;     // share of a wave's boss damage a player needs for its rewards (0 = any damage)
const int   TopDamageShown     = 3;      // damage leaders listed in chat when a rush ends

// Cosmic Midtown only: the region's difficulty tier must be this one (Tier3Superheroic is the Cosmic tier in 1.52)
const string RequiredDifficulty = "Difficulty/Tiers/Tier3Superheroic.prototype";

// On-screen top damage dealer when each boss dies ("Rhino Defeated! Top damage: Name (54%)"). This uses the metagame
// banner PvP uses for "X defeated you": the client fills $playersource$ with the player name and $intargzero$ with the
// first number. false = the plain "Rhino Defeated!" banner (the top damage dealer still goes to chat).
const bool   ShowTopDamageBanner = true;
const bool   ShowTopDamageOverhead = true;  // "Top Damage!" over the first place player's head when a boss dies
const float  OverheadSeconds       = 5f;
const bool   ShowBossPortraits     = true;  // portrait popup ("Rhino has arrived in Midtown!") when each boss spawns
const bool   ShowBossHealth        = true;  // HUD health bar for each boss of the wave (boss icon + health %)
const string BossHealthWidget      = "UI/MetaGame/SurturRaid/FiveMan/SlagHealth.prototype";
const float  DefeatedShowSeconds   = 2f;    // a defeated boss stays on the health bar (at 0 %) this long
const string AffixExclude      = "";     // comma-separated affix name words to never roll on bosses
const string PreferPaths       = "Patrol|Midtown|Manhattan";  // prefer boss variants whose path contains one of these

// Name is what players see, Match is how the boss is found in the game data (Midtown's own event bosses)
const string MidtownBoss = "Entity/Characters/Bosses/PatrolMidtown/MidtownEvent";
var Bullseye       = new Boss("Bullseye",         MidtownBoss + "Bullseye.prototype");
var Rhino          = new Boss("Rhino",            MidtownBoss + "Rhino.prototype");
var Electro        = new Boss("Electro",          MidtownBoss + "Electro.prototype");
var Taskmaster     = new Boss("Taskmaster",       MidtownBoss + "Taskmaster.prototype");
var Kraven         = new Boss("Kraven",           MidtownBoss + "Kraven.prototype");
var Tombstone      = new Boss("Tombstone",        MidtownBoss + "Tombstone.prototype");
var Lizard         = new Boss("Lizard",           MidtownBoss + "Lizard.prototype");
var DocOck         = new Boss("Doctor Octopus",   MidtownBoss + "DoctorOctopus.prototype");
var GreenGoblin    = new Boss("Green Goblin",     MidtownBoss + "GreenGoblin.prototype");
var Sabretooth     = new Boss("Sabretooth",       MidtownBoss + "Sabretooth.prototype");
var LadyDeathstrike = new Boss("Lady Deathstrike", MidtownBoss + "LadyDeathstrike.prototype");
var Juggernaut     = new Boss("Juggernaut",       MidtownBoss + "Juggernaut.prototype");
var Venom          = new Boss("Venom",            MidtownBoss + "Venom.prototype");
var Magneto        = new Boss("Magneto",          MidtownBoss + "Magneto.prototype");
var MisterSinister = new Boss("Mister Sinister",  MidtownBoss + "MisterSinister.prototype");
var DoctorDoom     = new Boss("Doctor Doom",      MidtownBoss + "DoctorDoom.prototype");

// Each wave: extra random affixes per boss, then the bosses that spawn together
var Waves = new List<Wave>
{
    new(0, Bullseye),
    new(0, Rhino),
    new(1, Electro),
    new(1, Taskmaster),
    new(1, Kraven),
    new(2, Tombstone, Lizard),
    new(2, DocOck),
    new(2, GreenGoblin),
    new(3, Sabretooth, LadyDeathstrike),
    new(3, Juggernaut),
    new(4, Venom),
    new(4, Magneto),
    new(5, DoctorDoom, MisterSinister, Venom),
};

// Fixed spawn spots. Midtown's layout is the same in every instance, so a spot is a plain map position.
// Only spots with a name are used; unnamed ones are ignored (and logged). If a spot is crowded, its bosses spawn wider
// around that same spot (then at another named spot) instead of near the players.
// No named spots at all = each wave spawns at a random spot out in Midtown, EntranceAvoidDistance away from the entrance / hub.
// To add one: stand where the bosses should appear, type !bossrush here Spot Name, and paste the line it prints.
// The name is shown in chat ("Wave 3: Electro at Times Square!").
var SpawnSpots = new List<Spot>
{
};

var NamedSpots = SpawnSpots.Where(spot => string.IsNullOrWhiteSpace(spot.Name) == false).ToList();
if (NamedSpots.Count < SpawnSpots.Count)
    Log.Warn($"{SpawnSpots.Count - NamedSpots.Count} spawn spot(s) have no name and are ignored");

//------------------------------------------------------------------------------
// Rewards
// Dropped at the feet of every player in the instance who damaged one of the wave's bosses, when the wave is cleared
// (each player gets their own copy). One line per wave, in the same order as Waves.
// CompletionRewards go to everyone in the instance who damaged any boss during the rush.
// One StarkTech Power Cube (Gem1) drops only at the end, as part of CompletionRewards. Bosses never drop them from their own loot.
//------------------------------------------------------------------------------

const string FC         = "Entity/Items/Consumables/Prototypes/FortuneCard/";
const string CSBox      = "Entity/Items/Consumables/Prototypes/CSGrant/";
const string StarkCube  = "Entity/Items/Gems/Gem1.prototype";   // StarkTech Power Cube
const string MidtownFC  = FC + "CosmicFortuneCardMidtown.prototype";

var WaveRewards = new Reward[][]
{
    /*  1 */ new Reward[] { new(FC + "AgeOfUltronFortuneCard.prototype", 2) },
    /*  2 */ new Reward[] { new(FC + "SpiderManHomecomingFortuneCard.prototype"), new(FC + "LoganFortuneCard.prototype") },
    /*  3 */ new Reward[] { new(MidtownFC) },
    /*  4 */ new Reward[] { new(FC + "GuardiansOfTheGalaxyVol2FortuneCard.prototype", 3) },
    /*  5 */ new Reward[] { new(CSBox + "CSGrantCrateProtectorsCommendations10Box.prototype") },
    /*  6 */ new Reward[] { new(FC + "SpiderManHomecomingFortuneCard.prototype", 3) },
    /*  7 */ new Reward[] { new(MidtownFC, 2) },
    /*  8 */ new Reward[] { new(FC + "LoganFortuneCard.prototype", 3) },
    /*  9 */ new Reward[] { new(CSBox + "CSGrantCrateHeroCommendation25Box.prototype") },
    /* 10 */ new Reward[] { new(CSBox + "CSGrantCrateARMORDriveBox25.prototype") },
    /* 11 */ new Reward[] { new(FC + "XMenFortuneCard.prototype", 5) },
    /* 12 */ new Reward[] { new("Entity/Items/Consumables/Prototypes/RandomGiftboxes/RandomCosmicArtifactBox.prototype") },
    /* 13 */ new Reward[] { new(FC + "OdinsBountyFortuneCard.prototype", 5) },
};

var CompletionRewards = new Reward[]
{
    new("Entity/Items/Consumables/Prototypes/GShop/ConsumablesMisc/FC75EternitySplinters.prototype"),
    new("Entity/Items/Consumables/Prototypes/DailyGift/LargeRunebox.prototype"),
    new("Entity/Items/Consumables/Prototypes/GenoshaInfluence200Box.prototype"),
    new(StarkCube, 1),
};

if (WaveRewards.Length != Waves.Count)
    Log.Warn($"WaveRewards has {WaveRewards.Length} lines but there are {Waves.Count} waves; waves without a line give no reward");

//------------------------------------------------------------------------------
// On-screen text (registered at load, see README)
//------------------------------------------------------------------------------

var AllBosses = Waves.SelectMany(wave => wave.Bosses).Distinct().ToList();

ScriptText.Register("mbr_starting", "Midtown Boss Rush Starting!");
ScriptText.Register("mbr_complete", "Midtown Boss Rush Complete!");
ScriptText.Register("mbr_failed", "Boss Rush Failed");
ScriptText.Register("mbr_overhead", "Top Damage!");
ScriptText.RegisterRange("mbr_clear", "Wave {0} Clear", 1, Waves.Count);

for (int i = 0; i < Waves.Count; i++)
{
    int number = i + 1;
    string names = string.Join(" & ", Waves[i].Bosses.Select(boss => boss.Name));
    string label = number == Waves.Count ? "Final Wave" : $"Wave {number}";

    ScriptText.RegisterRange("mbr_intro", $"{label}: {names}", number, number);
    ScriptText.RegisterRange("mbr_objective", $"Boss Rush - Wave {number} / {Waves.Count}", number, number);
    ScriptText.RegisterRange("mbr_incoming", $"Boss Rush - Wave {number} incoming", number, number);
}

for (int i = 0; i < AllBosses.Count; i++)
{
    ScriptText.RegisterRange("mbr_down", $"{AllBosses[i].Name} Defeated!", i, i);
    // Metagame banner placeholders (client syntax): $playersource$ / $playertarget$ = player names 1 / 2,
    // $intargzero$ / $intargone$ = first / second number
    ScriptText.Register($"mbr_top_{i}", $"{AllBosses[i].Name} Defeated! Top damage: $playersource$ ($intargzero$%)");
    ScriptText.Register($"mbr_top2_{i}", $"{AllBosses[i].Name} Defeated! 1st: $playersource$ ($intargzero$%)  2nd: $playertarget$ ($intargone$%)");

    // Portrait popup when the boss spawns
    ScriptText.RegisterRange("mbr_arrive", $"{AllBosses[i].Name} has arrived in Midtown!", i, i);
}

//------------------------------------------------------------------------------
// Runs (one per Midtown instance). All run state is only touched on the thread of the game that owns the region.
//------------------------------------------------------------------------------

var runs = new ConcurrentDictionary<Region, Run>();

PrototypeId requiredDifficultyRef = PrototypeId.Invalid;

// Midtown Patrol on the Cosmic tier only
bool IsMidtown(Region region)
{
    if (region == null) return false;
    var regionRef = (RegionPrototypeId)(ulong)region.PrototypeDataRef;
    if (regionRef != RegionPrototypeId.XManhattanRegion1to60 && regionRef != RegionPrototypeId.XManhattanRegion60Cosmic)
        return false;

    if (requiredDifficultyRef == PrototypeId.Invalid)
        requiredDifficultyRef = GameDatabase.GetPrototypeRefByName(RequiredDifficulty);

    return region.DifficultyTierRef == requiredDifficultyRef;
}

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
        ScriptHooks.SendChatMessage(player, "[Midtown Boss Rush] " + text, false);
}

bool ResolveBosses(out string missing)
{
    foreach (Boss boss in AllBosses)
    {
        if (boss.Ref == PrototypeId.Invalid)
            boss.Ref = ScriptSpawner.FindAgent(boss.Match, PreferPaths);
    }

    var unresolved = AllBosses.Where(boss => boss.Ref == PrototypeId.Invalid).Select(boss => boss.Name).ToList();
    missing = string.Join(", ", unresolved);
    return unresolved.Count < AllBosses.Count;
}

void StartRun(Player player, Region region, string intro)
{
    var run = new Run { Region = region, StarterDbId = player.DatabaseUniqueId, StarterName = player.GetName() };
    if (runs.TryAdd(region, run) == false)
        return;

    AttachDamageTracking(run);

    // One "Starting!" banner, then wave 1 as soon as it has been read
    ScriptText.ShowBannerToRegion(region, "mbr_starting", 0, "large", (int)(StartDelay * 1000));
    Announce(region, $"{intro}: {Waves.Count} waves of Midtown's worst. Everyone in the zone can join in!");

    After(region.Game, StartDelay, () => StartWave(run, 0));
    After(region.Game, TickSeconds, () => Tick(run));
}

void StartWave(Run run, int waveIndex)
{
    if (run.Ended)
        return;

    Region region = run.Region;
    Wave wave = Waves[waveIndex];
    int number = waveIndex + 1;
    run.WaveIndex = waveIndex;
    run.Alive.Clear();
    run.WaveDamage.Clear();
    run.BossDamage.Clear();

    Avatar anchor = FindAnchorAvatar(run);
    if (anchor == null)
    {
        EndRun(run, "mbr_failed", "Nobody is left to fight.");
        return;
    }

    // A named spot, or without any: one random spot out in Midtown, away from where players arrive (never the players' own area)
    Spot spot = NamedSpots.Count > 0
        ? NamedSpots[RandomSpotOrder ? Random.Shared.Next(NamedSpots.Count) : waveIndex % NamedSpots.Count]
        : FindFieldSpot(region);

    if (spot == null)
        Log.Warn($"Wave {number}: no named spawn spots and no open spot away from the entrance, skipping the wave");

    foreach (Boss boss in wave.Bosses)
    {
        if (boss.Ref == PrototypeId.Invalid || spot == null)
            continue;

        Agent agent = SpawnAtSpot(region, boss, spot, anchor, number);

        if (agent == null)
        {
            Log.Warn($"Wave {number}: failed to spawn {boss.Name}");
            continue;
        }

        ScriptSpawner.AddRandomAffixes(agent, wave.Affixes, AffixExclude);
        run.Alive[agent.Id] = AllBosses.IndexOf(boss);

        if (ShowBossHealth)
            ScriptPresentation.WidgetTrackHealth(region, BossHealthWidget, agent);
    }

    run.WaveTotal = run.Alive.Count;
    run.WaveDefeated = 0;

    if (run.WaveTotal == 0)
    {
        // Nothing could spawn (unknown boss or no room), skip ahead instead of stalling
        Log.Warn($"Wave {number}: no bosses spawned, skipping");
        WaveCleared(run);
        return;
    }

    ScriptText.ShowBannerToRegion(region, "mbr_intro", number, "large", 3500);

    // One portrait popup per boss that actually spawned (a boss appearing twice in a wave is announced once)
    if (ShowBossPortraits)
    {
        foreach (int bossIndex in run.Alive.Values.Distinct())
            ScriptText.ShowPortraitNotificationToRegion(region, AllBosses[bossIndex].Ref, "mbr_arrive", bossIndex);
    }

    ScriptText.SetObjectiveTitle(region, "mbr_objective", number);
    ScriptText.SetObjectiveCounter(region, 0, run.WaveTotal);

    string names = string.Join(" & ", wave.Bosses.Where(boss => boss.Ref != PrototypeId.Invalid).Select(boss => boss.Name));
    string where = string.IsNullOrEmpty(spot?.Name) ? "" : $" at {spot.Name}";
    Announce(region, (number == Waves.Count ? "Final wave" : $"Wave {number}") + $": {names}{where}!");
}

// Never near the players: the wave's spot, wider around it if crowded, then the other named spots
Agent SpawnAtSpot(Region region, Boss boss, Spot spot, Avatar anchor, int number)
{
    IEnumerable<Spot> candidates = NamedSpots.Contains(spot)
        ? NamedSpots.OrderBy(other => other == spot ? 0 : 1)
        : new[] { spot };

    foreach (Spot candidate in candidates)
    {
        for (int widen = 1; widen <= 3; widen++)
        {
            Agent agent = ScriptSpawner.SpawnHostile(region, boss.Ref, candidate.Position, 0f, SpotSpread * widen,
                anchor, BossesDropLoot, true, BossesHuntPlayers);
            if (agent == null)
                continue;

            if (candidate != spot)
                Log.Warn($"Wave {number}: no room for {boss.Name} at {spot}, spawned at {candidate} instead");
            return agent;
        }
    }

    return null;
}

// Without named spots: a random walkable spot at least EntranceAvoidDistance from the region's entrance AND from every
// player in the instance (players idle in the lobby, so bosses must never land there). Never falls back to a player's
// position: if no such spot is found the wave is skipped. Unnamed, so chat does not name a place.
Spot FindFieldSpot(Region region)
{
    if (ScriptSpawner.TryGetStartPosition(region, out Vector3 entrance) == false)
        return null;

    List<Vector3> avoid = PlayersIn(region).Select(player => player.CurrentAvatar.RegionLocation.Position).ToList();
    avoid.Add(entrance);
    float avoidSq = EntranceAvoidDistance * EntranceAvoidDistance;

    const int Attempts = 12;
    for (int i = 0; i < Attempts; i++)
    {
        if (ScriptSpawner.TryFindRandomSpot(region, out Vector3 pos, entrance, EntranceAvoidDistance) == false)
            continue;

        if (avoid.Any(point => Vector3.DistanceSquared2D(point, pos) < avoidSq))
            continue;

        return new Spot("", pos.X, pos.Y, pos.Z);
    }

    return null;
}

// Damage tracking uses the region's own AdjustHealthEvent (the same event the metagame score handlers use). It credits
// the player behind the damage, so pets, summons and team-ups count for their owner.
void AttachDamageTracking(Run run)
{
    run.DamageAction = (in AdjustHealthGameEvent evt) =>
    {
        // Region events do not catch exceptions, so never let one escape into the damage pipeline
        try
        {
            if (IsLoaded == false || run.Ended || evt.Player == null || evt.Damage >= 0 || evt.Dodged)
                return;

            WorldEntity boss = evt.Entity;
            if (boss == null || run.Alive.ContainsKey(boss.Id) == false)
                return;

            ulong dbId = evt.Player.DatabaseUniqueId;
            long damage = -evt.Damage;
            if (run.BossDamage.TryGetValue(boss.Id, out var bossDamage) == false)
                run.BossDamage[boss.Id] = bossDamage = new Dictionary<ulong, long>();
            bossDamage[dbId] = bossDamage.GetValueOrDefault(dbId) + damage;

            run.WaveDamage[dbId] = run.WaveDamage.GetValueOrDefault(dbId) + damage;
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

// Did this player do enough of the damage? MinDamagePercent 0 means any damage at all counts.
bool EarnedReward(Dictionary<ulong, long> damageByPlayer, ulong dbId)
{
    long damage = damageByPlayer.GetValueOrDefault(dbId);
    if (damage <= 0)
        return false;

    long total = damageByPlayer.Values.Sum();
    return total <= 0 || damage * 100.0 / total >= MinDamagePercent;
}

// "Rhino Defeated!" on screen, with the boss's top damage dealer in a metagame banner once ShowTopDamageBanner is on
void AnnounceBossDefeated(Run run, ulong entityId, int bossIndex)
{
    string bossName = AllBosses[bossIndex].Name;

    // Damage ranking for this boss: (player db id, name, percent)
    var ranking = new List<(ulong DbId, string Name, long Percent)>();
    if (run.BossDamage.TryGetValue(entityId, out var damageByPlayer) && damageByPlayer.Count > 0)
    {
        long total = Math.Max(damageByPlayer.Values.Sum(), 1);
        foreach (var pair in damageByPlayer.Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value).Take(3))
            ranking.Add((pair.Key, run.PlayerNames.GetValueOrDefault(pair.Key, "?"), pair.Value * 100 / total));
    }

    // The banner has two name slots ($playersource$ / $playertarget$), so it shows the top 2; chat gets the top 3
    if (ShowTopDamageBanner && ranking.Count >= 2)
        ScriptText.ShowPlayerBannerToRegion(run.Region, $"mbr_top2_{bossIndex}", 0, ranking[0].Name, ranking[1].Name,
            new[] { ranking[0].Percent, ranking[1].Percent });
    else if (ShowTopDamageBanner && ranking.Count == 1)
        ScriptText.ShowPlayerBannerToRegion(run.Region, $"mbr_top_{bossIndex}", 0, ranking[0].Name, "", new[] { ranking[0].Percent });
    else
        ScriptText.ShowBannerToRegion(run.Region, "mbr_down", bossIndex, "reward", 2500);

    if (ranking.Count == 0)
        return;

    Announce(run.Region, $"{bossName} defeated! Top damage: " +
        string.Join(", ", ranking.Select((entry, i) => $"{i + 1}. {entry.Name} ({entry.Percent}%)")));

    // "Top Damage!" over the first place player's head
    if (ShowTopDamageOverhead)
    {
        Avatar topAvatar = run.Region.Game.EntityManager.GetEntityByDbGuid<Player>(ranking[0].DbId)?.CurrentAvatar;
        if (topAvatar != null && topAvatar.Region == run.Region)
            ScriptText.ShowOverheadText(topAvatar, "mbr_overhead", 0, OverheadSeconds);
    }
}

void BossDown(Run run, ulong entityId, bool killed)
{
    if (run.Alive.Remove(entityId, out int bossIndex) == false)
        return;

    run.WaveDefeated++;

    if (killed)
        AnnounceBossDefeated(run, entityId, bossIndex);

    // Health bar: show it defeated for a moment, then take it off (right away if it vanished without dying)
    if (ShowBossHealth)
    {
        Region region = run.Region;
        if (killed)
        {
            ScriptPresentation.WidgetEntityDefeated(region, BossHealthWidget, entityId);
            After(region.Game, DefeatedShowSeconds, () => ScriptPresentation.WidgetUntrack(region, BossHealthWidget, entityId));
        }
        else
        {
            ScriptPresentation.WidgetUntrack(region, BossHealthWidget, entityId);
        }
    }

    run.BossDamage.Remove(entityId);

    ScriptText.SetObjectiveCounter(run.Region, run.WaveDefeated, run.WaveTotal);

    if (run.Alive.Count == 0)
        WaveCleared(run);
}

void WaveCleared(Run run)
{
    int number = run.WaveIndex + 1;
    Region region = run.Region;

    // A wave skipped because nothing could spawn gives nothing
    bool isFinal = number >= Waves.Count;
    if (run.WaveTotal > 0 || isFinal)
        GiveRewards(run, number, isFinal);

    if (isFinal)
    {
        EndRun(run, "mbr_complete", "Every villain in Midtown has been defeated. Well done, heroes!");
        return;
    }

    if (run.WaveTotal > 0)
        ScriptText.ShowBannerToRegion(region, "mbr_clear", number, "large", 3000);

    ScriptText.SetObjectiveTitle(region, "mbr_incoming", number + 1);
    ScriptText.SetObjectiveCounter(region, 0, Waves[number].Bosses.Length);

    int nextIndex = run.WaveIndex + 1;
    After(region.Game, NextWaveDelay, () => StartWave(run, nextIndex));
}

void EndRun(Run run, string bannerKey, string message)
{
    if (run.Ended)
        return;

    run.Ended = true;
    runs.TryRemove(run.Region, out _);
    DetachDamageTracking(run);

    // Remove the health bar before any boss despawns
    ScriptPresentation.ClearWidget(run.Region, BossHealthWidget);

    foreach (ulong entityId in run.Alive.Keys)
        ScriptSpawner.Despawn(run.Region.Game, entityId);
    run.Alive.Clear();

    ScriptText.ClearObjective(run.Region);
    ScriptText.ShowBannerToRegion(run.Region, bannerKey, 0, bannerKey == "mbr_complete" ? "rewardlarge" : "error", 5000);
    Announce(run.Region, message);

    // Top damage dealers of the whole rush
    var top = run.RunDamage.OrderByDescending(pair => pair.Value).Take(TopDamageShown).ToList();
    if (top.Count > 0)
    {
        long total = Math.Max(run.RunDamage.Values.Sum(), 1);
        Announce(run.Region, "Top damage: " + string.Join(", ", top.Select((pair, i) =>
            $"{i + 1}. {run.PlayerNames.GetValueOrDefault(pair.Key, "?")} ({pair.Value * 100 / total}%)")));
    }

    Log.Info($"Boss rush started by {run.StarterName} ended on wave {run.WaveIndex + 1}/{Waves.Count}: {message}");

    // Completed or failed, the next one comes around after the break
    ScheduleNextRush(run.Region);
}

void GiveRewards(Run run, int waveNumber, bool final)
{
    // Only players still in the instance who damaged a boss get anything: this wave's bosses for the wave reward,
    // any boss of this rush for the completion bonus
    foreach (Player player in PlayersIn(run.Region))
    {
        ulong dbId = player.DatabaseUniqueId;
        // A wave with an empty reward line gives nothing (and says nothing)
        bool waveHasRewards = waveNumber - 1 < WaveRewards.Length && WaveRewards[waveNumber - 1].Length > 0;
        bool waveEarned = waveHasRewards && EarnedReward(run.WaveDamage, dbId);
        bool bonusEarned = final && EarnedReward(run.RunDamage, dbId);

        if (waveEarned == false && bonusEarned == false)
        {
            if (waveHasRewards == false && final == false)
                continue;

            ScriptHooks.SendChatMessage(player, MinDamagePercent > 0
                ? $"[Midtown Boss Rush] Deal at least {MinDamagePercent}% of a wave's boss damage to earn its rewards."
                : "[Midtown Boss Rush] Damage a boss to earn wave rewards.", false);
            continue;
        }

        int slot = 1;
        if (waveEarned && waveNumber - 1 < WaveRewards.Length)
            slot = DropRewards(player, WaveRewards[waveNumber - 1], slot, waveNumber);
        if (bonusEarned)
            DropRewards(player, CompletionRewards, slot, waveNumber);

        ScriptHooks.SendChatMessage(player, "[Midtown Boss Rush] " + (bonusEarned
            ? (waveEarned ? "Wave rewards and the completion bonus have dropped for you!" : "The completion bonus has dropped for you!")
            : $"Wave {waveNumber} rewards have dropped for you."), false);
    }
}

int DropRewards(Player player, Reward[] rewards, int slot, int waveNumber)
{
    foreach (Reward reward in rewards)
    {
        if (ScriptRewards.DropItem(player, reward.Path, reward.Count, slot) == false)
            Log.Warn($"Wave {waveNumber}: could not drop [{reward.Path}] for {player.GetName()}");
        slot++;
    }

    return slot;
}

void Tick(Run run)
{
    if (run.Ended)
        return;

    // Bosses that vanished without dying (despawned, left the world) still count, so a wave can never get stuck
    foreach (ulong entityId in run.Alive.Keys.ToList())
    {
        if (run.Ended) return;
        if (ScriptSpawner.IsAlive(run.Region.Game, entityId) == false)
            BossDown(run, entityId, false);
    }

    if (run.Ended)
        return;

    run.EmptySeconds = PlayersIn(run.Region).Any() ? 0f : run.EmptySeconds + TickSeconds;
    if (run.EmptySeconds >= EmptyRegionTimeout)
    {
        EndRun(run, "mbr_failed", "Everyone left Midtown.");
        return;
    }

    After(run.Region.Game, TickSeconds, () => Tick(run));
}

Avatar FindAnchorAvatar(Run run)
{
    Avatar fallback = null;
    foreach (Player player in PlayersIn(run.Region))
    {
        Avatar avatar = player.CurrentAvatar;
        if (avatar.IsDead)
            continue;

        if (player.DatabaseUniqueId == run.StarterDbId)
            return avatar;

        fallback ??= avatar;
    }

    return fallback;
}

//------------------------------------------------------------------------------
// Hooks
//------------------------------------------------------------------------------

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "bossrush")
        return;

    e.Handled = true;

    Region region = e.Player.CurrentAvatar?.Region;

    // Admin: print the current position as a SpawnSpots line (works in any Midtown tier, it is the same map)
    if (e.GetArg(0) == "here" && ScriptHooks.IsAdmin(e.Player))
    {
        Avatar avatar = e.Player.CurrentAvatar;
        if (avatar == null || avatar.IsInWorld == false)
        {
            e.Reply("You need to be in the world.");
            return;
        }

        string spotName = string.Join(" ", Enumerable.Range(1, 8).Select(e.GetArg).Where(arg => string.IsNullOrEmpty(arg) == false)).Trim('"').Trim();
        if (spotName.Length == 0)
        {
            e.Reply("Give the spot a name: !bossrush here Times Square (unnamed spots are not used).");
            return;
        }
        Vector3 pos = avatar.RegionLocation.Position;
        string line = $"new(\"{spotName}\", {pos.X:0}f, {pos.Y:0}f, {pos.Z:0}f),";
        e.Reply("Add to SpawnSpots: " + line);
        Log.Info($"SpawnSpots line from {e.Player.GetName()} in [{region?.PrototypeName}]: {line}");
        return;
    }

    if (IsMidtown(region) == false)
    {
        e.Reply("The Midtown Boss Rush only runs in Cosmic Midtown Patrol.");
        return;
    }

    // Status only: rushes start and restart on their own
    if (runs.TryGetValue(region, out Run run))
        e.Reply($"Wave {Math.Max(run.WaveIndex + 1, 1)} / {Waves.Count}, {run.Alive.Count} boss(es) left.");
    else if (nextRushAt.TryGetValue(region, out TimeSpan startsAt))
        e.Reply($"The next boss rush starts in {FormatTime((float)(startsAt - region.Game.CurrentTime).TotalSeconds)}.");
    else
        e.Reply("No boss rush is running here.");
});

// Auto start: the first player into an empty Midtown instance kicks off a rush once they have loaded in.
// While a start is pending (loading in, or the break after a rush) nobody else can trigger one.
var pendingStarts = new ConcurrentDictionary<Region, byte>();
var nextRushAt = new ConcurrentDictionary<Region, TimeSpan>();

bool TryAutoStart(Region region, Player preferred, string intro)
{
    if (runs.ContainsKey(region))
        return false;

    List<Player> present = PlayersIn(region).ToList();
    Player starter = preferred != null && present.Contains(preferred) ? preferred : present.FirstOrDefault();
    if (starter == null)
        return false;

    if (ResolveBosses(out string missing) == false)
    {
        Log.Warn("Auto start skipped: no Midtown bosses could be found in the game data");
        return false;
    }

    if (missing.Length > 0)
        Log.Warn($"Bosses not found (their waves will be skipped or smaller): {missing}");

    StartRun(starter, region, intro ?? $"{starter.GetName()} has arrived in Midtown, and the villains noticed");
    return true;
}

// After a rush ends (completed or failed): announce, remind, then start the next one if anyone is still in the instance.
// If everyone leaves, the timer still runs out and a newcomer arriving later gets the normal first-player start.
void ScheduleNextRush(Region region)
{
    if (pendingStarts.TryAdd(region, 0) == false)
        return;

    nextRushAt[region] = region.Game.CurrentTime + TimeSpan.FromSeconds(NextRushDelay);
    Announce(region, $"The next boss rush starts in {FormatTime(NextRushDelay)}.");

    foreach (float reminder in new[] { 60f, 10f })
    {
        if (reminder >= NextRushDelay) continue;
        After(region.Game, NextRushDelay - reminder, () => Announce(region, $"The next boss rush starts in {FormatTime(reminder)}!"));
    }

    After(region.Game, NextRushDelay, () =>
    {
        pendingStarts.TryRemove(region, out _);
        nextRushAt.TryRemove(region, out _);
        TryAutoStart(region, null, "The villains are back for more");
    });
}

string FormatTime(float seconds)
{
    int total = Math.Max((int)Math.Ceiling(seconds), 0);
    if (total < 60) return $"{total} seconds";
    int minutes = total / 60, rest = total % 60;
    string text = minutes == 1 ? "1 minute" : $"{minutes} minutes";
    return rest == 0 ? text : $"{text} {rest} seconds";
}

void WaitForLoadIn(Player player, Region region, float waited)
{
    Avatar avatar = player.IsDestroyed ? null : player.CurrentAvatar;
    if (avatar == null || avatar.IsInWorld == false || avatar.Region != region)
    {
        if (waited >= LoadInTimeout || player.IsDestroyed)
            pendingStarts.TryRemove(region, out _);
        else
            After(region.Game, 2f, () => WaitForLoadIn(player, region, waited + 2f));
        return;
    }

    After(region.Game, AutoStartDelay, () =>
    {
        pendingStarts.TryRemove(region, out _);

        // Whoever triggered it may have left during the delay; anyone still here can carry it
        TryAutoStart(region, player, null);
    });
}

Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    if (AutoStart == false || IsMidtown(e.Region) == false || runs.ContainsKey(e.Region))
        return;

    // Only the first player in: nobody else already standing in this instance
    if (PlayersIn(e.Region).Any(player => player != e.Player))
        return;

    if (pendingStarts.TryAdd(e.Region, 0))
        WaitForLoadIn(e.Player, e.Region, 0f);
});

Hooks.On(ScriptHooks.EntityKilled, e =>
{
    // Runs on every kill in every game, so bail out early
    if (runs.IsEmpty || e.VictimIsAvatar)
        return;

    Region region = e.Victim.Region;
    if (region != null && runs.TryGetValue(region, out Run run) && run.Ended == false)
        BossDown(run, e.VictimId, true);
});

Log.Info($"Midtown Boss Rush loaded ({Waves.Count} waves, {AllBosses.Count} bosses). Starts when the first player enters Midtown Patrol.");

//------------------------------------------------------------------------------
// Types
//------------------------------------------------------------------------------

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

class Wave
{
    public int Affixes { get; }
    public Boss[] Bosses { get; }

    public Wave(int affixes, params Boss[] bosses)
    {
        Affixes = affixes;
        Bosses = bosses;
    }
}

class Spot
{
    public string Name { get; }
    public Vector3 Position { get; }

    public Spot(string name, float x, float y, float z)
    {
        Name = name;
        Position = new Vector3(x, y, z);
    }

    public override string ToString() => string.IsNullOrEmpty(Name) ? Position.ToString() : Name;
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

class Run
{
    public Region Region;
    public ulong StarterDbId;
    public string StarterName;
    public int WaveIndex = -1;
    public int WaveTotal;
    public int WaveDefeated;
    public float EmptySeconds;
    public bool Ended;
    public Dictionary<ulong, int> Alive = new();   // boss entity id => index into AllBosses
    public Dictionary<ulong, long> WaveDamage = new();      // player db id => boss damage this wave
    public Dictionary<ulong, long> RunDamage = new();       // player db id => boss damage this rush
    public Dictionary<ulong, string> PlayerNames = new();   // for the damage summary
    public Dictionary<ulong, Dictionary<ulong, long>> BossDamage = new(); // boss entity id => player db id => damage
    public Event<AdjustHealthGameEvent>.Action DamageAction;
}
