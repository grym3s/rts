# sim/orders — what units have been told to do

Owns: `MoveOrder` (target + path waypoint cursor) and `OrderSystem.ApplyCommands` (due Commands → orders).
Reads: due `Command`s handed over by `SimWorld.Step`. Writes: the orders dictionary (owned by the composition layer, passed in).
Runs at: tick step 1 (see ../CONTEXT.md).
Tests: `../tests/NavigationTests.cs`.
Do NOT: move anything (that is `../navigation`), store paths' geometry ownership elsewhere — the path lives in the order, A* produces it.
Change impact: command semantics hit the sim/game seam — `map/effects/CONTEXT.md`.
