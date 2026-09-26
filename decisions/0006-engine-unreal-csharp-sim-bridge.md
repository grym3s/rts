---
status: proposed
date: 2026-09-26
supersedes: 0002
---
# 0006 — Presentation engine: Unreal Engine 5 with an engine-independent .NET simulation

Target Unreal Engine 5 for the high-angle, high-fidelity 3D RTS presentation. Use native Unreal C++ for the runtime boundary and presentation, with Blueprints for visual iteration. Keep the deterministic .NET simulation as the only authority for game rules and state.

Unreal communicates with a self-contained .NET simulation host through a versioned local JSON-lines pipe. Requests contain tick-stamped existing `Command` values; responses contain immutable state snapshots and `SimEvent`s. Unreal performs rendering, camera, input, selection, animation, UI, audio, and VFX only. It never duplicates movement, targeting, combat, economy, or production rules.

The process boundary preserves the tested simulation and avoids depending on third-party C# engine bindings whose Linux support is not ready. It adds packaging and IPC work, which must be proven in an early spike before the presentation migration expands. If latency, deterministic replay, or packaged-process tests fail, revisit the boundary before adding more Unreal gameplay code.

Use Unreal coordinates with map X → world X, map Y → world Y, and height → world Z; begin with 100 cm per map cell. Preserve Blender source and reference art. Verify the runtime interchange format and all animation clips in the pinned Unreal editor before changing the art handoff contract.

The previous Godot 4 implementation remains the working runtime until the Unreal project can build, run the host, render authoritative state, accept commands, and pass equivalent CI and visual acceptance checks. Remove Godot only after those checks pass.

**Promotion condition:** install and pin an official Unreal Linux build; verify the C++ project toolchain; then pass the host-process and command round-trip spike. The user selected Unreal and authorized the migration. The machine currently requires Epic account sign-in and EULA acceptance before the official Linux editor download is available.
