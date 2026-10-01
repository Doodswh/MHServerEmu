-- Greets players when they enter an Omega difficulty region.
-- Copy this file out of _examples (anywhere else under Data/Scripts) to enable it.

hooks.on("PlayerEnteredRegion", function(e)
    if e.IsOmega then
        e:SendMessage("Welcome to " .. e.DifficultyName .. ". Enemies are tougher here, good luck!")
        log.info(e.PlayerName .. " entered Omega region " .. e.RegionName)
    end
end)
