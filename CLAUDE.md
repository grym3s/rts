# RTS — working title

A real-time strategy game. Deterministic simulation in `sim/` (.NET, engine-free), presented by Godot 4 in `game/`.
Built on ICM: folders carry architecture, each working folder has a `CONTEXT.md`, `map/` cites code and never restates it. Load only what the task needs.

## Where things live

| Folder | Holds |
|---|---|
| `sim/` | game rules and state; fixed tick, fixed-point, no engine references — the thing that matters |
| `game/` | Godot presentation, input, camera, UI, debug overlays; talks to `sim/` only via Commands |
| `tools/` | headless scenario runner, replay verify, bench |
| `content/` | unit / map / scenario data (plain JSON) |
| `map/` | System Map: shared nouns + change-impact index (`map/effects/CONTEXT.md`) |
| `decisions/` | ADRs — accepted decisions; superseded ones marked; index generated |
| `docs/` | design pillars, conventions, workflow |
| `_scripts/` | regenerate indexes, structure checks (used by CI) |

## Route by task

| If asked to… | Read first | Then |
|---|---|---|
| change movement / pathing | `sim/navigation/CONTEXT.md` (not yet created — see `sim/CONTEXT.md`), `map/effects/CONTEXT.md` | edit, `make test`, run scenarios |
| change combat / targeting / damage | `sim/combat/CONTEXT.md` (not yet created), `map/effects/CONTEXT.md` | same |
| add or tune a unit type | `content/CONTEXT.md`, then `sim/units/CONTEXT.md` | data first; code only for new behaviour |
| change how it looks or controls | `game/CONTEXT.md` | `make godot-test` + manual check |
| answer "what does changing X hit" | `map/effects/CONTEXT.md` | open the named contracts/cards |
| record a decision | `decisions/CONTEXT.md` | new ADR by PR |
| status of a task | GitHub Issues / PRs | not stored in this tree |
| understand why things are this way | `decisions/2026-08-17-foundation-ultra-review.md` | read only the section you need |

## Rules that are not negotiable

1. `sim/` never references Godot (CI checks).
2. `game/` changes sim state only through Commands.
3. Update the `CONTEXT.md` of any folder whose boundary you changed, in the same PR.
4. Don't load `map/` wholesale — use the effects index and open one card.
5. Validate with `make check test` before reporting done.
