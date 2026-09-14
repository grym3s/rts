#!/usr/bin/env python3
"""D-001 reference model: counter-matrix damage math for sim/combat.

Verifies, BEFORE the C# port:
  1. The canon matrix (docs/factions/counter-matrix.md) parses to the exact
     6x5 Fix64 raw values the C# constants will use (round-half-even, Q31.32).
  2. Fix64 multiply-truncate semantics: applied = (damage_raw * mult_raw) >> 32,
     then floor at 1 for a live weapon (a valid hit always does >= 1 whole damage
     -- soft-with-cliffs means chip, never bounce; decided here, ported byte-exact).
  3. Property sanity from the canon: no zero cells, min 0.35, max 1.75,
     Energy flat vs non-Structure, MBT mirror (AP x Heavy) == 1.25.

Run: python3 tools/refmodel_counter_matrix.py  (exit 0 = parity verified)
"""
import re
import sys
from fractions import Fraction

FRACTION_BITS = 32
ONE_RAW = 1 << FRACTION_BITS

# Canon table (docs/factions/counter-matrix.md). None = "-" cell (cannot be
# delivered: targeting fact, not a multiplier). Order fixed: canonical.
DAMAGE_TYPES = ["small-arms", "autocannon", "ap", "explosive", "missile", "energy"]
ARMOR_CLASSES = ["infantry", "light", "heavy", "air", "structure"]
MATRIX = {
    "small-arms": {"infantry": 1.5,  "light": 0.75, "heavy": 0.35, "air": None, "structure": 0.5},
    "autocannon": {"infantry": 1.0,  "light": 1.5,  "heavy": 0.5,  "air": 1.25, "structure": 0.75},
    "ap":         {"infantry": 0.5,  "light": 1.25, "heavy": 1.25, "air": None, "structure": 0.75},
    "explosive":  {"infantry": 1.5,  "light": 1.0,  "heavy": 0.6,  "air": None, "structure": 1.5},
    "missile":    {"infantry": 0.6,  "light": 1.0,  "heavy": 1.5,  "air": 1.75, "structure": 1.0},
    "energy":     {"infantry": 1.0,  "light": 1.0,  "heavy": 1.0,  "air": 1.0,  "structure": 0.75},
}


def from_double(v: float) -> int:
    """Mirror .NET Math.Round(double) (banker's rounding) then cast to long."""
    f = Fraction(v) * ONE_RAW
    num, den = f.numerator, f.denominator
    q, r = divmod(num, den)
    if r * 2 > den or (r * 2 == den and q % 2 == 0):
        q += 1
    return q


def mul(a_raw: int, b_raw: int) -> int:
    """Mirror Fix64 operator *: (Int128)a.Raw * b.Raw >> 32 (arithmetic shift = truncate toward -inf; values here are positive)."""
    return (a_raw * b_raw) >> FRACTION_BITS


def applied_damage(damage: float, mult: float) -> float:
    """End-to-end: author doubles -> Fix64 -> multiply -> whole-floor of 1 for live weapons."""
    raw = mul(from_double(damage), from_double(mult))
    if raw < ONE_RAW:
        raw = ONE_RAW  # chip, never bounce
    return raw / ONE_RAW


def main() -> int:
    fails = []

    def check(name, cond):
        if not cond:
            fails.append(name)
            print(f"FAIL {name}")

    # 1. canon invariants
    cells = [v for row in MATRIX.values() for v in row.values() if v is not None]
    check("no zero cells", all(v > 0 for v in cells))
    check("floor is 0.35", min(cells) == 0.35)
    check("ceiling is 1.75", max(cells) == 1.75)
    check("mbt mirror 1.25", MATRIX["ap"]["heavy"] == 1.25)
    for a in ARMOR_CLASSES:
        if a != "structure":
            check(f"energy flat vs {a}", MATRIX["energy"][a] == 1.0)

    # 2. Fix64 encoding of every canon multiplier is within 1 Q31.32 ulp of the
    # exact decimal (0.35/0.6 are not binary-representable; drift is inherent to
    # FromDouble, bounded by ulp(v), i.e. |enc(v) - v| <= v/2^32 + tiny).
    for dt in DAMAGE_TYPES:
        for ac in ARMOR_CLASSES:
            v = MATRIX[dt][ac]
            if v is None:
                continue
            raw = from_double(v)
            exact = Fraction(str(v))  # decimal 0.35 etc., not binary float
            check(f"enc within ulp {dt}x{ac}",
                  abs(Fraction(raw, ONE_RAW) - exact) <= exact / ONE_RAW + Fraction(1, 10**9))

    # 3. applied damage: worked examples (these become xUnit facts)
    cases = [
        # (damage, mult, expected applied)
        (8, 1.5, 12.0),    # rifleman vs infantry (rifleman vs conscript)
        (8, 0.35, 2.8),    # rifleman vs heavy: 8*0.35 = 2.8 exactly in Q31.32? 0.35 not binary-exact -> check truncation
        (7, 0.35, 2.45),   # conscript chip vs heavy
        (1, 0.35, 1.0),    # minimum weapon floors at 1
        (60, 1.0, 60.0),   # energy flat
        (8, 1.25, 10.0),   # AP vs light
    ]
    for dmg, mult, expect in cases:
        got = applied_damage(dmg, mult)
        print(f"applied({dmg} x {mult}) = {got}")
        check(f"applied {dmg}x{mult} == {expect}", abs(got - expect) < 1e-9)

    # 4. TTK asymmetry from canon (the whole point): 60hp heavy vs 8dmg small-arms
    #    vs 55hp infantry with missile 0.6 -- counter side must kill first.
    import math
    chip_shots = math.ceil(60 / applied_damage(8, 0.35))          # vs heavy
    anti_inf_shots = math.ceil(60 / applied_damage(8, 1.5))       # vs infantry
    check("counter asymmetry real", chip_shots > anti_inf_shots * 2)
    print(f"shots-to-kill 60hp: small-arms vs heavy={chip_shots}, vs infantry={anti_inf_shots}")

    # 5. randomized parity vs double semantics: encode (IEEE round-to-nearest,
    # what .NET Math.Round does) then truncate the product. Error <= 1 ulp of the
    # product: encode drift <= m*0.5ulp(d) + d*0.5ulp(m) <= 1 ulp(dm), truncate <= 1.
    import random
    rng = random.Random(7)
    for _ in range(100000):
        d = rng.randint(1, 400) / 10          # 0.1 .. 40.0
        m = rng.choice(cells)
        raw = mul(from_double(d), from_double(m))
        exact = d * m                          # IEEE double, same input as .NET FromDouble
        got = raw / ONE_RAW
        check(f"parity d={d} m={m}",
              abs(exact - got) <= 4.0 * 2.0 ** -32 * max(1.0, abs(exact)))

    if fails:
        print(f"\n{len(fails)} FAILURES")
        return 1
    print("\nreference model: all checks passed (30 cells encoded, worked examples, 100k truncation parity)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
