// Admin test (EXPERIMENTAL): mirror images of your own hero. Every client draws each image as your hero in your costume.
// The image has your powers, talents and traits, stays near you and fights the enemies around you with a combat brain
// (ScriptCombatAI) that works out from the game data what each power is for:
//  - upkeep first: toggles on, a heal when its health is low, self buffs that have run out, summons
//  - then the best attack for the situation: area powers on packs, debuffs on tough targets that do not have them yet,
//    powers that buff it while that buff is down, and whatever a current buff / talent / trait is amping
//  - big cooldowns, signatures and ultimates are saved for bosses, elites and packs
//  - movement powers close the distance; ranged heroes stay at range
//
//   !mirror          spawn one image of your hero
//   !mirror <n>      spawn n images (up to MaxImages at once)
//   !mirror clear    remove your images
//   !mirror summons  list what is in your hero's summon inventory (to chase "InventoryFull" summon warnings)
//
// Test on the test server first: a client that cannot draw the image may disconnect or crash.

//------------------------------------------------------------------------------
// Settings (the script reloads when you save it; new values apply to images spawned afterwards)
//------------------------------------------------------------------------------

const int   MaxImages       = 1;      // images a player can have out at once
const float CooldownSeconds = 15f;    // after an image is gone, how long before the next one (0 = none)

MirrorImageOptions MakeOptions(Player player) => new MirrorImageOptions
{
    LifespanSeconds = 45f,     // 0 = until you leave the region
    Name            = "",      // e.g. player.GetName() to float your name above each image
    DamageScale     = 1f,      // multiplies every hit: 5f = five times the damage (1 = the powers' own damage at your level, no gear)
    MaxPowers       = 0,       // how many of your activated powers it gets (0 = all of them)
    CopyTalents     = true,    // it gets the talents you have switched on
    CopyTraits      = true,    // it gets your traits (passive powers), at your ranks
    LogDamage       = true,    // server log: its powers and damage stats at spawn, then every hit step by step (noisy: for tuning)

    // How it fights
    AI = new CombatAIOptions
    {
        MoveSpeedScale     = 1.25f,   // 1.25 = 25% faster than the body's own run speed
        AggroRange         = 1000f,   // enemies this close to you are attacked
        FollowStartRange   = 350f,    // an idle image further than this walks back to you...
        FollowStopRange    = 180f,    // ...until it is this close
        TeleportRange      = 2500f,   // further than this it jumps straight back to you
        AttackDelaySeconds = 0.4f,    // between any two powers
        MaxChannelSeconds  = 2.5f,    // a channelled power is released after this long (and at once when its target dies)
        PackSize           = 3,       // enemies around the target that count as a pack (area powers, big cooldowns)
        PackRadius         = 350f,    // how close to the target they have to be
        BigCooldownSeconds = 15f,     // powers with at least this cooldown are saved for bosses, elites and packs
        LowHealthPct       = 0.4f,    // below this share of its health it uses a heal if it has one
        UseSignatures      = true,    // signature powers (on bosses, elites and packs)
        UseUltimates       = true,    // ultimates (on bosses and large packs)
        UseMovementPowers  = true,    // dashes and leaps to reach far targets
        UseSummons         = true,
        UseBuffs           = true,    // self buffs kept up, toggles switched on
        FreePowers         = true,    // its powers cost no spirit / resources (the body has none to spend)
        LogDecisions       = true,    // server log: the powers it learned at spawn, then every power it uses and why (noisy: for tuning)
    },
};

//------------------------------------------------------------------------------

// player database id => when their next image is allowed (shared by every game instance)
var nextAllowed = new System.Collections.Concurrent.ConcurrentDictionary<ulong, DateTime>();

Hooks.On(ScriptHooks.ChatCommand, e =>
{
    if (e.Command != "mirror")
        return;

    e.Handled = true;
    Player player = e.Player;
    if (ScriptHooks.IsAdmin(player) == false)
    {
        e.Reply("Admin only.");
        return;
    }

    Avatar avatar = player.CurrentAvatar;
    if (avatar == null || avatar.IsInWorld == false)
        return;

    string arg = e.GetArg(0);

    if (arg.Equals("clear", StringComparison.OrdinalIgnoreCase))
    {
        // Removing an image early starts the cooldown from now
        int removed = ScriptMirrorImages.Clear(avatar);
        if (removed > 0 && CooldownSeconds > 0f)
            nextAllowed[e.PlayerDbId] = DateTime.UtcNow.AddSeconds(CooldownSeconds);

        e.Reply($"Removed {removed} mirror images.");
        return;
    }

    if (arg.Equals("summons", StringComparison.OrdinalIgnoreCase))
    {
        // Everything your hero has summoned and is still tracking, counted by what it is
        var inventory = avatar.SummonedInventory;
        if (inventory == null)
        {
            e.Reply("Your hero has no summon inventory.");
            return;
        }

        var counts = new Dictionary<string, int>();
        int total = 0;
        foreach (var entry in inventory)
        {
            WorldEntity summoned = avatar.Game.EntityManager.GetEntity<WorldEntity>(entry.Id);
            string name = summoned != null ? summoned.PrototypeName + (summoned.IsDead ? " (dead)" : "") : "(missing entity)";
            counts[name] = counts.GetValueOrDefault(name) + 1;
            total++;
        }

        e.Reply($"Summon inventory: {total} of {inventory.GetCapacity()} used.");
        foreach (var kvp in counts.OrderByDescending(pair => pair.Value))
            e.Reply($"  {kvp.Value} x {kvp.Key}");
        return;
    }

    int count = 1;
    if (arg.Length > 0 && int.TryParse(arg, out count) == false)
    {
        e.Reply("Use: !mirror | !mirror <n> | !mirror clear");
        return;
    }

    count = Math.Clamp(count, 1, MaxImages - ScriptMirrorImages.Count(avatar));
    if (count <= 0)
    {
        e.Reply($"You already have {MaxImages} mirror image{(MaxImages == 1 ? "" : "s")} out. Use !mirror clear.");
        return;
    }

    if (nextAllowed.TryGetValue(e.PlayerDbId, out DateTime allowedAt) && DateTime.UtcNow < allowedAt)
    {
        e.Reply($"Mirror image is on cooldown: {(allowedAt - DateTime.UtcNow).TotalSeconds:0} seconds left.");
        return;
    }

    MirrorImageOptions options = MakeOptions(player);

    int spawned = 0;
    for (int i = 0; i < count; i++)
    {
        if (ScriptMirrorImages.Spawn(avatar, options) != null)
            spawned++;
    }

    // The cooldown runs from when the image's time is up
    if (spawned > 0 && CooldownSeconds > 0f)
        nextAllowed[e.PlayerDbId] = DateTime.UtcNow.AddSeconds((options.LifespanSeconds > 0f ? options.LifespanSeconds : 0f) + CooldownSeconds);

    e.Reply(spawned > 0
        ? $"Spawned {spawned} mirror image{(spawned == 1 ? "" : "s")} for {options.LifespanSeconds:0}s (damage x{options.DamageScale:0.##}, speed x{options.AI.MoveSpeedScale:0.##}). !mirror clear removes them."
        : "Could not spawn a mirror image (see the server log).");
});
