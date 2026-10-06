// Community Goal: everyone on the server works toward one shared total. Each stage the server reaches unlocks rewards for
// everyone who has contributed at least MinContribution.
//
// Set up as the Halloween Candy Hunt: every Halloween Candy a player picks up from the ground counts (the candy comes from
// events/halloween.csx: enemy drops and Trick or Treats). Set CountItem to "" for a kill goal instead (every enemy killed).
//
//  - Contributions count from every zone and instance. The total, each player's count and what they have collected are
//    saved to Data/ScriptData/<StoreName>.tsv, so restarts and script reloads keep the progress.
//  - When a stage unlocks, each contributor gets its rewards dropped at their feet: right away if they are playing,
//    otherwise the next time they log in or contribute. Players who reach MinContribution later still collect every
//    stage that is already unlocked.
//  - The goal ends when the last stage unlocks.
//  - Every contribution also scores on a leaderboard in the game's Leaderboards panel (see Leaderboard below).
//
//   !goal             progress, your contribution and what you can collect
//   !goal top         the top contributors
//   !goal add <n>     (admin) add to the server total (for testing)
//   !goal reset       (admin) wipe all progress and contributions
//   !goal mykills <n> (admin) set your own contribution (to test collecting rewards)
//   !goal npc here    (admin) move Beast to you and print the position to paste into NpcPosition
//
// Progress window: click Beast in Avengers Tower. It is the game's own community event screen (the BiFrost Unlock one):
// one bar with the server's progress toward the final stage, the stages and their targets listed below it, and the top
// contributors.
//
// NOTE: on-screen text registered here reaches players the next time they connect (client limitation).

using System.Collections.Concurrent;
using System.Threading;

//------------------------------------------------------------------------------
// Settings
//------------------------------------------------------------------------------

const bool   Enabled         = true;
const string GoalName        = "Halloween Candy Hunt";
const string StoreName       = "halloween_candy_hunt";   // save file name (Data/ScriptData/<name>.tsv); change it to start a new goal

// What counts. CountItem = an item path: every one of that item a player picks up from the ground counts (here: Halloween
// Candy, which events/halloween.csx drops from enemies and Trick or Treats). CountItem = "": every enemy killed counts.
const string CountItem       = "Entity/Items/CurrencyItems/SeasonalLE/Seasonal/HalloweenCandy.prototype";
const string Unit            = "candy";            // the word used for what is counted ("kills" for a kill goal)
const int    MinContribution = 50;                 // how many a player must contribute before they collect stage rewards
const float  SaveSeconds     = 60f;                // progress is written to disk this often (and at every unlock)
const bool   CountOnlyRanked = false;              // kill goal only: true = only champions, elites and bosses count

// Progress window: Beast stands in Avengers Tower. His vendor window is the game's own "BiFrost Unlock" community event
// screen (one progress bar per stage, an overall bar and the top contributors), relabelled for this goal.
const bool   ShowProgressNpc = true;
const string HubRegion       = "Regions/HUBRevamp/NPEAvengersTowerHUBRegion.prototype";
const string NpcPath         = "Entity/Characters/Vendors/Prototypes/CH09Asgard/BeastGlobalVendor.prototype";
Vector3? NpcPosition         = null;   // null = next to where players arrive in the hub; use "!goal npc here" to pick a spot
float NpcYawDegrees          = 0f;
const int    TopListLength   = 50;     // contributors shown in the window

// Leaderboard: every contribution also scores on this board in the game's Leaderboards panel ("" = none). The client can
// only show boards it has data for, so this reuses the Summer Event one (a 4 hour tournament, restarting every 4 hours),
// renamed here. Its four prize boxes are refilled with Halloween items by Data/Game/Patches/PatchDataHalloween.json.
// The board must be enabled in Data/Leaderboards/LeaderboardSchedule.json.
const string Leaderboard     = "Leaderboards/Prototypes/Leaderboards/Events/SummerEvent.prototype";
const string LeaderboardName = "Halloween Candy Hunt";

const string Items = "Entity/Items/";
const string FC    = Items + "Consumables/Prototypes/FortuneCard/";

