# Economy & tech tree (design canon)

Credits, power, the harvest loop, and the tier gates — the whole economic spine in one place. The faction files each carry a one-paragraph *Economy* section and their building tables carry cost/power/prereq columns; `CONTEXT.md` carries the shared building spine, the tier table, and the construction/production **timing ladders**. This file pins the numbers those depend on and does **not** restate the timing ladders — read them in `CONTEXT.md`.

## How to read

- **One currency: credits.** There is no second resource (reference `../rts-unit-roster-design-reference.md` Part 6.7). Intel (Coalition) and Conscription income (Hegemony) are **not** currencies — Intel is a combat-generated meter that *gates and fuels*, and Conscription *converts units into credits*. Keeping one currency gives the strategic flavour without dual-resource balance complexity.
- **Structural knobs are fixed here** (starting credits, capacities, power values, field pools, the low-power rule). **Effective rates** (credits/sec) are tuning targets — the balance pass sets them via `../../tools/scenario`, same split as `counter-matrix.md`.
- **Units** are Fix64 world units; **times** are seconds with ticks alongside (20 ticks/s).
- **Economy runs in tick-order step 2** (`../../sim/CONTEXT.md`, "production / economy") — harvest income, power recompute, and production progress all resolve there, after commands (1) and before movement (3).
- **Slice scope:** the ground slice needs credits + the harvest loop + power + the T1 gate. Intel, Conscription income, offshore oil, and superweapon charge are **post-slice** and marked.

## Starting state (skirmish)

| | Value |
|---|---|
| Starting credits | **5000** |
| Starting units | 1 **MCV** (undeployed → Construction Yard) |
| Starting power | **0** — the first Power Plant is the opening move |

5000 funds the standard opening (Power 400 → Refinery 1500 → Barracks 400 → Vehicle Bay 1500 = 3800) with a buffer, and no more — you cannot skip the refinery.

## The harvest loop (credits)

The primary income is the classic harvester round-trip: a harvester drives to a **resource field**, fills, returns to a **Refinery**, and deposits. The Refinery ships one harvester on completion.

| Knob | Value |
|---|---|
| **Resource field pool** | **25,000 credits**, finite — a field **depletes** and forces relocation/expansion |
| **Harvester capacity** (Collector) | **700** credits per load |
| **Fill time** at the field | **~7s (140t)** |
| **Round-trip travel** at nominal distance | **~7s (140t)** → ~14s/cycle |
| **Effective income** (nominal, *tuning target*) | **~50 credits/sec** per harvester |
| **Deposit** | at any friendly Refinery (build a second Refinery nearer a far field to shorten trips) |

**Depletion → expansion is the core macro loop.** A field is ~500 harvester-loads; when it runs dry, income stops until the harvester reaches a new field — so holding and defending fresh fields is the map game. Two levers make it a decision: field distance (near = safe but soon dry; far = rich but raidable) and the second Refinery (credits now vs. board presence).

**Per-faction harvester** (deltas, exact tuning deferred):

| Faction | Harvester | Delta |
|---|---|---|
| Coalition | **Collector** | baseline — 700 / ~14s cycle; Light armor, defenceless (raidable) |
| Hegemony | **Miner** | **Heavy armor** — hard to raid; slightly slower cycle (matches the slow faction) |
| Ascendant | **Cultivator** | **capacity 550, slower cycle** → lower early income, but its **field never depletes** (see below) — infinite late, never relocates |

## Power

Every structure either supplies or draws power. The building tables encode this as `+ / − / −− / −−−`; those symbols resolve to:

| Symbol | Power |
|---|---|
| `+` (Power Plant: Reactor / Furnace / Conduit) | **+100** supply |
| `−` (Refinery, Barracks, Vehicle Bay, most defenses) | **−20** |
| `−−` (Tech Centers, advanced defense, Airfield tech) | **−50** |
| `−−−` (Superweapon) | **−150** |

The Construction Yard / MCV is self-powered (0). **Net power = Σ supply − Σ draw**, recomputed each tick in step 2.

**Low-power rule (net < 0):** while net power is negative, **all production build times ×2** and **static defenses, detection/radar structures, and superweapon charging go offline** until net ≥ 0. Losing a Power Plant to a raid is therefore a real tempo and defensive hit, not just a number — the power grid is a legitimate target. (Moving units, harvesters, and already-built things are unaffected; only production speed and powered structures degrade.)

## The tech tree

