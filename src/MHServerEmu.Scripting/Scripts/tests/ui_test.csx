// Admin test commands for client presentation messages (ScriptPresentation). Each one sends a single message to you so you
// can see whether the client shows it here. Try them in a few places (hub, patrol zone, terminal): anything tied to the
// "metagame" (timer, counters, mode text) depends on the region's game mode.
//
//   !uitest list                 everything below
//   !uitest all                  the safe ones one after another (4 seconds apart); not cutscene / movie / teams
//   !uitest story [speaker]      portrait + text in the corner (speaker = prototype path, default Nick Fury)
//   !uitest timer [seconds]      HUD countdown with a custom label          !uitest timerstop
//   !uitest mode | timertext | extratext   metagame HUD texts
//   !uitest wave | score | deaths | crystals   metagame counters
//   !uitest track                spawn a Doombot and point the HUD at it    !uitest track clear
//   !uitest cutscene [name|path] doom, doomdeath, doom2, doom3, skrullship, superskrull, ultron, onslaught (or a full path)
//   !uitest movie [path]         fullscreen movie (default Heroes Triumphant)
//   !uitest sound <event|id>     Wwise audio event on your hero
//   !uitest tutorial [path]      HUD tutorial popup
//   !uitest notify [path]        UI notification prototype
//   !uitest global [0-1]         global event progress bar (BiFrost event) + a leaderboard with your name
//   !uitest teams                Civil War team selection dialog
//   !uitest circle               debug circle on the ground around you (probably debug clients only)
//
// HUD widgets (the system the boss rush title / counter bar use, which works in Midtown):
//   !uitest wtimer [seconds] [widget]     countdown widget (default UI/MetaGame/TimerCenter.prototype, 90 s)
//   !uitest wcounter [widget]             3 / 10 counter widget (default UI/MetaGame/WaveComplete.prototype)
//   !uitest wbutton                       clickable HUD button widget (Danger Room teleport button)
//   !uitest wready                        ready check widget with your name (run twice to toggle ready)
//   !uitest whealth [widget] [entry]      spawn a Doombot and show its health on a boss health bar (default bosshp);
//                                         run again to add another Doombot to the same bar
//   !uitest wclear                        remove the test widgets (and the test Doombots)
//   widget = a UI/MetaGame/... prototype path, or a short name: timer, timeleft, timeremaining, drtimer, ultrontimer,
//            wave, targets, enemies, hostages, bosshp, bosshp2, bosshp3, enrage
//
// Text registered here shows after a reconnect.

using MHServerEmu.Games.Entities.Avatars;

ScriptText.Register("uitest_story", "Trust no one. The Skrulls could be anyone... even me.");
ScriptText.Register("uitest_timer", "Skrull Invasion In");
ScriptText.Register("uitest_mode", "Secret Invasion");
ScriptText.Register("uitest_timertext", "Timer text test");
ScriptText.Register("uitest_extratext", "Extra text test");

const string DefaultSpeaker  = "Entity/Characters/NPCs/HubNPCs/NickFury.prototype";
const string TrackMob        = "Entity/Characters/Mobs/Doombots/Patrol/PatrolDoombotFlyer.prototype";
const string DefaultMovie    = "FullscreenMovies/Cinematics/HeroesTriumphant.prototype";
const string DefaultTutorial = "Tutorial/HUDTutorials/LegendaryQuests.prototype";
const string DefaultNotify   = "UI/UINotifications/BannerMessages/MissionAcceptedMessage.prototype";
const string GlobalEvent     = "Events/GlobalEvents/Events/BiFrostUnlock/BifrostUnlock.prototype";
const string CivilWarEvent   = "Events/PublicEvents/Events/CivilWar.prototype";

var Cutscenes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["doom"]        = "KismetSequences/DoomEntrance.prototype",
    ["doomdeath"]   = "KismetSequences/DoomDeath.prototype",
    ["doom2"]       = "KismetSequences/DoomTransformPhase2.prototype",
    ["doom3"]       = "KismetSequences/DoomTransformPhase3.prototype",
    ["skrullship"]  = "KismetSequences/CH10SecretInvasion/SInvSkrullShipDecloak.prototype",
    ["superskrull"] = "KismetSequences/CH10SecretInvasion/SuperSkrullEntrance.prototype",
    ["ultron"]      = "KismetSequences/EndgamePVE/AgeOfUltronUltronEntrance.prototype",
    ["onslaught"]   = "KismetSequences/EndgamePVE/AxisRaidOnslaughtEntrance.prototype",
};

