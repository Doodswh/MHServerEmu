// Dark Dimension Incursion: Doctor Strange in Avengers Tower sends a party into a hand-built map of Limbo (drawn in Map below,
// built from the Limbo tile set by the RegionGenerating hook) where Kaecilius is tearing the Dark Dimension open.
//
//  0. Click Doctor Strange in the hub and accept. You and your party members standing in the hub enter a private copy of the map.
//     Clea waits where you arrive: click her for the current objective and (once) her protective ward.
//  1. Seal the Rifts: four Dark Dimension portals (P on the map) keep pouring out demons. Destroy them; each one leaves a sealed
//     (crystallized) rift behind. All four share the HUD health bar.
//  2. Attune the Power Nodes: three Mystic Power Nodes (N). Click a node (or stand at it) to start a channel; a Dark Dimension guardian
//     and its demons attack. Keep someone at the node until the channel completes. Each attuned node blesses everyone with the Eye of
//     Agamotto (more damage against bosses, stacking).
//  3. Kaecilius (K): invulnerable while his Mirror Images live. At 66% and 33% health he hides behind new images.
//  4. The Dark Dimension bleeds through: a Mindless Titan. Defeat it and Doctor Strange appears; click him to claim your reward.
//
//   !incursion            status, time left and the distance to the current objective
//   !incursion open       (admin) open an incursion without the NPC
//   !incursion npc here   (admin) move the hub Doctor Strange to you and print the position to paste into NpcPosition
//
// NOTE: on-screen text registered here reaches players the next time they connect (client limitation).

using System.Collections.Concurrent;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Events;
using MHServerEmu.Games.Powers;

//------------------------------------------------------------------------------
// Settings
//------------------------------------------------------------------------------

const string HubRegion     = "Regions/HUBRevamp/NPEAvengersTowerHUBRegion.prototype";   // the current Avengers Tower
const string HostRegion    = "Regions/ZZZDevelopment/WhiteRoom/BlackRoomRegion.prototype";   // empty region the map is built in
const string CellSet       = "EndGame/Limbo/Limbo_A";
const string EventDifficulty = "Difficulty/Tiers/Tier3Superheroic.prototype";   // Cosmic
const int    MapLevel      = 60;

Vector3? NpcPosition       = null;   // null = next to where players arrive in the hub; use "!incursion npc here" to pick a spot
float NpcYawDegrees        = 0f;

// The map: one character per Limbo tile (2304 units). # = solid, S = arrival, P = rift portal, N = power node, K = Kaecilius.
// Keep it open (every room has 2+ neighbors, no dead ends or single straight corridors): Limbo has no tiles for those.
var Map = new[]
{
    "P...N...P",
    ".........",
    "..#...#..",
    ".N..K..N.",
    "....#....",
    ".........",
    "P...S...P",
};

const float TimeLimit          = 1200f;   // seconds
const string TimerWidget       = "UI/MetaGame/DangerRoom/DangerRoomTimer.prototype";
const string HealthWidget      = "UI/MetaGame/SurturRaid/FiveMan/SlagHealth.prototype";
const float StartDelay         = 6f;
const float LoadInTimeout      = 90f;
const float PendingTimeout     = 180f;
const float EmptyRegionTimeout = 120f;
const float ClaimWindow        = 120f;    // after the Titan falls: time to claim the reward from Doctor Strange
const float ReturnDelay        = 30f;     // then everyone is sent back to town after this
const float HintInterval       = 90f;
const float TickSeconds        = 2f;
const string AffixExclude      = "";

// Phase 1: rifts
const float PulseSeconds       = 14f;     // each open rift spits out demons this often
const int   PulseCount         = 3;
const int   MaxAddsPerRift     = 9;

// Phase 2: power nodes
const float AttuneSeconds      = 20f;
const float NodeRadius         = 700f;    // someone must stay this close for the channel to continue
const float NodeAutoStartRadius = 450f;   // standing this close starts the channel (clicking does too)
const float AttuneGrace        = 6f;      // seconds with nobody at the node before the channel breaks
const int   GuardianAffixes    = 2;
const int   NodeAddCount       = 4;
const float EyeBonusPerNode    = 0.08f;   // +8% damage against bosses per attuned node (stacks)
const float WardResist         = 0.10f;   // Clea's ward: 10% damage reduction for the run

// Phase 3: Kaecilius
const int   KaeciliusAffixes   = 3;
const int   MirrorImages       = 3;
var ShieldAt                   = new[] { 0.66f, 0.33f };   // health fractions where he hides behind new images

const int   TitanAffixes       = 3;

// Characters
const string Bosses   = "Entity/Characters/Bosses/DarkDimension/";
const string Mobs     = "Entity/Characters/Mobs/";
const string Props    = "Entity/Props/";
const string NPCs     = "Entity/Characters/NPCs/";

const string StrangePath   = NPCs + "DoctorStrange.prototype";
const string CleaPath      = NPCs + "Clea.prototype";
const string NodePath      = Props + "Missions/MysticPowerNode.prototype";
const string SealedPath    = Props + "Missions/DarkDimensionPortalCrystalized.prototype";
const string KaeciliusPath = Bosses + "KaeciliusDarkDimensionCosmic.prototype";
const string MirrorPath    = Bosses + "KaeciliusMirrorImage.prototype";
const string TitanPath     = Bosses + "DDBossMindlessTitanTerminalCosmic.prototype";

var Rifts = new List<Rift>
{
    new("Fire Rift",     Props + "Destructibles/DarkDimensionPortalFireDemon.prototype",
        Mobs + "FireDemons/Patrol/PatrolMysticMayhemFireBruteSwordSpawn.prototype",
        Mobs + "FireDemons/Limbo/DrStrangeEventFireBruteArcherSpawn.prototype",
        Mobs + "FireDemons/DrStrangeEventWingedMuspelheimDemon.prototype"),
    new("N'Garai Rift",  Props + "Destructibles/DarkDimensionPortalNGarai.prototype",
        Mobs + "NGarai/Patrol/DrStrangeLesserNGarai.prototype",
        Mobs + "NGarai/Patrol/DrStrangeLesserNGarai.prototype",
        Mobs + "NGarai/Patrol/DrStrangeGreaterNGarai.prototype"),
    new("Troll Rift",    Props + "Destructibles/DarkDimensionPortalRockTroll.prototype",
        Mobs + "FireDemons/Patrol/PatrolMysticMayhemFireBruteShieldSpawn.prototype",
        Mobs + "FireDemons/Patrol/PatrolMysticMayhemFireBruteMaulSpawn.prototype",
        Mobs + "LimboDemons/DrStrangeEventDemonSpitterLimboZone.prototype"),
    new("Dark Rift",     Props + "Destructibles/DarkDimensionPortalBase.prototype",
        Mobs + "MindlessOnes/DoctorStrangeEvent/DrStrangeMeleeMindlessOne.prototype",
        Mobs + "MindlessOnes/DoctorStrangeEvent/DrStrangeMeleeMindlessOne.prototype",
        Mobs + "MindlessOnes/DoctorStrangeEvent/DrStrangeMeleeMindlessOneCosmic.prototype"),
};

