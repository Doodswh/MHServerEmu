// Civil War: pick a side (Team Cap or Team Iron Man).
//
// The client's Civil War panel asks the player to choose; the engine saves the choice on the player (PublicEventTeamAssignment,
// saved with the account) and the game's own missions read it (e.g. "member of event team" checks on the Cap / Iron Man dailies).
// A player who hasn't picked a side yet gets the panel a few seconds after logging in.
//
//   !civilwar               your side
//   !civilwar choose        open the choose-a-side panel again
//   !civilwar reset         (admin) clear your side, so the panel comes up at the next login
//   !civilwar set cap|ironman   (admin) put yourself on a side without the panel
//   !civilwar resetstory    (admin) restart the Civil War story missions (Maria Hill's call onward)
//
// Needs the engine change (NetMessageSelectPublicEventTeam handler, Player.SetPublicEventTeam, PublicEventTeamSelected hook).

using System.Collections.Concurrent;
using System.Reflection;
using MHServerEmu.Games.GameData.Prototypes;
using MHServerEmu.Games.Missions;

const string EventPath = "Events/PublicEvents/Events/CivilWar.prototype";
const string TeamIronManPath = "Events/PublicEvents/Teams/CivilWarProReg.prototype";    // pro-registration
const string TeamCapPath = "Events/PublicEvents/Teams/CivilWarAntiReg.prototype";       // anti-registration

const bool AskOnLogin = true;          // open the panel at login for players without a side
const float AskDelaySeconds = 6f;      // after the loading screen

// player entities already asked (the panel comes up once per login, not on every region change)
var asked = new ConcurrentDictionary<ulong, bool>();

PublicEventPrototype EventProto() => GameDatabase.GetPrototypeRefByName(EventPath).As<PublicEventPrototype>();

string SideName(PrototypeId teamRef)
{
    string path = GameDatabase.GetPrototypeName(teamRef);
    if (path.Equals(TeamIronManPath, StringComparison.OrdinalIgnoreCase)) return "Team Iron Man (pro-registration)";
    if (path.Equals(TeamCapPath, StringComparison.OrdinalIgnoreCase)) return "Team Cap (anti-registration)";
    return "no side";
}

PrototypeId CurrentTeam(Player player)
{
    PublicEventPrototype eventProto = EventProto();
    return eventProto != null ? player.GetPublicEventTeam(eventProto) : PrototypeId.Invalid;
}

// The engine rolls a Cap daily/weekly AND an Iron Man daily/weekly for everyone each day. The side's own "member of the
// other team" failure check only runs while a mission is active, so after a side switch the old side's daily stayed in the
// log (Sharon still offering it) and the new side's one could be stuck failed from when the player was on the other side.
// After a side change (or at login) this drops the other side's dailies/weeklies and re-rolls the new side's if they failed.
// Only this player's missions are touched. A daily already completed today stays completed (one per day, as designed).
MethodInfo RerollMethod = typeof(MissionManager)
    .GetMethod("AdvancedMissionReroll", BindingFlags.NonPublic | BindingFlags.Instance);

// "cap" / "ironman" for a Civil War daily/weekly category, null for anything else
string CategorySide(AdvancedMissionCategoryPrototype category)
{
    if (category == null) return null;
    string path = GameDatabase.GetPrototypeName(category.DataRef);
    if (path.Contains("InfluenceMissions/CivilWar/") == false) return null;
    if (path.EndsWith("IronMan.prototype", StringComparison.OrdinalIgnoreCase)) return "ironman";
    if (path.EndsWith("Cap.prototype", StringComparison.OrdinalIgnoreCase)) return "cap";
    return null;
}

string TeamSide(PrototypeId teamRef)
{
    string path = GameDatabase.GetPrototypeName(teamRef);
    if (path.Equals(TeamIronManPath, StringComparison.OrdinalIgnoreCase)) return "ironman";
    if (path.Equals(TeamCapPath, StringComparison.OrdinalIgnoreCase)) return "cap";
    return null;
}

void SyncDailiesToSide(Player player)
{
    var manager = player?.MissionManager;
    if (manager == null) return;
    string side = TeamSide(CurrentTeam(player));

    var rerollCategories = new HashSet<AdvancedMissionCategoryPrototype>();
    var keptCategories = new HashSet<AdvancedMissionCategoryPrototype>();
    int dropped = 0;

    foreach (string pattern in new[] { "CivilWarDaily", "CivilWarWeekly" })
        foreach (var mission in manager.FindMissionsByPattern(pattern))
        {
            if (mission.Prototype is not AdvancedMissionPrototype advancedProto) continue;
            var category = advancedProto.CategoryProto;
            string missionSide = CategorySide(category);
            if (missionSide == null) continue;

            if (missionSide != side)
            {
                // the other side's (or any, with no side): out of the log and away from that side's liaison
                if (mission.State != MissionState.Invalid) { mission.SetState(MissionState.Invalid); dropped++; }
            }
            else if (mission.State == MissionState.Failed || mission.State == MissionState.Invalid)
            {
                // failed back when the player was on the other side: free the category so it can roll again
                if (mission.State == MissionState.Failed) mission.SetState(MissionState.Invalid);
                rerollCategories.Add(category);
            }
            else
            {
                keptCategories.Add(category);
            }
        }

    int rerolled = 0;
    if (RerollMethod != null)
        foreach (var category in rerollCategories)
        {
            if (keptCategories.Contains(category)) continue;   // the category already has a live mission
            RerollMethod.Invoke(manager, new object[] { category });
            rerolled++;
        }
    else if (rerollCategories.Count > 0)
        Log.Warn("[civilwar] MissionManager.AdvancedMissionReroll not found; the new side's dailies roll at the next daily reset");

    if (dropped > 0 || rerolled > 0)
        Log.Info($"[civilwar] {player.GetName()} ({side ?? "no side"}): dropped {dropped} other-side dailies/weeklies, re-rolled {rerolled}");
}

