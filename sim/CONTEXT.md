# sim — the deterministic simulation (.NET library, engine-free)

Owns: all game state and rules. Advances by `SimWorld.Tick(commands)` at a fixed rate (`SimWorld.TicksPerSecond`).
Reads: `Command`s (from game/tools/AI), scenario data from `content/`.
Writes: its own state; emits events for the current tick. Nothing else.
Never: references Godot or any engine; uses floats; reads wall-clock time.

## Tick order (the one place this is stated)
1. power: recompute net power per faction (`power/`, live)
2. income: harvest income before spend (`economy/`, live)
3. orders: apply commands for this tick (`orders/`, live)
4. production: build queues, spawn units (`production/`, live)
5. navigation: path + avoidance + movement (`navigation/`, live)
6. deposit: credit arrivals after movement (`economy/`, live)
7. combat: acquire, fire, damage, death (`combat/`, live)
8. cleanup: despawn dead, flush events

## Folders
| Folder | Status |
|---|---|
| `core/` | live — Fix64, Rng, EntityId, Command, SimWorld |
| `tests/` | live — xUnit |
| `world/`, `units/`, `orders/`, `navigation/` | live |
| `combat/` | live — acquire/fire/damage/death (see `combat/CONTEXT.md`) |
| `economy/` | live — credits + harvest loop slice 1 (see `economy/CONTEXT.md`) |
| `production/` | live — buildings, construction, unit queues slice 2a (see `production/CONTEXT.md`) |

Tests: `make test`. Change impact: `map/effects/CONTEXT.md`.