var Guardians = new List<Foe>
{
    new("the Fire Giant",          Bosses + "DDBossFireGiantCosmic.prototype"),
    new("the Rock Troll Warlord",  Bosses + "DDBossRockTrollWarlordCosmic.prototype"),
    new("the Fire Lobber",         Bosses + "DDBossFireLobberCosmic.prototype"),
};

var NodeAdds = new[]
{
    Mobs + "NGarai/Patrol/DrStrangeLesserNGarai.prototype",
    Mobs + "FireDemons/Patrol/PatrolMysticMayhemFireBruteSwordSpawn.prototype",
    Mobs + "MindlessOnes/DoctorStrangeEvent/DrStrangeMeleeMindlessOne.prototype",
    Mobs + "FireDemons/DrStrangeEventFireGiant.prototype",
};

//------------------------------------------------------------------------------
// Rewards
//------------------------------------------------------------------------------

const string FC = "Entity/Items/Consumables/Prototypes/FortuneCard/";

var RiftRewards = new Reward[]   // everyone who damaged a rift, when all four are sealed
{
    new(FC + "SpiderManHomecomingFortuneCard.prototype"),
    new(FC + "LoganFortuneCard.prototype"),
};

var KaeciliusRewards = new Reward[]   // everyone who damaged Kaecilius or his images
{
    new("Entity/Items/Consumables/Prototypes/CSGrant/CSGrantCrateHeroCommendation25Box.prototype"),
};

var CompletionRewards = new Reward[]   // claimed from Doctor Strange at the end
{
    new("Entity/Items/Gems/Gem1.prototype", 1),   // StarkTech Power Cube (only here, at the end)
    new("Entity/Items/Consumables/Prototypes/RandomGiftboxes/RandomCosmicArtifactBox.prototype"),
    new("Entity/Items/Consumables/Prototypes/DailyGift/LargeRunebox.prototype"),
    new(FC + "OdinsBountyFortuneCard.prototype", 3),
};

//------------------------------------------------------------------------------
// On-screen text (registered at load)
//------------------------------------------------------------------------------

ScriptText.Register("ddi_prompt", "Kaecilius has torn a hole into the Dark Dimension, and Limbo is bleeding through.\n" +
    "Enter with your party: seal the rifts, attune the power nodes and stop Kaecilius before Dormammu's realm swallows us all.");
ScriptText.Register("ddi_enter", "Enter Limbo");
ScriptText.Register("ddi_cancel", "Not yet");
ScriptText.Register("ddi_start", "Dark Dimension Incursion");
ScriptText.Register("ddi_intro", "The rifts are feeding the Dark Dimension. Seal all four before you face Kaecilius.");
ScriptText.Register("ddi_obj_rifts", "Dark Dimension Incursion - Seal the Rifts");
ScriptText.Register("ddi_obj_nodes", "Dark Dimension Incursion - Attune the Power Nodes");
ScriptText.Register("ddi_obj_kaecilius", "Dark Dimension Incursion - Defeat Kaecilius");
ScriptText.Register("ddi_obj_titan", "Dark Dimension Incursion - Destroy the Mindless Titan");
ScriptText.Register("ddi_obj_claim", "Dark Dimension Incursion - Speak with Doctor Strange");
ScriptText.Register("ddi_rift_sealed", "Rift Sealed!");
ScriptText.RegisterRange("ddi_rift_left", "Rift Sealed! {0} left", 1, 4);
ScriptText.Register("ddi_rifts_done", "The rifts are sealed. Now the power nodes: attune all three.");
ScriptText.Register("ddi_node_start", "Attunement started! Hold the node!");
ScriptText.Register("ddi_node_broken", "The attunement broke! Return to the node.");
ScriptText.Register("ddi_node_done", "Power Node Attuned!");
ScriptText.Register("ddi_eye", "The Eye of Agamotto strengthens you.");
ScriptText.Register("ddi_kaecilius_arrives", "Kaecilius Steps Through!");
ScriptText.Register("ddi_shielded", "Kaecilius hides behind his Mirror Images!");
ScriptText.Register("ddi_exposed", "Kaecilius is exposed! Strike now!");
ScriptText.Register("ddi_titan", "A Mindless Titan Breaks Through!");
ScriptText.Register("ddi_complete", "Incursion Repelled!");
ScriptText.Register("ddi_failed", "Limbo Has Fallen");
ScriptText.Register("ddi_overhead", "Top Damage!");
ScriptText.RegisterRange("ddi_attune_pct", "Attuning {0}%", 0, 100);

// Portrait lines (speaker = the character's portrait)
ScriptText.Register("ddi_strange_intro", "The rifts are anchored to Limbo itself. Seal them, and Kaecilius loses his grip on this place.");
ScriptText.Register("ddi_strange_nodes", "Those power nodes still answer to the Sanctum. Hold them long enough and the Eye of Agamotto will lend you its strength.");
ScriptText.Register("ddi_kae_arrive", "You've sealed a few doors. Dormammu has a thousand more. Let me show you what real power looks like.");
ScriptText.Register("ddi_kae_shield", "Which one of us is real? Does it even matter?");
ScriptText.Register("ddi_kae_shield2", "Time is a cage, and I hold the key!");
ScriptText.Register("ddi_kae_down", "Dormammu... I have failed... but he will not.");
ScriptText.Register("ddi_strange_titan", "Kaecilius' death tore the veil open. Something big is coming through. Stop it!");
ScriptText.Register("ddi_strange_end", "Well done. Limbo will hold, for now. Come, take what the Sanctum can offer.");
ScriptText.Register("ddi_strange_claimed", "The Sanctum thanks you. Go home and rest, you've earned it.");
ScriptText.Register("ddi_clea_hint", "Stay close to each other in here. Talk to me any time you lose your way.");
ScriptText.Register("ddi_clea_ward", "Here, take my ward. It will soften what Limbo throws at you.");

//------------------------------------------------------------------------------
// State
//------------------------------------------------------------------------------

var pendingBySeed   = new ConcurrentDictionary<int, Run>();
var pendingByPlayer = new ConcurrentDictionary<ulong, Run>();
var runs            = new ConcurrentDictionary<Region, Run>();
var hubNpcs         = new ConcurrentDictionary<Region, ulong>();

const string HubTag      = "ddi_strange_hub";
const string CleaTag     = "ddi_clea";
const string NodeTagBase = "ddi_node_";
const string StrangeTag  = "ddi_strange_end";
const string SealedTag   = "ddi_sealed";
const string WardKey     = "ddi_ward";
const string EyeKey      = "ddi_eye";

PrototypeId hubRef        = Resolve(HubRegion);
PrototypeId hostRef       = Resolve(HostRegion);
PrototypeId strangeRef    = Resolve(StrangePath);
PrototypeId cleaRef       = Resolve(CleaPath);
PrototypeId nodeRef       = Resolve(NodePath);
PrototypeId sealedRef     = Resolve(SealedPath);
PrototypeId kaeciliusRef  = Resolve(KaeciliusPath);
PrototypeId mirrorRef     = Resolve(MirrorPath);
PrototypeId titanRef      = Resolve(TitanPath);

foreach (Rift rift in Rifts)
{
    rift.Ref = Resolve(rift.Path);
    rift.AddRefs = rift.AddPaths.Select(Resolve).Where(r => r != PrototypeId.Invalid).ToArray();
}
foreach (Foe foe in Guardians)
    foe.Ref = Resolve(foe.Path);
