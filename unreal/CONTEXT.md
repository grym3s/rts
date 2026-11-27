# unreal/

Unreal presentation shell (migration slices 3-4). No game rules here — the .NET sim
host (`tools/sim-host/`, protocolVersion 1 over newline-delimited JSON) is the sole
simulation authority. See `docs/superpowers/specs/2026-09-26-unreal-rts-migration-design.md`.

## Layout
- `RtsBridge.uproject` — EngineAssociation 5.8; one Runtime module `RtsBridge`.
- `Source/RtsBridge/Public/RtsSimHostBridge.h` — process bridge: `FPlatformProcess`
  pipes only (cross-platform Linux/Windows); locates host via `RTS_HOST_BIN` env or
  `unreal/dist/<rid>/sim-host`; validates `protocolVersion==1` + `ok` envelope;
  decodes Q32.32 snapshot positions and hp/maxHp (raw / 2^32) and the step's `events`
  (`FRtsSimEvent`: kind, subject, target, amount, faction); EOF-then-terminate shutdown.
  Test: `RtsBridge.Host.HealthAndDeath` (hp drops, shot amounts, death event = roster exit).
  T35: also decodes the T30 economy: `state.factions` (`FRtsFactionSnapshot`: credits, power
  supply/draw), `state.veins` (`FRtsVeinSnapshot`), and per unit `buildingId`, construction
  remaining/total, production queue + head ticks, footprint w/h, `deploysTo`, harvest/load/carrying.
  The game mode keeps them as `GetFactionState` / `GetVeins` / `GetUnitRow` (latest snapshot).
  Test: `RtsBridge.Host.Economy` (MCV deploy, refinery, shipped harvester filling).
  T36: the init reply's `catalog` (`FRtsBuildingType` / `FRtsUnitType`, kept as `GetInitReply()`)
  and the read-only `query` kind (`QueryPlace(faction, building, cell)` -> allowed, reason, footprint
  `QueryRect`). Test: `RtsBridge.Host.BuildMenu` (catalog, prereq/blocked/radius verdicts, query
  never advances the tick, sidebar layout/hit).
  T37: per building `rally` (bHasRally, RallyX/Y in cells). Test: `RtsBridge.Host.Production` (train x2,
  cancel refunds, rally decoded, the rifleman walks there; panel layout/hit).
  T38: harvester rows add `LoadCapacity`. Test: `RtsBridge.Host.Harvest` (vein block sizes; a `harvest`
  body sends a collector to the named vein and leaves the near one untouched).
  T25: unit rows and unit types add `Domain` (land / naval / hover / air); aircraft are drawn
  `ARtsSimUnitActor::AirZ` above the ground (`SetAirborne`). Test: `RtsBridge.Host.Domain` (domain
  parse from a terrain-row scenario; water / shore reason texts).
  T26: `Bridges` (id, hp, cells, broken) and `bEngineer` on unit rows. The game mode patches its
  terrain rows when a bridge falls (`~`) or is mended (`=`), rebuilds the terrain actor and bumps
  `GetTerrainVersion` (the minimap re-renders). Orders: RMB on a damaged/fallen bridge sends
  selected engineers to mend it; Ctrl+RMB or an attack-move click on a standing bridge sends
  selected armed units to shoot it (`RequestBridgeAt`). Damaged bridges get an orange HP bar,
  `bridge_hit` events draw tracers. Test: `RtsBridge.Host.Domain` also checks bridge + engineer parse.
  T29: unit rows add `Type`, `bMarked`, `bConscriptable`, `PhaseTicks` (-1 = can't phase) and
  `PhaseCooldown`. The HUD puts a red pip over marked units and a cyan ring under phased ones. Phase (Q)
  and Conscript (C) are on the ability bar (see Abilities below); Coalition's Marking is passive.
  Faction pick: in single player (`CanRestart`) a "Choose your faction" panel shows before the
  first tick and again after Play again; the sim does not step until a button is clicked
  (`ChooseFaction`). `ARtsBridgeGameMode::PlayerFaction` (static, sim index 0/1/2) then decides
  everything that is "yours": command `faction`, selection, build menu and place queries,
  credits/power, rally markers, tooltips' "Yours", camera start, VICTORY/DEFEAT. Units keep
  their faction colours. Test: `RtsBridge.UI.FactionPick`. Panel clicks (pick, result screen,
  build menu, training panel) are hit-tested in the HUD's canvas size (`HudViewport`), the space the
  panels were laid out in; `GetViewportSize` can differ in PIE, which made pick clicks miss.
  Each pick click logs `RTS_FACTION_CLICK`.
  T28: unit rows add `bCanCapture`, faction rows `Trees` (`HasTree`). RMB on an enemy or neutral
  building, or an enemy MCV, sends selected capturing engineers to take it and the rest to attack
  it (`RequestCaptureOrAttack`; neutrals can't be attacked, so with no engineer it is a plain move).
  A captured actor re-tints to its new owner; neutral is grey. Trainable lists ignore the unit's
  faction (a captured barracks offers its own units); the build menu lists every tree in `Trees`.
  A destroyed derrick (`bWrecked`) draws as a dark slab until an engineer restores it (same RMB).
  Bridge huts (rows with `Bridge` >= 0) are grey 1x1 markers; RMB on one with engineers repairs
  its bridge. Buildings are picked anywhere on their footprint (`PickUnitImpl`), not only near
  their centre. F4 labels carry the display name; hovering anything (unit, building, bridge) shows
  a tooltip: name, owner and hp, and what you can do with it (`TooltipLines`, `BridgeTooltipLines`).
  Mindbender / Weaver: rows add `bMindControl`, `bRepairs`, `ChannelTarget` / `Channel` /
  `ChannelNeed`, `ControlledBy`. RMB on an enemy with a Mindbender selected sends `capture`; RMB on
  your own damaged building with a Weaver sends `capture` (`RequestRepair`). The HUD draws a purple
  channel bar with seconds left over the Mindbender and its target, joined by a line, and a faint
  line to each unit it holds (`DrawChannelBars`).
  Test: `RtsBridge.Host.Capture`.
  Abilities: rows add the ability state (`Sight`, `Radar`, `Inside`, `bCloaked` / `Detected`, `Shield`,
  `Sabotaged`, `Holds` / `Capacity` / `Used`, `bTeleport`, `Charge` / strike, rig `Control` / hold).
  Picking a faction re-inits the tick-0 world with the other two sides as host computer players
  (`RtsContent::WithComputerPlayers`, scenario `ai`). Ability bar (bottom centre, above the production
  panel, `AbilityButtons` / `AbilityLayout` / `AbilityHit`) and keys: Q phase, C conscript, U unload,
  T teleport (then LMB one of your Teleport Nodes), G fire a charged superweapon (then LMB the target,
  blast radius under the cursor), E deploy; RMB cancels a pending target. RMB your own transport, bunker
  or tunnel loads the selection (`RequestLoad`); Operatives / Hijackers take the capture path. Fog of war
  (presentation; F6 toggles): `FogCell` 2x2-cell grid, never seen / seen / in sight from your units' sight
  and Seam Radar radius; the view gets a soft dark overlay (one batched triangle item), the minimap dark
  runs. Enemy units outside sight, cloaked enemies without your detector and every passenger are hidden
  and left out of `UnitActors` (so never picked, hovered or on the minimap); enemy buildings stay shown
  once seen. A coming strike is a pulsing red ring with its countdown; rigs show their state and a hold
  bar; your cloaked units get a faint blue ring; tooltips cover every ability.
- `Source/RtsBridge/Private/RtsBridgeRoundTripTest.cpp` — editor automation test
  `RtsBridge.Host.RoundTrip` (real host launch, init, tick-0 move, 120 steps).
- `Config/DefaultEngine.ini` — GlobalDefaultGameMode = RtsBridgeGameMode;
  GlobalDefaultServerPlayerControllerClass = RtsRtsPlayerController (that alone does NOT
  set the local player's controller — the game mode ctor sets `PlayerControllerClass`);
  `EditorStartupMap=/Engine/Maps/Entry`.
- `Config/DefaultInput.ini` — legacy (non-EnhancedInput) PlayerInput/InputComponent
  classes. No axis mappings: the controller reads keys directly (see below).

## Slice 4 — live gameplay loop (presentation only)
- `RtsBridgeGameMode` — StartPlay loads the match from content via `RtsContentLoader`
  (`MatchScenarioId()`: env `RTS_SCENARIO`, default `tri-point-skirmish` (one MCV each);
  `BattleScenarioId` = `tri-point-30` is the old armies-only battle; `MapId` = `tri-point`; root = `$RTS_CONTENT_ROOT`,
  else `<project>/..`): the scenario minus `commands`/`ticks`/`assert` is the host
  init, its size sets `MapCellsW/H` (runtime statics) and the camera clamp
  (`RefreshMapBounds`), and the typed tiles go to `RtsTerrainActor`. No typed map →
  blocked rects drawn as cliff. Camera opens on the f0 (Coalition) army. Then Tick steps the
  host at the sim cadence (20 t/s), stamping queued command bodies with the current
  tick. Any host/protocol failure: log `RTS_SESSION_HOST_FAILURE`, halt stepping —
  never fabricate state.
- `RtsRtsCameraActor` — StarCraft-style cam: fixed north-up (yaw −90: screen right =
  +X, screen up = −Y), orbits a ground target at `Distance` (start 1600 UU), target clamped to `MapWorldBounds()`, scroll at 1.5×Distance UU/s,
  wheel zoom 400–5000 UU, `JumpTo` for minimap. No rotation.
- `RtsRtsPlayerController` — cursor always shown. Keys polled with `IsInputKeyDown`
  (WASD/arrows) + 8 px screen-edge scroll (only while the viewport has focus); wheel via
  `BindKey` — no config mappings. LMB: on minimap jump/drag camera, else click
  (<6 px movement: nearest own unit within 40 px, empty ground clears) or drag box
  (own units whose screen projection is inside); Shift toggles/adds. Ctrl+A = select all
  your (`PlayerFaction`) units (A is scroll); Ctrl+1–3 assign group, 1–3 recall, double-tap
  centres the camera on it; RMB = enemy under cursor ? `attack` (target = sim id) :
  `move` to the minimap point or ground trace. F arms attack-move (crosshair): next LMB
  issues `attack-move` (or `attack` on an enemy); RMB cancels. X = `stop`. Restart (T07, `RestartMatch`, no hotkey — only the T19 result screen's Play again calls it): host `reset` + `init` on the live process, relaunch if it died; clears actors, selection, groups and the result banner. A/S are
  WASD, hence F/X; Esc is left to the editor (stops PIE). T36: LMB a sidebar entry = placement mode (hand
  cursor); LMB ground queues `place` only when the query allows it (Shift keeps placing); RMB
  cancels; the minimap still moves the camera while placing. E = `deploy` the selected MCVs. T37:
  clicking a building selects it; unit orders skip buildings; RMB ground with a producing building
  selected also sends `rally` (`RequestRallySelected`); the production panel takes LMB (`train`,
  Shift = 5) and RMB (`cancel` the last of that unit, or the clicked queue slot). T38: RMB within a
  cell of a live vein (`VeinAt`) with harvesters selected sends them `harvest` for it and the rest a move. All orders go through
  `QueueOrderForSelection` (game mode); never sim state.
  Test: `RtsBridge.Host.Orders` (real host accepts the exact attack/stop/attack-move bodies). `RtsBridge.Host.Restart`: reset + init replays the same hash; init without reset is refused.
- `RtsSelection.h` (`FRtsSelection`) — pure selection + control groups 1–3 over sim
  ids, port of `game/selection/SelectionController`; pruned against every snapshot.
  Test: `RtsBridge.Selection.Rules` (no world, no host).
- `RtsMinimapHud` (HUDClass, set in the game mode ctor) — also draws the box-select
  drag rectangle; health bars (T15: over damaged or selected units, green→yellow→red,
  screen space under the minimap); shot tracers (T16, pillar 4: per `shot` event a faint
  shooter→target line in the shooter's faction tint, a travelling streak, muzzle flash and
  hit spark, 0.2 s, capped at 256 live; endpoints read before the dead leave the roster);
  match result screen (T19, drawn last: 1.5 s after `state.match.over` it fades in
  VICTORY/DEFEAT/DRAW, match time from the end tick, units lost/fielded for you and for every enemy side together (T27: factions 1 + 2) from
  snapshots + death events, and Play again (standalone only, `CanRestart`) / Quit buttons;
  layout + hit test are static `MatchScreenLayout`/`MatchScreenHit`, test `RtsBridge.UI.MatchScreen`;
  while up, the controller routes left clicks to it and the game mode refuses orders);
  economy bar (T35, top centre: your credits with a +/- flash on change, power as used / produced,
  red with LOW POWER when draw > supply; text from static `EconomyBarText`);
  build sidebar (T36, right edge: with no base and an MCV, a hint + Deploy button (E key does the
  same); with a base, your faction's buildings (and captured trees) except yards from `GetBuildOptions` (each row: name, cost, and the content `description` under it, blue for naval/offshore; "needs X" replaces it when a prereq is missing; red cost = can't afford; an orange bolt by the cost = would cause low power, hover it for why; naval/offshore listed last; T25 the sim answers `water` / `shore` when the ghost is on the wrong ground) --
  prereq from the sim's own query (re-asked only when your buildings or their finished state
  change), affordability and a low-power warning from the snapshot; static `BuildMenuLayout`/
  `BuildMenuHit`); placement ghost (footprint cells under the cursor, green/red from
  `QueryPlace`, cached per cell and tick, with the reason text); construction bars (blue, over
  buildings still building). Production panel (T37, bottom centre, `GetSelectedProducer`: the
  building's trainable units from the catalog as buttons with cost, queued count and greying when
  unaffordable / full / under construction; five queue slots with the head's progress; static
  `ProductionLayout`/`ProductionHit`); rally line and flag for selected buildings. Buildings are drawn by `ARtsSimUnitActor::SetBuildingShape` (footprint
  box rising from a slab, darker until finished); box select and Ctrl+A skip buildings;
  debug overlays (T20, F4 toggles all of them via `SetDebugMask`; in PIE the editor swallows
  F1-F3 and F8/F11 are taken, and the engine's own F4 view-mode binding is removed per
  player): stats panel (tick, smoothed host step ms and bridge round trip ms, units per
  side, fps), remaining paths and target lines (the selection, or all units when nothing is
  selected), `#id state` labels. The game mode adds `"debug":true` to steps only while
  overlays are on; test `RtsBridge.Host.Debug`; canvas-drawn minimap,
  bottom-left: terrain (transient texture, one texel per cell, built once from the
  tile rows), faction-coloured unit dots (selected = white outline), camera
  footprint (viewport corners deprojected to Z=0). `MinimapToWorld` for clicks. Map
  size comes from `ARtsBridgeGameMode::MapCellsW/H` (set from the loaded scenario).
- `RtsVeinActor` (T38) — one gold block per vein (instanced cubes, no collision), shrinking with its pool
  as a fraction of the largest pool seen (`VeinScale`, pure), gone when dry; updated from every snapshot.
  Harvesters carry a gold cargo block sized by load / `LoadCapacity`, brighter when full
  (`ARtsSimUnitActor::SetCargo`); the minimap draws live veins as gold squares.
- `RtsTerrainActor` — T14-lite: water plane under the map, then land / cliff / bridge
  tiles as row-run-merged boxes, one Movable instanced-mesh component per tile type
  (land top Z=0, cliffs 80 UU, bridges just above land). Collision = query-only so RMB
  ground traces hit any tile. `TileColor` is shared with the minimap.
- `RtsContentLoader` (`RtsContent::LoadMatch`) — reads scenario + typed map from
  content; T25 the scenario's own `map.terrain` rows win (they are what the sim walks on), then the
  typed map file, then `blocked` rects drawn as cliff. Test: `RtsBridge.Content.TriPoint` (loads, host accepts the init, 30 per
  faction, every spawn on a walkable tile).
  Order feedback (T12, pillar 1), drawn the same frame the order is queued and only if
  it was: move = green ground ping, attack-move = orange ping, attack = red ring on the
  target unit, stop = white ring on each stopped unit; 0.6 s, ground pings also on the
  minimap. Canvas lines only — no assets.
- `RtsSimUnitActor` — cube tinted per faction via BasicShapeMaterial `Color` param
  (T15: holds hp/maxHp from snapshots; a unit named by a `death` event plays
  `BeginDeath` — topple + darken, 2 s corpse, sink, self-destroy — outside the roster, so it
  is never picked or selected; a unit that leaves the roster without one is just removed)
  (dynamic MID) + green cylinder selection ring; position ONLY from host snapshots
  at 100 UU per map cell. Ticks to glide from its drawn position to the latest snapshot
  over one sim tick (render lags ≤ 50 ms, never extrapolates) and turns toward its
  motion. New ids snap (`SnapToSimPosition`).
- Game mode keeps actors in `ActorsById`: snapshot rows match actors by sim id, never
  by index (an index match would hand a dead unit's actor to another and glide it).
- `RtsExplosionActor` (T18) - procedural explosion from engine basic shapes: a white-hot fireball that swells, cools to grey smoke and rises, a flat shock ring, a 0.25 s light flash; about 1.1 s (`FrameAt` is the pure timeline, test `RtsBridge.UI.Explosion`). The game mode spawns one per death event of a vehicle or building you could see (infantry just fall; size from footprint, armor from the catalog), and one per superweapon pulse landing (radius = strike radius); at most 24 live. Colours stay off the team hues.
- `RtsBridge.Build.cs` needs `InputCore` (EKeys) and `RenderCore` (`GWhiteTexture` for the fog overlay).
- T17: imported unit models live in `Content/Units` (`.uasset` via Git LFS; always cooked, `Config/DefaultGame.ini`).
  The rifleman comes from the art pipeline's `game/assets/rifleman/rifleman.glb` through
  `Scripts/import_rifleman.py` (headless: `UnrealEditor-Cmd <uproject> -run=pythonscript -script=<it>
  -unattended -nullrhi`; needs PythonScriptPlugin, enabled in the uproject). `ARtsSimUnitActor::SetModel`
  swaps the cube for it (cube stays as the hidden pick target): half size, feet on the ground, yaw -90
  (the model faces +Y). Clips: idle / move from whether the snapshot glide moves, fire once per `shot`
  event, sped up to 0.5 s. Faction tint on the `coalition_plate/armor/fabric` slots (`BaseColorFactor`).
  Every other type is still a cube. Test: `RtsBridge.UI.RiflemanAsset` (mesh, slots, clips present).

## Package (T24)
`unreal/package.sh [out]` (default `unreal/Build/Package`, gitignored): publishes the self-contained
host, runs UAT BuildCookRun (Development, pak), then stages `sim-host` into
`<out>/<Platform>/RtsBridge/dist/<rid>/` and `content/` into `<out>/<Platform>/RtsBridge/rts/content`.
`RtsContent::ContentRoot` picks `<project>/rts` when it holds `content/`, and the bridge passes that root
to the host as `RTS_CONTENT_ROOT`, so the package runs with no .NET and no checkout.

## Build (installed engine at ~/.local/opt/UnrealEngine-5.8.3)
```
Engine/Build/BatchFiles/Linux/Build.sh RtsBridgeEditor Linux Development \
  -project=<abs>/unreal/RtsBridge.uproject -noP4        # Result: Succeeded
```
Monolithic **Game** target does NOT link against this installed build (no full-game
link libs shipped) — editor/module builds and UAT packaging are the supported paths.

## Test (headless, no GPU)
```
RTS_CONTENT_ROOT=<repo> Engine/Binaries/Linux/UnrealEditor-Cmd <abs>/unreal/RtsBridge.uproject \
  -nullrhi -NoSound -unattended -log \
  -ExecCmds="Automation RunTests RtsBridge; Quit"
```
Publish the host first: `dotnet publish tools/sim-host/SimHost.csproj -c Release
-r linux-x64 --self-contained -o unreal/dist/linux-x64` (assembly name `sim-host`).

## Engine pitfalls (5.8.3, verified the hard way)
- `FPlatformProcess::WritePipe(FString)` on Unix appends `\n` itself, writes
  `BytesAvailable+1`, and returns `BytesWritten == BytesAvailable` — ALWAYS false.
  Use the byte overload `WritePipe(void*, const uint8*, int32)` + explicit `\n`.
- PlayerController hook is `SetupInputComponent()` (APawn has the other name);
  input member is `InputComponent`. Keys via `BindKey(EKeys::X, ...)` — needs
  InputCore module dep.
- 5.8 removed `UStaticMesh::GetStaticMaterial(i)` — use `GetStaticMaterials()[i]`.
- Project-wide `make check` treats stale gitignored `tools/*/bin|obj` surviving a
  branch switch as content: `git clean -qfdX tools/` before running locally.
- Windows: `CreatePipe(..., bWritePipeLocal)` picks which end is NOT inheritable (Unix
  ignores the flag). The stdin pipe needs `true`, or `CreateProc` asserts "PipeReadChild
  is not inheritable". `CreateProc` Parms must be non-null (`TEXT("")`), or `check(Parms)` fires.
- Windows `FPlatformProcess::Sleep(0.001)` lasts a whole ~15.6 ms timer tick. A host
  reply bigger than one 4 KB pipe read then cost ~50 ms per step (Tri-Point froze). The
  bridge keeps host stdout as bytes across reads (`PendingBytes`), decodes whole lines
  only, and yields while waiting; the game mode runs ≤3 steps per frame so slow steps
  slow the sim instead of spiralling. Also: Windows `CreateProc` routes the child's
  stderr into the same pipe as stdout. Test: `RtsBridge.Content.TriPointStepSpeed`
  (full 30v30 fight, mean step < 10 ms; ~0.8 ms measured).
- `ConstructorHelpers::FObjectFinder` is constructor-only (fatal at runtime) — use
  `LoadObject` in StartPlay/BeginPlay.
- Actor tick flags must be set in the constructor; setting them in StartPlay is too late.
- No pawn ⇒ set `bAutoManageActiveCameraTarget = false` or the engine resets the view
  target to the controller (black screen).
- Runtime-spawned meshes/lights need `Movable` mobility, or they don't render/light.
  Tint BasicShapeMaterial via a dynamic MID built from the `LoadObject`ed material.
- Running the editor can rewrite `Config/DefaultInput.ini` to EnhancedInput classes;
  this project uses legacy input — revert that diff, don't commit it.

## Windows (verified 2026-11-24, UE 5.8.4 + VS 2022)
```
dotnet publish tools/sim-host/SimHost.csproj -c Release -r win-x64 --self-contained -o unreal/dist/win-x64
"<UE>\Engine\Build\BatchFiles\Build.bat" RtsBridgeEditor Win64 Development -project=<abs>\unreal\RtsBridge.uproject -WaitMutex
set RTS_CONTENT_ROOT=<repo>
"<UE>\Engine\Binaries\Win64\UnrealEditor-Cmd.exe" <abs>\unreal\RtsBridge.uproject -nullrhi -NoSound -unattended -ExecCmds="Automation RunTests RtsBridge; Quit"
```
Close any open Unreal Editor first: Live Coding blocks command-line builds.
