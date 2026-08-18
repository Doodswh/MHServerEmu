using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;

namespace MHServerEmu.Games.Powers.Conditions
{
    /// <summary>
    /// Functions for filtering <see cref="Condition"/> instances.
    /// </summary>
    public static class ConditionFilter
    {
        public delegate bool Func(Condition condition);
        public delegate bool Func<T>(Condition condition, T arg);

        // NOTE: Manual delegate caching should no longer be needed as of C# 11, so we can just pass functions as is now.
        // https://devblogs.microsoft.com/dotnet/understanding-the-cost-of-csharp-delegates/#c#-11

        /// <summary>
        /// Returns <see langword="true"/> if the provided <see cref="Condition"/> was created by the specified <see cref="Power"/>.
        /// </summary>
        public static bool IsConditionOfPower(Condition condition, PrototypeId powerProtoRef)
        {
            return condition.CreatorPowerPrototypeRef == powerProtoRef;
        }

        /// <summary>
        /// Returns <see langword="true"/> if the provided <see cref="Condition"/> has the specified keyword.
        /// </summary>
        public static bool IsConditionWithKeyword(Condition condition, KeywordPrototype keywordProto)
        {
            return condition.HasKeyword(keywordProto);
        }

        /// <summary>
        /// Returns <see langword="true"/> if the provided <see cref="Condition"/> has properties with the specified <see cref="PropertyEnum"/>.
        /// </summary>
        public static bool IsConditionWithPropertyOfType(Condition condition, PropertyEnum propertyEnum)
        {
            return condition.Properties.HasProperty(propertyEnum);
        }

        /// <summary>
        /// Returns <see langword="true"/> if the provided <see cref="Condition"/> is of the specified <see cref="ConditionType"/>.
        /// </summary>
        public static bool IsConditionOfType(Condition condition, ConditionType conditionType)
        {
            return condition.ConditionPrototype.ConditionType == conditionType;
        }

        public static bool IsConditionCancelOnHit(Condition condition)
        {
            return condition.CancelOnFlags.HasFlag(ConditionCancelOnFlags.OnHit);
        }

        public static bool IsConditionCancelOnKilled(Condition condition)
        {
            return condition.CancelOnFlags.HasFlag(ConditionCancelOnFlags.OnKilled);
        }

        public static bool IsConditionCancelOnPowerUse(Condition condition, PowerPrototype powerProto)
        {
            ConditionPrototype conditionProto = condition.ConditionPrototype;
            if (conditionProto == null)
                return false;

            return condition.CancelOnFlags.HasFlag(ConditionCancelOnFlags.OnPowerUse) &&
                (conditionProto.CancelOnPowerUseKeyword == null || powerProto.HasKeyword(conditionProto.CancelOnPowerUseKeyword));
        }

        public static bool IsConditionCancelOnIntraRegionTeleport(Condition condition)
        {
            return condition.CancelOnFlags.HasFlag(ConditionCancelOnFlags.OnIntraRegionTeleport);
        }

        public static bool IsConditionCancelOnPowerUsePost(Condition condition, PowerPrototype powerProto)
        {
            ConditionPrototype conditionProto = condition.ConditionPrototype;
            if (conditionProto == null)
                return false;

            return condition.CancelOnFlags.HasFlag(ConditionCancelOnFlags.OnPowerUsePost) &&
                (conditionProto.CancelOnPowerUseKeyword == null || powerProto.HasKeyword(conditionProto.CancelOnPowerUseKeyword));
        }

        public static bool IsConditionWithPrototype(Condition condition, PrototypeId protoRef)
        {
            return condition.ConditionPrototypeRef == protoRef;
        }
    }
}
