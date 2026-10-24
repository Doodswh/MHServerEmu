// Cosmic artifacts in Omega difficulty.
//
// In the 1.52 data the 85 Cosmic artifacts mostly come from one specific boss each (terminals, endgame dailies), chests, vendors,
// or the Bonus Item Find box (CosmicArtifactsTable, ~48 of them). The patrol bosses' shared Cosmic table has only one (Cargill's,
// Art361Cosmic), and Cosmic / Omega difficulty earn Bonus Item Find points at x1 (Normal x6, Heroic x9, Superheroic x13), so
// Omega players almost never see the others. This script, for Omega regions only:
//  1. raises the region's Bonus Item Find difficulty multiplier (the value the difficulty normally sets), so BIF boxes come
//     as often as on Superheroic;
//  2. gives every player near a boss kill a chance at one random Cosmic artifact from CosmicArtifactsTable.
// The artifacts then go through omega_items.csx like any other Omega gear drop (Omega rarity + stacked affixes).
// Edit the numbers below and save: the script hot reloads.

const int OmegaBonusItemFindMult = 13;    // Superheroic's value; 0 = leave the difficulty's own value (x1)
const int CosmicBonusItemFindMult = 0;    // same for Cosmic difficulty; 0 = unchanged

const float BossArtifactChance = 0.05f;   // per player near the boss, per boss kill
const bool IncludeMiniBosses = false;     // also roll for champions / elites / mini bosses
const float MiniBossArtifactChance = 0.01f;
const float RewardRadius = 3000f;         // players this close to the kill roll (loot is per player)

const string CosmicArtifactsTable = "Loot/Tables/Mob/Bosses/CosmicArtifactsTable.prototype";

if (ScriptRewards.IsValidLootTable(CosmicArtifactsTable) == false)
    Log.Warn($"[omega artifacts] loot table not found: {CosmicArtifactsTable}");

bool IsCosmicDifficulty(Region region) =>
    GameDatabase.GetPrototypeName(region.DifficultyTierRef).Contains("Tier4Cosmic", StringComparison.OrdinalIgnoreCase);

// 1. Bonus Item Find: the region copies its difficulty's multiplier into this property when it is created; set it again here
Hooks.On(ScriptHooks.PlayerEnteredRegion, e =>
{
    Region region = e.Region;
    if (region == null) return;

    int mult = region.IsOmegaDifficulty ? OmegaBonusItemFindMult : IsCosmicDifficulty(region) ? CosmicBonusItemFindMult : 0;
    if (mult <= 0) return;

    if (region.Properties[PropertyEnum.BonusItemFindBonusDifficultyMult] != mult)
    {
        region.Properties[PropertyEnum.BonusItemFindBonusDifficultyMult] = mult;
        Log.Info($"[omega artifacts] {region.PrototypeName}: Bonus Item Find difficulty multiplier set to x{mult}");
    }
});

// 2. Boss kills: a chance at one random Cosmic artifact for each player nearby
IEnumerable<Player> PlayersNear(Region region, Vector3 position, float radius)
{
    foreach (Player player in new PlayerIterator(region))
    {
        Avatar avatar = player.CurrentAvatar;
        if (avatar != null && avatar.IsInWorld && avatar.Region == region &&
            Vector3.Distance2D(avatar.RegionLocation.Position, position) <= radius)
            yield return player;
    }
}

Hooks.On(ScriptHooks.EntityKilled, e =>
{
    if (e.VictimIsAvatar || e.IsOmega == false)
        return;

    RankPrototype rank = e.Victim.GetRankPrototype();
    if (rank == null) return;

    float chance = rank.IsRankBoss ? BossArtifactChance
                 : IncludeMiniBosses && rank.IsRankChampionOrEliteOrMiniBoss ? MiniBossArtifactChance
                 : 0f;
    if (chance <= 0f) return;

    Region region = e.Victim.Region;
    foreach (Player player in PlayersNear(region, e.Victim.RegionLocation.Position, RewardRadius).ToList())
    {
        if (Random.Shared.NextSingle() >= chance) continue;
        if (ScriptRewards.DropLootTable(player, CosmicArtifactsTable))
            Log.Info($"[omega artifacts] {player.GetName()} got a Cosmic artifact from {e.VictimName}");
    }
});

Log.Info($"Omega Cosmic artifacts active (BIF x{OmegaBonusItemFindMult}, {BossArtifactChance:P0} per boss kill)");
