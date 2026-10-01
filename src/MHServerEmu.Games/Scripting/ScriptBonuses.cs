using System.Runtime.CompilerServices;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Inventories;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Script-owned bonus stat blocks on agents (e.g. gear set bonuses). A bonus is a <see cref="PropertyCollection"/> merged into
    /// the agent's stats and procs exactly the way an equipped item's properties are, and is identified by a key so it can be
    /// replaced or removed. Call from the game thread that owns the agent.
    /// </summary>
    /// <remarks>
    /// Bonuses live only in server memory: they are not saved and not sent to the client as stats. Use them for effects the
    /// server calculates (damage and damage taken, crit, procs). The character sheet does not show them, and client-side
    /// stats (movement speed, cooldowns) must not be changed this way. Recompute bonuses from gear when it changes
    /// (<see cref="ScriptHooks.EquipmentChanged"/>): a recreated agent (e.g. after a server transfer) starts without them.
    /// </remarks>
    public static class ScriptBonuses
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        private static readonly ConditionalWeakTable<Agent, Dictionary<string, PropertyCollection>> _bonuses = new();

        /// <summary>
        /// Applies <paramref name="bonus"/> to <paramref name="agent"/> under <paramref name="key"/>, replacing any earlier bonus with that key.
        /// The collection is owned by the agent afterwards; do not modify or reuse it.
        /// </summary>
        public static void Apply(Agent agent, string key, PropertyCollection bonus)
        {
            if (agent == null || string.IsNullOrEmpty(key) || bonus == null)
                return;

            Remove(agent, key);

            // Same order as equipping an item: assign proc powers, then merge the stats
            if (agent.UpdateProcEffectPowers(bonus, true) == false)
                Logger.Warn($"Apply(): Failed to assign some proc powers for bonus [{key}] on [{agent}]");

            agent.Properties.AddChildCollection(bonus);
            _bonuses.GetOrCreateValue(agent)[key] = bonus;
        }

        /// <summary>
        /// Removes the bonus with <paramref name="key"/> from <paramref name="agent"/>. Returns <see langword="false"/> if there was none.
        /// </summary>
        public static bool Remove(Agent agent, string key)
        {
            if (agent == null || _bonuses.TryGetValue(agent, out var bonuses) == false || bonuses.Remove(key, out PropertyCollection bonus) == false)
                return false;

            // Same order as unequipping an item. Proc powers are reference counted, so a power also granted by gear stays assigned.
            bonus.RemoveFromParent(agent.Properties);
            agent.UpdateProcEffectPowers(bonus, false);
            return true;
        }

        /// <summary>
        /// Returns <see langword="true"/> if <paramref name="agent"/> has a bonus with <paramref name="key"/>.
        /// </summary>
        public static bool Has(Agent agent, string key)
        {
            return agent != null && _bonuses.TryGetValue(agent, out var bonuses) && bonuses.ContainsKey(key);
        }

        /// <summary>
        /// Adds a proc to <paramref name="bonus"/>: when <paramref name="trigger"/> happens, <paramref name="chance"/> (0-1) to activate
        /// the power at <paramref name="powerPath"/>. <paramref name="threshold"/> is the trigger's parameter (e.g. 30 for OnHealthBelow 30%).
        /// The power must be an existing proc power from the game data; its own cooldown still applies.
        /// </summary>
        public static bool AddProc(PropertyCollection bonus, ProcTriggerType trigger, string powerPath, float chance, int threshold = 0)
        {
            PrototypeId powerRef = GameDatabase.GetPrototypeRefByName(powerPath);
            if (powerRef == PrototypeId.Invalid || powerRef.As<PowerPrototype>() == null)
                return Logger.WarnReturn(false, $"AddProc(): [{powerPath}] is not a power prototype");

            PropertyId procId = new(PropertyEnum.Proc, (PropertyParam)(int)trigger, Property.ToParam(PropertyEnum.Proc, 1, powerRef), (PropertyParam)threshold);
            bonus[procId] = Math.Clamp(chance, 0f, 1f);
            return true;
        }

        /// <summary>
        /// Returns how many items with <paramref name="itemRef"/> <paramref name="agent"/> has equipped.
        /// </summary>
        public static int CountEquipped(Agent agent, PrototypeId itemRef)
        {
            if (agent == null || itemRef == PrototypeId.Invalid)
                return 0;

            return InventoryIterator.GetMatchingContained(agent, itemRef, InventoryIterationFlags.Equipment);
        }
    }
}