// Stages: the server total needed, a name, and the rewards each contributor gets (one copy each, dropped at their feet).
// Scale the totals to your population. With events/halloween.csx's drop rates a player picks up very roughly one candy
// per 20 kills plus Trick or Treat payouts, so these suit a few dozen players over several days.
var Stages = new Stage[]
{
    new(2_000, "Stage 1",
        new(Items + "Consumables/Prototypes/FortuneCard/MysteryBox/HalloweenMysteryBox.prototype", 10),
        new(FC + "OdinsBountyFortuneCard.prototype", 3)),

    new(8_000, "Stage 2",
        new(Items + "Artifacts/HalloweenVisualPumpkinPotion1.prototype"),   // Happy Pumpkinification Potion (re-usable)
        new(Items + "Consumables/Prototypes/RandomGiftboxes/RandomCosmicArtifactBox.prototype"),
        new(Items + "CharacterTokens/Prototypes/TeamUps/WolverineBrood.prototype")),                    // Wolverine (Brood) team-up

    new(25_000, "Stage 3",
        new(Items + "Gems/Gem1.prototype"),                                 // StarkTech Power Cube
        new(Items + "Artifacts/HalloweenVisualGroundPumpkin.prototype"),    // Jack O' Lantern Visual
        new(Items + "Consumables/Prototypes/FortuneCard/MysteryBox/HalloweenMysteryBox.prototype", 20),
        new(Items + "CharacterTokens/Prototypes/TeamUps/FrankenCastle.prototype")),                     // Franken-Castle team-up

    new(60_000, "Final Stage",
        new(Items + "Pets/Pet013StashSummon.prototype"),                    // STASH Access Card (portable stash)
        new(Items + "Gems/Gem1.prototype", 2),
        new(Items + "Consumables/Prototypes/RandomGiftboxes/RandomCosmicArtifactBox.prototype", 3),
        new(Items + "CharacterTokens/Prototypes/TeamUps/RachelColeAlvez.prototype"),                  // Rachel Alves team-up (the Day 900 login reward)
        new(Items + "Armor/UniquePrototypes/Avatars/AnyHero/Slot1/Unique291Lvl60.prototype", 1, 75)),   // The Doomsaw unique (the rare drop) at item level 75, the max
};

bool CountsItems = CountItem.Length > 0;
PrototypeId countItemRef = CountsItems ? GameDatabase.GetPrototypeRefByName(CountItem) : PrototypeId.Invalid;
if (CountsItems && countItemRef == PrototypeId.Invalid)
    Log.Warn($"{GoalName}: CountItem not found: {CountItem}");

//------------------------------------------------------------------------------
// On-screen text (registered at load)
//------------------------------------------------------------------------------

for (int i = 0; i < Stages.Length; i++)
    ScriptText.Register($"goal_stage_{i}", $"{GoalName}: {Stages[i].Name} unlocked!");

foreach (Stage stage in Stages)
{
    foreach (Reward reward in stage.Rewards)
    {
        if (ScriptRewards.IsValidItem(reward.Path) == false)
            Log.Warn($"{stage.Name}: reward item not found: {reward.Path}");
    }
}

if (Enabled && Leaderboard.Length > 0)
{
    ScriptLeaderboards.TakeOver(Leaderboard);   // only this goal's contributions count (not the Summer Event's own rules)
    ScriptLeaderboards.Rename(Leaderboard, LeaderboardName,
        $"Collect the most Halloween {Unit} before the tournament ends!",
        $"This leaderboard tracks how much Halloween {Unit} you pick up. A new tournament starts every 4 hours.\n\n" +
        "1st Place Prize - 25 Halloween Pumpkins, 2 Halloween Mystery Bags, 1 Random Cosmic Artifact Box\n\n" +
        "Top 10% Prize - 20 Halloween Pumpkins, 1 Halloween Mystery Bag, 2 Odin's Bounty Fortune Cards\n\n" +
        "Top 50% Prize - 10 Halloween Pumpkins, 2 Odin's Bounty Fortune Cards\n\n" +
        "Placement Prize - 5 Halloween Pumpkins, 1 Odin's Bounty Fortune Card");

    // The Summer Tournament prize boxes: name and tooltip (their contents are changed by PatchDataHalloween.json)
    const string BoxIntro = "This box contains the following rewards:\n\n";
    const string BoxOutro = "\n\nUse this item to receive your reward!";
    var boxTexts = new (ulong NameId, ulong TooltipId, string Name, string Tooltip)[]
    {
        (11337184009512813881UL, 13119396004900373865UL, "First Place Rewards",
            "Congratulations on placing first in this Tournament!  " + BoxIntro +
            "25 Halloween Pumpkins\n2 Halloween Mystery Bags\n1 Random Cosmic Artifact Box" + BoxOutro),
        (2777989597966632257UL, 3331739727202092398UL, "Top 10% Rewards",
            "Congratulations on placing top 10% in this Tournament!  " + BoxIntro +
            "20 Halloween Pumpkins\n1 Halloween Mystery Bag\n2 Odin's Bounty Fortune Cards" + BoxOutro),
        (13887295604287341885UL, 16644860239845393776UL, "Top 50% Rewards",
            "Congratulations on placing in the top 50% in this Tournament!  " + BoxIntro +
            "10 Halloween Pumpkins\n2 Odin's Bounty Fortune Cards" + BoxOutro),
        (6382822569381987644UL, 57032935884064108UL, "Placement Rewards",
            "Congratulations on placing in this Tournament!  " + BoxIntro +
            "5 Halloween Pumpkins\n1 Odin's Bounty Fortune Card" + BoxOutro),
    };

    foreach (var box in boxTexts)
    {
        ScriptText.OverrideText((LocaleStringId)box.NameId, $"{LeaderboardName} - {box.Name}");
        ScriptText.OverrideText((LocaleStringId)box.TooltipId, box.Tooltip);
    }
}

