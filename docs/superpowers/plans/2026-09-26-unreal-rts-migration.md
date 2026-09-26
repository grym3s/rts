# Unreal RTS Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Godot presentation with a tested Unreal Engine 5 C++ presentation while preserving the authoritative deterministic .NET simulation.

**Architecture:** Unreal owns presentation and input. A versioned local JSON-lines pipe connects it to a self-contained .NET simulation-host executable, which alone advances `SimWorld` and returns immutable snapshots/events. A small IPC spike validates the boundary before broad presentation work.

**Tech Stack:** Unreal Engine 5 C++, Blueprints, C#/.NET 10 host and existing .NET 8 simulation library, JSON-lines over local anonymous pipes, Blender-authored models, Git LFS.

**Spec:** `docs/superpowers/specs/2026-09-26-unreal-rts-migration-design.md`

## Global Constraints

- `sim/` remains engine-free, fixed-tick, fixed-point, deterministic, and authoritative.
- Unreal sends player intent only through existing tick-stamped `Command` types.
- Simulation map X maps to Unreal X, map Y maps to Unreal Y, and elevation maps to Unreal Z; initial scale is 100 cm per map cell.
- Pin an exact UE 5 release after the Linux editor is installed and verified.
- Do not delete Godot or archived art until Unreal visual/runtime acceptance and CI pass.
- Preserve user work already present in the main checkout; never include editor caches or unrelated uncommitted art in the migration PR.

## Review Focus

- Host crash, early EOF, or malformed JSON must become a clear game error; add bridge process tests for each.
- Tick replay and duplicate/out-of-order tick requests must preserve deterministic results; add protocol ordering tests.
- Empty snapshots, zero units, and large unit batches must not crash rendering; add host serialization and Unreal smoke cases.
- GLB/FBX axis, root origin, scale, and skeleton import must be verified in the pinned editor before changing the art handoff contract.
- Packaging on Linux and Windows must find the self-contained host without assuming a globally installed .NET runtime; add packaged launch tests.

---

### Task 1: Install and pin Unreal on Linux

**Files:**
- Create: `docs/unreal-engine-setup.md`
- Modify: `docs/workflow.md`
- Verify: local official Unreal editor installation and C++ template project

**Interfaces:**
- Produces the pinned Unreal version, engine root path, and reproducible editor/build commands consumed by every later task.

- [ ] Download the prebuilt Linux ZIP from Epic's official Linux page after the user completes Epic account sign-in/EULA acceptance.
- [ ] Verify the ZIP source and checksum if Epic publishes one; extract under `~/.local/opt/UnrealEngine-<version>` without replacing an existing install.
- [ ] Run the editor's version command and open an empty C++ project; record exact version and successful startup.
- [ ] Run Epic's `SetupToolchain.sh`; record any user-required package-manager dependencies instead of guessing or performing a system-wide upgrade.
- [ ] Add only reproducible user-local setup instructions; keep install paths and caches out of the repository.

### Task 2: Define the .NET simulation host protocol

**Files:**
- Create: `tools/sim-host/SimHost.csproj`
- Create: `tools/sim-host/Program.cs`
- Create: `tools/sim-host/Protocol/` message records and JSON codec
- Create: `tools/sim-host.tests/`
- Modify: `Makefile`, `RTS.sln`, `tools/CONTEXT.md`

**Interfaces:**
- Consumes: existing `SimWorld`, `Command`, scenario/content loading, and deterministic test infrastructure.
- Produces: newline-delimited UTF-8 JSON request/response records with explicit `protocolVersion`, `tick`, `commands`, `state`, and `events` fields.

- [ ] Add protocol codec tests for one command, an empty command list, an empty roster, malformed input, unsupported version, duplicate tick, and skipped tick; run them and verify expected failures.
- [ ] Implement strict JSON-lines decoding/encoding and reject invalid tick order without advancing the world.
- [ ] Add one-request/one-snapshot simulation loop using the existing sim tick boundary; do not duplicate any rules.
- [ ] Add deterministic replay test: identical command stream through the host and scenario runner yields identical final state hash.
- [ ] Publish a self-contained host executable for Linux x64 and Windows x64 and verify execution without global runtime discovery.

### Task 3: Scaffold the Unreal C++ project and host bridge

