# Unreal RTS migration design

## Goal

Move the game presentation from Godot to Unreal Engine 5 while preserving the deterministic .NET simulation and the game's 3D high-angle RTS direction.

## Approved direction

- Use Unreal Engine 5 with a native C++ gameplay/presentation module and Blueprints for visual iteration.
- Keep `sim/` as the sole owner of game rules and state. It remains engine-free and is not ported to C++ as part of this migration.
- Run the .NET simulation in a companion process. Unreal sends tick-stamped `Command`s and receives versioned read-only snapshots and events over a local, framed JSON-lines pipe. The exact pipe implementation is selected in the bridge spike, but it must be local-only and work on Linux and Windows.
- Unreal owns camera, input, rendering, UI, audio, particles, and process lifecycle. It must not implement parallel movement, targeting, combat, economy, or production rules.
- Use Unreal coordinates with map X → world X, map Y → world Y, and height → world Z. The initial scale is 100 Unreal centimeters per map cell; model import tests may adjust asset scale without changing simulation coordinates.
- Preserve Blender `.blend` sources and the concept references. Select the runtime interchange format (FBX or glTF) by import and animation verification in the pinned Unreal build. Do not overwrite the archived Godot prototype.

## Why this direction

The user selected Unreal for the desired high-fidelity 3D RTS. The repository's simulation is already a tested, deterministic .NET library with engine-free contracts and must remain authoritative. Epic documents Unreal gameplay development in C++ and Blueprints; the third-party UnrealSharp project currently lists Linux as planned, so this project will not depend on it. A process boundary avoids binding core simulation to an experimental engine plugin.

## Runtime contract

The bridge is a versioned request/response stream. Each request carries a protocol version, monotonically increasing tick, and zero or more existing simulation commands. Each response carries the completed tick, immutable render state, and events. The simulation host alone advances ticks. Unreal interpolates between snapshots for display and sends user intent only as commands. Malformed messages, tick gaps, host exit, or protocol mismatch produce a visible recoverable error; they must not silently fall back to locally simulated rules.

The implementation spike must verify that Unreal can launch, communicate with, and shut down the .NET host on the target desktop platforms. It must also prove replay/state hashes remain stable for the existing scenario suite. If process IPC fails the latency or packaging test, revise the boundary before building the full presentation.

## Migration slices

1. Obtain and install the official Linux Unreal build; pin the exact version and verify editor startup and a native C++ project build.
2. Define and test the host protocol, then package the existing simulation behind a self-contained .NET executable.
3. Create the Unreal C++ project and process bridge; render one sim-controlled unit and prove a move command round-trip.
4. Add the 3D RTS camera, battlefield, squad rendering, selection, and command input.
5. Import the first authored infantry asset with its verified skeleton and animation clips; add representative combat VFX and review the scene at gameplay camera scale.
6. Add Unreal CI/build verification. Retire Godot files and jobs only after equivalent Unreal acceptance checks pass.

## Acceptance

- Existing `make check test` and scenario hashes pass unchanged.
- A clean Linux install opens the pinned Unreal project and builds it from source.
- An Unreal run launches the .NET host, renders authoritative sim state, sends a move command through the bridge, and reports protocol/host failure instead of fabricating a state.
- Replay hashes are identical whether the sim is driven by the headless runner or Unreal-originated tick-stamped commands.
- Infantry displays verified materials and `idle`, `move`, and `fire` animations; one representative explosion is visible and profiled at the RTS camera angle.
- CI verifies the .NET simulation and at least one supported Unreal project build path; runtime assets and source are versioned without editor caches.

## Current blocker and state

No Unreal editor is installed yet. The Linux prebuilt editor is available from Epic's account-gated download page. The current browser session is not signed in, and the Epic GitHub source repository returns HTTP 404 for the configured `grym3s` account. The user must complete Epic sign-in and accept Epic's EULA before an authenticated download can proceed. This design document is not evidence that the engine or runtime migration is complete.
