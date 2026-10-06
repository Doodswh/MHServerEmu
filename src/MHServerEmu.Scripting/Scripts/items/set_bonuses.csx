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
};

//------------------------------------------------------------------------------
// Tooltips and on-screen text (registered at load)
//------------------------------------------------------------------------------

foreach (GearSet set in Sets)
{
    string pieceList = set.Pieces.Length <= 4
        ? string.Join(", ", set.Pieces.Select(piece => piece.Name))
        : $"any {set.Tiers.Max(tier => tier.Pieces)} of the {set.Pieces.Length} {set.Name.Split(' ')[0]} artifacts";

    string tooltip = $"#highlight#{set.Name}#/highlight#\n" +
        string.Join("\n", set.Tiers.Select(tier => $"({tier.Pieces}) {tier.Description}")) +
        $"\nSet: {pieceList}" +
        (set.Heroes.Length > 0 ? $"\n{string.Join(" and ", set.Heroes.Select(hero => hero.Name))} only" : "");

    foreach (var piece in set.Pieces)
    {
        foreach (string path in piece.Paths)
        {
            if (ScriptText.OverrideItemFlavorText(path, tooltip) == false)
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
