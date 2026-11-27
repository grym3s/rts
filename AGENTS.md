# RTS — working title

A real-time strategy game. Deterministic simulation in `sim/` (.NET, engine-free), presented by Unreal 5.8 in `unreal/` through the `tools/sim-host` bridge (newline-delimited JSON over stdio). `game/` is the old Godot shell, kept until the retirement gate in `docs/godot-retirement-checklist.md` passes.
Built on ICM: folders carry architecture, each working folder has a `CONTEXT.md`, `map/` cites code and never restates it. Load only what the task needs.

## Where things live

| Folder | Holds |
|---|---|
| `sim/` | game rules and state; fixed tick, fixed-point, no engine references — the thing that matters |
| `unreal/` | Unreal presentation: input, camera, HUD, fog of war, debug overlays; sends Commands to the host, draws its snapshots, owns no rules |
| `tools/` | `sim-host` (the bridge Unreal launches), headless scenario runner, replay verify, bench |
| `content/` | unit / building / map / scenario data (plain JSON) |
| `game/` | legacy Godot presentation (retiring; don't build new features here) |
| `map/` | System Map: shared nouns + change-impact index (`map/effects/CONTEXT.md`) |
| `decisions/` | ADRs — accepted decisions; superseded ones marked; index generated |
| `docs/` | design pillars, conventions, workflow |
| `_scripts/` | regenerate indexes, structure checks (used by CI) |
| `_templates/` | starter `CONTEXT.md` to copy when a new working folder is created |

## Route by task

| If asked to… | Read first | Then |
|---|---|---|
| change movement / pathing | `sim/navigation/CONTEXT.md`, `map/effects/CONTEXT.md` | edit, `make test`, run scenarios |
| change combat / targeting / damage | `sim/combat/CONTEXT.md`, `map/effects/CONTEXT.md` | same |
| add or tune a unit type | `content/CONTEXT.md`, then `sim/units/CONTEXT.md` | data first; code only for new behaviour |
| change the computer opponents | `sim/ai/CONTEXT.md` | `make test` (`AiTests` plays a full skirmish) |
| change how it looks or controls | `unreal/CONTEXT.md` | publish the host, build `RtsBridgeEditor`, run the `RtsBridge` automation tests, manual check in the editor |
| change what the host sends or accepts | `tools/sim-host/CONTEXT.md` | `make host-test`; keep it additive to protocolVersion 1 |
| answer "what does changing X hit" | `map/effects/CONTEXT.md` | open the named contracts/cards |
| create a new sim system or presentation area | `_templates/folder-CONTEXT.md`, the parent `CONTEXT.md` | copy the template, fill every line, `make check` |
| record a decision | `decisions/CONTEXT.md` | new ADR by PR |
| status of a task | GitHub Issues / PRs; the road to the demo is `docs/superpowers/plans/2026-11-23-road-to-skirmish-30.md` | not stored in this tree otherwise |
| understand why things are this way | `decisions/2026-08-17-foundation-ultra-review.md` | read only the section you need |

## Rules that are not negotiable

1. `sim/` never references any engine (CI checks).
2. Presentation (`unreal/`, `game/`) changes sim state only through Commands.
3. Update the `CONTEXT.md` of any folder whose boundary you changed, in the same PR.
4. Don't load `map/` wholesale — use the effects index and open one card.
5. Validate with `make check test` before reporting done.
