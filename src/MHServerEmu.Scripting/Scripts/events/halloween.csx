// Halloween: Trick or Treat. A server-wide seasonal event built from the game's own Halloween content (candy, pumpkins,
// pumpkinification potions, the Halloween Mystery Bag, the Jack O' Lantern visuals).
//
//  1. Candy and pumpkins: every enemy a player kills has a chance to drop Halloween Candy (and, more rarely, a Halloween
//     Pumpkin) for the killer. Tougher enemies drop more often.
//  2. Trick or Treat: when a boss dies, everyone nearby gets a Trick or a Treat.
//       Treat        candy and pumpkins for everyone, and one Trick or Treat stack each.
//       Super Treat  a jackpot (SuperTreatRewards).
//       Trick        a Halloween villain bursts out with a pack of demons. Defeat it for a Treat.
//     Every TreatsForJackpot stacks pay out the game's Halloween loot explosion (JackpotLootTable) plus a Mystery Bag.
//  3. Exchanges: NPCs in Avengers Tower trade event items for rewards (Exchanges below): Ghost trades candy for a
//     Halloween Mystery Bag, Clea trades pumpkins for a Chest of 50 Event Currencies. Click
//     him for the trade popup ("Trade 1" / "Trade all"). Candy and pumpkins also still sell to any vendor for credits.
//
//   !halloween             your candy, pumpkins and Trick or Treat stacks
//   !halloween trick       (admin) force a Trick where you stand
//   !halloween treat       (admin) force a Treat for you
//   !halloween npc <key>   (admin) move that exchange's NPC to you (key = candy or pumpkin) and print the position to keep
//
// NOTE: on-screen text registered here reaches players the next time they connect (client limitation).

using System.Collections.Concurrent;
using MHServerEmu.Games.Entities.Avatars;

//------------------------------------------------------------------------------
// Settings
//------------------------------------------------------------------------------

const bool Enabled = true;
DateTime? StartsUtc = null;   // e.g. new DateTime(2026, 10, 15); null = no start limit
DateTime? EndsUtc   = null;   // e.g. new DateTime(2026, 11, 3);  null = no end limit

const string Candy   = "Entity/Items/CurrencyItems/SeasonalLE/Seasonal/HalloweenCandy.prototype";
const string Pumpkin = "Entity/Items/CurrencyItems/SeasonalLE/Seasonal/HalloweenPumpkin.prototype";

// Exchange NPCs (the list itself is under Rewards below)
const bool   ExchangeEnabled = true;
const int    MaxPerTrade     = 20;     // "Trade all" hands out at most this many rewards at once
const string HubRegion       = "Regions/HUBRevamp/NPEAvengersTowerHUBRegion.prototype";   // the current Avengers Tower
// Chance per kill (0-1) for the killer, by enemy rank
const float CandyChanceNormal   = 0.03f;
const float CandyChanceElite    = 0.20f;   // champions, elites, mini-bosses
const float PumpkinChanceNormal = 0.005f;
const float PumpkinChanceElite  = 0.06f;

// Trick or Treat (boss kills)
const float TrickChance        = 0.30f;
const float SuperTreatChance   = 0.08f;    // the rest are Treats
const float TrickOrTreatRadius = 3500f;    // players this close to the dead boss take part
const float RegionCooldown     = 45f;      // seconds between Trick or Treats in one region (boss-heavy modes)
const int   TreatsForJackpot   = 5;
const int   TrickDemons        = 6;
const int   TrickBossAffixes   = 1;
const string AffixExclude      = "";

const string Villain = "Entity/Characters/Bosses/PatrolMidtown/MidtownEvent";
const string Mobs    = "Entity/Characters/Mobs/";

// One of these bursts out on a Trick
var TrickBosses = new (string Name, string Path)[]
{
    ("Green Goblin", Villain + "GreenGoblin.prototype"),
    ("Venom",        Villain + "Venom.prototype"),
    ("Lizard",       Villain + "Lizard.prototype"),
    ("Tombstone",    Villain + "Tombstone.prototype"),
};

