// Admin test: put any client condition effect (glows, auras, spotlights, shields, flames, size changes...) on your hero. The
// effect is borrowed from a condition in the game data that uses it and applied with no stats, the same way !grow works.
// It lasts until turned off, through region changes and hero swaps.
//
//   !fx find <text>    list the effects whose name contains <text> (e.g. glow, spotlight, aura, shield, flame, buff)
//   !fx <name>         put that effect on your hero (replaces the one you had from this command)
//   !fx off            take it off
//
// Names are the client's condition effect classes without the "MarvelConditionEffect_" prefix, e.g. ThorCharged,
// XMenGoldSpotlight, PumpkinSpotlight, CosmicItemOffensiveBuff. The first !fx after a server start reads every power in the
// game data and takes a moment. Many effects are made for one hero or enemy and show nothing (or look odd) on others.

using System.Collections.Concurrent;

const string FxKey   = "fx_test.fx";
const int    MaxList = 40;   // names shown per !fx find

// players (database id) => the effect they have on
var active = new ConcurrentDictionary<ulong, string>();

// conditions are lost when the hero leaves the world (region change, hero swap)
Hooks.On(ScriptHooks.AvatarEnteredWorld, e =>
{
    if (e.Player != null && active.TryGetValue(e.Player.DatabaseUniqueId, out string name))
        ScriptConditions.ApplyEffect(e.Avatar, FxKey, name);
});

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "fx")
        return;

    e.Handled = true;
    if (ScriptHooks.IsAdmin(e.Player) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    Avatar avatar = e.Player.CurrentAvatar;
    string arg = e.GetArg(0);

    if (arg.Length == 0)
    {
        e.Reply("Use: !fx find <text> | !fx <name> | !fx off");
        return;
    }

    if (arg.Equals("off", StringComparison.OrdinalIgnoreCase))
    {
        active.TryRemove(e.PlayerDbId, out _);
        ScriptConditions.Remove(avatar, FxKey);
        e.Reply("Effect off.");
        return;
    }

    if (arg.Equals("find", StringComparison.OrdinalIgnoreCase))
    {
        List<string> names = ScriptConditions.FindEffects(e.GetArg(1));
        e.Reply($"{names.Count} effects" + (names.Count > MaxList ? $" (showing the first {MaxList}; narrow the search)" : "") + ":");
        for (int i = 0; i < Math.Min(names.Count, MaxList); i += 4)
            e.Reply("  " + string.Join(", ", names.Skip(i).Take(Math.Min(4, MaxList - i))));
        return;
    }

    if (avatar == null || ScriptConditions.ApplyEffect(avatar, FxKey, arg) == false)
    {
        active.TryRemove(e.PlayerDbId, out _);
        e.Reply($"Could not apply [{arg}]. Check the name with !fx find <text>.");
        return;
    }

    active[e.PlayerDbId] = arg;
    e.Reply($"Effect on: {arg}. !fx off to remove it.");
});