PrototypeId[] nodeAddRefs = NodeAdds.Select(Resolve).Where(r => r != PrototypeId.Invalid).ToArray();

PrototypeId Resolve(string path)
{
    PrototypeId protoRef = GameDatabase.GetPrototypeRefByName(path);
    if (protoRef == PrototypeId.Invalid)
        Log.Warn($"Not found: {path}");
    return protoRef;
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
        ScriptHooks.SendChatMessage(player, "[Incursion] " + text, false);
}

string FormatTime(float seconds)
{
    int total = Math.Max((int)Math.Ceiling(seconds), 0);
    if (total < 60) return $"{total} seconds";
    int minutes = total / 60, rest = total % 60;
    string text = minutes == 1 ? "1 minute" : $"{minutes} minutes";
    return rest == 0 ? text : $"{text} {rest} seconds";
}

// A spot in the room around a map marker that players can walk to from where they arrived (room centers can be lava or cut off).
// If the room has none, a reachable spot elsewhere on the map, so an objective is never stranded.
Vector3 ReachableSpot(Run run, Vector3 near, float maxDistance = 1100f)
{
    Region region = run.Region;
    if (ScriptSpawner.TryFindReachableSpotNear(region, near, maxDistance, run.Entrance, out Vector3 spot))
        return spot;

    Log.Warn($"No reachable ground within {maxDistance:0} of {near}, using another reachable spot");
    if (ScriptSpawner.TryFindRandomSpot(region, out spot, run.Entrance, 2000f) ||
        ScriptSpawner.TryFindRandomSpot(region, out spot, run.Entrance))
        return spot;

    return run.Entrance;
}

//------------------------------------------------------------------------------
// Hub: Doctor Strange
//------------------------------------------------------------------------------

void EnsureHubNpc(Region hub, Vector3? positionOverride = null, float? yawOverride = null)
{
    if (strangeRef == PrototypeId.Invalid)
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
        if (ScriptSpawner.TryGetStartPosition(hub, out Vector3 start) == false ||
            ScriptSpawner.TryFindSpotNear(hub, start, 300f, 600f, out position) == false)
        {
            Log.Warn("Could not find a spot for Doctor Strange in the hub; set NpcPosition (use !incursion npc here)");
            return;
        }

        yaw = MathF.Atan2(start.Y - position.Y, start.X - position.X) * 180f / MathF.PI;
    }

    WorldEntity npc = ScriptSpawner.SpawnInteractable(hub, strangeRef, position, yaw, HubTag);
    if (npc != null)
        hubNpcs[hub] = npc.Id;
}

//------------------------------------------------------------------------------
// Opening an incursion
//------------------------------------------------------------------------------

void ForgetExpiredPending()
{
    DateTime now = DateTime.UtcNow;
    foreach (var pair in pendingBySeed)
        if ((now - pair.Value.CreatedAt).TotalSeconds > PendingTimeout) pendingBySeed.TryRemove(pair.Key, out _);
    foreach (var pair in pendingByPlayer)
        if ((now - pair.Value.CreatedAt).TotalSeconds > PendingTimeout) pendingByPlayer.TryRemove(pair.Key, out _);
}

void OpenIncursion(Player opener)
{
    ForgetExpiredPending();

    var run = new Run { Seed = Random.Shared.Next(1, int.MaxValue), OpenerName = opener.GetName(), CreatedAt = DateTime.UtcNow };

    List<Player> group = ScriptTeleport.GetPartyMembersInRegion(opener);
    pendingBySeed[run.Seed] = run;
    foreach (Player member in group)
        pendingByPlayer[member.DatabaseUniqueId] = run;

    int sent = 0;
    foreach (Player member in group)
    {
        if (ScriptTeleport.ToBuiltMap(member, HostRegion, EventDifficulty, run.Seed))
        {
            sent++;
            ScriptHooks.SendChatMessage(member, $"[Incursion] {opener.GetName()} is taking you into Limbo!", false);
        }
    }

    if (sent == 0)
    {
        pendingBySeed.TryRemove(run.Seed, out _);
        ScriptHooks.SendChatMessage(opener, "[Incursion] The portal failed to open (see the server log).", false);
        return;
    }

    Log.Info($"{opener.GetName()} opened a Dark Dimension Incursion for {sent} player(s), seed {run.Seed}");
}

Run FindRunFor(Player player, Region region)
{
    if (runs.TryGetValue(region, out Run running))
        return running;

    if (region.PrototypeDataRef != hostRef)
        return null;

    if (pendingBySeed.TryGetValue(region.RandomSeed, out Run run))
        return run;

    if (pendingByPlayer.TryGetValue(player.DatabaseUniqueId, out run) && run.Region == null &&
        (DateTime.UtcNow - run.CreatedAt).TotalSeconds <= PendingTimeout)
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
    run.Map = ScriptTeleport.GetBuiltMap(region);
    if (run.Map == null)
    {
        Log.Warn("Incursion region has no built map, ending");
        EndRun(run, false, "The portal collapsed.", true);
        return;
    }

    // Clea next to the arrival point
    if (cleaRef != PrototypeId.Invalid && ScriptSpawner.TryFindReachableSpotNear(region, run.Entrance, 450f, run.Entrance, out Vector3 cleaSpot))
    {
        float yaw = MathF.Atan2(run.Entrance.Y - cleaSpot.Y, run.Entrance.X - cleaSpot.X) * 180f / MathF.PI;
        WorldEntity clea = ScriptSpawner.SpawnInteractable(region, cleaRef, cleaSpot, yaw, CleaTag);
        if (clea != null) run.Helpers.Add(clea.Id);
    }

    ScriptText.ShowBannerToRegion(region, "ddi_start", 0, "large", 4000);
    ScriptPresentation.StoryNotificationToRegion(region, "ddi_strange_intro", StrangePath, 7000);
    Announce(region, $"Seal the four rifts, attune the three power nodes, then stop Kaecilius. You have {FormatTime(TimeLimit)}. " +
        "Talk to Clea for help, or type !incursion for the way to your objective.");

    if (ScriptPresentation.WidgetTimer(region, TimerWidget, TimeLimit) == false)
        Log.Warn("Could not show the incursion timer widget");

    StartRifts(run);

    run.NextHintAt = HintInterval;
    After(region.Game, TickSeconds, () => Tick(run));
}

// Phase 1
void StartRifts(Run run)
{
    Region region = run.Region;
    run.Phase = Phase.Rifts;

    var spots = run.Map.GetMarkers('P');
    for (int i = 0; i < spots.Count && i < Rifts.Count; i++)
    {
        Rift rift = Rifts[i];
        if (rift.Ref == PrototypeId.Invalid)
            continue;

        Agent portal = ScriptSpawner.SpawnHostile(region, rift.Ref, ReachableSpot(run, spots[i]), 0f, 150f, null, true, true, aggroed: false);
        if (portal == null)
            continue;

        run.Targets[portal.Id] = new Target { Kind = Kind.Rift, Name = rift.Name, Position = portal.RegionLocation.Position, RiftIndex = i };
        run.RiftAdds[portal.Id] = new List<ulong>();
        ScriptPresentation.WidgetTrackHealth(region, HealthWidget, portal);
    }

    run.RiftTotal = run.Targets.Count;
    if (run.RiftTotal == 0)
    {
        Log.Warn("No rift portals could be placed, skipping to the power nodes");
        StartNodes(run);
        return;
    }

    run.NextPulseAt = 3f;
    ScriptText.SetObjectiveTitle(region, "ddi_obj_rifts");
    ScriptText.SetObjectiveCounter(region, 0, run.RiftTotal);
}

