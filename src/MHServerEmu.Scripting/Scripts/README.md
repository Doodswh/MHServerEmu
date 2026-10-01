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

- `ScriptRewards.DropItem(player, "Entity/Items/.../X.prototype", count, slot)` drops reward items owned by that player at their feet.

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

`events/_dark_dimension_incursion.csx` (ON HOLD, disabled by the leading `_`; remove it to turn the event back on): Doctor Strange in Avengers Tower sends a party into a hand-built Limbo map: four Dark Dimension rifts
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

## Gear bonuses and custom procs (C#)

- `ScriptBonuses.Apply(agent, key, propertyCollection)` / `Remove(agent, key)`: merge extra stats and procs into a character the
  same way an equipped item does. Server-side only: combat math uses them, the character sheet does not show them.
- `ScriptBonuses.AddProc(bonus, ProcTriggerType.OnSuperCrit, "Powers/ItemPowers/...", chance [, threshold])`: proc an existing power.
- `ScriptBonuses.CountEquipped(agent, itemRef)`.
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
