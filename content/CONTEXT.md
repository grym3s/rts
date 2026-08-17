# content — data the sim and game read

| Folder | Schema (all files carry `schemaVersion`) |
|---|---|
| `units/` | `{schemaVersion, id, name, speed, hp, range, damage, cooldownTicks}` — numbers are authored as decimals and converted to Fix64 at load |
| `scenarios/` | `{schemaVersion, seed, ticks}` (+ units/spawns/commands as `sim/units`, `sim/orders` land) |
| `maps/` | (not created) |

Rules: kebab-case ids; add a field → bump `schemaVersion` and update the loader in the same PR; unit balance changes need a balance scenario run (see `tools/scenario`).
Change impact: `map/effects/CONTEXT.md` (unit stats row).
