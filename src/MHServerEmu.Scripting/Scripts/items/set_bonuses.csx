// Gear set bonuses (test sets). Wearing several pieces of a set grants stacking bonuses at 2 / 3 / 4 pieces.
//
// How it works:
//  - Bonuses are real stats and procs applied by ScriptBonuses, so the server's combat math uses them: damage, damage
//    taken, crit / Brutal Strike procs.
//  - On heroes the stats are sent to the client too (clientVisible: an invisible condition carries them), so stats the client
//    needs to know work: area sizes, cooldowns, charges. Procs stay server-side. Team-ups get server-side bonuses only.
//  - Procs reuse existing proc powers from the game data (their visuals, damage and cooldowns come from that power).
//  - Each set piece's tooltip flavor text (the italic quote) is replaced with the set description. Text changes show
//    after a reconnect.
//  - A set can be limited to some heroes, and a tier can add a client visual borrowed from a power's condition (ScriptConditions),
//    e.g. Unstoppable Juggernauts (Hulk / Juggernaut) makes you giant at 4 pieces.
//
//  - A tier can also give a mirror image (ScriptMirrorImages): an ally that looks like your hero, has your powers, talents
//    and traits and fights beside you. It appears when you hit an enemy, one at a time, e.g. Legion of One at 4 pieces.
//
//   !sets    show your set pieces and active bonuses
//
// Test: !item givemaxlevel Art362   (and the other piece names below), equip them in your artifact slots.
//       Midtown Rogues' Gallery: Art362, Art366, Art360, Art067SuperHeroic (Cosmic / Personal versions count too), any hero.
//       Unstoppable Juggernauts: Art208, Art267, Art181, Art225 (Cosmic / Takedown versions count too) on Hulk or Juggernaut.
//       Legion of One: Art275, Art277 (or Art278 / Art279), Art313, Art314 (Cosmic / Takedown versions count too), any hero.
//       Legend of Punchclops: Cyclops uniques Unique021 (or Unique555), Unique070, Unique112, Unique068, Unique111; any 4, on Cyclops.
//       Director of S.H.I.E.L.D.: Nick Fury uniques Unique506, Unique507, Unique508, Unique509, Unique510; any 4, on Nick Fury.
//       Sentinel of Liberty: Captain America uniques Unique007 (or Unique008), Unique091, Unique105, Unique106, Unique107; any 4.
//       Invincible Iron Man: Unique003, Unique030 (or Unique329), Unique063 (or Unique124), Unique121, Unique125; any 4, on Iron Man.
//       Spectacular Spider-Man: Unique040 (or Unique251), Unique041, Unique077, Unique138 (or Unique205), Unique139; any 4.
//       Photographic Reflexes: Taskmaster uniques Unique239, Unique240, Unique241, Unique242, Unique243; any 4, on Taskmaster.
//       Flame On!: Human Torch uniques Unique028, Unique029, Unique094, Unique122, Unique123; any 4, on Human Torch.
//       Omega-Level Iceman: Iceman uniques Unique394, Unique395, Unique396, Unique397, Unique398; any 4, on Iceman.

using System.Runtime.CompilerServices;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Powers;

//------------------------------------------------------------------------------
// Sets
//------------------------------------------------------------------------------

const string Artifacts = "Entity/Items/Artifacts/Prototypes/";
const string ItemPowers = "Powers/ItemPowers/";
const string HulkPowers = "Powers/Player/Hulk/Rework/";
const string JuggernautPowers = "Powers/Player/Juggernaut/";

// Adds +amount (0.25 = +25%) damage for each of the named powers, the same way hero talents boost a power
static void BoostPowers(PropertyCollection bonus, float amount, params string[] powerPaths)
{
    foreach (string path in powerPaths)
    {
        PrototypeId powerRef = GameDatabase.GetPrototypeRefByName(path);
        if (powerRef == PrototypeId.Invalid)
            continue;

        bonus[PropertyEnum.DamageMultForPower, powerRef] = amount;
    }
}

// Adds +amount (0.25 = +25%) damage for summoned allies: the bonus for Summon powers that summoner artifacts use (as a
// percentage), plus the pet damage bonus that pets are given when they are summoned
static void BoostSummons(PropertyCollection bonus, float amount)
{
    PrototypeId summonKeyword = GameDatabase.GetPrototypeRefByName("Powers/Blueprints/Keywords/Summon.defaults");
    if (summonKeyword != PrototypeId.Invalid)
        bonus[PropertyEnum.DamagePctBonusForPowerKeyword, summonKeyword] = amount;

    bonus[PropertyEnum.PetDamagePctBonus] = amount;
}

// Adds +amount (0.25 = +25%) damage for every power with the keyword (e.g. Melee), the same way talents boost a power
static void BoostKeyword(PropertyCollection bonus, float amount, string keywordPath)
{
    PrototypeId keywordRef = GameDatabase.GetPrototypeRefByName(keywordPath);
    if (keywordRef != PrototypeId.Invalid)
        bonus[PropertyEnum.DamageMultForPowerKeyword, keywordRef] = amount;
}

// Powers with the keyword reach +amount further (0.30 = 30%). Only powers that have a range: not Melee and not areas
// centered on you (the server's Power.GetRange rule)
static void ExtendRange(PropertyCollection bonus, float amount, string keywordPath)
{
    PrototypeId keywordRef = GameDatabase.GetPrototypeRefByName(keywordPath);
    if (keywordRef != PrototypeId.Invalid)
        bonus[PropertyEnum.RangeModifierPctKeyword, keywordRef] = amount;
}

// Areas of powers with the keyword are +amount bigger (0.40 = 40%)
static void GrowAreas(PropertyCollection bonus, float amount, string keywordPath)
{
    PrototypeId keywordRef = GameDatabase.GetPrototypeRefByName(keywordPath);
    if (keywordRef != PrototypeId.Invalid)
        bonus[PropertyEnum.AOESizePctModifierKeyword, keywordRef] = amount;
}

// No cooldown reduction in sets: the server already has a 50% cooldown reduction built in.

// Powers with the keyword get +count charges (a power without charges of its own becomes a 2-charge power)
static void ExtraCharges(PropertyCollection bonus, int count, string keywordPath)
{
    PrototypeId keywordRef = GameDatabase.GetPrototypeRefByName(keywordPath);
    if (keywordRef != PrototypeId.Invalid)
        bonus[PropertyEnum.PowerChargesMaxBonusForKwd, keywordRef] = count;
}

// Projectiles of powers with the keyword have a chance to pass through enemies and keep going (0.5 = 50%)
static void PiercingMissiles(PropertyCollection bonus, float chance, string keywordPath)
{
    PrototypeId keywordRef = GameDatabase.GetPrototypeRefByName(keywordPath);
    if (keywordRef != PrototypeId.Invalid)
        bonus[PropertyEnum.MissilePierceChance, keywordRef] = Math.Clamp(chance, 0f, 1f);
}

// Like ScriptBonuses.AddProc, but the proc only fires for powers with the keyword: the way unique items write
// "chance to stun when you hit with a Melee power" (ProcKeyword[trigger, proc power, keyword])
static void AddKeywordProc(PropertyCollection bonus, ProcTriggerType trigger, string powerPath, string keywordPath, float chance)
{
    PrototypeId powerRef = GameDatabase.GetPrototypeRefByName(powerPath);
    PrototypeId keywordRef = GameDatabase.GetPrototypeRefByName(keywordPath);
    if (powerRef == PrototypeId.Invalid || keywordRef == PrototypeId.Invalid)
        return;

    PropertyId procId = new(PropertyEnum.ProcKeyword, (PropertyParam)(int)trigger,
        Property.ToParam(PropertyEnum.ProcKeyword, 1, powerRef), Property.ToParam(PropertyEnum.ProcKeyword, 2, keywordRef));
    bonus[procId] = Math.Clamp(chance, 0f, 1f);
}

