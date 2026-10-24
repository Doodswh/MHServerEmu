// X-Defense check: prints what the server thinks is going on in the wave mode you are standing in (X-Defense, Holo-Sim
// or any region with a metagame). For finding out why waves are not spawning.
//
//   !xdefcheck     (admin) run it INSIDE the broken instance, about a minute after entering. The full report goes to the
//                  server console / log (every line starts with [XDefCheck]); a short summary goes to your chat.
//
// It only reads state. Nothing is changed.

using MHServerEmu.Games.Dialog;
using MHServerEmu.Games.Entities.Avatars;
using MHServerEmu.Games.MetaGames;
using MHServerEmu.Games.Missions;

string Short(PrototypeId protoRef)
{
    string name = GameDatabase.GetPrototypeName(protoRef) ?? "";
    int slash = name.LastIndexOf('/');
    return (slash >= 0 ? name.Substring(slash + 1) : name).Replace(".prototype", "");
}

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "xdefcheck")
        return;

    e.Handled = true;
    if (ScriptHooks.IsAdmin(e.Player) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    Avatar avatar = e.Player.CurrentAvatar;
    Region region = avatar?.Region;
    if (region == null)
        return;

    var report = new List<string>();
    void Line(string text) => report.Add(text);

    Game game = region.Game;
    int playerCount = 0;
    foreach (Player player in new PlayerIterator(region)) playerCount++;
    Line($"Region {region.PrototypeName} (id 0x{region.Id:X}), difficulty {Short(region.DifficultyTierRef)}, game time {game.CurrentTime.TotalSeconds:0}s, " +
        $"players {playerCount}");

    // Metagames and their active states
    int stateCount = 0;
    foreach (ulong metaGameId in region.MetaGames)
    {
        MetaGame metaGame = game.EntityManager.GetEntity<MetaGame>(metaGameId);
        if (metaGame == null)
        {
            Line($"MetaGame id {metaGameId}: NOT FOUND");
            continue;
        }

        Line($"MetaGame {Short(metaGame.PrototypeDataRef)}: wave count {(int)metaGame.Properties[PropertyEnum.MetaGameWaveCount]}, {metaGame.MetaStates.Count} active state(s)");
        foreach (var state in metaGame.MetaStates)
        {
            stateCount++;
            Line($"    state {Short(state.PrototypeDataRef)} ({state.GetType().Name})");
        }
    }

    if (region.MetaGames.Count == 0)
        Line("No metagame in this region.");

    // Everything in the region, grouped by the mission that spawned it
    var all = region.Entities.OfType<WorldEntity>().Where(entity => entity.IsInWorld).ToList();
    int spawners = 0, spawnersActive = 0, hostileAlive = 0, students = 0, studentsDead = 0;
    var byMission = new Dictionary<PrototypeId, (int Total, int Spawners, int ActiveSpawners, int Hostile, int Students)>();

    foreach (WorldEntity entity in all)
    {
        bool isSpawner = entity is Spawner;
        bool isActiveSpawner = entity is Spawner spawner && spawner.IsActive;
        bool isStudent = entity.PrototypeName.Contains("Student", StringComparison.OrdinalIgnoreCase);
        bool isHostile = entity is Agent && entity is not Avatar && entity.IsDead == false && avatar.IsHostileTo(entity);

        if (isSpawner) spawners++;
        if (isActiveSpawner) spawnersActive++;
        if (isHostile) hostileAlive++;
        if (isStudent) { students++; if (entity.IsDead) studentsDead++; }

        PrototypeId missionRef = entity.MissionPrototype;
        if (missionRef == PrototypeId.Invalid)
            continue;

        byMission.TryGetValue(missionRef, out var counts);
        byMission[missionRef] = (counts.Total + 1, counts.Spawners + (isSpawner ? 1 : 0), counts.ActiveSpawners + (isActiveSpawner ? 1 : 0),
            counts.Hostile + (isHostile ? 1 : 0), counts.Students + (isStudent ? 1 : 0));
    }

    Line($"Entities in world: {all.Count}. Spawners {spawners} ({spawnersActive} active). Hostile agents alive {hostileAlive}. " +
        $"Students {students} ({studentsDead} dead).");

    // The enemies themselves: are they awake, where are they, and has this player's client been told about them?
    var hostiles = all.Where(entity => entity is Agent && entity is not Avatar && entity.IsDead == false && avatar.IsHostileTo(entity)).Cast<Agent>().ToList();
    if (hostiles.Count > 0)
    {
        Vector3 here = avatar.RegionLocation.Position;
        int dormant = hostiles.Count(agent => agent.IsDormant);
        int notSimulated = hostiles.Count(agent => agent.IsSimulated == false);
        int sentToClient = hostiles.Count(agent => e.Player.AOI.InterestedInEntity(agent.Id));
        int within2000 = hostiles.Count(agent => Vector3.Distance2D(here, agent.RegionLocation.Position) <= 2000f);
        float nearest = hostiles.Min(agent => Vector3.Distance2D(here, agent.RegionLocation.Position));

        Line($"Hostiles: {hostiles.Count} alive. Dormant {dormant}, not simulated {notSimulated}. Sent to your client {sentToClient}. " +
            $"Within 2000 of you {within2000}, nearest {nearest:0} away.");

        foreach (var group in hostiles.GroupBy(agent => agent.PrototypeName).OrderByDescending(group => group.Count()).Take(8))
            Line($"    {group.Count()} x {group.Key} (nearest {group.Min(agent => Vector3.Distance2D(here, agent.RegionLocation.Position)):0})");
    }

    // Missions: state, objectives, what the tracker finds for them (mission actions use the tracker to find their spawners)
    int activeMissions = 0;
    MissionManager missionManager = region.MissionManager;
    var missionRefs = new HashSet<PrototypeId>(byMission.Keys);
    if (missionManager != null)
        foreach (PrototypeId missionRef in missionManager.ActiveMissions)
            missionRefs.Add(missionRef);

    foreach (PrototypeId missionRef in missionRefs)
    {
        Mission mission = missionManager?.FindMissionByDataRef(missionRef);
        byMission.TryGetValue(missionRef, out var counts);

        int trackedSpawned = 0, trackedAction = 0, trackedAny = 0;
        foreach (WorldEntity tracked in region.EntityTracker.Iterate(missionRef, EntityTrackingFlag.SpawnedByMission)) trackedSpawned++;
        foreach (WorldEntity tracked in region.EntityTracker.Iterate(missionRef, EntityTrackingFlag.MissionAction)) trackedAction++;
        foreach (WorldEntity tracked in region.EntityTracker.Iterate(missionRef)) trackedAny++;

        string state = mission != null ? mission.State.ToString() : "no mission object";
        if (mission != null && mission.State == MissionState.Active) activeMissions++;

        // Skip the ambient student scenes unless something is odd about them
        bool isAmbient = counts.Spawners == 0 && counts.Hostile == 0 && (mission == null || mission.State != MissionState.Active || counts.Total <= 3);
        if (isAmbient && trackedSpawned == counts.Total)
            continue;

        Line($"Mission {Short(missionRef)}: {state}. In world: {counts.Total} entities ({counts.Spawners} spawners, {counts.ActiveSpawners} active; " +
            $"{counts.Hostile} hostile; {counts.Students} students). Tracker: {trackedSpawned} spawned-by-mission, {trackedAction} mission-action, {trackedAny} total.");

        if (mission != null)
        {
            foreach (MissionObjective objective in mission.Objectives)
                Line($"    objective {objective.PrototypeIndex}: {objective.State}, time left {objective.TimeRemainingForObjective.TotalSeconds:0}s");
        }

        if (counts.Total != trackedSpawned)
            Line($"    MISMATCH: {counts.Total} entities carry this mission but the tracker lists {trackedSpawned} as spawned by it.");
    }

    // Every HUD widget the server is currently telling clients about in this region, with its numbers. A wave's widgets
    // should disappear when its mission ends: leftovers from earlier waves would pile up on screen.
    try
    {
        string[] widgetLines = region.UIDataProvider.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        int widgetCount = widgetLines.Count(line => line.StartsWith("_dataDict["));
        Line($"HUD widgets: {widgetCount}");
        foreach (string widgetLine in widgetLines)
        {
            string text = widgetLine.TrimEnd();
            if (text.Length > 0)
                Line("    " + text.Replace("_dataDict", "widget"));
        }
    }
    catch (Exception ex)
    {
        Line($"HUD widgets: could not be listed ({ex.Message})");
    }

    foreach (string line in report)
        Log.Info("[XDefCheck] " + line);

    e.Reply($"X-Defense check written to the server log ({report.Count} lines, tagged [XDefCheck]).");
    e.Reply($"States {stateCount}, active missions {activeMissions}, spawners {spawners} ({spawnersActive} active), hostile alive {hostileAlive}, " +
        $"students {students} ({studentsDead} dead).");
    if (hostiles.Count > 0)
        e.Reply(report.First(line => line.StartsWith("Hostiles:")));
});

Log.Info("X-Defense check loaded (!xdefcheck)");
