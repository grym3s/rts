# sim/production — buildings, construction, unit queues

Owns: what may be built (prereq chains, `builtFrom` links), construction countdowns, per-building
train queues (cap 5), completion spawn placement (deterministic ring scan), refunds on cancel/destroy.
Reads: `content/buildings` (BuildingCatalog: cost, buildTicks, prereq, category), `content/units`
(UnitCatalog: cost, buildTicks, builtFrom — THE production-graph link), map walkability, UnitStore.
Writes: building Units (`Unit.Building` state, Structure armor, speed 0, raidable), spawned units,
money via the spend/refund callbacks it is given. Nothing else — never touches EconomyStore directly.
Runs at: tick step 2, after economy harvest in the same slot (`../CONTEXT.md`). Commands it consumes:
PlaceBuilding / Train / CancelProduction / Deploy (delivered via the due list alongside OrderSystem).
Tests: `sim/tests/ProductionTests.cs`, `make test`.
Numbers: canon lives in `docs/factions/CONTEXT.md` (timing ladders) + `docs/factions/economy.md`;
this folder cites, never restates. Queue cap 5 is our tuning const (canon silent).
Do NOT: add power gating (slice 2b), rally-point paths (ring scan only until the game layer
demands otherwise) or refund-on-sell (no sell command exists).
Change impact: `map/effects/CONTEXT.md` (production row).
Footprints (T31): each building covers `footprint: [w,h]` cells (default by category, see
`content/CONTEXT.md`) centred on the placement cell (origin = at − w/2); placement refuses unless
every cell is in bounds, open terrain, free of buildings and of live units; the building stands
at the footprint centre; a destroyed building's cells are freed at the start of the next
production step (`GameMap.ReleaseStructures`). Tests: `sim/tests/FootprintTests.cs`.
Deploy and build radius (T32): `DeployCommand` turns a unit with `DeploysTo` (content `deploysTo`,
the MCVs: mobile-command / foundry-crawler / seed-core) into that yard where it stands, free, the
3x3 centred on its cell (clear but for the MCV itself; no radius); the MCV leaves the roster at
cleanup with no death event and the yard builds for its BuildTicks. `place` of anything but a
construction yard must come within `ProductionSystem.BuildRadius` = 6 cells (Chebyshev, 1 =
touching; our tuning, canon gives no number) of a footprint of the faction's own live
buildings. A yard by `place` skips the radius: the scenario bootstrap. Tests: `sim/tests/DeployTests.cs`.
`ProductionSystem.CheckPlace` (T36) is the place rules as one pure check returning a
`PlaceVerdict` (Ok, WrongFaction, Prereq, Blocked, Occupied, Radius, Credits) and the footprint;
`place` and the host's read-only `query` both use it, so the Unreal build ghost never re-implements them.
Refineries (T33): a finished building that some harvester is `builtFrom` ships one of them, free
(canon, economy.md); no footprint may cover a vein cell (`GameMap.MarkResource`). Trained and
shipped units stand at the centre of their spawn cell (a corner spawn straddled four cells and
wedged against footprints).
Known limits (2026-11-25): scenarios still bootstrap yards by `place` (and a player could too, for
its cost); one queue per
building; construction is not interruptible.
