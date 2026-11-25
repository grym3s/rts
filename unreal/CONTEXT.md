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
  (`ScenarioId` = `tri-point-30`, `MapId` = `tri-point`; root = `$RTS_CONTENT_ROOT`,
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
  faction-0 units (A is scroll); Ctrl+1–3 assign group, 1–3 recall, double-tap
  centres the camera on it; RMB = enemy under cursor ? `attack` (target = sim id) :
  `move` to the minimap point or ground trace. F arms attack-move (crosshair): next LMB
  issues `attack-move` (or `attack` on an enemy); RMB cancels. X = `stop`. Restart (T07, `RestartMatch`, no hotkey — only the single-player menu, T19, calls it): host `reset` + `init` on the live process, relaunch if it died; clears actors, selection, groups and the result banner. A/S are
  WASD, hence F/X; Esc is left to the editor (stops PIE). All orders go through
  `QueueOrderForSelection` (game mode); never sim state.
  Test: `RtsBridge.Host.Orders` (real host accepts the exact attack/stop/attack-move bodies). `RtsBridge.Host.Restart`: reset + init replays the same hash; init without reset is refused.
- `RtsSelection.h` (`FRtsSelection`) — pure selection + control groups 1–3 over sim
  ids, port of `game/selection/SelectionController`; pruned against every snapshot.
  Test: `RtsBridge.Selection.Rules` (no world, no host).
- `RtsMinimapHud` (HUDClass, set in the game mode ctor) — also draws the box-select
  drag rectangle; health bars (T15: over damaged or selected units, green→yellow→red,
  screen space under the minimap); shot tracers (T16, pillar 4: per `shot` event a faint
  shooter→target line in the shooter's faction tint, a travelling streak, muzzle flash and
  hit spark, 0.2 s, capped at 256 live; endpoints read before the dead leave the roster); canvas-drawn minimap,
  bottom-left: terrain (transient texture, one texel per cell, built once from the
  tile rows), faction-coloured unit dots (selected = white outline), camera
  footprint (viewport corners deprojected to Z=0). `MinimapToWorld` for clicks. Map
  size comes from `ARtsBridgeGameMode::MapCellsW/H` (set from the loaded scenario).
- `RtsTerrainActor` — T14-lite: water plane under the map, then land / cliff / bridge
  tiles as row-run-merged boxes, one Movable instanced-mesh component per tile type
  (land top Z=0, cliffs 80 UU, bridges just above land). Collision = query-only so RMB
  ground traces hit any tile. `TileColor` is shared with the minimap.
- `RtsContentLoader` (`RtsContent::LoadMatch`) — reads scenario + typed map from
  content. Test: `RtsBridge.Content.TriPoint` (loads, host accepts the init, 30 per
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
- `RtsBridge.Build.cs` needs `InputCore` (EKeys).

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
