// Admin test commands for the random-event building blocks (clickable NPCs, popups, teleports, layout tweaks, random spots).
// Everything here is admin-only (accounts with the site commands badge). Delete or rename to _event_tools_test.csx when done.
//
//   !evtnpc [zone]                 spawn a clickable S.H.I.E.L.D. agent in front of you; clicking it offers to send you
//                                  (and your party members here) to [zone] (default T1L1BambooRegion)
//   !evtclear                      remove the test NPCs in this region
//   !evtlayout <zone> <rooms%> [connections%]
//                                  the next instance of <zone> gets a new random seed and cuts that % of its rooms
//                                  (and closes that % of extra connections); !evtlayout off to stop
//   !randspot                      move to a random walkable spot you can reach on foot
//   !farspot                       move to the farthest reachable spot found (where a boss would go)
//
// Popup text is new client text: reconnect once after the server starts for it to show.

using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Locomotion;

const string NpcPath = "Entity/Characters/NPCs/HubNPCs/SHIELDAgentMale.prototype";
const string NpcTag = "evt_test";
const string DefaultZone = "T1L1BambooRegion";
const float NpcDistance = 150f;

ScriptText.Register("evt_test_prompt", "Enter the test zone? A fresh instance is created for you and your party.");
ScriptText.Register("evt_test_enter", "Enter");
ScriptText.Register("evt_test_cancel", "Cancel");

// NPC entity id -> zone it sends players to
var npcZones = new System.Collections.Concurrent.ConcurrentDictionary<ulong, string>();

// Layout tweak for the next instances of one zone (null = off)
string layoutZone = null;
int layoutRoomPct = 0;
int layoutConnectionPct = -1;

bool IsAdmin(Player player) => ScriptHooks.IsAdmin(player);   // Admin or Dev accounts

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    switch (e.Command)
    {
        case "evtnpc":
        case "evtclear":
        case "evtlayout":
        case "randspot":
        case "farspot":
            break;
        default:
            return;
    }

    e.Handled = true;

    if (IsAdmin(e.Player) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    Avatar avatar = e.Player.CurrentAvatar;
    Region region = avatar?.Region;
    if (region == null || avatar.IsInWorld == false)
    {
        e.Reply("You need to be in the world.");
        return;
    }

    Vector3 here = avatar.RegionLocation.Position;

    switch (e.Command)
    {
        case "evtnpc":
        {
            string zone = e.ArgCount > 0 ? e.GetArg(0) : DefaultZone;
            if (ScriptTeleport.FindRegion(zone) == PrototypeId.Invalid)
            {
                e.Reply($"No region named {zone} (use the file name, e.g. T1L1BambooRegion, or the full path).");
                return;
            }

            Vector3 position = here + avatar.Forward * NpcDistance;
            float yawToPlayer = MathF.Atan2(here.Y - position.Y, here.X - position.X) * 180f / MathF.PI;

            WorldEntity npc = ScriptSpawner.SpawnInteractable(region, GameDatabase.GetPrototypeRefByName(NpcPath), position, yawToPlayer, NpcTag);
            if (npc == null)
            {
                e.Reply("Could not spawn the NPC here (see the server log).");
                return;
            }

            npcZones[npc.Id] = zone;
            e.Reply($"Spawned a test NPC (sends to {zone}). Click it.");
            break;
        }

        case "evtclear":
        {
            int removed = 0;
            foreach (ulong id in npcZones.Keys.ToList())
            {
                WorldEntity npc = region.Game.EntityManager.GetEntity<WorldEntity>(id);
                if (npc == null || npc.Region == region)
                {
                    ScriptSpawner.Despawn(region.Game, id);
                    npcZones.TryRemove(id, out _);
                    removed++;
                }
            }
            e.Reply($"Removed {removed} test NPC(s).");
            break;
        }

        case "evtlayout":
        {
            if (e.GetArg(0).Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                layoutZone = null;
                e.Reply("Layout tweaks off.");
                return;
            }

            if (e.ArgCount < 2 || int.TryParse(e.GetArg(1), out int roomPct) == false)
            {
                e.Reply("Usage: !evtlayout <zone> <rooms%> [connections%], or !evtlayout off");
                return;
            }

            layoutZone = e.GetArg(0);
            layoutRoomPct = roomPct;
            layoutConnectionPct = e.ArgCount > 2 && int.TryParse(e.GetArg(2), out int connectionPct) ? connectionPct : -1;
            e.Reply($"Next instances of {layoutZone}: new seed, cut {layoutRoomPct}% of rooms" +
                (layoutConnectionPct >= 0 ? $", close {layoutConnectionPct}% of extra connections." : "."));
            break;
        }

        case "randspot":
        case "farspot":
        {
            bool found = e.Command == "randspot"
                ? ScriptSpawner.TryFindRandomSpot(region, out Vector3 spot, here)
                : ScriptSpawner.TryFindFarSpot(region, here, out spot);

            if (found == false)
            {
                e.Reply("No reachable spot found.");
                return;
            }

            avatar.ChangeRegionPosition(spot, null, ChangePositionFlags.Teleport);
            e.Reply($"Moved {Vector3.Distance2D(here, spot):0} units, to {spot}.");
            break;
        }
    }
});

Hooks.On(ScriptHooks.EntityInteracted, e =>
{
    if (e.ScriptTag != NpcTag)
        return;

    if (npcZones.TryGetValue(e.EntityId, out string zone) == false)
        return;

    ScriptDialog.Show(e.Player, "evt_test_prompt", "evt_test_enter", "evt_test_cancel", (player, button) =>
    {
        if (button != 1)
            return;

        int sent = ScriptTeleport.ToRegionWithParty(player, zone);
        ScriptHooks.SendChatMessage(player, sent > 0 ? $"Sending {sent} player(s) to {zone}." : $"Could not send you to {zone} (see the server log).", false);
    }, e.Entity);
});

Hooks.On(ScriptHooks.RegionGenerating, e =>
{
    string zone = layoutZone;
    if (zone == null || e.RegionName.Contains(zone, StringComparison.OrdinalIgnoreCase) == false)
        return;

    int oldSeed = e.Seed;
    e.NewRandomSeed();
    e.SetRoomRemovalChance(layoutRoomPct);
    if (layoutConnectionPct >= 0)
        e.SetConnectionRemovalChance(layoutConnectionPct);

    Log.Info($"Layout tweak for [{e.RegionName}]: seed {oldSeed} -> {e.Seed}, rooms cut {layoutRoomPct}%, connections {layoutConnectionPct}%");
});

Log.Info("Event tools test commands loaded (!evtnpc, !evtclear, !evtlayout, !randspot, !farspot)");
