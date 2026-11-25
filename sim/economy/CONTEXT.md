# sim/economy — credits, harvest loop, (later: power, production)

Owns: credit balances per faction (start 5000), vein pools (finite, 25,000/field), refinery registry
(legacy scenario drop-off points; T33: refinery *buildings* are drop-offs too, read from UnitStore), harvester load/carry state transitions on `Unit`.
Reads: `world/UnitStore` (Harvest units), orders dictionary (issues deposit/fill MoveOrders), `content/units` role flags.
Writes: `EconomyStore` state; MoveOrders via the shared orders dict; `Unit.Load/Carrying/HomeVein`. Nothing else.
Runs at: tick step 2 (after commands, before navigation) — `../CONTEXT.md`.
Numbers: canon lives ONLY in `docs/factions/economy.md`; this folder cites, never restates.
Integer credits only — no fixed-point needed (every canon quantity is whole).
Tests: `sim/tests/EconomyTests.cs`, `make test`. Ported semantics: `tools/refmodel_economy.py` (D-001).
Do NOT: own buildings (sim/production places and completes them), power, production queues, Ascendant never-depleting
fields, or harvester pathing decisions — issue orders, navigation walks.
T33 (with `map` + `isRefinery` passed to IncomeStep/DepositStep, as both hosts do): a carrying
harvester heads for the nearest drop-off of its faction (scenario points first on ties, then
completed live refinery buildings in roster order; a dead refinery simply drops out, so harvesters
re-route); at a building it deposits once its cell touches the footprint (Chebyshev 1). An idle
empty harvester with no vein in FillRadius walks to its home vein while it lasts, else the
nearest live vein within `SeekRadius` = 12 cells; nothing in reach and a partial load: it goes to
unload. No refinery anywhere: it holds its load. Refinery = a building some harvester is
`builtFrom` (`UnitCatalog.IsRefinery`). Tests: `sim/tests/RefineryTests.cs`.
T38: `HarvestCommand` (passed to IncomeStep as `due`, applied first) makes a vein the home of your
live harvesters; empty or part-filled ones walk there, carrying ones deliver first and return to it.
A harvester walking to a live home vein fills only there, never at a vein it passes. Dry or unknown
veins and units that are not your harvesters are ignored. Tests: `sim/tests/RefineryTests.cs` (T38 section).
Known limits (2026-11-25): scenario refinery points remain for the old goldens (economy-2h);
fill/deposit radii are tuning constants; SpendCommand fails silently by design (atomic, no events yet).
