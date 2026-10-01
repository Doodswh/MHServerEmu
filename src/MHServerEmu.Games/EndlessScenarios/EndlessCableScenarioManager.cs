using Gazillion;
using MHServerEmu.Core.Collections;
using MHServerEmu.Core.Collisions;
using MHServerEmu.Core.Extensions;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.Memory;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.Behavior;
using MHServerEmu.Games.DRAG;
using MHServerEmu.Games.DRAG.Generators.Areas;
using MHServerEmu.Games.DRAG.Generators.Regions;
using MHServerEmu.Games.EndlessScenarios;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.Entities.Inventories;
using MHServerEmu.Games.Entities.Items;
using MHServerEmu.Games.Events;
using MHServerEmu.Games.Events.Templates;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.LiveTuning;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.GameData.Tables;
using MHServerEmu.Games.Loot;
using MHServerEmu.Games.Loot.Specs;
using MHServerEmu.Games.MetaGames;
using MHServerEmu.Games.Missions;
using MHServerEmu.Games.Navi;
using MHServerEmu.Games.Network;
using MHServerEmu.Games.Properties;
using MHServerEmu.Games.Regions;
using MHServerEmu.Games.Scripting;
using MHServerEmu.Games.UI;
using MHServerEmu.Games.UI.Widgets;

namespace MHServerEmu.Games.EndlessScenarios
{
    public sealed class EndlessCableScenarioManager
    {
        private const string CableScenarioItemPrototypeName = "Entity/Items/Consumables/Prototypes/DangerRoom/StaticChallenges/PortalToDRStaticScenarioCableFight.prototype";
        internal static readonly PrototypeId CableScenarioItemPrototypeRef = (PrototypeId)12129562728471014409UL;

        private const int BaseBossCount = 1;
        private const int BaseMobCount = 60;
        private const int BossIncreaseEveryWaves = 10;
        private const int MobIncreasePerWave = 5;
        private const int MinimumThemeMobPoolCount = 8;
        private const float ImmediateAggroRangeHostile = 12000.0f;
        private const float ImmediateAggroRangeAlly = 6000.0f;
        private const int SpawnLocationMaxAttempts = 18;
        private const int SpawnLocationFallbackMaxTests = 64;
        private const float SpawnAnchorOffsetMin = 384.0f;
        private const float SpawnAnchorOffsetMax = 900.0f;
        private const float SpawnAnchorOffsetPct = 0.22f;
        private const float SpawnInnerBoundsMarginMin = 384.0f;
        private const float SpawnInnerBoundsMarginMax = 900.0f;
        private const float SpawnInnerBoundsMarginPct = 0.22f;
        private const float SpawnJitterRadius = 320.0f;
        private const float SpawnFallbackMinDistance = 192.0f;
        private const float SpawnFallbackMaxDistance = 900.0f;
        private const int BonusDangerRoomMeritsPerRewardWave = 60;
        private const float RewardLootBonusPerWave = 0.10f;
        private const float MaxLiveTunedMultiplier = 1000.0f;
        private const float DefaultRewardXPBonusPerWave = 0.1f;
        private const float DefaultEnemyHealthBonusPerWave = 0.1f;
        private const float DefaultEnemyDamageBonusPerWave = 0.1f;
        private const float DefaultBonusXPOrbsPerWave = 1.0f;
        private const float DefaultCompletionCrafterEnabled = 1.0f;
        private const float DefaultUniqueUpgradeSuccessChancePct = 10.0f;
        private const ulong WaveClearBannerLocaleStringBase = 18000000000000070000UL;
        private const ulong DangerRoomFinishedBannerLocaleString = 18000000000000080001UL;
        private const int WaveClearBannerLocalizedWaveLimit = 10000;
        private const int WaveClearBannerTimeToLiveMS = 3000;
        private const int DangerRoomFinishedBannerTimeToLiveMS = 5000;
        private const ulong WaveWidgetLocaleStringBase = 18000000000000090000UL;
        private const int WaveWidgetLocalizedWaveLimit = 10000;
        private const string CableDangerRoomWaveWidgetPrototypeName = "UI/MetaGame/MissionName.prototype";
        private const string CableDangerRoomQuotaWidgetPrototypeName = "UI/MetaGame/DangerRoom/DangerRoomCounterBarBASE.prototype";
        private const string CableNativeMissionPrototypeName = "Missions/Prototypes/PVEEndgame/DangerRoom/UniqueScenarios/DRMissionChallengeCableFight.prototype";
        private const string DangerRoomRewardChestPrototypeName = "Entity/Props/Chests/DangerRoomChestTournamentScenarioEntity.prototype";
        private const string DangerRoomRewardChestLootTablePrototypeName = "Loot/Tables/Mob/Bosses/DangerRoom/CableEventDangerRoomBoss.prototype";
        private const string BonusExperienceOrbPrototypeName = "Entity/Items/Orbs/Items/ExperienceOrbSUPERMEGALargeNoMod.prototype";
        private const string DangerRoomMeritsItemPrototypeName = "Entity/Items/CurrencyItems/CurrencyPrototypes/DangerRoomMerits.prototype";
        private const string CableReturnPortalPrototypeName = "Entity/Transitions/ReturnToLastBaseDR.prototype";
        private const string GemRewardPrototypeName = "entity/items/gems/gem1.prototype";
        private PrototypeId _gemRewardRef;
        private const string PetBoxRewardPrototypeName = "Entity/Items/Consumables/Prototypes/CSGrant/LoginRandomVanityPetBox.prototype";
        private PrototypeId _petBoxRewardRef;
        private const string LargeRuneboxRewardPrototypeName = "Entity/Items/Consumables/Prototypes/DailyGift/LargeRunebox.prototype";
        private PrototypeId _largeRuneboxRewardRef;
        private const string ARMORDriveBoxRewardPrototypeName = "Entity/Items/Consumables/Prototypes/CSGrant/CSGrantCrateARMORDriveBox25.prototype";
        private PrototypeId _armorDriveBoxRewardRef;
        private const string HeroCommendationBoxRewardPrototypeName = "Entity/Items/Consumables/Prototypes/CSGrant/CSGrantCrateHeroCommendation25Box.prototype";
        private PrototypeId _heroCommendationBoxRewardRef;
        private const string StoneOfJordanRewardPrototypeName = "Entity/Items/Rings/StoneOfJordan.prototype";
        private PrototypeId _stoneOfJordanRewardRef;
        private const string Legendary014RewardPrototypeName = "entity/items/Legendaries/Prototypes/Legendary014.prototype";
        private PrototypeId _legendary014RewardRef;
        private const string ProtectorCommedationRewardPrototypeName = "Entity/Items/Consumables/Prototypes/CSGrant/CSGrantCrateProtectorsCommendations10Box.prototype";
        private PrototypeId _ProtectorCommedationRewardRef;
        private const string GenoshaInfluenceRewardPrototypeName = "Entity/Items/Consumables/Prototypes/GenoshaInfluence200Box.prototype";
        private PrototypeId _genoshaInfluenceRewardRef;
        private const string Art340CosmicRewardPrototypeName = "Entity/Items/Artifacts/Prototypes/SpecialArtifacts/CosmicArtifacts/Art340Cosmic.prototype";
        private PrototypeId _art340CosmicRewardRef;
        private const string FC75EternitySplintersRewardPrototypeName = "Entity/Items/Consumables/Prototypes/GShop/ConsumablesMisc/FC75EternitySplinters.prototype";
        private PrototypeId _fc75EternitySplintersRewardRef;
        private const string Legendary030RewardPrototypeName = "Entity/Items/legendaries/prototypes/legendary030.prototype";
        private PrototypeId _legendary030RewardRef;
        private const string RunewordGlyph038RewardPrototypeName = "Entity/Items/runewords/glyphs/runewordglyph038.prototype";
        private PrototypeId _runewordGlyph038RewardRef;
        private const string FCMKII100EternitySplintersRewardPrototypeName = "Entity/Items/Consumables/Prototypes/GShop/ConsumablesMisc/FCMKII100EternitySplinters.prototype";
        private PrototypeId _fcmkii100EternitySplintersRewardRef;
        private const string Legendary029RewardPrototypeName = "Entity/Items/legendaries/prototypes/legendary029.prototype";
        private PrototypeId _legendary029RewardRef;
        private const string RandomCosmicArtifactRewardPrototypeName = "Entity/Items/Consumables/Prototypes/RandomGiftboxes/RandomCosmicArtifactBox.prototype";
        private PrototypeId _randomCosmicArtifactRewardRef;
        private const string AgeOfUltronFCRewardPrototypeName = "Entity/Items/Consumables/Prototypes/FortuneCard/AgeOfUltronFortuneCard.prototype";
        private const string CowpocalypseFCRewardPrototypeName = "Entity/Items/Consumables/Prototypes/FortuneCard/CowpocalypseFortuneCard.prototype";
        private const string GotGVol2FCRewardPrototypeName = "Entity/Items/Consumables/Prototypes/FortuneCard/GuardiansOfTheGalaxyVol2FortuneCard.prototype";
        private const string SpideyHomecomingFCRewardPrototypeName = "Entity/Items/Consumables/Prototypes/FortuneCard/SpiderManHomecomingFortuneCard.prototype";
        private const string LoganFCRewardPrototypeName = "Entity/Items/Consumables/Prototypes/FortuneCard/LoganFortuneCard.prototype";
        private const string XMenFCRewardPrototypeName = "Entity/Items/Consumables/Prototypes/FortuneCard/XMenFortuneCard.prototype";
        private const string OdinsBountyFCRewardPrototypeName = "Entity/Items/Consumables/Prototypes/FortuneCard/OdinsBountyFortuneCard.prototype";
        private const string WinterFCRewardPrototypeName = "Entity/Items/Consumables/Prototypes/FortuneCard/WinterFortuneCard.prototype";
        private const string SecretInvasionFCRewardPrototypeName = "Entity/Items/Consumables/Prototypes/FortuneCard/SecretInvasionFortuneCard.prototype";

        private PrototypeId _ageOfUltronFCRewardRef;
        private PrototypeId _cowpocalypseFCRewardRef;
        private PrototypeId _gotGVol2FCRewardRef;
        private PrototypeId _spideyHomecomingFCRewardRef;
        private PrototypeId _loganFCRewardRef;
        private PrototypeId _xMenFCRewardRef;
        private PrototypeId _odinsBountyFCRewardRef;
        private PrototypeId _winterFCRewardRef;
        private PrototypeId _secretInvasionFCRewardRef;

        private const int RewardRecipientId = 1;
        internal const int CompletionRecipeMaxAttemptsPerRun = 3;
        private const float RewardObjectAnchorDistance = 520.0f;
        private const float RewardObjectAvoidDistance = 240.0f;
        private const float RewardLootMinRadius = 45.0f;
        private const float RewardLootRadiusStep = 16.0f;
        private const float RewardLootMaxRadius = 160.0f;

        private static readonly AssetId DangerRoomVictoryRoomLootSourceRef = (AssetId)17776916430842433975UL; // CrateDangerRoomVictoryRoom

        private static readonly Logger Logger = LogManager.CreateLogger();

        private static readonly TimeSpan StartInteractDelay = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan NextWaveDelay = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan BreakWaveDelay = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan CompletedRunRetention = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan AbortedRunRetention = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan ImmediateThinkDelay = TimeSpan.Zero;
        private static readonly TimeSpan FollowupThinkDelay = TimeSpan.FromMilliseconds(100);
        private static readonly TimeSpan ObjectiveWidgetRefreshInterval = TimeSpan.FromSeconds(2);

        private static readonly string[] ExcludedCombatAgentNameFragments =
        [
            "/Avatars/",
            "/Civilian",
            "/Civilians/",
            "/Chest",
            "/Companion",
            "/Debug",
            "/Destructible",
            "/Doop",
            "/Hotspot",
            "/Item",
            "/Items/",
            "/Missile",
            "/NPCs/",
            "/Orb",
            "/Pet",
            "/Pets/",
            "/PowerUp",
            "/Prop",
            "/Props/",
            "/RemovedFromChapters/",
            "/Spawner",
            "/SpawnIn/",
            "/SpawnInVariants/",
            "/Summon",
            "/Summons/",
            "/SurturRaid/",
            "/Targeter",
            "/TeamUp",
            "/TeamUps/",
            "/Test",
            "/Training",
            "/Vendor",
            "/Vendors/",
            "/zzzDeprecated/",
            "/Droids/RobotArm/",
            "/Morlocks/MorlockFemale",
            "/Morlocks/MorlockMale",
            "/NPCActives/Morlock",
            "AIMRobotArm",
            "Civilian",
            "CosmicDoop",
            "DRAIMRobotArm",
            "Doop",
            "Friendly",
            "Hostage",
            "LizardBatNest",
            "MGCivMorlockVulnerable",
            "MissionHelper",
            "Monolith",
            "MorlockCiv",
            "MorlockFemaleNPC",
            "MorlockGuardA",
            "MorlockMaleNPC",
            "Nest",
            "Onslaught",
            "Pedestrian",
            "PlayerSummon",
            "PatrolBack",
            "PatrolFront",
            "PatrolP",
            "Rescue",
            "SerpentRitualMaster",
            "Scientist",
            "SpawnWalk",
            "Surtur",
            "SurturRaid",
            "Summoned",
            "Targeter",
            "Targeters",
            "TGT",
            "TrainingDummy",
            "Turret",
            "Tutorial"
        ];

        private static readonly string[] ExcludedPromotedBossNameFragments =
        [
            "CommanderIra",
            "MiniBoss",
            "NamedMini",
            "TreasureNamed"
        ];

        private static readonly string[] AllowedCombatAgentPathPrefixes =
        [
            "Entity/Characters/Bosses/",
            "Entity/Characters/Mobs/"
        ];

        private readonly Game _game;
        private readonly Dictionary<ulong, EndlessCableScenarioRunState> _activeRuns = [];
        private readonly Dictionary<ulong, Event<EntityDeadGameEvent>.Action> _regionEntityDeadActions = [];
        private readonly Dictionary<ulong, Event<PlayerInteractGameEvent>.Action> _regionPlayerInteractActions = [];
        private readonly Dictionary<ulong, EventPointer<StartAfterInteractEvent>> _startAfterInteractEvents = [];
        private readonly Dictionary<ulong, int> _completionRecipeAttemptsByRun = [];
        private readonly EventGroup _pendingEvents = new();

        private List<EndlessCableCombatTheme> _combatThemes;
        private List<PrototypeId> _bossPool;
        private List<PrototypeId> _mobPool;
        private PrototypeId _dangerRoomRewardChestRef;
        private PrototypeId _dangerRoomRewardChestLootTableRef;
        private PrototypeId _bonusExperienceOrbRef;
        private PrototypeId _dangerRoomMeritsItemRef;
        private PrototypeId _cableReturnPortalRef;
        private PrototypeId _waveWidgetRef;
        private PrototypeId _quotaWidgetRef;
        private PrototypeId _cableNativeMissionRef;
        private AssetId _spawnVisualAsset;
        private ulong _nextRunCounter = 1;

        internal bool HasRun(ulong runId)
        {
            return _activeRuns.ContainsKey(runId);
        }
        public const string AddSocketRecipePath = "Entity/Items/Crafting/Recipes/Tab4Misc/AddCircleSocketToLowLevelArtifact.prototype";

        private static PrototypeId _addSocketRecipeRef = PrototypeId.Invalid;

        public static PrototypeId AddSocketRecipePrototypeRef
        {
            get
            {
                if (_addSocketRecipeRef == PrototypeId.Invalid)
                    _addSocketRecipeRef = GameDatabase.GetPrototypeRefByName(AddSocketRecipePath);

                return _addSocketRecipeRef;
            }
        }
        internal static bool IsInjectedCompletionRecipe(PrototypeId recipeProtoRef)
        {
            return IsUniqueUpgradeRecipe(recipeProtoRef) || recipeProtoRef == AddSocketRecipePrototypeRef;
        }
        internal EndlessCableScenarioManager(Game game)
        {
            _game = game;
        }

        internal static bool IsPresentationLauncherItem(PrototypeId prototypeRef)
        {
            return EndlessCableScenarioItemPresentation.IsPresentationLauncherItem(prototypeRef);
        }

        internal static void ApplyLiveTuningDefaults(TuningVarArray globalTuningVars)
        {
            if (globalTuningVars == null)
                return;

            globalTuningVars[(int)GlobalTuningVar.eGTV_EndlessCableRewardXPBonusPerWave] = DefaultRewardXPBonusPerWave;
            globalTuningVars[(int)GlobalTuningVar.eGTV_EndlessCableEnemyHealthBonusPerWave] = DefaultEnemyHealthBonusPerWave;
            globalTuningVars[(int)GlobalTuningVar.eGTV_EndlessCableEnemyDamageBonusPerWave] = DefaultEnemyDamageBonusPerWave;
            globalTuningVars[(int)GlobalTuningVar.eGTV_EndlessCableBonusXPOrbsPerWave] = DefaultBonusXPOrbsPerWave;
            globalTuningVars[(int)GlobalTuningVar.eGTV_EndlessCableCompletionCrafterEnabled] = DefaultCompletionCrafterEnabled;
            globalTuningVars[(int)GlobalTuningVar.eGTV_EndlessCableUniqueUpgradeSuccessChancePct] = DefaultUniqueUpgradeSuccessChancePct;
        }

        internal static bool IsLiveTuningVar(GlobalTuningVar tuningVar)
        {
            return tuningVar == GlobalTuningVar.eGTV_EndlessCableRewardXPBonusPerWave
                || tuningVar == GlobalTuningVar.eGTV_EndlessCableEnemyHealthBonusPerWave
                || tuningVar == GlobalTuningVar.eGTV_EndlessCableEnemyDamageBonusPerWave
                || tuningVar == GlobalTuningVar.eGTV_EndlessCableBonusXPOrbsPerWave
                || tuningVar == GlobalTuningVar.eGTV_EndlessCableCompletionCrafterEnabled
                || tuningVar == GlobalTuningVar.eGTV_EndlessCableUniqueUpgradeSuccessChancePct;
        }

        internal static ItemSpec ApplyScenarioItemPresentation(ItemSpec itemSpec)
        {
            return EndlessCableScenarioItemPresentation.ApplyItemSpecPresentation(itemSpec);
        }

        internal static PrototypeId GetScenarioRarityForRegion(Item item)
        {
            return EndlessCableScenarioItemPresentation.GetScenarioRarityForRegion(item);
        }

        internal static bool TryHandleItemActionUse(Game game, Player player, Item item, out bool interceptedItemUse)
        {
            interceptedItemUse = false;

            EndlessCableScenarioManager manager = game?.EndlessCableScenarioManager;
            if (player == null || item == null || manager == null)
                return false;

            return manager.TryHandleItemActionUse(player, item, out interceptedItemUse);
        }

        internal static bool TryInterceptPowerActivation(Game game, Player player, Avatar avatar, PrototypeId powerProtoRef, bool hasItemSourceId, ulong itemSourceId)
        {
            EndlessCableScenarioManager manager = game?.EndlessCableScenarioManager;
            if (manager == null || player == null || avatar == null)
                return false;

            return manager.TryInterceptPowerActivation(player, avatar, powerProtoRef, hasItemSourceId, itemSourceId);
        }

        internal static bool IsCompletionVendor(WorldEntity vendor)
        {
            return EndlessCableCompletionCrafter.IsCompletionVendor(vendor);
        }

        internal static bool IsCompletionVendorType(PrototypeId vendorTypeProtoRef)
        {
            return EndlessCableCompletionCrafter.IsCompletionVendorType(vendorTypeProtoRef);
        }

        internal static PrototypeId CompletionVendorTypePrototypeRef => EndlessCableCompletionCrafter.CompletionVendorTypePrototypeRef;

        internal static bool IsCompletionCrafterStashToGeneralTransfer(Player player, InventoryLocation fromInvLoc, InventoryLocation toInvLoc)
        {
            if (player == null)
                return false;

            if (fromInvLoc.InventoryCategory != InventoryCategory.PlayerStashAvatarSpecific &&
                fromInvLoc.InventoryCategory != InventoryCategory.PlayerStashGeneral)
            {
                return false;
            }

            if (toInvLoc.InventoryCategory != InventoryCategory.PlayerGeneral &&
                toInvLoc.InventoryCategory != InventoryCategory.PlayerGeneralExtra)
            {
                return false;
            }

            WorldEntity dialogTarget = player.GetDialogTarget();
            return IsCompletionVendor(dialogTarget) && dialogTarget.IsCrafter;
        }

        internal static bool TryPrepareCompletionVendorInteraction(Player player, WorldEntity vendor)
        {
            if (player == null || IsCompletionVendor(vendor) == false)
                return false;

            player.EnsureEndlessCableCompletionCrafterStock(vendor);

            PrototypeId vendorTypeProtoRef = vendor.Properties[PropertyEnum.VendorType];
            return true;
        }

        internal static PrototypeId UniqueUpgradeRecipePrototypeRef => EndlessCableUniqueUpgradeRecipe.PrototypeRef;
        internal static string UniqueUpgradeRecipePrototypeName => EndlessCableUniqueUpgradeRecipe.PrototypeName;
        internal static int UniqueUpgradeMaxItemLevel => EndlessCableUniqueUpgradeRecipe.MaxItemLevel;

        internal static bool IsUniqueUpgradeRecipe(PrototypeId recipeProtoRef)
        {
            return EndlessCableUniqueUpgradeRecipe.IsRecipe(recipeProtoRef);
        }

        internal static bool IsUniqueUpgradeCraft(CraftingRecipePrototype recipeProto, WorldEntity vendor)
        {
            return IsUniqueUpgradeRecipe(recipeProto?.DataRef ?? PrototypeId.Invalid) && IsCompletionVendor(vendor);
        }

        internal static bool ShouldSuppressCraftingSuccessMessageForTryCraft(Player player, Item recipeItem, WorldEntity vendor, NetMessageTryCraft tryCraft)
        {
            bool isUniqueUpgradeRecipe = IsUniqueUpgradeRecipe(recipeItem?.PrototypeDataRef ?? PrototypeId.Invalid);
            return isUniqueUpgradeRecipe && IsCompletionVendor(vendor);
        }

        internal static bool IsUniqueUpgradeEligibleTarget(Item item)
        {
            return EndlessCableUniqueUpgradeRecipe.IsEligibleTarget(item);
        }

        internal static int GetUniqueUpgradeItemLevel(Item item)
        {
            return EndlessCableUniqueUpgradeRecipe.GetItemLevel(item);
        }

        internal static int ResolveUniqueUpgradeSuccessChancePct()
        {
            return EndlessCableUniqueUpgradeRecipe.ResolveSuccessChancePct();
        }

        internal static LocaleStringId GetUniqueUpgradeResultBannerLocaleStringId(bool success, int attemptsRemaining)
        {
            return EndlessCableUniqueUpgradeRecipe.GetCraftingResultBannerLocaleStringId(success, attemptsRemaining);
        }

        internal int GetCompletionRecipeAttempts(ulong runId)
        {
            if (runId == 0)
                return 0;

            CleanupCompletionRecipeAttempts();
            return _completionRecipeAttemptsByRun.TryGetValue(runId, out int attempts) ? attempts : 0;
        }

        internal bool TryConsumeCompletionRecipeAttempt(ulong runId, out int attemptNumber, out int attemptsRemaining)
        {
            attemptNumber = 0;
            attemptsRemaining = 0;

            if (runId == 0)
                return false;

            int usedAttempts = GetCompletionRecipeAttempts(runId);
            if (usedAttempts >= CompletionRecipeMaxAttemptsPerRun)
                return false;

            attemptNumber = usedAttempts + 1;
            _completionRecipeAttemptsByRun[runId] = attemptNumber;
            attemptsRemaining = Math.Max(CompletionRecipeMaxAttemptsPerRun - attemptNumber, 0);
            return true;
        }

        private void CleanupCompletionRecipeAttempts()
        {
            if (_completionRecipeAttemptsByRun.Count == 0)
                return;

            using var staleHandle = ListPool<ulong>.Instance.Get(out List<ulong> staleRunIds);
            foreach (ulong runId in _completionRecipeAttemptsByRun.Keys)
            {
                if (_activeRuns.ContainsKey(runId) == false)
                    staleRunIds.Add(runId);
            }

            foreach (ulong runId in staleRunIds)
                _completionRecipeAttemptsByRun.Remove(runId);
        }

        internal bool TryHandleItemActionUse(Player player, Item item, out bool interceptedItemUse)
        {
            EndlessCableScenarioUseResult useResult = TryHandleItemUse(player, item, out interceptedItemUse);
            if (interceptedItemUse == false)
                return false;

            // Return the real result: returning true for a blocked use counted as "used" and could consume the item
            SendUseErrorIfAny(player, useResult);
            return useResult?.Success == true;
        }

        private void SendUseErrorIfAny(Player player, EndlessCableScenarioUseResult useResult)
        {
            if (player == null || useResult == null || useResult.Success || string.IsNullOrEmpty(useResult.ErrorMessage))
                return;

            _game.ChatManager?.SendChatFromCustomSystem(player, $"[Cable Endless Scenario] {useResult.ErrorMessage}", showSender: false);
        }

        /// <summary>
        /// Returns the in-progress run for this player, if any. PendingBind counts, so a second launch
        /// during the loading screen is blocked too.
        /// </summary>
        /// <summary>
        /// CUSTOM: Ends the current between-wave break early for the run the player is in (their own run, or the run
        /// in the region they are in), so the next wave starts after the normal short delay.
        /// </summary>
        internal bool TrySkipBreak(Player player, out string message)
        {
            message = null;
            if (player == null)
                return false;

            EndlessCableScenarioRunState runState = FindInProgressRunForPlayer(player.DatabaseUniqueId);
            ulong regionId = player.CurrentAvatar?.Region?.Id ?? 0;
            if (runState == null && regionId != 0)
                runState = FindInProgressRunInRegion(regionId);

            if (runState == null)
            {
                message = "You are not in an Endless Danger Room run.";
                return false;
            }

            if (runState.Status != EndlessCableScenarioRunStatus.WaitingForNextWave)
            {
                message = "There is no break to skip right now.";
                return false;
            }

            TimeSpan currentTime = _game.CurrentTime;
            if (runState.NextWaveAt - currentTime <= NextWaveDelay)
            {
                message = "The next wave is already starting.";
                return false;
            }

            runState.ShortenWaitForNextWave(currentTime + NextWaveDelay, currentTime);
            message = $"Break skipped, wave {runState.CurrentWave + 1} starts in {NextWaveDelay.TotalSeconds:0} seconds.";

            // Let the run owner know if someone else skipped it
            if (runState.PlayerDbId != player.DatabaseUniqueId)
            {
                Player owner = _game.EntityManager.GetEntityByDbGuid<Player>(runState.PlayerDbId);
                if (owner != null)
                    _game.ChatManager?.SendChatFromCustomSystem(owner, $"[Cable Endless Scenario] {player.GetName()} skipped the break. {message}", showSender: false);
            }

            return true;
        }

        private EndlessCableScenarioRunState FindInProgressRunForPlayer(ulong playerDbId)
        {
            foreach (EndlessCableScenarioRunState runState in _activeRuns.Values)
            {
                if (runState.PlayerDbId == playerDbId && runState.IsInProgress)
                    return runState;
            }

            return null;
        }

        /// <summary>
        /// Returns an in-progress run bound to the specified region (other than <paramref name="exceptRunId"/>), if any.
        /// </summary>
        private EndlessCableScenarioRunState FindInProgressRunInRegion(ulong regionId, ulong exceptRunId = 0)
        {
            if (regionId == 0)
                return null;

            foreach (EndlessCableScenarioRunState runState in _activeRuns.Values)
            {
                if (runState.RunId == exceptRunId || runState.RegionId != regionId)
                    continue;

                if (runState.IsInProgress)
                    return runState;
            }

            return null;
        }

        private EndlessCableScenarioRunState FindInProgressRunForPartyMember(Player player)
        {
            var party = player?.GetParty();
            if (party == null)
                return null;

            foreach (EndlessCableScenarioRunState runState in _activeRuns.Values)
            {
                if (runState.IsInProgress && runState.PlayerDbId != player.DatabaseUniqueId && party.IsMember(runState.PlayerDbId))
                    return runState;
            }

            return null;
        }

        private EndlessCableScenarioUseResult TryHandleItemUse(Player player, Item item, out bool interceptedItemUse)
        {
            interceptedItemUse = false;

            if (player == null || item == null)
                return null;

            if (IsCableScenarioItem(item.PrototypeDataRef) == false)
                return null;

            interceptedItemUse = true;

            EndlessCableScenarioRunState existingRun = FindInProgressRunForPlayer(player.DatabaseUniqueId);
            if (existingRun != null)
            {
                // The client can report one click through both the item-use and the power path, stay quiet for the duplicate
                bool isDuplicateOfJustStarted = existingRun.SourceItemEntityId == item.Id && _game.CurrentTime - existingRun.CreatedAt < TimeSpan.FromSeconds(3);

                return new EndlessCableScenarioUseResult
                {
                    ErrorMessage = isDuplicateOfJustStarted ? string.Empty : "You already have an active Cable Danger Room scenario in progress."
                };
            }

            // Using a launcher inside someone else's run would bind a second run to the same instance (double waves)
            if (FindInProgressRunInRegion(player.GetRegion()?.Id ?? 0) != null)
            {
                return new EndlessCableScenarioUseResult
                {
                    ErrorMessage = "A Cable Danger Room scenario is already running in this area."
                };
            }

            // Party members can be routed into the same instance, which would also stack two runs in one region
            if (FindInProgressRunForPartyMember(player) != null)
            {
                return new EndlessCableScenarioUseResult
                {
                    ErrorMessage = "A party member already has a Cable Danger Room scenario in progress."
                };
            }

            Avatar avatar = player.CurrentAvatar;
            if (avatar == null || avatar.IsInWorld == false)
            {
                return new EndlessCableScenarioUseResult
                {
                    ErrorMessage = "Player avatar is not in world."
                };
            }

            ItemPrototype targetItemPrototype = EndlessCableScenarioItemPresentation.IsPresentationLauncherItem(item.PrototypeDataRef)
                ? CableScenarioItemPrototypeRef.As<ItemPrototype>()
                : item.ItemPrototype;

            if (TryResolveCableScenarioTarget(targetItemPrototype, out PrototypeId targetRegionProtoRef, out PrototypeId targetStartTargetProtoRef) == false)
            {
                return new EndlessCableScenarioUseResult
                {
                    ErrorMessage = $"Unable to resolve {CableScenarioItemPrototypeName} portal target."
                };
            }



            PrototypeId superheroicTierRef = GameDatabase.GlobalsPrototype.GetDifficultyTierByEnum(DifficultyTier.Red)?.DataRef ?? PrototypeId.Invalid;

            ulong runId = EndlessCableScenarioRunState.RunIdPrefix | _nextRunCounter++;
            EndlessCableScenarioRunState runState = new()
            {
                RunId = runId,
                PlayerDbId = player.DatabaseUniqueId,
                SourceItemEntityId = item.Id,
                ScenarioItemProtoRef = item.PrototypeDataRef,

                TargetRegionProtoRef = targetRegionProtoRef,
                TargetStartTargetProtoRef = targetStartTargetProtoRef,
                CreatedAt = _game.CurrentTime
            };

            _activeRuns[runId] = runState;

            bool teleported;
            using (Teleporter teleporter = ObjectPoolManager.Instance.Get<Teleporter>())
            {
                teleporter.Initialize(player, TeleportContextEnum.TeleportContext_Debug);
                teleporter.DifficultyTierRef = superheroicTierRef;
                teleporter.RequiredItemProtoRef = item.PrototypeDataRef;
                teleporter.RequiredItemEntityId = item.Id;
                teleporter.DangerRoomScenarioRef = CableScenarioItemPrototypeRef;


                teleported = teleporter.TeleportToTarget(targetStartTargetProtoRef);
            }

            if (teleported)
            {
                ConsumeScenarioItemStack(item);
            }
            else
            {
                runState.Abort(_game.CurrentTime);
            }

            return new EndlessCableScenarioUseResult
            {
                Success = teleported,
                TeleportAttempted = true,
                TeleportSucceeded = teleported,
                RunId = runId,
                TargetRegionProtoRef = targetRegionProtoRef,
                TargetStartTargetProtoRef = targetStartTargetProtoRef,
                ErrorMessage = teleported ? string.Empty : "Teleport failed."
            };
        }

