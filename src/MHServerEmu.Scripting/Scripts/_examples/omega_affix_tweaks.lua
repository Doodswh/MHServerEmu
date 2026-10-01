-- Adjusts enemy affixes in Omega difficulty after they are rolled.
-- Copy this file out of _examples (anywhere else under Data/Scripts) to enable it.

hooks.on("EnemyAffixesRolled", function(e)
    if not e.IsOmega then
        return
    end

    -- Remove teleporting affixes
    local removed = e:RemoveAffix("Teleport")
    if removed > 0 then
        log.info("Removed " .. removed .. " teleport affix(es) from a " .. e.RankName .. " group in " .. e.RegionName)
    end

    -- Always make groups hulking
    e:AddAffix("Mods/MobAffixes/Normal/Hulking.prototype")
end)
