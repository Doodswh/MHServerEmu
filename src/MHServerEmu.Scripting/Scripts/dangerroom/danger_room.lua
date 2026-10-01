-- Endless Danger Room (Cable scenario) rules.
-- The engine side (spawning, tracking, chest, vendor, portal, widgets) stays in EndlessCableScenarioManager.cs;
-- this file decides wave sizes, breaks, messages and bonus rewards. Save to hot reload.
--
-- NOTE: on-screen text registered here reaches players the next time they connect (client limitation).

--------------------------------------------------------------------------------
-- Waves
--------------------------------------------------------------------------------

local BASE_BOSSES          = 1     -- bosses on wave 1
local BOSS_EVERY_WAVES     = 10    -- +1 boss every N waves
local BASE_MOBS            = 60    -- mobs on wave 1
local MOBS_PER_WAVE        = 5     -- extra mobs per wave

local NEXT_WAVE_DELAY      = 3     -- seconds between waves
local BREAK_DELAY          = 180   -- seconds for break waves
local BREAK_EVERY_WAVES    = 10    -- a break after every N cleared waves...
local BREAK_UNTIL_WAVE     = 200   -- ...up to and including this wave
local BREAKS_ENABLED       = true  -- false = no breaks at all, every wave uses NEXT_WAVE_DELAY
                                   -- (players can also end a single break early with !skipbreak)

local function isBreakWave(wave)
    return BREAKS_ENABLED and wave % BREAK_EVERY_WAVES == 0 and wave <= BREAK_UNTIL_WAVE
end

local BANNER_MAX_WAVE      = 1000  -- wave clear banners are registered up to this wave

hooks.on("DangerRoomWaveStarting", function(e)
    e.BossCount = BASE_BOSSES + math.floor(e.Wave / BOSS_EVERY_WAVES)
    e.MobCount  = BASE_MOBS + (e.Wave - 1) * MOBS_PER_WAVE
end)

--------------------------------------------------------------------------------
-- Wave clear messages
--------------------------------------------------------------------------------

ui.registerRange("dr_wave_clear", "Wave {0} Clear", 1, BANNER_MAX_WAVE)

hooks.on("DangerRoomWaveCleared", function(e)
    local isBreak = isBreakWave(e.Wave)
    e.DelaySeconds = isBreak and BREAK_DELAY or NEXT_WAVE_DELAY

    -- Take over the built-in banner so it works past wave 99
    e.ShowDefaultMessages = false
    if e.Wave <= BANNER_MAX_WAVE then
        e:ShowBanner("dr_wave_clear", e.Wave, "large", 3000)
    end

    if isBreak then
        e:SendMessage("[Cable Endless Scenario] Wave " .. e.Wave .. " cleared! Taking a " .. math.floor(BREAK_DELAY / 60) ..
            "-minute break. Catch your breath. Type !skipbreak to start the next wave now.")
    end
end)

--------------------------------------------------------------------------------
-- Completion bonus rewards
-- The main chest loot roll, experience orbs and Danger Room merits always drop (live tuned, see LiveTuning).
-- Each entry: { minimum cleared waves, item prototype path, count, drop slot around the chest }
--------------------------------------------------------------------------------

local GEM = "entity/items/gems/gem1.prototype"

local REWARDS = {
    { 10, "Entity/Items/Consumables/Prototypes/FortuneCard/AgeOfUltronFortuneCard.prototype",               10, 20 },
    { 15, "Entity/Items/Consumables/Prototypes/FortuneCard/CowpocalypseFortuneCard.prototype",               3, 21 },
    { 16, GEM,                                                                                                1,  2 },
    { 19, "Entity/Items/Consumables/Prototypes/FortuneCard/GuardiansOfTheGalaxyVol2FortuneCard.prototype",  10, 22 },
    { 20, "Entity/Items/Consumables/Prototypes/CSGrant/CSGrantCrateARMORDriveBox25.prototype",               1,  4 },
    { 21, "Entity/Items/Rings/StoneOfJordan.prototype",                                                       1,  5 },
    { 23, "Entity/Items/Consumables/Prototypes/FortuneCard/SpiderManHomecomingFortuneCard.prototype",       10, 23 },
    { 24, GEM,                                                                                                1,  2 },
    { 25, "Entity/Items/Consumables/Prototypes/CSGrant/LoginRandomVanityPetBox.prototype",                   1,  8 },
    { 26, "Entity/Items/Consumables/Prototypes/FortuneCard/LoganFortuneCard.prototype",                     10, 24 },
    { 27, "entity/items/Legendaries/Prototypes/Legendary014.prototype",                                      1,  9 },
    { 28, "Entity/Items/Consumables/Prototypes/CSGrant/CSGrantCrateProtectorsCommendations10Box.prototype",  1,  6 },
    { 29, "Entity/Items/Consumables/Prototypes/FortuneCard/XMenFortuneCard.prototype",                      10, 25 },
    { 30, "Entity/Items/Consumables/Prototypes/CSGrant/CSGrantCrateHeroCommendation25Box.prototype",         1, 10 },
    { 31, "Entity/Items/Consumables/Prototypes/RandomGiftboxes/RandomCosmicArtifactBox.prototype",           1, 19 },
    { 32, "Entity/Items/Artifacts/Prototypes/SpecialArtifacts/CosmicArtifacts/Art340Cosmic.prototype",       1, 13 },
    { 33, "Entity/Items/Consumables/Prototypes/FortuneCard/OdinsBountyFortuneCard.prototype",               10, 26 },
    { 34, "Entity/Items/Consumables/Prototypes/FortuneCard/WinterFortuneCard.prototype",                    10, 27 },
    { 35, "Entity/Items/Consumables/Prototypes/GenoshaInfluence200Box.prototype",                            1, 11 },
    { 36, "Entity/Items/Consumables/Prototypes/FortuneCard/SecretInvasionFortuneCard.prototype",            10, 28 },
    { 37, GEM,                                                                                                1,  2 },
    { 38, "Entity/Items/Consumables/Prototypes/GShop/ConsumablesMisc/FC75EternitySplinters.prototype",       1, 14 },
    { 40, "Entity/Items/Consumables/Prototypes/DailyGift/LargeRunebox.prototype",                            1, 12 },
    { 45, "Entity/Items/legendaries/prototypes/legendary030.prototype",                                      1, 15 },
    { 50, "Entity/Items/runewords/glyphs/runewordglyph038.prototype",                                        1, 16 },
    { 55, "Entity/Items/Consumables/Prototypes/GShop/ConsumablesMisc/FCMKII100EternitySplinters.prototype",  1, 17 },
    { 60, "Entity/Items/legendaries/prototypes/legendary029.prototype",                                      1, 18 },
}

hooks.on("DangerRoomRewards", function(e)
    e.ReplaceDefaultRewards = true

    for _, reward in ipairs(REWARDS) do
        local minWaves, path, count, slot = reward[1], reward[2], reward[3], reward[4]
        if e.ClearedWaves >= minWaves then
            e:AddReward(path, count, slot)
        end
    end
end)

--------------------------------------------------------------------------------
-- Run end (the built-in "Danger Room Finished" banner and chat messages are kept)
--------------------------------------------------------------------------------

hooks.on("DangerRoomFinished", function(e)
    log.info(e.PlayerName .. " finished a Danger Room run, cleared waves: " .. e.ClearedWaves)
end)

log.info("Danger Room rules active (" .. #REWARDS .. " bonus rewards, banners up to wave " .. BANNER_MAX_WAVE .. ")")