        private bool IsCableScenarioOnUsePower(PrototypeId powerProtoRef)
        {
            if (powerProtoRef == PrototypeId.Invalid)
                return false;

            ItemPrototype itemProto = CableScenarioItemPrototypeRef.As<ItemPrototype>();
            if (itemProto?.ActionsTriggeredOnItemEvent?.Choices == null)
                return false;

            foreach (ItemActionBasePrototype itemActionBaseProto in itemProto.ActionsTriggeredOnItemEvent.Choices)
            {
                if (itemActionBaseProto is ItemActionUsePowerPrototype usePowerProto && usePowerProto.Power == powerProtoRef)
                    return true;
            }

            return false;
        }

        private bool TryFindOwnedCableScenarioItemGrantingPower(Player player, PrototypeId powerProtoRef, out Item item)
        {
            item = null;
            if (player == null || powerProtoRef == PrototypeId.Invalid)
                return false;

            foreach (Entity entity in _game.EntityManager.IterateEntities())
            {
                if (entity is not Item candidate)
                    continue;

                if (EndlessCableScenarioItemPresentation.IsEndlessCableLauncherItem(candidate.PrototypeDataRef) == false)
                    continue;

                // The presented (Omega) launcher may not carry the native power, accept it anyway
                if (candidate.OnUsePower != powerProtoRef && EndlessCableScenarioItemPresentation.IsPresentationLauncherItem(candidate.PrototypeDataRef) == false)
                    continue;

                if (candidate.GetOwnerOfType<Player>() != player)
                    continue;

                item = candidate;
                return true;
            }

            return false;
        }

        internal bool TryInterceptPowerActivation(Player player, Avatar avatar, PrototypeId powerProtoRef, bool hasItemSourceId, ulong itemSourceId)
        {
            if (player == null || avatar == null)
                return false;

            // Strict validation: If the client explicitly defined the item, evaluate it immediately.
            if (hasItemSourceId)
            {
                Item sourceItem = _game.EntityManager.GetEntity<Item>(itemSourceId);

                if (sourceItem != null && (sourceItem.OnUsePower == powerProtoRef || IsCableScenarioItem(sourceItem.PrototypeDataRef)))
                {
                    EndlessCableScenarioUseResult useResult = TryHandleItemUse(player, sourceItem, out bool interceptedItemUse);
                    if (interceptedItemUse)
                        return FinalizePowerActivationInterception(player, sourceItem, useResult, "itemSourceId");
                }
                return false;
            }

            // Heuristic fallback: Only used if the client activated the power without an item source (e.g., from a hotbar).
            if (IsCableScenarioOnUsePower(powerProtoRef) == false)
                return false;

            if (TryFindOwnedCableScenarioItemGrantingPower(player, powerProtoRef, out Item fallbackItem))
            {
                EndlessCableScenarioUseResult useResult = TryHandleItemUse(player, fallbackItem, out bool interceptedPowerActivation);
                if (interceptedPowerActivation)
                    return FinalizePowerActivationInterception(player, fallbackItem, useResult, hasItemSourceId ? "powerFallbackWithSource" : "powerFallbackNoSource");
            }

            // No launcher item found: never let the native Cable portal power fire while a run is going,
            // the native portal would start a second (native) scenario next to the endless one.
            if (FindInProgressRunForPlayer(player.DatabaseUniqueId) != null)
            {
                _game.ChatManager?.SendChatFromCustomSystem(player, "[Cable Endless Scenario] You already have an active Cable Danger Room scenario in progress.", showSender: false);
                return true;
            }

            return false;
        }

        private bool FinalizePowerActivationInterception(Player player, Item item, EndlessCableScenarioUseResult useResult, string source)
        {
            SendUseErrorIfAny(player, useResult);
            return true;
        }

        internal void Update(TimeSpan currentTime)
        {
            if (_activeRuns.Count == 0)
                return;

            using var runListHandle = ListPool<EndlessCableScenarioRunState>.Instance.Get(out List<EndlessCableScenarioRunState> runList);
            runList.AddRange(_activeRuns.Values);

            foreach (EndlessCableScenarioRunState runState in runList)
            {
                switch (runState.Status)
                {
                    case EndlessCableScenarioRunStatus.PendingBind:
                        TryBindPendingRun(runState, currentTime);
                        break;

                    case EndlessCableScenarioRunStatus.AwaitingStart:
                        UpdateAwaitingStartRun(runState, currentTime);
                        break;

                    case EndlessCableScenarioRunStatus.Active:
                    case EndlessCableScenarioRunStatus.WaitingForNextWave:
                        UpdateActiveRun(runState, currentTime);
                        break;

                    case EndlessCableScenarioRunStatus.Completed:
                    case EndlessCableScenarioRunStatus.Aborted:
                        CleanupFinishedRunIfReady(runState, currentTime);
                        break;
                }
            }
        }

        internal bool TryResolveCompletionCraftRunId(WorldEntity vendor, ulong playerDbId, out ulong runId)
        {
            runId = 0;

            if (IsCompletionCrafterEnabled() == false)
                return false;

            if (vendor == null || EndlessCableCompletionCrafter.IsCompletionVendor(vendor) == false || playerDbId == 0)
                return false;

            ulong vendorRegionId = vendor.Region?.Id ?? 0;

            foreach (EndlessCableScenarioRunState runState in _activeRuns.Values)
            {
                if (runState == null || runState.PlayerDbId != playerDbId)
                    continue;

                if (runState.CompletionVendorEntityId == vendor.Id)
                {
                    runId = runState.RunId;
                    return true;
                }
            }

            if (vendorRegionId == 0)
                return false;

            foreach (EndlessCableScenarioRunState runState in _activeRuns.Values)
            {
                if (runState == null || runState.PlayerDbId != playerDbId)
                    continue;

                if (runState.RegionId != vendorRegionId)
                    continue;

                if (runState.HasCompletionVendor == false)
                    continue;

                if (_game.EntityManager.GetEntity<WorldEntity>(runState.CompletionVendorEntityId) == null)
                    continue;

                runId = runState.RunId;
                return true;
            }

            return false;
        }

        internal bool HasActiveCompletionVendorInRegionForPlayer(ulong regionId, ulong playerDbId)
        {
            if (IsCompletionCrafterEnabled() == false)
                return false;

            if (regionId == 0 || playerDbId == 0)
                return false;

            foreach (EndlessCableScenarioRunState runState in _activeRuns.Values)
            {
                if (runState == null || runState.RegionId != regionId || runState.PlayerDbId != playerDbId)
                    continue;

                if (runState.HasCompletionVendor == false)
                    continue;

                if (_game.EntityManager.GetEntity<WorldEntity>(runState.CompletionVendorEntityId) == null)
                    continue;

                return true;
            }

            return false;
        }

        private static int GetBossCountForWave(int wave)
        {
            wave = Math.Max(wave, 1);
            return BaseBossCount + (wave / BossIncreaseEveryWaves);
        }

        private static int GetMobCountForWave(int wave)
        {
            wave = Math.Max(wave, 1);
            return BaseMobCount + ((wave - 1) * MobIncreasePerWave);
        }

        private static float GetWaveMultiplier(int wave)
        {
            return GetWaveMultiplier(wave, DefaultEnemyHealthBonusPerWave);
        }

        private static float GetWaveMultiplier(int wave, float bonusPerWave)
        {
            bonusPerWave = SanitizeNonNegativeTuningValue(bonusPerWave, 0.0f);
            return 1.0f + Math.Clamp(GetLiveTuningBonusWaveCount(wave) * bonusPerWave, 0.0f, MaxLiveTunedMultiplier - 1.0f);
        }

        private static int GetLiveTuningBonusWaveCount(int waveOrClearedWaveCount)
        {
            return Math.Max(waveOrClearedWaveCount - 1, 0);
        }

        private bool TryBindPendingRun(EndlessCableScenarioRunState runState, TimeSpan currentTime)
        {
            Player player = _game.EntityManager.GetEntityByDbGuid<Player>(runState.PlayerDbId);

            Region region = player?.GetRegion();

            if (player == null || region == null)
                return false;
            PrototypeId redTierRef = GameDatabase.GlobalsPrototype.GetDifficultyTierByEnum(DifficultyTier.Red)?.DataRef ?? PrototypeId.Invalid;

            if (redTierRef != PrototypeId.Invalid)
            {
                region.DifficultyTierRef = redTierRef; // Force the region into Red/Heroic difficulty
            }
            bool scenarioTagged = region.Settings.DangerRoomScenarioRef == CableScenarioItemPrototypeRef;
            bool regionMatches = region.PrototypeDataRef == runState.TargetRegionProtoRef;
            if (scenarioTagged == false && regionMatches == false)
                return false;

            // Backstop: never bind two in-progress runs to the same region instance
            EndlessCableScenarioRunState otherRun = FindInProgressRunInRegion(region.Id, runState.RunId);
            if (otherRun != null)
            {
                Logger.Warn($"[EndlessCable] Run {runState.RunId:X} for player {runState.PlayerDbId} landed in region {region.Id} that already has run {otherRun.RunId:X}, aborting the new run");
                _game.ChatManager?.SendChatFromCustomSystem(player, "[Cable Endless Scenario] A scenario is already running in this instance, only one can run at a time.", showSender: false);
                AbortRun(runState, currentTime, "region already has an in-progress run");
                return false;
            }

            runState.BindRegion(region.Id, currentTime);
            EnsureRegionListener(region);

            CleanScenarioCombatants(region);

            _game.ChatManager?.SendChatFromCustomSystem(player, "[Cable Endless Scenario] Activate the Danger Room console to begin.", showSender: false);
            return true;
        }

        private void UpdateAwaitingStartRun(EndlessCableScenarioRunState runState, TimeSpan currentTime)
        {
            Region region = _game.RegionManager.GetRegion(runState.RegionId);
            Player player = _game.EntityManager.GetEntityByDbGuid<Player>(runState.PlayerDbId);
            Avatar avatar = player?.CurrentAvatar;

            if (region == null || player == null || avatar == null)
            {
                AbortRun(runState, currentTime, "region/player/avatar unavailable before scenario start");
                return;
            }

            if (player.GetRegion()?.Id != region.Id)
            {
                AbortRun(runState, currentTime, "player left scenario before start interact");
                return;
            }

            if (avatar.IsDead)
                AbortRun(runState, currentTime, "player died before scenario start");
            // Wait until the client finishes the loading screen and physically enters the map
            if (avatar.IsInWorld)
            {
                if (!runState.StartRequested)
                {
                    // First tick the player is in-world: Mark the start request.
                    // This natively updates runState.LastActivityAt to currentTime.
                    runState.MarkStartRequested(0, currentTime);

                    // Suppress the native Mission UI immediately
                    SuppressNativeCableObjectiveWidgets(runState, region, currentTime);

                    _game.ChatManager?.SendChatFromCustomSystem(player, "[Cable Endless Scenario] Scenario will begin in 5 seconds...", showSender: false);
                }
                else if (currentTime - runState.LastActivityAt >= TimeSpan.FromSeconds(5))
                {
                    // 5 seconds have passed since the start was requested. Fire the wave!
                    StartRunAfterInteract(runState.RunId);
                }
            }
            // --------------------------------------
        }

        private void UpdateActiveRun(EndlessCableScenarioRunState runState, TimeSpan currentTime)
        {
            Region region = _game.RegionManager.GetRegion(runState.RegionId);
            Player player = _game.EntityManager.GetEntityByDbGuid<Player>(runState.PlayerDbId);
            Avatar avatar = player?.CurrentAvatar;

            if (region == null || player == null || avatar == null)
            {
                AbortRun(runState, currentTime, "region/player/avatar unavailable");
                return;
            }

            if (avatar.IsDead)
            {
                if (TryReviveAndAutoClearWave(runState, player, avatar, region, currentTime) == false)
                    CompleteRunOnPlayerDeath(runState, player, region, avatar.RegionLocation.Position, currentTime);
                return;
            }

            CleanScenarioCombatants(region, runState);
            RefreshCableObjectiveWidgets(runState, region, currentTime);

            if (runState.Status == EndlessCableScenarioRunStatus.WaitingForNextWave)
            {
                if (currentTime < runState.NextWaveAt)
                    return;

                int nextWave = runState.CurrentWave + 1;
                CleanupLeftoverRunCombatants(runState, region, $"before wave {nextWave}");
                ApplyWaveDifficultyToRegion(runState, region, nextWave);
                if (SpawnWave(runState, region, nextWave, currentTime))
                    RefreshCableObjectiveWidgets(runState, region, currentTime, force: true);
                return;
            }

            if (GetAliveTrackedEnemyCount(runState) == 0)
                HandleWaveClear(runState, player, currentTime);
        }

        private void OnRegionEntityDead(ulong regionId, in EntityDeadGameEvent evt)
        {
            if (_activeRuns.Count == 0)
                return;

            foreach (EndlessCableScenarioRunState runState in _activeRuns.Values)
            {
                if (runState.RegionId != regionId || runState.IsInProgress == false)
                    continue;

                if (evt.Defender is Avatar deadAvatar)
                {
                    Player owner = deadAvatar.GetOwnerOfType<Player>();
                    if (owner != null && owner.DatabaseUniqueId == runState.PlayerDbId)
                    {
                        if (runState.Status is EndlessCableScenarioRunStatus.Active or EndlessCableScenarioRunStatus.WaitingForNextWave)
                        {
                            Region region = _game.RegionManager.GetRegion(regionId);
                            if (region != null)
                            {
                                if (TryReviveAndAutoClearWave(runState, owner, deadAvatar, region, _game.CurrentTime) == false)
                                    CompleteRunOnPlayerDeath(runState, owner, region, deadAvatar.RegionLocation.Position, _game.CurrentTime);
                            }
                        }
                        else
                        {
                            AbortRun(runState, _game.CurrentTime, "player died before scenario start");
                        }

                        return;
                    }
                }

                ulong defenderId = evt.Defender?.Id ?? 0;
                if (defenderId != 0 && runState.WaveEntityIds.Contains(defenderId))
                {
                    runState.UntrackWaveEntity(defenderId);
                    Player player = _game.EntityManager.GetEntityByDbGuid<Player>(runState.PlayerDbId);
                    if (player != null && runState.Status == EndlessCableScenarioRunStatus.Active)
                    {
                        Region region = _game.RegionManager.GetRegion(regionId);
                        int aliveCount = GetAliveTrackedEnemyCount(runState);
                        if (aliveCount == 0)
                            HandleWaveClear(runState, player, _game.CurrentTime);
                        else if (region != null)
                            RefreshCableObjectiveWidgets(runState, region, _game.CurrentTime, force: true);
                    }
                }
            }
        }

        private void OnRegionPlayerInteract(ulong regionId, in PlayerInteractGameEvent evt)
        {
            if (_activeRuns.Count == 0 || evt.Player == null)
                return;

            foreach (EndlessCableScenarioRunState runState in _activeRuns.Values)
            {
                if (TryHandleRewardChestInteract(runState, regionId, evt))
                    return;

                if (runState.RegionId != regionId || runState.Status != EndlessCableScenarioRunStatus.AwaitingStart)
                    continue;

                if (runState.PlayerDbId != evt.Player.DatabaseUniqueId || runState.StartRequested)
                    continue;

                if (IsCableScenarioStartInteract(evt) == false)
                    continue;

                ScheduleStartAfterInteract(runState, evt.InteractableObject);
                return;
            }
        }

        private bool TryHandleRewardChestInteract(EndlessCableScenarioRunState runState, ulong regionId, in PlayerInteractGameEvent evt)
        {
            if (runState == null || evt.Player == null || runState.RegionId != regionId)
                return false;

            if (runState.Status != EndlessCableScenarioRunStatus.Completed || runState.PlayerDbId != evt.Player.DatabaseUniqueId)
                return false;

            WorldEntity chest = evt.InteractableObject;
            if (chest == null || runState.RewardChestEntityId == 0 || chest.Id != runState.RewardChestEntityId)
                return false;

            DisableNativeRewardChestLoot(chest);

            if (runState.RewardChestOpened)
                return true;

            OpenRewardChest(runState, evt.Player, chest);
            return true;
        }

        private static bool IsCableScenarioStartInteract(in PlayerInteractGameEvent evt)
        {
            WorldEntity interactable = evt.InteractableObject;
            if (interactable == null || interactable is Transition)
                return false;

            if (interactable.Properties[PropertyEnum.Interactable])
                return true;

            if (interactable.Properties[PropertyEnum.EntSelActHasInteractOption])
                return true;

            return evt.MissionRef != PrototypeId.Invalid;
        }

        private void ScheduleStartAfterInteract(EndlessCableScenarioRunState runState, WorldEntity interactable)
        {
            if (runState == null || runState.StartRequested || _startAfterInteractEvents.ContainsKey(runState.RunId))
                return;

            EventScheduler scheduler = _game.GameEventScheduler;
            if (scheduler == null)
                return;

            EventPointer<StartAfterInteractEvent> startEvent = new();
            if (scheduler.ScheduleEvent(startEvent, StartInteractDelay, _pendingEvents) == false)
                return;

            startEvent.Get().Initialize(this, runState.RunId);
            _startAfterInteractEvents[runState.RunId] = startEvent;
            runState.MarkStartRequested(interactable?.Id ?? 0, _game.CurrentTime);

            if (interactable?.Region != null)
                SuppressNativeCableObjectiveWidgets(runState, interactable.Region, _game.CurrentTime);
        }

        private void StartRunAfterInteract(ulong runId)
        {
            _startAfterInteractEvents.Remove(runId);

            if (_activeRuns.TryGetValue(runId, out EndlessCableScenarioRunState runState) == false)
                return;

            if (runState.Status != EndlessCableScenarioRunStatus.AwaitingStart)
                return;

            TimeSpan currentTime = _game.CurrentTime;
            Region region = _game.RegionManager.GetRegion(runState.RegionId);
            Player player = _game.EntityManager.GetEntityByDbGuid<Player>(runState.PlayerDbId);
            Avatar avatar = player?.CurrentAvatar;
            if (region != null && avatar != null)
            {
                // Use the existing BuildBossAnchor logic to dynamically find the center 
                // of the generated area bounds.
                Vector3 center = BuildBossAnchor(runState, region);

                // Use the native method to move the player to the center.
                // ChangeRegionPosition works even if the player is already in the region.
                avatar.ChangeRegionPosition(center, null, ChangePositionFlags.Teleport);
            }
            if (region == null || player == null || avatar == null)
            {
                AbortRun(runState, currentTime, "region/player/avatar unavailable after start interact");
                return;
            }

            if (avatar.IsDead)
            {
                AbortRun(runState, currentTime, "player died during start interact");
                return;
            }

            ApplyWaveDifficultyToRegion(runState, region, 1);
            CleanScenarioCombatants(region);
            CapturePreRunAgents(runState, region);
            if (SpawnWave(runState, region, 1, currentTime) == false)
                return;

            RefreshCableObjectiveWidgets(runState, region, currentTime, force: true);
            _game.ChatManager?.SendChatFromCustomSystem(player, "[Cable Endless Scenario] Wave 1 started.", showSender: false);
        }

        private void HandleWaveClear(EndlessCableScenarioRunState runState, Player player, TimeSpan currentTime)
        {
            if (runState.Status != EndlessCableScenarioRunStatus.Active)
                return;

            int clearedWave = runState.CurrentWave;
            Region region = _game.RegionManager.GetRegion(runState.RegionId);

            bool isBreakWave = (clearedWave == 10 || clearedWave == 20 || clearedWave == 30 || clearedWave == 40 || clearedWave == 50 || clearedWave == 60);
            TimeSpan delay = isBreakWave ? BreakWaveDelay : NextWaveDelay;
            bool showDefaultMessages = true;

            // Break / delay rules and messages can be overridden by scripts (see Data/Scripts/dangerroom)
            if (ScriptHooks.DangerRoomWaveCleared.HasHandlers)
            {
                DangerRoomWaveClearedArgs hookArgs = new(player, region, clearedWave, (float)delay.TotalSeconds);
                ScriptHooks.DangerRoomWaveCleared.Invoke(hookArgs);
                delay = TimeSpan.FromSeconds(Math.Clamp(hookArgs.DelaySeconds, 0f, 3600f));
                showDefaultMessages = hookArgs.ShowDefaultMessages;
            }

            TimeSpan nextWaveAt = currentTime + delay;

            runState.MarkWaveClear(nextWaveAt, currentTime);
            CleanupLeftoverRunCombatants(runState, region, $"wave {clearedWave} clear");

            if (showDefaultMessages)
                SendWaveClearBanner(player, clearedWave);

            RefreshCableObjectiveWidgets(runState, region, currentTime, force: true);

            if (showDefaultMessages && isBreakWave)
            {
                _game.ChatManager?.SendChatFromCustomSystem(player, $"[Cable Endless Scenario] Wave {clearedWave} cleared! Taking a 3-minute break. Catch your breath.", showSender: false);
            }
        }

        private static void SendWaveClearBanner(Player player, int wave)
        {
            if (player == null)
                return;

            LocaleStringId bannerText = GetWaveClearBannerLocaleStringId(wave);
            if (bannerText == LocaleStringId.Invalid)
                return;

            player.SendBannerMessage(
                bannerText,
                TextStylePrototype.BannerMessageLarge,
                WaveClearBannerTimeToLiveMS,
                BannerMessageStyle.FlyIn,
                doNotQueue: true,
                showImmediately: true);
        }

        private static void SendDangerRoomFinishedBanner(Player player)
        {
            if (player == null)
                return;

            player.SendBannerMessage(
                (LocaleStringId)DangerRoomFinishedBannerLocaleString,
                TextStylePrototype.BannerMessageRewardLarge,
                DangerRoomFinishedBannerTimeToLiveMS,
                BannerMessageStyle.FlyIn,
                doNotQueue: true,
                showImmediately: true);
        }

        private static LocaleStringId GetWaveClearBannerLocaleStringId(int wave)
        {
            if (wave <= 0 || wave > WaveClearBannerLocalizedWaveLimit)
                return LocaleStringId.Invalid;

            return (LocaleStringId)(WaveClearBannerLocaleStringBase + (ulong)wave);
        }

        private void RefreshCableObjectiveWidgets(EndlessCableScenarioRunState runState, Region region, TimeSpan currentTime, bool force = false)
        {
            if (runState == null || region?.UIDataProvider == null)
                return;

            if (runState.Status is not EndlessCableScenarioRunStatus.Active and not EndlessCableScenarioRunStatus.WaitingForNextWave)
                return;

            if (force == false && runState.NextObjectiveWidgetRefreshAt != TimeSpan.Zero && currentTime < runState.NextObjectiveWidgetRefreshAt)
                return;

            if (EnsureCableWidgetPrototypes() == false)
                return;

            PrototypeId contextRef = GetCableWidgetContextRef(runState);
            RefreshCableWaveWidget(region.UIDataProvider, runState, contextRef);
            SuppressNativeCableObjectiveWidgets(runState, region, currentTime);

            UIWidgetGenericFraction quotaWidget = GetCableGenericFractionWidget(region.UIDataProvider, _quotaWidgetRef, contextRef);
            if (quotaWidget != null)
            {
                int requiredCount = Math.Max(runState.CurrentWaveTargetCount, 1);
                int aliveCount = runState.Status == EndlessCableScenarioRunStatus.WaitingForNextWave
                    ? 0
                    : Math.Clamp(GetAliveTrackedEnemyCount(runState), 0, requiredCount);
                int defeatedCount = Math.Clamp(requiredCount - aliveCount, 0, requiredCount);

                quotaWidget.SetAreaContext(contextRef);
                quotaWidget.SetCount(defeatedCount, requiredCount);
            }

            runState.ScheduleNextObjectiveWidgetRefresh(currentTime + ObjectiveWidgetRefreshInterval);
        }

        private void ClearCableObjectiveWidgets(EndlessCableScenarioRunState runState, Region region)
        {
            if (runState == null || region?.UIDataProvider == null)
                return;

            if (EnsureCableWidgetPrototypes())
            {
                PrototypeId contextRef = GetCableWidgetContextRef(runState);
                region.UIDataProvider.DeleteWidget(_waveWidgetRef, contextRef);
                region.UIDataProvider.DeleteWidget(_quotaWidgetRef, contextRef);
            }

            ClearNativeCableObjectiveWidgets(runState, region);
            runState.ClearObjectiveWidgetRefreshSchedule();
        }

        internal bool TryRefreshNativeObjectiveWidgetOverride(MissionObjective objective)
        {
            Mission mission = objective?.Mission;
            EndlessCableScenarioRunState runState = FindActiveRunForNativeCableMission(mission, null);
            if (runState == null)
                return false;

            Region region = mission.Region;
            if (region == null)
                return false;

            SuppressNativeCableObjectiveWidgets(runState, region, _game.CurrentTime);
            return true;
        }

        internal bool TrySendNativeMissionUpdateOverride(Mission mission, Player player, MissionUpdateFlags missionFlags, MissionObjectiveUpdateFlags objectiveFlags)
        {
            if (missionFlags == MissionUpdateFlags.None && objectiveFlags == MissionObjectiveUpdateFlags.None)
                return false;

            EndlessCableScenarioRunState runState = FindActiveRunForNativeCableMission(mission, player);
            if (runState == null)
                return false;

            SendNativeCableMissionTrackerSuppression(player, mission, runState);
            return true;
        }

        internal bool TrySendNativeObjectiveUpdateOverride(MissionObjective objective, Player player, MissionObjectiveUpdateFlags objectiveFlags)
        {
            if (objectiveFlags == MissionObjectiveUpdateFlags.None)
                return false;

            Mission mission = objective?.Mission;
            EndlessCableScenarioRunState runState = FindActiveRunForNativeCableMission(mission, player);
            if (runState == null)
                return false;

            SendNativeCableObjectiveTrackerSuppression(player, mission, objective, runState);
            return true;
        }

        private EndlessCableScenarioRunState FindActiveRunForNativeCableMission(Mission mission, Player player)
        {
            if (mission == null || mission.PrototypeDataRef == PrototypeId.Invalid)
                return null;

            if (EnsureCableNativeMissionPrototype() == false || mission.PrototypeDataRef != _cableNativeMissionRef)
                return null;

            Region region = mission.Region ?? player?.GetRegion();
            if (region == null)
                return null;

            foreach (EndlessCableScenarioRunState runState in _activeRuns.Values)
            {
                if (runState.IsInProgress == false || runState.RegionId == 0 || runState.RegionId != region.Id)
                    continue;

                if (player != null && player.DatabaseUniqueId != runState.PlayerDbId)
                    continue;

                if (ShouldSuppressNativeCableMission(runState) == false)
                    continue;

                return runState;
            }

            return null;
        }

        private static bool ShouldSuppressNativeCableMission(EndlessCableScenarioRunState runState)
        {
            if (runState == null)
                return false;

            if (runState.StartRequested)
                return true;

            return runState.Status is EndlessCableScenarioRunStatus.Active or EndlessCableScenarioRunStatus.WaitingForNextWave;
        }

        private void SuppressNativeCableObjectiveWidgets(EndlessCableScenarioRunState runState, Region region, TimeSpan currentTime)
        {
            if (runState == null || region == null || EnsureCableNativeMissionPrototype() == false)
                return;

            using var missionHandle = HashSetPool<Mission>.Instance.Get(out HashSet<Mission> missions);
            AddNativeCableMission(missions, region.MissionManager);

            foreach (Player player in new PlayerIterator(region))
                AddNativeCableMission(missions, player?.MissionManager);

            foreach (Mission mission in missions)
            {
                SuppressNativeCableMissionTrackerForRunPlayers(mission, runState);
                RefreshNativeCableObjectiveWidgetsForMission(region, mission);
            }
        }

        private void ClearNativeCableObjectiveWidgets(EndlessCableScenarioRunState runState, Region region)
        {
            if (runState == null || region == null || EnsureCableNativeMissionPrototype() == false)
                return;

            using var missionHandle = HashSetPool<Mission>.Instance.Get(out HashSet<Mission> missions);
            AddNativeCableMission(missions, region.MissionManager);

            foreach (Player player in new PlayerIterator(region))
                AddNativeCableMission(missions, player?.MissionManager);

            foreach (Mission mission in missions)
                ClearNativeCableObjectiveWidgetsForMission(region, mission);
        }

        private void AddNativeCableMission(HashSet<Mission> missions, MissionManager missionManager)
        {
            if (missions == null || missionManager == null || EnsureCableNativeMissionPrototype() == false)
                return;

            Mission mission = missionManager.FindMissionByDataRef(_cableNativeMissionRef);
            if (mission != null)
                missions.Add(mission);
        }

        private void RefreshNativeCableObjectiveWidgetsForMission(Region region, Mission mission)
        {
            UIDataProvider uiDataProvider = region?.UIDataProvider;
            if (uiDataProvider == null || mission == null)
                return;

            PrototypeId missionRef = mission.PrototypeDataRef;
            if (missionRef == PrototypeId.Invalid)
                return;

            foreach (MissionObjective objective in mission.Objectives)
            {
                MissionObjectivePrototype objectiveProto = objective?.Prototype;
                if (objectiveProto == null)
                    continue;

                SuppressNativeCableObjectiveWidget(uiDataProvider, missionRef, objectiveProto.MetaGameWidget);
                SuppressNativeCableObjectiveWidget(uiDataProvider, missionRef, objectiveProto.MetaGameWidgetFail);
            }

            SuppressCableMissionNameWidget(uiDataProvider, missionRef);
        }

        private void SuppressNativeCableMissionTrackerForRunPlayers(Mission mission, EndlessCableScenarioRunState runState)
        {
            if (mission == null || runState == null)
                return;

            foreach (Player player in GetRunPlayers(runState))
                SendNativeCableMissionTrackerSuppression(player, mission, runState);
        }

