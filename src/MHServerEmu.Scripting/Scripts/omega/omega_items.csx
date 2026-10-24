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
const int MaxAffixesPerStat = 0;

// Order: the item first rolls exactly as it normally would at the rarity it dropped with (its normal affixes, by that rarity's
// rules); then it becomes an Omega item and the extra affixes below go on top, past the game's affix limits.
// (Needs the engine's ItemAffixesRollingArgs.AfterNormalRoll; on an older server build the Omega part runs before the normal
// roll as it used to, and the normal roll then only fills in around it with the Omega rarity's rules.)
var afterNormalRoll = typeof(ItemAffixesRollingArgs).GetMethod("AfterNormalRoll");
if (afterNormalRoll == null)
    Log.Warn("This server build has no ItemAffixesRollingArgs.AfterNormalRoll: Omega affixes are added before the normal roll");

void MakeOmega(ItemAffixesRollingArgs e)
{
    if (e.SetOmegaRarity() == false)
    {
        Log.Warn("Omega rarity prototype not found, item left unchanged");
        return;
    }

    e.MaxAffixesPerStat = MaxAffixesPerStat;

    e.SetAffixRestrictions(AffixTier, HeroAffixesOnly);

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
}

// Restrictions on the extra affixes, taken from the game's own affix data (slot, rarity and item type are still ignored):
//  AffixTier       15 = only top tier affixes (tier 15 is what the best armor rolls), 1-14 = that tier, 0 = any tier,
//                  -1 = the item's own tier (artifacts are tier 1 in the game data, so that makes their affixes weak)
//  HeroAffixesOnly skip affixes meant for other heroes (e.g. mental damage on a physical hero)
const int  AffixTier       = 15;
const bool HeroAffixesOnly = true;

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

    // Item level first: the normal roll's affix values follow it
    e.ItemLevel = e.IsLegendary ? LegendaryLevel : e.IsTeamUpGear ? TeamUpGearLevel : GearLevel;

    if (afterNormalRoll != null)
        afterNormalRoll.Invoke(e, new object[] { (Action<ItemAffixesRollingArgs>)MakeOmega });
    else
        MakeOmega(e);
});

Log.Info("Omega item rules active");