// ...with a pack of these
var TrickDemonPaths = new[]
{
    Mobs + "NGarai/Patrol/DrStrangeLesserNGarai.prototype",
    Mobs + "NGarai/Patrol/DrStrangeLesserNGarai.prototype",
    Mobs + "NGarai/Patrol/DrStrangeGreaterNGarai.prototype",
    Mobs + "LimboDemons/DrStrangeEventDemonSpitterLimboZone.prototype",
};

//------------------------------------------------------------------------------
// Rewards (dropped at each player's feet, one copy each)
//------------------------------------------------------------------------------

const string Items = "Entity/Items/";
const string MysteryBag = Items + "Consumables/Prototypes/FortuneCard/MysteryBox/HalloweenMysteryBox.prototype";

var TreatRewards = new Reward[]
{
    new(Candy, 4),
    new(Pumpkin, 1),
};

var SuperTreatRewards = new Reward[]
{
    new(Candy, 12),
    new(Pumpkin, 4),
    new(MysteryBag),
};

// Every TreatsForJackpot Trick or Treat stacks: the original Halloween loot explosion table (12+ Rare/Epic/Cosmic armor, an Epic
// insignia, chances at rings, uniques and a Cosmic costume core, credits, XP orbs, 6 candy and 6 pumpkins, and its own
// "Trick or Treat" banner), plus JackpotRewards.
const string JackpotLootTable = "Loot/Tables/RandomGiftboxes/DailyGift/HalloweenLootExplosion.prototype";

var JackpotRewards = new Reward[]
{
    new(MysteryBag),
};

// Exchange NPCs in Avengers Tower: each trades Price of one event item for one reward. The trade is a popup ("Trade 1" /
// "Trade all"): the game's vendor window cannot show an event item price.
// The NPC must be one the client has a model for (Ghost, Clea, DoctorStrange, Mordo and HubNPCs/Magik work).
// Position = null puts the NPC next to where players arrive; use "!halloween npc <key>" to pick a spot and keep it.
var Exchanges = new Exchange[]
{
    new("candy", "Ghost", "Entity/Characters/NPCs/Ghost.prototype",
        costItem: Candy, costName: "Halloween Candy", price: 50,
        reward: MysteryBag, rewardName: "Halloween Mystery Bag")
    {
        Position = null, YawDegrees = 0f,
    },

    new("pumpkin", "Clea", "Entity/Characters/NPCs/Clea.prototype",
        costItem: Pumpkin, costName: "Halloween Pumpkins", price: 50,
        reward: Items + "Consumables/Prototypes/CSGrant/LoginEventCurrency50Box.prototype", rewardName: "Chest of 50 Event Currencies")
    {
        Position = null, YawDegrees = 0f,
    },
};

//------------------------------------------------------------------------------
// On-screen text (registered at load)
//------------------------------------------------------------------------------

ScriptText.Register("hw_treat", "Trick or Treat... TREAT!");
ScriptText.Register("hw_super", "You hit the treat jackpot! Happy Halloween!");
ScriptText.Register("hw_trick", "Trick or Treat... TRICK!");
ScriptText.Register("hw_trick_down", "Trick defeated. Have a Treat!");
foreach (Exchange exchange in Exchanges)
    ScriptText.Register($"hw_exchange_{exchange.Key}", $"Trick or treat! Bring me {exchange.CostName} and I'll swap it for goodies.\n\n" +
        $"{exchange.Price} {exchange.CostName} = 1 {exchange.RewardName}");
ScriptText.Register("hw_trade_one", "Trade 1");
ScriptText.Register("hw_trade_all", "Trade all");

for (int i = 0; i < TrickBosses.Length; i++)
    ScriptText.RegisterRange("hw_trick_boss", $"TRICK! {TrickBosses[i].Name} crashes the party!", i, i);

//------------------------------------------------------------------------------
// State (shared by every game instance, so everything here is thread safe)
//------------------------------------------------------------------------------