void PulseRifts(Run run, float elapsed)
{
    if (elapsed < run.NextPulseAt)
        return;

    run.NextPulseAt = elapsed + PulseSeconds;
    Region region = run.Region;
    Avatar target = PlayersIn(region).Select(p => p.CurrentAvatar).FirstOrDefault(a => a.IsDead == false);

    foreach (var pair in run.Targets.Where(pair => pair.Value.Kind == Kind.Rift).ToList())
    {
        List<ulong> adds = run.RiftAdds[pair.Key];
        adds.RemoveAll(id => ScriptSpawner.IsAlive(region.Game, id) == false);

        Rift rift = Rifts[pair.Value.RiftIndex];
        for (int i = 0; i < PulseCount && adds.Count < MaxAddsPerRift && rift.AddRefs.Length > 0; i++)
        {
            PrototypeId addRef = rift.AddRefs[Random.Shared.Next(rift.AddRefs.Length)];
            Agent add = ScriptSpawner.SpawnHostile(region, addRef, pair.Value.Position, 150f, 450f, target, true, true, aggroed: false);
            if (add != null) adds.Add(add.Id);
        }
    }
}

void RiftDown(Run run, ulong entityId, Target target, bool killed)
{
    Region region = run.Region;
    run.RiftsSealed++;

    if (killed)
    {
        ScriptPresentation.WidgetEntityDefeated(region, HealthWidget, entityId);
        After(region.Game, 2f, () => ScriptPresentation.WidgetUntrack(region, HealthWidget, entityId));
    }
    else
    {
        ScriptPresentation.WidgetUntrack(region, HealthWidget, entityId);
    }

    // The sealed (crystallized) rift stays as a landmark. It is only decoration: no interact option, so nobody tries to "use" it
    if (sealedRef != PrototypeId.Invalid)
    {
        WorldEntity sealedRift = ScriptSpawner.SpawnInteractable(region, sealedRef, target.Position, 0f, SealedTag);
        if (sealedRift != null)
        {
            sealedRift.Properties[PropertyEnum.EntSelActHasInteractOption] = false;
            sealedRift.Properties[PropertyEnum.Interactable] = 0;   // tri-state integer: 0 = not interactable
            run.Helpers.Add(sealedRift.Id);
        }
    }

    int riftsLeft = run.RiftTotal - run.RiftsSealed;
    if (riftsLeft > 0)
        ScriptText.ShowBannerToRegion(region, "ddi_rift_left", riftsLeft, "reward", 3000);
    else
        ScriptText.ShowBannerToRegion(region, "ddi_rift_sealed", 0, "reward", 2500);
    ScriptText.SetObjectiveCounter(region, run.RiftsSealed, run.RiftTotal);

    if (run.Targets.Values.Any(t => t.Kind == Kind.Rift))
    {
        Announce(region, $"{target.Name} sealed! {run.RiftTotal - run.RiftsSealed} left.");
        return;
    }

    GiveRewards(run, run.RiftDamage, RiftRewards, "Rifts sealed");
    Announce(region, "All rifts are sealed! Now attune the three Mystic Power Nodes.");
    ScriptText.ShowBannerToRegion(region, "ddi_rifts_done", 0, "large", 3500);
    After(region.Game, 4f, () => StartNodes(run));
}

// Phase 2
void StartNodes(Run run)
{
    if (run.Ended)
        return;

    Region region = run.Region;
    run.Phase = Phase.Nodes;
    ScriptPresentation.ClearWidget(region, HealthWidget);

    var spots = run.Map.GetMarkers('N');
    for (int i = 0; i < spots.Count && nodeRef != PrototypeId.Invalid; i++)
    {
        Vector3 spot = ReachableSpot(run, spots[i]);
        WorldEntity node = ScriptSpawner.SpawnInteractable(region, nodeRef, spot, 0f, NodeTagBase + i);
        if (node == null)
            continue;

        run.Nodes.Add(new Node { Index = i, EntityId = node.Id, Position = node.RegionLocation.Position });
        run.Helpers.Add(node.Id);
    }

    if (run.Nodes.Count == 0)
    {
        Log.Warn("No power nodes could be placed, skipping to Kaecilius");
        StartKaecilius(run);
        return;
    }

    ScriptPresentation.StoryNotificationToRegion(region, "ddi_strange_nodes", StrangePath, 7000);
    ScriptText.SetObjectiveTitle(region, "ddi_obj_nodes");
    ScriptText.SetObjectiveCounter(region, 0, run.Nodes.Count);
}

void StartAttuning(Run run, Node node, Player starter)
{
    if (run.Phase != Phase.Nodes || node.Attuned || node.Attuning)
        return;

    Region region = run.Region;
    node.Attuning = true;
    node.Progress = 0f;
    node.AwaySeconds = 0f;
    ScriptText.ShowBannerToRegion(region, "ddi_node_start", 0, "alert", 2500);
    Announce(region, $"{starter?.GetName() ?? "Someone"} began attuning a power node. Hold it for {AttuneSeconds:0} seconds!");

    // The node's guardian (one per node) and its demons
    if (node.GuardianId == 0 || ScriptSpawner.IsAlive(region.Game, node.GuardianId) == false)
    {
        Foe guardian = Guardians[node.Index % Guardians.Count];
        if (guardian.Ref != PrototypeId.Invalid)
        {
            Avatar target = starter?.CurrentAvatar;
            Agent boss = ScriptSpawner.SpawnHostile(region, guardian.Ref, ReachableSpot(run, node.Position, 800f), 0f, 150f, target, true, true);
            if (boss != null)
            {
                ScriptSpawner.AddRandomAffixes(boss, GuardianAffixes, AffixExclude);
                node.GuardianId = boss.Id;
                run.Targets[boss.Id] = new Target { Kind = Kind.Guardian, Name = guardian.Name, Position = boss.RegionLocation.Position };
                ScriptPresentation.WidgetTrackHealth(region, HealthWidget, boss);
                ScriptText.ShowPortraitNotificationToRegion(region, guardian.Ref, "ddi_node_start", 0, 5f);
            }
        }
    }

    for (int i = 0; i < NodeAddCount && nodeAddRefs.Length > 0; i++)
    {
        Agent add = ScriptSpawner.SpawnHostile(region, nodeAddRefs[Random.Shared.Next(nodeAddRefs.Length)], node.Position, 400f, 900f,
            starter?.CurrentAvatar, true, true);
        if (add != null) run.Extras.Add(add.Id);
    }
}

