# Godot retirement checklist (prepares GRY-290, slice 6)

Inventory only. This document deletes nothing. GRY-290 runs the removal after the
gate below passes. Sources: `docs/superpowers/specs/2026-09-26-unreal-rts-migration-design.md`
(retirement rule), `docs/superpowers/plans/2026-09-26-unreal-rts-migration.md`
(Task 6), ADR 0006 (supersedes ADR 0002), `unreal/CONTEXT.md` (current Unreal lane).
Inventory taken from main at `133eb46`, 2026-11-16; reconciled against a
tracked-file scan (section C3) after review feedback the same day.

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
   packaged host launch all pass. The package and packaged-launch procedure is a
   pre-removal prerequisite owned by GRY-290 (step 9); it is not yet defined.

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
| `art/build-asset.sh` import step (lines 40–51, header line 7) | `--install` copies exports to `game/assets/` and runs Godot headless import | Unreal import step chosen by slice 5 | rewrite step; keep Blender export logic |

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
| `art/work/rifleman/CONTEXT.md` (7) | "`--install` to copy the prototype to Godot and run headless import" | update wording with the step-4 import rewrite |
| `art/export/CONTEXT.md` (5–7) | "copies may be installed under `game/assets/<asset-id>/`"; "update `game/render/CONTEXT.md`" | replace with the Unreal runtime install path and the `unreal/` card names |
| `map/effects/CONTEXT.md` (7, 15) | rows open `game/CONTEXT.md` ("the sim/game seam", "how things look/control") | point the rows at `unreal/CONTEXT.md` and `tools/sim-host/CONTEXT.md` |
| `.github/PULL_REQUEST_TEMPLATE.md` (14) | validation box "`make godot-test` / manual check in editor" | replace with the Unreal automation check (`RtsBridge` automation run) |
| `.github/ISSUE_TEMPLATE/task.md` (11) | example folder `game/selection` | replace the example with `unreal/` folder names |
| `art/asset-manifest.template.json` (34) | `godotImported` review field | rename to `engineImported` for future manifests; existing manifests keep `godotImported` as history (section C2) |
| `.gitattributes` (2–5) | `*.tscn`, `*.tres`, `*.godot` text rules | remove those three lines (they only match `game/` files); keep `*.cs` and the LFS rules (`*.glb`, `*.blend`) |
| `.vscode/extensions.json`, `launch.json`, `settings.json`, `tasks.json` | godot-tools, Godot launch configs, `godotTools.*` settings, `godot: headless import + run` task | remove Godot entries; add Unreal C++ equivalents |
| `_scripts/check.sh` (19) | skips `game/.godot`, `game/bin`, `game/obj` | drop the `game/` skips once `game/` is gone |
| `.gitignore` (6–7) | `.godot/` | optional cleanup; harmless if kept. If kept it is a retained optional ignore, not an active dependency: excluded from the C3 and step-6 zero-match scans |

## C. Keep — do not delete

| Path | Why it stays |
|---|---|
| `sim/`, `content/`, `map/`, `tools/` | engine-free simulation, data, ICM map, scenario/bench/sim-host tooling. `tools/sim-host/` is the Unreal bridge authority — keep and grow |
| `unreal/` | the replacement lane |
| `art/work/`, `art/references/`, `art/archive/` | Blender sources, references, archived Godot prototype (spec: do not overwrite the archive) |
| `decisions/` | history. ADR 0002 stays with its `supersedes: 0002` note on 0006; never rewrite history |
| `docs/superpowers/` | migration plan and spec — the record of why this checklist exists |
| `_scripts/check.sh` engine-namespace grep (24) | keeps `sim/` engine-free; it already greps `UnityEngine` too, add Unreal tokens if useful. It is a negative guard, not a dependency: excluded from the C3 and step-6 zero-match scans, and never weakened to satisfy them |
| `sim/CONTEXT.md` "Never: references Godot" (6) | reword to "references any engine" or keep; either is fine, it is not a dependency |

## C2. Historical records — keep as-is (explicit exceptions)

These files mention Godot or `game/` as a record of what actually happened. They
are excluded from the step-6 zero-match check. Do not rewrite them.

| Path | Why it stays as-is |
|---|---|
| `docs/godot-retirement-checklist.md` | this inventory; it names Godot by design |
| `art/prompts/local-model-asset-pipeline-setup.md` (29–38) | stored art-prompt record; it documents the Godot-era axes and handoff contract that the archived exports were made under. Future prompts point at the Unreal contract instead |
| `art/work/rifleman/manifest.json`, `art/work/rifleman-v2/manifest.json`, `art/work/rifleman-trellis2/manifest.json`, `art/export/rifleman-v2/manifest.json` | record the historical Godot import acceptance (`godotImported`) as fact; the copies they describe were really imported |
| `art/work/rifleman/attempt-1/README.md` (3) | archive note describing where the first export copy went |
| `art/work/rifleman/build_rifleman.py` (15) | axis-convention comment; the convention itself (Blender Z-up → glTF Y-up, forward −Z) is unchanged by the engine switch |
| `decisions/0001`, `0002`, `0004`, `0006`, `2026-08-17-foundation-ultra-review.md`, `_index.md` | decision history (also section C) |
| `docs/superpowers/` | migration spec and plan (also section C) |

