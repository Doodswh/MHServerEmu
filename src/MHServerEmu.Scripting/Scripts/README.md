# Server Scripts

Scripts in this folder attach handlers to the shared hook registry (`MHServerEmu.Games.Scripting.ScriptHooks`).
The game simulation only invokes hooks; it never references MoonSharp or Roslyn.

- `.lua` files run with MoonSharp (sandboxed, no io/os).
- `.csx` files are compiled with Roslyn (trusted, full access to game assemblies).
- Files/folders starting with `_` are skipped (see `_examples`). Copy an example out to enable it.
- `omega/` holds the live Omega difficulty rules (knockback immunity, mob affixes, Omega item drops). Disabling scripting turns these off.
- With `HotReload=true` in `[Scripting]` (Config.ini), saving a file reloads it; deleting it unloads its handlers.

## Hooks

| Hook | When | Can change |
|---|---|---|
| `ConditionApplying` | A power is about to apply a condition | `Cancel = true` blocks it |
| `EnemyAffixesRolled` | Enemy affixes rolled for a spawn group | `RemoveAffix(text)`, `AddAffix(path)`, `RemoveDisplacementAffixes()`, `AddRandomAffixes(count, "excluded,name,words")` |
| `EntityKilled` | A world entity is killed | - |
| `PlayerEnteredRegion` | A player enters a region | `SendMessage(text)`, `ShowBanner(key [, n, style, ms])` |
| `DangerRoomWaveStarting` | Before an Endless Danger Room wave spawns | `BossCount`, `MobCount` |
| `DangerRoomWaveCleared` | A Danger Room wave is cleared | `DelaySeconds`, `ShowDefaultMessages`, `ShowBanner`, `SendMessage` |
| `DangerRoomRewards` | Danger Room completion rewards drop | `ReplaceDefaultRewards`, `AddReward(path [, count, slot])` |
| `DangerRoomFinished` | A Danger Room run ends | `ShowDefaultMessages`, `ShowBanner`, `SendMessage` |
| `ChatCommand` | A player types `!something` (before built-in commands) | `Handled = true` consumes it, `Reply(text)`; read `Command`, `GetArg(i)` |
| `EquipmentChanged` | An item is equipped / unequipped (also when saved gear loads) | - (read `Owner`, `Item`, `Equipped`) |
| `ItemAffixesRolling` | Every generated item, before its normal affixes roll (C#: runs often) | `SetRarity` / `SetOmegaRarity` / `SetCosmicRarity`, `ItemLevel`, `SetStackingLimits`, `AddStackedAffixes`, `AddUniqueAffixes`, `AddRandomGemAffix`, `AddRandomVisualAffix`, `SkipNormalAffixes`, `MaxAffixesPerStat` (cap on the finished item) |
| `RegionGenerating` | A region instance is about to build its layout | `Seed`, `NewRandomSeed()`, `SetRoomRemovalChance(pct [, areaNameContains])`, `SetConnectionRemovalChance(pct [, areaNameContains])` |
| `EntityInteracted` | A player clicks (talks to / uses) a world entity | - (read `Player`, `Entity`, `ScriptTag`) |

Useful properties (all hooks): `RegionName`, `IsOmega`. See `ScriptHookArgs.cs` for the full list.

## Lua

```lua
hooks.on("ConditionApplying", function(e)
    if e.TargetIsAvatar and e.IsOmega and e.IsStun then
        e.Cancel = true
    end
end, 0)   -- optional priority, lower runs first

log.info("loaded")   -- also log.warn / log.error / print
```

Call methods with a colon: `e:RemoveAffix("Teleport")`. Only hook args are visible to Lua, not raw game objects.

## On-screen text (banners)

The client can only show text it already knows, and it learns custom text when it connects. So text is registered
up front and shown by key:

```lua
ui.register("omega_welcome", "Welcome to Omega")          -- one string
ui.registerRange("dr_wave_clear", "Wave {0} Clear", 1, 1000) -- one string per number, {0} replaced

hooks.on("DangerRoomWaveCleared", function(e)
    e:ShowBanner("dr_wave_clear", e.Wave)                  -- optional: style, duration ms
end)
```

Styles: `large` (default), `standard`, `reward`, `rewardlarge`, `alert`, `error`, `unlock`.
Register at load time. Text added or changed by a hot reload only shows for players after they reconnect.
C#: `ScriptText.Register(...)`, `ScriptText.RegisterRange(...)`, `ScriptText.ShowBanner(player, key, number)`.

`dangerroom/` holds the live Endless Danger Room rules (wave sizes, breaks, banners, bonus rewards).

C# only, shown to everyone in a region:
- `ScriptText.ShowBannerToRegion(region, key [, n, style, ms])`
- `ScriptText.ShowPlayerBanner(player, key, n, name1 [, name2, intArgs])` / `ShowPlayerBannerToRegion(...)`: metagame banner
  with live player names and numbers filled into the text: `$playersource$` (name1), `$playertarget$` (name2),
  `$intargzero$` / `$intargone$` (intArgs). Example: `"Top damage: $playersource$ ($intargzero$%)"`.
- `ScriptText.ShowPortraitNotificationToRegion(region, entityRef, key [, n, seconds])`: popup with that character's portrait and the text.
  The client keeps these until the server clears them, so they are cleared `seconds` (default 8) after the latest one in the region;
  `ScriptText.ClearPortraitNotifications(region)` clears them right away.
- `ScriptText.ShowOverheadText(entity, key [, n, seconds])`: text floating over a character's head.
- `ScriptText.Register` stores text as is; only `RegisterRange` replaces `{0}` with the number.
- `ScriptText.SetObjectiveTitle(region, key [, n])`, `ScriptText.SetObjectiveCounter(region, current, total)`, `ScriptText.ClearObjective(region)`:
  the Danger Room HUD wave title and defeated / required counter bar.

## Encounters (C#)

- `After(game, seconds, () => ...)` runs code later on that game's thread. Skipped once the script is unloaded or reloaded (`IsLoaded`).
- `ScriptSpawner.FindAgent("DoctorOctopus|DocOck", "Patrol")` finds a boss prototype by name (or pass a full `.prototype` path). The pick is logged.
- `ScriptSpawner.SpawnHostile(region, protoRef, near, minDist, maxDist, targetAvatar)` spawns an aggroed hostile agent.
- `ScriptSpawner.AddRandomAffixes(agent, count)`, `ScriptSpawner.IsAlive(game, id)`, `ScriptSpawner.Despawn(game, id)`.

- `ScriptRewards.DropItem(player, "Entity/Items/.../X.prototype", count, slot, itemLevel)` drops reward items owned by that player at
  their feet. `itemLevel` 0 (the default) uses the hero's level; set it (e.g. 75, the max) for special gear rewards.

Random-map events:
- `ScriptSpawner.SpawnInteractable(region, protoRef, position, yawDegrees, tag)`: a friendly, invulnerable, clickable NPC or object.
  Clicking it raises `EntityInteracted` with `ScriptTag == tag`.
- `ScriptSpawner.TryFindRandomSpot(region, out pos [, reachableFrom, minDistanceFrom])`: a random walkable spot anywhere in the region
  (reachable on foot from `reachableFrom` when given). `TryFindFarSpot(region, from, out pos)`: the farthest reachable spot found.
  `TryGetStartPosition(region, out pos)`: where players arrive.
- `ScriptTeleport.ToRegion(player, "T1L1BambooRegion" or full path [, difficultyPath, seed])`, `ToRegionWithParty(...)` (party members
  in the same region too), `ToTown(player)`, `FindRegion(name)`. A non-zero `seed` always gets a brand-new private instance (terminals
  are otherwise reused), and that instance's `RandomSeed` / `RegionGenerating` seed is the same number, so scripts can recognise it.
- `ScriptSpawner.SpawnHostile(..., aggroed: false)`: the enemy guards its spot instead of hunting players across the map.
  `ScriptSpawner.TryFindSpotNear(region, near, min, max, out pos)`: a free spot near a point.
- `ScriptDialog.Show(player, messageKey, button1Key, button2Key, (player, button) => ...)`: a popup with one or two buttons
  (registered text keys; `button` is 1, 2, or 0 when closed).
- `RegionGenerating` room / connection removal only affects areas with a random grid layout (`!zonetest info` shows which);
  connection removal only applies to `CellGridGenerator` areas.

Hand-built maps (RegionGenerating): `e.BuildLayout("EndGame/Limbo/Limbo_A", rows)` draws the region's (single) area from text, one character
per cell of the cell set: `#` = solid, `S` = arrival, anything else = a room (neighbors connect, the cell with matching exits is picked).
`e.SetLevel(60)` sets the enemy level. `ScriptTeleport.ToBuiltMap(player, region, difficulty, seed)` sends players in (the region needs no
entrance; respawns use the S room too); `ScriptTeleport.GetBuiltMap(region).GetMarkers('P')` gives room centers by character (use
`ScriptSpawner.TryFindSpotNear` for a walkable spot in the room). Keep layouts open: every room 2+ neighbors, S not at a dead end
(the Limbo set has no dead-end or straight-corridor cells). `tests/map_test.csx` (`!maptest`) tries it. Host region used so far:
Regions/ZZZDevelopment/WhiteRoom/BlackRoomRegion (empty, private).

`events/dark_dimension_incursion.csx` (in testing): Doctor Strange in Avengers Tower sends a party into a hand-built outdoor map
(tile sets in `CellSets`, default the Fort Stryker training forest; `!incursion tiles` switches, `!incursion skip` jumps phases): four Dark Dimension rifts
spawning demons, three Mystic Power Nodes to channel while their guardians attack (Eye of Agamotto damage buff per node), Kaecilius
invulnerable behind Mirror Images, a Mindless Titan, then rewards claimed by clicking Doctor Strange (`!incursion`).

`events/terminal_breach.csx` uses all of these: a S.H.I.E.L.D. agent in Avengers Tower opens breaches into random Cosmic terminals with
reshuffled layouts, Breach Anchor villains spread across the map, and a Breach Overlord at the far end (`!breach` shows status and distances).

`events/age_of_moo.csx`: an Age of Ultron style raid with the Skrull "cows". A second S.H.I.E.L.D. agent in Avengers Tower sends parties
into a fresh random Cosmic terminal: cow stampede waves, disguised Skrull Commanders spread across the map, then High Commander Brevik
(herd reinforcements at 66% / 33% health) who rises again as All-Father Brevik. Brevik taunts through portrait popups
(`ScriptPresentation.StoryNotification`); `!moo` shows status and distances.

Presentation (C#, `ScriptPresentation`, experimental: `tests/ui_test.csx` / `!uitest` shows which ones the client draws where):
`StoryNotification(player, textKey, speakerPath)` (portrait + text), `StartTimer(region, seconds, labelKey)` / `StopTimer`,
`TrackEntity(player, entityId)`, `PlayCutscene(player, kismetPath)`, `PlayMovie`, `PlaySound(entity, wwiseEvent)`,
`WaveCounter` / `ScoreCounter` / `DeathCounter` / `CrystalCounter`, `SetModeText`, `HudTutorial`, `UINotification`,
`GlobalEventProgress` / `GlobalEventLeaderboard`, `TeamSelectDialog`, `DrawCircle`. Timer, counters and mode text belong to the
region's metagame. Confirmed so far: `StoryNotification` works anywhere; the metagame ones did not show in Midtown.
HUD widgets (work in any region, same system as the objective bar): `WidgetTimer(region, "UI/MetaGame/TimerCenter.prototype", seconds)`,
`WidgetCounter(region, widgetPath, current, total)`, `WidgetButton(region, widgetPath, player, (player, result) => ...)`,
`WidgetReadyCheck`, `ClearWidget`, `OverrideWidgetLabel(widgetPath, text)` (changes that widget's label everywhere).
Boss health bars: `WidgetTrackHealth(region, widgetPath, entity [, entry])` puts any boss on a UIWidgetEntityIcons widget (generic boss icon
+ health %, e.g. "UI/MetaGame/SurturRaid/FiveMan/SlagHealth.prototype"; the widget's own boss filter is ignored, call again to add more
bosses), `WidgetEntityDefeated(region, widgetPath, entityId)` from your kill handler, `WidgetUntrack(region, widgetPath, entityId)` before
a boss despawns (or `ClearWidget`). `!uitest whealth` tries it.

`tests/event_tools_test.csx` has admin commands to try these (`!evtnpc`, `!evtlayout`, `!randspot`, `!farspot`).
`!zonetest list / go / info / mark` (admin, built in) checks which unused zones load on the client.

## Custom leaderboards (C#)

The client can only show leaderboards from the game data, so a custom one is an existing board that a script renames and feeds:
- `ScriptLeaderboards.Rename(boardPath, name [, brief, extended])`: replaces the board's texts (shows after reconnecting).
- `ScriptLeaderboards.Submit(player, boardPath, value [, ruleIndex])`: adds a score through the board's own scoring rule. Kill / collect
  style rules add up, completion-time rules keep the lowest (time boards take milliseconds). `ScriptLeaderboards.IsActive(boardPath)`.
- The board keeps its reset cycle, rewards, display (number / time) and order from the game data, and must be enabled in
  Data/Leaderboards/LeaderboardSchedule.json (`!leaderboards reloadschedule`).
- `ScriptLeaderboards.TakeOver(boardPath)` at load makes the board script-only: its original scoring rules stop counting.
  (Patching the rules' context with the patch manager doesn't work: patches land after the context filter is built.)

In use: Midtown Boss Rush daily damage (Events/DaredevilVsHand, in thousands), Terminal Breach fastest clear
(DangerRoom/DRScenarioTimeBroodEpic, weekly), Age of MOO fastest clear (DangerRoom/DRScenarioTimeTrainyardCosmic, weekly) and
Endless Danger Room highest wave (Events/Anniversary2016, all-time, rule 54; `dangerroom/edr_leaderboard.csx`).
Only boards the CLIENT's data marks public / Live are listed in game: patching a private board (e.g. TestLeaderboard) in
Data/Game/Patches makes the server use it, but players never see it. Patch `PrototypeId` values must be numbers, not paths.
Careful when picking a board: many inherit their name / description text from a parent board, and renaming changes that shared
text on every board using it (e.g. the Epic time boards all inherit Brood Epic's name). Pick boards with their own Name. Free boards that fit: Events/SummerEvent and AgentsOfSHIELDEvent (4 hours, highest first),
the other DangerRoom/DRScenarioTime* boards (weekly, fastest first; not the Cable ones, EDR runs in the Cable region), PvP/* (weekly).

## Saved state (C#)

- `ScriptStorage.Load(name)` / `Save(name, pairs)`: a small key / value file per script in `Data/ScriptData`, so progress survives
  restarts and reloads. Read and written whole: save about once a minute, not on every change.
- `events/community_goal.csx` uses it: every kill on the server counts toward shared stages (`Stages`), contributors with
  `MinContribution` kills get each stage's rewards (also when they log in later). `!goal`, `!goal top`.

## Item prices (C#)

- `ScriptRewards.CountItems(player, itemPath)` / `TakeItems(player, itemPath, count)`: count or take items from a player's backpack
  and general stash (all or nothing), e.g. an event currency price. `events/halloween.csx` (Trick or Treat: candy and pumpkin drops,
  boss-kill tricks and treats, Ghost trading candy for Halloween Mystery Bags) uses them.
- `ScriptRewards.DropLootTable(player, lootTablePath, slot)`: rolls a game loot table for the player and drops the result like a
  kill would (items, credits, orbs, banner messages); loot cooldowns are ignored. `IsValidLootTable(path)` checks a path.
  `events/halloween.csx` uses it for the original Halloween loot explosion.
- `ScriptConditions.ApplyEffect(entity, key, effectName)`: puts a client condition effect (a glow, aura, spotlight, size change...)
  on an entity by its client class name without the `MarvelConditionEffect_` prefix, with no stats. `FindEffects(filter)` lists the
  names available. `tests/fx_test.csx` (`!fx find <text>`, `!fx <name>`, `!fx off`) is for trying them.
- `ScriptCombatAI.Attach(agent, leader, options, powers)`: gives any agent a combat brain that replaces its own AI and works out
  from the game data what each of its powers is for (attack, area, buff, debuff, heal, summon, toggle, dash, signature, ultimate):
  upkeep first, then the best-scoring attack for the situation, big cooldowns saved for bosses / elites / packs. `leader` (optional)
  is who it follows and fights around. `CombatAIOptions` holds every tuning value; `LogDecisions` logs each choice and why.
- EXPERIMENTAL `ScriptMirrorImages.Spawn(avatar, options)` / `Count` / `Clear`: allies that clients draw as the player's own hero
  and costume, with the hero's powers, talents and traits and a `ScriptCombatAI` brain (`MirrorImageOptions`, with `AI` inside).
  `tests/mirror_test.csx` (`!mirror`, `!mirror <n>`, `!mirror clear`) is for trying them.
  `ScriptMirrorImages.EnableAutoSpawn(avatar, options, cooldownSeconds)` / `DisableAutoSpawn(avatar)`: the avatar gets an image
  automatically when it hits an enemy (one at a time). `items/set_bonuses.csx` uses it for the Legion of One 4 piece bonus
  (a `SetTier` can take `mirror:` and `mirrorCooldownSeconds:`).
- `ScriptRewards.BindOnPickup(itemPath)`: the item binds to the account when picked up even if the server has account binding
  switched off: it cannot be traded and is destroyed when dropped. `ItemPickedUp` args have `WasDroppedByPlayer`, true when the
  item was on the ground because a player dropped it: ignore those when counting pickups.
- `events/community_goal.csx` also scores every contribution on a leaderboard (the Summer Event board, renamed). Its prize boxes are
  renamed by the script and refilled by `Data/Game/Patches/PatchDataHalloween.json`; enable the board in `LeaderboardSchedule.json`.
- `events/event_welcome.csx` shows players a popup about the running events once (until they click "Got it!"); edit its
  `Pages`, and change `PopupId` to show a new popup for the next event.

## Gear bonuses and custom procs (C#)

- `ScriptBonuses.Apply(agent, key, propertyCollection)` / `Remove(agent, key)`: merge extra stats and procs into a character the
  same way an equipped item does. Server-side only: combat math uses them, the character sheet does not show them.
  Pass `clientVisible: true` (heroes only) to send the stats to the client as well, through an invisible condition: needed for
  stats the client must know (area sizes, cooldowns, charges) and shows them on the character sheet. Procs stay server-side.
  Boost one power with `bonus[PropertyEnum.DamageMultForPower, powerRef] = 0.25f` (name the power that deals the hit, often a combo).
- `ScriptBonuses.AddProc(bonus, ProcTriggerType.OnSuperCrit, "Powers/ItemPowers/...", chance [, threshold])`: proc an existing power.
- `ScriptBonuses.CountEquipped(agent, itemRef)`.
- `ScriptConditions.ApplyPowerCondition(entity, key, powerPath [, conditionIndex, duration, properties])` / `Remove` / `Has`: put a
  power's condition on an entity without using the power, with no stats unless given some. Borrows its client visual (size, glow,
  particles). Zero duration = until removed. A hero loses conditions on region change / hero swap: reapply from
  `ScriptHooks.AvatarEnteredWorld` (`e.Avatar`, `e.Player`). Set tiers take a `visualPower:` and sets a `heroes:` list. `tests/size_test.csx` (`!grow`) uses Giant-Man's 4x grow.
  Client size rules: item size effects multiply and are capped at 0.7x-1.3x; only effects with OverrideMaxScale (Giant-Man grow 4x,
  Juggernaut/Venom ultimates 1.5x) raise the cap, to their own size.
- `ScriptText.OverrideItemFlavorText(itemPath, text)` / `OverrideText(stringId, text)`: replace existing game text (after reconnect).

`items/set_bonuses.csx` builds two test gear sets from these (`!sets` shows your progress).

`events/midtown_boss_rush.csx` uses all of these: it starts when the first player enters Cosmic Midtown Patrol, restarts 5 minutes
after each rush ends, and drops per-wave rewards (`!bossrush` shows status). Bosses never spawn around the players: they use
the named `SpawnSpots`, or without any, a random spot far from the entrance / hub. `SpawnSpots` lists named map positions (`!bossrush here <name>` prints the line for where you stand; Midtown's map is the same in every instance).
Unnamed spots are ignored, and crowded spots spread bosses wider around the spot instead of spawning them near the players.

## C#

```csharp
Hooks.On(ScriptHooks.EntityKilled, e =>
{
    Player player = e.Killer?.GetOwnerOfType<Player>();
    if (player != null)
        ScriptHooks.SendChatMessage(player, "Nice kill");
});
```

Globals: `Hooks`, `Log`, `ScriptName`, `IsLoaded`, `After(game, seconds, action)`. Handlers run on game threads: keep them fast, use thread-safe collections for shared state.
Compiled C# scripts stay in memory until restart, so frequent hot reloads of `.csx` files use a little more memory each time.

## Adding a hook

1. Declare it in `ScriptHooks.cs` and add it to the name table.
2. Add an args class in `ScriptHookArgs.cs`.
3. Invoke it from the simulation: `if (ScriptHooks.X.HasHandlers) ScriptHooks.X.Invoke(new(...));`
