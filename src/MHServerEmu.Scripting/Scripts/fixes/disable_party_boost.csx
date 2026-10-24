// Disables the Test Party Boost (Entity/Items/Consumables/Prototypes/Test/TestPartyBoost.prototype): a development item
// that was in the shop by mistake and multiplies damage for party members.
//
//  1. Using the item no longer does anything: its buff is blocked. The item is still used up, so leftover copies just vanish.
//  2. A buff that is already running (it is saved with the character) is removed when the player enters a region.
//
//   !partyboost    (admin) remove the buff from everyone in this game instance right now and show how many had it
//
// Uses only long-standing script hooks, so it loads on a running server without a restart.

const string BoostCondition = "Entity/Items/Consumables/Prototypes/Test/TestPartyBoostCondition.prototype";

PrototypeId conditionRef = GameDatabase.GetPrototypeRefByName(BoostCondition);
if (conditionRef == PrototypeId.Invalid)
    Log.Warn($"Not found: {BoostCondition}");

// Removes a running boost from the avatar. Returns true if it had one.
bool Strip(Avatar avatar)
{
    if (avatar == null || conditionRef == PrototypeId.Invalid || avatar.ConditionCollection == null)
        return false;

    if (avatar.ConditionCollection.GetConditionByRef(conditionRef) == null)
        return false;

    avatar.ConditionCollection.RemoveConditionsWithConditionPrototypeRef(conditionRef);
    return true;
}

Hooks.On(ScriptHooks.ConditionApplying, e =>
{
    // Cheapest check first: this runs for every condition in every game
    if (e.ConditionPrototype == null || e.ConditionPrototype.DataRef != conditionRef)
        return;

    e.Cancel = true;
    Log.Info($"Blocked a Test Party Boost on [{e.TargetName}]");
});

Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    if (Strip(e.Player.CurrentAvatar))
        Log.Info($"Removed a running Test Party Boost from {e.Player.GetName()}");
});

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "partyboost")
        return;

    e.Handled = true;
    if (ScriptHooks.IsAdmin(e.Player) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    int removed = 0;
    foreach (Player player in e.Player.Game.EntityManager.Players)
    {
        if (player != null && Strip(player.CurrentAvatar))
        {
            removed++;
            Log.Info($"Removed a running Test Party Boost from {player.GetName()}");
        }
    }

    e.Reply($"Test Party Boost removed from {removed} player(s) in this game instance. Using the item is blocked.");
});

Log.Info("Test Party Boost disabled");