        private IEnumerable<Player> GetRunPlayers(EndlessCableScenarioRunState runState)
        {
            if (runState == null || runState.RegionId == 0)
                yield break;

            Region region = _game.RegionManager.GetRegion(runState.RegionId);
            if (region == null)
                yield break;

            foreach (Player player in new PlayerIterator(region))
            {
                if (player?.DatabaseUniqueId == runState.PlayerDbId)
                    yield return player;
            }
        }

        private static void SendNativeCableMissionTrackerSuppression(Player player, Mission mission, EndlessCableScenarioRunState runState)
        {
            if (player == null || mission == null || mission.PrototypeDataRef == PrototypeId.Invalid)
                return;

            try
            {
                NetMessageMissionUpdate missionMessage = NetMessageMissionUpdate.CreateBuilder()
                    .SetMissionPrototypeId((ulong)mission.PrototypeDataRef)
                    .SetMissionState((uint)MissionState.Inactive)
                    .SetSuppressNotification(true)
                    .SetSuspendedState(true)
                    .Build();

                player.SendMessage(missionMessage);

                foreach (MissionObjective objective in mission.Objectives)
                    SendNativeCableObjectiveTrackerSuppression(player, mission, objective, runState);
            }
            catch (Exception)
            {
            }
        }

        private static void SendNativeCableObjectiveTrackerSuppression(Player player, Mission mission, MissionObjective objective, EndlessCableScenarioRunState runState)
        {
            if (player == null || mission == null || objective == null || mission.PrototypeDataRef == PrototypeId.Invalid)
                return;

            uint requiredCount = (uint)Math.Max(runState?.CurrentWaveTargetCount ?? 1, 1);
            NetMessageMissionObjectiveUpdate objectiveMessage = NetMessageMissionObjectiveUpdate.CreateBuilder()
                .SetMissionPrototypeId((ulong)mission.PrototypeDataRef)
                .SetObjectiveIndex(objective.PrototypeIndex)
                .SetObjectiveState((uint)MissionObjectiveState.Invalid)
                .SetCurrentCount(0)
                .SetRequiredCount(requiredCount)
                .SetFailCurrentCount(0)
                .SetFailRequiredCount(0)
                .SetSuppressNotification(true)
                .SetSuspendedState(true)
                .Build();

            player.SendMessage(objectiveMessage);
        }

        private static void ClearNativeCableObjectiveWidgetsForMission(Region region, Mission mission)
        {
            UIDataProvider uiDataProvider = region?.UIDataProvider;
            if (uiDataProvider == null || mission == null)
                return;

            PrototypeId missionRef = mission.PrototypeDataRef;
            if (missionRef == PrototypeId.Invalid)
                return;

            foreach (MissionObjective objective in mission.Objectives)
            {
                MissionObjectivePrototype objectiveProto = objective?.Prototype;
                if (objectiveProto == null)
                    continue;

                if (objectiveProto.MetaGameWidget != PrototypeId.Invalid)
                    uiDataProvider.DeleteWidget(objectiveProto.MetaGameWidget, missionRef);

                if (objectiveProto.MetaGameWidgetFail != PrototypeId.Invalid)
                    uiDataProvider.DeleteWidget(objectiveProto.MetaGameWidgetFail, missionRef);
            }

            SuppressCableMissionNameWidget(uiDataProvider, missionRef);
        }

        private static void SuppressNativeCableObjectiveWidget(UIDataProvider uiDataProvider, PrototypeId missionRef, PrototypeId widgetRef)
        {
            if (uiDataProvider == null || widgetRef == PrototypeId.Invalid)
                return;

            uiDataProvider.DeleteWidget(widgetRef, missionRef);
        }

        private static void SuppressCableMissionNameWidget(UIDataProvider uiDataProvider, PrototypeId missionRef)
        {
            PrototypeId missionNameWidgetRef = GameDatabase.UIGlobalsPrototype?.MetaGameWidgetMissionName ?? PrototypeId.Invalid;
            if (uiDataProvider == null || missionNameWidgetRef == PrototypeId.Invalid)
                return;

            uiDataProvider.DeleteWidget(missionNameWidgetRef, missionRef);
        }

        private void RefreshCableWaveWidget(UIDataProvider uiDataProvider, EndlessCableScenarioRunState runState, PrototypeId contextRef)
        {
            LocaleStringId waveText = GetWaveWidgetLocaleStringId(runState.CurrentWave);
            if (waveText == LocaleStringId.Invalid)
                return;

            UIWidgetMissionText waveWidget = GetCableMissionTextWidget(uiDataProvider, _waveWidgetRef, contextRef);
            if (waveWidget == null)
                return;

            waveWidget.SetAreaContext(contextRef);
            waveWidget.SetText(waveText, LocaleStringId.Blank);
        }

        private bool EnsureCableWidgetPrototypes()
        {
            if (_waveWidgetRef != PrototypeId.Invalid && _quotaWidgetRef != PrototypeId.Invalid)
                return true;

            _waveWidgetRef = GameDatabase.GetPrototypeRefByName(CableDangerRoomWaveWidgetPrototypeName);
            _quotaWidgetRef = GameDatabase.GetPrototypeRefByName(CableDangerRoomQuotaWidgetPrototypeName);

            if (_waveWidgetRef == PrototypeId.Invalid || GameDatabase.GetPrototype<MetaGameDataPrototype>(_waveWidgetRef) is not UIWidgetMissionTextPrototype)
                return false;

            if (_quotaWidgetRef == PrototypeId.Invalid || GameDatabase.GetPrototype<MetaGameDataPrototype>(_quotaWidgetRef) is not UIWidgetGenericFractionPrototype)
                return false;

            return true;
        }

        private bool EnsureCableNativeMissionPrototype()
        {
            if (_cableNativeMissionRef != PrototypeId.Invalid)
                return true;

            _cableNativeMissionRef = GameDatabase.GetPrototypeRefByName(CableNativeMissionPrototypeName);
            return _cableNativeMissionRef != PrototypeId.Invalid;
        }

        private static UIWidgetGenericFraction GetCableGenericFractionWidget(UIDataProvider uiDataProvider, PrototypeId widgetRef, PrototypeId contextRef)
        {
            if (uiDataProvider == null || widgetRef == PrototypeId.Invalid)
                return null;

            if (GameDatabase.GetPrototype<MetaGameDataPrototype>(widgetRef) is not UIWidgetGenericFractionPrototype)
                return null;

            return uiDataProvider.GetWidget<UIWidgetGenericFraction>(widgetRef, contextRef);
        }

        private static UIWidgetMissionText GetCableMissionTextWidget(UIDataProvider uiDataProvider, PrototypeId widgetRef, PrototypeId contextRef)
        {
            if (uiDataProvider == null || widgetRef == PrototypeId.Invalid)
                return null;

            if (GameDatabase.GetPrototype<MetaGameDataPrototype>(widgetRef) is not UIWidgetMissionTextPrototype)
                return null;

            return uiDataProvider.GetWidget<UIWidgetMissionText>(widgetRef, contextRef);
        }

        private static PrototypeId GetCableWidgetContextRef(EndlessCableScenarioRunState runState)
        {
            return runState?.TargetRegionProtoRef ?? PrototypeId.Invalid;
        }

        private static LocaleStringId GetWaveWidgetLocaleStringId(int wave)
        {
            if (wave <= 0 || wave > WaveWidgetLocalizedWaveLimit)
                return LocaleStringId.Invalid;

            return (LocaleStringId)(WaveWidgetLocaleStringBase + (ulong)wave);
        }

        private bool SpawnWave(EndlessCableScenarioRunState runState, Region region, int wave, TimeSpan currentTime)
        {
            EnsurePools();

            int bossCount = GetBossCountForWave(wave);
            int mobCount = GetMobCountForWave(wave);

            // Wave composition rules can be overridden by scripts (see Data/Scripts/dangerroom)
            if (ScriptHooks.DangerRoomWaveStarting.HasHandlers)
            {
                Player runPlayer = _game.EntityManager.GetEntityByDbGuid<Player>(runState.PlayerDbId);
                DangerRoomWaveStartingArgs hookArgs = new(runPlayer, region, wave, bossCount, mobCount);
                ScriptHooks.DangerRoomWaveStarting.Invoke(hookArgs);
                bossCount = Math.Clamp(hookArgs.BossCount, 0, 100);
                mobCount = Math.Clamp(hookArgs.MobCount, 0, 1000);
            }

            EndlessCableCombatTheme theme = PickRandomTheme();

            if (theme == null)
            {
                AbortRun(runState, currentTime, $"combat pool empty for wave {wave}");
                return false;
            }

            runState.StartWave(wave, currentTime);

            Vector3 bossAnchor = BuildBossAnchor(runState, region);
            Vector3[] anchors = BuildCornerAnchors(runState, region);
            int spawnedBosses = 0;
            int spawnedMobs = 0;

            int bossAttempts = 0;
            int maxBossAttempts = Math.Max(bossCount * 8, theme.Bosses.Count);
            while (spawnedBosses < bossCount && bossAttempts < maxBossAttempts)
            {
                PrototypeId bossProtoRef = PickRandomPrototype(theme.Bosses);
                if (TrySpawnWaveAgent(runState, region, bossProtoRef, bossAnchor, true, bossAttempts, out Agent bossAgent))
                {
                    spawnedBosses++;
                    runState.TrackWaveEntity(bossAgent.Id, true);
                }

                bossAttempts++;
            }

            int mobAttempts = 0;
            int maxMobAttempts = Math.Max(mobCount * 3, theme.Mobs.Count);
            while (spawnedMobs < mobCount && mobAttempts < maxMobAttempts)
            {
                PrototypeId mobProtoRef = PickRandomPrototype(theme.Mobs);
                if (TrySpawnWaveAgent(runState, region, mobProtoRef, anchors[(mobAttempts + bossCount) % anchors.Length], false, mobAttempts, out Agent mobAgent))
                {
                    spawnedMobs++;
                    runState.TrackWaveEntity(mobAgent.Id, false);
                }

                mobAttempts++;
            }

            if (spawnedBosses + spawnedMobs == 0)
            {
                AbortRun(runState, currentTime, $"wave {wave} produced zero combatants");
                return false;
            }

            return true;
        }

        private bool TrySpawnWaveAgent(EndlessCableScenarioRunState runState, Region region, PrototypeId agentProtoRef, Vector3 anchor, bool boss, int spawnIndex, out Agent agent)
        {
            agent = null;

            AgentPrototype agentProto = agentProtoRef.As<AgentPrototype>();
            if (agentProto == null)
                return false;
            var redTierRef = GameDatabase.GlobalsPrototype.GetDifficultyTierByEnum(DifficultyTier.Red)?.DataRef ?? PrototypeId.Invalid;
            Orientation orientation = Orientation.Zero;
            Area area = region.IterateAreas().FirstOrDefault();
            Vector3 reachableOrigin = area != null && area.RegionBounds.IsValid()
                ? RegionLocation.ProjectToFloor(region, area.RegionBounds.Center)
                : anchor; // Fallback to the requested anchor if the area bounds are missing
            if (TryResolveSpawnLocation(region, agentProto, anchor, reachableOrigin, orientation, spawnIndex, out Vector3 spawnPosition, out Cell spawnCell, out string placementMode) == false)
                return false;

            if (agentProto.Bounds != null)
                spawnPosition.Z += agentProto.Bounds.GetBoundHalfHeight();

            using EntitySettings settings = ObjectPoolManager.Instance.Get<EntitySettings>();
            settings.EntityRef = agentProto.DataRef;
            settings.Position = spawnPosition;
            settings.Orientation = orientation;
            settings.RegionId = region.Id;
            settings.Cell = spawnCell;
            settings.IsPopulation = true;

            using PropertyCollection settingsProperties = ObjectPoolManager.Instance.Get<PropertyCollection>();
            int level = spawnCell.Area.GetCharacterLevel(agentProto);
            settingsProperties[PropertyEnum.CharacterLevel] = level;
            settingsProperties[PropertyEnum.CombatLevel] = level;
            settingsProperties[PropertyEnum.DifficultyTier] = redTierRef;
            settingsProperties[PropertyEnum.AllianceOverride] = ResolveHostileAllianceOverride(agentProto);
            settingsProperties[PropertyEnum.Rank] = agentProto.Rank;
            settingsProperties[PropertyEnum.NoLootDrop] = true;
            settingsProperties[PropertyEnum.NoExpOnDeath] = true;
            settingsProperties[PropertyEnum.Dormant] = false;
            settingsProperties[PropertyEnum.IgnoreMissionOwnerForTargeting] = true;
            settingsProperties[PropertyEnum.Visible] = true;
            settings.Properties = settingsProperties;

            agent = _game.EntityManager.CreateEntity(settings) as Agent;
            if (agent == null)
                return false;

            ApplyGuaranteedBoosts(agent, agentProto);
            ForceWaveAgentCombatState(agent, agentProto);
            PrimeImmediateCombat(runState, agent);
            PlaySpawnVisual(agent);

            return true;
        }

        private bool TryResolveSpawnLocation(Region region, AgentPrototype agentProto, Vector3 anchor, Vector3 reachableOrigin, Orientation orientation, int spawnIndex, out Vector3 position, out Cell cell, out string placementMode)
        {
            position = Vector3.Zero;
            cell = null;
            placementMode = "none";

            if (region == null || agentProto == null)
                return false;

            Bounds bounds = CreateSpawnBounds(agentProto, anchor, orientation);
            PathFlags pathFlags = Region.GetPathFlagsForEntity(agentProto);
            if (pathFlags == PathFlags.None)
                pathFlags = PathFlags.Walk;

            PositionCheckFlags posFlags = PositionCheckFlags.CanBeBlockedEntity | PositionCheckFlags.PreferNoEntity | PositionCheckFlags.InRadius;
            BlockingCheckFlags blockFlags = BlockingCheckFlags.CheckSpawns | BlockingCheckFlags.CheckGroundMovementPowers | BlockingCheckFlags.CheckLanding;

            for (int attempt = 0; attempt < SpawnLocationMaxAttempts; attempt++)
            {
                float angle = (spawnIndex * 1.618034f + attempt) * MathF.PI * 0.75f;
                float distance = _game.Random.NextFloat(64.0f, SpawnJitterRadius);
                Vector3 offset = new(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance, 0.0f);
                Vector3 candidate = RegionLocation.ProjectToFloor(region, anchor + offset);

                if (TryFinalizeWaveSpawnLocation(region, agentProto, reachableOrigin, orientation, candidate, pathFlags, posFlags, blockFlags, out position, out cell))
                {
                    placementMode = "validated-quadrant-jitter";
                    return true;
                }
            }

            Vector3 searchOrigin = RegionLocation.ProjectToFloor(region, reachableOrigin);
            bounds.Center = searchOrigin;
            PositionCheckFlags fallbackPosFlags = PositionCheckFlags.CanBeBlockedEntity | PositionCheckFlags.CanPathTo | PositionCheckFlags.PreferNoEntity | PositionCheckFlags.InRadius;
            if (region.ChooseRandomPositionNearPoint(ref bounds, pathFlags, fallbackPosFlags, blockFlags, SpawnFallbackMinDistance, SpawnFallbackMaxDistance,
                    out Vector3 randomPosition, null, null, SpawnLocationFallbackMaxTests)
                && TryFinalizeWaveSpawnLocation(region, agentProto, reachableOrigin, orientation, randomPosition, pathFlags, posFlags, blockFlags, out position, out cell))
            {
                placementMode = "validated-random-near-avatar";
                return true;
            }

            if (TryGetFallbackWaveSpawnLocation(region, agentProto, reachableOrigin, orientation, pathFlags, posFlags, blockFlags, out position, out cell))
            {
                placementMode = "fallback-reachable-cell";
                return true;
            }

            return false;
        }

        private Vector3[] BuildCornerAnchors(EndlessCableScenarioRunState runState, Region region)
        {
            Player player = _game.EntityManager.GetEntityByDbGuid<Player>(runState.PlayerDbId);
            Avatar avatar = player?.CurrentAvatar;
            Area area = avatar?.Area ?? region.IterateAreas().FirstOrDefault();
            if (area == null || area.RegionBounds.IsValid() == false)
            {
                Vector3 origin = avatar?.RegionLocation.Position ?? Vector3.Zero;
                return
                [
                    origin + new Vector3(-SpawnAnchorOffsetMin, -SpawnAnchorOffsetMin, 0.0f),
                    origin + new Vector3(-SpawnAnchorOffsetMin,  SpawnAnchorOffsetMin, 0.0f),
                    origin + new Vector3( SpawnAnchorOffsetMin,  SpawnAnchorOffsetMin, 0.0f),
                    origin + new Vector3( SpawnAnchorOffsetMin, -SpawnAnchorOffsetMin, 0.0f)
                ];
            }

            Aabb bounds = area.RegionBounds;
            Vector3 originInRoom = avatar?.RegionLocation.Position ?? RegionLocation.ProjectToFloor(region, bounds.Center);
            float offsetX = Math.Clamp(bounds.Width * SpawnAnchorOffsetPct, SpawnAnchorOffsetMin, SpawnAnchorOffsetMax);
            float offsetY = Math.Clamp(bounds.Length * SpawnAnchorOffsetPct, SpawnAnchorOffsetMin, SpawnAnchorOffsetMax);

            Vector3[] anchors =
            [
                originInRoom + new Vector3(-offsetX, -offsetY, 0.0f),
                originInRoom + new Vector3(-offsetX,  offsetY, 0.0f),
                originInRoom + new Vector3( offsetX,  offsetY, 0.0f),
                originInRoom + new Vector3( offsetX, -offsetY, 0.0f)
            ];

            for (int i = 0; i < anchors.Length; i++)
                anchors[i] = RegionLocation.ProjectToFloor(region, ClampToInnerAreaBounds(anchors[i], bounds));

            return anchors;
        }

        private Vector3 BuildBossAnchor(EndlessCableScenarioRunState runState, Region region)
        {
            Player player = _game.EntityManager.GetEntityByDbGuid<Player>(runState.PlayerDbId);
            Avatar avatar = player?.CurrentAvatar;
            Area area = avatar?.Area ?? region.IterateAreas().FirstOrDefault();

            if (area?.RegionBounds.IsValid() == true)
                return RegionLocation.ProjectToFloor(region, area.RegionBounds.Center);

            return avatar?.RegionLocation.Position ?? Vector3.Zero;
        }

        private static bool TryFinalizeWaveSpawnLocation(Region region, WorldEntityPrototype entityPrototype, Vector3 reachableOrigin, Orientation orientation,
            Vector3 candidatePosition, PathFlags pathFlags, PositionCheckFlags posFlags, BlockingCheckFlags blockFlags, out Vector3 position, out Cell cell)
        {
            position = Vector3.Zero;
            cell = null;

            if (region == null || entityPrototype == null)
                return false;

            candidatePosition = RegionLocation.ProjectToFloor(region, candidatePosition);
            Cell candidateCell = region.GetCellAtPosition(candidatePosition);
            if (candidateCell == null)
                return false;

            Bounds bounds = CreateSpawnBounds(entityPrototype, candidatePosition, orientation);
            if (region.IsLocationClear(ref bounds, pathFlags, posFlags, blockFlags) == false)
                return false;

            Vector3 pathOrigin = RegionLocation.ProjectToFloor(region, reachableOrigin);
            Cell pathOriginCell = region.GetCellAtPosition(pathOrigin);
            if (pathOriginCell != null && NaviPath.CheckCanPathTo(region.NaviMesh, pathOrigin, candidatePosition, bounds.Radius, pathFlags) != NaviPathResult.Success)
                return false;

            position = candidatePosition;
            cell = candidateCell;
            return true;
        }

        private static bool TryGetFallbackWaveSpawnLocation(Region region, WorldEntityPrototype entityPrototype, Vector3 reachableOrigin, Orientation orientation,
            PathFlags pathFlags, PositionCheckFlags posFlags, BlockingCheckFlags blockFlags, out Vector3 position, out Cell cell)
        {
            position = Vector3.Zero;
            cell = null;

            Vector3 origin = RegionLocation.ProjectToFloor(region, reachableOrigin);
            float offset = SpawnAnchorOffsetMin;

            Span<Vector3> offsets =
            [
                Vector3.Zero,
                Vector3.Right * offset,
                -Vector3.Right * offset,
                Vector3.Forward * offset,
                -Vector3.Forward * offset,
                (Vector3.Right + Vector3.Forward) * offset,
                (-Vector3.Right + Vector3.Forward) * offset,
                (Vector3.Right - Vector3.Forward) * offset,
                (-Vector3.Right - Vector3.Forward) * offset
            ];

            foreach (Vector3 offsetVector in offsets)
            {
                if (TryFinalizeWaveSpawnLocation(region, entityPrototype, reachableOrigin, orientation, origin + offsetVector, pathFlags, posFlags, blockFlags, out position, out cell))
                    return true;
            }

            Cell closestCell = FindClosestCell(region, origin);
            if (closestCell == null)
                return false;

            Vector3 closestCellCenter = RegionLocation.ProjectToFloor(region, closestCell.RegionBounds.Center);
            return TryFinalizeWaveSpawnLocation(region, entityPrototype, reachableOrigin, orientation, closestCellCenter, pathFlags, posFlags, blockFlags, out position, out cell);
        }

        private static Vector3 ClampToInnerAreaBounds(Vector3 position, Aabb bounds)
        {
            float marginX = GetSpawnInnerMargin(bounds.Width);
            float marginY = GetSpawnInnerMargin(bounds.Length);
            return new(
                ClampToInnerRange(position.X, bounds.Min.X, bounds.Max.X, marginX),
                ClampToInnerRange(position.Y, bounds.Min.Y, bounds.Max.Y, marginY),
                position.Z);
        }

        private static float GetSpawnInnerMargin(float length)
        {
            if (length <= 0.0f)
                return 0.0f;

            float desiredMargin = Math.Clamp(length * SpawnInnerBoundsMarginPct, SpawnInnerBoundsMarginMin, SpawnInnerBoundsMarginMax);
            return Math.Min(desiredMargin, length * 0.45f);
        }

        private static float ClampToInnerRange(float value, float min, float max, float margin)
        {
            if (max <= min)
                return value;

            float innerMin = min + margin;
            float innerMax = max - margin;
            if (innerMin > innerMax)
                return (min + max) * 0.5f;

            return Math.Clamp(value, innerMin, innerMax);
        }

        // ── Revive buff (ShopReviveBuffTEST → ShopRevivePowerTEST → ShopRevive1Condition) ───────
        // ShopRevivePowerTEST applies this condition to the avatar; its presence is the "buff on" state.
        private const string EndlessCableReviveBuffConditionPath = "Powers/ItemPowers/ItemConditions/ShopRevive1Condition.prototype";
        private PrototypeId _endlessCableReviveBuffConditionRef = PrototypeId.Invalid;

        // If true the buff condition is removed when it saves the player (one death = one charge).
        // Set false to make it persistent (every death auto-clears while the condition is active).
        private const bool ConsumeReviveBuffOnUse = true;

        private PrototypeId GetEndlessCableReviveBuffConditionRef()
        {
            if (_endlessCableReviveBuffConditionRef == PrototypeId.Invalid)
                _endlessCableReviveBuffConditionRef = GameDatabase.GetPrototypeRefByName(EndlessCableReviveBuffConditionPath);

            return _endlessCableReviveBuffConditionRef;
        }

        private bool HasEndlessCableReviveBuff(Avatar avatar)
        {
            if (avatar == null)
                return false;

            PrototypeId conditionRef = GetEndlessCableReviveBuffConditionRef();
            if (conditionRef == PrototypeId.Invalid)
                return false;

            ConditionCollection conditions = avatar.ConditionCollection;
            return conditions != null && conditions.GetConditionByRef(conditionRef) != null;
        }

        private void ConsumeEndlessCableReviveBuff(Avatar avatar)
        {
            ConditionCollection conditions = avatar?.ConditionCollection;
            if (conditions == null)
                return;

            PrototypeId conditionRef = GetEndlessCableReviveBuffConditionRef();
            if (conditionRef == PrototypeId.Invalid)
                return;

            ulong conditionId = conditions.GetConditionIdByRef(conditionRef);
            if (conditionId != ConditionCollection.InvalidConditionId)
                conditions.RemoveCondition(conditionId);
        }

        // Returns true if the death was intercepted: the player was revived in place and the
        // current wave was auto-cleared, so the run continues instead of ending.
        private bool TryReviveAndAutoClearWave(EndlessCableScenarioRunState runState, Player player, Avatar avatar, Region region, TimeSpan currentTime)
        {
            // Only meaningful for an in-progress, post-start run.
            if (runState.Status is not (EndlessCableScenarioRunStatus.Active or EndlessCableScenarioRunStatus.WaitingForNextWave))
                return false;

            if (player == null || region == null || HasEndlessCableReviveBuff(avatar) == false)
                return false;

            if (ConsumeReviveBuffOnUse)
                ConsumeEndlessCableReviveBuff(avatar);

            // Bring the player back to life at full health/endurance, in place.
            if (avatar.IsDead)
                avatar.Resurrect();

            AutoClearCurrentWave(runState, player, region, currentTime);

            _game.ChatManager?.SendChatFromCustomSystem(player, "[Cable Endless Scenario] Revive buff triggered — wave auto-cleared.", showSender: false);
            return true;
        }

        private void AutoClearCurrentWave(EndlessCableScenarioRunState runState, Player player, Region region, TimeSpan currentTime)
        {
            // Kill every still-alive tracked enemy so their normal OnKilled loot/credit drops fire,
            // exactly as if the player had cleared the wave themselves. Snapshot first because Kill()
            // can re-enter OnRegionEntityDead and mutate WaveEntityIds mid-iteration.
            using var killListHandle = ListPool<ulong>.Instance.Get(out List<ulong> killList);
            killList.AddRange(runState.WaveEntityIds);

            foreach (ulong entityId in killList)
            {
                Agent agent = _game.EntityManager.GetEntity<Agent>(entityId);
                if (agent != null && agent.IsAliveInWorld)
                    agent.Kill(null);
            }

            // Advance through the standard wave-clear path (banner, CompletedWaveCount bump,
            // WaitingForNextWave + NextWaveDelay schedule). MarkWaveClear inside HandleWaveClear
            // clears the tracking sets, so the queued EntityDead events above are ignored.
            // If the kill cascade already fired HandleWaveClear, this no-ops on the status guard.
            if (runState.Status == EndlessCableScenarioRunStatus.Active)
                HandleWaveClear(runState, player, currentTime);
        }

        private void CompleteRunOnPlayerDeath(EndlessCableScenarioRunState runState, Player player, Region region, Vector3 deathPosition, TimeSpan currentTime)
        {
            if (runState.Status is EndlessCableScenarioRunStatus.Completed or EndlessCableScenarioRunStatus.Aborted)
                return;

            // Snapshot first: Kill() fires EntityDeadEvent synchronously, which re-enters OnRegionEntityDead and
            // removes the entity from WaveEntityIds. Iterating the live set threw after the first kill, which left
            // the rest of the wave alive and skipped the reward chest for that call.
            using (var killListHandle = ListPool<ulong>.Instance.Get(out List<ulong> killList))
            {
                killList.AddRange(runState.WaveEntityIds);
                foreach (ulong entityId in killList)
                {
                    Agent agent = _game.EntityManager.GetEntity<Agent>(entityId);
                    if (agent != null && agent.IsAliveInWorld)
                    {
                        // Use the engine's built-in kill method rather than Destroy()
                        // This triggers the OnEntityDead event properly
                        agent.Kill(null);
                    }

                    // Anything that refuses to die (invulnerable phase, death prevention, etc.) gets removed
                    agent = _game.EntityManager.GetEntity<Agent>(entityId);
                    if (agent != null && agent.IsAliveInWorld)
                        agent.Destroy();
                }
            }

            CleanScenarioCombatants(region);
            CleanupLeftoverRunCombatants(runState, region, "run end");

            Vector3 rewardAnchor = BuildCompletionRewardAnchor(runState, region, deathPosition);
            BuildRewardObjectAnchors(player, rewardAnchor, out Vector3 chestAnchor, out Vector3 vendorAnchor, out _);
            List<Vector3> occupiedRewardPositions = [];

            // Run end messages can be overridden by scripts (see Data/Scripts/dangerroom)
            bool showDefaultFinishedMessages = true;
            if (ScriptHooks.DangerRoomFinished.HasHandlers)
            {
                DangerRoomFinishedArgs hookArgs = new(player, region, runState.CompletedWaveCount);
                ScriptHooks.DangerRoomFinished.Invoke(hookArgs);
                showDefaultFinishedMessages = hookArgs.ShowDefaultMessages;
            }

            TrySpawnDangerRoomRewardChest(runState, region, player, chestAnchor, occupiedRewardPositions, out ulong rewardChestId, out Vector3 rewardChestPosition);
            if (rewardChestId != 0)
            {
                occupiedRewardPositions.Add(rewardChestPosition);
                if (showDefaultFinishedMessages)
                    SendDangerRoomFinishedBanner(player);


                WorldEntity chest = _game.EntityManager.GetEntity<WorldEntity>(rewardChestId);
                if (chest != null)
                {
                    if (player?.CurrentAvatar != null)
                    {
                        player.CurrentAvatar.Properties[PropertyEnum.RespawnHotspotOverrideInst] = chest.Id;
                    }
                }
                PrototypeId mapIconProto = GameDatabase.GlobalsPrototype?.ObjectiveMarkerTemplate ?? PrototypeId.Invalid;
                PrototypeId arrowProto = GameDatabase.GlobalsPrototype?.PointerArrowTemplate ?? PrototypeId.Invalid;
                void SpawnBeacon(PrototypeId beaconProto)
                {
                    if (beaconProto == PrototypeId.Invalid) return;

                    using EntitySettings settings = ObjectPoolManager.Instance.Get<EntitySettings>();
                    settings.EntityRef = beaconProto;
                    settings.RegionId = region.Id;
                    settings.Position = rewardChestPosition;

                    using PropertyCollection props = ObjectPoolManager.Instance.Get<PropertyCollection>();
                    props[PropertyEnum.Visible] = true;
                    settings.Properties = props;

                    _game.EntityManager.CreateEntity(settings);
                }

                // Spawns the permanent minimap blip
                SpawnBeacon(mapIconProto);
                // Spawns the permanent 3D yellow pointer arrow over the chest
                SpawnBeacon(arrowProto);

                _game.ChatManager?.SendChatFromCustomSystem(player, $"[Locator] The Reward Chest has spawned at coordinates: {rewardChestPosition.X:0}, {rewardChestPosition.Y:0}", showSender: false);
                // --------------------------------------
            }

            // Only spawn the completion crafter if the player cleared wave 10 or higher
            if (runState.CompletedWaveCount >= 10)
            {
                TrySpawnCompletionVendor(runState, region, player, vendorAnchor, occupiedRewardPositions, out Vector3 completionVendorPosition);
                if (runState.HasCompletionVendor)
                    occupiedRewardPositions.Add(completionVendorPosition);
            }

            RestoreRegionDifficulty(runState, region);
            ClearCableObjectiveWidgets(runState, region);
            runState.Complete(currentTime);

            _game.ChatManager?.SendChatFromCustomSystem(player, $"[Cable Endless Scenario] Run ended. Highest cleared wave: {runState.CompletedWaveCount}.", showSender: false);
        }