const string MeleeKeyword = "Powers/Blueprints/Keywords/Melee.defaults";
const string RangedKeyword = "Powers/Blueprints/Keywords/Ranged.defaults";
const string CyclopsUniques = "Entity/Items/Armor/UniquePrototypes/Avatars/Cyclops/";
const string FuryUniques = "Entity/Items/Armor/UniquePrototypes/Avatars/NickFury/";
const string FuryPowers = "Powers/Player/NickFury/";
const string PhysicalKeyword = "Powers/Blueprints/Keywords/Physical.defaults";
const string ShieldThrowKeyword = "Powers/Blueprints/Keywords/ShieldThrow.defaults";
const string CapUniques = "Entity/Items/Armor/UniquePrototypes/Avatars/CaptainAmerica/";
const string EnergyKeyword = "Powers/Blueprints/Keywords/Energy.defaults";
const string MovementKeyword = "Powers/Blueprints/Keywords/Movement.defaults";
const string AreaKeyword = "Powers/Blueprints/Keywords/Area.defaults";
const string IronManUniques = "Entity/Items/Armor/UniquePrototypes/Avatars/IronMan/";
const string SpiderManUniques = "Entity/Items/Armor/UniquePrototypes/Avatars/SpiderMan/";
const string TaskmasterUniques = "Entity/Items/Armor/UniquePrototypes/Avatars/Taskmaster/";
const string HumanTorchUniques = "Entity/Items/Armor/UniquePrototypes/Avatars/HumanTorch/";
const string IcemanUniques = "Entity/Items/Armor/UniquePrototypes/Avatars/Iceman/";

// Debuffs carrying these keywords (e.g. freezes, chills) last 0 seconds on the wearer: StatusResistByDurationPctKwd is
// matched against the debuff's own keywords, 100% = no duration
static void ImmuneToDebuffKeywords(PropertyCollection bonus, params string[] keywordPaths)
{
    foreach (string path in keywordPaths)
    {
        PrototypeId keywordRef = GameDatabase.GetPrototypeRefByName(path);
        if (keywordRef != PrototypeId.Invalid)
            bonus[PropertyEnum.StatusResistByDurationPctKwd, keywordRef] = 1f;
    }
}

// Makes the wearer take no damage from powers with these keywords (enemy fire attacks, burns, hellfire). Fire is Energy
// damage in this game, so immunity has to go by keyword: DamagePctResistVsPowerKeyword (100% = no damage, not capped)
static void ImmuneToKeywords(PropertyCollection bonus, params string[] keywordPaths)
{
    foreach (string path in keywordPaths)
    {
        PrototypeId keywordRef = GameDatabase.GetPrototypeRefByName(path);
        if (keywordRef != PrototypeId.Invalid)
            bonus[PropertyEnum.DamagePctResistVsPowerKeyword, keywordRef] = 1f;
    }
}

