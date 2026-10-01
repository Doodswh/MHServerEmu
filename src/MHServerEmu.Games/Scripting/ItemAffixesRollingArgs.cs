using MHServerEmu.Core.Collections;
using MHServerEmu.Core.Memory;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Loot;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Args for <see cref="ScriptHooks.ItemAffixesRolling"/>: an item that is being generated, before its normal affixes are rolled.
    /// Change the rarity / item level and add extra affixes here; the normal affixes are rolled afterwards for the resulting rarity.
    /// </summary>
    public sealed class ItemAffixesRollingArgs
    {
        private readonly IItemResolver _resolver;
        private readonly LootRollSettings _settings;
        private readonly HashSet<ScopedAffixRef> _affixSet;

        // Stacking state is shared by every AddStackedAffixes() call for this item, like one Omega roll
        private Dictionary<PrototypeId, int> _affixStacks;
        private Dictionary<PropertyId, int> _propertyStacks;
        private int _maxStacksPerAffix = 2;
        private int _maxStacksPerProcAffix = 1;
        private int _maxStacksPerStat = 4;

        private string _itemName;
        private string _difficultyName;

        public ItemPrototype ItemPrototype { get; }
        public ItemSpec ItemSpec { get; }
        public DropFilterArguments FilterArgs { get; }

        /// <summary>True once the script has changed anything on this item.</summary>
        public bool Modified { get; private set; }

        public string ItemName { get => _itemName ??= GameDatabase.GetPrototypeName(ItemPrototype.DataRef) ?? string.Empty; }
        public string RarityName { get => GameDatabase.GetPrototypeName(FilterArgs.Rarity) ?? string.Empty; }

        /// <summary>Name of the difficulty tier the loot is rolled for (empty if none, e.g. vendors).</summary>
        public string DifficultyName
        {
            get
            {
                if (_difficultyName == null)
                {
                    PrototypeId tier = _settings?.DifficultyTier ?? PrototypeId.Invalid;
                    _difficultyName = tier != PrototypeId.Invalid ? GameDatabase.GetPrototypeName(tier) ?? string.Empty : string.Empty;
                }
                return _difficultyName;
            }
        }

        public bool IsOmegaDifficulty { get => DifficultyName.Contains("Omega", StringComparison.OrdinalIgnoreCase); }
        public bool IsTeamUpGear { get => ItemPrototype is TeamUpGearPrototype; }
        public bool IsLegendary { get => ItemPrototype is LegendaryPrototype; }
        public bool IsCostume { get => ItemPrototype is CostumePrototype; }
        public bool IsCraftingIngredient { get => ItemPrototype is CraftingIngredientPrototype; }
        public bool IsStackable { get => ItemPrototype.StackSettings != null && ItemPrototype.StackSettings.MaxStacks > 1; }
        public bool IsGem { get => ItemPrototype.IsGem; }

        /// <summary>
        /// Set to <see langword="true"/> when the script has built the whole item: the normal affix roll is skipped.
        /// </summary>
        public bool SkipNormalAffixes { get; set; }

        /// <summary>
        /// Most affixes that may touch the same stat on the finished item (0 = no cap). Applied after the normal roll too, so it
        /// covers every source; built-in and special affixes (runeword, blessing, unique, ultimate, sockets) always stay and count,
        /// extra prefix / suffix / cosmic affixes are dropped.
        /// </summary>
        public int MaxAffixesPerStat { get; set; }

        /// <summary>True if the item goes into an equipment slot of the hero (or team-up) it is rolled for.</summary>
        public bool HasEquipmentSlot
        {
            get
            {
                EquipmentInvUISlot slot = FilterArgs.Slot;
                if (slot == EquipmentInvUISlot.Invalid)
                {
                    AgentPrototype agentProto = FilterArgs.RollFor.As<AgentPrototype>();
                    if (agentProto != null)
                        slot = ItemPrototype.GetInventorySlotForAgent(agentProto);
                }

                return slot != EquipmentInvUISlot.Invalid;
            }
        }

        public int ItemLevel
        {
            get => ItemSpec.ItemLevel;
            set { ItemSpec.ItemLevel = value; Modified = true; }
        }

        internal ItemAffixesRollingArgs(IItemResolver resolver, LootRollSettings settings, DropFilterArguments filterArgs,
            ItemSpec itemSpec, HashSet<ScopedAffixRef> affixSet, ItemPrototype itemProto)
        {
            _resolver = resolver;
            _settings = settings;
            FilterArgs = filterArgs;
            ItemSpec = itemSpec;
            _affixSet = affixSet;
            ItemPrototype = itemProto;
        }

        /// <summary>
        /// Returns <see langword="true"/> if the item's prototype path contains any of the comma-separated words (case-insensitive).
        /// </summary>
        public bool ItemNameContainsAny(string commaSeparatedWords)
        {
            if (string.IsNullOrWhiteSpace(commaSeparatedWords))
                return false;

            string itemName = ItemName;
            foreach (string word in commaSeparatedWords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (itemName.Contains(word, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Sets the item's rarity by prototype path. The normal affixes are then rolled using this rarity's limits.
        /// </summary>
        public bool SetRarity(string rarityPath)
        {
            PrototypeId rarityRef = GameDatabase.GetPrototypeRefByName(rarityPath);
            if (rarityRef == PrototypeId.Invalid)
                return false;

            FilterArgs.Rarity = rarityRef;
            ItemSpec.RarityProtoRef = rarityRef;
            Modified = true;
            return true;
        }

        /// <summary>
        /// Makes this an Omega item: the Omega rarity that crafting also recognizes (stacked affixes survive affix copying).
        /// </summary>
        public bool SetOmegaRarity() => SetRarity(LootUtilities.OmegaRarityName);

        /// <summary>
        /// Sets the item's rarity to Cosmic (from the loot globals).
        /// </summary>
        public bool SetCosmicRarity()
        {
            PrototypeId rarityRef = GameDatabase.LootGlobalsPrototype?.RarityCosmic ?? PrototypeId.Invalid;
            if (rarityRef == PrototypeId.Invalid)
                return false;

            FilterArgs.Rarity = rarityRef;
            ItemSpec.RarityProtoRef = rarityRef;
            Modified = true;
            return true;
        }

        /// <summary>
        /// Adds one random gem (socket) affix from all gem affixes in the game data, rolled for the hero the item drops for.
        /// Returns <see langword="false"/> if none could be rolled.
        /// </summary>
        public bool AddRandomGemAffix()
        {
            AffixPrototype[] gemAffixes = GetAffixPools().GemAffixes;
            if (gemAffixes.Length == 0)
                return false;

            Picker<AffixPrototype> picker = new(_resolver.Random);
            foreach (AffixPrototype affix in gemAffixes)
                picker.Add(affix, 100);

            // Its own affix set: a gem roll must not be limited by what the item already has
            HashSet<ScopedAffixRef> gemAffixSet = HashSetPool<ScopedAffixRef>.Instance.Get();
            try
            {
                AffixSpec gemSpec = new();
                if (gemSpec.RollAffix(_resolver.Random, FilterArgs.RollFor, ItemSpec, picker, gemAffixSet) == MutationResults.Error)
                    return false;

                ItemSpec.AddAffixSpec(gemSpec);
                Modified = true;
                return true;
            }
            finally
            {
                HashSetPool<ScopedAffixRef>.Instance.Return(gemAffixSet);
            }
        }

        /// <summary>
        /// Adds one random built-in visual effect affix (Entity/Items/Affixes/BuiltInVFX/). Returns <see langword="false"/> if there are none.
        /// </summary>
        public bool AddRandomVisualAffix()
        {
            AffixPrototype[] visualAffixes = GetAffixPools().VisualAffixes;
            if (visualAffixes.Length == 0)
                return false;

            Picker<AffixPrototype> picker = new(_resolver.Random);
            foreach (AffixPrototype affix in visualAffixes)
                picker.Add(affix, 100);

            AffixPrototype visualProto = picker.Pick();
            if (visualProto == null)
                return false;

            ItemSpec.AddAffixSpec(new AffixSpec(visualProto, PrototypeId.Invalid, _resolver.Random.Next()));
            Modified = true;
            return true;
        }

        #region Affix Pools

        private const string VisualAffixPathPrefix = "Entity/Items/Affixes/BuiltInVFX/";

        private static readonly object _poolLock = new();
        private static (AffixPrototype[] GemAffixes, AffixPrototype[] VisualAffixes)? _affixPools;

        // Built once from the game data (it never changes at runtime) instead of scanning every affix on each gem drop.
        // Same order as a full scan, so picks match the previous in-engine gem code.
        private static (AffixPrototype[] GemAffixes, AffixPrototype[] VisualAffixes) GetAffixPools()
        {
            var pools = _affixPools;
            if (pools != null)
                return pools.Value;

            lock (_poolLock)
            {
                if (_affixPools != null)
                    return _affixPools.Value;

                List<AffixPrototype> gemAffixes = new();
                List<AffixPrototype> visualAffixes = new();

                foreach (PrototypeId affixRef in GameDatabase.DataDirectory.IteratePrototypesInHierarchy<AffixPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                {
                    AffixPrototype proto = affixRef.As<AffixPrototype>();
                    if (proto == null)
                        continue;

                    string affixName = GameDatabase.GetPrototypeName(affixRef) ?? string.Empty;

                    if (proto.IsGemAffix && proto.Position == AffixPosition.Socket1 && affixName.Contains("DoNotDelete", StringComparison.OrdinalIgnoreCase) == false)
                        gemAffixes.Add(proto);
                    else if (proto.Position == AffixPosition.Visual && affixName.StartsWith(VisualAffixPathPrefix, StringComparison.OrdinalIgnoreCase))
                        visualAffixes.Add(proto);
                }

                _affixPools = (gemAffixes.ToArray(), visualAffixes.ToArray());
                return _affixPools.Value;
            }
        }

        #endregion

        /// <summary>
        /// Caps for <see cref="AddStackedAffixes"/> on this item: copies of one affix, copies of one proc affix, and affixes touching one stat.
        /// </summary>
        public void SetStackingLimits(int perAffix, int perProcAffix, int perStat)
        {
            _maxStacksPerAffix = perAffix;
            _maxStacksPerProcAffix = perProcAffix;
            _maxStacksPerStat = perStat;
        }

        /// <summary>
        /// Adds up to <paramref name="count"/> random affixes of <paramref name="position"/>, allowing repeats within the stacking limits
        /// (shared by all calls for this item). Normal attachment checks are skipped, as for Omega items.
        /// </summary>
        public void AddStackedAffixes(AffixPosition position, int count)
        {
            _affixStacks ??= new();
            _propertyStacks ??= new();

            LootUtilities.AddUniqueRandomAffixes(_resolver, FilterArgs, ItemSpec, _affixSet, position, count, true,
                _affixStacks, _propertyStacks, _maxStacksPerAffix, _maxStacksPerProcAffix, _maxStacksPerStat);
            Modified = true;
        }

        /// <summary>
        /// Adds up to <paramref name="count"/> random affixes of <paramref name="position"/>, each affix and affected stat at most once.
        /// Normal attachment checks are skipped.
        /// </summary>
        public void AddUniqueAffixes(AffixPosition position, int count)
        {
            LootUtilities.AddUniqueRandomAffixes(_resolver, FilterArgs, ItemSpec, _affixSet, position, count, true);
            Modified = true;
        }
    }
}
