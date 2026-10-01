using System.Collections.Concurrent;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.System.Random;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.Populations
{
    /// <summary>
    /// Queries over enemy boost (mob affix) prototypes used by scripts: the full affix list, filtered random pools,
    /// and detection of affixes that can displace players. Contains no policy; scripts decide what to do with the results.
    /// </summary>
    public static class EnemyBoostCatalog
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private static readonly object _allBoostsLock = new();
        private static List<PrototypeId> _allBoosts;

        private static readonly ConcurrentDictionary<PrototypeId, bool> _displacementCache = new();
        private static readonly ConcurrentDictionary<string, List<PrototypeId>> _poolCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns all non-abstract approved enemy boost prototypes.
        /// </summary>
        public static IReadOnlyList<PrototypeId> AllBoosts
        {
            get
            {
                if (_allBoosts == null)
                {
                    lock (_allBoostsLock)
                    {
                        if (_allBoosts == null)
                        {
                            List<PrototypeId> boosts = new();
                            foreach (PrototypeId boostRef in DataDirectory.Instance.IteratePrototypesInHierarchy<EnemyBoostPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                                boosts.Add(boostRef);
                            _allBoosts = boosts;
                        }
                    }
                }

                return _allBoosts;
            }
        }

        /// <summary>
        /// Returns a cached random pick pool: all boosts minus those whose name contains any of the comma-separated
        /// words in <paramref name="excludeNameContains"/>, and minus displacement boosts unless <paramref name="allowDisplacement"/> is set.
        /// </summary>
        public static IReadOnlyList<PrototypeId> GetPool(string excludeNameContains, bool allowDisplacement)
        {
            string key = $"{allowDisplacement}|{excludeNameContains}";
            return _poolCache.GetOrAdd(key, _ => BuildPool(excludeNameContains, allowDisplacement));
        }

        /// <summary>
        /// Adds up to <paramref name="count"/> random boosts from the specified pool to <paramref name="affixes"/>.
        /// Duplicates are skipped, so fewer may be added. Returns the number of boosts added.
        /// </summary>
        public static int AddRandom(List<PrototypeId> affixes, GRandom random, int count, string excludeNameContains, bool allowDisplacement)
        {
            IReadOnlyList<PrototypeId> pool = GetPool(excludeNameContains, allowDisplacement);
            if (pool.Count == 0 || random == null)
                return 0;

            int added = 0;
            for (int i = 0; i < count; i++)
            {
                PrototypeId boost = pool[random.Next(pool.Count)];
                if (affixes.Contains(boost))
                    continue;

                affixes.Add(boost);
                added++;
            }

            return added;
        }

        /// <summary>
        /// Returns <see langword="true"/> if the boost can knock back, pull, knock down or knock up its targets.
        /// Follows the boost's power chain (active / passive powers, triggered powers, summoned hotspots) through the game data.
        /// </summary>
        public static bool IsDisplacement(PrototypeId boostRef)
        {
            if (boostRef == PrototypeId.Invalid)
                return false;

            return _displacementCache.GetOrAdd(boostRef, static r =>
            {
                // Name backstop for known pulls
                string name = GameDatabase.GetPrototypeName(r);
                if (string.IsNullOrEmpty(name) == false && name.Contains("implosion", StringComparison.OrdinalIgnoreCase))
                    return true;

                return BoostHasDisplacement(r.As<EnemyBoostPrototype>());
            });
        }

        private static List<PrototypeId> BuildPool(string excludeNameContains, bool allowDisplacement)
        {
            string[] excludeWords = string.IsNullOrWhiteSpace(excludeNameContains)
                ? Array.Empty<string>()
                : excludeNameContains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            List<PrototypeId> pool = new();
            int excludedByName = 0;
            int excludedDisplacement = 0;

            foreach (PrototypeId boostRef in AllBoosts)
            {
                string name = GameDatabase.GetPrototypeName(boostRef);
                if (string.IsNullOrEmpty(name))
                    continue;

                if (excludeWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
                {
                    excludedByName++;
                    continue;
                }

                if (allowDisplacement == false && IsDisplacement(boostRef))
                {
                    excludedDisplacement++;
                    continue;
                }

                pool.Add(boostRef);
            }

            Logger.Info($"BuildPool(): Built random affix pool with {pool.Count} affixes ({excludedByName} excluded by name, {excludedDisplacement} displacement)");
            return pool;
        }

        private static bool BoostHasDisplacement(EnemyBoostPrototype boostProto)
        {
            if (boostProto == null) return false;

            HashSet<PrototypeId> visited = new();

            if (PowerHasDisplacement(boostProto.ActivePower, visited, 0))
                return true;

            if (boostProto.PassivePowers.HasValue())
                foreach (PrototypeId powerRef in boostProto.PassivePowers)
                    if (PowerHasDisplacement(powerRef, visited, 0))
                        return true;

            return false;
        }

        private static bool PowerHasDisplacement(PrototypeId powerRef, HashSet<PrototypeId> visited, int depth)
        {
            if (powerRef == PrototypeId.Invalid || depth > 6 || visited.Add(powerRef) == false)
                return false;

            PowerPrototype powerProto = powerRef.As<PowerPrototype>();
            if (powerProto == null) return false;

            if (powerProto.Properties != null && powerProto.Properties[PropertyEnum.KnockbackDistance] != 0f)
                return true;

            if (powerProto.AppliesConditions != null)
            {
                foreach (var item in powerProto.AppliesConditions)
                {
                    if (item.Prototype is not ConditionPrototype conditionProto || conditionProto.Properties == null)
                        continue;

                    if (conditionProto.Properties[PropertyEnum.Knockback] ||
                        conditionProto.Properties[PropertyEnum.Knockdown] ||
                        conditionProto.Properties[PropertyEnum.Knockup])
                        return true;
                }
            }

            if (powerProto.ActionsTriggeredOnPowerEvent.HasValue())
                foreach (PowerEventActionPrototype actionProto in powerProto.ActionsTriggeredOnPowerEvent)
                    if (actionProto != null && PowerHasDisplacement(actionProto.Power, visited, depth + 1))
                        return true;

            if (powerProto is SummonPowerPrototype summonPowerProto && summonPowerProto.SummonEntityContexts.HasValue())
            {
                foreach (SummonEntityContextPrototype context in summonPowerProto.SummonEntityContexts)
                {
                    if (context?.SummonEntity.As<WorldEntityPrototype>() is not HotspotPrototype hotspotProto || hotspotProto.AppliesPowers.IsNullOrEmpty())
                        continue;

                    foreach (PrototypeId hotspotPowerRef in hotspotProto.AppliesPowers)
                        if (PowerHasDisplacement(hotspotPowerRef, visited, depth + 1))
                            return true;
                }
            }

            return false;
        }
    }
}
