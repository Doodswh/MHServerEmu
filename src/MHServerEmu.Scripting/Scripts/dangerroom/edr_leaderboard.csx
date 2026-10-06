// Endless Danger Room leaderboard: the highest wave each player has ever cleared.
//
// Uses the game's "Omega Anniversary 2016" board (Events tab, never resets, highest first), taken over so only this script counts,
// renamed, and fed through its wave rule, which keeps each player's best (a lower wave later never lowers it).
// Every cleared wave is sent for everyone in the run. It never resets, so its own reward tiers never pay out.
// Needs the board enabled in Data/Leaderboards/LeaderboardSchedule.json. The new name shows after players reconnect.
//
// (The Test Leaderboard would reset weekly, but the client never lists it: its own game data marks it private / not in game, and a
// server-side patch can't change what the client shows.)

using MHServerEmu.Games.Entities.Avatars;

const string WaveLeaderboard = "Leaderboards/Prototypes/Leaderboards/Events/Anniversary2016.prototype";
const int    WaveRuleIndex   = 54;   // its MetaGameWaveComplete rule: keeps the highest value, 1 point per wave
const int    AnnounceEvery   = 10;   // chat line every this many waves ("Wave 20 recorded...")

ScriptLeaderboards.TakeOver(WaveLeaderboard);
ScriptLeaderboards.Rename(WaveLeaderboard, "Endless Danger Room - Highest Wave",
    "The highest Endless Danger Room wave each hero has cleared.",
    "Every wave you clear in the Endless Danger Room counts; only your best run is kept. All-time board, it never resets.");

Hooks.On(ScriptHooks.DangerRoomWaveCleared, e =>
{
    if (e.Region == null || e.Wave <= 0)
        return;

    foreach (Player player in new PlayerIterator(e.Region))
    {
        Avatar avatar = player.CurrentAvatar;
        if (avatar == null || avatar.Region != e.Region)
            continue;

        if (ScriptLeaderboards.Submit(player, WaveLeaderboard, e.Wave, WaveRuleIndex) && e.Wave % AnnounceEvery == 0)
            ScriptHooks.SendChatMessage(player, $"[Endless Danger Room] Wave {e.Wave} recorded on the Highest Wave leaderboard.", false);
    }
});

Log.Info("Endless Danger Room leaderboard active");