var treatStacks  = new ConcurrentDictionary<ulong, int>();        // player db id => Trick or Treat stacks
var lastRoll     = new ConcurrentDictionary<ulong, TimeSpan>();   // region id => game time of its last Trick or Treat
var trickBossIds = new ConcurrentDictionary<ulong, byte>();       // entity ids of Trick bosses (their death pays a Treat)
var trickAddIds  = new ConcurrentDictionary<ulong, byte>();       // entity ids of Trick demons (they drop nothing extra)
var hubNpcs      = new ConcurrentDictionary<(Region Hub, string Key), ulong>();   // hub instance + exchange => its NPC

PrototypeId hubRef = GameDatabase.GetPrototypeRefByName(HubRegion);

foreach (Exchange exchange in Exchanges)
{
    if (GameDatabase.GetPrototypeRefByName(exchange.NpcPath) == PrototypeId.Invalid)
        Log.Warn($"Exchange NPC not found: {exchange.NpcPath}");
    if (ScriptRewards.IsValidItem(exchange.CostItem) == false || ScriptRewards.IsValidItem(exchange.Reward) == false)
        Log.Warn($"Exchange [{exchange.Key}]: item not found ({exchange.CostItem} / {exchange.Reward})");
}
if (ScriptRewards.IsValidItem(MysteryBag) == false)
    Log.Warn($"Halloween Mystery Bag not found: {MysteryBag}");

PrototypeId[] trickBossRefs = TrickBosses.Select(boss => ScriptSpawner.FindAgent(boss.Path)).ToArray();
PrototypeId[] trickDemonRefs = TrickDemonPaths.Select(path => GameDatabase.GetPrototypeRefByName(path))
    .Where(protoRef => protoRef != PrototypeId.Invalid).ToArray();

for (int i = 0; i < TrickBosses.Length; i++)
{
    if (trickBossRefs[i] == PrototypeId.Invalid)
        Log.Warn($"Trick boss not found: {TrickBosses[i].Path}");
}

if (ScriptRewards.IsValidItem(Candy) == false || ScriptRewards.IsValidItem(Pumpkin) == false)
    Log.Warn("Halloween Candy / Pumpkin items not found");

// Candy and pumpkins bind to the account on pickup whatever the server's binding setting is: they cannot be traded, and
// dropping one destroys it
ScriptRewards.BindOnPickup(Candy);
ScriptRewards.BindOnPickup(Pumpkin);
if (ScriptRewards.IsValidLootTable(JackpotLootTable) == false)
    Log.Warn($"Loot explosion table not found: {JackpotLootTable}");

bool IsActive()
{
    if (Enabled == false)
        return false;

    DateTime now = DateTime.UtcNow;
    return (StartsUtc == null || now >= StartsUtc) && (EndsUtc == null || now <= EndsUtc);
}

bool IsAdmin(Player player) => ScriptHooks.IsAdmin(player);

void Tell(Player player, string text) => ScriptHooks.SendChatMessage(player, "[Halloween] " + text, false);

IEnumerable<Player> PlayersNear(Region region, Vector3 position, float radius)
{
    foreach (Player player in new PlayerIterator(region))
    {
        Avatar avatar = player.CurrentAvatar;
        if (avatar != null && avatar.IsInWorld && avatar.Region == region &&
            Vector3.Distance2D(avatar.RegionLocation.Position, position) <= radius)
            yield return player;
    }
}

void Give(Player player, Reward[] rewards)
{
    for (int i = 0; i < rewards.Length; i++)
        ScriptRewards.DropItem(player, rewards[i].Path, rewards[i].Count, i);
}

//------------------------------------------------------------------------------
// Trick or Treat
//------------------------------------------------------------------------------

void Treat(Player player, bool super)
{
    Give(player, super ? SuperTreatRewards : TreatRewards);
    ScriptText.ShowBanner(player, super ? "hw_super" : "hw_treat", 0, "reward", 3500);

    int stacks = treatStacks.AddOrUpdate(player.DatabaseUniqueId, 1, (_, current) => current + 1);
    if (stacks >= TreatsForJackpot)
    {
        treatStacks[player.DatabaseUniqueId] = 0;
        ScriptRewards.DropLootTable(player, JackpotLootTable);   // shows its own banner
        Give(player, JackpotRewards);
        Tell(player, $"{TreatsForJackpot} Trick or Treat stacks: Halloween loot explosion!");
    }
    else
    {
        Tell(player, $"Treat! Trick or Treat stacks: {stacks} / {TreatsForJackpot}.");
    }
}

