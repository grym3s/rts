# Owned-verb rules — Marking / Conscription / Phase (design canon)

One owned verb per faction is the strongest identity tool we use (ADR 0005: identity by *adding* a mechanic, never by *subtracting* an answer). The faction files describe each verb; this file makes them **buildable** — exact trigger, magnitude, radius, duration, cooldown, the power-budget tax each one pays, its deniability, and where it hooks into the tick order.

## How to read

- **Structural knobs are fixed here** (radius, duration, cooldown, refund %) — these are the buildable shape. **Raw magnitudes** (how much bonus damage, how much HP a repair restores, a blast's base damage) are the **balance pass**, expressed relative to the counter matrix, not pinned here — same split as `counter-matrix.md`.
- **Units** are Fix64 world units; **times** are seconds with the tick count alongside (sim runs at 20 ticks/s, `SimWorld.TicksPerSecond`).
- **Every verb pays a power budget** (reference Part 5, "cool ability = reduced stats") — the tax is written next to the verb. The most common indie-RTS failure is a cool ability *on top of* comparable stats.
- **Every verb is deniable** (ADR 0005 hard constraint) — the opponent can contest it. The counter is written next to the verb; an answer the opponent cannot interact with is a hole seen from the other side.
- **All three are post-slice** (`CONTEXT.md`, *Scope note*). The ground slice ships without owned verbs; this file specifies them so the owned-verb systems are buildable when they land.

---

## Marking — Coalition

*A unit spotted by a Coalition sensor takes bonus damage from all Coalition sources until it breaks contact.* The payoff for winning the information war is damage, not just vision — which is why the whole faction is built around sensors, and why killing the enemy's counter-recon is the Coalition's core skill.

| Knob | Value |
|---|---|
| **Type** | persistent enemy debuff (not an activated ability — no cooldown) |
| **Magnitude** | marked target takes **+25% damage** from all Coalition sources, applied *after* the counter-matrix cell (`final = base × matrix_cell × 1.25`) — *v0, the single most balance-sensitive Coalition number* |
| **Stacking** | **binary** — a target is marked or not, regardless of how many sensors see it. Does not stack |
| **Aura sources / radius** | Comms Rig **8**, Spotter Drone **7**, Sensor Command (static) **14** — mark every enemy within radius **and** line of sight |
| **On-hit sources** | Rifleman ("marks on hit"), Precision Turret — mark the struck target, no radius |
| **Duration** | aura: while the source holds LOS **+ 1.5s (30t) linger** after LOS breaks. On-hit: **2s (40t)** from the last hit, no LOS needed |
| **Gate** | the aura **network** unlocks with **Sensor Command** (T2). Rifleman on-hit mark is T1 but single-target and short — the early, weak version |
| **Power-budget tax** | paid at the faction level (Coalition ×1.15 cost, −10% HP) *and* in unit design: the pure mark-anchors (Comms Rig, Sensor Command) carry **no offensive weight of their own** — Light armor, no weapon. You spend a slot on seeing, not shooting |
| **Deniability** | kill the sensor (Comms Rig / drone / Marksman) or **break line of sight** (terrain, stealth) → mark drops after the linger. Counter-recon is the whole counterplay; a faction with concealment (Phase/cloak, Tunnel Network) denies the spotting outright |

**Sim state & tick order.** One int per enemy unit, `MarkedUntilTick`. Marking must resolve **before** the damage sub-step even though sensing is conceptually a visibility concern (visibility is step 5 in `../../sim/CONTEXT.md`, *after* combat) — so the combat step runs **acquire → refresh marks → fire → damage → death**, and the refresh reads this tick's post-movement positions (movement is step 3, already done). Each mark-source with LOS sets `MarkedUntilTick = tick + lingerTicks`; the damage sub-step applies ×1.25 when `tick ≤ MarkedUntilTick`. Integer-only, deterministic. *This is a refinement the combat issue must honor — note it in `sim/combat/CONTEXT.md` when that folder is created.*

**Interacts with Intel** (the Coalition economy meter) — fuelling and gating the network. The Intel spec lives in the economy doc (`economy.md`, when written); this file owns the combat effect only.

---

## Conscription — Hegemony

*Any Hegemony infantry can be sacrificed — for an instant credit refund or to fuel an effect.* Cheap infantry become a **currency**, not just a body. A Hegemony that is losing bodies anyway converts that loss into tempo; the limiter is that you are spending a real unit — the verb has no cooldown because the unit *is* the cost.

| Knob | Value |
|---|---|
| **Type** | activated command on friendly Hegemony infantry (`ConscriptCommand`), instant, **no cooldown** |
| **Refund — at a Conscription Hall** (or within its radius) | **50%** of the unit's cost, instant (Conscript 75 → +37 credits) |
| **Refund — in the field** (anywhere) | **30%** of cost |
| **Why < 100%** | it is *sunk-cost recovery*, not a money printer — you never profit, so infantry can't be a savings account; you recoup part of a loss you were taking anyway. Instant delivery (no harvester trip) is the tempo value |
| **Hall service radius** | **10** (the full-refund zone) |
| **Fuel effects** (the "or power an effect" path — same body→charge mechanic) | **Sapper self-destruct**: conscripting a Sapper detonates it — Explosive burst, radius **3**, base damage a balance-pass magnitude resolved through the matrix (Explosive → strong vs Infantry/Structure). Others (emergency structure repair, defense overcharge) are the same rule — **1 body → a fixed charge** — specified as those units land |
| **Power-budget tax** | paid at the faction level: Hegemony ×0.8 cost and **slow**. The infantry are cheap *because* they are fodder; the sub-100% refund is itself the budget. No per-unit stat bonus |
| **Deniability** | the opponent **sees the bodies vanish and the tempo swing**, and the army Conscription funds is slow — artillery outranges the blob it builds. And you **cannot cash out a corpse**: burst-killing Hegemony infantry (Explosive/Small arms → their Infantry armor, 1.5) denies the refund. Kill them faster than they can convert |

**Sim state & tick order.** `ConscriptCommand(units[], mode)` resolves in step 1 (apply commands): for each valid friendly infantry, despawn the unit and credit `refund = floor(cost × pct)` to the faction, **or** trigger its conscription-effect. A Sapper detonation queues a damage event at the unit's position for the combat step (step 4), so the blast goes through the same matrix/damage path as any other Explosive. All integer, deterministic. Validate: refund can never exceed cost; a unit already dead this tick cannot be conscripted.

---

## Phase — Ascendant

*Ascendant units briefly enter an untargetable state* — to dodge a volley, cross a kill-zone, or escape a lost fight. Combined with the faction's speed, Phase means the Ascendant chooses every engagement. It is an escape/timing tool, **not a shield**: short, on a long cooldown, and it costs the unit its own fire while active.

| Knob | Value |
|---|---|
| **Type** | activated command on Phase-capable units (`PhaseCommand`), manual |
| **Which units** | only those the faction file marks *phases* — Adept, Glaive, Wraith, Seer, Oracle, Phase Turret. Not the whole roster |
| **While phased** | **untargetable** (cannot be acquired, incoming attacks miss/expire) and **cannot fire** — but **moves at normal speed** (crossing the kill-zone is the point) and still collides with terrain/bodies |
| **Duration** | **2s (40t)** — eats one volley or a short crossing, not a standing shield |
| **Cooldown** | **10s (200t)**, measured from when Phase *ends* → ~17% uptime (2 on / 10 off) |
| **Action-cost** | no attacking while phased — phasing is a DPS sacrifice in the moment; a phased army deals no damage |
| **Power-budget tax** | **~15% less effective HP or DPS** than the equivalent non-Phase slot at the same cost (reference Part 5; ascendant.md), *on top of* the faction global −20% HP. Phase units are genuinely glass — the Glaive is a fast, phasing MBT that loses any straight trade. Write the budget per unit before writing the unit |
| **Deniability** | **cooldown** (bait the phase, then commit into the 10s window) · **trap** (it must reappear somewhere — time AoE / mines for the reappearance) · **it can't fire while phased**, so out-lasting it costs it all its damage · and it's fragile once caught out of phase. *Phase is untargetability, not concealment* — the detection-countered Ascendant mechanic is the Mindbender's area **cloak**, a separate verb |

**Sim state & tick order.** Per-unit `PhaseState { activeUntilTick, cooldownUntilTick }`. `PhaseCommand` (step 1) sets `activeUntilTick = tick + 40` only if `tick ≥ cooldownUntilTick`; on the tick Phase ends, set `cooldownUntilTick = tick + 200`. The combat acquire sub-step (step 4) **filters out** any unit with `tick < activeUntilTick` (untargetable); the movement step (step 3) ignores Phase entirely. Integer-only, deterministic. Edge cases: a phased unit issued an attack order queues it but does not fire until Phase ends; Phase cannot be re-triggered inside its own duration or cooldown.

---

## What to tune first (balance sensitivity)

These three magnitudes move whole matchups; tune them right after the MBT mirror, one at a time, via `../../tools/scenario`:

1. **Marking +25%** — amplifies the *entire* Coalition army's output whenever its sensors win. Too high and information beats everything; too low and the faction's whole premise is unpaid. Tune against a mirror where one side has a live Comms Rig and the other has killed it.
2. **Conscription refund 50% / 30%** — the credit floor under Hegemony spam. Too high and infantry becomes free economy; too low and the verb is never worth using.
3. **Phase 2s / 10s + the −15% stat tax** — uptime and fragility together decide whether the Ascendant "chooses every fight" fairly or oppressively. Tune duration/cooldown and the stat tax as one budget.

## Encoding hook & change impact

Owned verbs are **sim behaviour**, not just content — each needs a system (Marking in combat; Conscription in orders/economy; Phase in orders + combat targeting) plus per-unit flags in `content/` (`marks`, `conscriptable`, `phases`, and the per-unit Phase stat tax). Build each with its faction and a balance scenario. Change impact: these touch combat and economy balance and every golden replay once live — see `../../map/effects/CONTEXT.md` (owned-verbs row). When a knob changes, re-run the matchup scenario that covers it.
