using System.Collections.Concurrent;
using Gazillion;
using MHServerEmu.Core.Logging;
using MHServerEmu.Games.Achievements;
using MHServerEmu.Games.Entities;
using MHServerEmu.Games.GameData;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Locales;
using MHServerEmu.Games.Regions;
using MHServerEmu.Games.UI.Widgets;

namespace MHServerEmu.Games.Scripting
{
    /// <summary>
    /// On-screen text for scripts. Banners can only show localized strings the client already knows, and the client learns
    /// custom strings from the achievement database dump it receives when connecting. Scripts register text under a key,
    /// it gets a stable string id, and is added to that dump.
    /// </summary>
    /// <remarks>
    /// Register text when a script loads (scripts load before players can connect). Text registered or changed later
    /// (hot reload) only reaches players the next time they connect. Banners have no format arguments, so text with a
    /// number in it is registered as a range: one string per number.
    /// </remarks>
    public static class ScriptText
    {
        private static readonly Logger Logger = LogManager.CreateLogger();

        // Custom id space: top byte 0xFB, 36 bits from the key hash, 20 bits for the number (0 - 1048575).
        // Stable across restarts and reloads, so a client that connected earlier still resolves the same ids.
        private const ulong IdSpacePrefix = 0xFB00000000000000UL;
        private const int NumberBits = 20;
        public const int MaxNumber = (1 << NumberBits) - 1;
        public const int MaxRangeSize = 100000;

        private static readonly ConcurrentDictionary<string, (int From, int To)> _registeredKeys = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers a single piece of text under <paramref name="key"/>. The text is stored as is ("{0}" is not replaced).
        /// </summary>
        public static bool Register(string key, string text)
        {
            return RegisterInternal(key, text, 0, 0, false);
        }

        /// <summary>
        /// Registers one string per number from <paramref name="from"/> to <paramref name="to"/> (inclusive).
        /// Every "{0}" in <paramref name="template"/> is replaced with the number.
        /// </summary>
        public static bool RegisterRange(string key, string template, int from, int to)
        {
            return RegisterInternal(key, template, from, to, true);
        }

        private static bool RegisterInternal(string key, string template, int from, int to, bool replaceNumber)
        {
            if (string.IsNullOrWhiteSpace(key) || template == null)
                return Logger.WarnReturn(false, "RegisterRange(): Invalid key or text");

            if (from < 0 || to > MaxNumber || from > to)
                return Logger.WarnReturn(false, $"RegisterRange(): Invalid range {from}-{to} for [{key}], numbers must be 0-{MaxNumber}");

            if (to - from + 1 > MaxRangeSize)
                return Logger.WarnReturn(false, $"RegisterRange(): Range {from}-{to} for [{key}] is larger than {MaxRangeSize}");

            AchievementDatabase database = AchievementDatabase.Instance;
            for (int number = from; number <= to; number++)
                database.SetCustomString(GetId(key, number), replaceNumber ? template.Replace("{0}", number.ToString()) : template);

            _registeredKeys.AddOrUpdate(key, (from, to), (k, existing) => (Math.Min(existing.From, from), Math.Max(existing.To, to)));
            return true;
        }

        /// <summary>
        /// Replaces the text of an existing game string for every client (same mechanism as custom text, so it shows after a reconnect).
        /// Everything that uses <paramref name="id"/> changes, so only override strings that belong to one thing.
        /// </summary>
        public static bool OverrideText(LocaleStringId id, string text)
        {
            if (id == LocaleStringId.Invalid || id == LocaleStringId.Blank || text == null)
                return Logger.WarnReturn(false, "OverrideText(): Invalid string id or text");

            AchievementDatabase.Instance.SetCustomString(id, text);
            return true;
        }

        /// <summary>
        /// Replaces the italic flavor text at the bottom of an item's tooltip. Returns <see langword="false"/> if the item
        /// does not exist or has no flavor text of its own.
        /// </summary>
        public static bool OverrideItemFlavorText(string itemPath, string text)
        {
            ItemPrototype itemProto = GameDatabase.GetPrototypeRefByName(itemPath).As<ItemPrototype>();
            if (itemProto == null)
                return Logger.WarnReturn(false, $"OverrideItemFlavorText(): [{itemPath}] is not an item");

            if (itemProto.TooltipFlavorText == LocaleStringId.Invalid || itemProto.TooltipFlavorText == LocaleStringId.Blank)
                return Logger.WarnReturn(false, $"OverrideItemFlavorText(): [{itemPath}] has no flavor text to replace");

            return OverrideText(itemProto.TooltipFlavorText, text);
        }