void Trick(Region region, Vector3 position, List<Player> players)
{
    var available = Enumerable.Range(0, TrickBosses.Length).Where(i => trickBossRefs[i] != PrototypeId.Invalid).ToList();
    if (available.Count == 0)
    {
        foreach (Player player in players)
            Treat(player, false);
        return;
    }

    int bossIndex = available[Random.Shared.Next(available.Count)];
    Avatar target = players.Select(player => player.CurrentAvatar).FirstOrDefault(avatar => avatar != null && avatar.IsDead == false);

    Agent boss = ScriptSpawner.SpawnHostile(region, trickBossRefs[bossIndex], position, 0f, 400f, target, true, true, ignoreCrowds: true);
    if (boss == null)
    {
        foreach (Player player in players)
            Treat(player, false);
        return;
    }

    ScriptSpawner.AddRandomAffixes(boss, TrickBossAffixes, AffixExclude);
    trickBossIds[boss.Id] = 0;

    for (int i = 0; i < TrickDemons && trickDemonRefs.Length > 0; i++)
    {
        PrototypeId demonRef = trickDemonRefs[Random.Shared.Next(trickDemonRefs.Length)];
        Agent demon = ScriptSpawner.SpawnHostile(region, demonRef, position, 150f, 600f, target, false, true);
        if (demon != null)
            trickAddIds[demon.Id] = 0;
    }

    foreach (Player player in players)
    {
        ScriptText.ShowBanner(player, "hw_trick_boss", bossIndex, "alert", 4000);
        Tell(player, $"Trick! {TrickBosses[bossIndex].Name} crashes the party. Defeat them for a Treat.");
    }
}

void TrickOrTreat(Region region, Vector3 position, bool ignoreCooldown = false)
{
    TimeSpan now = region.Game.CurrentTime;
    if (ignoreCooldown == false && lastRoll.TryGetValue(region.Id, out TimeSpan last) && (now - last).TotalSeconds < RegionCooldown)
        return;

    List<Player> players = PlayersNear(region, position, TrickOrTreatRadius).ToList();
    if (players.Count == 0)
        return;

    lastRoll[region.Id] = now;

    float roll = Random.Shared.NextSingle();
    if (roll < TrickChance)
    {
        Trick(region, position, players);
        return;
    }

    bool super = roll < TrickChance + SuperTreatChance;
    foreach (Player player in players)
        Treat(player, super);
}

Hooks.On(ScriptHooks.EntityKilled, e =>
{
    if (e.VictimIsAvatar || IsActive() == false)
        return;

    WorldEntity victim = e.Victim;
    Region region = victim.Region;
    if (region == null)
        return;

    // A Trick boss went down: Treat for everyone around
    if (trickBossIds.TryRemove(victim.Id, out _))
    {
        foreach (Player player in PlayersNear(region, victim.RegionLocation.Position, TrickOrTreatRadius).ToList())
        {
            ScriptText.ShowBanner(player, "hw_trick_down", 0, "reward", 3000);
            Treat(player, false);
        }
        return;
    }

    if (trickAddIds.TryRemove(victim.Id, out _))
        return;

    Player killer = e.Killer?.GetOwnerOfType<Player>();
    if (killer == null)
        return;

    RankPrototype rank = victim.GetRankPrototype();
    if (rank != null && rank.IsRankBoss)
    {
        TrickOrTreat(region, victim.RegionLocation.Position);
        return;
    }

    bool tough = rank != null && rank.IsRankChampionOrEliteOrMiniBoss;
    if (Random.Shared.NextSingle() < (tough ? CandyChanceElite : CandyChanceNormal))
        ScriptRewards.DropItem(killer, Candy, 1, 1);
    if (Random.Shared.NextSingle() < (tough ? PumpkinChanceElite : PumpkinChanceNormal))
        ScriptRewards.DropItem(killer, Pumpkin, 1, 2);
});

//------------------------------------------------------------------------------
// Exchanges
//------------------------------------------------------------------------------

