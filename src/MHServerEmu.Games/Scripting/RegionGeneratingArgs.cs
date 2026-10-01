using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Args for <see cref="ScriptHooks.RegionGenerating"/>: a region instance is about to generate its layout.
    /// Scripts can pick the layout seed and tune how the random grid generators cut rooms and connections.
    /// </summary>
    /// <remarks>
    /// Only numbers the generators already use are changed, so layouts stay valid: the grid size and the cell sets stay
    /// as the game data defines them. Only areas with a random grid layout (see !zonetest info) are affected by the
    /// removal settings; the seed affects everything random in the region (layout, props, population picks).
    /// </remarks>
    public sealed class RegionGeneratingArgs
    {
        private readonly RegionSettings _settings;
        private RegionGenerationOverrides _overrides;

        public Region Region { get; }
        public Game Game { get => Region.Game; }
        public RegionPrototype RegionPrototype { get => Region.Prototype; }

        /// <summary>Full prototype path of the region (Regions/.../X.prototype).</summary>
        public string RegionName { get => Region.PrototypeName ?? string.Empty; }

        /// <summary>Name of the difficulty tier the region is created for.</summary>
        public string DifficultyName { get => GameDatabase.GetPrototypeName(_settings.DifficultyTierRef) ?? string.Empty; }
        public bool IsOmega { get => DifficultyName.Contains("Omega", StringComparison.OrdinalIgnoreCase); }

        /// <summary>Layout seed. The same seed gives the same layout for the same region.</summary>
        public int Seed { get; set; }

        internal RegionGeneratingArgs(Region region, RegionSettings settings)
        {
            Region = region;
            _settings = settings;
            Seed = settings.Seed;
        }

        /// <summary>
        /// Picks a new random seed, so this instance gets a different layout from the one it would have had.
        /// </summary>
        public int NewRandomSeed()
        {
            Seed = Random.Shared.Next(1, int.MaxValue);
            return Seed;
        }

        /// <summary>
        /// Percent (0-95) of a random grid area's rooms to cut. Higher = smaller, mazier map; lower = bigger, more open map.
        /// <paramref name="areaNameContains"/> limits it to areas whose prototype path contains the text (empty = all areas).
        /// The generator only cuts rooms it can remove safely (entrances, exits and required cells stay).
        /// </summary>
        public void SetRoomRemovalChance(int percent, string areaNameContains = "")
        {
            _overrides ??= new();
            _overrides.AddRule(areaNameContains, Math.Clamp(percent, 0, 95), null);
        }

        /// <summary>
        /// Chance (0-95%) for each extra connection between rooms of a random grid area to be closed off. Higher = more dead ends
        /// and longer routes; lower = more loops. The rooms always stay connected.
        /// </summary>
        public void SetConnectionRemovalChance(int percent, string areaNameContains = "")
        {
            _overrides ??= new();
            _overrides.AddRule(areaNameContains, null, Math.Clamp(percent, 0, 95));
        }

        /// <summary>
        /// Builds the map from text instead of the region's own layout (see <see cref="ScriptedLayout"/>): one character per cell of
        /// the cell set at <paramref name="cellSetPath"/> (folder under Resource/Cells, e.g. "EndGame/Limbo/Limbo_A"). Applies to the
        /// first area whose prototype path contains <paramref name="areaNameContains"/> (empty = the first area). Use it on regions
        /// with a single area. Players arrive in the <c>S</c> room.
        /// </summary>
        public ScriptedLayout BuildLayout(string cellSetPath, string[] rows, string areaNameContains = "")
        {
            _overrides ??= new();
            _overrides.Layout = new ScriptedLayout(cellSetPath, rows, areaNameContains);
            return _overrides.Layout;
        }

        /// <summary>
        /// Sets the region's level (enemy level). Regions made for testing are often level 1.
        /// </summary>
        public void SetLevel(int level)
        {
            if (level <= 0)
                return;

            _settings.ApplyLevelOverride = true;
            _settings.Level = level;
        }

        internal RegionGenerationOverrides GetOverrides() => _overrides;
    }

    /// <summary>
    /// Per-region generation tweaks set by <see cref="ScriptHooks.RegionGenerating"/> handlers and read by the grid generators.
    /// </summary>
    public sealed class RegionGenerationOverrides
    {
        private readonly List<(string AreaNameContains, int? RoomKillPct, int? ConnectionKillPct)> _rules = new();

        /// <summary>A script-built map (<see cref="RegionGeneratingArgs.BuildLayout"/>), or null.</summary>
        public ScriptedLayout Layout { get; internal set; }

        /// <summary>Returns the script-built map for <paramref name="area"/> (once: the first matching area gets it).</summary>
        internal ScriptedLayout TakeLayoutForArea(Area area)
        {
            if (Layout == null || Layout.Used || Matches(area, Layout.AreaNameContains) == false)
                return null;

            Layout.Used = true;
            return Layout;
        }

        internal void AddRule(string areaNameContains, int? roomKillPct, int? connectionKillPct)
        {
            _rules.Add((areaNameContains ?? string.Empty, roomKillPct, connectionKillPct));
        }

        /// <summary>Room removal chance for the area, or <paramref name="defaultPct"/> if no rule matches. Later rules win.</summary>
        public int GetRoomKillChance(Area area, int defaultPct)
        {
            int result = defaultPct;
            foreach (var rule in _rules)
            {
                if (rule.RoomKillPct.HasValue && Matches(area, rule.AreaNameContains))
                    result = rule.RoomKillPct.Value;
            }
            return result;
        }

        /// <summary>Connection removal chance for the area, or <paramref name="defaultPct"/> if no rule matches. Later rules win.</summary>
        public int GetConnectionKillChance(Area area, int defaultPct)
        {
            int result = defaultPct;
            foreach (var rule in _rules)
            {
                if (rule.ConnectionKillPct.HasValue && Matches(area, rule.AreaNameContains))
                    result = rule.ConnectionKillPct.Value;
            }
            return result;
        }

        private static bool Matches(Area area, string areaNameContains)
        {
            if (string.IsNullOrEmpty(areaNameContains))
                return true;

            string areaName = area?.PrototypeName;
            return areaName != null && areaName.Contains(areaNameContains, StringComparison.OrdinalIgnoreCase);
        }
    }
}