void SyncDailiesLater(Player player, float delaySeconds = 1f)
{
    if (player == null) return;
    ulong playerId = player.Id;
    Game game = player.Game;
    ScriptTimer.After(game, delaySeconds, () =>
    {
        try { SyncDailiesToSide(game.EntityManager.GetEntity<Player>(playerId)); }
        catch (Exception ex) { Log.Warn($"[civilwar] daily sync failed: {ex.Message}"); }
    });
}

Hooks.On(ScriptHooks.AvatarEnteredWorld, e =>
{
    Player player = e.Player;
    if (AskOnLogin == false || player == null) return;
    if (asked.TryAdd(player.Id, true) == false) return;   // once per login (the player entity is new each login)
    SyncDailiesLater(player, AskDelaySeconds);              // after the login's daily roll
    if (CurrentTeam(player) != PrototypeId.Invalid) return;

    ulong playerId = player.Id;
    Game game = player.Game;
    ScriptTimer.After(game, AskDelaySeconds, () =>
    {
        Player current = game.EntityManager.GetEntity<Player>(playerId);
        if (current == null || CurrentTeam(current) != PrototypeId.Invalid) return;
        ScriptPresentation.TeamSelectDialog(current, EventPath);
        Log.Info($"[civilwar] asked {current.GetName()} to pick a side");
    });
});

// (the client shows its own "You picked a side in the Civil War conflict." once the engine confirms the choice)
Hooks.On(ScriptHooks.PublicEventTeamSelected, e =>
{
    Log.Info($"[civilwar] {e.Player.GetName()} joined {SideName(e.TeamRef)}");
    SyncDailiesLater(e.Player);
});

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "civilwar") return;
    e.Handled = true;

    PublicEventPrototype eventProto = EventProto();
    if (eventProto == null) { e.Reply("The Civil War event isn't in the game data."); return; }

    string arg = e.GetArg(0)?.ToLowerInvariant();
    switch (arg)
    {
        case null:
        case "":
            e.Reply($"You are on {SideName(CurrentTeam(e.Player))}.");
            return;

        case "choose":
            ScriptPresentation.TeamSelectDialog(e.Player, EventPath);
            return;

        case "reset":
            if (ScriptHooks.IsAdmin(e.Player) == false) { e.Reply("Admin only."); return; }
            e.Player.SetPublicEventTeam(eventProto, PrototypeId.Invalid);
            SyncDailiesLater(e.Player);
            e.Reply("Your side is cleared; you'll be asked again at your next login (or use !civilwar choose).");
            return;

        case "resetstory":
        {
            // restart the Civil War story missions, e.g. for a player who did them out of order. FIRST one first: a later
            // phase restarted while the one before it still counts as done starts over at once (story 3 teleports to the
            // hideout); with story 1 restarted first, the later ones wait for it.
            if (ScriptHooks.IsAdmin(e.Player) == false) { e.Reply("Admin only."); return; }
            int restarted = 0;
            foreach (string phase in new[] { "Phase01", "Phase02", "Phase04Captain", "Phase04IronMan" })
            {
                PrototypeId missionRef = GameDatabase.GetPrototypeRefByName($"Missions/Prototypes/PVEEndgame/StaticScenarios/CivilWarEventProgressionMission{phase}.prototype");
                var mission = e.Player.MissionManager?.FindMissionByDataRef(missionRef);
                if (mission != null && mission.RestartMission()) restarted++;
            }
            e.Reply($"Civil War story restarted ({restarted} missions). Answer Maria Hill's call to begin.");
            return;
        }

        case "set":
            if (ScriptHooks.IsAdmin(e.Player) == false) { e.Reply("Admin only."); return; }
            string side = e.GetArg(1)?.ToLowerInvariant();
            string teamPath = side != null && side.StartsWith("iron") ? TeamIronManPath : side == "cap" ? TeamCapPath : null;
            if (teamPath == null) { e.Reply("Usage: !civilwar set cap|ironman"); return; }
            PrototypeId teamRef = GameDatabase.GetPrototypeRefByName(teamPath);
            if (e.Player.SetPublicEventTeam(eventProto, teamRef))
            {
                SyncDailiesLater(e.Player);
                e.Reply($"You are now on {SideName(teamRef)}.");
            }
            else
                e.Reply("Couldn't set your side (see the server log).");
            return;

        default:
            e.Reply("Usage: !civilwar | !civilwar choose | !civilwar reset | !civilwar set cap|ironman | !civilwar resetstory");
            return;
    }
});

Log.Info("Civil War sides loaded (!civilwar)");