        /// <summary>
        /// Returns <see langword="true"/> if text for <paramref name="key"/> (and <paramref name="number"/>) was registered.
        /// </summary>
        public static bool IsRegistered(string key, int number = 0)
        {
            return _registeredKeys.TryGetValue(key, out var range) && number >= range.From && number <= range.To;
        }

        /// <summary>
        /// Returns the string id for <paramref name="key"/> and <paramref name="number"/>.
        /// </summary>
        public static LocaleStringId GetId(string key, int number = 0)
        {
            // FNV-1a over the lowercase key, folded to 36 bits (36 + 20 number bits = 56, below the 0xFB prefix byte)
            ulong hash = 14695981039346656037UL;
            foreach (char c in key.ToLowerInvariant())
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }

            ulong keyBits = (hash ^ (hash >> 36)) & 0xFFFFFFFFFUL;
            return (LocaleStringId)(IdSpacePrefix | (keyBits << NumberBits) | ((ulong)number & MaxNumber));
        }

        /// <summary>
        /// Shows registered text as an on-screen banner to <paramref name="player"/>.
        /// </summary>
        /// <param name="style">large (default), standard, reward, rewardlarge, alert, error, unlock</param>
        public static bool ShowBanner(Player player, string key, int number = 0, string style = "large", int durationMS = 3000)
        {
            if (player == null)
                return false;

            if (IsRegistered(key, number) == false)
                return Logger.WarnReturn(false, $"ShowBanner(): No text registered for [{key}] #{number}");

            PrototypeId textStyle = ResolveTextStyle(style);
            BannerMessageStyle messageStyle = textStyle == TextStylePrototype.BannerMessageStandard ? BannerMessageStyle.Standard : BannerMessageStyle.FlyIn;

            return player.SendBannerMessage(GetId(key, number), textStyle, Math.Max(durationMS, 500), messageStyle, doNotQueue: true, showImmediately: true);
        }

        /// <summary>
        /// Shows registered text as an on-screen banner to every player in <paramref name="region"/>. Returns the number of players shown.
        /// </summary>
        public static int ShowBannerToRegion(Region region, string key, int number = 0, string style = "large", int durationMS = 3000)
        {
            if (region == null)
                return 0;

            if (IsRegistered(key, number) == false)
                return Logger.WarnReturn(0, $"ShowBannerToRegion(): No text registered for [{key}] #{number}");

            int shown = 0;
            foreach (Player player in new PlayerIterator(region))
            {
                if (ShowBanner(player, key, number, style, durationMS))
                    shown++;
            }

            return shown;
        }

        /// <summary>
        /// Shows registered text as a metagame banner (the kind PvP uses for "X defeated Y"). Unlike normal banners these carry
        /// up to two player names and some numbers as live arguments, which the client fills into placeholders in the text.
        /// </summary>
        /// <remarks>
        /// Placeholders (client syntax, from the PvP banner strings): $playersource$ = <paramref name="playerName1"/>,
        /// $playertarget$ = <paramref name="playerName2"/>, $intargzero$ / $intargone$ = the first / second of <paramref name="intArgs"/>.
        /// </remarks>
        public static bool ShowPlayerBanner(Player player, string key, int number, string playerName1, string playerName2 = "", long[] intArgs = null)
        {
            if (player == null)
                return false;

            if (IsRegistered(key, number) == false)
                return Logger.WarnReturn(false, $"ShowPlayerBanner(): No text registered for [{key}] #{number}");

            var message = NetMessageMetaGameBanner.CreateBuilder()
                .SetMessageStringId((ulong)GetId(key, number))
                .SetPlayerName1(playerName1 ?? string.Empty)
                .SetPlayerName2(playerName2 ?? string.Empty);

            if (intArgs != null)
                message.AddRangeIntArgs(intArgs);

            player.SendMessage(message.Build());
            return true;
        }