var Widgets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["timer"]         = "UI/MetaGame/TimerCenter.prototype",
    ["timeleft"]      = "UI/MetaGame/TimerLeft.prototype",
    ["timeremaining"] = "UI/MetaGame/TimeRemaining.prototype",
    ["drtimer"]       = "UI/MetaGame/DangerRoom/DangerRoomTimer.prototype",
    ["ultrontimer"]   = "UI/MetaGame/UltronRaid/AgeOfUltronTimer.prototype",
    ["wave"]          = "UI/MetaGame/WaveComplete.prototype",
    ["targets"]       = "UI/MetaGame/Targets.prototype",
    ["enemies"]       = "UI/MetaGame/CurrentCountEnemiesRemaining.prototype",
    ["hostages"]      = "UI/MetaGame/HostageCount.prototype",
    // Boss health bars (UIWidgetEntityIcons): generic boss icon + health %
    ["bosshp"]        = "UI/MetaGame/SurturRaid/FiveMan/SlagHealth.prototype",
    ["bosshp2"]       = "UI/MetaGame/SurturRaid/FiveMan/SurturFightSurturHealth.prototype",
    ["bosshp3"]       = "UI/MetaGame/GenoshaRaid/P1Sentinels/P1StarkTechSentinelA.prototype",
    ["enrage"]        = "UI/MetaGame/SurturRaid/FiveMan/SlagEnrageTimer.prototype",
};
const string ButtonWidget = "UI/MetaGame/DangerRoom/TeleportButtonWidget.prototype";
const string ReadyWidget  = "UI/MetaGame/ReadyCheck.prototype";

string WidgetPath(string nameOrPath, string fallback)
{
    if (string.IsNullOrEmpty(nameOrPath)) return Widgets[fallback];
    return Widgets.TryGetValue(nameOrPath, out string path) ? path : nameOrPath;
}

var usedWidgets = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
var readyByPlayer = new System.Collections.Concurrent.ConcurrentDictionary<ulong, bool>();

var trackedByPlayer = new System.Collections.Concurrent.ConcurrentDictionary<ulong, ulong>();
var healthTestMobs = new System.Collections.Concurrent.ConcurrentDictionary<ulong, string>();   // mob id => health widget path

// A health bar widget shows a dead test mob as 0 % / defeated
Hooks.On(ScriptHooks.EntityKilled, e =>
{
    if (healthTestMobs.IsEmpty || healthTestMobs.TryGetValue(e.VictimId, out string path) == false)
        return;

    Region region = e.Victim.Region;
    if (region != null)
        ScriptPresentation.WidgetEntityDefeated(region, path, e.VictimId);
});