**Files:**
- Create: `game-unreal/RtsUnreal.uproject`
- Create: `game-unreal/Source/RtsUnreal/` module and process bridge classes
- Create: `game-unreal/Source/RtsUnrealTests/` automation tests where supported
- Modify: `.gitignore`, `.gitattributes`, `Makefile`, root `CONTEXT.md`

**Interfaces:**
- Consumes: Task 1's pinned UE installation and Task 2's exact protocol records.
- Produces: `USimHostSubsystem` lifecycle API and typed immutable snapshot structs for presentation consumers.

- [ ] Write host launch/path resolution tests or automation harness cases before process code.
- [ ] Create the native C++ Unreal project with the installed editor/project generator; compile the untouched template.
- [ ] Implement host start, request write, response read, EOF/error reporting, and clean shutdown over local pipes only.
- [ ] Verify launch from both editor and packaged-development process context; test missing host executable and immediate host exit.
- [ ] Verify the engine does not update sim state when the host reports a protocol or process failure.

### Task 4: Render one authoritative unit and round-trip an order

**Files:**
- Create: `game-unreal/Source/RtsUnreal/Presentation/` camera, field, unit, selection, and input classes
- Create: `game-unreal/Content/` minimal test map and placeholder materials
- Modify: `game-unreal/` module build settings only as required by the created project

**Interfaces:**
- Consumes: `USimHostSubsystem` snapshots and existing sim command JSON schema.
- Produces: perspective RTS camera, one visible sim-driven unit, ground-plane order projection, and a selected-unit display.

- [ ] Add automation test for sim X/Y to Unreal X/Y conversion and 100 cm per cell.
- [ ] Add automation test for screen-ray-to-ground conversion at center and viewport edges.
- [ ] Implement camera pan/zoom and a test field; keep all rule calculations in `sim/`.
- [ ] Render units from snapshots, interpolate between completed ticks, and derive facing from snapshot velocity.
- [ ] Implement click/drag selection and right-click movement by emitting `MoveOrder` commands only.
- [ ] Run the editor build and interactive round-trip: select squad, issue move, confirm host state changes, and confirm deterministic headless hash.

### Task 5: Import animated art and a representative explosion

**Files:**
- Create: `art/export/rifleman/` validated runtime interchange and manifest
- Create: `game-unreal/Content/Units/Rifleman/` imported Unreal assets
- Create: `game-unreal/Content/VFX/` one representative explosion effect
- Modify: `art/CONTEXT.md`, `art/export/CONTEXT.md`, `docs/asset-pipeline.md`, art agent prompt

**Interfaces:**
- Consumes: Blender `.blend` source, original reference images, Task 4's snapshot presentation.
- Produces: verified Unreal skeletal mesh/materials and `idle`, `move`, `fire` animations; visible explosion effect.

- [ ] Import a disposable test export and verify forward axis, origin, scale, material, rig, and all clip names before changing the canonical art prompt.
- [ ] Select FBX or glTF based on verified animation/material retention in the pinned editor; retain `.blend` source and export manifest.
- [ ] Integrate the reviewed infantry asset and map simulation locomotion/combat state to animations without adding game rules.
- [ ] Add one explosion and verify timing, scale, lighting, and legibility at close/default/far tactical zoom.
- [ ] Capture a rendered scene and record performance and any art/export constraints.

### Task 6: Replace Godot documentation and CI after parity

**Files:**
- Modify: `README.md`, `AGENTS.md`, `CLAUDE.md`, root and folder `CONTEXT.md` files, `docs/workflow.md`, `.github/workflows/ci.yml`, `Makefile`
- Remove only after acceptance: `game/` Godot runtime and Godot-specific CI/setup

**Interfaces:**
- Consumes: passing Unreal host, presentation, art import, and package checks from Tasks 1–5.
- Produces: Unreal-first contributor workflow and CI without removing the independent .NET simulation/tooling.

- [ ] Add an Unreal-enabled self-hosted CI runner or reproducible engine cache strategy; keep public CI running all sim tests without Epic credentials.
- [ ] Run `make check test`, all scenario hash checks, Unreal C++ build, Unreal automation smoke, and packaged host launch from a clean checkout.
- [ ] Review the diff for Godot references and remove old runtime files only after the new acceptance path is green.
- [ ] Generate ICM indexes and verify every changed working folder has a current `CONTEXT.md`.
