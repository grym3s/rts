# sim/production — buildings, construction, unit queues

Owns: what may be built (prereq chains, `builtFrom` links), construction countdowns, per-building
train queues (cap 5), completion spawn placement (deterministic ring scan), refunds on cancel/destroy.
Reads: `content/buildings` (BuildingCatalog: cost, buildTicks, prereq, category), `content/units`
(UnitCatalog: cost, buildTicks, builtFrom — THE production-graph link), map walkability, UnitStore.
Writes: building Units (`Unit.Building` state, Structure armor, speed 0, raidable), spawned units,
money via the spend/refund callbacks it is given. Nothing else — never touches EconomyStore directly.
Runs at: tick step 2, after economy harvest in the same slot (`../CONTEXT.md`). Commands it consumes:
PlaceBuilding / Train / CancelProduction (delivered via the due list alongside OrderSystem).
Tests: `sim/tests/ProductionTests.cs`, `make test`.
Numbers: canon lives in `docs/factions/CONTEXT.md` (timing ladders) + `docs/factions/economy.md`;
this folder cites, never restates. Queue cap 5 is our tuning const (canon silent).
Do NOT: add power gating (slice 2b), rally-point paths (ring scan only until the game layer
demands otherwise), build-radius constraints, or refund-on-sell (no sell command exists).
Change impact: `map/effects/CONTEXT.md` (production row).
Known limits (2026-09-14): buildings don't block map cells yet (placement checks occupancy of
units, not structures' footprints); no MCV deploy (scenario places CY directly); one queue per
building; construction is not interruptible.
