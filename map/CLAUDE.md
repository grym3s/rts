# map — System Map of this repository

Answers "what is X" and "what else moves if I change X" without slurping the tree. The code is the source of truth; cards cite it. Read `CONTEXT.md` here for universes and name collisions, then open **one** card or the effects index.

## Catalog (stub lines — no card until the noun exists in code)

| Noun | Universe | Card | Owning code |
|---|---|---|---|
| Fix64 / FixVec2 | live | (none needed — `sim/core/Fix64.cs` is self-describing) | `sim/core/` |
| Command | live | stub | `sim/core/Commands.cs` |
| Tick / SimWorld | live | stub | `sim/core/SimWorld.cs` |
| Unit | ghost | — | `sim/units/` (not created) |
| Order | ghost | — | `sim/orders/` (not created) |
| MapGrid | ghost | — | `sim/world/` (not created) |
| Scenario | live (data) | stub | `content/scenarios/`, `tools/scenario/` |

`objects/` and `processes/` shelves are created only when the first real card is written (gated slice after the core loop lands). Effects index: `effects/CONTEXT.md`.
