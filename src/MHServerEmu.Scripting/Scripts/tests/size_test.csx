// Admin test: size changes on your hero. Each borrows the client visual of an existing power's condition as a stat-less
// condition that lasts until turned off. The client keeps heroes between 0.7x and 1.3x size unless the effect itself lifts
// that limit: Giant-Man's grow lifts the upper one, Ant-Man's ant flight the lower one. Item size effects multiply on top.
//
//   !grow               toggle Giant-Man size (4x)
//   !shrink jugg        1.5x (Juggernaut's ultimate); !shrink hulkout = 1.3x (Deadpool's Hulk-out): the other grow effects
//   !shrink             toggle Ant-Man size (0.2x, from his ant flight)
//   !shrink <name>      another shrink: antnado (0.2x), small (0.75x), hulk (0.7x)
//   !grow off / !shrink off
//
// Stays on through region changes and hero swaps until turned off. Only one size effect at a time.

using System.Collections.Concurrent;

const string SizeKey = "size_test.size";

// name => the power whose condition (its first one) carries the visual
var Effects = new Dictionary<string, (string Power, string Label)>(StringComparer.OrdinalIgnoreCase)
{
    ["grow"]    = ("Powers/Player/AntMan/GiantManFootGrowConditionEffect.prototype",       "Giant-Man size (4x)"),
    ["jugg"]    = ("Powers/Player/Juggernaut/Ultimate.prototype",                          "big (1.5x, Juggernaut's ultimate)"),
    ["hulkout"] = ("Powers/Player/Deadpool/Rework/PowerUpHulkDollEffect.prototype",        "big (1.3x, Deadpool's Hulk-out)"),
    ["tiny"]    = ("Powers/Player/TravelPower/AntmanFlight.prototype",                     "Ant-Man size (0.2x, ant flight)"),
    ["antnado"] = ("Powers/Player/AntMan/AntnadoMovementPower.prototype",                  "Ant-Man size (0.2x, Antnado)"),
    ["small"]   = ("Powers/ItemPowers/ArtifactPowers/Art014PlayerSizeProc.prototype",      "small (0.75x, Pym Particles artifact)"),
    ["hulk"]    = ("Powers/Player/Hulk/Rework/PassiveToughReviveShrinkGrowCond.prototype", "small (0.7x, Hulk's revive shrink)"),
};

// players (database id) => the effect they have on
var active = new ConcurrentDictionary<ulong, string>();

bool Apply(Avatar avatar, string name)
{
    return avatar != null && ScriptConditions.ApplyPowerCondition(avatar, SizeKey, Effects[name].Power);
}

// conditions are lost when the hero leaves the world (region change, hero swap)
Hooks.On(ScriptHooks.AvatarEnteredWorld, e =>
{
    if (e.Player != null && active.TryGetValue(e.Player.DatabaseUniqueId, out string name))
        Apply(e.Avatar, name);
});

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "grow" && e.Command != "shrink")
        return;

    e.Handled = true;
    if (ScriptHooks.IsAdmin(e.Player) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    Avatar avatar = e.Player.CurrentAvatar;
    string arg = e.GetArg(0);
    string name = e.Command == "grow" ? "grow" : (arg.Length == 0 ? "tiny" : arg);

    // "off", or the same command again, turns it off
    bool sameAgain = active.TryGetValue(e.PlayerDbId, out string current) && current.Equals(name, StringComparison.OrdinalIgnoreCase)
        && avatar != null && ScriptConditions.Has(avatar, SizeKey);

    if (arg.Equals("off", StringComparison.OrdinalIgnoreCase) || sameAgain)
    {
        active.TryRemove(e.PlayerDbId, out _);
        ScriptConditions.Remove(avatar, SizeKey);
        e.Reply("Size effect off.");
        return;
    }

    if (Effects.ContainsKey(name) == false)
    {
        e.Reply($"Unknown size effect. Use: !grow, !shrink, !shrink {string.Join(" | ", Effects.Keys.Where(k => k != "grow" && k != "tiny"))}, or off.");
        return;
    }

    if (Apply(avatar, name) == false)
    {
        active.TryRemove(e.PlayerDbId, out _);
        e.Reply("Could not apply the size effect (see the server log).");
        return;
    }

    active[e.PlayerDbId] = name;
    e.Reply($"Size effect on: {Effects[name].Label}. Same command again (or 'off') turns it off.");
});