//------------------------------------------------------------------------------
// State (shared by every game instance, so everything here is thread safe)
//------------------------------------------------------------------------------

long total = 0;         // server-wide kills
int unlocked = 0;        // number of stages unlocked
int dirty = 0;           // 1 = there is unsaved progress
int saving = 0;          // 1 = a save is in progress
long lastSaveTicks = DateTime.UtcNow.Ticks;
object unlockLock = new();

var contributors = new ConcurrentDictionary<ulong, Contributor>();   // player database id => contribution

void LoadProgress()
{
    Dictionary<string, string> data = ScriptStorage.Load(StoreName);

    if (data.TryGetValue("total", out string totalText) && long.TryParse(totalText, out long savedTotal))
        total = savedTotal;

    if (data.TryGetValue("unlocked", out string unlockedText) && int.TryParse(unlockedText, out int savedUnlocked))
        unlocked = Math.Clamp(savedUnlocked, 0, Stages.Length);

    foreach (var kvp in data)
    {
        // p:<player id> = kills|claimed stages|name
        if (kvp.Key.StartsWith("p:") == false || ulong.TryParse(kvp.Key.AsSpan(2), out ulong playerId) == false)
            continue;

        string[] parts = kvp.Value.Split('|', 3);
        if (parts.Length < 2 || long.TryParse(parts[0], out long kills) == false || int.TryParse(parts[1], out int claimed) == false)
            continue;

        contributors[playerId] = new Contributor
        {
            Kills = kills,
            Claimed = Math.Clamp(claimed, 0, Stages.Length),
            Name = parts.Length > 2 ? parts[2] : "",
        };
    }
}

void SaveProgress()
{
    // one save at a time; a save that is skipped here is picked up by the next one
    if (Interlocked.CompareExchange(ref saving, 1, 0) != 0)
        return;

    try
    {
        Interlocked.Exchange(ref dirty, 0);
        Interlocked.Exchange(ref lastSaveTicks, DateTime.UtcNow.Ticks);

        var data = new List<KeyValuePair<string, string>>
        {
            new("total", Interlocked.Read(ref total).ToString()),
            new("unlocked", Volatile.Read(ref unlocked).ToString()),
        };

        foreach (var kvp in contributors)
        {
            Contributor c = kvp.Value;
            data.Add(new($"p:{kvp.Key}", $"{Interlocked.Read(ref c.Kills)}|{Volatile.Read(ref c.Claimed)}|{c.Name.Replace('|', ' ')}"));
        }

        if (ScriptStorage.Save(StoreName, data) == false)
            Interlocked.Exchange(ref dirty, 1);
    }
    finally
    {
        Interlocked.Exchange(ref saving, 0);
    }
}

void SaveIfDue()
{
    if (Volatile.Read(ref dirty) == 0)
        return;

    if ((DateTime.UtcNow.Ticks - Interlocked.Read(ref lastSaveTicks)) / (double)TimeSpan.TicksPerSecond >= SaveSeconds)
        SaveProgress();
}

bool IsComplete() => Volatile.Read(ref unlocked) >= Stages.Length;