string Run(string test, ChatCommandArgs e, Avatar avatar, Region region)
{
    Player player = e.Player;
    string arg = e.GetArg(1);
    string meta = ScriptPresentation.GetMetaGameId(region) != 0 ? "" : " (this region has no metagame, so nothing can show)";

    switch (test)
    {
        case "story":
            return ScriptPresentation.StoryNotification(player, "uitest_story", arg.Length > 0 ? arg : DefaultSpeaker, 8000)
                ? "Sent: a portrait with \"Trust no one...\" in the corner for 8 seconds?" : "Failed (see the server log).";

        case "timer":
            float seconds = float.TryParse(arg, out float s) ? s : 90f;
            return $"Sent to {ScriptPresentation.StartTimer(region, seconds, "uitest_timer", 30f, 10f)} player(s): a {seconds:0}s countdown labelled \"Skrull Invasion In\"?{meta}";

        case "timerstop":
            ScriptPresentation.StopTimer(region);
            return "Sent: did the timer disappear?";

        case "mode":
            ScriptPresentation.SetModeText(region, "uitest_mode");
            return $"Sent: \"Secret Invasion\" as the game mode name?{meta}";

        case "timertext":
            ScriptPresentation.SetTimerText(region, "uitest_timertext", 42);
            return "Sent: \"Timer text test\" anywhere on the HUD?";

        case "extratext":
            ScriptPresentation.SetExtraText(region, "uitest_extratext");
            return "Sent: \"Extra text test\" anywhere on the HUD?";

        case "wave":
            ScriptPresentation.WaveCounter(region, 2, 5);
            return $"Sent: a wave counter 2 / 5?{meta}";

        case "score":
            ScriptPresentation.ScoreCounter(region, 1234);
            return $"Sent: a score of 1234?{meta}";

        case "deaths":
            ScriptPresentation.DeathCounter(region, 3);
            return $"Sent: a death counter of 3?{meta}";

        case "crystals":
            ScriptPresentation.CrystalCounter(region, 4, 10);
            return $"Sent: a crystal counter 4 / 10?{meta}";

        case "track":
        {
            if (trackedByPlayer.TryRemove(player.DatabaseUniqueId, out ulong oldId))
                ScriptSpawner.Despawn(region.Game, oldId);

            if (arg.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                ScriptPresentation.TrackEntity(player, 0);
                return "Tracking cleared.";
            }

            Agent mob = ScriptSpawner.SpawnHostile(region, GameDatabase.GetPrototypeRefByName(TrackMob), avatar.RegionLocation.Position,
                800f, 1400f, null, false, false, aggroed: false);
            if (mob == null)
                return "Could not spawn the Doombot here.";

            trackedByPlayer[player.DatabaseUniqueId] = mob.Id;
            ScriptPresentation.TrackEntity(player, mob.Id);
            return "Spawned a Doombot 8-14 m away and tracked it: an arrow, marker or health bar pointing at it?";
        }

        case "cutscene":
        {
            string key = arg.Length > 0 ? arg : "doom";
            string path = Cutscenes.TryGetValue(key, out string known) ? known : key;
            return ScriptPresentation.PlayCutscene(player, path)
                ? $"Sent {path}: did a cutscene play? (many only work in their own map)" : "Unknown cutscene (see the server log).";
        }

        case "movie":
            return ScriptPresentation.PlayMovie(player, arg.Length > 0 ? arg : DefaultMovie) ? "Sent: did a fullscreen movie play?" : "Unknown movie.";

        case "sound":
            if (arg.Length == 0) return "Usage: !uitest sound <wwise event name or id>";
            ScriptPresentation.PlaySound(avatar, arg);
            return $"Sent audio event {arg} (id {(uint.TryParse(arg, out uint id) ? id : ScriptPresentation.WwiseHash(arg))}): did you hear anything?";

        case "tutorial":
            return ScriptPresentation.HudTutorial(player, arg.Length > 0 ? arg : DefaultTutorial) ? "Sent: a HUD tutorial popup?" : "Unknown tutorial.";

        case "notify":
            return ScriptPresentation.UINotification(player, arg.Length > 0 ? arg : DefaultNotify) ? "Sent: a UI notification (\"Mission accepted\" style)?" : "Unknown notification.";

        case "global":
        {
            float progress = float.TryParse(arg, out float p) ? p : 0.42f;
            ScriptPresentation.GlobalEventProgress(player, GlobalEvent, progress);
            ScriptPresentation.GlobalEventLeaderboard(player, GlobalEvent, new[] { player.GetName(), "Nick Fury", "Maria Hill" });
            return $"Sent BiFrost event progress {progress:P0} and a leaderboard: anything in the event / mission tracker UI?";
        }

        case "teams":
            return ScriptPresentation.TeamSelectDialog(player, CivilWarEvent) ? "Sent: a Civil War team selection dialog?" : "Civil War event not found.";

        case "circle":
            ScriptPresentation.DrawCircle(region, avatar.RegionLocation.Position, 300f, 255, 40, 40, 15000);
            return "Sent: a red circle on the ground around you for 15 seconds?";

        case "wtimer":
        {
            float widgetSeconds = float.TryParse(arg, out float ws) ? ws : 90f;
            string path = WidgetPath(e.GetArg(2), "timer");
            usedWidgets[path] = 0;
            return ScriptPresentation.WidgetTimer(region, path, widgetSeconds)
                ? $"Set {path.Split('/').Last()} to {widgetSeconds:0}s: a countdown widget on the HUD?" : "Failed (not a timer widget? see the server log).";
        }

        case "wcounter":
        {
            string path = WidgetPath(arg, "wave");
            usedWidgets[path] = 0;
            return ScriptPresentation.WidgetCounter(region, path, 3, 10)
                ? $"Set {path.Split('/').Last()} to 3 / 10: a counter widget on the HUD?" : "Failed (not a counter widget? see the server log).";
        }

        case "wbutton":
            usedWidgets[ButtonWidget] = 0;
            return ScriptPresentation.WidgetButton(region, ButtonWidget, player, (clicker, result) =>
                ScriptHooks.SendChatMessage(clicker, $"[uitest] Button clicked! (result {result})", false))
                ? "Added a HUD button: can you see and click it? A chat line confirms the click." : "Failed (see the server log).";

        case "wready":
        {
            bool ready = readyByPlayer.AddOrUpdate(player.DatabaseUniqueId, false, (_, old) => !old);
            usedWidgets[ReadyWidget] = 0;
            return ScriptPresentation.WidgetReadyCheck(region, ReadyWidget, player, ready)
                ? $"Ready check shows you as {(ready ? "READY" : "pending")}: a ready check widget with your name?" : "Failed (see the server log).";
        }

        case "whealth":
        {
            // Spawns a Doombot (standing still) and puts it on a boss health bar widget; run again to add a second one
            string path = WidgetPath(arg, "bosshp");
            int entry = int.TryParse(e.GetArg(2), out int entryIndex) ? entryIndex : 0;

            Agent mob = ScriptSpawner.SpawnHostile(region, GameDatabase.GetPrototypeRefByName(TrackMob), avatar.RegionLocation.Position,
                300f, 600f, null, false, false, aggroed: false);
            if (mob == null)
                return "Could not spawn the Doombot here.";

            healthTestMobs[mob.Id] = path;
            usedWidgets[path] = 0;
            return ScriptPresentation.WidgetTrackHealth(region, path, mob, entry)
                ? $"Spawned a Doombot on {path.Split('/').Last()} (entry {entry}): a boss icon + health % on the HUD that drops as you hit it?"
                : "Failed (not an entity icons widget? see the server log).";
        }

        case "wclear":
            foreach (var kvp in healthTestMobs)
            {
                ScriptPresentation.WidgetUntrack(region, kvp.Value, kvp.Key);
                ScriptSpawner.Despawn(region.Game, kvp.Key);
            }
            healthTestMobs.Clear();

            foreach (string path in usedWidgets.Keys)
                ScriptPresentation.ClearWidget(region, path);
            usedWidgets.Clear();
            return "Test widgets removed.";

        default:
            return null;
    }
}