void UpdateNodes(Run run)
{
    Region region = run.Region;
    var avatars = PlayersIn(region).Select(p => p.CurrentAvatar).Where(a => a.IsDead == false).ToList();

    foreach (Node node in run.Nodes)
    {
        if (node.Attuned)
            continue;

        bool someoneClose = avatars.Any(a => Vector3.Distance2D(a.RegionLocation.Position, node.Position) <= NodeRadius);

        if (node.Attuning == false)
        {
            // Standing right at a node starts the channel too
            Avatar starter = avatars.FirstOrDefault(a => Vector3.Distance2D(a.RegionLocation.Position, node.Position) <= NodeAutoStartRadius);
            if (starter != null)
                StartAttuning(run, node, starter.GetOwnerOfType<Player>());
            continue;
        }

        if (someoneClose)
        {
            node.AwaySeconds = 0f;
            node.Progress += TickSeconds;
        }
        else
        {
            node.AwaySeconds += TickSeconds;
            if (node.AwaySeconds >= AttuneGrace)
            {
                node.Attuning = false;
                node.Progress = 0f;
                ScriptText.ShowBannerToRegion(region, "ddi_node_broken", 0, "error", 2500);
                continue;
            }
        }

        int percent = Math.Clamp((int)(node.Progress * 100f / AttuneSeconds), 0, 100);
        WorldEntity nodeEntity = region.Game.EntityManager.GetEntity<WorldEntity>(node.EntityId);
        if (nodeEntity != null)
            ScriptText.ShowOverheadText(nodeEntity, "ddi_attune_pct", percent, TickSeconds + 0.5f);

        if (node.Progress >= AttuneSeconds)
            NodeAttuned(run, node);
    }
}

void NodeAttuned(Run run, Node node)
{
    Region region = run.Region;
    node.Attuning = false;
    node.Attuned = true;
    run.NodesAttuned++;

    ScriptText.ShowBannerToRegion(region, "ddi_node_done", 0, "reward", 3000);
    ScriptText.SetObjectiveCounter(region, run.NodesAttuned, run.Nodes.Count);

    // Eye of Agamotto: stacking damage bonus against bosses for everyone here
    foreach (Player player in PlayersIn(region))
        ApplyEye(run, player);

    if (run.NodesAttuned < run.Nodes.Count)
    {
        Announce(region, $"Power node attuned! The Eye of Agamotto strengthens you ({run.NodesAttuned} / {run.Nodes.Count}).");
        return;
    }

    Announce(region, "All power nodes are attuned! Kaecilius is coming.");
    After(region.Game, 4f, () => StartKaecilius(run));
}

void ApplyEye(Run run, Player player)
{
    Avatar avatar = player.CurrentAvatar;
    if (avatar == null || run.NodesAttuned <= 0)
        return;

    var bonus = new PropertyCollection();
    bonus[PropertyEnum.DamagePctBonusVsBosses] = EyeBonusPerNode * run.NodesAttuned;
    ScriptBonuses.Remove(avatar, EyeKey);
    ScriptBonuses.Apply(avatar, EyeKey, bonus);
    ScriptText.ShowOverheadText(avatar, "ddi_eye", 0, 3f);
}

void ApplyWard(Player player)
{
    Avatar avatar = player.CurrentAvatar;
    if (avatar == null || ScriptBonuses.Has(avatar, WardKey))
        return;

    var bonus = new PropertyCollection();
    bonus[PropertyEnum.DamagePctResist, DamageType.Any] = WardResist;
    ScriptBonuses.Apply(avatar, WardKey, bonus);
}

void RemoveBuffs(Avatar avatar)
{
    if (avatar == null) return;
    ScriptBonuses.Remove(avatar, EyeKey);
    ScriptBonuses.Remove(avatar, WardKey);
}

// Phase 3
void StartKaecilius(Run run)
{
    if (run.Ended || run.Phase == Phase.Kaecilius)
        return;

    Region region = run.Region;
    run.Phase = Phase.Kaecilius;
    ScriptPresentation.ClearWidget(region, HealthWidget);

    Vector3 arena = run.Map.GetMarkers('K').Count > 0 ? ReachableSpot(run, run.Map.GetMarkers('K')[0]) : run.Entrance;
    run.Arena = arena;

    Agent kaecilius = kaeciliusRef != PrototypeId.Invalid
        ? ScriptSpawner.SpawnHostile(region, kaeciliusRef, arena, 0f, 300f, null, true, true, aggroed: false)
        : null;

    if (kaecilius == null)
    {
        Log.Warn("Kaecilius could not be spawned, going to the Titan");
        StartTitan(run, arena);
        return;
    }

    ScriptSpawner.AddRandomAffixes(kaecilius, KaeciliusAffixes, AffixExclude);
    run.KaeciliusId = kaecilius.Id;
    run.ShieldsUsed.Clear();
    run.Targets[kaecilius.Id] = new Target { Kind = Kind.Kaecilius, Name = "Kaecilius", Position = kaecilius.RegionLocation.Position };
    ScriptPresentation.WidgetTrackHealth(region, HealthWidget, kaecilius);

    ScriptText.ShowBannerToRegion(region, "ddi_kaecilius_arrives", 0, "large", 4000);
    ScriptPresentation.StoryNotificationToRegion(region, "ddi_kae_arrive", KaeciliusPath, 7000);
    ScriptText.SetObjectiveTitle(region, "ddi_obj_kaecilius");
    ScriptText.SetObjectiveCounter(region, 0, 1);
    Announce(region, "Kaecilius waits at the heart of Limbo. Break his Mirror Images, then strike him down!");

    Shield(run, kaecilius, "ddi_kae_shield");
}

// Kaecilius becomes invulnerable and splits into Mirror Images
void Shield(Run run, Agent kaecilius, string tauntKey)
{
    Region region = run.Region;
    kaecilius.Properties[PropertyEnum.Invulnerable] = true;
    run.KaeciliusShielded = true;

    Avatar target = PlayersIn(region).Select(p => p.CurrentAvatar).FirstOrDefault(a => a.IsDead == false);
    for (int i = 0; i < MirrorImages && mirrorRef != PrototypeId.Invalid; i++)
    {
        // Images must be reachable: a stranded one would leave Kaecilius invulnerable for good
        Agent image = ScriptSpawner.SpawnHostile(region, mirrorRef, ReachableSpot(run, kaecilius.RegionLocation.Position, 700f), 0f, 100f,
            target, false, true);
        if (image == null)
            continue;

        run.Targets[image.Id] = new Target { Kind = Kind.Mirror, Name = "Mirror Image", Position = image.RegionLocation.Position };
        ScriptPresentation.WidgetTrackHealth(region, HealthWidget, image);
    }

    if (run.Targets.Values.Any(t => t.Kind == Kind.Mirror) == false)
    {
        Expose(run, kaecilius);   // no images could spawn: never leave him invulnerable for good
        return;
    }

    ScriptText.ShowBannerToRegion(region, "ddi_shielded", 0, "alert", 3000);
    ScriptPresentation.StoryNotificationToRegion(region, tauntKey, KaeciliusPath, 5000);
}

void Expose(Run run, Agent kaecilius)
{
    kaecilius.Properties[PropertyEnum.Invulnerable] = false;
    run.KaeciliusShielded = false;
    ScriptText.ShowBannerToRegion(run.Region, "ddi_exposed", 0, "large", 2500);
}

