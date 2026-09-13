# sim/navigation — get units from A to B without walking through each other

Owns: grid A* (8-dir, octile heuristic, no corner cutting), local separation, movement integration + wall slide.
Reads: world grid (../world), unit positions (../world), orders (../orders).
Writes: unit positions/velocities only. Never health, never orders (order removal on arrival/unreachable is the one exception, by contract).
Runs at: tick step 3 (see ../CONTEXT.md). Plan budget: ≤25 A* runs/tick, spawn order.
Tests: `../tests/NavigationTests.cs`, `../tests/ScenarioTests.cs`; golden replay `content/scenarios/chokepoint-30.json`.
Do NOT touch from here: combat, presentation.
Change impact: see `map/effects/CONTEXT.md#navigation` — every change breaks golden hashes.
Known limits / leftover / ghost (2026-09-13): per-unit A* only — **flow fields are the known next step** if the 500-unit bench degrades; separation is pairwise O(n²) (`ponytail: fine to ~1k units; spatial hash if bench says so`); no formations by design (arrive-and-spread).