void Tell(Player player, string text) => ScriptHooks.SendChatMessage(player, $"[{GoalName}] " + text, false);

// Adds kills to the server total and unlocks every stage that it reaches
void AddToTotal(long amount)
{
    long newTotal = Interlocked.Add(ref total, amount);
    Interlocked.Exchange(ref dirty, 1);

    if (IsComplete() || newTotal < Stages[Volatile.Read(ref unlocked)].Kills)
        return;

    bool unlockedAny = false;
    lock (unlockLock)
    {
        while (unlocked < Stages.Length && Interlocked.Read(ref total) >= Stages[unlocked].Kills)
        {
            Log.Info($"{GoalName}: {Stages[unlocked].Name} unlocked at {Stages[unlocked].Kills:N0} {Unit}");
            Interlocked.Increment(ref unlocked);
            unlockedAny = true;
        }
    }

    if (unlockedAny)
        SaveProgress();
}

// Gives a player every unlocked stage they have not collected yet. Called on the player's own game thread.
void Collect(Player player, Contributor c)
{
    int nowUnlocked = Volatile.Read(ref unlocked);
    if (Volatile.Read(ref c.Claimed) >= nowUnlocked)
        return;

    if (Interlocked.Read(ref c.Kills) < MinContribution)
    {
        // Tell them once per stage that there is something waiting
        if (c.ToldAbout < nowUnlocked)
        {
            c.ToldAbout = nowUnlocked;
            Tell(player, $"{Stages[nowUnlocked - 1].Name} is unlocked! Contribute {MinContribution} {Unit} to collect the rewards " +
                $"(you have {Interlocked.Read(ref c.Kills)}).");
        }
        return;
    }

    Avatar avatar = player.CurrentAvatar;
    if (avatar == null || avatar.IsInWorld == false)
        return;   // try again on their next kill or when they enter the world

    while (true)
    {
        int stageIndex = Volatile.Read(ref c.Claimed);
        if (stageIndex >= nowUnlocked)
            break;

        // Claim the stage first so two threads can never pay it out twice
        if (Interlocked.CompareExchange(ref c.Claimed, stageIndex + 1, stageIndex) != stageIndex)
            continue;

        Stage stage = Stages[stageIndex];
        for (int i = 0; i < stage.Rewards.Length; i++)
            ScriptRewards.DropItem(player, stage.Rewards[i].Path, stage.Rewards[i].Count, i + stageIndex * 4, stage.Rewards[i].ItemLevel);

        ScriptText.ShowBanner(player, $"goal_stage_{stageIndex}", 0, "rewardlarge", 4500);
        Tell(player, $"{stage.Name} unlocked at {stage.Kills:N0} {Unit} server-wide. Your rewards dropped at your feet. Thank you for your {Interlocked.Read(ref c.Kills):N0}!");
    }

    Interlocked.Exchange(ref dirty, 1);
    SaveProgress();
}

Contributor GetContributor(Player player)
{
    Contributor c = contributors.GetOrAdd(player.DatabaseUniqueId, _ => new Contributor());
    if (c.Name.Length == 0)
        c.Name = player.GetName();
    return c;
}

LoadProgress();

//------------------------------------------------------------------------------
// Hooks
//------------------------------------------------------------------------------

// Adds a player's contribution (kills, or items picked up) to the goal, then hands out anything they can collect
void Contribute(Player player, long amount)
{
    Contributor c = GetContributor(player);

    // The leaderboard keeps running after the goal is complete
    if (amount > 0 && Leaderboard.Length > 0)
        ScriptLeaderboards.Submit(player, Leaderboard, amount);

    if (IsComplete() == false && amount > 0)
    {
        Interlocked.Add(ref c.Kills, amount);
        AddToTotal(amount);

        // Progress note in chat each time the server passes another tenth of the current stage
        int stageIndex = Volatile.Read(ref unlocked);
        if (stageIndex < Stages.Length)
        {
            long from = stageIndex == 0 ? 0 : Stages[stageIndex - 1].Kills;
            long now = Interlocked.Read(ref total);
            int tenth = stageIndex * 10 + (int)Math.Clamp((now - from) * 10 / Math.Max(Stages[stageIndex].Kills - from, 1), 0, 9);
            if (tenth > c.TenthSeen)
            {
                bool first = c.TenthSeen < 0;
                c.TenthSeen = tenth;
                if (first == false)
                    Tell(player, $"{now:N0} / {Stages[stageIndex].Kills:N0} {Unit} toward {Stages[stageIndex].Name}.");
            }
        }
    }

    Collect(player, c);
    SaveIfDue();
}

