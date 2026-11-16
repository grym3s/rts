# Godot retirement checklist (prepares GRY-290, slice 6)

Inventory only. This document deletes nothing. GRY-290 runs the removal after the
gate below passes. Sources: `docs/superpowers/specs/2026-09-26-unreal-rts-migration-design.md`
(retirement rule), `docs/superpowers/plans/2026-09-26-unreal-rts-migration.md`
(Task 6), ADR 0006 (supersedes ADR 0002), `unreal/CONTEXT.md` (current Unreal lane).
Inventory taken from main at `133eb46`, 2026-11-16.

## Gate rule

Retire Godot only after equivalent Unreal acceptance checks pass (spec rule 6).
All of these must be true before any removal step:

1. Slice 5 accepted: infantry mesh, `idle`/`move`/`fire` animations, and one
   explosion verified in the pinned Unreal editor; visual acceptance recorded on
   GRY-290.
2. An Unreal CI lane exists (self-hosted runner or reproducible engine cache) and
   is green on a PR. Public CI keeps all sim tests without Epic credentials.
3. Clean-checkout parity: `make check test`, all scenario hash runs, Unreal C++
   editor build, Unreal automation smoke (`RtsBridge.Host.RoundTrip`), and a
   packaged host launch all pass.

Current status (verified 2026-11-16): slices 1–4 are merged (`d82580e`, `2a27b45`,
`f042374`, `75d49fb`). Slice 5 is scoped, not started. No Unreal CI lane exists.
The gate is not met; GRY-290 stays blocked on Unreal art acceptance.

## A. Paths to remove at slice 6

| Path | Godot-dependent role | Unreal replacement | Status |
|---|---|---|---|
| `game/` (26 files) | Godot 4.7 .NET presentation: `project.godot`, `main.tscn`, `Main.cs` (steps `SimWorld` directly), `Game.csproj`/`Game.sln` (not in root `RTS.sln`) | `unreal/` shell + `tools/sim-host/` bridge (protocolVersion 1) | replace whole dir |
| `game/camera/RtsCamera.cs` | high-angle RTS camera | `RtsRtsCameraActor` (scroll, zoom 400–3000 UU, Q/E rotate) | exists |
| `game/selection/` | cursor selection + overlay | `RtsRtsPlayerController` select-under-cursor + selection ring on `RtsSimUnitActor` | exists |
| `game/orders/OrdersInput.cs` | RMB move → Command | `RtsRtsPlayerController` ground trace → `RequestMoveTo` | exists |
| `game/render/UnitRenderer.cs` | unit meshes | `RtsSimUnitActor` (faction-tinted cube placeholder) | placeholder; final mesh lands with slice 5 |
| `game/debug/DebugOverlay.cs` | debug overlay | none yet | gap: port or accept loss before removal |
| `game/assets/rifleman/rifleman.glb` | runtime Godot import copy | slice-5 Unreal import (FBX or glTF, chosen by slice-5 verification) | move only after slice 5 verifies import; source stays in `art/work/` |
| `Makefile` `run` (line 45) | `godot --path game` | Unreal editor launch documented in `unreal/CONTEXT.md` | remove target |
| `Makefile` `godot-test` (lines 47–50) | headless import + run + `SMOKE PASS` grep | Unreal automation `RtsBridge.Host.RoundTrip` + packaged host launch | remove after automation is the CI gate |
| `.github/workflows/ci.yml` `godot` job (lines 28–47) | setup-godot 4.7.1, `dotnet build game/Game.csproj`, headless import + run | Unreal CI job (needs self-hosted runner or engine cache) | remove only when the Unreal job is green |
| `art/build-asset.sh` import step (lines 44–50, header line 7) | `--install` copies exports to `game/assets/` and runs Godot headless import | Unreal import step chosen by slice 5 | rewrite step; keep Blender export logic |

## B. Paths to edit at slice 6 (keep the file, change the Godot wording)