        private Vector3 BuildCompletionRewardAnchor(EndlessCableScenarioRunState runState, Region region, Vector3 fallbackPosition)
        {
            if (runState == null || region == null)
                return fallbackPosition;

            Vector3 center = BuildBossAnchor(runState, region);
            if (region.GetCellAtPosition(center) != null)
                return center;

            return fallbackPosition;
        }

        private void OpenRewardChest(EndlessCableScenarioRunState runState, Player player, WorldEntity chest)
        {
            if (runState == null || player?.CurrentAvatar == null || chest == null)
                return;

            Region region = chest.Region ?? player.GetRegion();
            if (region == null)
                return;

            Vector3 chestPosition = chest.RegionLocation.Position;
            DisableNativeRewardChestLoot(chest);
            SpawnCompletionChestLoot(runState, player, region, chestPosition);
            runState.MarkRewardChestOpened();

            chest.Properties[PropertyEnum.Interactable] = (int)TriBool.False;
            chest.Properties[PropertyEnum.InteractableUsesLeft] = 0;
            chest.SetVisible(false);
            chest.UpdateInterestPolicies(true);
            chest.Destroy();

            List<Vector3> occupiedRewardPositions = [];
            if (runState.HasCompletionVendor && _game.EntityManager.GetEntity<WorldEntity>(runState.CompletionVendorEntityId) is WorldEntity vendor)
                occupiedRewardPositions.Add(vendor.RegionLocation.Position);

            BuildRewardObjectAnchors(player, chestPosition, out _, out _, out Vector3 portalAnchor);
            TrySpawnReturnPortal(runState, region, player, portalAnchor, occupiedRewardPositions, out ulong returnPortalId);
        }

