# sim/world — the playfield and its roster

Owns: `GameMap` (square walkable grid; cell (cx,cy) spans [cx,cx+1]²; terrain `Block` plus a separate building-footprint layer, T31: `IsWalkable` = open terrain and no building, `IsTerrainWalkable`/`IsBlocked` = terrain only, `StructureEpoch` bumps on every place/release, not hashed since it follows from building units), `UnitStore` + `Unit` (positions, velocity, speed, radius).
Reads: scenario data (via tools/scenario). Writes: its own structures only.
Runs at: created before the first tick; mutated by `sim/orders` + `sim/navigation` during a tick.
Tests: `../tests/NavigationTests.cs`, `../tests/FootprintTests.cs`.
Do NOT: hold orders (that is `../orders`), pathfinding (`../navigation`), combat target/cooldown state (that is `../combat`).
Change impact: grid geometry or unit fields change scenario hashes — `map/effects/CONTEXT.md`.