| Path | Godot reference | Edit |
|---|---|---|
| `README.md` (3, 7–10) | "Godot 4 presentation", install note, `make run` | point at Unreal + `unreal/CONTEXT.md` build/test commands |
| `AGENTS.md` / `CLAUDE.md` (3, 11, 27, 36) | "presented by Godot 4 in `game/`", `make godot-test` route, rule 1 | route to `unreal/`; rule 1 keeps its meaning: `sim/` never references any engine |
| root `CONTEXT.md` (6) | `game (Godot)` in the data-flow line | replace with `unreal (presentation) ← sim-host` |
| `docs/workflow.md` (4–5, 15) | Godot 4.7.1 pin, `make godot-test`, CI stage 3 | pin Unreal 5.8.3, new targets, new CI stage |
| `docs/asset-pipeline.md` (1, 3, 8–9, 19, 25, 35, 48, 51) | Godot import conventions, X/Z axis mapping, `game/assets/` copies | rewrite for the slice-5 Unreal import path; keep Blender steps |
| `docs/conventions.md` (4, 7) | "net8.0 (Godot)", Godot scene rules | drop the Godot note (keep the net8.0 target), replace scene rules with Unreal module rules |
| `docs/CONTEXT.md` (13) | asset-pipeline row says "Godot asset workflow" | update the row text |
| `art/CONTEXT.md` (6–7) | "imports into the pinned Godot project" | point at the Unreal import check |
| `.vscode/extensions.json`, `launch.json`, `settings.json`, `tasks.json` | godot-tools, Godot launch configs, `godotTools.*` settings, `godot: headless import + run` task | remove Godot entries; add Unreal C++ equivalents |
| `_scripts/check.sh` (19) | skips `game/.godot`, `game/bin`, `game/obj` | drop the `game/` skips once `game/` is gone |
| `.gitignore` (6–7) | `.godot/` | optional cleanup; harmless if kept |
| `art/export/*/manifest.json`, `art/work/*/manifest.json` | record Godot import acceptance | update wording when the import step changes |

## C. Keep — do not delete

| Path | Why it stays |
|---|---|
| `sim/`, `content/`, `map/`, `tools/` | engine-free simulation, data, ICM map, scenario/bench/sim-host tooling. `tools/sim-host/` is the Unreal bridge authority — keep and grow |
| `unreal/` | the replacement lane |
| `art/work/`, `art/references/`, `art/archive/` | Blender sources, references, archived Godot prototype (spec: do not overwrite the archive) |
| `decisions/` | history. ADR 0002 stays with its `supersedes: 0002` note on 0006; never rewrite history |
| `docs/superpowers/` | migration plan and spec — the record of why this checklist exists |
| `_scripts/check.sh` engine-namespace grep (24) | keeps `sim/` engine-free; it already greps `UnityEngine` too, add Unreal tokens if useful |
| `sim/CONTEXT.md` "Never: references Godot" (6) | reword to "references any engine" or keep; either is fine, it is not a dependency |

## D. Ordered removal checklist (GRY-290 execution)

Do all steps on one branch in one PR. Revert the PR is the rollback.

0. Confirm the gate above (slice 5 accepted, Unreal CI green, parity run green).
   - Verify: `RTS_CONTENT_ROOT=<repo> <engine>/Engine/Binaries/Linux/UnrealEditor-Cmd <repo>/unreal/RtsBridge.uproject -nullrhi -NoSound -unattended -log -ExecCmds="Automation RunTests RtsBridge; Quit"` → `Result: Success`; GRY-290 carries the visual-acceptance record.
1. `git rm -r game`
   - Verify: `test ! -e game && echo GONE`; `make check` passes.
2. Makefile: delete `run` and `godot-test` targets and their names from `.PHONY`.
   - Verify: `! grep -qi godot Makefile`; `make check test` passes.
3. `ci.yml`: delete the `godot` job.
   - Verify: `! grep -qi godot .github/workflows/ci.yml`; `gh pr checks` green.
4. `art/build-asset.sh`: replace the Godot import step with the slice-5 Unreal
   import step; update the usage header.
   - Verify: `! grep -qi godot art/build-asset.sh`; `art/build-asset.sh rifleman` exits 0.
5. Docs: apply every edit in section B.
   - Verify: `grep -rni godot README.md AGENTS.md CLAUDE.md CONTEXT.md docs/ art/CONTEXT.md .vscode/ | grep -v docs/superpowers` returns nothing.
6. `.vscode/`: remove Godot extensions, launch configs, settings keys, tasks.
   - Verify: `! grep -qi godot .vscode/extensions.json .vscode/launch.json .vscode/settings.json .vscode/tasks.json`.
7. `_scripts/check.sh`: drop the `game/` skip case (line 19).
   - Verify: `make check` passes.
8. Regenerate indexes: `make gen`.
   - Verify: `make check` passes; every changed folder has a current `CONTEXT.md` (AGENTS rule 3).
9. Full parity on the final revision: `make check test`; each scenario hash run
   from `ci.yml`; Unreal editor build; `RtsBridge` automation; packaged host
   launch (commands in `unreal/CONTEXT.md`).
   - Verify: every command exits 0 with its expected hash/`Result: Success`.
10. Submit for review and merge (separate review; production approval does not
    apply — no deployment path here).

## Open gaps to close before step 1

- Debug overlay has no Unreal equivalent (section A, `game/debug/`).
- No Unreal CI lane exists yet (gate item 2).
- `game/assets/rifleman/rifleman.glb` must be re-imported and verified in Unreal
  (slice 5) before `game/` is removed; the GLB itself may be kept under `art/`
  until the chosen interchange format is fixed.
