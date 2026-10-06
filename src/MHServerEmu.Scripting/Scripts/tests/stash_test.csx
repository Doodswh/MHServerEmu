// Admin test: the console version's unified stash on PC (one big stash page with a slot limit that can be raised).
// Needs EnableUnifiedStashOnPC=true under [CustomGameOptions] in the server config.
//
//   !stash open          put a holographic S.T.A.S.H. in front of you for 15 minutes (click it to use your stash)
//   !stash panel <name>  open any client window by panel name (CraftingPanel, VendorPanel, TeamUpPanel, ...)
//   !stash tabs          which stash tabs you have unlocked, and which team-up tabs are locked
//   !stash teamup        unlock both team-up gear stash tabs (Team-Up 01 and 02, 48 slots each)
//   !stash unlock        unlock the unified stash for you (like buying a stash tab: the client is told about it)
//   !stash info          its capacity, used slots and your extra slots
//   !stash slots <n>     add n extra slots (negative removes); the data allows up to 440 extra on top of the 50 default
//
// The slot limit is enforced by the server. Whether the PC client shows the tab, and how many slots it shows, is what
// this test is for.

using MHServerEmu.Games.Entities.Inventories;

const string UnifiedStash = "Entity/Inventory/PlayerInventories/StashInventories/PageProtos/Unified/PlayerStashUnified.prototype";
const string SlotGroup    = "Entity/Inventory/SoftCapacity/TestStashSlotsGroup.prototype";

const string HoloStash = "Entity/Characters/PetsAndSummons/HoloStashAgent.prototype";
const float HoloStashSeconds = 900f;

var TeamUpTabs = new[]
{
    "Entity/Inventory/PlayerInventories/StashInventories/PageProtos/General/PlayerStashTeamUpGeneral01.prototype",
    "Entity/Inventory/PlayerInventories/StashInventories/PageProtos/General/PlayerStashTeamUpGeneral02.prototype",
};

PrototypeId stashRef =GameDatabase.GetPrototypeRefByName(UnifiedStash);
PrototypeId groupRef = GameDatabase.GetPrototypeRefByName(SlotGroup);

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "stash")
        return;

    e.Handled = true;
    Player player = e.Player;
    if (ScriptHooks.IsAdmin(player) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    string first = e.GetArg(0).ToLowerInvariant();

    // !stash open: put a holographic S.T.A.S.H. in front of you (the game's own portable stash). The client only lets you
    // move items while you are interacting with a real stash object, so opening the window alone is not enough: click it.
    if (first == "open")
    {
        Avatar avatar = player.CurrentAvatar;
        PrototypeId holoRef = GameDatabase.GetPrototypeRefByName(HoloStash);
        if (avatar?.Region == null || holoRef == PrototypeId.Invalid)
        {
            e.Reply("Could not place a stash here.");
            return;
        }

        Vector3 spot = avatar.RegionLocation.Position + avatar.Forward * 130f;
        WorldEntity holo = ScriptSpawner.SpawnInteractable(avatar.Region, holoRef, spot, 0f, "stash_test");
        if (holo == null)
        {
            e.Reply("Could not place a stash here.");
            return;
        }

        Game game = avatar.Game;
        ulong holoId = holo.Id;
        After(game, HoloStashSeconds, () => ScriptSpawner.Despawn(game, holoId));
        e.Reply($"A holographic S.T.A.S.H. is in front of you for {HoloStashSeconds / 60f:0} minutes. Click it and try moving items.");
        return;
    }

    // !stash panel <name>: open any client window by its panel name
    if (first == "panel")
    {
        string panel = e.GetArg(1);
        e.Reply(panel.Length > 0 && ScriptPresentation.OpenPanel(player, panel)
            ? $"Asked the client to open [{panel}]."
            : "Usage: !stash panel <PanelName> (e.g. CraftingPanel, VendorPanel, TeamUpPanel, PlayerStashInventoryPanel)");
        return;
    }

    // !stash tabs: which stash tabs you have, and which are still locked
    if (first == "tabs")
    {
        var unlocked = new List<PrototypeId>();
        var locked = new List<PrototypeId>();
        player.GetStashInventoryProtoRefs(unlocked, false, true);
        player.GetStashInventoryProtoRefs(locked, true, false);

        string Names(List<PrototypeId> refs, string contains) => string.Join(", ",
            refs.Select(r => r.GetNameFormatted()).Where(n => n.Contains(contains, StringComparison.OrdinalIgnoreCase)).OrderBy(n => n));

        e.Reply($"Unlocked: {unlocked.Count}, locked: {locked.Count}.");
        e.Reply($"Team-up tabs unlocked: [{Names(unlocked, "TeamUp")}] locked: [{Names(locked, "TeamUp")}]");
        e.Reply($"General tabs unlocked: [{Names(unlocked, "StashGeneral")}]");
        e.Reply($"Crafting tabs unlocked: [{Names(unlocked, "Crafting")}]");
        return;
    }

    // !stash teamup: unlock both team-up gear stash tabs (48 slots each)
    if (first == "teamup")
    {
        foreach (string tab in TeamUpTabs)
        {
            PrototypeId tabRef = GameDatabase.GetPrototypeRefByName(tab);
            if (tabRef == PrototypeId.Invalid)
                continue;

            if (player.GetInventoryByRef(tabRef) != null)
                e.Reply($"{tabRef.GetNameFormatted()}: already unlocked.");
            else
                e.Reply($"{tabRef.GetNameFormatted()}: {(player.UnlockInventory(tabRef) ? "unlocked" : "unlock failed (see the server log)")}.");
        }
        return;
    }

    if (stashRef == PrototypeId.Invalid || groupRef == PrototypeId.Invalid)
    {
        e.Reply("Unified stash prototypes not found.");
        return;
    }

    if (player.IsUsingUnifiedStash == false)
    {
        e.Reply("The unified stash is off. Set EnableUnifiedStashOnPC=true under [CustomGameOptions] and restart.");
        return;
    }

    string sub = e.GetArg(0).ToLowerInvariant();
    Inventory stash = player.GetInventoryByRef(stashRef);

    if (sub == "unlock")
    {
        if (stash != null)
        {
            e.Reply("You already have the unified stash.");
            return;
        }

        e.Reply(player.UnlockInventory(stashRef)
            ? "Unified stash unlocked. Open your stash and look for a tab called \"Stash\"."
            : "Unlock failed (see the server log).");
        return;
    }

    if (stash == null)
    {
        e.Reply("You do not have the unified stash yet. Use !stash unlock.");
        return;
    }

    if (sub == "slots")
    {
        if (int.TryParse(e.GetArg(1), out int change) == false)
        {
            e.Reply("Usage: !stash slots <number>");
            return;
        }

        int extra = player.Properties[PropertyEnum.InventoryExtraSlotsAvailable, groupRef];
        int newExtra = Math.Clamp(extra + change, 0, 440);
        player.Properties[PropertyEnum.InventoryExtraSlotsAvailable, groupRef] = newExtra;
        e.Reply($"Extra slots {extra} -> {newExtra}.");
    }

    int extraSlots = player.Properties[PropertyEnum.InventoryExtraSlotsAvailable, groupRef];
    e.Reply($"Unified stash: {stash.Count} used of {stash.GetCapacity()} usable slots ({extraSlots} extra; hard limit {stash.MaxCapacity}).");
});
