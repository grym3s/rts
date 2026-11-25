# effects — if you are changing X, open these

First-order only. If a row and a folder contract disagree, fix the contract and this row in the same PR.

| Changing… | Open | Hits | Does not hit |
|---|---|---|---|
| the sim/game seam (`Command`, `SimState`, events) | `sim/core/CONTEXT.md`, `game/CONTEXT.md`, ADR 0001 | every consumer of sim state; golden replays | content files |
| `Fix64` / `Rng` | `sim/core/CONTEXT.md`, ADR 0003 | **all** sim behaviour and every golden replay hash | presentation |
| tick order | `sim/CONTEXT.md` | every system's assumptions about what ran before it | content |
| unit stats | `content/CONTEXT.md` | balance scenarios | code (unless a new field) |
| the counter matrix (armor × damage) | `docs/factions/counter-matrix.md`, ADR 0005 | **all** combat balance + every golden replay (once combat reads it); the in-game armor/damage table | navigation, presentation |
| an owned verb (Marking / Conscription / Phase) | `docs/factions/owned-verbs.md`, ADR 0005 | that faction's combat/economy balance; the matchup scenario covering it; golden replays (once live) | the other factions' verbs; presentation |
| navigation (when created) | `sim/navigation/CONTEXT.md` | movement scenarios, bench; building footprints (T31) also feed it via `GameMap` | combat, presentation |
| building footprints / placement (T31) | `sim/production/CONTEXT.md`, `sim/world/CONTEXT.md` | pathing around buildings, attack-move onto buildings, MCV deploy + build radius (T32), the Unreal build menu ghost (T36), `produce-1t1` hash, any scenario that places buildings | terrain-only scenarios (no buildings: hashes unchanged) |
| combat (when created) | `sim/combat/CONTEXT.md` | balance scenarios; `shot` / `death` events (health bars, tracers) | navigation, presentation |
| match rules (win / lose) | `sim/match/CONTEXT.md` | the host snapshot `match` field and `match_end` event; Unreal win/lose screen (T19); `match-end` scenario | golden replay hashes (match state is derived, not hashed) |
| how things look/control | `game/CONTEXT.md` | nothing in `sim/` | sim |