var Sets = new List<GearSet>
{
    // Four Midtown villains' artifacts. Offensive, built around crits and Brutal Strikes.
    new GearSet("Midtown Rogues' Gallery",
        pieces: new[]
        {
            // Regular, Cosmic and Personal versions all count
            ("Bullseye's Deck of Cards",       Artifacts + "Tier1Artifacts/Art362.prototype|" +
                                               Artifacts + "SpecialArtifacts/CosmicArtifacts/Art362Cosmic.prototype"),
            ("Vulture's Mechanical Wings",     Artifacts + "Tier1Artifacts/Art366.prototype|" +
                                               Artifacts + "SpecialArtifacts/CosmicArtifacts/Art366Cosmic.prototype|" +
                                               Artifacts + "SpecialArtifacts/TakedownArtifacts/TakedownArt366.prototype"),
            ("Wizard's Id Machine",            Artifacts + "SpecialArtifacts/Art360.prototype|" +
                                               Artifacts + "SpecialArtifacts/CosmicArtifacts/Art360Cosmic.prototype"),
            ("Nefarious Mask of Doom",         Artifacts + "SpecialArtifacts/Art067SuperHeroic.prototype|" +
                                               Artifacts + "SpecialArtifacts/Unused/Art067.prototype|" +
                                               Artifacts + "SpecialArtifacts/Unused/Art067Heroic.prototype|" +
                                               Artifacts + "SpecialArtifacts/CosmicArtifacts/Art067Cosmic.prototype"),
        },
        tiers: new[]
        {
            new SetTier(2, "+10% Damage vs Bosses",
                bonus => bonus[PropertyEnum.DamagePctBonusVsBosses] = 0.10f),

            new SetTier(3, "25% chance on Critical Hit to gain the Deadly Assassin Aura (Damage vs Bosses, Critical and Brutal Strike Rating, Health Regeneration for you and allies)",
                bonus => ScriptBonuses.AddProc(bonus, ProcTriggerType.OnCrit, ItemPowers + "InsigniaPowers/InsigniaRework/Insignia121ProcEffect.prototype", 0.25f)),

            new SetTier(4, "35% chance on Brutal Strike to blast your target with chain lightning that hits up to 8 enemies",
                bonus => ScriptBonuses.AddProc(bonus, ProcTriggerType.OnSuperCrit, ItemPowers + "UniquesPowers/Unique399ChainLightning.prototype", 0.35f)),
        }),

    // Any four of the eight Bloodstone beast artifacts. Defensive.
    new GearSet("Bloodstone Menagerie",
        pieces: new[]
        {
            ("Bloodstone Lion",      Artifacts + "Bloodstone/Art012.prototype"),
            ("Bloodstone Panther",   Artifacts + "Bloodstone/Art060.prototype"),
            ("Bloodstone Raptor",    Artifacts + "Bloodstone/Art061.prototype"),
            ("Bloodstone Jaguar",    Artifacts + "Bloodstone/Art062.prototype"),
            ("Bloodstone Porcupine", Artifacts + "Bloodstone/Art063.prototype"),
            ("Bloodstone Mammoth",   Artifacts + "Bloodstone/Art064.prototype"),
            ("Bloodstone Eel",       Artifacts + "Bloodstone/Art065.prototype"),
            ("Bloodstone Eagle",     Artifacts + "Bloodstone/Art066.prototype"),
        },
        tiers: new[]
        {
            new SetTier(2, "5% Damage Reduction",
                bonus => bonus[PropertyEnum.DamagePctResist, DamageType.Any] = 0.05f),

            new SetTier(3, "When you drop below 30% Health, gain Damage Reduction and Health Regeneration (has a cooldown)",
                bonus => ScriptBonuses.AddProc(bonus, ProcTriggerType.OnHealthBelow, ItemPowers + "ArtifactPowers/Art270DamageReductionProc.prototype", 1f, 30)),

            new SetTier(4, "25% chance on Critical Hit to summon a squirrel to fight for you (1 minute cooldown)",
                bonus => ScriptBonuses.AddProc(bonus, ProcTriggerType.OnCrit, ItemPowers + "ArtifactPowers/Art054SquirrelSummonProc.prototype", 0.25f)),
        }),

    // Four brute-themed artifacts; only Hulk and Juggernaut get the bonuses. The 4 piece bonus makes you giant: it borrows
    // the client visual of Ant-Man's Giant-Man grow (4x size), applied with no stats of its own.
    new GearSet("Unstoppable Juggernauts",
        pieces: new[]
        {
            ("White Suit Jacket",                    Artifacts + "SpecialArtifacts/Art208.prototype|" +
                                                     Artifacts + "SpecialArtifacts/CosmicArtifacts/Art208Cosmic.prototype|" +
                                                     Artifacts + "SpecialArtifacts/TakedownArtifacts/TakedownArt208.prototype"),
            ("Hulkbuster Physics Package",           Artifacts + "Tier1Artifacts/Art267.prototype|" +
                                                     Artifacts + "SpecialArtifacts/CosmicArtifacts/Art267Cosmic.prototype|" +
                                                     Artifacts + "SpecialArtifacts/TakedownArtifacts/TakedownArt267.prototype"),
            ("Advanced Crimson Crystal of Cyttorak", Artifacts + "Tier1Artifacts/Art181.prototype"),
            ("Advanced Circlet of Cyttorak",         Artifacts + "Tier1Artifacts/Art225.prototype|" +
                                                     Artifacts + "SpecialArtifacts/CosmicArtifacts/Art225Cosmic.prototype|" +
                                                     Artifacts + "SpecialArtifacts/TakedownArtifacts/TakedownArt225.prototype"),
        },
        heroes: new[]
        {
            ("Hulk",       "Entity/Characters/Avatars/Shipping/Hulk.prototype"),
            ("Juggernaut", "Entity/Characters/Avatars/Shipping/Juggernaut.prototype"),
        },
        tiers: new[]
        {
            // Power boosts name the powers that deal the damage (a power's hit is often a separate "combo" power, with a
            // second copy for Hulk's Very Angry state). Each tier lists both heroes; only your hero's powers matter.
            new SetTier(2, "5% Damage Reduction. +25% damage with Gamma Strike (Hulk) / Sunday Punch (Juggernaut)",
                bonus =>
                {
                    bonus[PropertyEnum.DamagePctResist, DamageType.Any] = 0.05f;
                    BoostPowers(bonus, 0.25f,
                        HulkPowers + "GammaPunch.prototype",
                        JuggernautPowers + "SundayPunch.prototype",
                        JuggernautPowers + "SundayPunchFullSpender.prototype");
                }),

            new SetTier(3, "+10% Damage vs Bosses. +25% damage with Hulk Smash! and Avalanche Leap (Hulk) / Enter the Fray and Big Elbow Drop (Juggernaut)",
                bonus =>
                {
                    bonus[PropertyEnum.DamagePctBonusVsBosses] = 0.10f;
                    BoostPowers(bonus, 0.25f,
                        HulkPowers + "PBAoESlamImpactBase.prototype",
                        HulkPowers + "PBAoESlamImpactVeryAngry.prototype",
                        HulkPowers + "LeapQuakeEnd.prototype",
                        HulkPowers + "LeapQuakeEndVeryAngry.prototype",
                        JuggernautPowers + "EarthquakeLeapEnd.prototype",
                        JuggernautPowers + "PeoplesElbowEnd.prototype");
                }),

            new SetTier(4, "Giant size: you grow to twice your size and your area powers reach 30% further. Another 5% Damage Reduction. " +
                           "+30% damage with Ultimate Destruction (Hulk) / Wrath of Cyttorak (Juggernaut)",
                bonus =>
                {
                    bonus[PropertyEnum.DamagePctResist, DamageType.Any] = 0.10f;
                    bonus[PropertyEnum.AOESizePctModifier] = 0.30f;
                    BoostPowers(bonus, 0.30f,
                        HulkPowers + "Clap.prototype",
                        HulkPowers + "ClapHandDamageCombo.prototype",
                        JuggernautPowers + "WrathOfCyttorak.prototype");
                },
                // The client only has fixed size effects: 4x (Giant-Man), 1.5x (Juggernaut's ultimate), 1.3x (Deadpool's Hulk-out).
                // There is no 2x, so this stacks 1.5x and 1.3x for about 1.95x. For the old 4x giant use
                // "Powers/Player/AntMan/GiantManFootGrowConditionEffect.prototype" instead.
                visualPower: "Powers/Player/Juggernaut/Ultimate.prototype|Powers/Player/Deadpool/Rework/PowerUpHulkDollEffect.prototype"),
        }),

    // The four summoner artifacts, for any hero. Summoned allies hit harder, and at 4 pieces you summon yourself.
    new GearSet("Legion of One",
        pieces: new[]
        {
            ("Iron Legion Armor Sheath", Artifacts + "Tier1Artifacts/Art275.prototype|" +
                                         Artifacts + "SpecialArtifacts/CosmicArtifacts/Art275Cosmic.prototype|" +
                                         Artifacts + "SpecialArtifacts/TakedownArtifacts/TakedownArt275.prototype"),
            ("Ultron Encephalo-Beam",    Artifacts + "SpecialArtifacts/Art277.prototype|" +
                                         Artifacts + "SpecialArtifacts/Art278.prototype|" +
                                         Artifacts + "Tier1Artifacts/Art279.prototype|" +
                                         Artifacts + "SpecialArtifacts/CosmicArtifacts/Art277Cosmic.prototype|" +
                                         Artifacts + "SpecialArtifacts/CosmicArtifacts/Art278Cosmic.prototype|" +
                                         Artifacts + "SpecialArtifacts/CosmicArtifacts/Art279Cosmic.prototype|" +
                                         Artifacts + "SpecialArtifacts/TakedownArtifacts/TakedownArt277.prototype|" +
                                         Artifacts + "SpecialArtifacts/TakedownArtifacts/TakedownArt278.prototype|" +
                                         Artifacts + "SpecialArtifacts/TakedownArtifacts/TakedownArt279.prototype"),
            ("Sharabus Neotum",          Artifacts + "SpecialArtifacts/Art313.prototype|" +
                                         Artifacts + "SpecialArtifacts/Art313a.prototype|" +
                                         Artifacts + "SpecialArtifacts/Art313b.prototype"),
            ("Mole Man's Staff",         Artifacts + "Tier1Artifacts/Art314.prototype|" +
                                         Artifacts + "SpecialArtifacts/CosmicArtifacts/Art314Cosmic.prototype|" +
                                         Artifacts + "SpecialArtifacts/TakedownArtifacts/PersonalArt314.prototype"),
        },
        tiers: new[]
        {
            // Each tier states the total, not an addition to the tier before it
            new SetTier(2, "+15% Summoned Ally Damage",
                bonus => BoostSummons(bonus, 0.15f)),

            new SetTier(3, "+30% Summoned Ally Damage",
                bonus => BoostSummons(bonus, 0.30f)),

            new SetTier(4, "+50% Summoned Ally Damage. When you hit an enemy, a Mirror Image of your hero joins the fight for 45 seconds " +
                           "with all your powers, talents and traits (15 second cooldown)",
                bonus => BoostSummons(bonus, 0.50f),
                mirrorCooldownSeconds: 15f,
                mirror: new MirrorImageOptions
                {
                    LifespanSeconds = 45f,
                    DamageScale     = 1f,      // multiplies every hit of the image (1 = its powers' own damage at your level, no gear)
                    MaxPowers       = 0,       // all of your activated powers
                    CopyTalents     = true,
                    CopyTraits      = true,
                    AI = new CombatAIOptions
                    {
                        MoveSpeedScale = 1.25f,
                    },
                }),
        }),

    // Cyclops only, built from his uniques (one per gear slot; any 4 of the 5 complete it). The community joke
    // "Punchclops" (the game's own Heroism of Punchclops achievement and Legacy of Punchclops unique): ignore the optic
    // blasts and punch everything. Boosts every Melee power: Punch Combo, Tactical Kicks, Sweeping Kicks, Combat Roll.
    // Both Punchclops uniques are gloves, so either one is the glove piece (they already stun on Melee hits). The 4 piece
    // proc is Fist of Apocalypse's: attack speed on Melee hits.
    new GearSet("Legend of Punchclops",
        pieces: new[]
        {
            ("Legacy or Heroism of Punchclops", CyclopsUniques + "Unique021.prototype|" +
                                                CyclopsUniques + "Unique555.prototype"),
            ("Ruby Quartz Sunglasses",          CyclopsUniques + "Unique070.prototype"),
            ("Headmaster's Leather Jacket",     CyclopsUniques + "Unique112.prototype"),
            ("Clan Rebellion Bodysuit",         CyclopsUniques + "Unique068.prototype"),
            ("X-Factor Mutant Hunter Boots",    CyclopsUniques + "Unique111.prototype"),
        },
        heroes: new[]
        {
            ("Cyclops", "Entity/Characters/Avatars/Shipping/Cyclops.prototype"),
        },
        tiers: new[]
        {
            // Each tier states the total, not an addition to the tier before it
            new SetTier(2, "+25% damage with Melee powers",
                bonus => BoostKeyword(bonus, 0.25f, MeleeKeyword)),

            new SetTier(3, "+50% damage with Melee powers. 10% Damage Reduction while you are in their face",
                bonus =>
                {
                    BoostKeyword(bonus, 0.50f, MeleeKeyword);
                    bonus[PropertyEnum.DamagePctResist, DamageType.Any] = 0.10f;
                }),

            new SetTier(4, "Punchclops: +100% damage with Melee powers, 10% Damage Reduction, +10% Damage vs Bosses, and a 25% chance " +
                           "when you hit with a Melee power to gain Attack Speed for 10 seconds",
                bonus =>
                {
                    BoostKeyword(bonus, 1.00f, MeleeKeyword);
                    bonus[PropertyEnum.DamagePctResist, DamageType.Any] = 0.10f;
                    bonus[PropertyEnum.DamagePctBonusVsBosses] = 0.10f;
                    AddKeywordProc(bonus, ProcTriggerType.OnAnyHit, ItemPowers + "ArtifactPowers/Art322AttackSpeedProc.prototype", MeleeKeyword, 0.25f);
                }),
        }),

    // Nick Fury only, built from his uniques (one per gear slot; any 4 of the 5 complete it). His three sides: the guns
    // (every Ranged power), the S.H.I.E.L.D. agents he calls in (summons), and the assassin finish (Execute and the sniper
    // shots). The 4 piece proc is the Leather Trenchcoat's own S.H.I.E.L.D. sniper, here on every Critical Hit.
    new GearSet("Director of S.H.I.E.L.D.",
        pieces: new[]
        {
            ("Personalized Needle Pistol",       FuryUniques + "Unique506.prototype"),
            ("9-ply Kevlar S.H.I.E.L.D Uniform", FuryUniques + "Unique507.prototype"),
            ("Leather Trenchcoat",               FuryUniques + "Unique508.prototype"),
            ("The Watcher's Eye",                FuryUniques + "Unique509.prototype"),
            ("Classic Machine Gun",              FuryUniques + "Unique510.prototype"),
        },
        heroes: new[]
        {
            ("Nick Fury", "Entity/Characters/Avatars/Shipping/NickFury.prototype"),
        },
        tiers: new[]
        {
            // Each tier states the total, not an addition to the tier before it
            new SetTier(2, "+25% damage with Ranged powers. +20% Summoned Ally Damage",
                bonus =>
                {
                    BoostKeyword(bonus, 0.25f, RangedKeyword);
                    BoostSummons(bonus, 0.20f);
                }),

            new SetTier(3, "+50% damage with Ranged powers. +40% Summoned Ally Damage. 10% Damage Reduction",
                bonus =>
                {
                    BoostKeyword(bonus, 0.50f, RangedKeyword);
                    BoostSummons(bonus, 0.40f);
                    bonus[PropertyEnum.DamagePctResist, DamageType.Any] = 0.10f;
                }),

            new SetTier(4, "Director's Orders: +100% damage with Ranged powers, +75% Summoned Ally Damage, 10% Damage Reduction, " +
                           "+15% Damage vs Bosses, +50% damage with Execute and sniper shots, and a 30% chance on Critical Hit for a " +
                           "S.H.I.E.L.D. sniper to fire on your target",
                bonus =>
                {
                    BoostKeyword(bonus, 1.00f, RangedKeyword);
                    BoostSummons(bonus, 0.75f);
                    bonus[PropertyEnum.DamagePctResist, DamageType.Any] = 0.10f;
                    bonus[PropertyEnum.DamagePctBonusVsBosses] = 0.15f;
                    BoostPowers(bonus, 0.50f,
                        FuryPowers + "ExecuteHit.prototype",
                        FuryPowers + "SniperShot.prototype",
                        FuryPowers + "EyesEverywhereSniperShots.prototype");
                    ScriptBonuses.AddProc(bonus, ProcTriggerType.OnCrit, ItemPowers + "UniquesPowers/Unique508SniperProc.prototype", 0.30f);
                }),
        }),

    // Captain America only, built from his uniques (one per gear slot; either shield counts; any 4 of the 5 complete it).
    // Nearly his whole kit is Physical, the shield throws have their own keyword, and he leads from the front: tougher,
    // with the Invaders backing him up. The procs are his own uniques' powers: the Zero-Point Energy Shield's energy blast
    // (on Shield Throw hits) and the Invaders Photograph's Original Human Torch (when attacked; it keeps its own cooldown).
    new GearSet("Sentinel of Liberty",
        pieces: new[]
        {
            ("Uru-Enhanced or Zero-Point Energy Shield", CapUniques + "Unique007.prototype|" +
                                                         CapUniques + "Unique008.prototype"),
            ("Stark Industries Support Armor",           CapUniques + "Unique091.prototype"),
            ("Invaders Photograph",                      CapUniques + "Unique105.prototype"),
            ("Boondockers",                              CapUniques + "Unique106.prototype"),
            ("The Captain's Mask",                       CapUniques + "Unique107.prototype"),
        },
        heroes: new[]
        {
            ("Captain America", "Entity/Characters/Avatars/Shipping/CaptainAmerica.prototype"),
        },
        tiers: new[]
        {
            // Each tier states the total, not an addition to the tier before it
            new SetTier(2, "+25% damage with Physical powers. 5% Damage Reduction",
                bonus =>
                {
                    BoostKeyword(bonus, 0.25f, PhysicalKeyword);
                    bonus[PropertyEnum.DamagePctResist, DamageType.Any] = 0.05f;
                }),

            new SetTier(3, "+50% damage with Physical powers. +50% damage with Shield Throw powers. 10% Damage Reduction",
                bonus =>
                {
                    BoostKeyword(bonus, 0.50f, PhysicalKeyword);
                    BoostKeyword(bonus, 0.50f, ShieldThrowKeyword);
                    bonus[PropertyEnum.DamagePctResist, DamageType.Any] = 0.10f;
                }),

            new SetTier(4, "Avengers Assemble: +100% damage with Physical powers, +75% damage with Shield Throw powers, " +
                           "15% Damage Reduction, +15% Damage vs Bosses, a 30% chance when you hit with a Shield Throw power to " +
                           "unleash a Zero-Point energy blast, and a 25% chance when attacked to call in the Original Human Torch",
                bonus =>
                {
                    BoostKeyword(bonus, 1.00f, PhysicalKeyword);
                    BoostKeyword(bonus, 0.75f, ShieldThrowKeyword);
                    bonus[PropertyEnum.DamagePctResist, DamageType.Any] = 0.15f;
                    bonus[PropertyEnum.DamagePctBonusVsBosses] = 0.15f;
                    AddKeywordProc(bonus, ProcTriggerType.OnAnyHit, ItemPowers + "UniquesPowers/Unique008OnAttackedProc.prototype", ShieldThrowKeyword, 0.30f);
                    ScriptBonuses.AddProc(bonus, ProcTriggerType.OnGotAttacked, ItemPowers + "UniquesPowers/Unique105HumanTorchOnAttacked.prototype", 0.25f);
                }),
        }),

    // Iron Man only, built from his uniques (one per gear slot; either armor and either belt counts; any 4 of the 5).
    // Long-range artillery: his repulsors, Unibeam and missiles are Energy, so Energy powers reach further, punch through
    // crowds and blast bigger areas. The 4 piece safety net is Tony Stark's Checkbook's invulnerability
    // when hit (it keeps its own 25 second cooldown).
    new GearSet("Invincible Iron Man",
        pieces: new[]
        {
            ("Experimental Overcharged Repulsors",         IronManUniques + "Unique003.prototype"),
            ("Uru-Infused or Hulkbuster-Plated Armor",     IronManUniques + "Unique030.prototype|" +
                                                           IronManUniques + "Unique329.prototype"),
            ("Tony Stark's Checkbook or Extremis Undersheath", IronManUniques + "Unique063.prototype|" +
                                                           IronManUniques + "Unique124.prototype"),
            ("Boots of the Galactic Guardian",             IronManUniques + "Unique121.prototype"),
            ("ST-99 \"Executioner\" Targeting Software",   IronManUniques + "Unique125.prototype"),
        },
        heroes: new[]
        {
            ("Iron Man", "Entity/Characters/Avatars/Shipping/IronMan.prototype"),
        },
        tiers: new[]
        {
            // Each tier states the total, not an addition to the tier before it
            // Each tier includes the ones before it
            new SetTier(2, "Long Range: Energy powers reach 30% further",
                bonus => ExtendRange(bonus, 0.30f, EnergyKeyword)),

            new SetTier(3, "Armor-Piercing Repulsors: +50% damage with Energy powers, and Energy projectiles have a 50% chance " +
                           "to pierce through enemies",
                bonus =>
                {
                    BoostKeyword(bonus, 0.50f, EnergyKeyword);
                    PiercingMissiles(bonus, 0.50f, EnergyKeyword);
                }),

            new SetTier(4, "Repulsor Barrage: +100% damage with Energy powers, Energy areas are 40% bigger, and a 15% chance " +
                           "when attacked to become invulnerable for a moment",
                bonus =>
                {
                    BoostKeyword(bonus, 1.00f, EnergyKeyword);   // replaces the 3 piece +50%
                    GrowAreas(bonus, 0.40f, EnergyKeyword);
                    ScriptBonuses.AddProc(bonus, ProcTriggerType.OnGotAttacked, ItemPowers + "UniquesPowers/Unique063InvulnOnGotHit.prototype", 0.15f);
                }),
        }),

    // Spider-Man only, built from his uniques (one per gear slot; either web-shooters and either gloves count; any 4 of
    // the 5). The acrobat: faster hands, webs that reach further, and swings / leaps (Movement powers) with an extra charge
    // that go further. The 4 piece proc is the Superior Clawed Gloves' bleed (stacks into Superior Senses).
    new GearSet("Spectacular Spider-Man",
        pieces: new[]
        {
            ("Horizon Labs or Parker Industries Web-Shooters", SpiderManUniques + "Unique040.prototype|" +
                                                               SpiderManUniques + "Unique251.prototype"),
            ("Shadowed Mask of Kaine",                         SpiderManUniques + "Unique041.prototype"),
            ("Iron Spider Breastplate",                        SpiderManUniques + "Unique077.prototype"),
            ("Superior Clawed Gloves or Spider-Bots",          SpiderManUniques + "Unique138.prototype|" +
                                                               SpiderManUniques + "Unique205.prototype"),
            ("Spider-Armor MK II Boots",                       SpiderManUniques + "Unique139.prototype"),
        },
        heroes: new[]
        {
            ("Spider-Man", "Entity/Characters/Avatars/Shipping/SpiderMan.prototype"),
        },
        tiers: new[]
        {
            // Each tier states the total, not an addition to the tier before it
            // Each tier includes the ones before it
            new SetTier(2, "Web-Slinger: +15% Attack Speed, and Ranged (web) powers reach 40% further",
                bonus =>
                {
                    bonus[PropertyEnum.CastSpeedIncrPct] = 0.15f;
                    ExtendRange(bonus, 0.40f, RangedKeyword);
                }),

            new SetTier(3, "Swing Line: Movement powers gain an extra charge and reach 30% further",
                bonus =>
                {
                    ExtraCharges(bonus, 1, MovementKeyword);
                    ExtendRange(bonus, 0.30f, MovementKeyword);
                }),

            new SetTier(4, "Spider-Sense: +75% damage with Physical powers, +25% Attack Speed in total, and a 30% chance when " +
                           "you hit with a Melee power to make your target bleed (stacks into Superior Senses)",
                bonus =>
                {
                    BoostKeyword(bonus, 0.75f, PhysicalKeyword);
                    bonus[PropertyEnum.CastSpeedIncrPct] = 0.25f;   // replaces the 2 piece +15%
                    AddKeywordProc(bonus, ProcTriggerType.OnAnyHit, ItemPowers + "UniquesPowers/Unique138BleedOnHitMelee.prototype", MeleeKeyword, 0.30f);
                }),
        }),

    // Taskmaster only, built from his uniques (one per gear slot; any 4 of the 5). The copycat: everything he does lasts
    // longer and spreads wider, he attacks faster, and at 4 pieces he copies himself: a Mirror Image with all
    // his powers, talents and traits joins the fight when he hits.
    new GearSet("Photographic Reflexes",
        pieces: new[]
        {
            ("Finesse's Baton",         TaskmasterUniques + "Unique239.prototype"),
            ("Agency X Body Armor",     TaskmasterUniques + "Unique240.prototype"),
            ("Frightful Four Hood",     TaskmasterUniques + "Unique241.prototype"),
            ("Videotape Collection",    TaskmasterUniques + "Unique242.prototype"),
            ("Solid Energy Shield",     TaskmasterUniques + "Unique243.prototype"),
        },
        heroes: new[]
        {
            ("Taskmaster", "Entity/Characters/Avatars/Shipping/Taskmaster.prototype"),
        },
        tiers: new[]
        {
            // Each tier states the total, not an addition to the tier before it
            // Each tier includes the ones before it
            new SetTier(2, "Studied Technique: everything you apply lasts 25% longer (buffs, debuffs, summons), and your area " +
                           "powers are 25% bigger",
                bonus =>
                {
                    bonus[PropertyEnum.OmniDurationBonusPct] = 0.25f;
                    GrowAreas(bonus, 0.25f, AreaKeyword);
                }),

            new SetTier(3, "Muscle Memory: +20% Attack Speed",
                bonus => bonus[PropertyEnum.CastSpeedIncrPct] = 0.20f),

            new SetTier(4, "Perfect Mimicry: +50% damage with Melee and Ranged powers, and when you hit an enemy, a Mirror Image " +
                           "of yourself joins the fight for 30 seconds with all your powers, talents and traits (20 second cooldown)",
                bonus =>
                {
                    BoostKeyword(bonus, 0.50f, MeleeKeyword);
                    BoostKeyword(bonus, 0.50f, RangedKeyword);
                },
                mirrorCooldownSeconds: 20f,
                mirror: new MirrorImageOptions
                {
                    LifespanSeconds = 30f,
                    DamageScale     = 1f,
                    MaxPowers       = 0,       // all of your activated powers
                    CopyTalents     = true,
                    CopyTraits      = true,
                    AI = new CombatAIOptions { MoveSpeedScale = 1.25f },
                }),
        }),

    // Human Torch only, built from his uniques (one per gear slot; any 4 of the 5). Fireproof first: no damage from enemy
    // fire, burns or hellfire. Then his fire spreads (bigger areas, fireballs that punch through) and lingers (everything he
    // applies lasts longer, so burns and Fire Wall too), with the Gloves of the Light Brigade's stacking burn on hit.
    new GearSet("Flame On!",
        pieces: new[]
        {
            ("Gloves of the Light Brigade",          HumanTorchUniques + "Unique028.prototype"),
            ("Future Foundation Belt",               HumanTorchUniques + "Unique029.prototype"),
            ("Stark's Unstable Catalyst",            HumanTorchUniques + "Unique094.prototype"),
            ("The Original Human Torch's Boots",     HumanTorchUniques + "Unique122.prototype"),
            ("Stellar Fire",                         HumanTorchUniques + "Unique123.prototype"),
        },
        heroes: new[]
        {
            ("Human Torch", "Entity/Characters/Avatars/Shipping/HumanTorch.prototype"),
        },
        tiers: new[]
        {
            // Each tier includes the ones before it
            new SetTier(2, "Fireproof: you take no damage from fire, burns or hellfire",
                bonus => ImmuneToKeywords(bonus,
                    "Powers/Blueprints/Keywords/Fire.defaults",
                    "Powers/Blueprints/Keywords/Burn.defaults",
                    "Powers/Blueprints/Keywords/Hellfire.defaults")),

            new SetTier(3, "Heat Wave: your area powers are 35% bigger, and your fireballs have a 50% chance to pierce through enemies",
                bonus =>
                {
                    bonus[PropertyEnum.AOESizePctModifier] = 0.35f;
                    PiercingMissiles(bonus, 0.50f, EnergyKeyword);
                }),

            new SetTier(4, "Supernova: +75% damage with Energy powers, everything you apply lasts 50% longer (burns, Fire Wall, " +
                           "your buffs), and a 35% chance when you hit to set your target ablaze (stacking burn)",
                bonus =>
                {
                    BoostKeyword(bonus, 0.75f, EnergyKeyword);
                    bonus[PropertyEnum.OmniDurationBonusPct] = 0.50f;
                    ScriptBonuses.AddProc(bonus, ProcTriggerType.OnAnyHit, ItemPowers + "UniquesPowers/Unique028BurnProc.prototype", 0.35f);
                }),
        }),

    // Iceman only, built from his uniques (one per gear slot; any 4 of the 5). He is an Omega-level mutant: cold can't hurt
    // or freeze him, everything he builds out of ice lasts longer (Ice Golem, Frozen Orb, ice clones, Ice Wall, chills),
    // and at 4 pieces his summons hit harder and the X-Factor Bodysuit's retaliation blasts whoever damages him.
    new GearSet("Omega-Level Iceman",
        pieces: new[]
        {
            ("Age of Apocalypse Spiked Fists", IcemanUniques + "Unique394.prototype"),
            ("X-Factor Bodysuit",              IcemanUniques + "Unique395.prototype"),
            ("X-Men Leather Trenchcoat",       IcemanUniques + "Unique396.prototype"),
            ("Original X-Men Boots",           IcemanUniques + "Unique397.prototype"),
            ("Age of Apocalypse Armor",        IcemanUniques + "Unique398.prototype"),
        },
        heroes: new[]
        {
            ("Iceman", "Entity/Characters/Avatars/Shipping/Iceman.prototype"),
        },
        tiers: new[]
        {
            // Each tier includes the ones before it
            new SetTier(2, "Cold-Blooded: you take no damage from ice, freezing or chilling attacks, and can't be frozen or chilled",
                bonus =>
                {
                    string[] cold =
                    {
                        "Powers/Blueprints/Keywords/Ice.defaults",
                        "Powers/Blueprints/Keywords/Freeze.defaults",
                        "Powers/Blueprints/Keywords/Chill.defaults",
                    };
                    ImmuneToKeywords(bonus, cold);
                    ImmuneToDebuffKeywords(bonus, cold);
                }),

            new SetTier(3, "Permafrost: everything you create lasts 50% longer (Ice Golem, Frozen Orb, ice clones, Ice Wall, chills), " +
                           "and your area powers are 30% bigger",
                bonus =>
                {
                    bonus[PropertyEnum.OmniDurationBonusPct] = 0.50f;
                    bonus[PropertyEnum.AOESizePctModifier] = 0.30f;
                }),

            new SetTier(4, "Omega-Level Mutant: +75% damage with Energy powers, your summons hit 50% harder, and a 30% chance when " +
                           "you take damage to blast your attacker with cold",
                bonus =>
                {
                    BoostKeyword(bonus, 0.75f, EnergyKeyword);
                    BoostSummons(bonus, 0.50f);
                    ScriptBonuses.AddProc(bonus, ProcTriggerType.OnGotDamaged, ItemPowers + "UniquesPowers/Unique395ThornsProc.prototype", 0.30f);
                }),
        }),
};