void ShowBalance(Player player)
{
    Tell(player, $"You have {ScriptRewards.CountItems(player, Candy)} Halloween Candy and {ScriptRewards.CountItems(player, Pumpkin)} " +
        $"Halloween Pumpkins. Trick or Treat stacks: {treatStacks.GetValueOrDefault(player.DatabaseUniqueId)} / {TreatsForJackpot}.");
}

// Trades the exchange's cost item for up to maxCount rewards. Returns how many were bought.
int Trade(Player player, Exchange exchange, int maxCount)
{
    if (player.CurrentAvatar == null || player.CurrentAvatar.IsInWorld == false)
        return 0;

    int owned = ScriptRewards.CountItems(player, exchange.CostItem);
    int count = Math.Min(owned / exchange.Price, maxCount);
    if (count <= 0)
    {
        Tell(player, $"1 {exchange.RewardName} costs {exchange.Price} {exchange.CostName}. You have {owned} (in your backpack and general stash).");
        return 0;
    }

    if (ScriptRewards.TakeItems(player, exchange.CostItem, count * exchange.Price) == false)
    {
        Tell(player, "The trade failed. Nothing was taken.");
        return 0;
    }

    ScriptRewards.DropItem(player, exchange.Reward, count);
    Tell(player, $"Traded {count * exchange.Price} {exchange.CostName} for {count} x {exchange.RewardName} (dropped at your feet). " +
        $"{owned - count * exchange.Price} left.");
    return count;
}

void ShowExchange(Player player, Exchange exchange, WorldEntity npc)
{
    ScriptDialog.Show(player, $"hw_exchange_{exchange.Key}", "hw_trade_one", "hw_trade_all", (buyer, button) =>
    {
        if (button == 1)
        {
            // keep the popup open for another trade while they can still afford one
            if (Trade(buyer, exchange, 1) > 0 && ScriptRewards.CountItems(buyer, exchange.CostItem) >= exchange.Price && npc.IsDestroyed == false)
                ShowExchange(buyer, exchange, npc);
        }
        else if (button == 2)
        {
            Trade(buyer, exchange, MaxPerTrade);
        }
    }, npc);
}

void EnsureHubNpc(Region hub, Exchange exchange, Vector3? positionOverride = null, float? yawOverride = null)
{
    PrototypeId npcRef = GameDatabase.GetPrototypeRefByName(exchange.NpcPath);
    if (npcRef == PrototypeId.Invalid)
        return;

    var slot = (hub, exchange.Key);
    bool wanted = ExchangeEnabled && IsActive();
    if (wanted && positionOverride == null && hubNpcs.TryGetValue(slot, out ulong existingId) && ScriptSpawner.IsAlive(hub.Game, existingId))
        return;

    hubNpcs.TryRemove(slot, out _);

    // Remove every copy of this NPC in this hub, including one left behind by an earlier load of this script
    foreach (WorldEntity old in hub.Entities.OfType<WorldEntity>().Where(entity => ScriptSpawner.GetTag(entity) == exchange.Tag).ToList())
        ScriptSpawner.Despawn(hub.Game, old.Id);

    if (wanted == false)
        return;

    Vector3 position;
    float yaw;

    if (positionOverride.HasValue || exchange.Position.HasValue)
    {
        position = positionOverride ?? exchange.Position.Value;
        yaw = yawOverride ?? exchange.YawDegrees;
    }
    else
    {
        if (ScriptSpawner.TryGetStartPosition(hub, out Vector3 start) == false ||
            ScriptSpawner.TryFindSpotNear(hub, start, 350f, 700f, out position) == false)
        {
            Log.Warn($"Could not find a spot for {exchange.NpcName} in the hub; set its Position (use !halloween npc {exchange.Key})");
            return;
        }

        yaw = MathF.Atan2(start.Y - position.Y, start.X - position.X) * 180f / MathF.PI;
    }

    WorldEntity npc = ScriptSpawner.SpawnInteractable(hub, npcRef, position, yaw, exchange.Tag);
    if (npc != null)
        hubNpcs[slot] = npc.Id;
}

Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    if (e.Region.PrototypeDataRef != hubRef)
        return;

    foreach (Exchange exchange in Exchanges)
        EnsureHubNpc(e.Region, exchange);
});

