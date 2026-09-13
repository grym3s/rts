# sim/units — content-driven spawn parameters

Owns: `UnitCatalog` (content/units/*.json v2 → id, speed, radius). The only place authoring doubles may enter the sim (FromDouble, authoring-time only).
Reads: content/units/. Writes: nothing at runtime; per-match runtime unit state lives in ../world/UnitStore.
Runs at: load time (scenario/game boot).
Tests: `../tests/ScenarioTests.cs`.
Do NOT: hold mutable unit state (world), stats beyond spawn-relevant fields (combat reads its own slices later), apply balance — catalog is a mirror.
Change impact: unit stats row in `map/effects/CONTEXT.md`.
