// Omega difficulty items: gear that drops in Omega difficulty becomes an Omega-rarity item with stacked extra affixes.
// (Moved here from LootUtilities.cs.) Runs for every generated item, so it stays a C# script and bails out early.
//
// The engine still keeps Omega items' stacked affixes when affixes are copied (crafting / upgrades); that part is in
// LootUtilities.CopyAffixSpecs and only depends on the item having the Omega rarity.
// If this script is removed or fails to load, Omega drops become normal items (the server logs a warning at startup).
//
// Team-up gear: the regular affixes added here (prefix / suffix / cosmic / unique...) apply to the HERO that owns the team-up,
// not the team-up wearing the item (engine rule: Item.AppliesAffixToOwnerAvatar). Team-up affixes keep their normal target.

// Item types / names that never become Omega items
const string ExcludedNameWords = "Rune,Uru,Token,Credit,Currency,Bundle,Box,Chest";

// Item levels
const int LegendaryLevel = 90;
const int TeamUpGearLevel = 60;   // for affix values only: the engine makes team-up gear equippable at level 1 (Item.ApplyItemSpecProperties)
const int GearLevel = 75;

// Stacking caps for the stacked affixes: copies of one affix, of one proc affix, and affixes touching one stat
const int MaxStacksPerAffix = 2;
const int MaxStacksPerProcAffix = 1;
const int MaxStacksPerStat = 2;

// Most affixes touching the same stat (Fighting, Brutal Strike damage, ...) on the FINISHED item, counting the game's normal roll,
// built-in and unique affixes too. Extra prefix / suffix / cosmic affixes over this are dropped. 0 = no cap.
const int MaxAffixesPerStat = 2;

Hooks.On(ScriptHooks.ItemAffixesRolling, e =>
{
    // Cheapest checks first: this runs for every generated item
    if (e.IsOmegaDifficulty == false)
        return;

    // Gems have their own rules (engine, LootUtilities), and an item another script fully built is left alone
    if (e.IsGem || e.SkipNormalAffixes)
        return;

    // Exclusions: crafting materials, costumes, stackables (relics, currencies, splinters, fragments), and a name safety net
    if (e.IsCraftingIngredient || e.IsCostume || e.IsStackable)
        return;

    if (e.ItemNameContainsAny(ExcludedNameWords))
        return;

    // Inclusions: team-up gear, or anything that goes into an equipment slot of the hero it rolled for
    if (e.IsTeamUpGear == false && e.HasEquipmentSlot == false)
        return;

    if (e.SetOmegaRarity() == false)
    {
        Log.Warn("Omega rarity prototype not found, item left unchanged");
        return;
    }

    e.ItemLevel = e.IsLegendary ? LegendaryLevel : e.IsTeamUpGear ? TeamUpGearLevel : GearLevel;
    e.MaxAffixesPerStat = MaxAffixesPerStat;

    // Stacked affixes (repeats allowed within the caps, shared across these four calls)
    e.SetStackingLimits(MaxStacksPerAffix, MaxStacksPerProcAffix, MaxStacksPerStat);
    e.AddStackedAffixes(AffixPosition.Cosmic, 2);
    e.AddStackedAffixes(AffixPosition.Prefix, 6);
    e.AddStackedAffixes(AffixPosition.Suffix, 6);
    e.AddStackedAffixes(AffixPosition.Socket1, 1);

    // One of each
    e.AddUniqueAffixes(AffixPosition.Runeword, 2);
    e.AddUniqueAffixes(AffixPosition.Blessing, 2);
    e.AddUniqueAffixes(AffixPosition.Unique, 2);
    e.AddUniqueAffixes(AffixPosition.Ultimate, 2);
});

Log.Info("Omega item rules active");
