using System.Runtime.CompilerServices;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Powers.Conditions;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Script-owned conditions on world entities, identified by a key so they can be replaced or removed. Mainly for borrowing a
    /// power's client visual (e.g. a size change) without its gameplay: the condition is applied with no stats unless given some.
    /// Call from the game thread that owns the entity.
    /// </summary>
    /// <remarks>
    /// Conditions are not saved and end when the entity is recreated (e.g. after a region transfer), so reapply them from
    /// <see cref="ScriptHooks.PlayerEnteredRegion"/>.
    /// </remarks>
    public static class ScriptConditions
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private static readonly ConditionalWeakTable<WorldEntity, Dictionary<string, ulong>> _conditions = new();

        /// <summary>
        /// Applies the condition at <paramref name="conditionIndex"/> of the power at <paramref name="powerPath"/> (its AppliesConditions
        /// list) to <paramref name="target"/> under <paramref name="key"/>, replacing any earlier condition with that key.
        /// A zero <paramref name="duration"/> lasts until removed. Returns <see langword="false"/> if it could not be applied.
        /// </summary>
        public static bool ApplyPowerCondition(WorldEntity target, string key, string powerPath, int conditionIndex = 0,
            TimeSpan duration = default, PropertyCollection properties = null)
        {
            if (target?.ConditionCollection == null || string.IsNullOrEmpty(key))
                return false;

            PowerPrototype powerProto = GameDatabase.GetPrototypeRefByName(powerPath).As<PowerPrototype>();
            if (powerProto == null)
                return Logger.WarnReturn(false, $"ApplyPowerCondition(): [{powerPath}] is not a power prototype");

            if (powerProto.AppliesConditions == null || conditionIndex < 0 || conditionIndex >= powerProto.AppliesConditions.Count)
                return Logger.WarnReturn(false, $"ApplyPowerCondition(): [{powerPath}] has no condition {conditionIndex}");

            if (powerProto.AppliesConditions[conditionIndex].Prototype is not ConditionPrototype conditionProto)
                return Logger.WarnReturn(false, $"ApplyPowerCondition(): condition {conditionIndex} of [{powerPath}] is not a condition prototype");

            Remove(target, key);

            ConditionCollection conditions = target.ConditionCollection;
            Condition condition = ConditionCollection.AllocateCondition();
            if (condition.InitializeFromPowerMixinCondition(conditions.NextConditionId, target.Game, target, powerProto, conditionProto,
                duration, properties ?? new PropertyCollection()) == false || conditions.AddCondition(condition) == false)
            {
                if (condition.IsInCollection == false)
                    ConditionCollection.DeleteCondition(condition);
                return Logger.WarnReturn(false, $"ApplyPowerCondition(): failed to add condition {conditionIndex} of [{powerPath}] to [{target}]");
            }

            _conditions.GetOrCreateValue(target)[key] = condition.Id;
            return true;
        }

        /// <summary>
        /// Applies <paramref name="properties"/> to <paramref name="target"/> as an invisible condition (no icon, no visual) under
        /// <paramref name="key"/>, replacing any earlier condition with that key. Unlike a plain child property collection, a
        /// condition is sent to the client with its stats, so the client knows about them too (cooldowns, charges, area sizes,
        /// character sheet). The target must be in the world. Lasts until removed or the target leaves the world.
        /// </summary>
        public static bool ApplyProperties(WorldEntity target, string key, PropertyCollection properties)
        {
            if (target?.ConditionCollection == null || string.IsNullOrEmpty(key) || properties == null || target.IsInWorld == false)
                return false;

            // A blank container condition from the game data: the game uses it on team-up agents for their synergy stats
            ConditionPrototype conditionProto = GameDatabase.GlobalsPrototype.TeamUpSynergyCondition.As<ConditionPrototype>();
            if (conditionProto == null)
                return Logger.WarnReturn(false, "ApplyProperties(): no container condition prototype");

            Remove(target, key);

            ConditionCollection conditions = target.ConditionCollection;
            Condition condition = ConditionCollection.AllocateCondition();
            if (condition.InitializeFromConditionPrototype(conditions.NextConditionId, target.Game, target.Id, target.Id, target.Id,
                conditionProto, TimeSpan.Zero, properties) == false || conditions.AddCondition(condition) == false)
            {
                if (condition.IsInCollection == false)
                    ConditionCollection.DeleteCondition(condition);
                return Logger.WarnReturn(false, $"ApplyProperties(): failed to add [{key}] to [{target}]");
            }

            _conditions.GetOrCreateValue(target)[key] = condition.Id;
            return true;
        }

        /// <summary>
        /// Applies the client condition effect called <paramref name="effectName"/> (the client's MarvelConditionEffect_ class, with
        /// or without that prefix, e.g. "ThorCharged") to <paramref name="target"/> under <paramref name="key"/>, with no stats.
        /// It is borrowed from the first condition in the game data that uses it. A zero <paramref name="duration"/> lasts until removed.
        /// </summary>
        public static bool ApplyEffect(WorldEntity target, string key, string effectName, TimeSpan duration = default)
        {
            if (target?.ConditionCollection == null || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(effectName))
                return false;

            if (GetEffectIndex().TryGetValue(NormalizeEffectName(effectName), out EffectSource source) == false)
                return Logger.WarnReturn(false, $"ApplyEffect(): no condition in the game data uses the effect [{effectName}]");

            // A power's own condition: the same path as ApplyPowerCondition
            if (source.PowerRef != PrototypeId.Invalid)
                return ApplyPowerCondition(target, key, GameDatabase.GetPrototypeName(source.PowerRef), source.ConditionIndex, duration);

            ConditionPrototype conditionProto = source.ConditionRef.As<ConditionPrototype>();
            if (conditionProto == null)
                return false;

            Remove(target, key);

            ConditionCollection conditions = target.ConditionCollection;
            Condition condition = ConditionCollection.AllocateCondition();
            if (condition.InitializeFromConditionPrototype(conditions.NextConditionId, target.Game, target.Id, target.Id, target.Id,
                conditionProto, duration, new PropertyCollection()) == false || conditions.AddCondition(condition) == false)
            {
                if (condition.IsInCollection == false)
                    ConditionCollection.DeleteCondition(condition);
                return Logger.WarnReturn(false, $"ApplyEffect(): failed to add the effect [{effectName}] to [{target}]");
            }

            _conditions.GetOrCreateValue(target)[key] = condition.Id;
            return true;
        }

        /// <summary>
        /// Returns the names of the client condition effects that can be applied with <see cref="ApplyEffect"/> and contain
        /// <paramref name="filter"/> (empty = all), sorted. The first call reads every power in the game data and takes a moment.
        /// </summary>
        public static List<string> FindEffects(string filter = "")
        {
            return GetEffectIndex().Values
                .Where(source => string.IsNullOrEmpty(filter) || source.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Select(source => source.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private const string EffectPrefix = "MarvelConditionEffect_";

        private readonly record struct EffectSource(string Name, PrototypeId PowerRef, int ConditionIndex, PrototypeId ConditionRef);

        private static Dictionary<string, EffectSource> _effectIndex;
        private static readonly object _effectIndexLock = new();

        private static string NormalizeEffectName(string effectName)
        {
            return effectName.StartsWith(EffectPrefix, StringComparison.OrdinalIgnoreCase) ? effectName[EffectPrefix.Length..] : effectName;
        }

        // Effect name (without the prefix) => a condition in the game data that uses it
        private static Dictionary<string, EffectSource> GetEffectIndex()
        {
            lock (_effectIndexLock)
            {
                if (_effectIndex != null)
                    return _effectIndex;

                Dictionary<string, EffectSource> index = new(StringComparer.OrdinalIgnoreCase);

                void Add(ConditionPrototype conditionProto, PrototypeId powerRef, int conditionIndex, PrototypeId conditionRef)
                {
                    if (conditionProto == null || conditionProto.UnrealClass == AssetId.Invalid)
                        return;

                    string name = GameDatabase.GetAssetName(conditionProto.UnrealClass);
                    if (string.IsNullOrEmpty(name))
                        return;

                    name = NormalizeEffectName(name);
                    index.TryAdd(name, new EffectSource(name, powerRef, conditionIndex, conditionRef));
                }

                // Conditions written inside a power
                foreach (PrototypeId powerRef in DataDirectory.Instance.IteratePrototypesInHierarchy<PowerPrototype>(PrototypeIterateFlags.NoAbstract))
                {
                    PowerPrototype powerProto = powerRef.As<PowerPrototype>();
                    if (powerProto?.AppliesConditions == null)
                        continue;

                    for (int i = 0; i < powerProto.AppliesConditions.Count; i++)
                        Add(powerProto.AppliesConditions[i].Prototype as ConditionPrototype, powerRef, i, PrototypeId.Invalid);
                }

                // Conditions that are prototypes of their own (only for effects no power carries)
                foreach (PrototypeId conditionRef in DataDirectory.Instance.IteratePrototypesInHierarchy<ConditionPrototype>(PrototypeIterateFlags.NoAbstract))
                    Add(conditionRef.As<ConditionPrototype>(), PrototypeId.Invalid, -1, conditionRef);

                Logger.Info($"GetEffectIndex(): found {index.Count} condition effects");
                return _effectIndex = index;
            }
        }

        /// <summary>
        /// Removes the condition with <paramref name="key"/> from <paramref name="target"/>. Returns <see langword="false"/> if there was none.
        /// </summary>
        public static bool Remove(WorldEntity target, string key)
        {
            if (target == null || _conditions.TryGetValue(target, out var conditions) == false || conditions.Remove(key, out ulong conditionId) == false)
                return false;

            return target.ConditionCollection?.RemoveCondition(conditionId) ?? false;
        }

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="target"/> still has the condition with <paramref name="key"/>.
        /// </summary>
        public static bool Has(WorldEntity target, string key)
        {
            return target?.ConditionCollection != null && _conditions.TryGetValue(target, out var conditions)
                && conditions.TryGetValue(key, out ulong conditionId) && target.ConditionCollection.GetCondition(conditionId) != null;
        }
    }
}