The gate graph is shared across factions (`CONTEXT.md`, *The shared spine*); only the names differ. Prerequisites (each faction's building table has the exact chain):

```
MCV ─deploy→ Construction Yard ─┬─ Power Plant ──────────────── (powers everything)
                                ├─ Refinery (+harvester) ─────── credits
                                ├─ Barracks ──────────────────── T1 infantry
                                └─ Vehicle Bay ───────────────── T1 vehicles
                                        │
                                   Tech Center I  (T2 gate) ──── T2 units + Airfield/Helipad
                                        │
                                   Tech Center II (T3 gate) ──── T3 units + Superweapon
```

| Tier | Gate | Opens |
|---|---|---|
| **T1** | Barracks / Vehicle Bay | worker, engineer, basic infantry, scout, MBT — the slice arsenal |
| **T2** | Tech Center I (Sensor Command / Industrial Works / Nexus) | AT/AA specialists, APC, mobile AA, gunship, transport, first specialist, aircraft |
| **T3** | Tech Center II (Aerospace Command / War Academy / Ascendancy) | artillery, superheavy, bomber, elite specialists, superweapon |

Construction and production **times** are the ladders in `CONTEXT.md` (× each faction's global build/production modifier). This file does not repeat them.

## Per-faction economy deltas

The global cost/production/HP dials live at the top of each faction file; collected here with each faction's distinctive economic mechanic:

| Faction | Cost | Production | Build / structure HP | Distinctive mechanic |
|---|---|---|---|---|
| **Coalition** | ×1.15 | ×1.0 | ×1.0 | **Intel** meter (below) |
| **Hegemony** | ×0.8 | vehicles/air ×1.1, basic infantry fast | ×1.1 / HP +25% | **Conscription income** (`owned-verbs.md`) |
| **Ascendant** | ×1.2 | ×0.85 | ×0.7 / HP −25% | **never-depleting fields** (below) |

### Intel — Coalition (post-slice)

A secondary **meter**, not a currency. It fills as Coalition units **deal and take combat damage**, so a Coalition that never fights never reaches its late game.

| Knob | Value (v0) |
|---|---|
| Accrual | **+1 Intel per 10 points** of combat damage dealt *or* taken by Coalition units |
| **T3 gate** | Aerospace Command / T3 requires **500 Intel accumulated** (cumulative milestone, not spent) — you must have fought to tech up |
| **Marking upkeep** | the Sensor Command **Marking network** drains Intel while online; at 0 Intel the network goes **dormant** and marks stop refreshing (`owned-verbs.md` owns the per-mark combat effect; this is the network's fuel) |

This resolves the hook `owned-verbs.md` deferred: the per-mark effect (radius, +25%, linger) is combat; the *network's existence* is fuelled and gated here.

### Never-depleting fields — Ascendant

The Ascendant Wellspring's field (worked by the Cultivator) has **no finite pool** — it never depletes. The Ascendant income is *lower early* (smaller Cultivator load, slower cycle) and *infinite late*: it never has to relocate its economy, so a defended Ascendant base out-scales the other two on a long game. The trade is the slow start and the fragility (−20% HP, ×1.2 cost) that makes defending that static economy costly. Deniable like any economy — raid the Cultivators and the Wellspring; the field is permanent, the harvester and refinery are not.

### Conscription income — Hegemony

Fully specified in `owned-verbs.md` (50% refund at a Hall, 30% in the field). Economically it is a **sunk-cost recovery** income source, never a profit: it puts a credit floor under infantry spam without printing money, which is why the ×0.8-cost faction can trade bodies freely without going broke.

## Offshore economy — oil (post-slice, with naval)

When naval lands, offshore income makes the sea worth fighting over (reference Part 6.7–6.8). Summary, canon deferred to the naval slice:

- **Same currency** (credits), different acquisition: a **passive drip**, no harvester trip — safe from raiding but exposed to naval attack (harvester income is the reverse).
- **Buildable rigs on indestructible sites:** an engineer/construction unit builds a rig (~60–90s) on a neutral, always-visible oil site; the **rig is destructible, the site is not**, so contesting it is a renewable fight, not a one-time kill.
- **Payback 4–6 minutes** — the main dial (faster = no-brainer, slower = ignored).
- **Diminishing returns** on multiple rigs (100% / 70% / 50%) to cap the snowball; central rigs sit in exposed water and are contested, not held.

## Encoding hook & change impact

Economy is **sim behaviour + content**. It needs: a `content/buildings/*.json` schema (cost, power `+/−` value, prereq, build time), a resource-field / scenario schema (field pools, harvester start), and `startingCredits` in the scenario file (`content/CONTEXT.md` — bump `schemaVersion` with the loader in the same PR). The sim gains an economy system in step 2 (harvest income, power net, production progress) and, post-slice, the Intel meter and offshore drip. Change impact: economy numbers move build orders, timing, and every balance scenario — see `../../map/effects/CONTEXT.md` (economy row). When a knob changes, re-run the affected build-order/balance scenario.