        /// <summary>
        /// <see cref="ShowPlayerBanner"/> for every player in <paramref name="region"/>. Returns the number of players shown.
        /// </summary>
        public static int ShowPlayerBannerToRegion(Region region, string key, int number, string playerName1, string playerName2 = "", long[] intArgs = null)
        {
            if (region == null)
                return 0;

            int shown = 0;
            foreach (Player player in new PlayerIterator(region))
            {
                if (ShowPlayerBanner(player, key, number, playerName1, playerName2, intArgs))
                    shown++;
            }

            return shown;
        }

        // Region id => game time when the portrait notifications shown there should be cleared
        private static readonly ConcurrentDictionary<ulong, TimeSpan> _portraitClearTimes = new();

        /// <summary>
        /// Shows a notification with <paramref name="entityRef"/>'s portrait and registered text (the popup metagame events use
        /// to announce a villain) to every player in <paramref name="region"/>. Returns the number of players shown.
        /// </summary>
        /// <remarks>
        /// The client keeps these until the server clears them (and a clear removes all of them), so they are cleared
        /// <paramref name="durationSeconds"/> after the most recent one shown in the region.
        /// </remarks>
        public static int ShowPortraitNotificationToRegion(Region region, PrototypeId entityRef, string key, int number = 0, float durationSeconds = 8f)
        {
            if (region == null)
                return 0;

            if (IsRegistered(key, number) == false)
                return Logger.WarnReturn(0, $"ShowPortraitNotificationToRegion(): No text registered for [{key}] #{number}");

            WorldEntityPrototype entityProto = entityRef.As<WorldEntityPrototype>();
            if (entityProto == null)
                return Logger.WarnReturn(0, $"ShowPortraitNotificationToRegion(): [{entityRef}] is not a world entity");

            var message = NetMessageMetaGameInfoNotification.CreateBuilder()
                .SetEntityPrototypeId((ulong)entityProto.DataRef)
                .SetDialogTextStringId((ulong)GetId(key, number))
                .SetIconPathOverrideId((ulong)entityProto.IconPath)
                .Build();

            int shown = 0;
            foreach (Player player in new PlayerIterator(region))
            {
                player.SendMessage(message);
                shown++;
            }

            ScheduleClearPortraitNotifications(region, durationSeconds);
            return shown;
        }

        /// <summary>
        /// Removes every portrait notification shown to players in <paramref name="region"/>.
        /// </summary>
        public static void ClearPortraitNotifications(Region region)
        {
            if (region == null)
                return;

            _portraitClearTimes.TryRemove(region.Id, out _);

            foreach (Player player in new PlayerIterator(region))
                player.SendMessage(NetMessageClearMetaGameInfoNotification.DefaultInstance);
        }

        private static void ScheduleClearPortraitNotifications(Region region, float durationSeconds)
        {
            Game game = region.Game;
            if (game == null || durationSeconds <= 0f)
                return;

            ulong regionId = region.Id;
            TimeSpan clearAt = game.CurrentTime + TimeSpan.FromSeconds(durationSeconds);
            _portraitClearTimes[regionId] = clearAt;

            ScriptTimer.After(game, durationSeconds, () =>
            {
                // A newer notification pushed the clear time back: its own timer will clear them
                if (_portraitClearTimes.TryGetValue(regionId, out TimeSpan latest) == false || game.CurrentTime < latest)
                    return;

                Region current = game.RegionManager.GetRegion(regionId);
                if (current == null)
                {
                    _portraitClearTimes.TryRemove(regionId, out _);
                    return;
                }

                ClearPortraitNotifications(current);
            });
        }

        /// <summary>
        /// Shows registered text floating over <paramref name="entity"/>'s head for players near it.
        /// </summary>
        public static bool ShowOverheadText(WorldEntity entity, string key, int number = 0, float durationSeconds = 4f)
        {
            if (entity == null || entity.IsInWorld == false)
                return false;

            if (IsRegistered(key, number) == false)
                return Logger.WarnReturn(false, $"ShowOverheadText(): No text registered for [{key}] #{number}");

            entity.ShowOverheadText(GetId(key, number), durationSeconds);
            return true;
        }

        #region Objective Widgets