## C3. Scan reconciliation

Every tracked file that mentions `godot` (case-insensitive) or a `game/` path,
outside `game/` itself, appears in section A (remove), B (edit), C (keep) or C2
(historical exception). Re-run both scans before the removal PR and after it:

```
git grep -il godot -- ':!game' ':!docs/superpowers' ':!docs/godot-retirement-checklist.md' \
  ':!decisions' ':!art/work' ':!art/export' ':!art/prompts' ':!sim/CONTEXT.md' \
  ':(exclude)_scripts/check.sh' ':(exclude).gitignore'
git grep -l 'game/' -- ':!game' ':!docs/superpowers' ':!docs/godot-retirement-checklist.md' \
  ':!decisions' ':!art/work' ':!art/export' ':!art/prompts'
```

(`:(exclude)` is required for the last two paths: `:!_scripts/...` fails on git
2.53 because `_` after `:!` parses as pathspec magic.)

The first scan omits two files whose `godot` mentions survive retirement and are
not active dependencies: the engine-isolation guard at `_scripts/check.sh:24`
(a negative grep that keeps `sim/` engine-free; section C requires keeping it)
and the optional `.godot/` ignore at `.gitignore:6-7` (section B: harmless if
kept). Do not remove or weaken either to satisfy the scan.

At the inventory baseline the first scan returns exactly the section A and B
files except those two retained entries. After steps 1–6 it must return nothing.
The second scan's non-A/B results are the section B and C files plus the C2
historical exceptions.

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
4. `art/build-asset.sh`: replace the Godot import step (lines 40–51) with the
   slice-5 Unreal import step; update the usage header (line 7).
   - Verify: `! grep -qi godot art/build-asset.sh`; run
     `art/build-asset.sh rifleman --install` — it must exit 0, copy the export
     set to the new Unreal runtime location, and print the rewritten import
     step's success line. Plain `art/build-asset.sh rifleman` skips the
     `--install` branch entirely, so it does not exercise the rewrite.
5. `.vscode/`: remove Godot extensions, launch configs, settings keys, tasks.
   - Verify: `! grep -qi godot .vscode/extensions.json .vscode/launch.json .vscode/settings.json .vscode/tasks.json`.
6. Docs and templates: apply every edit in section B.
   - Verify (zero-match oracle, scoped to active dependencies; it excludes this
     checklist and `docs/superpowers`, which mention Godot on purpose):
     `git grep --untracked -in godot -- README.md AGENTS.md CLAUDE.md CONTEXT.md docs/ art/CONTEXT.md .vscode/ .github/PULL_REQUEST_TEMPLATE.md .github/ISSUE_TEMPLATE/task.md map/effects/CONTEXT.md art/work/rifleman/CONTEXT.md art/export/CONTEXT.md art/asset-manifest.template.json .gitattributes ':!docs/superpowers' ':!docs/godot-retirement-checklist.md'`
     returns nothing. `--untracked` lets the positive control below be caught.
   - Positive control: `printf 'stale godot reference\n' > docs/_godot_probe.md`;
     the same command must return exactly that line; `rm docs/_godot_probe.md`
     and it returns nothing again.
   - Verify full tree: the C3 first scan returns nothing. It excludes the
     retained `check.sh` guard and the optional `.gitignore` entry, so those
     history/guard mentions do not fail the oracle.
   - Positive control for the full-tree scan: keep the probe file
     `docs/_godot_probe.md` from the control above in place, add `--untracked`
     to the C3 first scan, and confirm it names exactly that probe file. Remove
     the probe and the scan returns nothing again.
7. `_scripts/check.sh`: drop the `game/` skip case (line 19).
   - Verify: `make check` passes. The only remaining `godot` mention in the
     file is the retained engine-isolation guard (line 24); the C3 scan excludes
     it on purpose.
8. Regenerate indexes: `make gen`.
   - Verify: `make check` passes; every changed folder has a current `CONTEXT.md` (AGENTS rule 3).
9. Full parity on the final revision: `make check test`; each scenario hash run
   from `ci.yml`; Unreal editor build; `RtsBridge` automation; packaged host
   launch. `unreal/CONTEXT.md` documents the host publish
   (`dotnet publish tools/sim-host/SimHost.csproj -c Release -r linux-x64
   --self-contained -o unreal/dist/linux-x64`) and the editor automation, but it
   has no package or packaged-launch procedure yet. Defining those commands and
   recording one successful packaged launch is a pre-removal prerequisite owned
   by GRY-290. Do not treat retirement parity as proved until that record exists.
   - Verify: every command exits 0 with its expected hash/`Result: Success`; the
     packaged-launch record is on GRY-290.
10. Submit for review and merge (separate review; production approval does not
    apply — no deployment path here).

## Open gaps to close before step 1

- Debug overlay has no Unreal equivalent (section A, `game/debug/`).
- No Unreal CI lane exists yet (gate item 2).
- No package or packaged-launch procedure is defined or executed yet (step 9
  prerequisite, owned by GRY-290).
- `game/assets/rifleman/rifleman.glb` must be re-imported and verified in Unreal
  (slice 5) before `game/` is removed; the GLB itself may be kept under `art/`
  until the chosen interchange format is fixed.
