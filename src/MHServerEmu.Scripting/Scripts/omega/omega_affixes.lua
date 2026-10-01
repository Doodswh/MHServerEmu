-- Omega difficulty mob affixes.
--  1. Strip rolled affixes that can knock back / pull players (they can lock the client in place).
--  2. Add extra random affixes for chaos, from a pool that excludes displacement affixes.
-- Edit and save this file to change the rules; it hot reloads.

-- Number of extra random affixes added to every Omega spawn group
local EXTRA_AFFIXES = 3

-- Affixes whose prototype name contains any of these words are never picked as extras
local EXCLUDE_FROM_RANDOM = table.concat({
    "chest", "xdef", "mission", "emp", "level", "region", "hazard", "trap", "turret",
    "structure", "mystic", "mayhem", "simulacrum", "limbo", "inferno", "barrier",
    "wall", "zone", "field", "implosion",
}, ",")

hooks.on("EnemyAffixesRolled", function(e)
    if not e.IsOmega then
        return
    end

    e:RemoveDisplacementAffixes()
    e:AddRandomAffixes(EXTRA_AFFIXES, EXCLUDE_FROM_RANDOM)
end)

log.info("Omega affixes active (" .. EXTRA_AFFIXES .. " extra per group)")
