# Script bridge: engine files touched

What another MHServerEmu fork needs to take to run the script bridge (`.csx` and `.lua` scripts in `Data/Scripts`).

**How this list was made.** There is no clean upstream copy to diff against, so it was built by searching this tree for every
reference to the scripting types and for the `CUSTOM` comments that mention scripts. Line numbers are from this tree on
2026-10-06 and will drift. Expect the compiler to find a few more dependencies on a different fork (see "Likely extra
dependencies" at the end): add the core, build, and fix what it reports.

Paths are relative to `src/`.

---

## 1. Core (required)

### New files: copy as they are

| Path | What it is |
|---|---|
| `MHServerEmu.Scripting/` (whole project) | The loader: `ScriptManager.cs`, `ScriptingConfig.cs`, `ScriptLog.cs`, `IScriptHost.cs`, `CSharp/CSharpScriptHost.cs`, `CSharp/ScriptGlobals.cs`, `Lua/LuaScriptHost.cs`, the `.csproj`. NuGet: `MoonSharp 2.0.0`, `Microsoft.CodeAnalysis.CSharp.Scripting 4.8.0`. The `.csproj` also copies `Scripts/**` to `Data/Scripts` in the build output. |
| `MHServerEmu.Scripting/Scripts/README.md` | The script API reference for script authors. |
| `MHServerEmu.Games/Scripting/ScriptHook.cs` | The hook type (handler list, `HasHandlers`, `Invoke`). |
| `MHServerEmu.Games/Scripting/ScriptHooks.cs` | Every hook, plus `IsAdmin` and `SendChatMessage`. |
| `MHServerEmu.Games/Scripting/ScriptHookArgs.cs` | Argument classes for the general hooks. |
| `MHServerEmu.Games/Scripting/ScriptTimer.cs` | `After(game, seconds, action)` on the game's event scheduler. |
| `MHServerEmu.Games/Scripting/ScriptStorage.cs` | Small key/value files for scripts (plain file IO). |

### Edits to existing files

| File | Line | Change |
|---|---|---|
| `MHServerEmu.Core/Network/ServerManager.cs` | 18 | New `GameServiceType.Scripting` enum value, placed before `Frontend` so scripts load before players can connect. |
| `MHServerEmu/ServerApp.cs` | 21, 126 | `using MHServerEmu.Scripting;` and `serverManager.RegisterGameService(new ScriptManager(), GameServiceType.Scripting);` |
| `MHServerEmu/MHServerEmu.csproj` | 41 | Project reference to `MHServerEmu.Scripting`. |
| `MHServerEmu.sln` | | Add the `MHServerEmu.Scripting` project. |
| `MHServerEmu/Config.ini` | 281 | New `[Scripting]` section: `EnableScripting`, `ScriptDirectory`, `EnableLua`, `EnableCSharp`, `HotReload`. |

### Hook call sites (one or two lines each)

Each is guarded by `ScriptHooks.X.HasHandlers`, so it costs nothing when no script listens.

| Hook | File | Line | Where it fires |
|---|---|---|---|
| `ChatCommand` | `MHServerEmu.Games/Social/ChatManager.cs` | 51, 76-77 | Before the normal chat handling; `!command` lines go to scripts first (`TryInvokeScriptCommand`). |
| `PlayerEnteredRegion` | `MHServerEmu.Games/Network/PlayerConnection.cs` | 475-476 | After the player has entered a region. |
| `AvatarEnteredWorld` | `MHServerEmu.Games/Entities/Avatars/Avatar.cs` | 7280-7284 | When an avatar enters the world (line 7282 belongs to the Bonuses pack). |
| `EntityInteracted` | `MHServerEmu.Games/Entities/Avatars/Avatar.cs` | 4400-4401 | When a player clicks an interactable entity. |
| `EntityKilled` | `MHServerEmu.Games/Entities/WorldEntity.cs` | 375-376 | When any world entity dies. |
| `EquipmentChanged` | `MHServerEmu.Games/Entities/Agent.cs` | 1885-1886, 1908-1909 | Item equipped / unequipped on an agent. |
| `ItemPickedUp` | `MHServerEmu.Games/Network/PlayerConnection.cs` | 1074-1086, 1124-1126 | Item picked up from the ground (two code paths). Uses `ScriptRewards.WasPlayerDropped` (Rewards pack). |
| `ConditionApplying` | `MHServerEmu.Games/Powers/PowerPayload.cs` | 2327-2334 | Before a condition is applied; a script can cancel it. |
| `EnemyAffixesRolled` | `MHServerEmu.Games/Populations/ClusterObject.cs` | 588-590 | After a spawn group's affixes are rolled; a script can change the list. |
| `GlobalEventRequested` | `MHServerEmu.Games/Network/PlayerConnection.cs` | 2283-2294 | When the client asks for global event data. |

---

## 2. Feature packs (optional)

Each pack is the API file(s) plus the engine edits that API needs. Skip a pack and the scripts that use it will not compile.

### Spawning and encounters

- New: `MHServerEmu.Games/Scripting/ScriptSpawner.cs`, `MHServerEmu.Games/Scripting/ScriptCombatAI.cs`,
  `MHServerEmu.Games/Populations/EnemyBoostCatalog.cs` (the affix catalogue `AddRandomAffixes` picks from).
- `MHServerEmu.Games/Network/PlayerConnection.cs` 1053-1054: scenery items placed by scripts cannot be picked up (`IsSceneryItem`).
- `MHServerEmu.Games/Entities/WorldEntity.cs` 3524, 3566, 3958, 4838: `IsDiscoverable` guards, so script-spawned entities that are
  not on the discovery channel do not log "not discoverable" warnings.

### Rewards and item binding

- New: `MHServerEmu.Games/Scripting/ScriptRewards.cs`.
- `MHServerEmu.Games/Entities/Items/Item.cs` 190-191: an item binds on pickup if a script marked it (`IsAlwaysBound`), even with account binding off.
- `MHServerEmu.Games/Entities/Player.cs` 1374-1375: marks an item a player dropped (`MarkPlayerDropped`), so re-picking it up is not a new find.

### Bonuses, conditions and visual effects

- New: `MHServerEmu.Games/Scripting/ScriptBonuses.cs`, `MHServerEmu.Games/Scripting/ScriptConditions.cs`.
- `MHServerEmu.Games/Entities/Avatars/Avatar.cs` 7282: `ScriptBonuses.OnAvatarEnteredWorld(this)` puts client-visible bonuses back after a region change.

### On-screen text, dialogs and HUD widgets

- New: `MHServerEmu.Games/Scripting/ScriptText.cs`, `ScriptDialog.cs`, `ScriptPresentation.cs`.
- `MHServerEmu.Games/Achievements/AchievementDatabase.cs` 34-39, 193-202, 220+: custom strings registered at runtime are merged into
  the achievement string dump the client receives at login. This is how script text reaches the client, and why new text only
  shows after a reconnect.
- `MHServerEmu.Games/UI/Widgets/UIWidgetEntityIconsSyncData.cs` 262-330: `AddScriptEntity` and its remove / defeated counterparts,
  for boss health bars on entities the region's own metagame does not track.
- `ScriptDialog` uses the existing `GameDialogManager`; no engine edit.

### Teleports and forced difficulty

- New: `MHServerEmu.Games/Scripting/ScriptTeleport.cs`.
- `MHServerEmu.Games/Regions/Teleporter.cs` 39, 67, 210-212: `ForceDifficultyTier`, so an event can pick its difficulty whatever
  the party or player preference is.

### Script-built maps and layout tweaks

- New: `MHServerEmu.Games/Scripting/RegionGeneratingArgs.cs` (also holds `RegionGenerationOverrides`), `ScriptedLayout.cs`,
  `MHServerEmu.Games/DRAG/Generators/Areas/ScriptedLayoutGenerator.cs`.
- `MHServerEmu.Games/Regions/Region.cs`
  - 89-90: `GenerationOverrides` property.
  - 291-303: raises `RegionGenerating` before areas are generated and stores the overrides.
  - 1068-1073, 1112-1113: an empty target into a built map resolves to its arrival room.
  - 1123-1181: `TryGetBuiltMapStartPosition` (finds a walkable arrival spot in the map's start room).
- `MHServerEmu.Games/Regions/Area.cs` 115-119: a script layout replaces the area's own generator.
- `MHServerEmu.Games/DRAG/Generators/Areas/BaseGridAreaGenerator.cs` 53-61: room and connection removal chances can be overridden per region.
- `MHServerEmu.Games/DRAG/CellSetRegistry.cs` 176+: load every cell of a set by folder name.
- `MHServerEmu.Games/Network/TransferParams.cs` 85-86: players arriving in a built map are placed in its arrival room.
- `MHServerEmu.Games/Entities/Avatars/Avatar.cs` 540-541: respawn in the arrival room (built maps have no respawn target).

### Leaderboards

- New: `MHServerEmu.Games/Scripting/ScriptLeaderboards.cs`.
- `MHServerEmu.Games/Leaderboards/LeaderboardManager.cs` 156, 192: a board a script has taken over ignores its own scoring rules (`IsScriptOwned`).

### Cosmic-only loot on higher difficulties

- `MHServerEmu.Games/GameData/Prototypes/LootRollModifierPrototype.cs` (`LootRollRequireDifficultyTierPrototype`): a loot
  restriction that lists `Tier3Superheroic` (the game's Cosmic difficulty) also passes on `Tier4Cosmic` and `Tier5Omega1`.
  122 restrictions in 88 stock tables (most bosses' Cosmic artifacts, Cosmic EGMs, Cosmic bonus drops) listed Cosmic alone,
  so they never rolled on Omega. No stock list names Tier4Cosmic / Omega without Cosmic, so nothing drops twice.

### Item affix rolls (Omega items)

- New: `MHServerEmu.Games/Scripting/ItemAffixesRollingArgs.cs`.
- `MHServerEmu.Games/Loot/LootUtilities.cs`
  - 217-238: raises `ItemAffixesRolling` before the normal roll, then applies the per-stat cap.
  - After the normal roll: runs the work a script queued with `ItemAffixesRollingArgs.AfterNormalRoll` (errors are logged,
    the item still drops), so extras such as the Omega rarity and stacked affixes go on top of a normally rolled item.
  - 322-430: `AddUniqueRandomAffixes` (stacked / unique extra affixes, tier and hero filter).
  - 446-528: `EnforceAffixStatCap`, `IsCappableAffixPosition`.
  - 530-560: `AllowsTierAndHero`.
  - `CopyAffixSpecs` and `IsOmegaRarity`: Omega-rarity items keep stacked duplicates when affixes are copied.
- Related, outside the script bridge: `Entities/Items/Item.cs` (duplicate affix merging, `AppliesAffixToOwnerAvatar`),
  `Properties/ReplicatedPropertyCollection.cs` 154-175 (aggregate values pushed to the client for items). These belong to the
  Omega item system itself.

### Mirror images (an ally drawn as the player's hero)

- New: `MHServerEmu.Games/Scripting/ScriptMirrorImages.cs` (needs `ScriptSpawner` and `ScriptCombatAI`).
- `MHServerEmu.Games/Entities/EntitySettings.cs` 81-83, 128-129: `ClientAvatarPrototypeRef`, `ClientAvatarName`.
- `MHServerEmu.Games/Entities/WorldEntity.cs`
  - 208-209: reads those settings at creation.
  - 298: writes the avatar fields into the replication archive.
  - 2315-2317: tells `ScriptMirrorImages` when a hero damages an enemy (spawn on hit).
  - 4371-4419: `IsClientRenderedAsAvatar`, `SpoofAvatarWorldInstanceId`, `InitClientRenderAsAvatar`, `GetClientPrototypeDataRef`.
- `MHServerEmu.Games/Network/ArchiveMessageBuilder.cs` 117, 196, 213, 317, 560, 589, 603: create and enter-world messages send the avatar prototype and instance id for these entities.
- `MHServerEmu.Games/Network/AreaOfInterest.cs` 752-756, 765-768: hierarchy update messages are sent for them as for avatars.
- `MHServerEmu.Games/Powers/Power.PowerEvents.cs` 1502-1503, 1530: mapped-power events are skipped for them.
- `MHServerEmu.Games/Powers/PowerPayload.cs` 976-977, 1037-1044: optional damage logging and the image's damage scale.

### Web portal (a page in the client's browser that talks to scripts)

- New: `MHServerEmu.WebFrontend/Handlers/Portal/PortalWebHandler.cs` (serves the page), `PortalActionWebHandler.cs` (receives its
  actions), `MHServerEmu.WebFrontend/Data/Web/Portal/index.html` (test page), `Scripts/tests/web_test.csx`.
- `MHServerEmu.Core/Network/GameServiceProtocol.cs`: `WebActionRequest`, `WebActionResponse`, `WebActionGameRequest`,
  `WebActionGameResponse` (region "Web Actions").
- `MHServerEmu.PlayerManagement/Network/PlayerManagerServiceMailbox.cs`: `OnWebActionRequest` (checks the session's platform
  ticket, like Add G) and `OnWebActionGameResponse`, plus their two `case` lines.
- `MHServerEmu.Games/Network/InstanceManagement/GameInstanceService.cs`: routes `WebActionGameRequest` to the player's game.
- `MHServerEmu.Games/Network/GameServiceMailbox.cs`: `OnWebActionGameRequest` raises the `WebAction` hook and always answers.
- `MHServerEmu.Games/Scripting/ScriptHooks.cs` / `ScriptHookArgs.cs`: the `WebAction` hook and `WebActionArgs`.
- `MHServerEmu.WebFrontend/Network/GameServiceTaskManager.cs` (`DoWebActionAsync`), `WebFrontendServiceMailbox.cs` (one `case`),
  `WebFrontendService.cs` (registers `/Portal` and `/Portal/Action`), the `.csproj` (copies the page to the output).

### Danger Room hooks (only for forks that have the Endless Cable Scenario)

- New: `MHServerEmu.Games/Scripting/DangerRoomHookArgs.cs`.
- `MHServerEmu.Games/EndlessScenarios/EndlessCableScenarioManager.cs` 1262-1266 (`DangerRoomWaveCleared`), 1761-1766
  (`DangerRoomWaveStarting`), 2198-2203 (`DangerRoomFinished`), 2355-2360 (`DangerRoomRewards`), 3440 (`ScriptTeleport.ToTown`).
- On a fork without that system: leave out the file and the four hooks in `ScriptHooks.cs`.

---

## 3. Which scripts need what

| Scripts | Needs beyond the core |
|---|---|
| `tests/slay.csx`, `fixes/disable_party_boost.csx`, `omega/omega_knockback_immunity.csx`, `omega/omega_affixes.lua` | Nothing (the last two need a difficulty tier with "Omega" in its name to do anything). |
| `events/halloween.csx`, `events/community_goal.csx`, `events/event_welcome.csx` | Spawning, Rewards, Text / dialogs, Leaderboards. |
| `events/midtown_boss_rush.csx` | Spawning, Rewards, Text / widgets, Leaderboards. |
| `events/terminal_breach.csx` | Spawning, Rewards, Text / widgets, Teleports, Layout tweaks, Leaderboards. |
| `events/dark_dimension_incursion.csx` | Spawning, Rewards, Bonuses, Text / widgets, Teleports, Script-built maps. |
| `items/set_bonuses.csx` | Bonuses and conditions, Mirror images. |
| `omega/omega_items.csx` | Item affix rolls. |
| `dangerroom/*` | Danger Room hooks, Leaderboards. |

---

## 4. Likely extra dependencies on another fork

The API files call into this fork's engine. These are the members most likely to be missing or different elsewhere:

- `Game.CustomGameOptions` (e.g. `DisableAccountBinding`) and `ChatManager.SendChatFromCustomSystem`.
- `Region.IsOmegaDifficulty` (used by the `IsOmega` properties in `ScriptHookArgs.cs`).
- `RegionPrototypeId` enum values used by scripts (e.g. `XManhattanRegion60Cosmic`).
- `LeaderboardInfoCache` and the leaderboard service, which `ScriptLeaderboards` writes to.
- Admin detection in `ScriptHooks.IsAdmin` (account user levels).

The scripts also name prototypes by path. They are 1.52 paths; several were chosen because the 1.52 client has a model for
them, so check them on any other client version.

## 5. Plain bug fixes (not needed for scripts, useful on any fork)

- `MHServerEmu.Games/Entities/Items/Item.cs` `InteractWithAvatar`: on-use actions with pick method `PickWeightTryAll` were
  skipped, so the Anniversary "Leveling Artifact Box" did nothing when clicked. A new branch tries the actions in turn.
- `MHServerEmu.Games/DRAG/Generators/Areas/StaticAreaCellGenerator.cs`: `CellSubstitutions` (cell resource path -> another
  cell) lets scripts swap single district tiles; `events/xmas_hightown.csx` uses it to fill the 8 Christmas Hightown tiles
  the 1.52 client has no map for. An empty value leaves the tile out (`tests/civilwar_tiles.csx`). A substitute from another
  tile set is moved so its center sits on the original's (district tiles carry their position in their own bounds), plus an
  optional nudge from `CellSubstitutionOffsets`; tiles with matching bounds (Christmas Hightown) are unaffected.
  `PreGenerate` now moves each cell's box to its position (`BoundingBox.Translate`) instead of `BoundingBox + position`,
  which only grows the box to take in the point: the same while every marker sits at the origin, but a moved
  substitute left the area too small ("Failed to generate areas", navi AddPoint errors).
- `CellSubstitutionSpawnMarkers` (opt-in per original path): the stand-in takes over the original cell's spawn markers
  (`SpawnMarkerRegistry.AddStandInCell`), each moved to the stand-in's nearest own spawn spot; rarest marker types pick
  first. Needed when a scenario puts bosses on the original's markers (Civil War Bazaar: BossTiny/BossSmall/BossMedium).
  Every stand-in records `CellSettings.StandInForRef`, and `SpawnLocation` RestrictToCells matches stand-ins too.
- `Player.KismetSeqFallbackSeconds` (by prototype name or path): a blocking cutscene is still sent, but if the client
  hasn't reported it finished after that many seconds the server finishes it (`OnPlayKismetSeqDone`), so missions waiting
  on it go on when the loaded map doesn't have the scene (a rebuilt map).
- `Player.SkippedKismetSeqs` (by prototype name or path): cutscenes not sent to the client at all; the player's region
  gets `KismetSeqFinishedEvent` a second later (testing which Civil War Airport scenes the 1.52 client can play). A value
  of false holds the scene instead: never sent, never finished, so whatever waits on it (the Airport bosses) never comes.
- `SingleCellAreaGenerator.CellReplacements` (by the original cell's path): a single-cell area built from several cells,
  each given as a cell path and its center relative to the original's center; neighbors are connected. Every part is a
  stand-in (`StandInSharesOriginal`): it takes over only the original's spawn markers that fall on it, and places the
  original's `InitializeSet` entities that fall on it (`Cell.InstanceStandInInitializeSet`, e.g. the region exit).
  District pieces (`IsOffsetInMapFile`, map and navigation built at a fixed spot) can't be moved: they are placed at their
  own spot and the original's markers are mapped onto the middle of the pieces instead. A plan can't mix them with tiles.
  A stand-in with no spawn spots of its own keeps the original's markers in place (pulled inside it).
  Transitions on a stand-in find their destinations through the replaced cell too (`TransitionDestination.
  AddDestinationsFromConnectionTargets`, random instances in `Transition.OnEnteredWorld`), and log where they lead.
  `Transition.OnEnteredWorld`: a region origin that names no region (a region a script teleported the player to) is no
  longer used as a destination (the client won't use a portal that leads nowhere); it falls through to the default region.
- `MHServerEmu.Games/Network/AreaOfInterest.cs` `ClientAreaOverrides` (area path -> area path): the area id sent to the
  client in `NetMessageAddArea` is another area's, so the client loads that area's backdrop map (`ClientMap`). The server
  keeps the real area. Used for the Civil War Airport in the helicarrier hangar (shown as `HelicarrierInvasionArea1`).
- Public event teams (the Civil War choose-a-side panel): `PlayerConnection.OnSelectPublicEventTeam` handles the client's
  `NetMessageSelectPublicEventTeam` (was commented out): saves the team with `Player.SetPublicEventTeam` (the
  `PublicEventTeamAssignment` property, saved to the database), answers `NetMessagePublicEventTeamAssigned`, and raises
  `ScriptHooks.PublicEventTeamSelected`. `Player.GetPublicEventTeamPrototype` returns the saved team (was a stub returning
  null). `PublicEventPrototype.PostProcess` linked no team to its event (inverted check); fixed. Script: `events/civil_war_teams.csx`.
  `Player.SetPublicEventTeam` raises `Region.PlayerEventTeamChangedEvent` (the "member of event team" mission condition
  waits for it), and `MissionActionShowTeamSelectDialog.Run` opens the panel (was empty). With the Civil War story missions
  switched back on (`Data/Game/Patches/PatchDataCivilWar.json`) the game's own progression and dailies run;
  `events/civil_war_event.csx` adds the rebuilt maps, warp NPCs and exits.
- `MHServerEmu.Games/Missions/Actions/MissionActionPlayerTeleport.cs`: a player still on a loading screen is teleported
  once loading is done (retried every second, up to 30 s): a mission teleporting someone as they arrived left the client
  stuck loading. `SkippedFor` (parts of mission paths) turns a mission's teleports off (the Civil War Bazaar's auto-return).
- `MHServerEmu.Games/Regions/Cell.cs`: a stand-in doesn't carry over an initial entity it already places itself (e.g. its
  own exit, at its own floor height), and `Cell.StandInSkippedEntities` (prototype paths) leaves out some of a stand-in's
  own initial entities (the hangar's doorway to the rest of the helicarrier); it also covers entities carried over from the
  replaced cell. `Cell.StandInBareCells` (prototype paths): stand-ins that place none of their own entities from either
  marker set (the helicarrier hub as Team Iron Man's hideout; the script places the hideout's NPCs).
- `MHServerEmu.Games/Regions/Region.cs` `FindTargetLocation`: `ArrivalFallbackFor` (region names) makes players arrive in the
  first cell when the region's arrival marker is missing, instead of hanging on the loading screen. Opt-in, set by scripts.
- `MHServerEmu.Games/Regions/RegionManager.cs`: `GenerationLogFor` (region names) turns on the detailed map-generation log for
  matching regions; `MHServerEmu.Games/DRAG/Generators/Areas/BaseGridAreaGenerator.cs` `TrySpawningSuperCell` logs (with that
  log on) the grid's external openings, why each spot was refused, and where a set piece was placed.
- `MHServerEmu.Games/DRAG/CellSetRegistry.cs`: `CellSetFallbacks` (cell set folder -> backup folder) adds the backup's
  cells for wall shapes a set is missing; `events/winter_docks.csx` uses it for the incomplete winter Docks Patrol sets.
- `MHServerEmu.Games/Scripting/ScriptCombatAI.cs` `Use`: hold-and-release powers (Colossus' Titan Throw) are released right
  after they start, so script-driven agents no longer freeze in the wind-up.
- `MHServerEmu.Games/Entities/Avatars/Avatar.cs` `SetActivePowerSpec`: Rogue keeps a stolen power set per power spec. The
  set she leaves is saved (`Data/ScriptData/RogueSpecStolenPowers.tsv`, key avatar db id + spec), her slots are cleared,
  and the new spec's saved set is put back (a spec she hasn't used yet starts as a copy of the one she left). Powers she
  no longer has are skipped. Other avatars: mapped powers are still cleared on a spec swap, as before.
  `UnassignMappedPowersForRespec`: the Rogue check was inverted (a respec wiped only Rogue's stolen powers and left her
  hotbar pointing at removed powers). Rogue now keeps them, re-applied with `MapPower` so reset slots get them back.
