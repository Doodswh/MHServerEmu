-- Blocks stuns from enemies on players in Omega difficulty (shows how to veto a condition).
-- Copy this file out of _examples (anywhere else under Data/Scripts) to enable it.

hooks.on("ConditionApplying", function(e)
    if e.TargetIsAvatar and e.IsOmega and e.IsStun and not e.IsSelfApplied then
        e.Cancel = true
    end
end)
