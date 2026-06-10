using Gazillion;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.LiveTuning;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Properties;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Games.MetaGames
{
    public static class XDefenseScaling
    {
        private const float MaxMultiplier = 1000f;

        public static bool IsInfiniteScalingEnabled(Region region)
        {
            return IsXDefenseRegion(region)
                && LiveTuningManager.GetLiveGlobalTuningVar(GlobalTuningVar.eGTV_XDefenseInfiniteScalingEnabled) != 0f;
        }

        public static int ClampDifficultyIndex(Region region, int difficultyIndex, int minIndex, int maxIndex)
        {
            return ClampDifficultyIndex(IsInfiniteScalingEnabled(region), difficultyIndex, minIndex, maxIndex);
        }

        public static int ClampDifficultyIndex(bool infiniteScalingEnabled, int difficultyIndex, int minIndex, int maxIndex)
        {
            if (infiniteScalingEnabled)
                return Math.Max(difficultyIndex, minIndex);

            return Math.Clamp(difficultyIndex, minIndex, maxIndex);
        }

        public static float GetWaveXpMultiplier(Region region)
        {
            return GetWaveMultiplier(region, GlobalTuningVar.eGTV_XDefenseWaveXPBonusPerWave);
        }

        public static float GetEnemyHealthPctBonus(WorldEntity entity, EntitySettings settings)
        {
            if (entity is not Agent || entity.CanBePlayerOwned())
                return 0f;

            Region region = ResolveRegion(entity, settings);
            if (IsInfiniteScalingEnabled(region) == false)
                return 0f;

            if (IsXDefenseStudent(entity))
                return GetStudentHealthPctBonus(GetStudentHealthMultiplier());

            if (entity.IsHostileToPlayers() == false)
                return 0f;

            int wave = GetWaveCount(region);
            float perWave = LiveTuningManager.GetLiveGlobalTuningVar(GlobalTuningVar.eGTV_XDefenseEnemyHealthBonusPerWave);
            return GetEnemyHealthPctBonus(wave, perWave);
        }

        public static float GetEnemyDamageMultiplier(Region region)
        {
            return GetWaveMultiplier(region, GlobalTuningVar.eGTV_XDefenseEnemyDamageBonusPerWave);
        }

        public static bool ShouldBypassModeEnd(MetaGame metaGame)
        {
            Region region = metaGame?.Region;
            return IsInfiniteScalingEnabled(region) && GetWaveCount(region) > 0;
        }

        public static int GetWaveCount(Region region)
        {
            MetaGame metaGame = GetXDefenseMetaGame(region);
            if (metaGame == null)
                return 0;

            int wave = metaGame.Properties[PropertyEnum.MetaGameWaveCount];

            foreach (var kvp in metaGame.Properties.IteratePropertyRange(PropertyEnum.MetaStateWaveCount))
                wave = Math.Max(wave, kvp.Value);

            return Math.Max(wave, 0);
        }

        public static bool IsXDefenseRegion(Region region)
        {
            if (region == null)
                return false;

            if (GetXDefenseMetaGame(region) != null)
                return true;

            string prototypeName = region.PrototypeName ?? string.Empty;
            return prototypeName.Contains("XDefense", StringComparison.OrdinalIgnoreCase)
                || prototypeName.Contains("XmansionNWS", StringComparison.OrdinalIgnoreCase);
        }

        private static float GetWaveMultiplier(Region region, GlobalTuningVar tuningVar)
        {
            if (IsInfiniteScalingEnabled(region) == false)
                return 1f;

            int wave = GetWaveCount(region);
            float perWave = LiveTuningManager.GetLiveGlobalTuningVar(tuningVar);
            return GetWaveMultiplier(wave, perWave);
        }

        public static float GetWaveMultiplier(int wave, float perWave)
        {
            if (wave <= 0)
                return 1f;

            perWave = Math.Max(perWave, 0f);
            return 1f + ClampMultiplierBonus(perWave * wave);
        }

        public static float GetEnemyHealthPctBonus(int wave, float perWave)
        {
            if (wave <= 0)
                return 0f;

            perWave = Math.Max(perWave, 0f);
            return ClampMultiplierBonus(perWave * wave);
        }

        public static float GetStudentHealthPctBonus(float multiplier)
        {
            return Math.Max(ClampStudentHealthMultiplier(multiplier) - 1f, 0f);
        }

        private static float ClampMultiplierBonus(float bonus)
        {
            if (float.IsNaN(bonus) || float.IsInfinity(bonus))
                return 0f;

            return Math.Clamp(bonus, 0f, MaxMultiplier - 1f);
        }

        private static float GetStudentHealthMultiplier()
        {
            return ClampStudentHealthMultiplier(LiveTuningManager.GetLiveGlobalTuningVar(GlobalTuningVar.eGTV_XDefenseStudentHealthMultiplier));
        }

        private static float ClampStudentHealthMultiplier(float multiplier)
        {
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier))
                return 1f;

            return Math.Clamp(multiplier, 1f, MaxMultiplier);
        }

        private static bool IsXDefenseStudent(WorldEntity entity)
        {
            var globals = GameDatabase.GlobalsPrototype;
            if (globals == null)
                return false;

            if (entity.IsFriendlyTo(globals.PlayerAlliancePrototype) == false)
                return false;

            string prototypeName = entity.PrototypeName ?? string.Empty;
            if (prototypeName.Contains("Student", StringComparison.OrdinalIgnoreCase)
                || prototypeName.Contains("Civilian", StringComparison.OrdinalIgnoreCase)
                || prototypeName.Contains("Xavier", StringComparison.OrdinalIgnoreCase))
                return true;

            return entity.IsHostileToPlayers() == false;
        }

        private static Region ResolveRegion(WorldEntity entity, EntitySettings settings)
        {
            if (entity?.Game?.RegionManager == null)
                return null;

            if (settings.RegionId != 0)
                return entity.Game.RegionManager.GetRegion(settings.RegionId);

            return entity.Region;
        }

        private static MetaGame GetXDefenseMetaGame(Region region)
        {
            if (region?.Game?.EntityManager == null)
                return null;

            foreach (ulong metaGameId in region.MetaGames)
            {
                MetaGame metaGame = region.Game.EntityManager.GetEntity<MetaGame>(metaGameId);
                if (metaGame?.MetaGamePrototype?.MetaGameMetricEvent == MetaGameMetricEventType.XDefense)
                    return metaGame;
            }

            return null;
        }
    }
}