#!/usr/bin/env python3
"""D-001 reference model: harvest-loop integer semantics for sim/economy.

Integer credits throughout (no fixed-point needed: every canon quantity here is a
whole credit). Verifies BEFORE the C# port:
  1. canon constants load-to-fill = 700 / 140t = exactly 5 cr/t, no rounding drift
  2. worked examples: accrual, full-load trip, deposit, pool depletion, spend
  3. 10k randomized cycles: conservation invariant (credits + loads + pools ==
     starting + deposits booked exactly once), pools never negative, loads never
     exceed capacity, harvesters bank-at-field when no refinery exists.

Semantics (mirror these byte-exact in C# EconomySystem):
  - tick step 2, store order; per harvester with state: Load, Carrying
  - Carrying and inside deposit radius of nearest refinery (lowest id tie) ->
    credits += Load; Load = 0; Carrying = False        (deposited THIS tick)
  - not Carrying, Load < Capacity, inside fill radius of a live vein (pool > 0,
    lowest id tie) -> take = min(RATE, Capacity - Load, pool); Load += take; pool -= take
    when Load == Capacity -> Carrying = True (movement itself is navigation's job;
    the scenario places units or issues the orders)
  - no refinery on the map -> filled load banks on the harvester (income not counted)
Run: python3 tools/refmodel_economy.py  (exit 0 = semantics verified)
"""
import random
import sys

CAPACITY = 700
FILL_TICKS = 140
RATE = CAPACITY // FILL_TICKS          # integer division is the port: 5
START_CREDITS = 5000
FIELD_POOL = 25_000

assert RATE * FILL_TICKS == CAPACITY, "canon requires exact division: 700/140 == 5"


class Harv:
    def __init__(self):
        self.load = 0
        self.carrying = False


def tick(harvs, veins, credits, refineries_present):
    """One economy tick. veins: list[int] pools; harvs all inside range in this model."""
    for h in harvs:
        if h.carrying:
            if refineries_present:
                credits[0] += h.load
                h.load = 0
                h.carrying = False
            continue
        if h.load >= CAPACITY:
            h.carrying = True
            continue
        for vi, pool in enumerate(veins):
            if pool <= 0:
                continue
            take = min(RATE, CAPACITY - h.load, pool)
            h.load += take
            veins[vi] -= take
            if h.load == CAPACITY:
                h.carrying = True
            break


def main() -> int:
    fails = []

    def check(name, cond):
        if not cond:
            fails.append(name)
            print(f"FAIL {name}")

    check("rate is exactly 5", RATE == 5)

    # worked: one harvester fills in exactly 140t
    h, veins, cr = Harv(), [FIELD_POOL], [START_CREDITS]
    for _ in range(139):
        tick([h], veins, cr, True)
    check("139t not full", h.load == 695)
    tick([h], veins, cr, True)
    check("140t full and carrying", h.load == 700 and h.carrying)
    tick([h], veins, cr, True)  # next tick deposits (travel abstracted)
    check("deposit 5700", cr[0] == 5700 and h.load == 0)

    # worked: depletion — 25,000 = 35 full loads (24,500) + 500 riding the harvester forever
    h, veins, cr = Harv(), [FIELD_POOL], [0]
    for _ in range(FILL_TICKS * 40):
        tick([h], veins, cr, True)
    check("35 loads deposited", cr[0] == FIELD_POOL - 500)
    check("last partial rides", h.load == 500 and not h.carrying)
    check("pool drained exactly", veins[0] == 0)
    print(f"depletion: banked {cr[0]}, riding {h.load}")

    # worked: no refinery -> bank at field, nothing counted, pool drains
    h, veins, cr = Harv(), [FIELD_POOL], [0]
    for _ in range(300):
        tick([h], veins, cr, False)
    check("banked on unit", h.load == CAPACITY and h.carrying and cr[0] == 0 and veins[0] == FIELD_POOL - 700)

    # randomized conservation: N harvesters, V veins, random despawns; model has no
    # travel, so every deposit must equal exactly one full load, taken once from pools.
    rng = random.Random(7)
    for trial in range(10000):
        n = rng.randint(1, 4)
        v = rng.randint(1, 3)
        pools = [rng.choice([700, 1400, 25000, 137]) for _ in range(v)]
        total_pool = sum(pools)
        harvs = [Harv() for _ in range(n)]
        credits = [0]
        lost = 0  # loads destroyed with despawned harvesters
        have_ref = True
        for t in range(rng.randint(1, 500) + 1):
            if rng.random() < 0.005:
                have_ref = not have_ref
            if harvs and rng.random() < 0.003:
                lost += harvs.pop(rng.randrange(len(harvs))).load
            tick(harvs, pools, credits, have_ref)
            for hh in harvs:
                check("load cap", hh.load <= CAPACITY)
            check("pools non-negative", all(p >= 0 for p in pools))
        drained = total_pool - sum(pools)
        # exact accounting: everything drained from pools is deposited, riding, or lost to despawn
        check("conservation exact", credits[0] + sum(hh.load for hh in harvs) + lost == drained)
        if fails:
            print(f"(trial {trial}, t={t})"); break

    if fails:
        print(f"\n{len(fails)} FAILURES")
        return 1
    print("\nreference model: all checks passed (exact rate, worked trips, depletion, bank-at-field, 10k conservation runs)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
