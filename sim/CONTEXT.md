# sim — the deterministic simulation (.NET library, engine-free)

Owns: all game state and rules. Advances by `SimWorld.Tick(commands)` at a fixed rate (`SimWorld.TicksPerSecond`).
Reads: `Command`s (from game/tools/AI), scenario data from `content/`.
Writes: its own state; emits events for the current tick. Nothing else.
Never: references Godot or any engine; uses floats; reads wall-clock time.

## Tick order (the one place this is stated)
1. apply commands for this tick → orders (`orders/`, ghost)
2. (future) production / economy
3. navigation: path + avoidance + movement (`navigation/`, ghost)
4. combat: acquire, fire, damage, death (`combat/`, ghost)
5. (future) visibility
6. cleanup: despawn dead, flush events

## Folders
| Folder | Status |
|---|---|
| `core/` | live — Fix64, Rng, EntityId, Command, SimWorld |
| `tests/` | live — xUnit |
| `world/`, `units/`, `orders/`, `navigation/`, `combat/` | ghost — create with the issue that implements them, each with its own CONTEXT.md |

Tests: `make test`. Change impact: `map/effects/CONTEXT.md`.
