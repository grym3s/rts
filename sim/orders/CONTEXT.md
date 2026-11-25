# sim/orders — what units have been told to do

Owns: `MoveOrder` (target + path waypoint cursor) and `OrderSystem.ApplyCommands` (due Commands → orders). T26 `BridgeCommand` sets `Unit.BridgeTarget` on the faction's own units (orders cleared; sim/combat's BridgeSystem walks them there); every other order clears it. T28 `CaptureCommand` likewise sets `Unit.CaptureTarget` on own living engineers that may capture (CaptureSystem walks them there); every other order clears it. T29 `PhaseCommand` phases own phase-capable living units whose
cooldown is over (`PhaseUntilTick = tick + 40`, `PhaseReadyTick = that + 200`); it is not a move
order, so it clears nothing.
Reads: due `Command`s handed over by `SimWorld.Step`. Writes: the orders dictionary (owned by the composition layer, passed in), and each ordered unit's engagement state: every `move` / `attack-move` / `attack` / `stop` clears target, attack-move and guard (`Disengage`); `attack` also drops the move order (combat issues the chase).
Runs at: tick step 1 (see ../CONTEXT.md).
Tests: `../tests/NavigationTests.cs`, `../tests/CombatTests.cs` (order × guard interplay).
Do NOT: move anything (that is `../navigation`), store paths' geometry ownership elsewhere — the path lives in the order, A* produces it.
Change impact: command semantics hit the sim/game seam — `map/effects/CONTEXT.md`.