void UpdateKaecilius(Run run)
{
    Region region = run.Region;
    Agent kaecilius = region.Game.EntityManager.GetEntity<Agent>(run.KaeciliusId);
    if (kaecilius == null || kaecilius.IsAliveInWorld == false)
        return;

    if (run.KaeciliusShielded)
    {
        if (run.Targets.Values.Any(t => t.Kind == Kind.Mirror) == false)
            Expose(run, kaecilius);
        return;
    }

    long health = kaecilius.Properties[PropertyEnum.Health];
    long healthMax = Math.Max((long)kaecilius.Properties[PropertyEnum.HealthMaxOther], 1);
    float fraction = (float)health / healthMax;

    foreach (float at in ShieldAt)
    {
        if (fraction <= at && run.ShieldsUsed.Add(at))
        {
            Shield(run, kaecilius, run.ShieldsUsed.Count == 1 ? "ddi_kae_shield" : "ddi_kae_shield2");
            break;
        }
    }
}

// Phase 4
void StartTitan(Run run, Vector3 at)
{
    if (run.Ended || run.Phase == Phase.Titan)
        return;

    Region region = run.Region;
    run.Phase = Phase.Titan;

    Avatar target = PlayersIn(region).Select(p => p.CurrentAvatar).FirstOrDefault(a => a.IsDead == false);
    Agent titan = titanRef != PrototypeId.Invalid
        ? ScriptSpawner.SpawnHostile(region, titanRef, ReachableSpot(run, at, 700f), 0f, 150f, target, true, true)
        : null;
    if (titan == null)
    {
        Log.Warn("The Mindless Titan could not be spawned, finishing");
        Finish(run, at);
        return;
    }

    ScriptSpawner.AddRandomAffixes(titan, TitanAffixes, AffixExclude);
    run.Targets[titan.Id] = new Target { Kind = Kind.Titan, Name = "the Mindless Titan", Position = titan.RegionLocation.Position };
    ScriptPresentation.WidgetTrackHealth(region, HealthWidget, titan);

    ScriptText.ShowBannerToRegion(region, "ddi_titan", 0, "large", 4000);
    ScriptPresentation.StoryNotificationToRegion(region, "ddi_strange_titan", StrangePath, 6000);
    ScriptText.SetObjectiveTitle(region, "ddi_obj_titan");
    ScriptText.SetObjectiveCounter(region, 0, 1);
}

// The end: Doctor Strange appears, players claim their reward from him
void Finish(Run run, Vector3 at)
{
    if (run.Ended || run.Phase == Phase.Claim)
        return;

    Region region = run.Region;
    run.Phase = Phase.Claim;
    run.FinishedAt = region.Game.CurrentTime;
    ScriptPresentation.ClearWidget(region, HealthWidget);
    ScriptPresentation.ClearWidget(region, TimerWidget);

    // Leftover demons go away
    foreach (ulong id in run.Extras.Concat(run.RiftAdds.Values.SelectMany(list => list)))
        ScriptSpawner.Despawn(region.Game, id);
    run.Extras.Clear();

    if (strangeRef != PrototypeId.Invalid)
    {
        Vector3 spot = ReachableSpot(run, at, 600f);
        WorldEntity strange = ScriptSpawner.SpawnInteractable(region, strangeRef, spot, 0f, StrangeTag);
        if (strange != null) run.Helpers.Add(strange.Id);
    }

    ScriptText.ShowBannerToRegion(region, "ddi_complete", 0, "rewardlarge", 5000);
    ScriptPresentation.StoryNotificationToRegion(region, "ddi_strange_end", StrangePath, 7000);
    ScriptText.SetObjectiveTitle(region, "ddi_obj_claim");
    ScriptText.SetObjectiveCounter(region, 0, 1);

    float seconds = (float)(run.FinishedAt - run.StartTime).TotalSeconds;
    Announce(region, $"Limbo is safe! Cleared in {FormatTime(seconds)}. Speak with Doctor Strange within {FormatTime(ClaimWindow)} to claim your reward.");
    AnnounceTopDamage(run);
}

void Claim(Run run, Player player)
{
    if (run.Phase != Phase.Claim)
    {
        ScriptPresentation.StoryNotification(player, "ddi_strange_intro", StrangePath, 5000);
        return;
    }

    if (run.Claimed.Add(player.DatabaseUniqueId) == false)
    {
        ScriptHooks.SendChatMessage(player, "[Incursion] You already claimed your reward.", false);
        return;
    }

    DropRewards(player, CompletionRewards);
    ScriptPresentation.StoryNotification(player, "ddi_strange_claimed", StrangePath, 5000);
    ScriptHooks.SendChatMessage(player, "[Incursion] Doctor Strange's reward has dropped for you.", false);

    if (PlayersIn(run.Region).All(p => run.Claimed.Contains(p.DatabaseUniqueId)))
        EndRun(run, true, "Everyone has claimed their reward.", true);
}

//------------------------------------------------------------------------------
// Kills, damage and rewards
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
            if (target == null || run.Targets.TryGetValue(target.Id, out Target info) == false)
                return;

            ulong dbId = evt.Player.DatabaseUniqueId;
            long damage = -evt.Damage;
            run.RunDamage[dbId] = run.RunDamage.GetValueOrDefault(dbId) + damage;
            run.PlayerNames[dbId] = evt.Player.GetName();

            if (info.Kind == Kind.Rift)
                run.RiftDamage[dbId] = run.RiftDamage.GetValueOrDefault(dbId) + damage;
            else if (info.Kind == Kind.Kaecilius || info.Kind == Kind.Mirror)
                run.KaeciliusDamage[dbId] = run.KaeciliusDamage.GetValueOrDefault(dbId) + damage;
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

    switch (target.Kind)
    {
        case Kind.Rift:
            foreach (ulong addId in run.RiftAdds.GetValueOrDefault(entityId) ?? new List<ulong>())
                run.Extras.Add(addId);   // its demons stay, but are cleaned up at the end
            run.RiftAdds.Remove(entityId);
            RiftDown(run, entityId, target, killed);
            break;

        case Kind.Guardian:
            ScriptPresentation.WidgetUntrack(region, HealthWidget, entityId);
            if (killed) Announce(region, $"{target.Name} has been banished!");
            break;

        case Kind.Mirror:
            ScriptPresentation.WidgetUntrack(region, HealthWidget, entityId);
            break;

        case Kind.Kaecilius:
            ScriptPresentation.WidgetEntityDefeated(region, HealthWidget, entityId);
            foreach (ulong mirrorId in run.Targets.Where(pair => pair.Value.Kind == Kind.Mirror).Select(pair => pair.Key).ToList())
            {
                run.Targets.Remove(mirrorId);
                ScriptSpawner.Despawn(region.Game, mirrorId);
            }

            if (killed == false)
            {
                // He vanished: bring him back so the run can't get stuck
                run.Phase = Phase.Nodes;
                After(region.Game, 3f, () => StartKaecilius(run));
                break;
            }

            ScriptPresentation.StoryNotificationToRegion(region, "ddi_kae_down", KaeciliusPath, 5000);
            GiveRewards(run, run.KaeciliusDamage, KaeciliusRewards, "Kaecilius");
            ScriptText.SetObjectiveCounter(region, 1, 1);
            Vector3 fellAt = run.Arena;
            After(region.Game, 4f, () =>
            {
                ScriptPresentation.ClearWidget(region, HealthWidget);
                StartTitan(run, fellAt);
            });
            break;

        case Kind.Titan:
            if (killed == false)
            {
                run.Phase = Phase.Kaecilius;
                After(region.Game, 3f, () => StartTitan(run, run.Arena));
                break;
            }

            WorldEntity body = region.Game.EntityManager.GetEntity<WorldEntity>(entityId);
            Finish(run, body?.RegionLocation.Position ?? target.Position);
            break;
    }
}

