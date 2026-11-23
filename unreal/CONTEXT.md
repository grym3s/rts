# unreal/

Unreal presentation shell (migration slices 3-4). No game rules here — the .NET sim
host (`tools/sim-host/`, protocolVersion 1 over newline-delimited JSON) is the sole
simulation authority. See `docs/superpowers/specs/2026-09-26-unreal-rts-migration-design.md`.

## Layout
- `RtsBridge.uproject` — EngineAssociation 5.8; one Runtime module `RtsBridge`.
- `Source/RtsBridge/Public/RtsSimHostBridge.h` — process bridge: `FPlatformProcess`
  pipes only (cross-platform Linux/Windows); locates host via `RTS_HOST_BIN` env or
  `unreal/dist/<rid>/sim-host`; validates `protocolVersion==1` + `ok` envelope;
  decodes Q32.32 snapshot positions (raw / 2^32); EOF-then-terminate shutdown.
- `Source/RtsBridge/Private/RtsBridgeRoundTripTest.cpp` — editor automation test
  `RtsBridge.Host.RoundTrip` (real host launch, init, tick-0 move, 120 steps).
- `Config/DefaultEngine.ini` — GlobalDefaultGameMode = RtsBridgeGameMode;
  GlobalDefaultServerPlayerControllerClass = RtsRtsPlayerController.
- `Config/DefaultInput.ini` — legacy (non-EnhancedInput) PlayerInput/InputComponent
  classes; axis mappings MoveForward/MoveRight/CamZoom/CamRotate.

## Slice 4 — live gameplay loop (presentation only)
- `RtsBridgeGameMode` — StartPlay spawns battlefield (plane+grid, sun, skylight),
  launches host + init (fixture roster; rules still host-owned), then Tick steps the
  host at the sim cadence (20 t/s), stamping queued command bodies with the current
  tick. Any host/protocol failure: log `RTS_SESSION_HOST_FAILURE`, halt stepping —
  never fabricate state.
- `RtsRtsCameraActor` — high-angle RTS cam: yaw-relative scroll (zoom-scaled),
  wheel zoom 400–3000 UU, Q/E rotate, pitch clamped −80°/−25°.
- `RtsRtsPlayerController` — LMB select-under-cursor (screen-space nearest), A =
  select all, RMB = move selected faction-0 units (ground trace -> RequestMoveTo).
  Talks to the game mode only; never touches sim state directly.
- `RtsSimUnitActor` — cube tinted per faction via BasicShapeMaterial `Color` param
  (dynamic MID) + green cylinder selection ring; position ONLY from host snapshots
  at 100 UU per map cell.
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