// Kill goal: every enemy a player kills counts
Hooks.On(ScriptHooks.EntityKilled, e =>
{
    if (Enabled == false || CountsItems || e.VictimIsAvatar || e.Victim is not Agent)
        return;

    Player killer = e.Killer?.GetOwnerOfType<Player>();
    if (killer == null)
        return;

    if (CountOnlyRanked && IsComplete() == false)
    {
        RankPrototype rank = e.Victim.GetRankPrototype();
        if (rank == null || (rank.IsRankBoss == false && rank.IsRankChampionOrEliteOrMiniBoss == false))
            return;
    }

    Contribute(killer, 1);
});

// Item goal: every CountItem picked up from the ground counts, once. The item is made to bind on pickup (it cannot be
// traded, and dropping it destroys it), and anything a player dropped is ignored when it is picked up again.
if (CountsItems)
    ScriptRewards.BindOnPickup(CountItem);

Hooks.On(ScriptHooks.ItemPickedUp, e =>
{
    if (Enabled == false || CountsItems == false || e.ItemRef != countItemRef)
        return;

    // Only fresh finds count: an item a player dropped and picked up again (their own or someone else's) does not
    if (e.WasDroppedByPlayer)
        return;

    Contribute(e.Player, e.Count);
});

// Offline contributors collect when they come back
Hooks.On(ScriptHooks.AvatarEnteredWorld, e =>
{
    if (Enabled == false || e.Player == null)
        return;

    if (contributors.TryGetValue(e.Player.DatabaseUniqueId, out Contributor c))
        Collect(e.Player, c);
});

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "goal")
        return;

    e.Handled = true;
    Player player = e.Player;
    string sub = e.GetArg(0).ToLowerInvariant();

    if (sub == "npc")
    {
        if (ScriptHooks.IsAdmin(player) == false) { e.Reply("Admin only."); return; }
        Avatar me = player.CurrentAvatar;
        Region hub = me?.Region;
        if (hub == null || hub.PrototypeDataRef != hubRef) { e.Reply("Go to Avengers Tower first."); return; }

        float yaw = MathF.Atan2(me.Forward.Y, me.Forward.X) * 180f / MathF.PI + 180f;   // facing you
        Vector3 spot = me.RegionLocation.Position + me.Forward * 120f;
        EnsureHubNpc(hub, spot, yaw);
        e.Reply("Beast moved (until restart). To keep him there, set in community_goal.csx:");
        e.Reply($"Vector3? NpcPosition = new Vector3({spot.X:0}f, {spot.Y:0}f, {spot.Z:0}f); float NpcYawDegrees = {yaw:0}f;");
        return;
    }

    // Testing helper: your own contribution
    if (sub == "mykills")
    {
        if (ScriptHooks.IsAdmin(player) == false) { e.Reply("Admin only."); return; }
        if (long.TryParse(e.GetArg(1), out long kills) == false || kills < 0) { e.Reply("Usage: !goal mykills <number>"); return; }
        Contributor me = GetContributor(player);
        Interlocked.Exchange(ref me.Kills, kills);
        Interlocked.Exchange(ref dirty, 1);
        e.Reply($"Your contribution set to {kills:N0} {Unit}.");
        Collect(player, me);
        return;
    }

    if (sub == "add" || sub == "reset")
    {
        if (ScriptHooks.IsAdmin(player) == false) { e.Reply("Admin only."); return; }

        if (sub == "reset")
        {
            lock (unlockLock)
            {
                Interlocked.Exchange(ref total, 0);
                Interlocked.Exchange(ref unlocked, 0);
                contributors.Clear();
            }
            SaveProgress();
            e.Reply("Community goal reset: total, stages and all contributions wiped.");
            return;
        }

        if (long.TryParse(e.GetArg(1), out long amount) == false || amount <= 0) { e.Reply("Usage: !goal add <amount>"); return; }
        AddToTotal(amount);
        e.Reply($"Added {amount:N0}. Total {Interlocked.Read(ref total):N0}, stages unlocked {Volatile.Read(ref unlocked)} / {Stages.Length}.");
        Collect(player, GetContributor(player));
        return;
    }

    if (Enabled == false)
    {
        e.Reply("There is no community goal running.");
        return;
    }

    if (sub == "top")
    {
        var top = contributors.Values.OrderByDescending(c => Interlocked.Read(ref c.Kills)).Take(10).ToList();
        e.Reply($"Top contributors ({contributors.Count} players have taken part):");
        for (int i = 0; i < top.Count; i++)
            e.Reply($"  {i + 1}. {(top[i].Name.Length > 0 ? top[i].Name : "?")}: {Interlocked.Read(ref top[i].Kills):N0}");
        return;
    }

    Contributor mine = GetContributor(player);
    long myKills = Interlocked.Read(ref mine.Kills);
    long serverKills = Interlocked.Read(ref total);
    int stagesUnlocked = Volatile.Read(ref unlocked);

    if (stagesUnlocked >= Stages.Length)
        e.Reply($"Complete! All {Stages.Length} stages unlocked with {serverKills:N0} {Unit} server-wide.");
    else
        e.Reply($"{serverKills:N0} / {Stages[stagesUnlocked].Kills:N0} {Unit} toward {Stages[stagesUnlocked].Name} " +
            $"({stagesUnlocked} of {Stages.Length} stages unlocked).");

    e.Reply(myKills >= MinContribution
        ? $"Your {Unit}: {myKills:N0}. You collect every stage as it unlocks (collected {Volatile.Read(ref mine.Claimed)})."
        : $"Your {Unit}: {myKills:N0}. Reach {MinContribution} to collect stage rewards.");

    Collect(player, mine);
});