void AnnounceTopDamage(Run run)
{
    var top = run.RunDamage.OrderByDescending(pair => pair.Value).Take(3).ToList();
    if (top.Count == 0)
        return;

    long total = Math.Max(run.RunDamage.Values.Sum(), 1);
    Announce(run.Region, "Top damage: " + string.Join(", ", top.Select((pair, i) =>
        $"{i + 1}. {run.PlayerNames.GetValueOrDefault(pair.Key, "?")} ({pair.Value * 100 / total}%)")));

    Avatar topAvatar = run.Region.Game.EntityManager.GetEntityByDbGuid<Player>(top[0].Key)?.CurrentAvatar;
    if (topAvatar != null && topAvatar.Region == run.Region)
        ScriptText.ShowOverheadText(topAvatar, "ddi_overhead", 0, 5f);
}

void GiveRewards(Run run, Dictionary<ulong, long> damageByPlayer, Reward[] rewards, string what)
{
    foreach (Player player in PlayersIn(run.Region))
    {
        if (damageByPlayer.GetValueOrDefault(player.DatabaseUniqueId) <= 0)
            continue;

        DropRewards(player, rewards);
        ScriptHooks.SendChatMessage(player, $"[Incursion] {what}: rewards have dropped for you.", false);
    }
}

void DropRewards(Player player, Reward[] rewards)
{
    int slot = 1;
    foreach (Reward reward in rewards)
    {
        if (ScriptRewards.DropItem(player, reward.Path, reward.Count, slot++) == false)
            Log.Warn($"Could not drop [{reward.Path}] for {player.GetName()}");
    }
}

//------------------------------------------------------------------------------
// Ending and the main tick
//------------------------------------------------------------------------------

void EndRun(Run run, bool success, string message, bool announce)
{
    if (run.Ended)
        return;

    run.Ended = true;
    Region region = run.Region;
    DetachDamageTracking(run);
    ScriptPresentation.ClearWidget(region, TimerWidget);
    ScriptPresentation.ClearWidget(region, HealthWidget);   // before anything it tracks despawns

    // Anyone who earned the completion reward but didn't claim it still gets it
    if (success && run.Phase == Phase.Claim)
    {
        foreach (Player player in PlayersIn(region))
        {
            if (run.RunDamage.GetValueOrDefault(player.DatabaseUniqueId) > 0 && run.Claimed.Add(player.DatabaseUniqueId))
            {
                DropRewards(player, CompletionRewards);
                ScriptHooks.SendChatMessage(player, "[Incursion] Your unclaimed reward has dropped for you.", false);
            }
        }
    }

    foreach (ulong id in run.Targets.Keys.Concat(run.Extras).Concat(run.RiftAdds.Values.SelectMany(list => list)).Concat(run.Helpers))
        ScriptSpawner.Despawn(region.Game, id);
    run.Targets.Clear();
    run.Extras.Clear();
    run.RiftAdds.Clear();
    run.Helpers.Clear();

    foreach (Player player in PlayersIn(region))
        RemoveBuffs(player.CurrentAvatar);

    if (announce)
    {
        ScriptText.ClearObjective(region);
        if (success == false)
            ScriptText.ShowBannerToRegion(region, "ddi_failed", 0, "error", 5000);
        Announce(region, message);
    }

    Log.Info($"Incursion opened by {run.OpenerName} ended: {message}");

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

// The tick never stops on an error (that would freeze the whole event): log it and keep going
void Tick(Run run)
{
    if (run.Ended)
        return;

    try
    {
        TickBody(run);
    }
    catch (Exception e)
    {
        Log.Error($"Incursion tick failed (phase {run.Phase}): {e}");
    }

    if (run.Ended == false)
        After(run.Region.Game, TickSeconds, () => Tick(run));
}

void TickBody(Run run)
{
    Region region = run.Region;

    // Targets that vanished without dying still count, so the run can never get stuck
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
        EndRun(run, false, "Everyone left Limbo.", false);
        return;
    }

    float elapsed = (float)(region.Game.CurrentTime - run.StartTime).TotalSeconds;

    if (run.Phase == Phase.Claim)
    {
        if ((region.Game.CurrentTime - run.FinishedAt).TotalSeconds >= ClaimWindow)
        {
            EndRun(run, true, "Doctor Strange closes the way out of Limbo.", true);
            return;
        }
    }
    else
    {
        float left = TimeLimit - elapsed;
        if (left <= 0f)
        {
            EndRun(run, false, "Time ran out. Limbo has fallen to the Dark Dimension.", true);
            return;
        }

        foreach (float warning in new[] { 300f, 60f })
        {
            if (left <= warning && run.WarningsGiven.Add(warning))
                Announce(region, $"{FormatTime(warning)} until Limbo falls!");
        }

        switch (run.Phase)
        {
            case Phase.Rifts:     PulseRifts(run, elapsed); break;
            case Phase.Nodes:     UpdateNodes(run); break;
            case Phase.Kaecilius: UpdateKaecilius(run); break;
        }

        if (elapsed >= run.NextHintAt)
        {
            run.NextHintAt = elapsed + HintInterval;
            foreach (Player player in PlayersIn(region))
                ScriptHooks.SendChatMessage(player, "[Incursion] " + ObjectiveHint(run, player.CurrentAvatar), false);
        }
    }
}

string ObjectiveHint(Run run, Avatar avatar)
{
    Vector3 here = avatar.RegionLocation.Position;
    string Meters(Vector3 to) => $"about {(int)(Vector3.Distance2D(here, to) / 100f)} m away";

    switch (run.Phase)
    {
        case Phase.Rifts:
            var rift = run.Targets.Values.Where(t => t.Kind == Kind.Rift).OrderBy(t => Vector3.Distance2D(here, t.Position)).FirstOrDefault();
            return rift != null ? $"Rifts sealed {run.RiftsSealed} / {run.RiftTotal}. Nearest: the {rift.Name}, {Meters(rift.Position)}." : "Sealing the rifts...";

        case Phase.Nodes:
            var node = run.Nodes.Where(n => n.Attuned == false).OrderBy(n => Vector3.Distance2D(here, n.Position)).FirstOrDefault();
            return node != null
                ? $"Power nodes {run.NodesAttuned} / {run.Nodes.Count}. Nearest unattuned node: {Meters(node.Position)}" +
                  (node.Attuning ? $" (attuning, {(int)(node.Progress * 100 / AttuneSeconds)}%)." : ". Click it or stand on it.")
                : "Attuning the power nodes...";

        case Phase.Kaecilius:
            return $"Kaecilius is {Meters(run.Arena)}" + (run.KaeciliusShielded ? ". Destroy his Mirror Images first!" : ".");

        case Phase.Titan:
            var titan = run.Targets.Values.FirstOrDefault(t => t.Kind == Kind.Titan);
            return titan != null ? $"The Mindless Titan is {Meters(titan.Position)}." : "Destroy the Mindless Titan!";

        case Phase.Claim:
            return "Speak with Doctor Strange to claim your reward.";
    }

    return "The incursion is starting...";
}

//------------------------------------------------------------------------------
// Hooks
//------------------------------------------------------------------------------

