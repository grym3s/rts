# unreal/

Unreal presentation shell (migration slice 3). No game rules here — the .NET sim
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
- `Config/DefaultEngine.ini` — GlobalDefaultGameMode = RtsBridgeGameMode (spike driver).

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