//------------------------------------------------------------------------------
// Progress window (Beast's global event vendor screen)
//------------------------------------------------------------------------------

const string BifrostEvent = "Events/GlobalEvents/Events/BiFrostUnlock/BifrostUnlock.prototype";
const string NpcTag = "community_goal";

// The window has one progress bar (the overall bar, 0-100) and a list of eight rows. Tested: the rows never fill, they always
// show a check (in the original event they listed the items it accepted). So the bar shows the server's progress toward
// the final stage, and the rows are labels: the stages with their targets (up to 7), then a line about collecting.
// Row label text ids:
var Bars = new (string Path, ulong NameId)[]
{
    ("Events/GlobalEvents/Events/BiFrostUnlock/Bifrost1Medkits.prototype",      11973406955339646206UL),
    ("Events/GlobalEvents/Events/BiFrostUnlock/Bifrost2Uncommon.prototype",     15361901174224061696UL),
    ("Events/GlobalEvents/Events/BiFrostUnlock/Bifrost3Rare.prototype",         18142266472542110978UL),
    ("Events/GlobalEvents/Events/BiFrostUnlock/Bifrost4Epic.prototype",          3676235467590468868UL),
    ("Events/GlobalEvents/Events/BiFrostUnlock/Bifrost5Cosmic.prototype",       15984159424854295855UL),
    ("Events/GlobalEvents/Events/BiFrostUnlock/Bifrost6Unique.prototype",       12594700934274680113UL),
    ("Events/GlobalEvents/Events/BiFrostUnlock/Bifrost7Ymir.prototype",          7348998779935917326UL),
    ("Events/GlobalEvents/Events/BiFrostUnlock/Bifrost8YmirUpgrade1.prototype",  6333526704284370236UL),
};

int stageBars = Math.Min(Stages.Length, Bars.Length - 1);
int personalBar = Bars.Length - 1;

PrototypeId hubRef = GameDatabase.GetPrototypeRefByName(HubRegion);
PrototypeId npcRef = GameDatabase.GetPrototypeRefByName(NpcPath);
var hubNpcs = new ConcurrentDictionary<Region, ulong>();

if (ShowProgressNpc)
{
    // Relabel the event screen (text changes reach players the next time they connect)
    ScriptText.OverrideText((LocaleStringId)9861826236045853948UL, GoalName);
    for (int i = 0; i < Bars.Length; i++)
    {
        string label = i < stageBars ? $"{Stages[i].Name}: {Stages[i].Kills:N0} {Unit}"
            : i == personalBar ? $"Rewards: collect {MinContribution} {Unit} of your own (type !goal)"
            : "-";
        ScriptText.OverrideText((LocaleStringId)Bars[i].NameId, label);
    }

    string tooltip = CountsItems ? $"{GoalName}: every {Unit} picked up on the server counts. Open to see the progress." : $"{GoalName}: every enemy killed on the server counts. Open to see the progress.";
    ScriptText.OverrideText((LocaleStringId)1090898947771729257UL, tooltip);
    ScriptText.OverrideText((LocaleStringId)2438011274045031787UL, tooltip);
    ScriptText.OverrideText((LocaleStringId)5967260556290950509UL, tooltip);

    if (npcRef == PrototypeId.Invalid) Log.Warn($"Progress NPC not found: {NpcPath}");
}

