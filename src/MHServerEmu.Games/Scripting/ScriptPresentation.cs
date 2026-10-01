using System.Text;
using Gazillion;
using Google.ProtocolBuffers;
using MHServerEmu.Core.Logging;
using MHServerEmu.Core.VectorMath;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Network;
using MHServerEmu.Games.Regions;
using MHServerEmu.Games.UI;
using MHServerEmu.Games.UI.Widgets;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// Client presentation for script events: story notifications (portrait + text), HUD timer, tracked entity, cutscenes,
    /// sounds, wave / score counters, global event progress, team selection and debug primitives.
    /// </summary>
    /// <remarks>
    /// Several of these are only drawn by the client in certain situations (e.g. a region whose metagame has the matching UI
    /// panel). Use the tests/ui_test.csx commands to see which ones work where. Text arguments are registered text keys
    /// (<see cref="ScriptText"/>). Methods taking a <see cref="Region"/> send to every player in it.
    /// </remarks>
    public static class ScriptPresentation
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        #region Story notification (portrait + text)

        /// <summary>
        /// Shows a story notification: <paramref name="speakerPath"/>'s portrait (any entity prototype, e.g. an NPC or villain)
        /// with the registered text <paramref name="textKey"/>. <paramref name="voAssetId"/> optionally plays an existing voice line.
        /// </summary>
        public static bool StoryNotification(Player player, string textKey, string speakerPath = null, int durationMs = 6000, ulong voAssetId = 0)
        {
            IMessage message = BuildStoryNotification(textKey, speakerPath, durationMs, voAssetId);
            return Send(player, message);
        }

        public static int StoryNotificationToRegion(Region region, string textKey, string speakerPath = null, int durationMs = 6000, ulong voAssetId = 0)
        {
            return SendToRegion(region, BuildStoryNotification(textKey, speakerPath, durationMs, voAssetId));
        }

        private static IMessage BuildStoryNotification(string textKey, string speakerPath, int durationMs, ulong voAssetId)
        {
            LocaleStringId text = ScriptText.GetId(textKey);
            if (text == LocaleStringId.Blank)
            {
                Logger.Warn($"StoryNotification(): unregistered text key [{textKey}]");
                return null;
            }

            var builder = NetMessageStoryNotification.CreateBuilder()
                .SetDisplayTextStringId((ulong)text)
                .SetTimeToLiveMS((uint)Math.Max(durationMs, 500))
                .SetVoTriggerAssetId(voAssetId);

            PrototypeId speakerRef = ResolvePrototype(speakerPath);
            if (speakerRef != PrototypeId.Invalid)
                builder.SetSpeakingEntityPrototypeId((ulong)speakerRef);

            return builder.Build();
        }

        #endregion

        #region HUD timer and texts (tied to the region's metagame)

        /// <summary>
        /// Starts the HUD countdown timer (the PvP / game mode timer) for <paramref name="seconds"/>, labelled with the registered text
        /// <paramref name="labelKey"/> (empty = default label). The timer belongs to the region's metagame.
        /// </summary>
        public static int StartTimer(Region region, float seconds, string labelKey = "", float lowWarningSeconds = 60f, float criticalWarningSeconds = 10f)
        {
            ulong metaGameId = GetMetaGameId(region);
            if (metaGameId == 0)
                return Logger.WarnReturn(0, $"StartTimer(): region [{region?.PrototypeName}] has no metagame");

            var message = NetMessageStartPvPTimer.CreateBuilder()
                .SetMetaGameId(metaGameId)
                .SetStartTime((uint)(seconds * 1000f))
                .SetEndTime(0)
                .SetLowTimeWarning((uint)(lowWarningSeconds * 1000f))
                .SetCriticalTimeWarning((uint)(criticalWarningSeconds * 1000f))
                .SetLabelOverrideTextId((ulong)ScriptText.GetId(labelKey))
                .Build();

            return SendToRegion(region, message);
        }

        public static int StopTimer(Region region)
        {
            ulong metaGameId = GetMetaGameId(region);
            if (metaGameId == 0)
                return 0;

            return SendToRegion(region, NetMessageStopPvPTimer.CreateBuilder().SetMetaGameId(metaGameId).Build());
        }

        /// <summary>
        /// Sets the metagame "mode" text (the game mode name shown on the HUD in game modes that display one).
        /// </summary>
        public static int SetModeText(Region region, string textKey)
        {
            ulong metaGameId = GetMetaGameId(region);
            if (metaGameId == 0)
                return 0;

            var message = NetMessageSetModeText.CreateBuilder()
                .SetMetaGameId(metaGameId)
                .SetModeRef(0)
                .SetModeTextId((ulong)ScriptText.GetId(textKey))
                .Build();

            return SendToRegion(region, message);
        }

        public static int SetTimerText(Region region, string textKey, long arg = 0)
        {
            return SendToRegion(region, NetMessageSetTimerText.CreateBuilder().SetTimerTextId((ulong)ScriptText.GetId(textKey)).SetArg1(arg).Build());
        }

        public static int SetExtraText(Region region, string textKey)
        {
            return SendToRegion(region, NetMessageSetExtraText.CreateBuilder().SetExtraTextId((ulong)ScriptText.GetId(textKey)).Build());
        }

        #endregion

        #region Counters (tied to the region's metagame)

        public static int WaveCounter(Region region, int current, int max)
        {
            ulong metaGameId = GetMetaGameId(region);
            if (metaGameId == 0) return 0;

            return SendToRegion(region, NetMessageMetaGameWaveUpdate.CreateBuilder()
                .SetMetaGameId(metaGameId).SetCurrentWaveCount((ulong)Math.Max(current, 0)).SetMaxWaveCount((ulong)Math.Max(max, 0)).Build());
        }

        public static int ScoreCounter(Region region, int score)
        {
            ulong metaGameId = GetMetaGameId(region);
            if (metaGameId == 0) return 0;

            return SendToRegion(region, NetMessagePvEInstanceRegionScoreUpdate.CreateBuilder()
                .SetMetaGameId(metaGameId).SetCurrentRegionScore((ulong)Math.Max(score, 0)).Build());
        }

        public static int DeathCounter(Region region, int deaths)
        {
            ulong metaGameId = GetMetaGameId(region);
            if (metaGameId == 0) return 0;

            return SendToRegion(region, NetMessagePvEInstanceDeathUpdate.CreateBuilder()
                .SetMetaGameId(metaGameId).SetCurrentDeathCount((ulong)Math.Max(deaths, 0)).Build());
        }

        public static int CrystalCounter(Region region, int current, int max)
        {
            ulong metaGameId = GetMetaGameId(region);
            if (metaGameId == 0) return 0;

            return SendToRegion(region, NetMessagePvEInstanceCrystalUpdate.CreateBuilder()
                .SetMetaGameId(metaGameId).SetCurrentCrystalCount((ulong)Math.Max(current, 0)).SetMaxCrystalCount((ulong)Math.Max(max, 0)).Build());
        }

        #endregion

        #region Tracked entity, cutscenes, movies, sounds

        /// <summary>
        /// Points the player's HUD at an entity (the game mode "tracked entity", e.g. a boss). 0 clears it.
        /// </summary>
        public static bool TrackEntity(Player player, ulong entityId)
        {
            return Send(player, NetMessageSetUITrackedEntityId.CreateBuilder().SetEntityId(entityId).Build());
        }

        public static int TrackEntityForRegion(Region region, ulong entityId)
        {
            return SendToRegion(region, NetMessageSetUITrackedEntityId.CreateBuilder().SetEntityId(entityId).Build());
        }

        /// <summary>
        /// Plays a cutscene (Kismet sequence prototype, e.g. "KismetSequences/DoomEntrance.prototype").
        /// </summary>
        public static bool PlayCutscene(Player player, string kismetPath)
        {
            PrototypeId kismetRef = ResolvePrototype(kismetPath);
            if (kismetRef == PrototypeId.Invalid)
                return Logger.WarnReturn(false, $"PlayCutscene(): unknown prototype [{kismetPath}]");

            return Send(player, NetMessagePlayKismetSeq.CreateBuilder().SetKismetSeqPrototypeId((ulong)kismetRef).Build());
        }

        public static int PlayCutsceneToRegion(Region region, string kismetPath)
        {
            PrototypeId kismetRef = ResolvePrototype(kismetPath);
            if (kismetRef == PrototypeId.Invalid)
                return Logger.WarnReturn(0, $"PlayCutsceneToRegion(): unknown prototype [{kismetPath}]");

            return SendToRegion(region, NetMessagePlayKismetSeq.CreateBuilder().SetKismetSeqPrototypeId((ulong)kismetRef).Build());
        }

        /// <summary>
        /// Queues a fullscreen movie (e.g. "FullscreenMovies/Cinematics/HeroesTriumphant.prototype").
        /// </summary>
        public static bool PlayMovie(Player player, string moviePath)
        {
            PrototypeId movieRef = ResolvePrototype(moviePath);
            if (movieRef == PrototypeId.Invalid)
                return Logger.WarnReturn(false, $"PlayMovie(): unknown prototype [{moviePath}]");

            return Send(player, NetMessageQueueFullscreenMovie.CreateBuilder().SetMoviePrototypeId((ulong)movieRef).Build());
        }

        /// <summary>
        /// Plays a Wwise audio event on an entity for nearby players. <paramref name="akEvent"/> is the event name (hashed the
        /// Wwise way) or a numeric id.
        /// </summary>
        public static int PlaySound(WorldEntity entity, string akEvent, bool isVoice = false)
        {
            if (entity == null || entity.Region == null)
                return 0;

            uint akEventId = uint.TryParse(akEvent, out uint numericId) ? numericId : WwiseHash(akEvent);
            var message = NetMessageRecvAkEventFromEntity.CreateBuilder()
                .SetAkEventId(akEventId)
                .SetEntityId(entity.Id)
                .SetIsVO(isVoice)
                .SetEventType(0)
                .Build();

            return SendToRegion(entity.Region, message);
        }

        /// <summary>
        /// Wwise ids are the 32-bit FNV-1 hash of the lowercase event name.
        /// </summary>
        public static uint WwiseHash(string name)
        {
            uint hash = 2166136261;
            foreach (byte b in Encoding.ASCII.GetBytes(name.ToLowerInvariant()))
            {
                hash *= 16777619;
                hash ^= b;
            }
            return hash;
        }

        #endregion

        #region Tutorials, notifications, global events, teams, primitives

        /// <summary>
        /// Shows a HUD tutorial popup (Tutorial/HUDTutorials/...).
        /// </summary>
        public static bool HudTutorial(Player player, string tutorialPath)
        {
            PrototypeId tutorialRef = ResolvePrototype(tutorialPath);
            if (tutorialRef == PrototypeId.Invalid)
                return Logger.WarnReturn(false, $"HudTutorial(): unknown prototype [{tutorialPath}]");

            return Send(player, NetMessageHUDTutorial.CreateBuilder().SetHudTutorialProtoId((ulong)tutorialRef).Build());
        }

        /// <summary>
        /// Shows a UI notification prototype (UI/UINotifications/...).
        /// </summary>
        public static bool UINotification(Player player, string notificationPath)
        {
            PrototypeId notificationRef = ResolvePrototype(notificationPath);
            if (notificationRef == PrototypeId.Invalid)
                return Logger.WarnReturn(false, $"UINotification(): unknown prototype [{notificationPath}]");

            return Send(player, NetMessageUINotificationMessage.CreateBuilder().SetUiNotificationRef((ulong)notificationRef).Build());
        }

        /// <summary>
        /// Updates a global event's progress (0-1) for the player (Events/GlobalEvents/Events/...).
        /// </summary>
        public static bool GlobalEventProgress(Player player, string eventPath, float progress)
        {
            PrototypeId eventRef = ResolvePrototype(eventPath);
            if (eventRef == PrototypeId.Invalid)
                return Logger.WarnReturn(false, $"GlobalEventProgress(): unknown prototype [{eventPath}]");

            return Send(player, NetMessageGlobalEventDataUpdate.CreateBuilder().SetEventId((ulong)eventRef).SetTotalProgress(Math.Clamp(progress, 0f, 1f)).Build());
        }

        /// <summary>
        /// Sends a global event leaderboard (top player names) for the player.
        /// </summary>
        public static bool GlobalEventLeaderboard(Player player, string eventPath, IEnumerable<string> names)
        {
            PrototypeId eventRef = ResolvePrototype(eventPath);
            if (eventRef == PrototypeId.Invalid)
                return false;

            List<string> nameList = names.ToList();
            var builder = NetMessageGlobalEventLeaderboardUpdate.CreateBuilder().SetEventId((ulong)eventRef).SetLeaderboardLength((uint)nameList.Count);
            foreach (string name in nameList)
                builder.AddPlayerNames(name);

            return Send(player, builder.Build());
        }

        /// <summary>
        /// Opens the public event team selection dialog (Events/PublicEvents/Events/..., e.g. the Civil War event).
        /// </summary>
        public static bool TeamSelectDialog(Player player, string publicEventPath)
        {
            PrototypeId eventRef = ResolvePrototype(publicEventPath);
            if (eventRef == PrototypeId.Invalid)
                return Logger.WarnReturn(false, $"TeamSelectDialog(): unknown prototype [{publicEventPath}]");

            return Send(player, NetMessageTeamSelectDialog.CreateBuilder().SetPublicEventProtoId((ulong)eventRef).Build());
        }

        public static bool TeamAssigned(Player player, string publicEventPath, bool success = true)
        {
            PrototypeId eventRef = ResolvePrototype(publicEventPath);
            if (eventRef == PrototypeId.Invalid)
                return false;

            return Send(player, NetMessagePublicEventTeamAssigned.CreateBuilder().SetPublicEventProtoId((ulong)eventRef).SetSuccess(success).Build());
        }

        /// <summary>
        /// Draws a circle on the ground (a debug primitive; may only be drawn by debug-enabled clients). Color is RGB 0-255.
        /// </summary>
        public static int DrawCircle(Region region, Vector3 center, float radius, int r, int g, int b, int lifetimeMs = 10000)
        {
            var message = NetMessageRegionPrimitiveCircle.CreateBuilder()
                .SetCenter(center.ToNetStructPoint3())
                .SetRadius(radius)
                .SetColor(NetStructIPoint3.CreateBuilder().SetX((uint)Math.Clamp(r, 0, 255)).SetY((uint)Math.Clamp(g, 0, 255)).SetZ((uint)Math.Clamp(b, 0, 255)).Build())
                .SetLifetimeInMilliseconds((ulong)lifetimeMs)
                .SetAdd(true)
                .Build();

            return SendToRegion(region, message);
        }

        #endregion

        #region HUD widgets (UI/MetaGame/..., the same system as ScriptText.SetObjectiveTitle / SetObjectiveCounter)

        // Widgets live in the region's UI data provider and are synced to everyone in the region, in any region type.
        // Their label is the widget prototype's Descriptor text; OverrideWidgetLabel replaces it (for every use of that widget).

        /// <summary>
        /// Shows a countdown widget (e.g. "UI/MetaGame/TimerCenter.prototype", "UI/MetaGame/TimeRemaining.prototype").
        /// </summary>
        public static bool WidgetTimer(Region region, string widgetPath, float seconds)
        {
            UIWidgetGenericFraction widget = GetRegionWidget<UIWidgetGenericFraction>(region, widgetPath);
            if (widget == null)
                return false;

            widget.SetAreaContext(region.PrototypeDataRef);
            widget.SetTimeRemaining((long)(seconds * 1000f));
            return true;
        }

        /// <summary>
        /// Shows a current / total widget (e.g. "UI/MetaGame/WaveComplete.prototype", "UI/MetaGame/Targets.prototype").
        /// </summary>
        public static bool WidgetCounter(Region region, string widgetPath, int current, int total)
        {
            UIWidgetGenericFraction widget = GetRegionWidget<UIWidgetGenericFraction>(region, widgetPath);
            if (widget == null)
                return false;

            widget.SetAreaContext(region.PrototypeDataRef);
            widget.SetCount(Math.Max(current, 0), Math.Max(total, 0));
            return true;
        }

        /// <summary>
        /// Adds a clickable HUD button widget (e.g. "UI/MetaGame/DangerRoom/TeleportButtonWidget.prototype") for
        /// <paramref name="player"/>. <paramref name="onClick"/> gets the player and the result the client sent.
        /// </summary>
        public static bool WidgetButton(Region region, string widgetPath, Player player, Action<Player, bool> onClick)
        {
            UIWidgetButton widget = GetRegionWidget<UIWidgetButton>(region, widgetPath);
            if (widget == null || player == null)
                return false;

            Game game = region.Game;
            widget.SetAreaContext(region.PrototypeDataRef);
            widget.AddCallback(player.DatabaseUniqueId, (playerGuid, result) =>
            {
                Player clicker = game.EntityManager.GetEntityByDbGuid<Player>(playerGuid);
                if (clicker == null || onClick == null)
                    return;

                try { onClick(clicker, result); }
                catch (Exception e) { Logger.Error($"WidgetButton(): callback failed: {e}"); }
            });
            return true;
        }

        /// <summary>
        /// Shows / updates a player's line in a ready check widget ("UI/MetaGame/ReadyCheck.prototype").
        /// </summary>
        public static bool WidgetReadyCheck(Region region, string widgetPath, Player player, bool ready)
        {
            UIWidgetReadyCheck widget = GetRegionWidget<UIWidgetReadyCheck>(region, widgetPath);
            if (widget == null || player == null)
                return false;

            widget.SetAreaContext(region.PrototypeDataRef);
            widget.SetPlayerState(player.DatabaseUniqueId, player.GetName(), ready ? PlayerState.Ready : PlayerState.Pending);
            return true;
        }

        /// <summary>
        /// Shows <paramref name="entity"/>'s health on a health bar widget (a UIWidgetEntityIcons widget, e.g.
        /// "UI/MetaGame/SurturRaid/FiveMan/SlagHealth.prototype": generic boss icon + health percent). The widget's own entity
        /// filter is ignored, so any boss works. Call again for more bosses on the same widget; <paramref name="entryIndex"/>
        /// picks the widget's entry (most have one). Remove the entity (or clear the widget) before it despawns.
        /// </summary>
        public static bool WidgetTrackHealth(Region region, string widgetPath, WorldEntity entity, int entryIndex = 0)
        {
            UIWidgetEntityIconsSyncData widget = GetRegionWidget<UIWidgetEntityIconsSyncData>(region, widgetPath);
            if (widget == null || entity == null)
                return false;

            widget.SetAreaContext(region.PrototypeDataRef);
            return widget.AddScriptEntity(entity, entryIndex);
        }

        /// <summary>
        /// Shows an entity added with <see cref="WidgetTrackHealth"/> as defeated (call it from your kill handler).
        /// </summary>
        public static void WidgetEntityDefeated(Region region, string widgetPath, ulong entityId)
        {
            FindRegionWidget<UIWidgetEntityIconsSyncData>(region, widgetPath)?.SetScriptEntityDead(entityId);
        }

        /// <summary>
        /// Removes an entity added with <see cref="WidgetTrackHealth"/> from the widget.
        /// </summary>
        public static bool WidgetUntrack(Region region, string widgetPath, ulong entityId)
        {
            return FindRegionWidget<UIWidgetEntityIconsSyncData>(region, widgetPath)?.RemoveScriptEntity(entityId) == true;
        }

        /// <summary>
        /// Removes a widget from the region's HUD.
        /// </summary>
        public static void ClearWidget(Region region, string widgetPath)
        {
            PrototypeId widgetRef = ResolvePrototype(widgetPath);
            if (region?.UIDataProvider == null || widgetRef == PrototypeId.Invalid)
                return;

            region.UIDataProvider.DeleteWidget(widgetRef, region.PrototypeDataRef);
        }

        /// <summary>
        /// Replaces a widget's label (its Descriptor text) for everyone, everywhere that widget is used. Shows after a reconnect.
        /// </summary>
        public static bool OverrideWidgetLabel(string widgetPath, string text)
        {
            MetaGameDataPrototype widgetProto = GameDatabase.GetPrototype<MetaGameDataPrototype>(ResolvePrototype(widgetPath));
            if (widgetProto == null || widgetProto.Descriptor == LocaleStringId.Blank)
                return Logger.WarnReturn(false, $"OverrideWidgetLabel(): [{widgetPath}] is not a widget with a label");

            return ScriptText.OverrideText(widgetProto.Descriptor, text);
        }

        private static T GetRegionWidget<T>(Region region, string widgetPath) where T : UISyncData
        {
            if (region?.UIDataProvider == null)
                return null;

            PrototypeId widgetRef = ResolvePrototype(widgetPath);
            if (widgetRef == PrototypeId.Invalid)
                return Logger.WarnReturn<T>(null, $"GetRegionWidget(): unknown widget [{widgetPath}]");

            T widget = region.UIDataProvider.GetWidget<T>(widgetRef, region.PrototypeDataRef);
            if (widget == null)
                Logger.Warn($"GetRegionWidget(): [{widgetPath}] is not a {typeof(T).Name} widget");

            return widget;
        }

        // Like GetRegionWidget, but never creates the widget (for removals / updates of a widget that may already be gone)
        private static T FindRegionWidget<T>(Region region, string widgetPath) where T : UISyncData
        {
            PrototypeId widgetRef = ResolvePrototype(widgetPath);
            if (region?.UIDataProvider == null || widgetRef == PrototypeId.Invalid)
                return null;

            return region.UIDataProvider.FindWidget<T>(widgetRef, region.PrototypeDataRef);
        }

        #endregion

        #region Helpers

        /// <summary>
        /// The id of the region's first metagame entity (the HUD timer and counters belong to it), or 0.
        /// </summary>
        public static ulong GetMetaGameId(Region region)
        {
            if (region == null || region.MetaGames.Count == 0)
                return 0;

            return region.MetaGames[0];
        }

        private static PrototypeId ResolvePrototype(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return PrototypeId.Invalid;

            if (ulong.TryParse(path, out ulong numericId))
                return (PrototypeId)numericId;

            return GameDatabase.GetPrototypeRefByName(path);
        }

        private static bool Send(Player player, IMessage message)
        {
            if (player == null || message == null)
                return false;

            player.SendMessage(message);
            return true;
        }

        private static int SendToRegion(Region region, IMessage message)
        {
            if (region == null || message == null)
                return 0;

            int count = 0;
            foreach (Player player in new PlayerIterator(region))
            {
                player.SendMessage(message);
                count++;
            }

            return count;
        }

        #endregion
    }
}