//------------------------------------------------------------------------------
// Tooltips and on-screen text (registered at load)
//------------------------------------------------------------------------------

// The set text replaces a piece's flavor quote. Quotes worth keeping are listed here and shown under the set text
// (the server has no copy of the client's text, so they are written out).
var KeptFlavorText = new Dictionary<string, string>
{
    [CyclopsUniques + "Unique021.prototype"] = "Coming Soon: Punchclops Vs. Wolverine!",
    [CyclopsUniques + "Unique555.prototype"] = "Punchclops is such an overachiever!",
    [CyclopsUniques + "Unique070.prototype"] = "Wait, Scott is demonstrating fashion sense?\nIsn't that a sign of the apocalypse?\n—Iceman",
    [CyclopsUniques + "Unique112.prototype"] = "Summers never fully shook off being possessed by Apocalypse.  He came back darker.  That's when he put that jacket on.\n—Wolverine",
    [CyclopsUniques + "Unique068.prototype"] = "When Clan Askani was wiped out, we became the last resistance against Apocalypse. \n—Slym Dayspring (a.k.a. Scott Summers)",
    [CyclopsUniques + "Unique111.prototype"] = "I wore these the first time I commanded a team without the Professor to turn to.  That's when I learned true leadership.",
    [FuryUniques + "Unique506.prototype"] = "At a velocity of 1800 feet per second, these aren't your average sewing set needles.\n—Nick Fury",
    [FuryUniques + "Unique507.prototype"] = "All the way from technicians to the director, S.H.I.E.L.D. protects its own as well.\n—Nick Fury",
    [FuryUniques + "Unique508.prototype"] = "The name is Nick Fury. I'm the Director of S.H.I.E.L.D. If you don't know about us, you will very soon.\n—Nicholas Fury (Earth-1610)",
    [FuryUniques + "Unique509.prototype"] = "That's what it means to be the man on the wall. To be the invisible monster who keeps the other monsters at bay.\n—Nick Fury",
    [FuryUniques + "Unique510.prototype"] = "No lasers. No alien tech. Sometimes, there's just no substitute for efficiency.\n—Nick Fury",
    [CapUniques + "Unique007.prototype"] = "It's a blend of American know-how and Asgardian artistry.\n—Steve Rogers (a.k.a. Captain America)",
    [CapUniques + "Unique008.prototype"] = "Einstein described the theory behind this energy to me once, but I was a few doctorates short of understanding him.\n—Captain America",
    [CapUniques + "Unique091.prototype"] = "Some people mock it's appearance, but this armor was an actual life saver.\n—Captain America",
    [CapUniques + "Unique105.prototype"] = "It is with great honor that I salute my fellow allies of the Greatest Generation.",
    [CapUniques + "Unique106.prototype"] = "Many a battle is won by climbing a hill, trekking through a forest, or rushing a field.",
    [CapUniques + "Unique107.prototype"] = "There are a few times when democracy demands I stand against actions the government takes.",
    [IronManUniques + "Unique003.prototype"] = "I keep meaning to go back and polish the design, but then somebody like Titanium Man shows up and ruins my schedule.\n—Tony Stark (a.k.a. Iron Man)",
    [IronManUniques + "Unique030.prototype"] = "I have GOT to get some of these Asgardian dwarves on contract.\n—Tony Stark (a.k.a. Iron Man)",
    [IronManUniques + "Unique063.prototype"] = "If there's a problem it can't solve, I haven't found it yet.\n—Tony Stark",
    [IronManUniques + "Unique121.prototype"] = "Between P.E.P.P.E.R. and the Stark Omniversal Multitasking Software, I really can be in two places at once.",
    [IronManUniques + "Unique124.prototype"] = "Direct Cybernetic Interface. The next level of integrated circuits.",
    [IronManUniques + "Unique125.prototype"] = "Target and disable with extreme prejudice.\n—Iron Man",
    [IronManUniques + "Unique329.prototype"] = "This armor has all of the durability of my Hulkbuster suit and none of the size issues. Packs a helluva punch, too.",
    [SpiderManUniques + "Unique040.prototype"] = "A few hours working in this lab is better than a year tinkering in my basement.\n—Spider-Man",
    [SpiderManUniques + "Unique041.prototype"] = "Ah, Kaine. My finest creation.\n—Miles Warren (a.k.a. the Jackal)",
    [SpiderManUniques + "Unique077.prototype"] = "I built him one of the most advanced armors in the world and the guy basically threw it in a dumpster and went back to wearing pajamas.\n—Tony Stark a.k.a. Iron Man",
    [SpiderManUniques + "Unique138.prototype"] = "I'm the smartest, the fastest, the strongest.  Superior in every way.",
    [SpiderManUniques + "Unique139.prototype"] = "Through scientific advancement and brilliant minds - behold Spider-Armor MK II",
    [SpiderManUniques + "Unique205.prototype"] = "Most spiders have eight eyes. As of tonight, thanks to my Spider-Bots, I shall have eight thousand.\n—Spider-Man (a.k.a. Doctor Octopus)",
    [SpiderManUniques + "Unique251.prototype"] = "Parker Industries! A revolution in start-ups! An overthrow of the technology sector! Mark my words, and mark them well... together we will rule the world of science!",
    [TaskmasterUniques + "Unique239.prototype"] = "She's probably his daughter. Cold, calculating. And those annoying damn reflexes.\n—Clint Barton (a.k.a. Hawkeye)",
    [TaskmasterUniques + "Unique240.prototype"] = "Is that more protective than the spandex pirate outfit? Let's test it.\n—Frank Castle (a.k.a. The Punisher)",
    [TaskmasterUniques + "Unique241.prototype"] = "I'll help you out. But you have to wear the old banana costume from issue #35 of Vol. 3!\n—Wade Wilson (a.k.a. Deadpool)",
    [TaskmasterUniques + "Unique242.prototype"] = "I've got a tape of every martial artist or scrapper this side of the sun. Some from the other side, too.",
    [TaskmasterUniques + "Unique243.prototype"] = "I beat Iron Man with this one!",
    [HumanTorchUniques + "Unique028.prototype"] = "When you get killed by Annihilus a few times, it's nice to have a souvenir to remember it.\n—Human Torch",
    [HumanTorchUniques + "Unique029.prototype"] = "Reed tried to tell me about all the tech in this thing, but all I could hear was \"blah blah hyperblah experimental blahblah.\"\n—Human Torch",
    [HumanTorchUniques + "Unique094.prototype"] = "I'm not going to say Reed Richards makes awful armor and weapons but... you should have come to me in the first place.\n—Tony Stark (a.k.a. Iron Man)",
    [HumanTorchUniques + "Unique122.prototype"] = "JIM HAMMOND, THE FIRST OF THE MARVELS:\n He showed us that heroes can be made.\n—Statue inscription, Avengers Initiative training facility",
    [HumanTorchUniques + "Unique123.prototype"] = "Frankie has the power of a Human Torch combined with that of the Herald of Galactus. Sexy and powerful, too bad she got away.",
    [IcemanUniques + "Unique394.prototype"] = "Mein gott. This version of Bobby is a bit more... aggressive!\n—Kurt Wagner (a.k.a. Nightcrawler)",
    [IcemanUniques + "Unique395.prototype"] = "Bobby's been with us since the beginning. Through some hard times too, including those X-Factor getups...\n—Jean Grey",
    [IcemanUniques + "Unique396.prototype"] = "Dark leather. It was in for a while. Didn't really help with the whole freezing cold thing.",
    [IcemanUniques + "Unique397.prototype"] = "Yo! Why aren't you a snowman?!\n—Wade Wilson (a.k.a. Deadpool)",
    [IcemanUniques + "Unique398.prototype"] = "I'm a bit sick of cuttin' up evil ice dudes, Bobby. Knock it off.\n—James Howlett (a.k.a. Wolverine)",
};