        private void SpawnCompletionChestLoot(EndlessCableScenarioRunState runState, Player player, Region region, Vector3 anchorPosition)
        {
            if (player?.CurrentAvatar == null || region == null)
                return;

            EnsureCompletionRewardPrototypes();
            if (_dangerRoomRewardChestLootTableRef.As<LootTablePrototype>() == null)
            {
                return;
            }

            int clearedWaves = Math.Max(runState.CompletedWaveCount, 0);
            int rewardWaves = Math.Max(clearedWaves, 1);
            int bonusRewardWaves = GetLiveTuningBonusWaveCount(clearedWaves);
            float xpBonusPerWave = GetRewardXPBonusPerWave();
            float xpRewardMultiplier = GetRewardMultiplierForWaves(bonusRewardWaves, xpBonusPerWave);
            float spawnedOrbExperienceBonusPct = Math.Max(0.0f, xpRewardMultiplier - 1.0f);
            float lootRewardMultiplier = GetRewardMultiplierForWaves(bonusRewardWaves, RewardLootBonusPerWave);

            float originalLootXPPct = region.Properties[PropertyEnum.LootBonusXPPct];
            float originalLootRarityPct = region.Properties[PropertyEnum.LootBonusRarityPct];
            float originalLootSpecialPct = region.Properties[PropertyEnum.LootBonusSpecialPct];
            float bonusExperienceOrbsPerWave = GetBonusExperienceOrbsPerRewardWave();
            int bonusExperienceOrbCount = GetBonusExperienceOrbCount(bonusRewardWaves, bonusExperienceOrbsPerWave);
            int spawnedBonusExperienceOrbs = 0;

            try
            {
                region.Properties[PropertyEnum.LootBonusXPPct] = CombineBonusPctWithMultiplier(originalLootXPPct, xpRewardMultiplier);
                region.Properties[PropertyEnum.LootBonusRarityPct] = CombineBonusPctWithMultiplier(originalLootRarityPct, lootRewardMultiplier);
                region.Properties[PropertyEnum.LootBonusSpecialPct] = CombineBonusPctWithMultiplier(originalLootSpecialPct, lootRewardMultiplier);

                SpawnCompletionLootRoll(player, region, _dangerRoomRewardChestLootTableRef, anchorPosition, 0, spawnedOrbExperienceBonusPct);
                spawnedBonusExperienceOrbs = SpawnBonusExperienceOrbs(player, region, anchorPosition, 1, bonusExperienceOrbCount, spawnedOrbExperienceBonusPct);
            }
            finally
            {
                region.Properties[PropertyEnum.LootBonusXPPct] = originalLootXPPct;
                region.Properties[PropertyEnum.LootBonusRarityPct] = originalLootRarityPct;
                region.Properties[PropertyEnum.LootBonusSpecialPct] = originalLootSpecialPct;
            }

            (int spawnedDangerRoomMerits, bool dangerRoomMeritsDropped) = SpawnBonusDangerRoomMerits(runState, player, region, anchorPosition, 1 + bonusExperienceOrbCount, bonusRewardWaves);

            // Bonus item rewards can be extended or replaced by scripts (see Data/Scripts/dangerroom)
            DangerRoomRewardsArgs rewardArgs = null;
            if (ScriptHooks.DangerRoomRewards.HasHandlers)
            {
                rewardArgs = new(player, region, clearedWaves);
                ScriptHooks.DangerRoomRewards.Invoke(rewardArgs);

                // Script slots use the same numbering as the built-in ladder, offset past the experience orbs like it
                int nextFreeSlot = 40;
                foreach (DangerRoomRewardsArgs.Reward reward in rewardArgs.Rewards)
                {
                    int slot = reward.Slot >= 0 ? reward.Slot : nextFreeSlot++;
                    SpawnMultipleBonusItems(runState, reward.ItemRef, reward.Count, player, region, anchorPosition, slot + bonusExperienceOrbCount);
                }
            }

            if (rewardArgs != null && rewardArgs.ReplaceDefaultRewards)
                return;

            if (clearedWaves >= 10)
            {
                SpawnMultipleBonusItems(runState, _ageOfUltronFCRewardRef, 10, player, region, anchorPosition, 20 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 15)
            {
                SpawnMultipleBonusItems(runState, _cowpocalypseFCRewardRef, 3, player, region, anchorPosition, 21 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 16)
            {
                SpawnBonusGem(runState, player, region, anchorPosition, 2 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 19)
            {
                SpawnMultipleBonusItems(runState, _gotGVol2FCRewardRef, 10, player, region, anchorPosition, 22 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 20)
            {
                SpawnBonusItem(runState, _armorDriveBoxRewardRef, player, region, anchorPosition, 4 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 21)
            {
                SpawnBonusItem(runState, _stoneOfJordanRewardRef, player, region, anchorPosition, 5 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 23)
            {
                SpawnMultipleBonusItems(runState, _spideyHomecomingFCRewardRef, 10, player, region, anchorPosition, 23 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 24)
            {
                SpawnBonusGem(runState, player, region, anchorPosition, 2 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 25)
            {
                SpawnWave25PetBox(runState, player, region, anchorPosition, 8 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 26)
            {
                SpawnMultipleBonusItems(runState, _loganFCRewardRef, 10, player, region, anchorPosition, 24 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 27)
            {
                SpawnBonusItem(runState, _legendary014RewardRef, player, region, anchorPosition, 9 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 28)
            {
                SpawnBonusItem(runState, _ProtectorCommedationRewardRef, player, region, anchorPosition, 6 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 29)
            {
                SpawnMultipleBonusItems(runState, _xMenFCRewardRef, 10, player, region, anchorPosition, 25 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 30)
            {
                SpawnBonusItem(runState, _heroCommendationBoxRewardRef, player, region, anchorPosition, 10 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 31)
            {
                SpawnBonusItem(runState, _randomCosmicArtifactRewardRef, player, region, anchorPosition, 19 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 32)
            {
                SpawnBonusItem(runState, _art340CosmicRewardRef, player, region, anchorPosition, 13 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 33)
            {
                SpawnMultipleBonusItems(runState, _odinsBountyFCRewardRef, 10, player, region, anchorPosition, 26 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 34)
            {
                SpawnMultipleBonusItems(runState, _winterFCRewardRef, 10, player, region, anchorPosition, 27 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 35)
            {
                SpawnBonusItem(runState, _genoshaInfluenceRewardRef, player, region, anchorPosition, 11 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 36)
            {
                SpawnMultipleBonusItems(runState, _secretInvasionFCRewardRef, 10, player, region, anchorPosition, 28 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 37)
            {
                SpawnBonusGem(runState, player, region, anchorPosition, 2 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 38)
            {
                SpawnBonusItem(runState, _fc75EternitySplintersRewardRef, player, region, anchorPosition, 14 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 40)
            {
                SpawnBonusItem(runState, _largeRuneboxRewardRef, player, region, anchorPosition, 12 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 45)
            {
                SpawnBonusItem(runState, _legendary030RewardRef, player, region, anchorPosition, 15 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 50)
            {
                SpawnBonusItem(runState, _runewordGlyph038RewardRef, player, region, anchorPosition, 16 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 55)
            {
                SpawnBonusItem(runState, _fcmkii100EternitySplintersRewardRef, player, region, anchorPosition, 17 + bonusExperienceOrbCount);
            }
            if (clearedWaves >= 60)
            {
                SpawnBonusItem(runState, _legendary029RewardRef, player, region, anchorPosition, 18 + bonusExperienceOrbCount);
            }
        }

        private (int amount, bool dropped) SpawnBonusDangerRoomMerits(EndlessCableScenarioRunState runState, Player player, Region region, Vector3 anchorPosition, int rollIndex, int bonusRewardWaves)
        {
            if (BonusDangerRoomMeritsPerRewardWave <= 0 || bonusRewardWaves <= 0 || player?.CurrentAvatar == null || region == null)
                return (0, false);

            PrototypeId dangerRoomMeritsRef = GameDatabase.CurrencyGlobalsPrototype.DangerRoomMerits;
            CurrencyPrototype dangerRoomMeritsProto = dangerRoomMeritsRef.As<CurrencyPrototype>();
            if (dangerRoomMeritsProto == null)
            {
                return (0, false);
            }

            PropertyId currencyProperty = new(PropertyEnum.Currency, dangerRoomMeritsRef);
            int currentAmount = player.Properties[currencyProperty];
            int grantAmount = BonusDangerRoomMeritsPerRewardWave * bonusRewardWaves;
            if (dangerRoomMeritsProto.MaxAmount > 0)
                grantAmount = Math.Min(grantAmount, Math.Max(dangerRoomMeritsProto.MaxAmount - currentAmount, 0));

            if (grantAmount <= 0)
            {
                return (0, false);
            }

            EnsureCompletionRewardPrototypes();
            WorldEntityPrototype meritItemPrototype = _dangerRoomMeritsItemRef.As<WorldEntityPrototype>();
            if (meritItemPrototype != null)
            {
                Vector3 dropPosition = GetCompletionLootDropPosition(region, anchorPosition, rollIndex);

                using LootInputSettings inputSettings = ObjectPoolManager.Instance.Get<LootInputSettings>();
                inputSettings.Initialize(LootContext.Drop, player, null, player.CurrentAvatar.CharacterLevel, dropPosition);
                inputSettings.EventType = LootDropEventType.OnKilled;
                inputSettings.LootRollSettings.DropChanceModifiers |= LootDropChanceModifiers.IgnoreCooldown;

                using LootResultSummary lootResultSummary = ObjectPoolManager.Instance.Get<LootResultSummary>();
                CurrencySpec currencySpec = new(_dangerRoomMeritsItemRef, dangerRoomMeritsRef, grantAmount);
                lootResultSummary.Add(new LootResult(currencySpec));

                if (_game.LootManager.SpawnLootFromSummary(lootResultSummary, inputSettings, RewardRecipientId))
                    return (grantAmount, true);

            }
            else
            {
            }

            player.Properties.AdjustProperty(grantAmount, currencyProperty);
            region?.CurrencyCollectedEvent.Invoke(new(player, dangerRoomMeritsRef, player.Properties[currencyProperty]));
            player.OnScoringEvent(new(ScoringEventType.CurrencyCollected, dangerRoomMeritsProto, grantAmount));

            return (grantAmount, false);
        }

        private static int GetBonusExperienceOrbCount(int rewardWaves, float bonusOrbsPerWave)
        {
            rewardWaves = Math.Max(rewardWaves, 0);
            bonusOrbsPerWave = SanitizeNonNegativeTuningValue(bonusOrbsPerWave, 0.0f);

            double orbCount = rewardWaves * (double)bonusOrbsPerWave;
            if (double.IsNaN(orbCount) || double.IsInfinity(orbCount) || orbCount <= 0.0d)
                return 0;

            return (int)Math.Min(Math.Floor(orbCount), int.MaxValue);
        }

        private int SpawnBonusExperienceOrbs(Player player, Region region, Vector3 anchorPosition, int rollIndexStart, int orbCount, float spawnedOrbExperienceBonusPct)
        {
            if (orbCount <= 0 || player?.CurrentAvatar == null || region == null)
                return 0;

            EnsureCompletionRewardPrototypes();
            WorldEntityPrototype orbPrototype = _bonusExperienceOrbRef.As<WorldEntityPrototype>();
            if (orbPrototype == null)
            {
                return 0;
            }

            int spawnedOrbs = 0;
            for (int i = 0; i < orbCount; i++)
            {
                Vector3 dropPosition = GetCompletionLootDropPosition(region, anchorPosition, rollIndexStart + i);

                using LootInputSettings inputSettings = ObjectPoolManager.Instance.Get<LootInputSettings>();
                inputSettings.Initialize(LootContext.Drop, player, null, player.CurrentAvatar.CharacterLevel, dropPosition);
                inputSettings.EventType = LootDropEventType.OnKilled;
                inputSettings.SpawnedOrbExperienceBonusPct = spawnedOrbExperienceBonusPct;
                inputSettings.LootRollSettings.DropChanceModifiers |= LootDropChanceModifiers.IgnoreCooldown;

                using LootResultSummary lootResultSummary = ObjectPoolManager.Instance.Get<LootResultSummary>();
                AgentSpec agentSpec = new(_bonusExperienceOrbRef, player.CurrentAvatar.CharacterLevel, 0);
                lootResultSummary.Add(new LootResult(agentSpec));

                if (_game.LootManager.SpawnLootFromSummary(lootResultSummary, inputSettings, RewardRecipientId))
                    spawnedOrbs++;
            }

            return spawnedOrbs;
        }

        private void SpawnCompletionLootRoll(Player player, Region region, PrototypeId lootTableProtoRef, Vector3 anchorPosition, int rollIndex, float spawnedOrbExperienceBonusPct)
        {
            if (player?.CurrentAvatar == null || region == null || lootTableProtoRef == PrototypeId.Invalid)
                return;

            Vector3 dropPosition = GetCompletionLootDropPosition(region, anchorPosition, rollIndex);
            using LootInputSettings inputSettings = ObjectPoolManager.Instance.Get<LootInputSettings>();
            inputSettings.Initialize(LootContext.Drop, player, null, player.CurrentAvatar.CharacterLevel, dropPosition);
            inputSettings.EventType = LootDropEventType.OnKilled;
            inputSettings.SpawnedOrbExperienceBonusPct = spawnedOrbExperienceBonusPct;
            inputSettings.LootRollSettings.DropChanceModifiers |= LootDropChanceModifiers.IgnoreCooldown;
            _game.LootManager.SpawnLootFromTable(lootTableProtoRef, inputSettings, RewardRecipientId);
        }

        private static Vector3 GetCompletionLootDropPosition(Region region, Vector3 anchorPosition, int rollIndex)
        {
            if (region == null)
                return anchorPosition;

            if (rollIndex <= 0)
                return RegionLocation.ProjectToFloor(region, anchorPosition);

            const float goldenAngle = 2.39996323f;
            float radius = Math.Min(RewardLootMaxRadius, RewardLootMinRadius + ((rollIndex - 1) % 8) * RewardLootRadiusStep);
            float angle = rollIndex * goldenAngle;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                float attemptAngle = angle + attempt * (MathF.PI * 0.25f);
                float attemptRadius = Math.Min(RewardLootMaxRadius, radius + attempt * 20.0f);
                Vector3 candidate = new(
                    anchorPosition.X + MathF.Cos(attemptAngle) * attemptRadius,
                    anchorPosition.Y + MathF.Sin(attemptAngle) * attemptRadius,
                    anchorPosition.Z);

                candidate = RegionLocation.ProjectToFloor(region, candidate);
                if (region.GetCellAtPosition(candidate) == null)
                    continue;

                if (region.NaviMesh.Contains(candidate, 32.0f, new DefaultContainsPathFlagsCheck(PathFlags.Walk)))
                    return candidate;
            }

            return RegionLocation.ProjectToFloor(region, anchorPosition);
        }

        private static float GetRewardXPBonusPerWave()
        {
            return GetLiveEndlessCableTuningValue(
                GlobalTuningVar.eGTV_EndlessCableRewardXPBonusPerWave,
                DefaultRewardXPBonusPerWave);
        }

        private static float GetEnemyHealthWaveMultiplier(int wave)
        {
            float bonusPerWave = GetLiveEndlessCableTuningValue(
                GlobalTuningVar.eGTV_EndlessCableEnemyHealthBonusPerWave,
                DefaultEnemyHealthBonusPerWave);

            return GetWaveMultiplier(wave, bonusPerWave);
        }

        private static float GetEnemyDamageWaveMultiplier(int wave)
        {
            float bonusPerWave = GetLiveEndlessCableTuningValue(
                GlobalTuningVar.eGTV_EndlessCableEnemyDamageBonusPerWave,
                DefaultEnemyDamageBonusPerWave);

            return GetWaveMultiplier(wave, bonusPerWave);
        }

        private static float GetWaveXPRewardMultiplier(int wave)
        {
            return GetWaveMultiplier(wave, GetRewardXPBonusPerWave());
        }

        private static float GetBonusExperienceOrbsPerRewardWave()
        {
            return GetLiveEndlessCableTuningValue(
                GlobalTuningVar.eGTV_EndlessCableBonusXPOrbsPerWave,
                DefaultBonusXPOrbsPerWave);
        }

        private static bool IsCompletionCrafterEnabled()
        {
            return GetLiveEndlessCableTuningValue(
                GlobalTuningVar.eGTV_EndlessCableCompletionCrafterEnabled,
                DefaultCompletionCrafterEnabled) > 0.0f;
        }

        private static float GetLiveEndlessCableTuningValue(GlobalTuningVar tuningVar, float fallbackValue)
        {
            float tunedValue = LiveTuningManager.GetLiveGlobalTuningVar(tuningVar);
            return SanitizeNonNegativeTuningValue(tunedValue, fallbackValue);
        }

        private static float SanitizeNonNegativeTuningValue(float value, float fallbackValue)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                value = fallbackValue;

            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0.0f;

            return Math.Max(value, 0.0f);
        }

        private static float GetRewardMultiplierForWaves(int waves, float bonusPerWave)
        {
            waves = Math.Max(waves, 0);
            bonusPerWave = SanitizeNonNegativeTuningValue(bonusPerWave, 0.0f);
            return 1.0f + Math.Clamp(waves * bonusPerWave, 0.0f, MaxLiveTunedMultiplier - 1.0f);
        }

        private void EnsureCompletionRewardPrototypes()
        {
            if (_dangerRoomRewardChestRef == PrototypeId.Invalid)
                _dangerRoomRewardChestRef = GameDatabase.GetPrototypeRefByName(DangerRoomRewardChestPrototypeName);

            if (_dangerRoomRewardChestLootTableRef == PrototypeId.Invalid)
                _dangerRoomRewardChestLootTableRef = GameDatabase.GetPrototypeRefByName(DangerRoomRewardChestLootTablePrototypeName);

            if (_bonusExperienceOrbRef == PrototypeId.Invalid)
                _bonusExperienceOrbRef = GameDatabase.GetPrototypeRefByName(BonusExperienceOrbPrototypeName);

            if (_dangerRoomMeritsItemRef == PrototypeId.Invalid)
                _dangerRoomMeritsItemRef = GameDatabase.GetPrototypeRefByName(DangerRoomMeritsItemPrototypeName);

            if (_cableReturnPortalRef == PrototypeId.Invalid)
                _cableReturnPortalRef = GameDatabase.GetPrototypeRefByName(CableReturnPortalPrototypeName);
            if (_gemRewardRef == PrototypeId.Invalid)
                _gemRewardRef = GameDatabase.GetPrototypeRefByName(GemRewardPrototypeName);
            if (_petBoxRewardRef == PrototypeId.Invalid)
                _petBoxRewardRef = GameDatabase.GetPrototypeRefByName(PetBoxRewardPrototypeName);
            if (_largeRuneboxRewardRef == PrototypeId.Invalid)
                _largeRuneboxRewardRef = GameDatabase.GetPrototypeRefByName(LargeRuneboxRewardPrototypeName);
            if (_armorDriveBoxRewardRef == PrototypeId.Invalid)
                _armorDriveBoxRewardRef = GameDatabase.GetPrototypeRefByName(ARMORDriveBoxRewardPrototypeName);
            if (_heroCommendationBoxRewardRef == PrototypeId.Invalid)
                _heroCommendationBoxRewardRef = GameDatabase.GetPrototypeRefByName(HeroCommendationBoxRewardPrototypeName);
            if (_ProtectorCommedationRewardRef == PrototypeId.Invalid)
                _ProtectorCommedationRewardRef = GameDatabase.GetPrototypeRefByName(ProtectorCommedationRewardPrototypeName);
            if (_stoneOfJordanRewardRef == PrototypeId.Invalid)
                _stoneOfJordanRewardRef = GameDatabase.GetPrototypeRefByName(StoneOfJordanRewardPrototypeName);
            if (_legendary014RewardRef == PrototypeId.Invalid)
                _legendary014RewardRef = GameDatabase.GetPrototypeRefByName(Legendary014RewardPrototypeName);
            if (_genoshaInfluenceRewardRef == PrototypeId.Invalid)
                _genoshaInfluenceRewardRef = GameDatabase.GetPrototypeRefByName(GenoshaInfluenceRewardPrototypeName);
            if (_art340CosmicRewardRef == PrototypeId.Invalid)
                _art340CosmicRewardRef = GameDatabase.GetPrototypeRefByName(Art340CosmicRewardPrototypeName);
            if (_fc75EternitySplintersRewardRef == PrototypeId.Invalid)
                _fc75EternitySplintersRewardRef = GameDatabase.GetPrototypeRefByName(FC75EternitySplintersRewardPrototypeName);
            if (_legendary030RewardRef == PrototypeId.Invalid)
                _legendary030RewardRef = GameDatabase.GetPrototypeRefByName(Legendary030RewardPrototypeName);
            if (_runewordGlyph038RewardRef == PrototypeId.Invalid)
                _runewordGlyph038RewardRef = GameDatabase.GetPrototypeRefByName(RunewordGlyph038RewardPrototypeName);
            if (_fcmkii100EternitySplintersRewardRef == PrototypeId.Invalid)
                _fcmkii100EternitySplintersRewardRef = GameDatabase.GetPrototypeRefByName(FCMKII100EternitySplintersRewardPrototypeName);
            if (_legendary029RewardRef == PrototypeId.Invalid)
                _legendary029RewardRef = GameDatabase.GetPrototypeRefByName(Legendary029RewardPrototypeName);
            if (_ageOfUltronFCRewardRef == PrototypeId.Invalid) _ageOfUltronFCRewardRef = GameDatabase.GetPrototypeRefByName(AgeOfUltronFCRewardPrototypeName);
            if (_cowpocalypseFCRewardRef == PrototypeId.Invalid) _cowpocalypseFCRewardRef = GameDatabase.GetPrototypeRefByName(CowpocalypseFCRewardPrototypeName);
            if (_gotGVol2FCRewardRef == PrototypeId.Invalid) _gotGVol2FCRewardRef = GameDatabase.GetPrototypeRefByName(GotGVol2FCRewardPrototypeName);
            if (_spideyHomecomingFCRewardRef == PrototypeId.Invalid) _spideyHomecomingFCRewardRef = GameDatabase.GetPrototypeRefByName(SpideyHomecomingFCRewardPrototypeName);
            if (_loganFCRewardRef == PrototypeId.Invalid) _loganFCRewardRef = GameDatabase.GetPrototypeRefByName(LoganFCRewardPrototypeName);
            if (_xMenFCRewardRef == PrototypeId.Invalid) _xMenFCRewardRef = GameDatabase.GetPrototypeRefByName(XMenFCRewardPrototypeName);
            if (_odinsBountyFCRewardRef == PrototypeId.Invalid) _odinsBountyFCRewardRef = GameDatabase.GetPrototypeRefByName(OdinsBountyFCRewardPrototypeName);
            if (_winterFCRewardRef == PrototypeId.Invalid) _winterFCRewardRef = GameDatabase.GetPrototypeRefByName(WinterFCRewardPrototypeName);
            if (_secretInvasionFCRewardRef == PrototypeId.Invalid) _secretInvasionFCRewardRef = GameDatabase.GetPrototypeRefByName(SecretInvasionFCRewardPrototypeName);
        }

        private bool TrySpawnDangerRoomRewardChest(EndlessCableScenarioRunState runState, Region region, Player player, Vector3 anchorPosition, IReadOnlyList<Vector3> avoidPositions, out ulong chestEntityId, out Vector3 chestPosition)
        {
            chestEntityId = 0;
            chestPosition = Vector3.Zero;

            if (region == null || player?.CurrentAvatar == null)
                return false;

            if (runState.HasRewardChest && _game.EntityManager.GetEntity<WorldEntity>(runState.RewardChestEntityId) is WorldEntity existingChest)
            {
                chestEntityId = runState.RewardChestEntityId;
                chestPosition = existingChest.RegionLocation.Position;
                return true;
            }

            EnsureCompletionRewardPrototypes();

            WorldEntityPrototype chestPrototype = _dangerRoomRewardChestRef.As<WorldEntityPrototype>();
            if (chestPrototype == null)
                return false;

            if (_dangerRoomRewardChestLootTableRef.As<LootTablePrototype>() == null)
                return false;

            Orientation orientation = player.CurrentAvatar.RegionLocation.Orientation;
            EnsureRewardChestLootSourceOverride(region);
            if (TryResolveVendorSpawnLocation(region, chestPrototype, anchorPosition, orientation, out Vector3 spawnPosition, out Cell spawnCell, out string placementMode, avoidPositions, RewardObjectAvoidDistance) == false)
            {
                return false;
            }

            using EntitySettings settings = ObjectPoolManager.Instance.Get<EntitySettings>();
            settings.EntityRef = _dangerRoomRewardChestRef;
            settings.RegionId = region.Id;
            settings.Position = spawnPosition;
            settings.Orientation = orientation;
            settings.Cell = spawnCell;
            settings.Lifespan = CompletedRunRetention;
            settings.SourceEntityId = player.CurrentAvatar.Id;

            using PropertyCollection settingsProperties = ObjectPoolManager.Instance.Get<PropertyCollection>();
            settingsProperties[PropertyEnum.Interactable] = (int)TriBool.True;
            settingsProperties[PropertyEnum.InteractableUsesLeft] = 1;
            settingsProperties[PropertyEnum.Visible] = true;
            settingsProperties[PropertyEnum.LootTableSource] = AssetId.Invalid;
            settingsProperties[PropertyEnum.RestrictedToPlayerGuid] = player.DatabaseUniqueId;
            settingsProperties[PropertyEnum.CharacterLevel] = player.CurrentAvatar.CharacterLevel;
            settingsProperties[PropertyEnum.CombatLevel] = player.CurrentAvatar.CombatLevel;
            settings.Properties = settingsProperties;

            WorldEntity chest = _game.EntityManager.CreateEntity(settings) as WorldEntity;
            if (chest == null)
                return false;

            DisableNativeRewardChestLoot(chest);
            chest.SetVisible(true);
            chest.UpdateInterestPolicies(true);
            chestEntityId = chest.Id;
            chestPosition = chest.RegionLocation.Position;
            runState.AttachRewardChest(chest.Id);

            return true;
        }

        private void EnsureRewardChestLootSourceOverride(Region region)
        {
            if (region == null || _dangerRoomRewardChestLootTableRef == PrototypeId.Invalid)
                return;

            AssetId onInteractedWith = Property.PropertyEnumToAsset(PropertyEnum.LootTablePrototype, 0, (int)LootDropEventType.OnInteractedWith);
            if (onInteractedWith != AssetId.Invalid)
                region.Properties[PropertyEnum.LootSourceTableOverride, DangerRoomVictoryRoomLootSourceRef, onInteractedWith] = _dangerRoomRewardChestLootTableRef;

            AssetId unspecified = Property.PropertyEnumToAsset(PropertyEnum.LootTablePrototype, 0, (int)LootDropEventType.None);
            if (unspecified != AssetId.Invalid)
                region.Properties[PropertyEnum.LootSourceTableOverride, DangerRoomVictoryRoomLootSourceRef, unspecified] = _dangerRoomRewardChestLootTableRef;
        }
        private void SpawnBonusGem(EndlessCableScenarioRunState runState, Player player, Region region, Vector3 anchorPosition, int rollIndex)
        {
            if (player?.CurrentAvatar == null || region == null)
                return;

            EnsureCompletionRewardPrototypes();
            if (_gemRewardRef == PrototypeId.Invalid)
            {
                return;
            }

            Vector3 dropPosition = GetCompletionLootDropPosition(region, anchorPosition, rollIndex);

            using LootInputSettings inputSettings = ObjectPoolManager.Instance.Get<LootInputSettings>();
            inputSettings.Initialize(LootContext.Drop, player, null, player.CurrentAvatar.CharacterLevel, dropPosition);
            inputSettings.EventType = LootDropEventType.OnKilled;
            inputSettings.LootRollSettings.DropChanceModifiers |= LootDropChanceModifiers.IgnoreCooldown;

            using LootResultSummary lootResultSummary = ObjectPoolManager.Instance.Get<LootResultSummary>();

            ItemSpec gemSpec = _game.LootManager.CreateItemSpec(_gemRewardRef, LootContext.Drop, player);
            if (gemSpec != null)
            {
                lootResultSummary.Add(new LootResult(gemSpec));
                _game.LootManager.SpawnLootFromSummary(lootResultSummary, inputSettings, RewardRecipientId);
            }
        }
        private static void DisableNativeRewardChestLoot(WorldEntity chest)
        {
            if (chest == null)
                return;

            chest.Properties.RemovePropertyRange(PropertyEnum.LootTablePrototype);
            chest.Properties.RemoveProperty(PropertyEnum.LootTableSource);
        }

        private bool TrySpawnCompletionVendor(EndlessCableScenarioRunState runState, Region region, Player player, Vector3 anchorPosition, IReadOnlyList<Vector3> avoidPositions, out Vector3 vendorPosition)
        {
            vendorPosition = Vector3.Zero;

            if (IsCompletionCrafterEnabled() == false)
                return false;

            if (runState.HasCompletionVendor && _game.EntityManager.GetEntity<WorldEntity>(runState.CompletionVendorEntityId) is WorldEntity existingVendor)
            {
                vendorPosition = existingVendor.RegionLocation.Position;
                return true;
            }

            WorldEntityPrototype vendorPrototype = EndlessCableCompletionCrafter.CompletionVendorPrototypeRef.As<WorldEntityPrototype>();
            if (vendorPrototype == null)
                return false;

            if (EndlessCableCompletionCrafter.CompletionVendorTypePrototypeRef.As<VendorTypePrototype>() == null)
                return false;

            Orientation orientation = player?.CurrentAvatar?.RegionLocation.Orientation ?? Orientation.Zero;
            if (TryResolveVendorSpawnLocation(region, vendorPrototype, anchorPosition, orientation, out Vector3 spawnPosition, out Cell spawnCell, out string placementMode, avoidPositions, RewardObjectAvoidDistance) == false)
            {
                return false;
            }

            using EntitySettings settings = ObjectPoolManager.Instance.Get<EntitySettings>();
            settings.EntityRef = EndlessCableCompletionCrafter.CompletionVendorPrototypeRef;
            settings.RegionId = region.Id;
            settings.Position = spawnPosition;
            settings.Orientation = orientation;
            settings.Cell = spawnCell;
            settings.Lifespan = CompletedRunRetention;
            settings.SourceEntityId = player?.CurrentAvatar?.Id ?? 0;

            using PropertyCollection settingsProperties = ObjectPoolManager.Instance.Get<PropertyCollection>();
            settingsProperties[PropertyEnum.Interactable] = (int)TriBool.True;
            settingsProperties[PropertyEnum.InteractableUsesLeft] = -1;
            settingsProperties[PropertyEnum.Visible] = true;
            settingsProperties[PropertyEnum.VendorType] = EndlessCableCompletionCrafter.CompletionVendorTypePrototypeRef;
            settings.Properties = settingsProperties;

            WorldEntity vendor = _game.EntityManager.CreateEntity(settings) as WorldEntity;
            if (vendor == null)
                return false;

            if (vendor is Agent vendorAgent)
                vendorAgent.SetDormant(false);

            vendor.SetVisible(true);
            vendor.UpdateInterestPolicies(true);
            runState.AttachCompletionVendor(vendor.Id);
            vendorPosition = vendor.RegionLocation.Position;

            return true;
        }

        private bool TrySpawnReturnPortal(EndlessCableScenarioRunState runState, Region region, Player player, Vector3 anchorPosition, IReadOnlyList<Vector3> avoidPositions, out ulong portalEntityId)
        {
            portalEntityId = 0;

            if (region == null || player?.CurrentAvatar == null)
                return false;

            if (runState.HasReturnPortal && _game.EntityManager.GetEntity<Transition>(runState.ReturnPortalEntityId) != null)
            {
                portalEntityId = runState.ReturnPortalEntityId;
                return true;
            }

            EnsureCompletionRewardPrototypes();

            if (_cableReturnPortalRef == PrototypeId.Invalid)
                return false;

            TransitionPrototype portalPrototype = _cableReturnPortalRef.As<TransitionPrototype>();
            if (portalPrototype == null)
                return false;

            if (TryResolveDangerRoomHubStartTarget(out PrototypeId dangerRoomHubStartTarget) == false)
                return false;

            Orientation orientation = player.CurrentAvatar.RegionLocation.Orientation;
            if (TryResolveVendorSpawnLocation(region, portalPrototype, anchorPosition, orientation, out Vector3 spawnPosition, out Cell spawnCell, out string placementMode, avoidPositions, RewardObjectAvoidDistance) == false)
            {
                return false;
            }

            using EntitySettings settings = ObjectPoolManager.Instance.Get<EntitySettings>();
            settings.EntityRef = _cableReturnPortalRef;
            settings.RegionId = region.Id;
            settings.Position = spawnPosition;
            settings.Orientation = orientation;
            settings.Cell = spawnCell;
            settings.Lifespan = CompletedRunRetention;
            settings.SourceEntityId = player.CurrentAvatar.Id;

            using PropertyCollection settingsProperties = ObjectPoolManager.Instance.Get<PropertyCollection>();
            settingsProperties[PropertyEnum.Interactable] = (int)TriBool.True;
            settingsProperties[PropertyEnum.InteractableUsesLeft] = -1;
            settingsProperties[PropertyEnum.Visible] = true;
            settings.Properties = settingsProperties;

            Transition portal = _game.EntityManager.CreateEntity(settings) as Transition;
            if (portal == null)
                return false;

            // if (portal.ConfigureDirectTarget(dangerRoomHubStartTarget) == false)
            // {
            //     portal.Destroy();
            //    return false;
            // }

            portal.SetVisible(true);
            portal.UpdateInterestPolicies(true);
            portalEntityId = portal.Id;
            runState.AttachReturnPortal(portal.Id);

            return true;
        }

        internal bool TryUseReturnPortal(Player player, Transition transition)
        {
            if (player == null || transition == null)
                return false;

            // Block native Danger Room scenario portals (they carry the scenario item guid) while this player has a run going
            if (transition.Properties[PropertyEnum.DangerRoomScenarioItemDbGuid] != 0UL && FindInProgressRunForPlayer(player.DatabaseUniqueId) != null)
            {
                _game.ChatManager?.SendChatFromCustomSystem(player, "[Cable Endless Scenario] Finish or leave your current Cable Danger Room scenario before starting another one.", showSender: false);
                return true;
            }

            EndlessCableScenarioRunState runState = null;
            foreach (EndlessCableScenarioRunState activeRunState in _activeRuns.Values)
            {
                if (activeRunState.ReturnPortalEntityId != transition.Id)
                    continue;

                runState = activeRunState;
                break;
            }

            if (runState == null || runState.Status != EndlessCableScenarioRunStatus.Completed)
                return false;

            if (TryResolveDangerRoomHubStartTarget(out PrototypeId dangerRoomHubStartTarget) == false)
                return false;

            using Teleporter teleporter = ObjectPoolManager.Instance.Get<Teleporter>();
            teleporter.Initialize(player, TeleportContextEnum.TeleportContext_Transition);
            teleporter.TransitionEntity = transition;

            bool teleported = teleporter.TeleportToTarget(dangerRoomHubStartTarget);

            return teleported;
        }

        private static bool TryResolveDangerRoomHubStartTarget(out PrototypeId startTargetRef)
        {
            startTargetRef = PrototypeId.Invalid;

            RegionPrototype dangerRoomHubRegion = ((PrototypeId)RegionPrototypeId.DangerRoomHubRegion).As<RegionPrototype>();
            if (dangerRoomHubRegion == null || dangerRoomHubRegion.StartTarget == PrototypeId.Invalid)
                return false;

            startTargetRef = dangerRoomHubRegion.StartTarget;
            return true;
        }

        private static void BuildRewardObjectAnchors(Player player, Vector3 anchorPosition, out Vector3 chestAnchor, out Vector3 vendorAnchor, out Vector3 portalAnchor)
        {
            Orientation orientation = player?.CurrentAvatar?.RegionLocation.Orientation ?? Orientation.Zero;
            Vector3 forward = new(MathF.Cos(orientation.Yaw), MathF.Sin(orientation.Yaw), 0.0f);
            forward = Vector3.SafeNormalize2D(forward, Vector3.Forward);
            Vector3 right = Vector3.Perp2D(forward);

            chestAnchor = anchorPosition;
            vendorAnchor = anchorPosition + right * RewardObjectAnchorDistance + forward * 120.0f;
            portalAnchor = anchorPosition;
        }

        private bool TryResolveVendorSpawnLocation(Region region, WorldEntityPrototype vendorPrototype, Vector3 anchorPosition, Orientation orientation, out Vector3 position, out Cell cell, out string placementMode, IReadOnlyList<Vector3> avoidPositions = null, float avoidDistance = 0.0f)
        {
            position = Vector3.Zero;
            cell = null;
            placementMode = "none";

            Vector3 forward = new(MathF.Cos(orientation.Yaw), MathF.Sin(orientation.Yaw), 0.0f);
            forward = Vector3.SafeNormalize2D(forward, Vector3.Forward);
            Vector3 right = Vector3.Perp2D(forward);

            Span<Vector3> offsets =
            [
                Vector3.Zero,
                right * 192.0f,
                -right * 192.0f,
                forward * 192.0f,
                -forward * 192.0f,
                (right + forward) * 280.0f,
                (-right + forward) * 280.0f,
                (right - forward) * 280.0f,
                (-right - forward) * 280.0f,
                right * 384.0f,
                -right * 384.0f,
                forward * 384.0f,
                -forward * 384.0f
            ];

            foreach (Vector3 offset in offsets)
            {
                Vector3 candidate = RegionLocation.ProjectToFloor(region, anchorPosition + offset);
                if (IsNearAvoidedRewardPosition(candidate, avoidPositions, avoidDistance))
                    continue;

                Bounds bounds = CreateSpawnBounds(vendorPrototype, candidate, orientation);
                PathFlags pathFlags = Region.GetPathFlagsForEntity(vendorPrototype);
                if (pathFlags == PathFlags.None)
                    pathFlags = PathFlags.Walk;

                PositionCheckFlags posFlags = PositionCheckFlags.CanBeBlockedEntity | PositionCheckFlags.PreferNoEntity;
                BlockingCheckFlags blockFlags = BlockingCheckFlags.CheckSpawns | BlockingCheckFlags.CheckGroundMovementPowers | BlockingCheckFlags.CheckLanding;
                if (region.IsLocationClear(ref bounds, pathFlags, posFlags, blockFlags))
                {
                    cell = region.GetCellAtPosition(candidate);
                    if (cell != null)
                    {
                        position = candidate;
                        placementMode = "validated-near-player";
                        return true;
                    }
                }
            }

            Vector3 fallback = RegionLocation.ProjectToFloor(region, anchorPosition);
            if (IsNearAvoidedRewardPosition(fallback, avoidPositions, avoidDistance) == false)
            {
                cell = region.GetCellAtPosition(fallback);
                if (cell != null)
                {
                    position = fallback;
                    placementMode = "fallback-anchor-position";
                    return true;
                }
            }

            Cell closestCell = FindClosestCell(region, anchorPosition);
            if (closestCell == null)
                return false;

            Vector3 closestCellCenter = RegionLocation.ProjectToFloor(region, closestCell.RegionBounds.Center);
            if (IsNearAvoidedRewardPosition(closestCellCenter, avoidPositions, avoidDistance) == false)
            {
                position = closestCellCenter;
                cell = region.GetCellAtPosition(position) ?? closestCell;
                placementMode = "fallback-nearest-cell";
                return true;
            }

            for (int i = 0; i < 16; i++)
            {
                float angle = i * (MathF.PI * 2.0f / 16.0f);
                Vector3 offset = new(MathF.Cos(angle) * RewardObjectAnchorDistance, MathF.Sin(angle) * RewardObjectAnchorDistance, 0.0f);
                Vector3 candidate = RegionLocation.ProjectToFloor(region, closestCellCenter + offset);
                if (IsNearAvoidedRewardPosition(candidate, avoidPositions, avoidDistance))
                    continue;

                Cell candidateCell = region.GetCellAtPosition(candidate);
                if (candidateCell == null)
                    continue;

                position = candidate;
                cell = candidateCell;
                placementMode = "fallback-nearest-cell-ring";
                return true;
            }

            return false;
        }

        private static bool IsNearAvoidedRewardPosition(Vector3 candidate, IReadOnlyList<Vector3> avoidPositions, float avoidDistance)
        {
            if (avoidPositions == null || avoidPositions.Count == 0 || avoidDistance <= 0.0f)
                return false;

            float avoidDistanceSq = avoidDistance * avoidDistance;
            foreach (Vector3 avoidPosition in avoidPositions)
            {
                if (Vector3.DistanceSquared2D(candidate, avoidPosition) < avoidDistanceSq)
                    return true;
            }

            return false;
        }

        private void CleanScenarioCombatants(Region region, EndlessCableScenarioRunState activeRunState = null)
        {
            if (region == null)
                return;

            using var destroyListHandle = ListPool<Agent>.Instance.Get(out List<Agent> destroyList);
            foreach (Entity entity in region.Entities)
            {
                if (entity is not Agent agent)
                    continue;

                if (activeRunState != null && activeRunState.WaveEntityIds.Contains(agent.Id))
                    continue;

                if (activeRunState != null && activeRunState.CompletionVendorEntityId == agent.Id)
                    continue;

                if (ShouldDestroyScenarioCombatant(agent) == false)
                    continue;

                destroyList.Add(agent);
            }

            foreach (Agent agent in destroyList)
                agent.Destroy();
        }

        private static void CapturePreRunAgents(EndlessCableScenarioRunState runState, Region region)
        {
            runState.PreRunAgentIds.Clear();
            if (region == null)
                return;

            foreach (Entity entity in region.Entities)
            {
                if (entity is Agent)
                    runState.PreRunAgentIds.Add(entity.Id);
            }
        }

        /// <summary>
        /// Removes hostile agents that appeared during the run but aren't tracked wave entities: summons that outlive
        /// their summoner (KillEntityOnOwnerDeath = false, e.g. shaman spirits/totems), mobs revived by shamans after
        /// they were untracked, on-death spawns, etc. Nothing counts them toward the wave, so the wave clears around them
        /// and they stay in the room, scaling with every wave's region difficulty.
        /// </summary>
        private int CleanupLeftoverRunCombatants(EndlessCableScenarioRunState runState, Region region, string reason)
        {
            if (runState == null || region == null)
                return 0;

            using var destroyListHandle = ListPool<Agent>.Instance.Get(out List<Agent> destroyList);
            foreach (Entity entity in region.Entities)
            {
                if (entity is Agent agent && IsLeftoverRunCombatant(runState, agent))
                    destroyList.Add(agent);
            }

            foreach (Agent agent in destroyList)
            {
                Logger.Info($"[EndlessCable] Removing leftover combatant {agent.PrototypeDataRef.GetName()} ({agent.Id}) on {reason}");
                agent.Destroy();
            }

            return destroyList.Count;
        }

        private static bool IsLeftoverRunCombatant(EndlessCableScenarioRunState runState, Agent agent)
        {
            if (agent == null || agent is Avatar || agent is Missile || agent.IsDestroyProtectedEntity)
                return false;

            if (agent.IsDestroyed || agent.TestStatus(EntityStatus.PendingDestroy) || agent.IsAliveInWorld == false)
                return false;

            // Things the run tracks or spawned on purpose
            if (runState.WaveEntityIds.Contains(agent.Id) || runState.PreRunAgentIds.Contains(agent.Id))
                return false;

            if (agent.Id == runState.CompletionVendorEntityId || agent.Id == runState.RewardChestEntityId || agent.Id == runState.ReturnPortalEntityId)
                return false;

            // Never touch anything that belongs to a player (team-ups, pets, controlled mobs, player summons)
            if (agent.IsVendor || agent.GetOwnerOfType<Player>() != null || agent.GetMostResponsiblePowerUser<Avatar>() != null)
                return false;

            return agent.IsHostileToPlayers();
        }

        private static bool ShouldDestroyScenarioCombatant(Agent agent)
        {
            // 1. Hard Protections
            if (agent == null || agent is Avatar || agent.IsDestroyProtectedEntity)
                return false;
            if (agent.GetOwnerOfType<Player>() != null || agent.IsVendor)
                return false;

            // 2. EXCLUDE PROJECTILES (The "No Damage" fix)
            string protoPath = agent.PrototypeDataRef.GetName();
            if (protoPath.Contains("PowerEntities", StringComparison.OrdinalIgnoreCase) ||
                protoPath.Contains("Missile", StringComparison.OrdinalIgnoreCase))
                return false;

            // 3. ONLY DESTROY DOOPS (The "Scoped Cleanup" fix)
            // Instead of "vaporizing everything", only return true if it matches a Doop
            if (protoPath.Contains("Cow", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Otherwise, protect everything else (Brood, AIM, etc. will stay alive)
            return false;
        }

        private void ApplyWaveDifficultyToRegion(EndlessCableScenarioRunState runState, Region region, int wave)
        {
            if (runState == null || region == null)
                return;

            // Resolve what the current region tier actually is
            var redTierRef = GameDatabase.GlobalsPrototype.GetDifficultyTierByEnum(DifficultyTier.Red)?.DataRef ?? PrototypeId.Invalid;
            bool isCurrentlyRed = region.DifficultyTierRef == redTierRef;

            // If we haven't captured, OR we captured the wrong (Cosmic) tier, force a refresh
            if (!runState.RegionDifficultyScalingCaptured || !isCurrentlyRed)
            {
                // If we are forcing the tier, ensure the property is set
                if (redTierRef != PrototypeId.Invalid && !isCurrentlyRed)
                {
                    region.Properties[PropertyEnum.DifficultyTier] = redTierRef;
                }

                // Reset the flag to force a re-capture of the baseline values
                runState.RegionDifficultyScalingCaptured = false;

                runState.CaptureRegionDifficultyScaling(
                    region.Properties[PropertyEnum.DamageRegionPlayerToMob],
                    region.Properties[PropertyEnum.DamageRegionMobToPlayer],
                    region.Properties[PropertyEnum.ExperienceBonusPct],
                    region.Properties[PropertyEnum.LootBonusXPPct],
                    region.Properties[PropertyEnum.LootBonusRarityPct],
                    region.Properties[PropertyEnum.LootBonusSpecialPct]);
            }

            float enemyHealthMultiplier = GetEnemyHealthWaveMultiplier(wave);
            float enemyDamageMultiplier = GetEnemyDamageWaveMultiplier(wave);
            float xpMultiplier = GetWaveXPRewardMultiplier(wave);
            float lootMultiplier = GetWaveMultiplier(wave, RewardLootBonusPerWave);

            region.Properties[PropertyEnum.DamageRegionPlayerToMob] = runState.OriginalPlayerToMobDamageMultiplier / enemyHealthMultiplier;
            region.Properties[PropertyEnum.DamageRegionMobToPlayer] = runState.OriginalMobToPlayerDamageMultiplier * enemyDamageMultiplier;
            region.Properties[PropertyEnum.ExperienceBonusPct] = CombineBonusPctWithMultiplier(runState.OriginalExperienceBonusPct, xpMultiplier);
            region.Properties[PropertyEnum.LootBonusXPPct] = CombineBonusPctWithMultiplier(runState.OriginalLootBonusXPPct, xpMultiplier);
            region.Properties[PropertyEnum.LootBonusRarityPct] = CombineBonusPctWithMultiplier(runState.OriginalLootBonusRarityPct, lootMultiplier);
            region.Properties[PropertyEnum.LootBonusSpecialPct] = CombineBonusPctWithMultiplier(runState.OriginalLootBonusSpecialPct, lootMultiplier);
        }

        private void RestoreRegionDifficulty(EndlessCableScenarioRunState runState, Region region)
        {
            if (runState == null || region == null || runState.RegionDifficultyScalingCaptured == false)
                return;

            region.Properties[PropertyEnum.DamageRegionPlayerToMob] = runState.OriginalPlayerToMobDamageMultiplier;
            region.Properties[PropertyEnum.DamageRegionMobToPlayer] = runState.OriginalMobToPlayerDamageMultiplier;
            region.Properties[PropertyEnum.ExperienceBonusPct] = runState.OriginalExperienceBonusPct;
            region.Properties[PropertyEnum.LootBonusXPPct] = runState.OriginalLootBonusXPPct;
            region.Properties[PropertyEnum.LootBonusRarityPct] = runState.OriginalLootBonusRarityPct;
            region.Properties[PropertyEnum.LootBonusSpecialPct] = runState.OriginalLootBonusSpecialPct;
        }

        private static float CombineBonusPctWithMultiplier(float currentBonusPct, float multiplier)
        {
            multiplier = Math.Max(multiplier, 0.0f);
            return ((1.0f + currentBonusPct) * multiplier) - 1.0f;
        }

        private int GetAliveTrackedEnemyCount(EndlessCableScenarioRunState runState)
        {
            int aliveCount = 0;
            using var staleListHandle = ListPool<ulong>.Instance.Get(out List<ulong> staleIds);

            foreach (ulong entityId in runState.WaveEntityIds)
            {
                WorldEntity entity = _game.EntityManager.GetEntity<WorldEntity>(entityId);
                if (entity == null || entity.IsAliveInWorld == false)
                {
                    staleIds.Add(entityId);
                    continue;
                }

                aliveCount++;
            }

            foreach (ulong entityId in staleIds)
                runState.UntrackWaveEntity(entityId);

            return aliveCount;
        }

        private void AbortRun(EndlessCableScenarioRunState runState, TimeSpan currentTime, string reason)
        {
            Region region = _game.RegionManager.GetRegion(runState.RegionId);
            CancelStartAfterInteractEvent(runState.RunId);
            CleanTrackedWaveCombatants(runState);
            CleanupLeftoverRunCombatants(runState, region, "run abort");
            ClearCableObjectiveWidgets(runState, region);
            RestoreRegionDifficulty(runState, region);
            runState.Abort(currentTime);
        }

        private void CleanTrackedWaveCombatants(EndlessCableScenarioRunState runState)
        {
            if (runState == null || runState.WaveEntityIds.Count == 0)
                return;

            using var destroyListHandle = ListPool<Agent>.Instance.Get(out List<Agent> destroyList);
            foreach (ulong entityId in runState.WaveEntityIds)
            {
                if (_game.EntityManager.GetEntity<Agent>(entityId) is Agent agent && ShouldDestroyScenarioCombatant(agent))
                    destroyList.Add(agent);
            }

            foreach (Agent agent in destroyList)
                agent.Destroy();

            runState.WaveEntityIds.Clear();
            runState.BossEntityIds.Clear();
        }

        private void CleanupFinishedRunIfReady(EndlessCableScenarioRunState runState, TimeSpan currentTime)
        {
            TimeSpan retention = runState.Status == EndlessCableScenarioRunStatus.Completed ? CompletedRunRetention : AbortedRunRetention;
            if (currentTime - runState.CompletedAt < retention)
                return;

            Player player = _game.EntityManager.GetEntityByDbGuid<Player>(runState.PlayerDbId);
            bool playerStillInRegion = player?.GetRegion()?.Id == runState.RegionId;
            bool vendorStillExists = runState.HasCompletionVendor && _game.EntityManager.GetEntity<WorldEntity>(runState.CompletionVendorEntityId) != null;

            if (runState.Status == EndlessCableScenarioRunStatus.Completed && playerStillInRegion && vendorStillExists)
                return;

            _activeRuns.Remove(runState.RunId);
            _completionRecipeAttemptsByRun.Remove(runState.RunId);
            CancelStartAfterInteractEvent(runState.RunId);
            CleanupRegionListener(runState.RegionId);
        }

        private void EnsureRegionListener(Region region)
        {
            if (region == null)
                return;

            if (_regionEntityDeadActions.ContainsKey(region.Id) == false)
            {
                Event<EntityDeadGameEvent>.Action action = (in EntityDeadGameEvent evt) => OnRegionEntityDead(region.Id, evt);
                region.EntityDeadEvent.AddActionBack(action);
                _regionEntityDeadActions[region.Id] = action;
            }

            if (_regionPlayerInteractActions.ContainsKey(region.Id) == false)
            {
                Event<PlayerInteractGameEvent>.Action action = (in PlayerInteractGameEvent evt) => OnRegionPlayerInteract(region.Id, evt);
                region.PlayerInteractEvent.AddActionBack(action);
                _regionPlayerInteractActions[region.Id] = action;
            }
        }

        private void CleanupRegionListener(ulong regionId)
        {
            if (regionId == 0)
                return;

            bool regionStillUsed = _activeRuns.Values.Any(run => run.RegionId == regionId);
            if (regionStillUsed)
                return;

            Region region = _game.RegionManager.GetRegion(regionId);

            if (_regionEntityDeadActions.TryGetValue(regionId, out Event<EntityDeadGameEvent>.Action deadAction))
            {
                region?.EntityDeadEvent.RemoveAction(deadAction);
                _regionEntityDeadActions.Remove(regionId);
            }

            if (_regionPlayerInteractActions.TryGetValue(regionId, out Event<PlayerInteractGameEvent>.Action interactAction))
            {
                region?.PlayerInteractEvent.RemoveAction(interactAction);
                _regionPlayerInteractActions.Remove(regionId);
            }
        }

        private void CancelStartAfterInteractEvent(ulong runId)
        {
            if (_startAfterInteractEvents.TryGetValue(runId, out EventPointer<StartAfterInteractEvent> startEvent) == false)
                return;

            _game.GameEventScheduler?.CancelEvent(startEvent);
            _startAfterInteractEvents.Remove(runId);
        }

        private void PrimeImmediateCombat(EndlessCableScenarioRunState runState, Agent agent)
        {
            AIController aiController = agent?.AIController;
            PropertyCollection blackboardProperties = aiController?.Blackboard?.PropertyCollection;
            if (blackboardProperties == null)
                return;

            blackboardProperties[PropertyEnum.AIAggroRangeOverrideHostile] = ImmediateAggroRangeHostile;
            blackboardProperties[PropertyEnum.AIAggroRangeOverrideAlly] = ImmediateAggroRangeAlly;
            blackboardProperties[PropertyEnum.AIAlwaysAggroed] = true;
            blackboardProperties[PropertyEnum.AIAggroState] = true;
            blackboardProperties[PropertyEnum.AIAggroTime] = (long)_game.CurrentTime.TotalMilliseconds;
            blackboardProperties[PropertyEnum.AIStartsEnabled] = true;
            agent.Properties[PropertyEnum.AIAggroRangeOverrideHostile] = ImmediateAggroRangeHostile;
            agent.Properties[PropertyEnum.AIAggroRangeOverrideAlly] = ImmediateAggroRangeAlly;
            agent.Properties[PropertyEnum.AIAlwaysAggroed] = true;
            agent.Properties[PropertyEnum.AIAggroState] = true;
            agent.Properties[PropertyEnum.AIAggroTime] = (long)_game.CurrentTime.TotalMilliseconds;
            agent.Properties[PropertyEnum.AIStartsEnabled] = true;
            blackboardProperties.RemoveProperty(PropertyEnum.AINextSensoryUpdate);
            blackboardProperties.RemoveProperty(PropertyEnum.AINextHostileSense);
            blackboardProperties.RemoveProperty(PropertyEnum.AINextAllySense);
            blackboardProperties.RemoveProperty(PropertyEnum.AINextItemSense);
            agent.Properties.RemoveProperty(PropertyEnum.AINextSensoryUpdate);
            agent.Properties.RemoveProperty(PropertyEnum.AINextHostileSense);
            agent.Properties.RemoveProperty(PropertyEnum.AINextAllySense);
            agent.Properties.RemoveProperty(PropertyEnum.AINextItemSense);

            Avatar targetAvatar = FindRunAvatar(runState, agent.Region, agent.RegionLocation.Position);
            if (targetAvatar != null)
            {
                aiController.SetTargetEntity(targetAvatar);
                blackboardProperties[PropertyEnum.AIPendingTargetId] = targetAvatar.Id;
                agent.Properties[PropertyEnum.AIPendingTargetId] = targetAvatar.Id;
                aiController.Senses.Interrupt |= BehaviorInterruptType.Alerted | BehaviorInterruptType.TargetSighted | BehaviorInterruptType.Override;
            }

            aiController.SetIsEnabled(true);
            aiController.ScheduleAIThinkEvent(ImmediateThinkDelay, useGlobalThinkVariance: false, ignoreActivePower: true);
            aiController.ScheduleAIThinkEvent(FollowupThinkDelay, useGlobalThinkVariance: false, ignoreActivePower: true);
        }

        private Avatar FindRunAvatar(EndlessCableScenarioRunState runState, Region region, Vector3 origin)
        {
            Player player = _game.EntityManager.GetEntityByDbGuid<Player>(runState.PlayerDbId);
            Avatar avatar = player?.CurrentAvatar;
            if (avatar == null || avatar.IsInWorld == false || avatar.Region != region || avatar.IsDead)
                return null;

            return avatar;
        }

        private void PlaySpawnVisual(WorldEntity entity)
        {
            if (entity == null || entity.IsInWorld == false)
                return;

            AssetId spawnVisual = ResolveSpawnVisualAsset();
            if (spawnVisual == AssetId.Invalid)
                return;

            NetMessagePlayPowerVisuals message = NetMessagePlayPowerVisuals.CreateBuilder()
                .SetEntityId(entity.Id)
                .SetPowerAssetRef((ulong)spawnVisual)
                .Build();

            _game.NetworkManager.SendMessageToInterested(message, entity, AOINetworkPolicyValues.AOIChannelProximity);
        }

        private AssetId ResolveSpawnVisualAsset()
        {
            if (_spawnVisualAsset != AssetId.Invalid)
                return _spawnVisualAsset;

            PowerVisualsGlobalsPrototype visualsProto = GameDatabase.PowerVisualsGlobalsPrototype;
            _spawnVisualAsset = visualsProto?.AvatarLeashTeleportClass ?? AssetId.Invalid;
            if (_spawnVisualAsset == AssetId.Invalid)
                _spawnVisualAsset = visualsProto?.DailyMissionCompleteClass ?? AssetId.Invalid;

            return _spawnVisualAsset;
        }

        private void EnsurePools()
        {
            if (_combatThemes != null && _bossPool != null && _mobPool != null)
                return;

            _combatThemes = [];
            _bossPool = [];
            _mobPool = [];

            using var bossSetHandle = HashSetPool<PrototypeId>.Instance.Get(out HashSet<PrototypeId> bossSet);
            using var mobSetHandle = HashSetPool<PrototypeId>.Instance.Get(out HashSet<PrototypeId> mobSet);
            Dictionary<string, EndlessCableCombatThemeBuilder> themeBuilders = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> sourceCounts = new(StringComparer.OrdinalIgnoreCase);

            CollectCombatThemeBuilders(themeBuilders, sourceCounts);

            foreach (EndlessCableCombatThemeBuilder builder in themeBuilders.Values)
            {
                EndlessCableCombatTheme theme = builder.BuildOrNull();
                if (theme == null)
                    continue;

                _combatThemes.Add(theme);

                foreach (PrototypeId bossProtoRef in theme.Bosses)
                    bossSet.Add(bossProtoRef);
                foreach (PrototypeId mobProtoRef in theme.Mobs)
                    mobSet.Add(mobProtoRef);
            }

            _combatThemes = _combatThemes
                .OrderByDescending(theme => theme.Mobs.Count + (theme.Bosses.Count * 4))
                .ThenBy(theme => theme.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _bossPool.AddRange(bossSet.OrderBy(protoRef => protoRef.GetNameFormatted()));
            _mobPool.AddRange(mobSet.OrderBy(protoRef => protoRef.GetNameFormatted()));
        }

        private sealed class EndlessCableCombatTheme
        {
            public string Category { get; init; }
            public string Name { get; init; }
            public List<PrototypeId> Bosses { get; init; } = [];
            public List<PrototypeId> Mobs { get; init; } = [];
            public int RawCandidateCount { get; init; }

            public string DisplayName => string.IsNullOrWhiteSpace(Category) ? Name : $"{Category}/{Name}";
        }

        private sealed class StartAfterInteractEvent : CallMethodEventParam1<EndlessCableScenarioManager, ulong>
        {
            private static readonly CallbackDelegate EventCallback = (manager, runId) => manager.StartRunAfterInteract(runId);

            protected override CallbackDelegate GetCallback()
            {
                return EventCallback;
            }
        }

        private sealed class EndlessCableCombatThemeBuilder
        {
            private readonly HashSet<PrototypeId> _bosses = [];
            private readonly HashSet<PrototypeId> _mobs = [];

            public string Category { get; }
            public string Name { get; }
            public int RawCandidateCount { get; private set; }

            public EndlessCableCombatThemeBuilder(string category, string name)
            {
                Category = category;
                Name = name;
            }

            public void AddCandidates(IEnumerable<PrototypeId> candidates)
            {
                if (candidates == null)
                    return;

                foreach (PrototypeId candidateRef in candidates)
                {
                    RawCandidateCount++;
                    TryAddCombatPoolEntry(candidateRef, _bosses, _mobs);
                }
            }

            public EndlessCableCombatTheme BuildOrNull()
            {
                if (_bosses.Count == 0 || _mobs.Count < MinimumThemeMobPoolCount)
                    return null;

                return new EndlessCableCombatTheme
                {
                    Category = Category,
                    Name = Name,
                    Bosses = _bosses.OrderBy(protoRef => protoRef.GetNameFormatted()).ToList(),
                    Mobs = _mobs.OrderBy(protoRef => protoRef.GetNameFormatted()).ToList(),
                    RawCandidateCount = RawCandidateCount
                };
            }
        }

        private static void CollectCombatThemeBuilders(Dictionary<string, EndlessCableCombatThemeBuilder> themeBuilders, Dictionary<string, int> sourceCounts)
        {
            foreach (PrototypeId themeRef in DataDirectory.Instance.IteratePrototypesInHierarchy<PopulationThemePrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                AddCombatThemeFromSource(themeBuilders, "PopulationTheme", themeRef, candidates => CollectPopulationThemeEntities("Theme.PopulationTheme", themeRef, candidates, sourceCounts));

            foreach (PrototypeId populationObjectRef in DataDirectory.Instance.IteratePrototypesInHierarchy<PopulationObjectPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                AddCombatThemeFromSource(themeBuilders, "PopulationObject", populationObjectRef, candidates => CollectPopulationObjectEntities("Theme.PopulationObject", populationObjectRef.As<PopulationObjectPrototype>(), candidates, sourceCounts));

            foreach (PrototypeId requiredObjectListRef in DataDirectory.Instance.IteratePrototypesInHierarchy<PopulationRequiredObjectListPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                AddCombatThemeFromSource(themeBuilders, "PopulationRequiredObjectList", requiredObjectListRef, candidates => CollectPopulationRequiredObjectEntities("Theme.PopulationRequiredObjectList", requiredObjectListRef.As<PopulationRequiredObjectListPrototype>()?.RequiredObjects, candidates, sourceCounts));

            foreach (PrototypeId spawnerRef in DataDirectory.Instance.IteratePrototypesInHierarchy<SpawnerPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                AddCombatThemeFromSource(themeBuilders, "Spawner", spawnerRef, candidates => CollectSpawnerEntities("Theme.Spawner", spawnerRef.As<SpawnerPrototype>(), candidates, sourceCounts));

            foreach (PrototypeId metaStateRef in DataDirectory.Instance.IteratePrototypesInHierarchy<MetaStatePrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                AddCombatThemeFromSource(themeBuilders, "MetaState", metaStateRef, candidates => CollectMetaStateEntities(metaStateRef.As<MetaStatePrototype>(), candidates, sourceCounts));

            foreach (PrototypeId metaModeRef in DataDirectory.Instance.IteratePrototypesInHierarchy<MetaGameModePrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                AddCombatThemeFromSource(themeBuilders, "MetaMode", metaModeRef, candidates => CollectMetaModeEntities(metaModeRef.As<MetaGameModePrototype>(), candidates, sourceCounts));

            foreach (PrototypeId missionRef in DataDirectory.Instance.IteratePrototypesInHierarchy<MissionPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                AddCombatThemeFromSource(themeBuilders, "Mission", missionRef, candidates => CollectMissionEntities(missionRef.As<MissionPrototype>(), candidates, sourceCounts));
        }

        private static void AddCombatThemeFromSource(Dictionary<string, EndlessCableCombatThemeBuilder> themeBuilders, string source, PrototypeId ownerRef, Action<HashSet<PrototypeId>> collectCandidates)
        {
            string ownerPath = GetPrototypePath(ownerRef);
            if (TryResolveCombatTheme(ownerPath, out string key, out string category, out string name) == false)
                return;

            using var candidateSetHandle = HashSetPool<PrototypeId>.Instance.Get(out HashSet<PrototypeId> candidates);
            collectCandidates?.Invoke(candidates);
            if (candidates.Count == 0)
                return;

            if (themeBuilders.TryGetValue(key, out EndlessCableCombatThemeBuilder builder) == false)
            {
                builder = new EndlessCableCombatThemeBuilder(category, name);
                themeBuilders[key] = builder;
            }

            builder.AddCandidates(candidates);
        }

        private static string GetPrototypePath(PrototypeId protoRef)
        {
            Prototype proto = protoRef.As<Prototype>();
            return proto?.DataRef.GetName() ?? string.Empty;
        }

        private static bool TryResolveCombatTheme(string prototypePath, out string key, out string category, out string name)
        {
            key = null;
            category = null;
            name = null;

            if (string.IsNullOrWhiteSpace(prototypePath))
                return false;

            string normalizedPath = prototypePath.Replace('\\', '/');
            if (IsUnsafeThemePath(normalizedPath))
                return false;

            string[] parts = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (TryResolveThemeAfterSegment(parts, "Terminals", out name) || TryResolveThemeAfterSegment(parts, "EndgameDailies", out name))
                category = "Terminal";
            else if (TryResolveThemeAfterSegment(parts, "OneShotMissions", out name) || TryResolveThemeAfterSegment(parts, "OneShots", out name))
                category = "OneShot";
            else if (TryResolveThemeAfterSegment(parts, "Operations", out name))
                category = "Operation";
            else if (TryResolveThemeAfterSegment(parts, "Patrols", out name) || TryResolveThemeAfterSegment(parts, "Patrol", out name))
                category = "Patrol";
            else if (TryResolveThemeAfterSegment(parts, "Limbo", out name) || TryResolveThemeByContainedSegment(parts, "Limbo", out name))
                category = "Limbo";
            else if (TryResolveThemeAfterSegment(parts, "Raids", out name) || TryResolveThemeAfterSegment(parts, "Raid", out name) || TryResolveThemeByContainedSegment(parts, "Raid", out name))
                category = "Raid";
            else if (TryResolveThemeAfterSegment(parts, "MetaGames", out name) || TryResolveThemeAfterSegment(parts, "MetaGame", out name))
                category = "GameMode";
            else if (TryResolveThemeByContainedSegment(parts, "Chapter", out name) || TryResolveStoryTheme(parts, out name))
                category = "Story";
            else if (TryResolveThemeAfterSegment(parts, "Special", out name))
                category = "Special";

            if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(name))
                return false;

            key = $"{category}/{name}".ToUpperInvariant();
            return true;
        }

        private static bool TryResolveThemeAfterSegment(string[] parts, string marker, out string name)
        {
            name = null;
            if (parts.HasValue() == false)
                return false;

            for (int i = 0; i < parts.Length; i++)
            {
                if (string.Equals(parts[i], marker, StringComparison.OrdinalIgnoreCase) == false)
                    continue;

                for (int j = i + 1; j < parts.Length; j++)
                {
                    if (TryNormalizeThemeName(parts[j], out name))
                        return true;
                }
            }

            return false;
        }

        private static bool TryResolveStoryTheme(string[] parts, out string name)
        {
            name = null;
            if (parts.HasValue() == false)
                return false;

            foreach (string part in parts)
            {
                if (part.Length < 4 || part.StartsWith("CH", StringComparison.OrdinalIgnoreCase) == false)
                    continue;

                if (char.IsDigit(part[2]) == false || char.IsDigit(part[3]) == false)
                    continue;

                if (TryNormalizeThemeName(part, out name))
                    return true;
            }

            return false;
        }

        private static bool TryResolveThemeByContainedSegment(string[] parts, string marker, out string name)
        {
            name = null;
            if (parts.HasValue() == false)
                return false;

            foreach (string part in parts)
            {
                if (part.Contains(marker, StringComparison.OrdinalIgnoreCase) && TryNormalizeThemeName(part, out name))
                    return true;
            }

            return false;
        }

        private static bool TryNormalizeThemeName(string segment, out string name)
        {
            name = null;
            if (IsThemeNameSegment(segment) == false)
                return false;

            name = NormalizeThemeName(segment);
            return string.IsNullOrWhiteSpace(name) == false && IsThemeNameSegment(name);
        }

        private static string NormalizeThemeName(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
                return string.Empty;

            string name = segment;
            const string PrototypeSuffix = ".prototype";
            if (name.EndsWith(PrototypeSuffix, StringComparison.OrdinalIgnoreCase))
                name = name[..^PrototypeSuffix.Length];

            if (name.StartsWith("DailyG", StringComparison.OrdinalIgnoreCase) || name.StartsWith("DailyR", StringComparison.OrdinalIgnoreCase) || name.StartsWith("DailyC", StringComparison.OrdinalIgnoreCase))
                name = name[6..];

            string[] suffixes =
            [
                "RegionL60",
                "RegionL11To60",
                "Region",
                "Mission",
                "Population",
                "PopObject",
                "MetaGame",
                "Controller"
            ];

            foreach (string suffix in suffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name[..^suffix.Length];
                    break;
                }
            }

            return name.Trim('_', '-', ' ');
        }

        private static bool IsThemeNameSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment))
                return false;

            string name = segment;
            const string PrototypeSuffix = ".prototype";
            if (name.EndsWith(PrototypeSuffix, StringComparison.OrdinalIgnoreCase))
                name = name[..^PrototypeSuffix.Length];

            if (name.Length < 3 || name.StartsWith("zz", StringComparison.OrdinalIgnoreCase))
                return false;

            return name is not "AltRegions"
                and not "Bosses"
                and not "Chapter"
                and not "Chapters"
                and not "Common"
                and not "Cosmic"
                and not "DangerRoom"
                and not "DangerRoomMode"
                and not "EndGame"
                and not "Entity"
                and not "Events"
                and not "Green"
                and not "Heroic"
                and not "MetaGames"
                and not "Mission"
                and not "Missions"
                and not "Mobs"
                and not "NonChapterBound"
                and not "NonChapterRegions"
                and not "Normal"
                and not "OneShot"
                and not "OneShotMissions"
                and not "OneShots"
                and not "Patrol"
                and not "Patrols"
                and not "Population"
                and not "Populations"
                and not "Prototype"
                and not "Prototypes"
                and not "Raid"
                and not "Raids"
                and not "Red"
                and not "Regions"
                and not "Shared"
                and not "Special"
                and not "StaticChallenges"
                and not "SuperHeroic"
                and not "Terminals"
                and not "Tier1"
                and not "Tier2"
                and not "Tier3"
                and not "UniqueScenarios";
        }

        private static bool IsUnsafeThemePath(string path)
        {
            return path.Contains("Onslaught", StringComparison.OrdinalIgnoreCase)
                || path.Contains("Surtur", StringComparison.OrdinalIgnoreCase)
                || path.Contains("zzzDeprecated", StringComparison.OrdinalIgnoreCase);
        }

        private static void CollectCombatPoolCandidates(HashSet<PrototypeId> candidates, Dictionary<string, int> sourceCounts)
        {
            foreach (PrototypeId themeRef in DataDirectory.Instance.IteratePrototypesInHierarchy<PopulationThemePrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                CollectPopulationThemeEntities("PopulationTheme", themeRef, candidates, sourceCounts);

            foreach (PrototypeId populationObjectRef in DataDirectory.Instance.IteratePrototypesInHierarchy<PopulationObjectPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                CollectPopulationObjectEntities("PopulationObject.All", populationObjectRef.As<PopulationObjectPrototype>(), candidates, sourceCounts);

            foreach (PrototypeId requiredObjectListRef in DataDirectory.Instance.IteratePrototypesInHierarchy<PopulationRequiredObjectListPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                CollectPopulationRequiredObjectEntities("PopulationRequiredObjectList", requiredObjectListRef.As<PopulationRequiredObjectListPrototype>()?.RequiredObjects, candidates, sourceCounts);

            foreach (PrototypeId spawnerRef in DataDirectory.Instance.IteratePrototypesInHierarchy<SpawnerPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                CollectSpawnerEntities("Spawner.SpawnSequence", spawnerRef.As<SpawnerPrototype>(), candidates, sourceCounts);

            foreach (PrototypeId metaStateRef in DataDirectory.Instance.IteratePrototypesInHierarchy<MetaStatePrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                CollectMetaStateEntities(metaStateRef.As<MetaStatePrototype>(), candidates, sourceCounts);

            foreach (PrototypeId metaModeRef in DataDirectory.Instance.IteratePrototypesInHierarchy<MetaGameModePrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                CollectMetaModeEntities(metaModeRef.As<MetaGameModePrototype>(), candidates, sourceCounts);

            foreach (PrototypeId missionRef in DataDirectory.Instance.IteratePrototypesInHierarchy<MissionPrototype>(PrototypeIterateFlags.NoAbstractApprovedOnly))
                CollectMissionEntities(missionRef.As<MissionPrototype>(), candidates, sourceCounts);
        }

        private static void CollectPopulationThemeEntities(string source, PrototypeId themeRef, HashSet<PrototypeId> candidates, Dictionary<string, int> sourceCounts)
        {
            if (themeRef == PrototypeId.Invalid)
                return;

            Prototype themeProto = themeRef.As<Prototype>();
            if (themeProto is PopulationThemeSetPrototype themeSetProto)
            {
                if (themeSetProto.Themes.HasValue())
                    foreach (PrototypeId childThemeRef in themeSetProto.Themes)
                        CollectPopulationThemeEntities(source + ".ThemeSet", childThemeRef, candidates, sourceCounts);
                return;
            }

            if (themeProto is not PopulationThemePrototype populationThemeProto)
                return;

            CollectPopulationObjectListEntities(source + ".Enemies", populationThemeProto.Enemies, candidates, sourceCounts);
            CollectPopulationObjectListEntities(source + ".Encounters", populationThemeProto.Encounters, candidates, sourceCounts);
        }

        private static void CollectPopulationObjectListEntities(string source, PopulationObjectListPrototype populationListProto, HashSet<PrototypeId> candidates, Dictionary<string, int> sourceCounts)
        {
            if (populationListProto?.List.HasValue() != true)
                return;

            foreach (PopulationObjectInstancePrototype objectInstanceProto in populationListProto.List)
            {
                if (objectInstanceProto == null || objectInstanceProto.Object == PrototypeId.Invalid)
                    continue;

                Prototype objectProto = objectInstanceProto.Object.As<Prototype>();
                if (objectProto is PopulationObjectListPrototype childListProto)
                {
                    CollectPopulationObjectListEntities(source, childListProto, candidates, sourceCounts);
                }
                else if (objectProto is PopulationObjectPrototype populationObjectProto)
                {
                    CollectPopulationObjectEntities(source, populationObjectProto, candidates, sourceCounts);
                }
                else if (objectProto is PopulationThemePrototype or PopulationThemeSetPrototype)
                {
                    CollectPopulationThemeEntities(source, objectInstanceProto.Object, candidates, sourceCounts);
                }
            }
        }

        private static void CollectPopulationObjectEntities(string source, PopulationObjectPrototype populationObjectProto, HashSet<PrototypeId> candidates, Dictionary<string, int> sourceCounts)
        {
            if (populationObjectProto == null)
                return;

            using var entitiesHandle = HashSetPool<PrototypeId>.Instance.Get(out HashSet<PrototypeId> entities);
            populationObjectProto.GetContainedEntities(entities, unwrapEntitySelectors: true);
            AddCombatPoolCandidates(source, entities, candidates, sourceCounts);

            if (populationObjectProto is PopulationFormationPrototype formationProto)
                CollectPopulationRequiredObjectEntities(source + ".Formation", formationProto.Objects, candidates, sourceCounts);
        }

        private static void CollectPopulationRequiredObjectEntities(string source, PopulationRequiredObjectPrototype[] requiredObjects, HashSet<PrototypeId> candidates, Dictionary<string, int> sourceCounts)
        {
            if (requiredObjects.HasValue() == false)
                return;

            foreach (PopulationRequiredObjectPrototype requiredObjectProto in requiredObjects)
            {
                if (requiredObjectProto == null)
                    continue;

                CollectPopulationObjectEntities(source, requiredObjectProto.GetPopObject(), candidates, sourceCounts);
            }
        }

        private static void CollectSpawnerEntities(string source, SpawnerPrototype spawnerProto, HashSet<PrototypeId> candidates, Dictionary<string, int> sourceCounts)
        {
            if (spawnerProto?.SpawnSequence.HasValue() != true)
                return;

            foreach (SpawnerSequenceEntryPrototype sequenceEntryProto in spawnerProto.SpawnSequence)
                CollectPopulationObjectEntities(source, sequenceEntryProto?.GetPopObject(), candidates, sourceCounts);
        }

        private static void CollectMetaStateEntities(MetaStatePrototype metaStateProto, HashSet<PrototypeId> candidates, Dictionary<string, int> sourceCounts)
        {
            switch (metaStateProto)
            {
                case MetaStatePopulationMaintainPrototype maintainProto:
                    CollectPopulationRequiredObjectEntities("MetaState.PopulationMaintain", maintainProto.PopulationObjects, candidates, sourceCounts);
                    break;
                case MetaStateMissionActivatePrototype activateProto:
                    CollectPopulationRequiredObjectEntities("MetaState.MissionActivate", activateProto.PopulationObjects, candidates, sourceCounts);
                    break;
                case MetaStateMissionSequencerPrototype sequencerProto when sequencerProto.Sequence.HasValue():
                    foreach (MetaMissionEntryPrototype entryProto in sequencerProto.Sequence)
                        CollectPopulationRequiredObjectEntities("MetaState.MissionSequencer", entryProto?.PopulationObjects, candidates, sourceCounts);
                    break;
            }
        }

        private static void CollectMetaModeEntities(MetaGameModePrototype metaModeProto, HashSet<PrototypeId> candidates, Dictionary<string, int> sourceCounts)
        {
            switch (metaModeProto)
            {
                case PvEScaleGameModePrototype scaleProto:
                    if (scaleProto.BossPopulationObjects.HasValue())
                        foreach (PrototypeId bossPopulationObjectRef in scaleProto.BossPopulationObjects)
                            CollectPopulationObjectEntities("MetaMode.PvEScale.BossPopulationObjects", bossPopulationObjectRef.As<PopulationObjectPrototype>(), candidates, sourceCounts);

                    CollectPopulationObjectEntities("MetaMode.PvEScale.WavePopulation", scaleProto.WavePopulation.As<PopulationObjectPrototype>(), candidates, sourceCounts);
                    CollectPopulationThemeEntities("MetaMode.PvEScale.PopulationOverrideTheme", scaleProto.PopulationOverrideTheme, candidates, sourceCounts);
                    break;
                case PvEWaveGameModePrototype waveProto:
                    CollectSpawnerEntities("MetaMode.PvEWave.BossSpawner", waveProto.BossSpawner.As<SpawnerPrototype>(), candidates, sourceCounts);
                    CollectPopulationObjectEntities("MetaMode.PvEWave.BossPopulationObject", waveProto.BossPopulationObject.As<PopulationObjectPrototype>(), candidates, sourceCounts);
                    CollectPopulationThemeEntities("MetaMode.PvEWave.PopulationOverrideTheme", waveProto.PopulationOverrideTheme, candidates, sourceCounts);
                    break;
            }
        }

        private static void CollectMissionEntities(MissionPrototype missionProto, HashSet<PrototypeId> candidates, Dictionary<string, int> sourceCounts)
        {
            if (missionProto == null)
                return;

            if (missionProto.PopulationSpawns.HasValue())
                foreach (MissionPopulationEntryPrototype populationEntryProto in missionProto.PopulationSpawns)
                    CollectPopulationObjectEntities("Mission.PopulationSpawns", populationEntryProto?.Population, candidates, sourceCounts);

            CollectMissionActionEntities("Mission.Actions", missionProto.OnAvailableActions, candidates, sourceCounts);
            CollectMissionActionEntities("Mission.Actions", missionProto.OnStartActions, candidates, sourceCounts);
            CollectMissionActionEntities("Mission.Actions", missionProto.OnFailActions, candidates, sourceCounts);
            CollectMissionActionEntities("Mission.Actions", missionProto.OnSuccessActions, candidates, sourceCounts);

            if (missionProto.Objectives.HasValue())
            {
                foreach (MissionObjectivePrototype objectiveProto in missionProto.Objectives)
                {
                    if (objectiveProto == null)
                        continue;

                    CollectMissionActionEntities("MissionObjective.Actions", objectiveProto.OnAvailableActions, candidates, sourceCounts);
                    CollectMissionActionEntities("MissionObjective.Actions", objectiveProto.OnStartActions, candidates, sourceCounts);
                    CollectMissionActionEntities("MissionObjective.Actions", objectiveProto.OnFailActions, candidates, sourceCounts);
                    CollectMissionActionEntities("MissionObjective.Actions", objectiveProto.OnSuccessActions, candidates, sourceCounts);
                }
            }
        }

        private static void CollectMissionActionEntities(string source, MissionActionPrototype[] actions, HashSet<PrototypeId> candidates, Dictionary<string, int> sourceCounts)
        {
            if (actions.HasValue() == false)
                return;

            foreach (MissionActionPrototype actionProto in actions)
            {
                switch (actionProto)
                {
                    case MissionActionEntityCreatePrototype createProto:
                        AddCombatPoolCandidate(source + ".EntityCreate", createProto.EntityPrototype, candidates, sourceCounts);
                        break;
                    case MissionActionEncounterSpawnPrototype encounterProto:
                        CollectEncounterResourceEntities(source + ".EncounterSpawn", encounterProto.GetEncounterRef(), candidates, sourceCounts);
                        break;
                    case MissionActionTimedActionPrototype timedProto:
                        CollectMissionActionEntities(source + ".Timed", timedProto.ActionsToPerform, candidates, sourceCounts);
                        break;
                    case MissionActionInventoryRemoveItemPrototype removeProto:
                        CollectMissionActionEntities(source + ".InventoryRemove", removeProto.OnRemoveActions, candidates, sourceCounts);
                        break;
                }
            }
        }

        private static void CollectEncounterResourceEntities(string source, PrototypeId encounterRef, HashSet<PrototypeId> candidates, Dictionary<string, int> sourceCounts)
        {
            EncounterResourcePrototype encounterProto = encounterRef.As<EncounterResourcePrototype>();
            if (encounterProto?.MarkerSet == null)
                return;

            using var entitiesHandle = HashSetPool<PrototypeId>.Instance.Get(out HashSet<PrototypeId> entities);
            encounterProto.MarkerSet.GetContainedEntities(entities);
            AddCombatPoolCandidates(source, entities, candidates, sourceCounts);
        }

        private static void AddCombatPoolCandidates(string source, HashSet<PrototypeId> sourceEntities, HashSet<PrototypeId> candidates, Dictionary<string, int> sourceCounts)
        {
            if (sourceEntities == null)
                return;

            foreach (PrototypeId protoRef in sourceEntities)
                AddCombatPoolCandidate(source, protoRef, candidates, sourceCounts);
        }

        private static void AddCombatPoolCandidate(string source, PrototypeId protoRef, HashSet<PrototypeId> candidates, Dictionary<string, int> sourceCounts)
        {
            if (protoRef == PrototypeId.Invalid || protoRef.As<AgentPrototype>() == null)
                return;

            candidates.Add(protoRef);
            if (sourceCounts != null)
                sourceCounts[source] = sourceCounts.TryGetValue(source, out int count) ? count + 1 : 1;
        }

        private static bool TryAddCombatPoolEntry(PrototypeId protoRef, HashSet<PrototypeId> bossSet, HashSet<PrototypeId> mobSet)
        {
            AgentPrototype agentProto = protoRef.As<AgentPrototype>();
            if (IsValidCombatAgentPrototype(agentProto) == false)
                return false;

            Rank rank = agentProto.RankPrototype?.Rank ?? Rank.Max;
            if (rank is Rank.Boss or Rank.GroupBoss)
            {
                if (IsExcludedBoss(agentProto))
                    return false;

                bossSet.Add(protoRef);
                return true;
            }

            if (rank is Rank.Popcorn or Rank.Champion or Rank.Elite)
            {
                if (IsExcludedMob(agentProto))
                    return false;

                mobSet.Add(protoRef);
                return true;
            }

            return false;
        }

        private static bool IsValidCombatAgentPrototype(AgentPrototype agentProto)
        {
            if (agentProto == null)
                return false;

            if (agentProto is AvatarPrototype or AgentTeamUpPrototype or MissilePrototype or OrbPrototype or SmartPropPrototype)
                return false;

            if (agentProto.ApprovedForUse() == false)
                return false;

            if (agentProto.BehaviorProfile == null)
                return false;

            if (agentProto.RankPrototype == null)
                return false;

            if (AlliancePrototype.IsHostileToPlayerAlliance(agentProto.AlliancePrototype) == false)
                return false;

            if (agentProto.Bounds == null || agentProto.UnrealClass == AssetId.Invalid)
                return false;

            if (agentProto.Locomotion?.Immobile == true)
                return false;

            if (IsDormantWakeBehaviorExcludedFromEndlessCableCombatPool(agentProto))
                return false;

            if (Region.GetPathFlagsForEntity(agentProto) == PathFlags.None)
                return false;

            string formattedName = agentProto.DataRef.GetNameFormatted();
            string prototypePath = agentProto.DataRef.GetName();
            if (IsPrototypePathExcludedFromEndlessCableCombatPool(prototypePath))
                return false;

            return IsExcludedCombatAgentName(formattedName) == false;
        }

        private static bool IsPrototypePathExcludedFromEndlessCableCombatPool(string prototypePath)
        {
            if (IsAllowedCombatAgentPath(prototypePath) == false)
                return true;

            return IsExcludedCombatAgentName(prototypePath);
        }

        private static bool IsDormantWakeBehaviorExcludedFromEndlessCableCombatPool(float wakeRange, int wakeDelayMS, float returnToDormantRange)
        {
            return wakeRange > 0.0f
                || wakeDelayMS > 0
                || returnToDormantRange > 0.0f;
        }

        private static bool IsDormantWakeBehaviorExcludedFromEndlessCableCombatPool(AgentPrototype agentProto)
        {
            if (agentProto == null)
                return true;

            return IsDormantWakeBehaviorExcludedFromEndlessCableCombatPool(
                agentProto.WakeRange,
                agentProto.WakeDelayMS,
                agentProto.ReturnToDormantRange);
        }

        private static string FormatSourceSummary(Dictionary<string, int> sourceCounts)
        {
            if (sourceCounts == null || sourceCounts.Count == 0)
                return "none";

            return string.Join(", ", sourceCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(8)
                .Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        private static string FormatPrototypeSample(IReadOnlyList<PrototypeId> prototypes)
        {
            if (prototypes == null || prototypes.Count == 0)
                return "none";

            return string.Join(", ", prototypes.Take(5).Select(protoRef => protoRef.GetNameFormatted() ?? $"0x{(ulong)protoRef:X}"));
        }

        private static string FormatThemeSample(IReadOnlyList<EndlessCableCombatTheme> themes)
        {
            if (themes == null || themes.Count == 0)
                return "none";

            return string.Join(", ", themes
                .Take(8)
                .Select(theme => $"{theme.DisplayName}(b={theme.Bosses.Count},m={theme.Mobs.Count})"));
        }

        private static string FormatThemeCategorySummary(IReadOnlyList<EndlessCableCombatTheme> themes)
        {
            if (themes == null || themes.Count == 0)
                return "none";

            return string.Join(", ", themes
                .GroupBy(theme => string.IsNullOrWhiteSpace(theme.Category) ? "Unknown" : theme.Category, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Key}={group.Count()}"));
        }

        private static bool IsExcludedBoss(AgentPrototype agentProto)
        {
            string formattedName = agentProto?.DataRef.GetNameFormatted() ?? string.Empty;
            string prototypePath = agentProto?.DataRef.GetName() ?? string.Empty;
            if (formattedName.Contains("Surtur", StringComparison.OrdinalIgnoreCase)
                || formattedName.Contains("Onslaught", StringComparison.OrdinalIgnoreCase)
                || formattedName.Contains("Supergiant", StringComparison.OrdinalIgnoreCase)
                || prototypePath.Contains("Surtur", StringComparison.OrdinalIgnoreCase)
                || prototypePath.Contains("Onslaught", StringComparison.OrdinalIgnoreCase)
                || prototypePath.Contains("Supergiant", StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (string fragment in ExcludedPromotedBossNameFragments)
            {
                if (formattedName.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                    || prototypePath.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsExcludedMob(AgentPrototype agentProto)
        {
            string formattedName = agentProto?.DataRef.GetNameFormatted() ?? string.Empty;
            string prototypePath = agentProto?.DataRef.GetName() ?? string.Empty;

            foreach (string fragment in ExcludedPromotedBossNameFragments)
            {
                if (formattedName.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                    || prototypePath.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            Rank rank = agentProto?.RankPrototype?.Rank ?? Rank.Max;
            return rank == Rank.MiniBoss;
        }

        private static bool IsAllowedCombatAgentPath(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            name = name.Replace('\\', '/');
            foreach (string prefix in AllowedCombatAgentPathPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsExcludedCombatAgentName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;

            name = name.Replace('\\', '/');
            string leafName = name;
            int lastSlashIndex = leafName.LastIndexOf('/');
            if (lastSlashIndex >= 0)
                leafName = leafName[(lastSlashIndex + 1)..];

            const string PrototypeSuffix = ".prototype";
            if (leafName.EndsWith(PrototypeSuffix, StringComparison.OrdinalIgnoreCase))
                leafName = leafName[..^PrototypeSuffix.Length];

            if (leafName.Contains("Spawn", StringComparison.OrdinalIgnoreCase)
                || leafName.EndsWith("Spawner", StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (string fragment in ExcludedCombatAgentNameFragments)
            {
                if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private PrototypeId PickRandomPrototype(IReadOnlyList<PrototypeId> prototypes)
        {
            if (prototypes == null || prototypes.Count == 0)
                return PrototypeId.Invalid;

            return prototypes[_game.Random.Next(0, prototypes.Count)];
        }

        private EndlessCableCombatTheme PickRandomTheme()
        {
            if (_combatThemes == null || _combatThemes.Count == 0)
                return null;

            return _combatThemes[_game.Random.Next(0, _combatThemes.Count)];
        }

        private static Bounds CreateSpawnBounds(WorldEntityPrototype entityPrototype, Vector3 position, Orientation orientation)
        {
            Bounds bounds = entityPrototype?.Bounds != null
                ? new Bounds(entityPrototype.Bounds, position)
                : new Bounds();

            if (bounds.Geometry == GeometryType.None || bounds.Radius < 16.0f)
                bounds.InitializeCapsule(24.0f, 48.0f, BoundsCollisionType.Blocking, BoundsFlags.None);

            bounds.Center = position;
            bounds.Orientation = orientation;
            bounds.CollisionType = BoundsCollisionType.Blocking;
            return bounds;
        }

        private static Cell FindClosestCell(Region region, Vector3 anchor)
        {
            Cell closestCell = null;
            float closestDistanceSq = float.MaxValue;

            foreach (Cell candidateCell in region.Cells)
            {
                if (candidateCell == null)
                    continue;

                float distanceSq = candidateCell.RegionBounds.DistanceToPointSq2D(anchor);
                if (distanceSq >= closestDistanceSq)
                    continue;

                closestDistanceSq = distanceSq;
                closestCell = candidateCell;
            }

            return closestCell;
        }

        private static void ApplyGuaranteedBoosts(Agent agent, AgentPrototype agentProto)
        {
            if (agent == null || agentProto?.ModifiersGuaranteed == null)
                return;

            foreach (PrototypeId boost in agentProto.ModifiersGuaranteed)
                agent.Properties[PropertyEnum.EnemyBoost, boost] = true;
        }

        private static PrototypeId ResolveHostileAllianceOverride(AgentPrototype agentProto)
        {
            if (agentProto != null && AlliancePrototype.IsHostileToPlayerAlliance(agentProto.AlliancePrototype) && agentProto.Alliance != PrototypeId.Invalid)
                return agentProto.Alliance;

            return GameDatabase.GlobalsPrototype.AnyHostileAlliancePrototype;
        }

        private static void ForceWaveAgentCombatState(Agent agent, AgentPrototype agentProto)
        {
            if (agent == null)
                return;

            agent.Properties[PropertyEnum.AllianceOverride] = ResolveHostileAllianceOverride(agentProto);
            agent.Properties.RemoveProperty(PropertyEnum.MissionPrototype);
            agent.Properties[PropertyEnum.IgnoreMissionOwnerForTargeting] = true;
            agent.Properties[PropertyEnum.Visible] = true;
            agent.Properties[PropertyEnum.Untargetable] = false;
            agent.Properties[PropertyEnum.Unaffectable] = false;
            agent.Properties[PropertyEnum.Invulnerable] = false;
            agent.Properties[PropertyEnum.Dormant] = false;

            agent.SetDormant(false);
            agent.Properties[PropertyEnum.Dormant] = false;
            agent.SetSimulated(true);
            agent.ActivateAI();
        }

        private static bool IsCableScenarioItem(PrototypeId itemProtoRef)
        {
            if (EndlessCableScenarioItemPresentation.IsEndlessCableLauncherItem(itemProtoRef))
                return true;

            string name = itemProtoRef.GetNameFormatted();
            return string.Equals(name, CableScenarioItemPrototypeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, EndlessCableScenarioItemPresentation.OmegaCableScenarioPresentationPrototypeName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolveCableScenarioTarget(ItemPrototype itemProto, out PrototypeId regionProtoRef, out PrototypeId startTargetProtoRef)
        {
            regionProtoRef = PrototypeId.Invalid;
            startTargetProtoRef = PrototypeId.Invalid;

            if (itemProto?.ActionsTriggeredOnItemEvent?.Choices == null)
                return false;

            foreach (ItemActionBasePrototype itemActionBaseProto in itemProto.ActionsTriggeredOnItemEvent.Choices)
            {
                if (itemActionBaseProto is not ItemActionUsePowerPrototype usePowerProto || usePowerProto.Power == PrototypeId.Invalid)
                    continue;

                SummonPowerPrototype summonPowerProto = usePowerProto.Power.As<SummonPowerPrototype>();
                if (summonPowerProto?.SummonEntityContexts == null)
                    continue;

                foreach (SummonEntityContextPrototype summonContextProto in summonPowerProto.SummonEntityContexts)
                {
                    TransitionPrototype transitionProto = summonContextProto?.SummonEntity.As<TransitionPrototype>();
                    if (transitionProto == null || transitionProto.DirectTarget == PrototypeId.Invalid)
                        continue;

                    RegionConnectionTargetPrototype connectionTargetProto = transitionProto.DirectTarget.As<RegionConnectionTargetPrototype>();
                    if (connectionTargetProto == null || connectionTargetProto.Region == PrototypeId.Invalid)
                        continue;

                    regionProtoRef = connectionTargetProto.Region;
                    startTargetProtoRef = transitionProto.DirectTarget;
                    return true;
                }
            }

            regionProtoRef = itemProto.GetPortalTarget();
            return regionProtoRef != PrototypeId.Invalid;
        }

        private static void ConsumeScenarioItemStack(Item item)
        {
            if (item == null || item.CurrentStackSize <= 0)
                return;

            if (item.CurrentStackSize == 1)
            {
                item.Destroy();
                return;
            }

            if (item.DecrementStack() == false)
            {
            }
        }
        private void SpawnBonusItem(EndlessCableScenarioRunState runState, PrototypeId rewardRef, Player player, Region region, Vector3 anchorPosition, int rollIndex)
        {
            if (player?.CurrentAvatar == null || region == null)
                return;

            EnsureCompletionRewardPrototypes();
            if (rewardRef == PrototypeId.Invalid)
            {
                return;
            }

            Vector3 dropPosition = GetCompletionLootDropPosition(region, anchorPosition, rollIndex);

            using LootInputSettings inputSettings = ObjectPoolManager.Instance.Get<LootInputSettings>();
            inputSettings.Initialize(LootContext.Drop, player, null, player.CurrentAvatar.CharacterLevel, dropPosition);
            inputSettings.EventType = LootDropEventType.OnKilled;
            inputSettings.LootRollSettings.DropChanceModifiers |= LootDropChanceModifiers.IgnoreCooldown;

            using LootResultSummary lootResultSummary = ObjectPoolManager.Instance.Get<LootResultSummary>();

            ItemSpec itemSpec = _game.LootManager.CreateItemSpec(rewardRef, LootContext.Drop, player);
            if (itemSpec != null)
            {
                lootResultSummary.Add(new LootResult(itemSpec));
                _game.LootManager.SpawnLootFromSummary(lootResultSummary, inputSettings, RewardRecipientId);
            }
        }
        private void SpawnWave25PetBox(EndlessCableScenarioRunState runState, Player player, Region region, Vector3 anchorPosition, int rollIndex)
        {
            if (player?.CurrentAvatar == null || region == null)
                return;

            EnsureCompletionRewardPrototypes();
            if (_petBoxRewardRef == PrototypeId.Invalid)
            {

                return;
            }

            Vector3 dropPosition = GetCompletionLootDropPosition(region, anchorPosition, rollIndex);

            using LootInputSettings inputSettings = ObjectPoolManager.Instance.Get<LootInputSettings>();
            inputSettings.Initialize(LootContext.Drop, player, null, player.CurrentAvatar.CharacterLevel, dropPosition);
            inputSettings.EventType = LootDropEventType.OnKilled;
            inputSettings.LootRollSettings.DropChanceModifiers |= LootDropChanceModifiers.IgnoreCooldown;

            using LootResultSummary lootResultSummary = ObjectPoolManager.Instance.Get<LootResultSummary>();

            ItemSpec petBoxSpec = _game.LootManager.CreateItemSpec(_petBoxRewardRef, LootContext.Drop, player);
            if (petBoxSpec != null)
            {
                lootResultSummary.Add(new LootResult(petBoxSpec));
                _game.LootManager.SpawnLootFromSummary(lootResultSummary, inputSettings, RewardRecipientId);
            }
        }
        private void SpawnMultipleBonusItems(EndlessCableScenarioRunState runState, PrototypeId rewardRef, int count, Player player, Region region, Vector3 anchorPosition, int rollIndex)
        {
            if (player?.CurrentAvatar == null || region == null || rewardRef == PrototypeId.Invalid || count <= 0)
                return;

            EnsureCompletionRewardPrototypes();

            Vector3 dropPosition = GetCompletionLootDropPosition(region, anchorPosition, rollIndex);

            using LootInputSettings inputSettings = ObjectPoolManager.Instance.Get<LootInputSettings>();
            inputSettings.Initialize(LootContext.Drop, player, null, player.CurrentAvatar.CharacterLevel, dropPosition);
            inputSettings.EventType = LootDropEventType.OnKilled;
            inputSettings.LootRollSettings.DropChanceModifiers |= LootDropChanceModifiers.IgnoreCooldown;

            using LootResultSummary lootResultSummary = ObjectPoolManager.Instance.Get<LootResultSummary>();

            bool hasLoot = false;
            for (int i = 0; i < count; i++)
            {
                ItemSpec itemSpec = _game.LootManager.CreateItemSpec(rewardRef, LootContext.Drop, player);
                if (itemSpec != null)
                {
                    lootResultSummary.Add(new LootResult(itemSpec));
                    hasLoot = true;
                }
            }

            if (hasLoot)
            {
                _game.LootManager.SpawnLootFromSummary(lootResultSummary, inputSettings, RewardRecipientId);
            }
        }
        private enum EndlessCableScenarioRunStatus
        {
            PendingBind,
            AwaitingStart,
            Active,
            WaitingForNextWave,
            Completed,
            Aborted
        }

        private static class EndlessCableScenarioItemPresentation
        {
            public const string NormalCableScenarioItemPrototypeName = "Entity/Items/Consumables/Prototypes/DangerRoom/StaticChallenges/PortalToDRStaticScenarioCableFight.prototype";

            public const string OmegaCableScenarioPresentationPrototypeName = "Entity/Items/Consumables/Prototypes/DangerRoom/DangerRoomScenarioCrateUniqueCableFight.prototype";

            public static readonly PrototypeId NormalCableScenarioItemPrototypeRef = (PrototypeId)12129562728471014409UL;
            public static readonly PrototypeId OmegaCableScenarioPresentationPrototypeRef = (PrototypeId)17067585073904428862UL;
            public static readonly PrototypeId NormalCableScenarioGameplayRarityRef = (PrototypeId)9254498193264414304UL; // R4Epic, the native Cable scenario gameplay rarity

            public static readonly LocaleStringId OmegaEndlessScenarioDisplayNameLocaleStringId = (LocaleStringId)18000000000000080010UL;
            public static readonly LocaleStringId OmegaCableScenarioPresentationDisplayNameLocaleStringId = (LocaleStringId)648426245676139781UL;
            public static readonly LocaleStringId OmegaCableScenarioPresentationDescriptionLocaleStringId = (LocaleStringId)8927776814607303943UL;
            public static readonly PrototypeId OmegaEndlessScenarioVisualRarityRef = (PrototypeId)6033964048325414744UL; // R6Unique / orange item frame

            public static bool IsNormalCableScenarioItem(PrototypeId prototypeRef)
            {
                return prototypeRef == NormalCableScenarioItemPrototypeRef;
            }

            public static bool IsPresentationLauncherItem(PrototypeId prototypeRef)
            {
                return prototypeRef == OmegaCableScenarioPresentationPrototypeRef;
            }

            public static bool IsEndlessCableLauncherItem(PrototypeId prototypeRef)
            {
                return IsNormalCableScenarioItem(prototypeRef) || IsPresentationLauncherItem(prototypeRef);
            }

            public static bool IsCableScenarioItemOrCosmicChild(PrototypeId prototypeRef)
            {
                return prototypeRef == NormalCableScenarioItemPrototypeRef;
            }

            public static ItemSpec ApplyItemSpecPresentation(ItemSpec itemSpec)
            {
                if (itemSpec == null || IsNormalCableScenarioItem(itemSpec.ItemProtoRef) == false)
                    return itemSpec;

                ItemSpec presentedSpec = new(
                    OmegaCableScenarioPresentationPrototypeRef,
                    OmegaEndlessScenarioVisualRarityRef,
                    itemSpec.ItemLevel,
                    itemSpec.CreditsAmount,
                    itemSpec.AffixSpecs.Select(affixSpec => new AffixSpec(affixSpec)),
                    itemSpec.Seed,
                    itemSpec.EquippableBy);

                presentedSpec.StackCount = itemSpec.StackCount;
                return presentedSpec;
            }

            public static PrototypeId GetScenarioRarityForRegion(Item item)
            {
                if (item != null && IsPresentationLauncherItem(item.PrototypeDataRef))
                    return NormalCableScenarioGameplayRarityRef;

                if (item != null && IsNormalCableScenarioItem(item.PrototypeDataRef) && item.ItemSpec?.IsValid == true)
                    return item.ItemSpec.RarityProtoRef;

                return item?.Properties[PropertyEnum.ItemRarity] ?? PrototypeId.Invalid;
            }
        }

        private sealed class EndlessCableScenarioRunState
        {
            public const ulong RunIdPrefix = 0xECA0000000000000UL;

            public ulong RunId { get; init; }
            public ulong PlayerDbId { get; init; }
            public ulong SourceItemEntityId { get; init; }
            public PrototypeId ScenarioItemProtoRef { get; init; }
            public PrototypeId TargetRegionProtoRef { get; init; }
            public PrototypeId TargetStartTargetProtoRef { get; init; }
            public TimeSpan CreatedAt { get; init; }

            public EndlessCableScenarioRunStatus Status { get; private set; } = EndlessCableScenarioRunStatus.PendingBind;
            public ulong RegionId { get; set; }
            public int CurrentWave { get; private set; } = 1;
            public int CompletedWaveCount { get; private set; }
            public TimeSpan LastActivityAt { get; private set; }
            public TimeSpan NextWaveAt { get; private set; }
            public TimeSpan NextObjectiveWidgetRefreshAt { get; private set; }
            public TimeSpan CompletedAt { get; private set; }
            public ulong RewardChestEntityId { get; private set; }
            public ulong CompletionVendorEntityId { get; private set; }
            public ulong ReturnPortalEntityId { get; private set; }
            public bool RewardChestOpened { get; private set; }
            public ulong StartInteractEntityId { get; private set; }
            public bool StartRequested { get; private set; }
            public int CurrentWaveTargetCount { get; private set; }

            public HashSet<ulong> WaveEntityIds { get; } = [];
            public HashSet<ulong> BossEntityIds { get; } = [];

            // Agents that were already in the region before wave 1 spawned (natives, vendors, etc.).
            // Anything hostile that shows up after that and isn't a tracked wave entity belongs to the run.
            public HashSet<ulong> PreRunAgentIds { get; } = [];

            public bool RegionDifficultyScalingCaptured { get; set; }
            public float OriginalPlayerToMobDamageMultiplier { get; set; }
            public float OriginalMobToPlayerDamageMultiplier { get; set; }
            public float OriginalExperienceBonusPct { get; set; }
            public float OriginalLootBonusXPPct { get; set; }
            public float OriginalLootBonusRarityPct { get; set; }
            public float OriginalLootBonusSpecialPct { get; set; }

            public bool IsInProgress => Status is EndlessCableScenarioRunStatus.PendingBind or EndlessCableScenarioRunStatus.AwaitingStart or EndlessCableScenarioRunStatus.Active or EndlessCableScenarioRunStatus.WaitingForNextWave;
            public bool HasRewardChest => RewardChestEntityId != 0;
            public bool HasCompletionVendor => CompletionVendorEntityId != 0;
            public bool HasReturnPortal => ReturnPortalEntityId != 0;

            public void BindRegion(ulong regionId, TimeSpan currentTime)
            {
                RegionId = regionId;
                Status = EndlessCableScenarioRunStatus.AwaitingStart;
                LastActivityAt = currentTime;
            }

            public void MarkStartRequested(ulong interactEntityId, TimeSpan currentTime)
            {
                StartInteractEntityId = interactEntityId;
                StartRequested = true;
                LastActivityAt = currentTime;
            }

            public void StartWave(int wave, TimeSpan currentTime)
            {
                CurrentWave = Math.Max(wave, 1);
                Status = EndlessCableScenarioRunStatus.Active;
                NextWaveAt = TimeSpan.Zero;
                LastActivityAt = currentTime;
                WaveEntityIds.Clear();
                BossEntityIds.Clear();
                CurrentWaveTargetCount = 0;
                NextObjectiveWidgetRefreshAt = TimeSpan.Zero;
            }

            public void TrackWaveEntity(ulong entityId, bool boss)
            {
                if (entityId == 0)
                    return;

                if (WaveEntityIds.Add(entityId))
                    CurrentWaveTargetCount++;

                if (boss)
                    BossEntityIds.Add(entityId);
            }

            public void UntrackWaveEntity(ulong entityId)
            {
                WaveEntityIds.Remove(entityId);
                BossEntityIds.Remove(entityId);
            }

            public void ScheduleNextObjectiveWidgetRefresh(TimeSpan nextObjectiveWidgetRefreshAt)
            {
                NextObjectiveWidgetRefreshAt = nextObjectiveWidgetRefreshAt;
            }

            public void ClearObjectiveWidgetRefreshSchedule()
            {
                NextObjectiveWidgetRefreshAt = TimeSpan.Zero;
            }

            public void MarkWaveClear(TimeSpan nextWaveAt, TimeSpan currentTime)
            {
                CompletedWaveCount = Math.Max(CompletedWaveCount, CurrentWave);
                Status = EndlessCableScenarioRunStatus.WaitingForNextWave;
                NextWaveAt = nextWaveAt;
                LastActivityAt = currentTime;
                WaveEntityIds.Clear();
                BossEntityIds.Clear();
            }

            public void ShortenWaitForNextWave(TimeSpan nextWaveAt, TimeSpan currentTime)
            {
                if (Status != EndlessCableScenarioRunStatus.WaitingForNextWave || nextWaveAt >= NextWaveAt)
                    return;

                NextWaveAt = nextWaveAt;
                LastActivityAt = currentTime;
            }

            public void AttachCompletionVendor(ulong entityId)
            {
                CompletionVendorEntityId = entityId;
            }

            public void AttachRewardChest(ulong entityId)
            {
                RewardChestEntityId = entityId;
                RewardChestOpened = false;
            }

            public void AttachReturnPortal(ulong entityId)
            {
                ReturnPortalEntityId = entityId;
            }

            public void MarkRewardChestOpened()
            {
                RewardChestOpened = true;
            }

            public void Complete(TimeSpan currentTime)
            {
                Status = EndlessCableScenarioRunStatus.Completed;
                CompletedAt = currentTime;
                LastActivityAt = currentTime;
            }

            public void Abort(TimeSpan currentTime)
            {
                Status = EndlessCableScenarioRunStatus.Aborted;
                CompletedAt = currentTime;
                LastActivityAt = currentTime;
            }

            public void CaptureRegionDifficultyScaling(
                float playerToMobDamageMultiplier,
                float mobToPlayerDamageMultiplier,
                float experienceBonusPct,
                float lootBonusXPPct,
                float lootBonusRarityPct,
                float lootBonusSpecialPct)
            {
                if (RegionDifficultyScalingCaptured)
                    return;

                OriginalPlayerToMobDamageMultiplier = playerToMobDamageMultiplier;
                OriginalMobToPlayerDamageMultiplier = mobToPlayerDamageMultiplier;
                OriginalExperienceBonusPct = experienceBonusPct;
                OriginalLootBonusXPPct = lootBonusXPPct;
                OriginalLootBonusRarityPct = lootBonusRarityPct;
                OriginalLootBonusSpecialPct = lootBonusSpecialPct;
                RegionDifficultyScalingCaptured = true;
            }
        }

        private static class EndlessCableCompletionCrafter
        {
            public const string CompletionVendorPrototypeName = "Entity/Characters/Vendors/Prototypes/Endgame/DangerRoomRewardsVendor.prototype";
            public const string CompletionVendorTypePrototypeName = "Entity/Characters/Vendors/VendorTypes/TestVendorCrafter.prototype";

            public static readonly PrototypeId CompletionVendorPrototypeRef = (PrototypeId)9464237972577394631UL;
            public static readonly PrototypeId CompletionVendorTypePrototypeRef = (PrototypeId)843033980896156323UL;

            public static bool IsCompletionVendorType(PrototypeId vendorTypeProtoRef)
            {
                return vendorTypeProtoRef == CompletionVendorTypePrototypeRef;
            }

            public static bool IsCompletionVendor(WorldEntity vendor)
            {
                if (vendor == null)
                    return false;

                PrototypeId vendorTypeProtoRef = vendor.Properties[PropertyEnum.VendorType];
                return vendor.PrototypeDataRef == CompletionVendorPrototypeRef && IsCompletionVendorType(vendorTypeProtoRef);
            }
        }

        private static class EndlessCableUniqueUpgradeRecipe
        {
            public const string PrototypeName = "UnbindUnique";
            public static readonly PrototypeId PrototypeRef = (PrototypeId)9691334961261451315UL;

            public const int MinItemLevel = 63;
            public const int MaxItemLevel = 100;
            public const int SuccessChancePct = 10;

            private const ulong CraftingSuccessBannerLocaleStringBase = 18000000000000050000UL;
            private const ulong CraftingFailedBannerLocaleStringBase = 18000000000000050010UL;
            private const int CraftingResultBannerMaxAttemptsRemaining = 2;

            private static readonly PrototypeId[] AllowedRarityRefs =
            {
            (PrototypeId)6033964048325414744UL,   // R6Unique
            (PrototypeId)3312071462101520554UL,   // R6Runewords
            (PrototypeId)11419154057066188490UL,  // R6Omega
            (PrototypeId)8359589995259367461UL,   // R5Ultimate
            (PrototypeId)8643734431032020798UL,   // R5Cosmic
            (PrototypeId)9254498193264414304UL,   // R4Epic
        };

            public static bool IsRecipe(PrototypeId recipeProtoRef)
            {
                return recipeProtoRef == PrototypeRef;
            }

            public static bool IsEligibleTarget(Item item)
            {
                if (item == null)
                    return false;

                int itemLevel = GetItemLevel(item);
                if (itemLevel < MinItemLevel || itemLevel >= MaxItemLevel)
                    return false;

                return IsAllowedRarity((PrototypeId)item.Properties[PropertyEnum.ItemRarity]);
            }

            public static int GetItemLevel(Item item)
            {
                if (item == null)
                    return 0;

                int propertyLevel = item.Properties[PropertyEnum.ItemLevel];
                return propertyLevel > 0 ? propertyLevel : item.ItemSpec.ItemLevel;
            }

            public static bool IsAllowedRarity(PrototypeId rarityProtoRef)
            {
                if (rarityProtoRef == PrototypeId.Invalid)
                    return false;

                foreach (PrototypeId allowedRarityProtoRef in AllowedRarityRefs)
                    if (rarityProtoRef == allowedRarityProtoRef)
                        return true;

                return rarityProtoRef == GameDatabase.LootGlobalsPrototype.RarityUnique;
            }

            public static int ResolveSuccessChancePct()
            {
                float tunedChancePct = LiveTuningManager.GetLiveGlobalTuningVar(GlobalTuningVar.eGTV_EndlessCableUniqueUpgradeSuccessChancePct);
                return Math.Clamp((int)MathF.Round(tunedChancePct), 0, 100);
            }

            public static LocaleStringId GetCraftingResultBannerLocaleStringId(bool success, int attemptsRemaining)
            {
                if (attemptsRemaining < 0 || attemptsRemaining > CraftingResultBannerMaxAttemptsRemaining)
                    return LocaleStringId.Invalid;

                ulong baseId = success ? CraftingSuccessBannerLocaleStringBase : CraftingFailedBannerLocaleStringBase;
                return (LocaleStringId)(baseId + (ulong)attemptsRemaining);
            }
        }

        private sealed class EndlessCableScenarioUseResult
        {
            public bool Success { get; init; }
            public bool TeleportAttempted { get; init; }
            public bool TeleportSucceeded { get; init; }
            public string ErrorMessage { get; init; } = string.Empty;
            public ulong RunId { get; init; }
            public PrototypeId TargetRegionProtoRef { get; init; }
            public PrototypeId TargetStartTargetProtoRef { get; init; }
        }

    }

    internal static class EndlessScenarioLiveTuningHooks
    {
        internal static void ApplyDefaults(TuningVarArray globalTuningVars)
        {
            EndlessCableScenarioManager.ApplyLiveTuningDefaults(globalTuningVars);
        }

        internal static bool IsServerOnlyGlobalTuningVar(GlobalTuningVar tuningVar)
        {
            return EndlessCableScenarioManager.IsLiveTuningVar(tuningVar);
        }
    }

    internal static class EndlessScenarioLootHooks
    {
        internal static ItemSpec ApplyItemPresentation(ItemSpec itemSpec)
        {
            return EndlessCableScenarioManager.ApplyScenarioItemPresentation(itemSpec);
        }
    }
}

namespace MHServerEmu.Games
{
    public partial class Game
    {
        internal EndlessCableScenarioManager EndlessCableScenarioManager { get; private set; }

        private void InitializeEndlessScenarios()
        {
            EndlessCableScenarioManager = new(this);
        }

        private void UpdateEndlessScenarios(TimeSpan currentTime)
        {
            EndlessCableScenarioManager.Update(currentTime);
        }

        internal bool TryUseEndlessScenarioReturnPortal(Player player, Transition transition)
        {
            return EndlessCableScenarioManager?.TryUseReturnPortal(player, transition) == true;
        }

        /// <summary>
        /// Ends the current Endless Danger Room break early for the run the player is in.
        /// </summary>
        public bool TrySkipEndlessScenarioBreak(Player player, out string message)
        {
            message = "Endless Danger Room is not available.";
            return EndlessCableScenarioManager?.TrySkipBreak(player, out message) == true;
        }

        internal bool TryInterceptEndlessScenarioPowerActivation(Player player, Avatar avatar, PrototypeId powerProtoRef, bool hasItemSourceId, ulong itemSourceId)
        {
            return EndlessCableScenarioManager.TryInterceptPowerActivation(this, player, avatar, powerProtoRef, hasItemSourceId, itemSourceId);
        }

        internal bool TrySendEndlessScenarioMissionUpdateOverride(Mission mission, Player player, MissionUpdateFlags missionFlags, MissionObjectiveUpdateFlags objectiveFlags)
        {
            return EndlessCableScenarioManager?.TrySendNativeMissionUpdateOverride(mission, player, missionFlags, objectiveFlags) == true;
        }

        internal bool TryRefreshEndlessScenarioObjectiveWidgetOverride(MissionObjective objective)
        {
            return EndlessCableScenarioManager?.TryRefreshNativeObjectiveWidgetOverride(objective) == true;
        }

        internal bool TrySendEndlessScenarioObjectiveUpdateOverride(MissionObjective objective, Player player, MissionObjectiveUpdateFlags objectiveFlags)
        {
            return EndlessCableScenarioManager?.TrySendNativeObjectiveUpdateOverride(objective, player, objectiveFlags) == true;
        }
    }
}

namespace MHServerEmu.Games.Entities.Items
{
    public partial class Item
    {
        private bool IsCustomClonedWhenPurchasedFromVendor => EndlessCableScenarioManager.IsPresentationLauncherItem(PrototypeDataRef);

        private PrototypeId GetCustomScenarioRarityForRegion()
        {
            return EndlessCableScenarioManager.GetScenarioRarityForRegion(this);
        }

        private bool TryHandleCustomItemActionUse(Player player, out bool interceptedItemUse)
        {
            return EndlessCableScenarioManager.TryHandleItemActionUse(Game, player, this, out interceptedItemUse);
        }
    }
}

namespace MHServerEmu.Games.Entities
{
    public partial class Player
    {
        private readonly HashSet<ulong> _endlessCableCompletionRecipeItemIds = new();
        private bool _endlessCableCompletionCrafterInventoriesFiltered;

        private bool TryHandleCustomCraftRecipe(CraftingRecipePrototype recipeProto, Item recipeItem, List<ulong> ingredientIds,
            WorldEntity vendor, Inventory resultsInv, bool isRecraft, out CraftingResult craftingResult)
        {
            craftingResult = CraftingResult.Success;
            if (recipeProto.DataRef == (PrototypeId)0x501F318F3EC020E9UL)
            {
                craftingResult = CraftCustomArtifactSocket(recipeProto, ingredientIds, isRecraft, resultsInv, vendor);
                return true;
            }

            if (EndlessCableScenarioManager.IsUniqueUpgradeCraft(recipeProto, vendor) == false)
                return false;

            if (isRecraft)
            {
                craftingResult = PrepareEndlessCableUniqueUpgradeRecraft(ingredientIds, resultsInv);
                if (craftingResult != CraftingResult.Success)
                    return true;
            }

            CraftingResult canCraftEndlessRecipeResult = CanCraftEndlessCableUniqueUpgradeRecipe(recipeItem, ingredientIds, vendor, resultsInv, isRecraft);
            if (canCraftEndlessRecipeResult != CraftingResult.Success)
            {
                craftingResult = canCraftEndlessRecipeResult;
                return true;
            }

            using PropertyCollection endlessCurrencyCost = ObjectPoolManager.Instance.Get<PropertyCollection>();
            if (recipeProto.GetCraftingCost(this, ingredientIds, out uint endlessCreditsCost, out uint endlessLegendaryMarksCost, endlessCurrencyCost) == false)
            {
                craftingResult = CraftingResult.InsufficientIngredients;
                return true;
            }

            CraftingResult endlessCostResult = ValidateCraftingCost(endlessCreditsCost, endlessLegendaryMarksCost, endlessCurrencyCost);
            if (endlessCostResult != CraftingResult.Success)
            {
                craftingResult = endlessCostResult;
                return true;
            }

            craftingResult = CraftEndlessCableUniqueUpgrade(recipeProto, ingredientIds, isRecraft, resultsInv, vendor, endlessCreditsCost, endlessLegendaryMarksCost, endlessCurrencyCost);
            return true;
        }

        private bool CanCraftCustomRecipeWithVendor(CraftingRecipePrototype recipeProto, WorldEntity vendor)
        {
            return EndlessCableScenarioManager.IsUniqueUpgradeCraft(recipeProto, vendor);
        }

        private bool IsCustomCraftingStashToGeneralTransfer(InventoryLocation fromInvLoc, InventoryLocation toInvLoc)
        {
            return EndlessCableScenarioManager.IsCompletionCrafterStashToGeneralTransfer(this, fromInvLoc, toInvLoc);
        }

        internal bool TryPrepareCustomCompletionVendorInteraction(WorldEntity vendor)
        {
            return EndlessCableScenarioManager.TryPrepareCompletionVendorInteraction(this, vendor);
        }

        internal bool ShouldSuppressCustomCraftingSuccessMessage(Item recipeItem, WorldEntity vendor, NetMessageTryCraft tryCraft)
        {
            return EndlessCableScenarioManager.ShouldSuppressCraftingSuccessMessageForTryCraft(this, recipeItem, vendor, tryCraft);
        }

        private bool IsCustomCompletionVendorType(PrototypeId vendorTypeProtoRef)
        {
            return IsEndlessCableCompletionVendorType(vendorTypeProtoRef);
        }

        private void OnCustomDialogTargetChanged(WorldEntity dialogTarget)
        {
            OnEndlessCableDialogTargetChanged(dialogTarget);
        }

        private bool TryBlockCustomCompletionVendorReroll(PrototypeId vendorTypeProtoRef, bool isInitializing)
        {
            return TryBlockEndlessCableCompletionVendorReroll(vendorTypeProtoRef, isInitializing);
        }

        private bool ShouldSkipCustomRecipeInVendorRoll(bool isCompletionVendorType, PrototypeId recipeProtoRef)
        {
            return ShouldSkipEndlessCableUniqueUpgradeRecipeInVendorRoll(isCompletionVendorType, recipeProtoRef);
        }

        private bool IsEndlessCableCompletionVendor(WorldEntity vendor)
        {
            return EndlessCableScenarioManager.IsCompletionVendor(vendor);
        }

        private bool IsEndlessCableCompletionVendorType(PrototypeId vendorTypeProtoRef)
        {
            return EndlessCableScenarioManager.IsCompletionVendorType(vendorTypeProtoRef);
        }

        private void OnEndlessCableDialogTargetChanged(WorldEntity dialogTarget)
        {
            if (IsEndlessCableCompletionVendor(dialogTarget))
            {
                EnsureEndlessCableCompletionCrafterStock(dialogTarget);
                return;
            }

            if (ShouldRetainEndlessCableCompletionCrafterIsolation())
            {
                return;
            }

            ClearEndlessCableCompletionCraftingRecipes();
        }

        private void InitializeVendorInventoryCore(PrototypeId inventoryProtoRef)
        {
            WorldEntity dialogTarget = GetDialogTarget(false);
            if (TryInitializeEndlessCableCompletionVendorInventory(inventoryProtoRef, ref dialogTarget))
                return;

            // if (IsEndlessCableCompletionVendor(dialogTarget) == false && TryInitializeDialogTargetVendorInventory(dialogTarget, inventoryProtoRef))
            // return;

            foreach (PrototypeId vendorTypeProtoRef in DataDirectory.Instance.IteratePrototypesInHierarchy<VendorTypePrototype>(PrototypeIterateFlags.NoAbstract))
            {
                VendorTypePrototype vendorTypeProto = vendorTypeProtoRef.As<VendorTypePrototype>();
                if (vendorTypeProto.ContainsInventory(inventoryProtoRef))
                {
                    if (TryInitializeEndlessCableCompletionVendorTypeInventory(vendorTypeProto, vendorTypeProtoRef, inventoryProtoRef))
                        return;

                    RollVendorInventory(vendorTypeProto, true);
                    return;
                }
            }
        }

        private bool TryInitializeEndlessCableCompletionVendorInventory(PrototypeId inventoryProtoRef, ref WorldEntity dialogTarget)
        {
            if (IsEndlessCableCompletionVendor(dialogTarget))
            {
                PrototypeId completionVendorTypeProtoRef = dialogTarget.Properties[PropertyEnum.VendorType];
                VendorTypePrototype completionVendorTypeProto = completionVendorTypeProtoRef.As<VendorTypePrototype>();
                if (completionVendorTypeProto?.ContainsInventory(inventoryProtoRef) == true)
                {
                    EnsureEndlessCableCompletionCrafterStock(completionVendorTypeProto, completionVendorTypeProtoRef, false, "completion-inventory-init");
                    return true;
                }
            }

            if (_endlessCableCompletionCrafterInventoriesFiltered == false)
                return false;

            VendorTypePrototype filteredVendorTypeProto = EndlessCableScenarioManager.CompletionVendorTypePrototypeRef.As<VendorTypePrototype>();
            if (filteredVendorTypeProto?.ContainsInventory(inventoryProtoRef) != true)
                return false;

            if (ShouldRetainEndlessCableCompletionCrafterIsolation())
            {
                return true;
            }

            ClearEndlessCableCompletionCraftingRecipes();
            dialogTarget = GetDialogTarget(false);
            return true;
        }

        private bool TryInitializeEndlessCableCompletionVendorTypeInventory(VendorTypePrototype vendorTypeProto, PrototypeId vendorTypeProtoRef, PrototypeId inventoryProtoRef)
        {
            if (IsUsingEndlessCableCompletionCrafter(vendorTypeProtoRef))
            {
                EnsureEndlessCableCompletionCrafterStock(vendorTypeProto, vendorTypeProtoRef, false, "completion-inventory-init");
                return true;
            }

            if (EndlessCableScenarioManager.IsCompletionVendorType(vendorTypeProtoRef) == false || _endlessCableCompletionCrafterInventoriesFiltered == false)
                return false;

            if (ShouldRetainEndlessCableCompletionCrafterIsolation())
            {
                return true;
            }

            ClearEndlessCableCompletionCraftingRecipes();
            return true;
        }

        private bool TryBlockEndlessCableCompletionVendorReroll(PrototypeId vendorTypeProtoRef, bool isInitializing)
        {
            if (_endlessCableCompletionCrafterInventoriesFiltered == false || EndlessCableScenarioManager.IsCompletionVendorType(vendorTypeProtoRef) == false)
                return false;

            if (isInitializing == false)
                SendMessage(NetMessageVendorRefresh.CreateBuilder().SetVendorTypeProtoId((ulong)vendorTypeProtoRef).Build());

            return true;
        }

        private bool ShouldSkipEndlessCableUniqueUpgradeRecipeInVendorRoll(bool isCompletionVendorType, PrototypeId recipeProtoRef)
        {
            return isCompletionVendorType && EndlessCableScenarioManager.IsUniqueUpgradeRecipe(recipeProtoRef);
        }

        private CraftingResult CanCraftEndlessCableUniqueUpgradeRecipe(Item recipeItem, List<ulong> ingredientIds, WorldEntity vendor, Inventory resultsInv, bool isRecraft)
        {
            if (recipeItem == null) return CraftingResult.CraftingFailed;
            if (ingredientIds == null || ingredientIds.Count == 0) return CraftingResult.IngredientInvalid;
            if (vendor == null || EndlessCableScenarioManager.IsCompletionVendor(vendor) == false)
                return CraftingResult.CraftingFailed;

            if (EndlessCableScenarioManager.IsUniqueUpgradeRecipe(recipeItem.PrototypeDataRef) == false)
                return CraftingResult.RecipeNotInRecipeLibrary;

            if (Owns(recipeItem) == false)
                return CraftingResult.CraftingFailed;

            if (isRecraft == false && resultsInv?.Count > 0)
                return CraftingResult.CraftingFailed;

            Item sourceItem = ResolveEndlessCableUniqueUpgradeSourceItem(ingredientIds, resultsInv, isRecraft);
            if (sourceItem == null)
                return CraftingResult.IngredientInvalid;

            if (sourceItem.GetOwnerOfType<Player>() != this)
                return CraftingResult.CraftingFailed;

            if (EndlessCableScenarioManager.IsUniqueUpgradeEligibleTarget(sourceItem) == false)
                return CraftingResult.IngredientLevelRestricted;

            if (TryResolveEndlessCableCompletionRunId(vendor, out ulong completionRunId) == false)
                return CraftingResult.CraftingFailed;

            int usedAttempts = Game.EndlessCableScenarioManager.GetCompletionRecipeAttempts(completionRunId);
            if (usedAttempts >= EndlessCableScenarioManager.CompletionRecipeMaxAttemptsPerRun)
            {
                SendEndlessCableUpgradeAttemptLimitMessage();
                return CraftingResult.CraftingFailed;
            }

            return CraftingResult.Success;
        }

        private Item ResolveEndlessCableUniqueUpgradeSourceItem(List<ulong> ingredientIds, Inventory resultsInv, bool isRecraft)
        {
            if (ingredientIds != null)
            {
                foreach (ulong ingredientId in ingredientIds)
                {
                    if (ingredientId == InvalidId)
                        continue;

                    Item ingredient = Game.EntityManager.GetEntity<Item>(ingredientId);
                    if (ingredient == null)
                        continue;

                    if (EndlessCableScenarioManager.IsUniqueUpgradeRecipe(ingredient.PrototypeDataRef))
                        continue;

                    return ingredient;
                }
            }

            if (isRecraft && resultsInv != null)
            {
                ulong recraftItemId = resultsInv.GetEntityInSlot(0);
                if (recraftItemId != InvalidId)
                    return Game.EntityManager.GetEntity<Item>(recraftItemId);
            }

            return null;
        }

        private bool TryResolveEndlessCableCompletionRunId(WorldEntity vendor, out ulong runId)
        {
            runId = 0;

            EndlessCableScenarioManager endlessManager = Game?.EndlessCableScenarioManager;
            if (endlessManager != null && endlessManager.TryResolveCompletionCraftRunId(vendor, DatabaseUniqueId, out runId))
                return true;

            return false;
        }

        private void SendEndlessCableUpgradeAttemptLimitMessage()
        {
            if (Game?.ChatManager == null)
                return;

            Game.ChatManager.SendChatFromCustomSystem(
                this,
                $"[Endless Cable Crafting] Attempt limit reached ({EndlessCableScenarioManager.CompletionRecipeMaxAttemptsPerRun}/{EndlessCableScenarioManager.CompletionRecipeMaxAttemptsPerRun}) for this run.",
                showSender: false);
        }

        private CraftingResult CraftEndlessCableUniqueUpgrade(CraftingRecipePrototype recipeProto, List<ulong> ingredientIds, bool isRecraft,
            Inventory resultsInv, WorldEntity vendor, uint creditsCost, uint legendaryMarksCost, PropertyCollection currencyCost)
        {
            Avatar avatar = CurrentAvatar;
            if (avatar == null) return CraftingResult.CraftingFailed;

            Item sourceItem = ResolveEndlessCableUniqueUpgradeSourceItem(ingredientIds, resultsInv, isRecraft);
            if (sourceItem == null)
                return CraftingResult.IngredientInvalid;

            if (EndlessCableScenarioManager.IsUniqueUpgradeEligibleTarget(sourceItem) == false)
            {
                return CraftingResult.IngredientLevelRestricted;
            }

            if (TryResolveEndlessCableCompletionRunId(vendor, out ulong completionRunId) == false)
                return CraftingResult.CraftingFailed;

            int sourceLevel = EndlessCableScenarioManager.GetUniqueUpgradeItemLevel(sourceItem);
            PrototypeId sourceProtoRef = sourceItem.PrototypeDataRef;
            string sourceProtoName = sourceProtoRef.GetNameFormatted() ?? sourceProtoRef.ToString();
            int successChancePct = EndlessCableScenarioManager.ResolveUniqueUpgradeSuccessChancePct();
            bool success = Game.Random.NextPct(successChancePct);
            int outputLevel = success ? Math.Min(sourceLevel + 1, EndlessCableScenarioManager.UniqueUpgradeMaxItemLevel) : sourceLevel;

            Item outputItem = CreateEndlessCableUniqueUpgradeOutput(sourceItem, resultsInv, outputLevel);
            if (outputItem == null)
                return CraftingResult.CraftingFailed;

            if (Game.EndlessCableScenarioManager.TryConsumeCompletionRecipeAttempt(completionRunId, out int attemptNumber, out int attemptsRemaining) == false)
            {
                outputItem.Destroy();
                SendEndlessCableUpgradeAttemptLimitMessage();
                return CraftingResult.CraftingFailed;
            }

            // Point of no return from here. A failed roll preserves the target item at its original level,
            // but the recipe cost and materials are spent.
            int quantity = sourceItem.IsRelic ? sourceItem.CurrentStackSize : 1;
            sourceItem.DecrementStack(quantity);
            CraftPayCost(creditsCost, legendaryMarksCost, currencyCost);

            TriggerEndlessCableUniqueUpgradeCraftEvents(recipeProto, outputItem);
            SendEndlessCableUniqueUpgradeResultMessage(success, sourceLevel, outputLevel, outputItem, attemptNumber, attemptsRemaining);

            return CraftingResult.Success;
        }

        private CraftingResult PrepareEndlessCableUniqueUpgradeRecraft(List<ulong> ingredientIds, Inventory resultsInv)
        {
            if (resultsInv == null) return CraftingResult.CraftingFailed;

            ulong recraftItemId = resultsInv.GetEntityInSlot(0);
            if (recraftItemId == InvalidId) return CraftingResult.CraftingFailed;

            Item recraftItem = Game.EntityManager.GetEntity<Item>(recraftItemId);
            if (recraftItem == null) return CraftingResult.CraftingFailed;

            uint recraftFreeSlot = resultsInv.GetFreeSlot(recraftItem, false, false);
            if (recraftFreeSlot == Inventory.InvalidSlot) return CraftingResult.CraftingFailed;

            ulong? stackEntityId = null;
            InventoryResult recraftItemMoveResult = recraftItem.ChangeInventoryLocation(resultsInv, recraftFreeSlot, ref stackEntityId, false);
            if (recraftItemMoveResult != InventoryResult.Success)
                return CraftingResult.CraftingFailed;

            if (ingredientIds.Count > 0 && ingredientIds[0] == InvalidId)
                ingredientIds[0] = recraftItemId;

            return CraftingResult.Success;
        }

        private Item CreateEndlessCableUniqueUpgradeOutput(Item sourceItem, Inventory resultsInv, int outputLevel)
        {
            if (sourceItem == null) return null;
            if (resultsInv == null) return null;

            ItemSpec outputSpec = new(sourceItem.ItemSpec)
            {
                ItemLevel = outputLevel,
                StackCount = sourceItem.IsRelic ? sourceItem.CurrentStackSize : 1
            };

            uint outputSlot = resultsInv.GetFreeSlot(null, false);
            if (outputSlot == Inventory.InvalidSlot)
                return null;

            using EntitySettings settings = ObjectPoolManager.Instance.Get<EntitySettings>();
            settings.EntityRef = outputSpec.ItemProtoRef;
            settings.ItemSpec = outputSpec;
            settings.InventoryLocation = new(Id, resultsInv.PrototypeDataRef, outputSlot);
            settings.OptionFlags |= EntitySettingsOptionFlags.DoNotAllowStackingOnCreate;

            if (IsInGame == false)
                settings.OptionFlags &= ~EntitySettingsOptionFlags.EnterGame;

            using PropertyCollection properties = ObjectPoolManager.Instance.Get<PropertyCollection>();
            settings.Properties = properties;
            properties[PropertyEnum.InventoryStackCount] = outputSpec.StackCount;

            Item outputItem = Game.EntityManager.CreateEntity(settings) as Item;
            if (outputItem == null)
                return null;

            if (outputItem.InventoryLocation.InventoryRef != resultsInv.PrototypeDataRef)
            {
                outputItem.Destroy();
                return null;
            }

            outputItem.Properties.CopyProperty(sourceItem.Properties, PropertyEnum.ItemLimitedEdition);

            if (sourceItem.IsPetItem)
                outputItem.Properties.CopyPropertyRange(sourceItem.Properties, PropertyEnum.PetItemDonationCount);

            return outputItem;
        }

        private void TriggerEndlessCableUniqueUpgradeCraftEvents(CraftingRecipePrototype recipeProto, Item outputItem)
        {
            if (recipeProto == null || outputItem == null)
                return;

            Region region = GetRegion();
            RarityPrototype rarityProto = outputItem.RarityPrototype;
            int count = outputItem.CurrentStackSize;

            region?.PlayerCraftedItemEvent.Invoke(new(this, outputItem, recipeProto.DataRef, count));
            OnScoringEvent(new(ScoringEventType.ItemCrafted, recipeProto, rarityProto, count));
        }

        private void SendEndlessCableUniqueUpgradeResultMessage(bool success, int sourceLevel, int outputLevel, Item outputItem, int attemptNumber, int attemptsRemaining)
        {
            SendEndlessCableUniqueUpgradeResultBanner(success, attemptsRemaining);

            if (Game?.ChatManager == null || outputItem == null)
                return;

            string itemName = outputItem.PrototypeDataRef.GetNameFormatted();
            string message = success
                ? $"[Endless Cable Crafting] Success: {itemName} upgraded from level {sourceLevel} to {outputLevel}. Attempt {attemptNumber}/{EndlessCableScenarioManager.CompletionRecipeMaxAttemptsPerRun}."
                : $"[Endless Cable Crafting] Upgrade failed: {itemName} stayed at level {sourceLevel}. Attempt {attemptNumber}/{EndlessCableScenarioManager.CompletionRecipeMaxAttemptsPerRun}.";

            if (attemptsRemaining == 0)
                message += " No attempts left for this run.";
            else
                message += $" Remaining attempts this run: {attemptsRemaining}.";

            Game.ChatManager.SendChatFromCustomSystem(this, message, showSender: false);
        }

        private void SendEndlessCableUniqueUpgradeResultBanner(bool success, int attemptsRemaining)
        {
            LocaleStringId bannerText = EndlessCableScenarioManager.GetUniqueUpgradeResultBannerLocaleStringId(success, attemptsRemaining);
            if (bannerText == LocaleStringId.Invalid)
            {
                return;
            }

            SendBannerMessage(
                bannerText,
                TextStylePrototype.BannerMessageLarge,
                timeToLiveMS: 3500,
                BannerMessageStyle.FlyIn,
                doNotQueue: true,
                showImmediately: true);
        }

        public void EnsureEndlessCableCompletionCrafterStock(WorldEntity vendor)
        {
            if (vendor == null)
                return;

            if (EndlessCableScenarioManager.IsCompletionVendor(vendor) == false)
                return;

            PrototypeId vendorTypeProtoRef = vendor.Properties[PropertyEnum.VendorType];
            VendorTypePrototype vendorTypeProto = vendorTypeProtoRef.As<VendorTypePrototype>();
            if (vendorTypeProto == null)
                return;

            EnsureEndlessCableCompletionCrafterStock(vendorTypeProto, vendorTypeProtoRef, true, "completion-dialog-option");
        }

        private bool EnsureEndlessCableCompletionCrafterStock(VendorTypePrototype vendorTypeProto, PrototypeId vendorTypeProtoRef, bool notifyClient, string source)
        {
            if (vendorTypeProto == null)
                return false;

            if (EndlessCableScenarioManager.IsCompletionVendorType(vendorTypeProtoRef) == false)
                return false;

            bool stocked = EnsureEndlessCableCompletionCraftingRecipe(vendorTypeProto, vendorTypeProtoRef);

            if (stocked == false)
                return false;

            if (notifyClient && vendorTypeProtoRef != PrototypeId.Invalid)
                SendMessage(NetMessageVendorRefresh.CreateBuilder().SetVendorTypeProtoId((ulong)vendorTypeProtoRef).Build());

            return true;
        }

        private bool EnsureEndlessCableCompletionCraftingRecipe(VendorTypePrototype vendorTypeProto, PrototypeId vendorTypeProtoRef)
        {
            if (vendorTypeProto == null) return false;
            if (vendorTypeProto.IsCrafter == false) return false;

            using var inventoryListHandle = ListPool<PrototypeId>.Instance.Get(out List<PrototypeId> inventoryList);
            if (vendorTypeProto.GetInventories(inventoryList) == false)
                return false;

            CleanupTrackedEndlessCableCompletionRecipes();
            FilterEndlessCableCompletionCrafterInventories(vendorTypeProto, inventoryList);

            // Define all recipes to inject into the completion vendor
            PrototypeId[] recipesToInject =
            {
        EndlessCableScenarioManager.UniqueUpgradeRecipePrototypeRef,
        EndlessCableScenarioManager.AddSocketRecipePrototypeRef
    };

            using var ingredientSetHandle = HashSetPool<PrototypeId>.Instance.Get(out HashSet<PrototypeId> craftingIngredientSet);
            EntityManager entityManager = Game.EntityManager;
            bool injectedAny = false;

            foreach (PrototypeId inventoryProtoRef in inventoryList)
            {
                Inventory inventory = GetInventoryByRef(inventoryProtoRef);
                if (inventory == null)
                    continue;

                foreach (PrototypeId recipeRef in recipesToInject)
                {
                    if (recipeRef == PrototypeId.Invalid)
                    {
                        continue;
                    }

                    CraftingRecipePrototype recipeProto = recipeRef.As<CraftingRecipePrototype>();
                    if (recipeProto == null)
                    {
                        continue;
                    }

                    if (HasTrackedEndlessCableCompletionRecipe(inventory, recipeRef))
                    {
                        InitializeCraftingIngredientAvailable(recipeProto, craftingIngredientSet);
                        injectedAny = true;
                        continue;
                    }

                    uint slot = inventory.GetFreeSlot(null, false);
                    if (slot == Inventory.InvalidSlot)
                    {
                        continue;
                    }

                    ItemSpec itemSpec = Game.LootManager.CreateItemSpec(recipeRef, LootContext.Vendor, this);
                    if (itemSpec == null)
                    {
                        continue;
                    }

                    using EntitySettings settings = ObjectPoolManager.Instance.Get<EntitySettings>();
                    settings.EntityRef = recipeRef;
                    settings.ItemSpec = itemSpec;
                    settings.InventoryLocation = new(Id, inventory.PrototypeDataRef, slot);
                    settings.OptionFlags |= EntitySettingsOptionFlags.DoNotAllowStackingOnCreate;

                    if (IsInGame == false)
                        settings.OptionFlags &= ~EntitySettingsOptionFlags.EnterGame;

                    Item recipeItem = entityManager.CreateEntity(settings) as Item;
                    if (recipeItem == null)
                    {
                        continue;
                    }

                    _endlessCableCompletionRecipeItemIds.Add(recipeItem.Id);
                    InitializeCraftingIngredientAvailable(recipeProto, craftingIngredientSet);
                    injectedAny = true;
                }
            }

            if (injectedAny)
            {
                UpdateCraftingIngredientAvailableStackCounts(craftingIngredientSet);
            }

            return injectedAny || _endlessCableCompletionCrafterInventoriesFiltered;
        }

        private bool IsUsingEndlessCableCompletionCrafter(PrototypeId vendorTypeProtoRef)
        {
            if (EndlessCableScenarioManager.IsCompletionVendorType(vendorTypeProtoRef) == false)
                return false;

            return EndlessCableScenarioManager.IsCompletionVendor(GetDialogTarget(false));
        }

        private void FilterEndlessCableCompletionCrafterInventories(VendorTypePrototype vendorTypeProto, List<PrototypeId> inventoryList)
        {
            if (_endlessCableCompletionCrafterInventoriesFiltered && IsEndlessCableCompletionCrafterAlreadyIsolated(inventoryList))
            {
                _initializedVendorTypeProtoRefs.Remove(vendorTypeProto.DataRef);
                return;
            }

            int clearedInventories = 0;
            foreach (PrototypeId inventoryProtoRef in inventoryList)
            {
                Inventory inventory = GetInventoryByRef(inventoryProtoRef);
                if (inventory == null)
                    continue;

                inventory.DestroyContained();
                clearedInventories++;
            }

            _endlessCableCompletionRecipeItemIds.Clear();
            _initializedVendorTypeProtoRefs.Remove(vendorTypeProto.DataRef);
            _endlessCableCompletionCrafterInventoriesFiltered = true;
        }

        private bool IsEndlessCableCompletionCrafterAlreadyIsolated(List<PrototypeId> inventoryList)
        {
            bool foundTrackedRecipe = false;
            foreach (PrototypeId inventoryProtoRef in inventoryList)
            {
                Inventory inventory = GetInventoryByRef(inventoryProtoRef);
                if (inventory == null)
                    continue;

                foreach (var entry in inventory)
                {
                    Item item = Game.EntityManager.GetEntity<Item>(entry.Id);
                    if (item == null || item.IsScheduledToDestroy)
                        continue;

                    if (IsTrackedEndlessCableCompletionRecipe(item.Id) && EndlessCableScenarioManager.IsInjectedCompletionRecipe(item.PrototypeDataRef))
                    {
                        foundTrackedRecipe = true;
                        continue;
                    }

                    return false;
                }
            }

            return foundTrackedRecipe;
        }
        private CraftingResult CraftCustomArtifactSocket(CraftingRecipePrototype recipeProto, List<ulong> ingredientIds, bool isRecraft, Inventory resultsInv, WorldEntity vendor)
        {
            Item sourceItem = ResolveEndlessCableUniqueUpgradeSourceItem(ingredientIds, resultsInv, isRecraft);
            if (sourceItem == null) return CraftingResult.IngredientInvalid;

            IReadOnlyList<AffixPrototype> socketPool = GameDataTables.Instance.LootPickingTable.GetAffixesByPosition(AffixPosition.Socket1);
            if (socketPool == null || socketPool.Count == 0)
            {
                Logger.Warn("[CustomCrafting] No Socket1 affixes exist in the engine's picking table!");
                return CraftingResult.CraftingFailed;
            }
            AffixPrototype baseSocketProto = socketPool[0];

            bool isAlreadySocketed = false;
            foreach (AffixSpec affix in sourceItem.ItemSpec.AffixSpecs)
            {

                if (affix.AffixProto != null && affix.AffixProto.DataRef == baseSocketProto.DataRef)
                {
                    isAlreadySocketed = true;
                    break;
                }
            }

            if (isAlreadySocketed)
            {
                Game?.ChatManager?.SendChatFromCustomSystem(this, "[Crafting] Failed: This item already has a socket.", showSender: false);
                return CraftingResult.CraftingFailed;
            }

            ItemSpec clonedSpec = new ItemSpec(sourceItem.ItemSpec);


            AffixSpec socketSpec = new AffixSpec(baseSocketProto, PrototypeId.Invalid, Game.Random.Next(1, int.MaxValue));
            clonedSpec.AddAffixSpec(socketSpec);


            using EntitySettings settings = ObjectPoolManager.Instance.Get<EntitySettings>();
            settings.EntityRef = sourceItem.PrototypeDataRef;
            settings.ItemSpec = clonedSpec;

            Item outputItem = Game.EntityManager.CreateEntity(settings) as Item;
            if (outputItem == null) return CraftingResult.CraftingFailed;


            InventoryLocation prevInvLoc = InventoryLocation.Invalid;

            outputItem.SetStatus(EntityStatus.SkipItemBindingCheck, true);

            InventoryResult moveResult = Inventory.ChangeEntityInventoryLocationOnCreate(outputItem, resultsInv, Inventory.InvalidSlot, false, true, ref prevInvLoc);

            outputItem.SetStatus(EntityStatus.SkipItemBindingCheck, false);

            if (moveResult != InventoryResult.Success)
            {
                Logger.Warn($"[CustomCrafting] Failed to move socketed artifact to results box. Error: {moveResult}");
                outputItem.Destroy(); // Clean up the orphaned item
                return CraftingResult.CraftingFailed;
            }

            int quantity = sourceItem.IsRelic ? sourceItem.CurrentStackSize : 1;
            sourceItem.DecrementStack(quantity);

            TriggerEndlessCableUniqueUpgradeCraftEvents(recipeProto, outputItem);

            Game?.ChatManager?.SendChatFromCustomSystem(this, $"[Crafting] Success: {outputItem.PrototypeDataRef.GetNameFormatted()} was granted an empty socket.", showSender: false);

            return CraftingResult.Success;
        }
        private bool HasTrackedEndlessCableCompletionRecipe(Inventory inventory, PrototypeId recipeRef)
        {
            if (inventory == null)
                return false;

            foreach (var entry in inventory)
            {
                Item item = Game.EntityManager.GetEntity<Item>(entry.Id);
                if (item != null && IsTrackedEndlessCableCompletionRecipe(item.Id) && item.PrototypeDataRef == recipeRef)
                    return true;
            }

            return false;
        }

        private bool IsTrackedEndlessCableCompletionRecipe(ulong itemId)
        {
            return _endlessCableCompletionRecipeItemIds.Contains(itemId);
        }

        private void CleanupTrackedEndlessCableCompletionRecipes()
        {
            _endlessCableCompletionRecipeItemIds.RemoveWhere(itemId =>
            {
                Item item = Game?.EntityManager.GetEntity<Item>(itemId);
                return item == null || item.IsScheduledToDestroy;
            });
        }

        private void ClearEndlessCableCompletionCraftingRecipes()
        {
            if (_endlessCableCompletionRecipeItemIds.Count == 0 && _endlessCableCompletionCrafterInventoriesFiltered == false)
                return;

            int removed = _endlessCableCompletionCrafterInventoriesFiltered
                ? DestroyEndlessCableCompletionCrafterInventoryContents()
                : DestroyTrackedEndlessCableCompletionRecipeItems();

            _endlessCableCompletionRecipeItemIds.Clear();
            _initializedVendorTypeProtoRefs.Remove(EndlessCableScenarioManager.CompletionVendorTypePrototypeRef);
        }

        private int DestroyTrackedEndlessCableCompletionRecipeItems()
        {
            int removed = 0;

            foreach (ulong itemId in _endlessCableCompletionRecipeItemIds)
            {
                Item item = Game?.EntityManager.GetEntity<Item>(itemId);
                if (item == null || EndlessCableScenarioManager.IsInjectedCompletionRecipe(item.PrototypeDataRef) == false)
                    continue;

                item.Destroy();
                removed++;
            }

            return removed;
        }

        private int DestroyEndlessCableCompletionCrafterInventoryContents()
        {
            VendorTypePrototype vendorTypeProto = EndlessCableScenarioManager.CompletionVendorTypePrototypeRef.As<VendorTypePrototype>();
            if (vendorTypeProto == null)
                return DestroyTrackedEndlessCableCompletionRecipeItems();

            using var inventoryListHandle = ListPool<PrototypeId>.Instance.Get(out List<PrototypeId> inventoryList);
            if (vendorTypeProto.GetInventories(inventoryList) == false)
                return DestroyTrackedEndlessCableCompletionRecipeItems();

            int removed = 0;
            foreach (PrototypeId inventoryProtoRef in inventoryList)
            {
                Inventory inventory = GetInventoryByRef(inventoryProtoRef);
                if (inventory == null)
                    continue;

                foreach (var entry in inventory)
                {
                    Item item = Game?.EntityManager.GetEntity<Item>(entry.Id);
                    if (item != null && item.IsScheduledToDestroy == false)
                        removed++;
                }

                inventory.DestroyContained();
            }

            return removed;
        }
        private CraftingResult ValidateCraftingCost(uint creditsCost, uint legendaryMarksCost, PropertyCollection currencyCost)
        {
            if (creditsCost > 0 && Properties[PropertyEnum.Currency, GameDatabase.CurrencyGlobalsPrototype.Credits] < creditsCost)
                return CraftingResult.CraftingFailed;

            if (legendaryMarksCost > 0 && Properties[PropertyEnum.Currency, GameDatabase.CurrencyGlobalsPrototype.LegendaryMarks] < legendaryMarksCost)
                return CraftingResult.CraftingFailed;

            if (currencyCost != null)
            {
                foreach (var prop in currencyCost)
                {
                    if (Properties[prop.Key] < prop.Value)
                        return CraftingResult.CraftingFailed;
                }
            }

            return CraftingResult.Success;
        }
        private bool ShouldRetainEndlessCableCompletionCrafterIsolation()
        {
            if (_endlessCableCompletionCrafterInventoriesFiltered == false)
                return false;

            Region region = GetRegion();
            EndlessCableScenarioManager endlessManager = Game?.EndlessCableScenarioManager;
            if (region == null || endlessManager == null)
                return false;

            ulong playerDbId = DatabaseUniqueId;
            return endlessManager.HasActiveCompletionVendorInRegionForPlayer(region.Id, playerDbId);
        }

    }
}