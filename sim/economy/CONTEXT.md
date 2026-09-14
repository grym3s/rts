# sim/economy — credits, harvest loop, (later: power, production)

Owns: credit balances per faction (start 5000), vein pools (finite, 25,000/field), refinery registry
(slice 1: scenario-placed points, not entities), harvester load/carry state transitions on `Unit`.
Reads: `world/UnitStore` (Harvest units), orders dictionary (issues deposit/fill MoveOrders), `content/units` role flags.
Writes: `EconomyStore` state; MoveOrders via the shared orders dict; `Unit.Load/Carrying/HomeVein`. Nothing else.
Runs at: tick step 2 (after commands, before navigation) — `../CONTEXT.md`.
Numbers: canon lives ONLY in `docs/factions/economy.md`; this folder cites, never restates.
Integer credits only — no fixed-point needed (every canon quantity is whole).
Tests: `sim/tests/EconomyTests.cs`, `make test`. Ported semantics: `tools/refmodel_economy.py` (D-001).
Do NOT: model buildings as entities (PR 2), power, production queues, Ascendant never-depleting
fields, or harvester pathing decisions — issue orders, navigation walks.
Known limits (2026-09-14): refineries are static scenario points (raiding them needs entities);
fill/deposit radii are tuning constants; SpendCommand fails silently by design (atomic, no events yet).
