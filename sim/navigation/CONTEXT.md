# sim/navigation — get units from A to B without walking through each other

Owns: grid A* (8-dir, octile heuristic, no corner cutting), local separation, movement integration + wall slide.
Buildings (T31): footprint cells are unwalkable like cliffs. A goal inside a footprint (attack or
move onto a building) is swapped for the nearest open cell around it, closest to the start; the
path then ends at that cell's centre. A path planned before `GameMap.StructureEpoch` changed is
dropped and re-planned only if a remaining waypoint now sits in a footprint.
Domains (T25): every walkability check is the unit's `Domain`'s (`GameMap.IsWalkable(x, y, domain)`):
land on land and bridges, naval on water and under bridges, hover on both, air anywhere in bounds
(over cliffs and buildings). A goal on terrain the domain can't stand on (a ship sent at a tank on
the beach) is swapped for the nearest cell it can, like a footprint goal. Aircraft and surface
units never push each other apart (separation is per layer).
Reads: world grid (../world), unit positions (../world), orders (../orders).
Writes: unit positions/velocities only. Never health, never orders (order removal on arrival/unreachable is the one exception, by contract).
Runs at: tick step 3 (see ../CONTEXT.md). Plan budget: ≤25 A* runs/tick, spawn order.
Tests: `../tests/NavigationTests.cs`, `../tests/FootprintTests.cs`, `../tests/TerrainDomainTests.cs`; golden replay `content/scenarios/chokepoint-30.json`.
Do NOT touch from here: combat, presentation.
Change impact: see `map/effects/CONTEXT.md#navigation` — every change breaks golden hashes.
Known limits / leftover / ghost (2026-09-13): per-unit A* only — **flow fields are the known next step** if the 500-unit bench degrades; separation is pairwise O(n²) (`ponytail: fine to ~1k units; spatial hash if bench says so`); no formations by design (arrive-and-spread).
