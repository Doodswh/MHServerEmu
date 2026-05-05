using Gazillion;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData.LiveTuning;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Regions;

namespace MHServerEmu.Games.Populations
{
    public static class PatrolBossSpawnProtection
    {
        public const float MaxInvulnerabilitySeconds = 300f;

        public static TimeSpan GetClampedDuration(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f)
                return TimeSpan.Zero;

            return TimeSpan.FromSeconds(Math.Min(seconds, MaxInvulnerabilitySeconds));
        }

        public static bool ShouldProtectBoss(WorldEntity entity, Region region)
        {
            if (entity == null || region == null)
                return false;

            if (region.Behavior != RegionBehavior.PublicCombatZone)
                return false;

            if (entity.IsHostileToPlayers() == false)
                return false;

            RankPrototype rankProto = entity.GetRankPrototype();
            return rankProto?.IsRankBoss == true;
        }

        public static bool TryApply(WorldEntity entity, Region region)
        {
            if (ShouldProtectBoss(entity, region) == false)
                return false;

            float seconds = LiveTuningManager.GetLiveGlobalTuningVar(GlobalTuningVar.eGTV_PatrolBossSpawnInvulnerabilitySeconds);
            TimeSpan duration = GetClampedDuration(seconds);
            if (duration <= TimeSpan.Zero)
                return false;

            entity.ApplyTemporaryInvulnerability(duration);
            return true;
        }
    }
}