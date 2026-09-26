# map — System Map of this repository

Answers "what is X" and "what else moves if I change X" without slurping the tree. The code is the source of truth; cards cite it. Read `CONTEXT.md` here for universes and name collisions, then open **one** card or the effects index.

## Catalog (stub lines — no card until the noun needs one beyond its code)

| Noun | Universe | Card | Owning code |
|---|---|---|---|
| Fix64 / FixVec2 | live | (none needed — `sim/core/Fix64.cs` is self-describing) | `sim/core/` |
| Command | live | stub | `sim/core/Commands.cs` |
| Tick / SimWorld | live | stub | `sim/core/SimWorld.cs` |
| Unit | live | — | `sim/world/` (`Unit`, `UnitStore`), spawn params `sim/units/` |
| Order | live | — | `sim/orders/` (`MoveOrder`, `OrderSystem`) |
| GameMap | live | — | `sim/world/` (`GameMap`) |
| Building | live | — | `sim/production/` + `sim/world/` (`Unit.Building`) |
| Scenario | live (data) | stub | `content/scenarios/`, `tools/scenario/` |
| Visibility / fog | ghost | — | (not created) |

`objects/` and `processes/` shelves are created only when the first real card is written (gated slice after the core loop lands). Effects index: `effects/CONTEXT.md`.
