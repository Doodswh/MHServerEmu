// Gear set bonuses (test sets). Wearing several pieces of a set grants stacking bonuses at 2 / 3 / 4 pieces.
//
// How it works:
//  - Bonuses are real stats and procs merged into the avatar the same way equipped items are (ScriptBonuses), so the
//    server's combat math uses them: damage, damage taken, crit / Brutal Strike procs.
//  - The client is not told about them, so the character sheet does not show them. Only use stats the server calculates.
//  - Procs reuse existing proc powers from the game data (their visuals, damage and cooldowns come from that power).
//  - Each set piece's tooltip flavor text (the italic quote) is replaced with the set description. Text changes show
//    after a reconnect.
//
//   !sets    show your set pieces and active bonuses
//
// Test: !item givemaxlevel Art362Cosmic   (and the other piece names below), equip them in your artifact slots.

using System.Runtime.CompilerServices;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Powers;

//------------------------------------------------------------------------------
// Sets
//------------------------------------------------------------------------------

const string Artifacts = "Entity/Items/Artifacts/Prototypes/";
const string ItemPowers = "Powers/ItemPowers/";

var Sets = new List<GearSet>
{
    // Four Midtown villains' Cosmic artifacts. Offensive, built around crits and Brutal Strikes.
    new GearSet("Midtown Rogues' Gallery",
        pieces: new[]
        {
            ("Bullseye's Deck of Cards",       Artifacts + "SpecialArtifacts/CosmicArtifacts/Art362Cosmic.prototype"),
            ("Vulture's Mechanical Wings",     Artifacts + "SpecialArtifacts/CosmicArtifacts/Art366Cosmic.prototype"),
            ("Wizard's Id Machine",            Artifacts + "SpecialArtifacts/CosmicArtifacts/Art360Cosmic.prototype"),
            ("Nefarious Mask of Doom",         Artifacts + "SpecialArtifacts/CosmicArtifacts/Art067Cosmic.prototype"),
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
        $"\nSet: {pieceList}";

    foreach (var piece in set.Pieces)
    {
        if (ScriptText.OverrideItemFlavorText(piece.Path, tooltip) == false)
            Log.Warn($"{set.Name}: could not set the tooltip of [{piece.Path}]");
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
    // Each distinct piece counts once, so two copies of one artifact do not complete a set
    int count = 0;
    foreach (var piece in set.Pieces)
    {
        if (piece.Ref == PrototypeId.Invalid)
            piece.Ref = GameDatabase.GetPrototypeRefByName(piece.Path);

        if (ScriptBonuses.CountEquipped(agent, piece.Ref) > 0)
            count++;
    }

    return count;
}

void UpdateSets(Agent agent)
{
    Dictionary<string, int> tiers = appliedTiers.GetOrCreateValue(agent);
    Player player = agent.GetOwnerOfType<Player>();

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
            ScriptBonuses.Apply(agent, set.Key, set.BuildBonus(tier));

        // Tell the player when a tier changes in play (not while their gear is being loaded)
        if (player != null && agent.IsInWorld && agent is Avatar && (known || tier > 0))
        {
            if (tier > previous)
                ScriptText.ShowBanner(player, $"set_{set.Key}_{tier}", 0, "reward", 3000);

            string active = tier == 0 ? "no bonus" : string.Join(" | ", set.Tiers.Where(t => t.Pieces <= tier).Select(t => $"({t.Pieces}) {t.Description}"));
            ScriptHooks.SendChatMessage(player, $"[Set] {set.Name} {pieces}/{set.Tiers.Max(t => t.Pieces)}: {active}", false);
        }
    }
}

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

    foreach (GearSet set in Sets)
    {
        int pieces = CountPieces(avatar, set);
        var owned = set.Pieces.Where(piece => ScriptBonuses.CountEquipped(avatar, piece.Ref) > 0).Select(piece => piece.Name);
        e.Reply($"{set.Name}: {pieces}/{set.Tiers.Max(t => t.Pieces)} " + (pieces > 0 ? $"({string.Join(", ", owned)})" : "") +
            (ScriptBonuses.Has(avatar, set.Key) ? " - bonus active" : ""));
    }
});

Log.Info($"Set bonuses loaded: {string.Join(", ", Sets.Select(set => set.Name))}");

//------------------------------------------------------------------------------
// Types
//------------------------------------------------------------------------------

class SetPiece
{
    public string Name { get; }
    public string Path { get; }
    public PrototypeId Ref { get; set; } = PrototypeId.Invalid;

    public SetPiece(string name, string path)
    {
        Name = name;
        Path = path;
    }
}

class SetTier
{
    public int Pieces { get; }
    public string Description { get; }
    public Action<PropertyCollection> AddTo { get; }

    public SetTier(int pieces, string description, Action<PropertyCollection> addTo)
    {
        Pieces = pieces;
        Description = description;
        AddTo = addTo;
    }
}

class GearSet
{
    public string Name { get; }
    public string Key { get; }
    public SetPiece[] Pieces { get; }
    public SetTier[] Tiers { get; }

    public GearSet(string name, (string Name, string Path)[] pieces, SetTier[] tiers)
    {
        Name = name;
        Key = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        Pieces = pieces.Select(piece => new SetPiece(piece.Name, piece.Path)).ToArray();
        Tiers = tiers.OrderBy(tier => tier.Pieces).ToArray();
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
