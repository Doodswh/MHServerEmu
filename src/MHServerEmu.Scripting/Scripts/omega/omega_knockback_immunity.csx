// Omega difficulty: players are immune to knockback / knockdown / knockup from other sources. Pulls (e.g. the Implosion
// affix) are allowed.
//
// This is a C# script rather than Lua on purpose: ConditionApplying fires for every condition in every game,
// and C# handlers run natively without the lock Lua needs across game threads.

// Set to true to log displacement the avatar applies to itself (own movement powers, death / resurrect effects).
// Useful when chasing a movement lock that the immunity does not cover.
const bool LogSelfApplied = true;

Hooks.On(ScriptHooks.ConditionApplying, e =>
{
    // Cheapest checks first, this runs a lot
    if (e.TargetIsAvatar == false || e.IsDisplacement == false || e.IsOmega == false)
        return;

    if (e.IsSelfApplied)
    {
        // Own movement powers keep working
        if (LogSelfApplied)
            Log.Info($"Self-applied displacement allowed on [{e.TargetName}]: power=[{e.PowerName}] condition=[{e.ConditionName}] " +
                $"knockback={e.IsKnockback} knockdown={e.IsKnockdown} knockup={e.IsKnockup}");
        return;
    }

    // Pulls are fine, only pushes are blocked
    if (e.IsPull)
        return;

    e.Cancel = true;
});

Log.Info("Omega knockback immunity active");
