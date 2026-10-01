// Tells players when they reach kill milestones in Omega difficulty (C# script example).
// Copy this file out of _examples (anywhere else under Data/Scripts) to enable it.
// C# scripts are compiled with Roslyn and have full access to the game assemblies.

using System.Collections.Concurrent;
using System.Threading;

// Handlers run on game threads, so shared state must be thread-safe
var killCounts = new ConcurrentDictionary<ulong, int>();

Hooks.On(ScriptHooks.EntityKilled, e =>
{
    if (e.IsOmega == false || e.KillerIsAvatar == false || e.VictimIsAvatar)
        return;

    Player player = e.Killer.GetOwnerOfType<Player>();
    if (player == null)
        return;

    int kills = killCounts.AddOrUpdate(player.DatabaseUniqueId, 1, (id, count) => count + 1);
    if (kills % 500 == 0)
        ScriptHooks.SendChatMessage(player, $"{kills} Omega kills this session!");
});

Log.Info("Kill milestones loaded");