foreach (GearSet set in Sets)
{
    int fullSet = set.Tiers.Max(tier => tier.Pieces);
    string pieceList = set.Pieces.Length <= 4
        ? string.Join(", ", set.Pieces.Select(piece => piece.Name))
        : set.Pieces.Length <= 5
            ? $"any {fullSet} of: " + string.Join(", ", set.Pieces.Select(piece => piece.Name))
            : $"any {fullSet} of the {set.Pieces.Length} {set.Name.Split(' ')[0]} artifacts";

    string tooltip = $"#highlight#{set.Name}#/highlight#\n" +
        string.Join("\n", set.Tiers.Select(tier => $"({tier.Pieces}) {tier.Description}")) +
        $"\nSet: {pieceList}" +
        (set.Heroes.Length > 0 ? $"\n{string.Join(" and ", set.Heroes.Select(hero => hero.Name))} only" : "");

    foreach (var piece in set.Pieces)
    {
        foreach (string path in piece.Paths)
        {
            string text = KeptFlavorText.TryGetValue(path, out string quote) ? tooltip + "\n\n" + quote : tooltip;
            if (ScriptText.OverrideItemFlavorText(path, text) == false)
                Log.Warn($"{set.Name}: could not set the tooltip of [{path}]");
        }
    }

    foreach (SetTier tier in set.Tiers)
        ScriptText.Register($"set_{set.Key}_{tier.Pieces}", $"{set.Name} ({tier.Pieces}) Active");
}