Hooks.On(ScriptHooks.RegionGenerating, e =>
{
    if (e.RegionPrototype.DataRef != hostRef || pendingBySeed.ContainsKey(e.Seed) == false)
        return;

    e.BuildLayout(CellSet, Map);
    e.SetLevel(MapLevel);
    Log.Info($"Building the incursion map ({Map.Length}x{Map.Max(row => row.Length)}, seed {e.Seed})");
});

Hooks.On(ScriptHooks.EntityInteracted, e =>
{
    string tag = e.ScriptTag;
    if (string.IsNullOrEmpty(tag) || tag.StartsWith("ddi_") == false)
        return;

    if (tag == HubTag)
    {
        ScriptDialog.Show(e.Player, "ddi_prompt", "ddi_enter", "ddi_cancel", (player, button) =>
        {
            if (button == 1 && player.GetRegion()?.PrototypeDataRef == hubRef)
                OpenIncursion(player);
        }, e.Entity);
        return;
    }

    Region region = e.Player.GetRegion();
    if (region == null || runs.TryGetValue(region, out Run run) == false || run.Ended)
        return;

    if (tag == CleaTag)
    {
        ScriptPresentation.StoryNotification(e.Player, run.WardGiven.Add(e.Player.DatabaseUniqueId) ? "ddi_clea_ward" : "ddi_clea_hint", CleaPath, 5000);
        ApplyWard(e.Player);
        ScriptHooks.SendChatMessage(e.Player, "[Incursion] " + ObjectiveHint(run, e.Player.CurrentAvatar), false);
        return;
    }

    if (tag == StrangeTag)
    {
        Claim(run, e.Player);
        return;
    }

    if (tag.StartsWith(NodeTagBase) && int.TryParse(tag.AsSpan(NodeTagBase.Length), out int nodeIndex))
    {
        Node node = run.Nodes.FirstOrDefault(n => n.Index == nodeIndex);
        if (node == null)
            return;

        if (run.Phase != Phase.Nodes)
            ScriptHooks.SendChatMessage(e.Player, "[Incursion] The node is dormant. Seal the rifts first.", false);
        else if (node.Attuned)
            ScriptHooks.SendChatMessage(e.Player, "[Incursion] This node is already attuned.", false);
        else
            StartAttuning(run, node, e.Player);
    }
});

Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    Region region = e.Region;

    // Event buffs only last inside the incursion
    if (runs.ContainsKey(region) == false)
        RemoveBuffs(e.Player.CurrentAvatar);

    if (region.PrototypeDataRef == hubRef)
    {
        EnsureHubNpc(region);
        return;
    }

    if (runs.TryGetValue(region, out Run running))
    {
        // Late joiners get the blessings earned so far
        if (running.NodesAttuned > 0)
            After(region.Game, 3f, () => ApplyEye(running, e.Player));
        return;
    }

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
    if (e.Command != "incursion")
        return;

    e.Handled = true;
    Avatar avatar = e.Player.CurrentAvatar;
    Region region = avatar?.Region;
    if (region == null)
        return;

    string sub = e.GetArg(0).ToLowerInvariant();

    if (sub == "open")
    {
        if (ScriptHooks.IsAdmin(e.Player) == false) { e.Reply("Admin only. Talk to Doctor Strange in Avengers Tower."); return; }
        OpenIncursion(e.Player);
        return;
    }

    if (sub == "npc")
    {
        if (ScriptHooks.IsAdmin(e.Player) == false) { e.Reply("Admin only."); return; }
        if (region.PrototypeDataRef != hubRef) { e.Reply("Go to Avengers Tower first."); return; }

        Vector3 here = avatar.RegionLocation.Position;
        float yaw = MathF.Atan2(avatar.Forward.Y, avatar.Forward.X) * 180f / MathF.PI + 180f;
        Vector3 spot = here + avatar.Forward * 120f;
        EnsureHubNpc(region, spot, yaw);
        e.Reply("Doctor Strange moved (until restart). To keep him there, set in dark_dimension_incursion.csx:");
        e.Reply($"Vector3? NpcPosition = new Vector3({spot.X:0}f, {spot.Y:0}f, {spot.Z:0}f); float NpcYawDegrees = {yaw:0}f;");
        return;
    }

    if (runs.TryGetValue(region, out Run run) && run.Ended == false)
    {
        if (run.StartTime == TimeSpan.Zero)
        {
            e.Reply("The incursion is starting...");
            return;
        }

        string time = run.Phase == Phase.Claim ? "" : $" {FormatTime(TimeLimit - (float)(region.Game.CurrentTime - run.StartTime).TotalSeconds)} left.";
        e.Reply(ObjectiveHint(run, avatar) + time);
    }
    else
    {
        e.Reply("No incursion here. Talk to Doctor Strange in Avengers Tower to enter Limbo.");
    }
});

Log.Info("Dark Dimension Incursion loaded");

//------------------------------------------------------------------------------
// Types
//------------------------------------------------------------------------------

enum Phase { Starting, Rifts, Nodes, Kaecilius, Titan, Claim }
enum Kind { Rift, Guardian, Kaecilius, Mirror, Titan }

class Rift
{
    public string Name { get; }
    public string Path { get; }
    public string[] AddPaths { get; }
    public PrototypeId Ref { get; set; } = PrototypeId.Invalid;
    public PrototypeId[] AddRefs { get; set; } = Array.Empty<PrototypeId>();

    public Rift(string name, string path, params string[] addPaths)
    {
        Name = name;
        Path = path;
        AddPaths = addPaths;
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
    public Kind Kind;
    public string Name;
    public Vector3 Position;
    public int RiftIndex;
}

class Node
{
    public int Index;
    public ulong EntityId;
    public Vector3 Position;
    public bool Attuning;
    public bool Attuned;
    public float Progress;
    public float AwaySeconds;
    public ulong GuardianId;
}

class Run
{
    public int Seed;
    public string OpenerName;
    public DateTime CreatedAt;

    public Region Region;
    public ScriptedLayout Map;
    public Vector3 Entrance;
    public Vector3 Arena;
    public bool StartScheduled;
    public TimeSpan StartTime;
    public TimeSpan FinishedAt;
    public bool Ended;
    public Phase Phase = Phase.Starting;
    public float EmptySeconds;
    public float NextHintAt;
    public float NextPulseAt;
    public HashSet<float> WarningsGiven = new();

    public int RiftTotal;
    public int RiftsSealed;
    public int NodesAttuned;
    public ulong KaeciliusId;
    public bool KaeciliusShielded;
    public HashSet<float> ShieldsUsed = new();

    public Dictionary<ulong, Target> Targets = new();                // entity id => event target
    public Dictionary<ulong, List<ulong>> RiftAdds = new();           // rift entity id => its demons
    public List<Node> Nodes = new();
    public List<ulong> Extras = new();                                // other spawned enemies (cleaned up at the end)
    public List<ulong> Helpers = new();                               // NPCs and objects (cleaned up at the end)
    public HashSet<ulong> Claimed = new();                            // player db ids that claimed the end reward
    public HashSet<ulong> WardGiven = new();

    public Dictionary<ulong, long> RunDamage = new();
    public Dictionary<ulong, long> RiftDamage = new();
    public Dictionary<ulong, long> KaeciliusDamage = new();
    public Dictionary<ulong, string> PlayerNames = new();
    public Event<AdjustHealthGameEvent>.Action DamageAction;
}
