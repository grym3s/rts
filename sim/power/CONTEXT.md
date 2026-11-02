# sim/power — supply/draw ledger and production parity gate (issue #27)

Owns: per-faction net power (supply minus draw over alive, construction-complete
buildings) and the low-power production rule.
Reads: `world/UnitStore` (Building component, `BuildingCatalog.Power` per building type),
production queue state. Writes: `PowerStore` net value + hash mixer contribution only.
Rule: while net < 0, a building's production countdown decrements only when
`(tick & 1) != (buildingId & 1)` — integer parity gate, exactly half rate, staggered by
parity. First tick with net >= 0 runs full rate; no catch-up, no stored deficit, no
hysteresis. Integer arithmetic only (ADR 0003: no fractions in sim).
Runs at: canonical tick order position 1 (Power Income Order Production Navigation
Deposit Combat Despawn) — `../CONTEXT.md`. PowerSystem reads buildings' prior-tick
construction/HP state, so position relative to Production is behavior-neutral; it is
pinned at 1 by the canonical comment in `tools/scenario/Program.cs`.
Numbers: reactor +100, barracks -20 live in `content/buildings/*` (catalog fields).
Tests: `sim/tests/PowerTests.cs`, `make test`; scenario goldens `content/scenarios/power-*.json`.
Do NOT: add hysteresis or deficit memory, introduce fractional decrements, gate combat or
movement, or wire power into sim-host beyond the canonical order.
