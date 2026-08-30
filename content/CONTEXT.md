# content — data the sim and game read

Plain JSON authored from the design canon in `docs/factions/`. The faction files are the source; these files are the machine-readable subset. All files carry `schemaVersion`.

| Folder | Holds |
|---|---|
| `units/` | one file per unit — see the schema below (`schemaVersion: 2`) |
| `scenarios/` | `{schemaVersion, seed, ticks}` (+ units/spawns/commands as `sim/units`, `sim/orders` land) |
| `buildings/` | one file per structure — schema below (`schemaVersion: 1`) |
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
| `armor` | enum | `Armor / Attack` | `infantry` \| `light` \| `heavy` \| `air` \| `structure`; naval extension `light-naval` \| `heavy-naval` \| `submerged` (`counter-matrix.md`) |

### `stats` — required object, **provisional values**

| Field | Type | Notes |
|---|---|---|
| `hp` | number | untuned; faction HP modifier is *already* baked into the value, not applied at load |
| `speed` | number | world cells / second (`0` for immobile) |
| `sight` | number | vision radius in cells |

### `weapon` — optional object (omit for non-combat units: harvester, engineer, pure support)

| Field | Type | From canon | Notes |
|---|---|---|---|
| `damageType` | enum | `Armor / Attack` | `small-arms` \| `autocannon` \| `ap` \| `explosive` \| `missile` \| `energy`; naval extension `naval-gun` (shares the AP column) \| `torpedo` (`counter-matrix.md`) |
| `damage` | number | — | **base** damage, pre-matrix; final = `damage × matrix_cell` (`counter-matrix.md`). Provisional |
| `range` | number | unit table where stated | cells (e.g. Coalition MBT 12); provisional otherwise |
| `cooldownTicks` | int | — | ticks between shots; provisional |
| `targets` | string[] | `counter-matrix.md` (targeting) | subset of `["ground","air","sea"]` — which domains the weapon can engage *at all*, separate from the multiplier |

### `verbs` — optional flags (owned-verb applicability, `owned-verbs.md`)

Present only where canon says the unit has it: `marksOnHit` (Coalition — a weapon that marks the struck target), `marksAura` (Coalition — a radius Marking source: Comms Rig, Spotter Drone), `conscriptable` (Hegemony infantry), `phases` (Ascendant phase-capable, per `owned-verbs.md`). Post-slice behaviour; authored now because which unit carries the verb *is* canon.

## `buildings/` schema (v1)

Same identity/provisional split as units. Structures are **faction-namespaced**: unlike units (whose names are globally unique), factions share building archetypes — Coalition and Hegemony both field a "Refinery" and a "Shipyard" — so a building `id` is **`<faction>-<name>`** (`coalition-refinery`, `ascendant-manifold`), and a unit's `builtFrom` is that prefixed id. That is the one link between the two folders; `builtFrom` on the unit is the source of truth for what a production building makes (a building does not re-list its units, to avoid drift).

### Identity — required, canon-backed

| Field | Type | From canon | Notes |
|---|---|---|---|
| `schemaVersion` | int | — | `1` |
| `id` | string | — | `<faction>-<name>`, kebab-case, globally unique |
| `name` | string | building table | display name (e.g. "Sensor Command") |
| `faction` | enum | — | `coalition` \| `hegemony` \| `ascendant` |
| `category` | enum | — | `construction-yard` \| `power` \| `economy` \| `production` \| `tech` \| `air` \| `naval` \| `defense` \| `superweapon` \| `logistics` \| `offshore` |
| `tier` | int | building table | `1` \| `2` \| `3` |
| `prereq` | string\|null | Prereq column | the building id required first; `null` for the Construction Yard (placed by deploying the faction MCV) |
| `cost` | int | building table | credits (faction-adjusted value as written) |
| `buildTimeSeconds` | number | building table | ×20 = ticks |
| `power` | int | Power column | economy.md mapping: `+`→+100, `−`→−20, `−−`→−50, `−−−`→−150, none→0 |
| `produces` | string[] | Produces/unlocks column | short slugs of what it builds or unlocks; the authoritative building→unit link is the unit's `builtFrom`, not this list |
| `requiresShore` | bool | `T1*` / "+ shore" | present & `true` for shipyards, coastal batteries, offshore platforms |

`stats.hp` is a provisional object (qualitative HP tier from the table → a placeholder number), same rule as units. Optional flags (`garrisonable`) where canon states them.

## Rules

- kebab-case ids; unit ids globally unique; building ids `<faction>-<name>` (see above).
- Numbers authored as decimals, converted to Fix64 at load. Authoring-only `FromDouble` is allowed (ADR 0003); never a runtime float in `sim/`.
- Identity fields come from the faction file — if you change one here, change it there in the same PR (they must not drift).
- Add or rename a field → bump `schemaVersion` and update the loader (when it exists) in the same PR.
- A `stats`/`weapon` change is a balance change → needs a balance scenario run once the combat sim exists (`tools/scenario`).

Change impact: `../map/effects/CONTEXT.md` — unit stats (balance), the counter matrix (armor/`damageType`), economy (cost/`buildTimeSeconds`), owned verbs (`verbs`).