        // The same HUD widgets Endless Danger Room uses for its wave title and defeated / required counter bar.
        // They belong to the region, so everyone in it sees them.
        private const string ObjectiveTitleWidgetName = "UI/MetaGame/MissionName.prototype";
        private const string ObjectiveCounterWidgetName = "UI/MetaGame/DangerRoom/DangerRoomCounterBarBASE.prototype";

        private static PrototypeId _objectiveTitleWidgetRef;
        private static PrototypeId _objectiveCounterWidgetRef;

        /// <summary>
        /// Shows registered text in the objective title widget of <paramref name="region"/> (the "Wave N" line in Danger Room).
        /// </summary>
        public static bool SetObjectiveTitle(Region region, string key, int number = 0)
        {
            if (IsRegistered(key, number) == false)
                return Logger.WarnReturn(false, $"SetObjectiveTitle(): No text registered for [{key}] #{number}");

            if (TryGetObjectiveWidgets(region, out PrototypeId titleRef, out _) == false)
                return false;

            PrototypeId contextRef = region.PrototypeDataRef;
            UIWidgetMissionText widget = region.UIDataProvider.GetWidget<UIWidgetMissionText>(titleRef, contextRef);
            if (widget == null)
                return false;

            widget.SetAreaContext(contextRef);
            widget.SetText(GetId(key, number), LocaleStringId.Blank);
            return true;
        }

        /// <summary>
        /// Shows a <paramref name="current"/> / <paramref name="total"/> counter bar in <paramref name="region"/>.
        /// </summary>
        public static bool SetObjectiveCounter(Region region, int current, int total)
        {
            if (TryGetObjectiveWidgets(region, out _, out PrototypeId counterRef) == false)
                return false;

            PrototypeId contextRef = region.PrototypeDataRef;
            UIWidgetGenericFraction widget = region.UIDataProvider.GetWidget<UIWidgetGenericFraction>(counterRef, contextRef);
            if (widget == null)
                return false;

            total = Math.Max(total, 1);
            widget.SetAreaContext(contextRef);
            widget.SetCount(Math.Clamp(current, 0, total), total);
            return true;
        }

        /// <summary>
        /// Removes the objective title and counter widgets from <paramref name="region"/>.
        /// </summary>
        public static void ClearObjective(Region region)
        {
            if (TryGetObjectiveWidgets(region, out PrototypeId titleRef, out PrototypeId counterRef) == false)
                return;

            region.UIDataProvider.DeleteWidget(titleRef, region.PrototypeDataRef);
            region.UIDataProvider.DeleteWidget(counterRef, region.PrototypeDataRef);
        }

        private static bool TryGetObjectiveWidgets(Region region, out PrototypeId titleRef, out PrototypeId counterRef)
        {
            if (_objectiveTitleWidgetRef == PrototypeId.Invalid)
                _objectiveTitleWidgetRef = GameDatabase.GetPrototypeRefByName(ObjectiveTitleWidgetName);

            if (_objectiveCounterWidgetRef == PrototypeId.Invalid)
                _objectiveCounterWidgetRef = GameDatabase.GetPrototypeRefByName(ObjectiveCounterWidgetName);

            titleRef = _objectiveTitleWidgetRef;
            counterRef = _objectiveCounterWidgetRef;

            if (region?.UIDataProvider == null)
                return false;

            return GameDatabase.GetPrototype<MetaGameDataPrototype>(titleRef) is UIWidgetMissionTextPrototype
                && GameDatabase.GetPrototype<MetaGameDataPrototype>(counterRef) is UIWidgetGenericFractionPrototype;
        }

        #endregion

        private static PrototypeId ResolveTextStyle(string style)
        {
            return style?.ToLowerInvariant() switch
            {
                "standard"      => TextStylePrototype.BannerMessageStandard,
                "reward"        => TextStylePrototype.BannerMessageReward,
                "rewardlarge"   => TextStylePrototype.BannerMessageRewardLarge,
                "alert"         => TextStylePrototype.BannerMessageAlert,
                "error"         => TextStylePrototype.BannerMessageErrorLarge,
                "unlock"        => TextStylePrototype.BannerMessageUnlock,
                _               => TextStylePrototype.BannerMessageLarge,
            };
        }
    }
}
