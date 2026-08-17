# effects — if you are changing X, open these

First-order only. If a row and a folder contract disagree, fix the contract and this row in the same PR.

| Changing… | Open | Hits | Does not hit |
|---|---|---|---|
| the sim/game seam (`Command`, `SimState`, events) | `sim/core/CONTEXT.md`, `game/CONTEXT.md`, ADR 0001 | every consumer of sim state; golden replays | content files |
| `Fix64` / `Rng` | `sim/core/CONTEXT.md`, ADR 0003 | **all** sim behaviour and every golden replay hash | presentation |
| tick order | `sim/CONTEXT.md` | every system's assumptions about what ran before it | content |
| unit stats | `content/CONTEXT.md` | balance scenarios | code (unless a new field) |
| navigation (when created) | `sim/navigation/CONTEXT.md` | movement scenarios, bench | combat, presentation |
| combat (when created) | `sim/combat/CONTEXT.md` | balance scenarios | navigation, presentation |
| how things look/control | `game/CONTEXT.md` | nothing in `sim/` | sim |
