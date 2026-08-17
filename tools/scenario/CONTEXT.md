# tools/scenario — run a scenario headlessly

Owns: loading `content/scenarios/*.json`, stepping the sim N ticks, printing/asserting the state hash. Golden-replay tests will call this.
Reads: `sim/` public API, scenario JSON. Writes: stdout, exit code.
Tests: covered by `make scenario S=content/scenarios/smoke.json`; CI runs it.
Do NOT: add game rules; render anything.
Known limits (2026-08-17): scenario schema is `{schemaVersion, seed, ticks}` only — units and command logs arrive with `sim/units` and `sim/orders`.
