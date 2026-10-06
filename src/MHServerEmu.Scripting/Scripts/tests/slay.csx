// Admin tool: kill a player's hero or enemies with a chat command.
//
//   !slay <player>        kill that player's current hero (they can revive as usual)
//   !slay near [radius]   kill every enemy within radius of you (default DefaultRadius)
//   !slay all             kill every enemy in your region
//   !slay boss            kill the nearest boss
//   !slay <text>          kill the nearest enemy whose name contains <text> (e.g. !slay rhino)
//
// A player name is tried first (the exact name, or the start of it if only one player matches), then enemy names.
// Players can only be reached in the same game instance as you (your region and the regions sharing its server instance).
// Enemies you kill count as your kills: they drop loot and give experience as usual.

const float DefaultRadius = 1500f;
const float MaxRadius     = 20000f;

// Every living enemy in the region, nearest to the admin first: no breakable props, no allies
List<WorldEntity> Enemies(Avatar avatar)
{
    Vector3 position = avatar.RegionLocation.Position;

    return avatar.Region.Entities.OfType<WorldEntity>()
        .Where(entity => entity is Agent && entity is not Avatar && entity.IsInWorld && entity.IsDead == false &&
            entity.IsDestructible == false && avatar.IsHostileTo(entity))
        .OrderBy(entity => Vector3.Distance2D(position, entity.RegionLocation.Position))
        .ToList();
}

int KillAll(Avatar avatar, IEnumerable<WorldEntity> enemies)
{
    int count = 0;
    foreach (WorldEntity enemy in enemies)
    {
        enemy.Kill(avatar);
        count++;
    }

    return count;
}

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "slay")
        return;

    e.Handled = true;
    Player admin = e.Player;
    if (ScriptHooks.IsAdmin(admin) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    Avatar avatar = admin.CurrentAvatar;
    if (avatar == null || avatar.IsInWorld == false || avatar.Region == null)
        return;

    string arg = e.GetArg(0);
    if (arg.Length == 0)
    {
        e.Reply("Use: !slay <player> | !slay near [radius] | !slay all | !slay boss | !slay <enemy name>");
        return;
    }

    // A player first: the exact name, or the start of a name if only one player matches
    List<Player> players = admin.Game.EntityManager.Players.Where(player => player != null && player.CurrentAvatar != null).ToList();
    Player target = players.FirstOrDefault(player => player.GetName().Equals(arg, StringComparison.OrdinalIgnoreCase));
    if (target == null)
    {
        List<Player> partial = players.Where(player => player.GetName().StartsWith(arg, StringComparison.OrdinalIgnoreCase)).ToList();
        if (partial.Count == 1)
            target = partial[0];
        else if (partial.Count > 1 && arg.Length >= 3)
        {
            e.Reply($"Several players match [{arg}]: {string.Join(", ", partial.Select(player => player.GetName()))}. Type more of the name.");
            return;
        }
    }

    if (target != null)
    {
        Avatar victim = target.CurrentAvatar;
        if (victim.IsInWorld == false || victim.IsDead)
        {
            e.Reply($"{target.GetName()}'s hero is not alive in the world right now.");
            return;
        }

        victim.Kill();
        e.Reply($"Killed {target.GetName()}'s hero ({victim.PrototypeName}).");
        Log.Info($"{admin.GetName()} used !slay on player {target.GetName()}");
        return;
    }

    switch (arg.ToLowerInvariant())
    {
        case "near":
        {
            float radius = DefaultRadius;
            if (e.GetArg(1).Length > 0 && float.TryParse(e.GetArg(1), out float parsed))
                radius = Math.Clamp(parsed, 1f, MaxRadius);

            Vector3 position = avatar.RegionLocation.Position;
            int count = KillAll(avatar, Enemies(avatar).Where(enemy => Vector3.Distance2D(position, enemy.RegionLocation.Position) <= radius));
            e.Reply($"Killed {count} enemies within {radius:0}.");
            return;
        }

        case "all":
        {
            e.Reply($"Killed {KillAll(avatar, Enemies(avatar))} enemies in this region.");
            return;
        }

        case "boss":
        {
            WorldEntity boss = Enemies(avatar).FirstOrDefault(enemy => enemy.GetRankPrototype()?.IsRankBoss == true);
            if (boss == null)
            {
                e.Reply("No living boss in this region.");
                return;
            }

            string name = boss.PrototypeName;
            boss.Kill(avatar);
            e.Reply($"Killed {name}.");
            return;
        }

        default:
        {
            WorldEntity enemy = Enemies(avatar).FirstOrDefault(candidate => candidate.PrototypeName.Contains(arg, StringComparison.OrdinalIgnoreCase));
            if (enemy == null)
            {
                e.Reply($"No player or living enemy matches [{arg}] here.");
                return;
            }

            string name = enemy.PrototypeName;
            enemy.Kill(avatar);
            e.Reply($"Killed {name}.");
            return;
        }
    }
});