//------------------------------------------------------------------------------
// Bonus tracking
//------------------------------------------------------------------------------

// Last applied tier per agent and set (0 = none). Entries vanish with the agent.
var appliedTiers = new ConditionalWeakTable<Agent, Dictionary<string, int>>();
var pendingUpdates = new ConditionalWeakTable<Agent, object>();

int CountPieces(Agent agent, GearSet set)
{
    if (set.CanUse(agent) == false)
        return 0;

    // Each distinct piece counts once, so two copies of one artifact do not complete a set
    return set.Pieces.Count(piece => piece.IsEquipped(agent));
}

void UpdateSets(Agent agent)
{
    Dictionary<string, int> tiers = appliedTiers.GetOrCreateValue(agent);
    Player player = agent.GetOwnerOfType<Player>();
    bool mirrorChanged = false;

    foreach (GearSet set in Sets)
    {
        int pieces = CountPieces(agent, set);
        int tier = set.Tiers.Where(t => pieces >= t.Pieces).Select(t => t.Pieces).DefaultIfEmpty(0).Max();

        bool known = tiers.TryGetValue(set.Key, out int previous);
        if (known && previous == tier)
            continue;

        tiers[set.Key] = tier;

        if (tier == 0)
            ScriptBonuses.Remove(agent, set.Key);
        else
            ScriptBonuses.Apply(agent, set.Key, set.BuildBonus(tier), clientVisible: true);

        UpdateVisual(agent, set, tier);
        mirrorChanged = true;

        // Tell the player when a tier changes in play (not while their gear is being loaded)
        if (player != null && agent.IsInWorld && agent is Avatar && (known || tier > 0))
        {
            if (tier > previous)
                ScriptText.ShowBanner(player, $"set_{set.Key}_{tier}", 0, "reward", 3000);

            string active = tier == 0 ? "no bonus" : string.Join(" | ", set.Tiers.Where(t => t.Pieces <= tier).Select(t => $"({t.Pieces}) {t.Description}"));
            ScriptHooks.SendChatMessage(player, $"[Set] {set.Name} {pieces}/{set.Tiers.Max(t => t.Pieces)}: {active}", false);
        }
    }

    // Mirror image on hit: on while a tier that grants it is active (the highest such tier of any set), off otherwise
    if (mirrorChanged && agent is Avatar avatar)
    {
        SetTier mirrorTier = Sets
            .SelectMany(set => set.Tiers.Where(t => t.Mirror != null && tiers.TryGetValue(set.Key, out int active) && t.Pieces <= active))
            .LastOrDefault();

        if (mirrorTier != null)
            ScriptMirrorImages.EnableAutoSpawn(avatar, mirrorTier.Mirror, mirrorTier.MirrorCooldownSeconds);
        else
            ScriptMirrorImages.DisableAutoSpawn(avatar);
    }
}

