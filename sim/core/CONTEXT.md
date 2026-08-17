# sim/core — primitives every other sim folder depends on

Owns: `Fix64` (Q32.32 fixed-point), `FixVec2`, `Rng` (seeded xorshift), `EntityId`, `Command` types, `SimWorld` (state container + `Tick`).
Reads: nothing. Writes: nothing outside itself.
Runs at: `SimWorld.Tick` drives the order in `../CONTEXT.md`.
Tests: `../tests/Fix64Tests.cs`, `../tests/SimWorldTests.cs`.
Do NOT: add game rules here (they go in their own folder); add floats; add engine types.
Change impact: changing `Fix64` or `Rng` changes every golden replay — see `map/effects/CONTEXT.md`.
Known limits (2026-08-17): `Fix64.Sqrt` is integer Newton, unoptimised; no trig yet — add when navigation needs it.