// The client asks for the numbers when a player opens Beast's window
Hooks.On(ScriptHooks.GlobalEventRequested, e =>
{
    if (Enabled == false || ShowProgressNpc == false || e.EventPath.Equals(BifrostEvent, StringComparison.OrdinalIgnoreCase) == false)
        return;

    // The overall bar: server progress toward the final stage, 0-100
    long serverTotal = Interlocked.Read(ref total);
    float overall = Math.Clamp(serverTotal * 100f / Math.Max(Stages[^1].Kills, 1), 0f, 100f);
    ScriptPresentation.GlobalEventUpdate(e.Player, BifrostEvent, overall, null);

    var top = contributors.Values.Where(c => c.Name.Length > 0).OrderByDescending(c => Interlocked.Read(ref c.Kills))
        .Take(TopListLength).Select(c => c.Name);
    ScriptPresentation.GlobalEventLeaderboard(e.Player, BifrostEvent, top);
});

void EnsureHubNpc(Region hub, Vector3? positionOverride = null, float? yawOverride = null)
{
    if (npcRef == PrototypeId.Invalid)
        return;

    if (positionOverride == null && hubNpcs.TryGetValue(hub, out ulong existingId) && ScriptSpawner.IsAlive(hub.Game, existingId))
        return;

    hubNpcs.TryRemove(hub, out _);

    // Remove every copy in this hub, including one left behind by an earlier load of this script
    foreach (WorldEntity old in hub.Entities.OfType<WorldEntity>().Where(entity => ScriptSpawner.GetTag(entity) == NpcTag).ToList())
        ScriptSpawner.Despawn(hub.Game, old.Id);

    if (Enabled == false || ShowProgressNpc == false)
        return;

    Vector3 position;
    float yaw;

    if (positionOverride.HasValue || NpcPosition.HasValue)
    {
        position = positionOverride ?? NpcPosition.Value;
        yaw = yawOverride ?? NpcYawDegrees;
    }
    else
    {
        if (ScriptSpawner.TryGetStartPosition(hub, out Vector3 start) == false ||
            ScriptSpawner.TryFindSpotNear(hub, start, 450f, 800f, out position) == false)
        {
            Log.Warn("Could not find a spot for Beast in the hub; set NpcPosition (use !goal npc here)");
            return;
        }

        yaw = MathF.Atan2(start.Y - position.Y, start.X - position.X) * 180f / MathF.PI;
    }

    WorldEntity npc = ScriptSpawner.SpawnInteractable(hub, npcRef, position, yaw, NpcTag);
    if (npc == null)
        return;

    // He is a real vendor: leave clicking him to the game's own vendor window instead of the scripted "talk" interaction
    npc.Properties[PropertyEnum.EntSelActHasInteractOption] = false;
    hubNpcs[hub] = npc.Id;
}

Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    if (e.Region.PrototypeDataRef == hubRef)
        EnsureHubNpc(e.Region);
});

Log.Info($"{GoalName} loaded:{Interlocked.Read(ref total):N0} {Unit}, {unlocked} / {Stages.Length} stages unlocked, {contributors.Count} contributors");

//------------------------------------------------------------------------------
// Types
//------------------------------------------------------------------------------

class Reward
{
    public string Path { get; }
    public int Count { get; }
    public int ItemLevel { get; }   // 0 = the hero's level

    public Reward(string path, int count = 1, int itemLevel = 0)
    {
        Path = path;
        Count = count;
        ItemLevel = itemLevel;
    }
}

class Stage
{
    public long Kills { get; }
    public string Name { get; }
    public Reward[] Rewards { get; }

    public Stage(long kills, string name, params Reward[] rewards)
    {
        Kills = kills;
        Name = name;
        Rewards = rewards;
    }
}

class Contributor
{
    public long Kills;          // this player's kills toward the goal
    public int Claimed;         // number of stages they have collected
    public string Name = "";
    public int TenthSeen = -1;  // last progress note they were shown
    public int ToldAbout;       // last stage they were told is waiting for them
}
