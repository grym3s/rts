# sim/setup — one match's world, wired in the canonical tick order

Owns: `MatchWorld.FromScenario` — builds the stores from a scenario-v2 object (map, bridges, units, veins, structures, bridge huts, refineries, `ai` sides) and registers every system in the tick order of `sim/CONTEXT.md`, plus the hash mixers. The only place that order is wired.
Reads: `content/units`, `content/buildings` (catalogs), the scenario JSON.
Writes: nothing outside the new world it returns.
Runs at: load time (wiring only; the systems run in their own steps).
Tests: every test that builds through it (`sim/tests/AbilityTests.cs` `Arena`), `tools/sim-host.tests` (replay parity), the CI scenarios.
Do NOT: put rules here; wire a system somewhere else (tools/scenario and tools/sim-host call this, never their own list).
Change impact: `map/effects/CONTEXT.md` (tick order)
Known limits (2026-11-28): sim/tests still wire small hand-made worlds of their own for focused tests; that is fine, they are not the match.
