# sim — the deterministic simulation (.NET library, engine-free)

Owns: all game state and rules. Advances by `SimWorld.Tick(commands)` at a fixed rate (`SimWorld.TicksPerSecond`).
Reads: `Command`s (from game/tools/AI), scenario data from `content/`.
Writes: its own state; emits events for the current tick. Nothing else.
Never: references Godot or any engine; uses floats; reads wall-clock time.

## Tick order (the one place this is stated)
Wired once, in `setup/MatchWorld` (the scenario runner and the host both build from it).
0. input: computer players (`ai/`, scenario `ai`) add their Commands to the step's input (`SimWorld.InputSources`)
1. power: recompute net power per faction (`power/`, live)
2. income: harvest income before spend (`economy/`, live); then the oil drip (T28 `DripStep`, owned derricks, once a second)
3. orders: apply commands for this tick (`orders/`, live)
4. production: build queues, spawn units (`production/`, live); then transport (`transport/`: load, unload, teleport, passengers ride)
5. navigation: path + avoidance + movement (`navigation/`, live)
6. deposit: credit arrivals after movement (`economy/`, live)
7. combat: acquire, fire, damage, death (`combat/`, live); then bridges (T26 `BridgeSystem`: attack, collapse, engineer repair); then captures (T28 `CaptureSystem`: engineer touch changes owner; Mindbender channel, Weaver repair, Operative sabotage, Hijacker); then rig hold-to-capture (`combat/Control`); then carriers (`combat/Carriers`: launch and steer the wing); then superweapons (`superweapons/`: charge, strike)
8. cleanup: despawn dead
9. match: decide win / lose (`match/`, live)

Events (`core/SimWorld.Emit`) are buffered per tick and cleared at the next step's start: combat emits `Shot` and `Death`, match emits `MatchEnded`. Events never enter the state hash.

## Folders
| Folder | Status |
|---|---|
| `core/` | live — Fix64, Rng, EntityId, Command, SimWorld |
| `tests/` | live — xUnit |
| `world/`, `units/`, `orders/`, `navigation/` | live |
| `combat/` | live — acquire/fire/damage/death (see `combat/CONTEXT.md`) |
| `economy/` | live — credits + harvest loop slice 1 (see `economy/CONTEXT.md`) |
| `production/` | live — buildings, construction, unit queues slice 2a (see `production/CONTEXT.md`) |
| `match/` | live — win / lose (see `match/CONTEXT.md`) |
| `power/` | live — supply, draw, low power (defenses hold fire, superweapons stop charging) |
| `setup/` | live — `MatchWorld`: builds a world from a scenario and wires the tick order |
| `transport/` | live — transports, bunkers, the Tunnel Network, Teleport Nodes (see `transport/CONTEXT.md`) |
| `superweapons/` | live — charge, fire on command, telegraphed strike (see `superweapons/CONTEXT.md`) |
| `ai/` | live — computer opponents (see `ai/CONTEXT.md`) |

Tests: `make test`. Change impact: `map/effects/CONTEXT.md`.