// A tier's visual can be several powers separated by '|' (their effects are all put on, e.g. two size changes that multiply)
const int MaxVisualsPerTier = 4;

// Puts on (or takes off) the visual of the highest active tier that has one
void UpdateVisual(Agent agent, GearSet set, int tier)
{
    string visualPower = set.Tiers.Where(t => t.Pieces <= tier && t.VisualPower != null).Select(t => t.VisualPower).LastOrDefault();
    string[] powers = visualPower?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? Array.Empty<string>();

    for (int i = 0; i < MaxVisualsPerTier; i++)
    {
        string visualKey = $"set.{set.Key}.visual" + (i == 0 ? "" : i.ToString());

        if (i >= powers.Length)
            ScriptConditions.Remove(agent, visualKey);
        else if (agent.IsInWorld && ScriptConditions.Has(agent, visualKey) == false)
            ScriptConditions.ApplyPowerCondition(agent, visualKey, powers[i]);
    }
}

// An avatar loses its conditions when it leaves the world (region change, hero swap): put set visuals back on
Hooks.On(ScriptHooks.AvatarEnteredWorld, e =>
{
    Avatar avatar = e.Avatar;
    if (appliedTiers.TryGetValue(avatar, out var tiers) == false)
    {
        UpdateSets(avatar);   // a hero this script has not looked at yet
        return;
    }

    foreach (GearSet set in Sets)
    {
        if (tiers.TryGetValue(set.Key, out int tier) && tier > 0)
            UpdateVisual(avatar, set, tier);
    }
});

