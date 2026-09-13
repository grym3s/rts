# sim/world — the playfield and its roster

Owns: `GameMap` (square walkable grid; cell (cx,cy) spans [cx,cx+1]²), `UnitStore` + `Unit` (positions, velocity, speed, radius).
Reads: scenario data (via tools/scenario). Writes: its own structures only.
Runs at: created before the first tick; mutated by `sim/orders` + `sim/navigation` during a tick.
Tests: `../tests/NavigationTests.cs`, `../tests/ScenarioTests.cs`.
Do NOT: hold orders (that is `../orders`), pathfinding (`../navigation`), combat state (no folder yet — ADR: combat comes later).
Change impact: grid geometry or unit fields change scenario hashes — `map/effects/CONTEXT.md`.
