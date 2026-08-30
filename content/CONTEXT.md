# content — data the sim and game read

Plain JSON authored from the design canon in `docs/factions/`. The faction files are the source; these files are the machine-readable subset. All files carry `schemaVersion`.

| Folder | Holds |
|---|---|
| `units/` | one file per unit — see the schema below (`schemaVersion: 2`) |
| `scenarios/` | `{schemaVersion, seed, ticks}` (+ units/spawns/commands as `sim/units`, `sim/orders` land) |
| `buildings/` | (not created — lands with the economy system; schema will mirror `units/` identity + power/prereq) |
| `maps/` | (not created) |

## `units/` schema (v2)

A unit file has three parts, and the split is the point: **identity is canon** (straight from the faction files, trustworthy), **`stats` and `weapon` are provisional** (untuned placeholders until the combat sim + `tools/scenario` tune them, MBT mirror first — `docs/factions/counter-matrix.md`). Never treat a `stats`/`weapon` number as balanced; do treat identity as fixed.

### Identity — required, canon-backed

| Field | Type | From canon | Notes |
|---|---|---|---|
| `schemaVersion` | int | — | `2` |
| `id` | string | — | kebab-case, unique across all factions |
| `name` | string | faction file | display name |
| `faction` | enum | faction file | `coalition` \| `hegemony` \| `ascendant` |
| `role` | string | unit table | `mbt`, `basic-infantry`, `scout`, `harvester`, `engineer`, `anti-armor`, `anti-air`, `artillery`, … |
| `tier` | int | tier table | `1` \| `2` \| `3` |
| `builtFrom` | string | building table | producing building id (`vehicle-bay`, `barracks`, …) — the prerequisite |
| `cost` | int | unit table | credits (faction-adjusted value as written in the faction file) |
| `buildTimeSeconds` | number | unit table | ×20 = ticks at 20 t/s |
| `armor` | enum | `Armor / Attack` | `infantry` \| `light` \| `heavy` \| `air` \| `structure` (`counter-matrix.md`) |

### `stats` — required object, **provisional values**

| Field | Type | Notes |
|---|---|---|
| `hp` | number | untuned; faction HP modifier is *already* baked into the value, not applied at load |
| `speed` | number | world cells / second (`0` for immobile) |
| `sight` | number | vision radius in cells |

### `weapon` — optional object (omit for non-combat units: harvester, engineer, pure support)

| Field | Type | From canon | Notes |
|---|---|---|---|
| `damageType` | enum | `Armor / Attack` | `small-arms` \| `autocannon` \| `ap` \| `explosive` \| `missile` \| `energy` |
| `damage` | number | — | **base** damage, pre-matrix; final = `damage × matrix_cell` (`counter-matrix.md`). Provisional |
| `range` | number | unit table where stated | cells (e.g. Coalition MBT 12); provisional otherwise |
| `cooldownTicks` | int | — | ticks between shots; provisional |
| `targets` | string[] | `counter-matrix.md` (targeting) | subset of `["ground","air","sea"]` — which domains the weapon can engage *at all*, separate from the multiplier |

### `verbs` — optional flags (owned-verb applicability, `owned-verbs.md`)

Present only where canon says the unit has it: `marksOnHit` (Coalition), `conscriptable` (Hegemony infantry), `phases` (Ascendant phase-capable). Post-slice behaviour; authored now because which unit carries the verb *is* canon.

## Rules

- kebab-case ids; unique across factions.
- Numbers authored as decimals, converted to Fix64 at load. Authoring-only `FromDouble` is allowed (ADR 0003); never a runtime float in `sim/`.
- Identity fields come from the faction file — if you change one here, change it there in the same PR (they must not drift).
- Add or rename a field → bump `schemaVersion` and update the loader (when it exists) in the same PR.
- A `stats`/`weapon` change is a balance change → needs a balance scenario run once the combat sim exists (`tools/scenario`).

Change impact: `../map/effects/CONTEXT.md` — unit stats (balance), the counter matrix (armor/`damageType`), economy (cost/`buildTimeSeconds`), owned verbs (`verbs`).