// Set bonuses are always on while the pieces are worn: nobody has to take gear off and on again. A hero this script has
// not looked at yet (it was already in the world when the script was loaded or reloaded) is picked up the moment it enters
// a region or kills something.
void EnsureTracked(Avatar avatar)
{
    if (avatar != null && avatar.IsInWorld && appliedTiers.TryGetValue(avatar, out _) == false)
        UpdateSets(avatar);
}

Hooks.On(ScriptHooks.PlayerEnteredRegion, e => EnsureTracked(e.Player.CurrentAvatar));

Hooks.On(ScriptHooks.EntityKilled, e => EnsureTracked(e.Killer?.GetOwnerOfType<Player>()?.CurrentAvatar));

Hooks.On(ScriptHooks.EquipmentChanged, e =>
{
    Agent agent = e.Owner;

    // Several items change at once when gear loads: recompute once, after the inventory has settled
    if (pendingUpdates.TryGetValue(agent, out _))
        return;

    pendingUpdates.Add(agent, null);
    After(agent.Game, 0f, () =>
    {
        pendingUpdates.Remove(agent);
        if (agent.IsDestroyed == false)
            UpdateSets(agent);
    });
});

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "sets")
        return;

    e.Handled = true;
    Avatar avatar = e.Player.CurrentAvatar;
    if (avatar == null)
        return;

    UpdateSets(avatar);   // make sure what is shown is what is applied

    foreach (GearSet set in Sets)
    {
        if (set.CanUse(avatar) == false)
        {
            e.Reply($"{set.Name}: {string.Join(" and ", set.Heroes.Select(hero => hero.Name))} only");
            continue;
        }

        int pieces = CountPieces(avatar, set);
        var owned = set.Pieces.Where(piece => piece.IsEquipped(avatar)).Select(piece => piece.Name);
        e.Reply($"{set.Name}: {pieces}/{set.Tiers.Max(t => t.Pieces)} " + (pieces > 0 ? $"({string.Join(", ", owned)})" : "") +
            (ScriptBonuses.Has(avatar, set.Key) ? " - bonus active" : ""));
    }

    // What the server's damage math reads for this hero right now (set bonuses, gear and powers together)
    float resist = avatar.Properties[PropertyEnum.DamagePctResist, DamageType.Any];
    float vsBosses = avatar.Properties[PropertyEnum.DamagePctBonusVsBosses];
    float areaSize = avatar.Properties[PropertyEnum.AOESizePctModifier];
    e.Reply($"Server stats: {resist:P0} damage reduction (all damage), {vsBosses:P0} damage vs bosses, {areaSize:P0} area size.");
});

Log.Info($"Set bonuses loaded: {string.Join(", ", Sets.Select(set => set.Name))}");

//------------------------------------------------------------------------------
// Types
//------------------------------------------------------------------------------

class SetPiece
{
    public string Name { get; }

    // Every version of the piece that counts (regular, Cosmic, ...): written as paths separated by '|'
    public string[] Paths { get; }
    private PrototypeId[] _refs;

    public SetPiece(string name, string paths)
    {
        Name = name;
        Paths = paths.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public bool IsEquipped(Agent agent)
    {
        _refs ??= Paths.Select(GameDatabase.GetPrototypeRefByName).Where(protoRef => protoRef != PrototypeId.Invalid).ToArray();
        return _refs.Any(protoRef => ScriptBonuses.CountEquipped(agent, protoRef) > 0);
    }
}

class SetTier
{
    public int Pieces { get; }
    public string Description { get; }
    public Action<PropertyCollection> AddTo { get; }

    // Optional client visual while this tier is active: a power whose condition is borrowed (see ScriptConditions)
    public string VisualPower { get; }

    // Optional mirror image while this tier is active: it appears when the wearer hits an enemy, one at a time, and the
    // next one no sooner than MirrorCooldownSeconds after the previous one's time is up
    public MirrorImageOptions Mirror { get; }
    public float MirrorCooldownSeconds { get; }

    public SetTier(int pieces, string description, Action<PropertyCollection> addTo, string visualPower = null,
        MirrorImageOptions mirror = null, float mirrorCooldownSeconds = 15f)
    {
        Pieces = pieces;
        Description = description;
        AddTo = addTo;
        VisualPower = visualPower;
        Mirror = mirror;
        MirrorCooldownSeconds = mirrorCooldownSeconds;
    }
}

class GearSet
{
    public string Name { get; }
    public string Key { get; }
    public SetPiece[] Pieces { get; }
    public SetTier[] Tiers { get; }

    // Heroes that can use this set (empty = everyone); Path is the avatar prototype
    public (string Name, string Path)[] Heroes { get; }

    public GearSet(string name, (string Name, string Path)[] pieces, SetTier[] tiers, (string Name, string Path)[] heroes = null)
    {
        Name = name;
        Key = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        Pieces = pieces.Select(piece => new SetPiece(piece.Name, piece.Path)).ToArray();
        Tiers = tiers.OrderBy(tier => tier.Pieces).ToArray();
        Heroes = heroes ?? Array.Empty<(string, string)>();
    }

    public bool CanUse(Agent agent)
    {
        if (Heroes.Length == 0)
            return true;

        // Full prototype path (Entity.PrototypeName is only the short name)
        string agentPath = agent != null ? GameDatabase.GetPrototypeName(agent.PrototypeDataRef) : null;
        return agentPath != null && Heroes.Any(hero => hero.Path.Equals(agentPath, StringComparison.OrdinalIgnoreCase));
    }

    // Bonuses stack: the 4 piece bonus also includes the 2 and 3 piece ones
    public PropertyCollection BuildBonus(int tier)
    {
        var bonus = new PropertyCollection();
        foreach (SetTier setTier in Tiers.Where(t => t.Pieces <= tier))
            setTier.AddTo(bonus);
        return bonus;
    }
}