var AllSafe = new[] { "story", "wtimer", "wcounter", "wbutton", "wready", "timer", "mode", "timertext", "extratext", "wave", "score", "deaths", "crystals", "track", "tutorial", "notify", "global", "circle" };

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "uitest")
        return;

    e.Handled = true;

    if (ScriptHooks.IsAdmin(e.Player) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    Avatar avatar = e.Player.CurrentAvatar;
    Region region = avatar?.Region;
    if (region == null || avatar.IsInWorld == false)
    {
        e.Reply("You need to be in the world.");
        return;
    }

    string test = e.GetArg(0).ToLowerInvariant();

    if (test == "" || test == "list")
    {
        e.Reply("Tests: story, timer, timerstop, mode, timertext, extratext, wave, score, deaths, crystals, track, cutscene, movie, sound, tutorial, notify, global, teams, circle, all");
        e.Reply("Widgets: wtimer [seconds] [widget], wcounter [widget], wbutton, wready, wclear");
        e.Reply($"Region: {region.PrototypeName}, metagame id: {ScriptPresentation.GetMetaGameId(region)}");
        return;
    }

    if (test == "all")
    {
        e.Reply($"Running {AllSafe.Length} tests, 4 seconds apart. Watch the screen and note which ones show.");
        for (int i = 0; i < AllSafe.Length; i++)
        {
            string name = AllSafe[i];
            After(region.Game, i * 4f, () =>
            {
                Avatar current = e.Player.CurrentAvatar;
                if (current == null || current.Region != region)
                    return;

                string result = Run(name, e, current, region);
                ScriptHooks.SendChatMessage(e.Player, $"[uitest {name}] {result}", false);
            });
        }
        return;
    }

    string reply = Run(test, e, avatar, region);
    e.Reply(reply ?? $"Unknown test {test}. Try !uitest list");
    Log.Info($"uitest {test} by {e.PlayerName} in {region.PrototypeName}: {reply}");
});

Log.Info("UI test commands loaded (!uitest list)");
