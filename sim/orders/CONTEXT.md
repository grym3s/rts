# sim/orders — what units have been told to do

Owns: `MoveOrder` (target + path waypoint cursor) and `OrderSystem.ApplyCommands` (due Commands → orders).
Reads: due `Command`s handed over by `SimWorld.Step`. Writes: the orders dictionary (owned by the composition layer, passed in), and each ordered unit's engagement state: every `move` / `attack-move` / `attack` / `stop` clears target, attack-move and guard (`Disengage`); `attack` also drops the move order (combat issues the chase).
Runs at: tick step 1 (see ../CONTEXT.md).
Tests: `../tests/NavigationTests.cs`, `../tests/CombatTests.cs` (order × guard interplay).
Do NOT: move anything (that is `../navigation`), store paths' geometry ownership elsewhere — the path lives in the order, A* produces it.
Change impact: command semantics hit the sim/game seam — `map/effects/CONTEXT.md`.
