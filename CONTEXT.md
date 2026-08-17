# How to walk this repository

Two halves and a seam:

```
input ──► Commands ──► sim (.NET library, deterministic, fixed-point) ──► SimState / SimEvents ──► game (Godot) / tools (headless)
```

- `sim/` owns all game state and rules and advances by `Tick(commands)` at a fixed rate. It depends on nothing but the .NET BCL.
- `game/` converts input to Commands, renders SimState, never writes sim state directly.
- `tools/` drives the same sim headlessly (scenarios, replays, benches).
- `content/` is data both sides read.

Working folders (`sim/<system>/`, `game/<area>/`, `tools/<tool>/`) each carry a `CONTEXT.md`: owns / reads / writes / tests / do-not-touch. Read the one for the folder you are editing; do not read them all.

## Universes

- **live** — in force; implement and cite against it.
- **leftover** — still present, no longer the main path; touch only if in scope.
- **ghost** — named or planned, not wired (e.g. `sim/navigation/` until it exists). Do not implement against ghosts as if they existed.

## Name collisions

- "Unit" in design talk = an `EntityId` plus rows in the component arrays in `sim/units/` (ghost until created), not a class.
- "Order" = a per-unit queued intent (Move, AttackMove, …); "Command" = a tick-stamped player/AI input that *creates* orders. Commands cross the seam; orders never do.
- "Scenario" = a `content/scenarios/*.json` start state + optional command log; "Replay" = a scenario plus a recorded command log and expected end-state hash.

## Where state lives

- Game state: in the sim, per run — never on disk except as scenarios/replays.
- Task state: GitHub Issues/PRs.
- Decisions: `decisions/`. Rules and pillars: `docs/`.