Hooks.On(ScriptHooks.EntityInteracted, e =>
{
    Exchange exchange = Exchanges.FirstOrDefault(x => x.Tag == e.ScriptTag);
    if (exchange == null)
        return;

    ShowBalance(e.Player);
    ShowExchange(e.Player, exchange, e.Entity);
});
//------------------------------------------------------------------------------
// Commands
//------------------------------------------------------------------------------

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "halloween")
        return;

    e.Handled = true;
    Player player = e.Player;
    Avatar avatar = player.CurrentAvatar;
    string sub = e.GetArg(0).ToLowerInvariant();

    if (sub == "npc")
    {
        if (IsAdmin(player) == false) { e.Reply("Admin only."); return; }
        Region hub = avatar?.Region;
        if (hub == null || hub.PrototypeDataRef != hubRef) { e.Reply("Go to Avengers Tower first."); return; }

        Exchange exchange = Exchanges.FirstOrDefault(x => x.Key.Equals(e.GetArg(1), StringComparison.OrdinalIgnoreCase));
        if (exchange == null) { e.Reply($"Usage: !halloween npc <{string.Join(" | ", Exchanges.Select(x => x.Key))}>"); return; }

        float yaw = MathF.Atan2(avatar.Forward.Y, avatar.Forward.X) * 180f / MathF.PI + 180f;   // facing you
        Vector3 spot = avatar.RegionLocation.Position + avatar.Forward * 120f;
        EnsureHubNpc(hub, exchange, spot, yaw);
        e.Reply($"{exchange.NpcName} moved (until restart). To keep them there, set in the [{exchange.Key}] exchange in halloween.csx:");
        e.Reply($"Position = new Vector3({spot.X:0}f, {spot.Y:0}f, {spot.Z:0}f), YawDegrees = {yaw:0}f,");
        return;
    }
    if (sub == "trick" || sub == "treat")
    {
        if (IsAdmin(player) == false) { e.Reply("Admin only."); return; }
        if (avatar?.Region == null) return;

        if (sub == "treat")
            Treat(player, e.GetArg(1).Equals("super", StringComparison.OrdinalIgnoreCase));
        else
            Trick(avatar.Region, avatar.RegionLocation.Position, PlayersNear(avatar.Region, avatar.RegionLocation.Position, TrickOrTreatRadius).ToList());
        return;
    }

    if (IsActive() == false)
    {
        e.Reply("The Halloween event is not running.");
        return;
    }

    ShowBalance(player);
    e.Reply("Enemies drop Halloween Candy and Pumpkins. Boss kills are a Trick or a Treat. " +
        (ExchangeEnabled ? string.Concat(Exchanges.Select(x => $"{x.NpcName} in Avengers Tower trades {x.Price} {x.CostName} for 1 {x.RewardName}. ")) : "") +
        "candy and pumpkins also sell to any vendor for credits.");
});

Log.Info($"Halloween loaded ({(IsActive() ? "active" : "not active")}, {trickBossRefs.Count(r => r != PrototypeId.Invalid)} trick bosses)");

//------------------------------------------------------------------------------
// Types
//------------------------------------------------------------------------------

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

class Exchange
{
    public string Key { get; }          // short name, used in commands
    public string NpcName { get; }
    public string NpcPath { get; }
    public string CostItem { get; }
    public string CostName { get; }
    public int Price { get; }
    public string Reward { get; }
    public string RewardName { get; }

    // Where the NPC stands in the hub (null = next to where players arrive)
    public Vector3? Position { get; set; }
    public float YawDegrees { get; set; }

    // Marks this exchange's NPC in the world ("halloween" for the first one, as before)
    public string Tag { get => Key == "candy" ? "halloween" : "halloween_" + Key; }

    public Exchange(string key, string npcName, string npcPath, string costItem, string costName, int price, string reward, string rewardName)
    {
        Key = key;
        NpcName = npcName;
        NpcPath = npcPath;
        CostItem = costItem;
        CostName = costName;
        Price = price;
        Reward = reward;
        RewardName = rewardName;
    }
}
