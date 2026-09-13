# Counter matrix — armor × damage-type (design canon)

The connective tissue for balance. Every unit in the faction files carries an **`Armor / Attack`** pair (`Heavy / AP`, `Infantry / Small arms`, `Light / Energy`…); this file is the table those pairs resolve against. It is the "counter matrix as a spreadsheet" the reference (`../rts-unit-roster-design-reference.md` Part 4, Part 8) tells you to build before the units — built here as project canon.

## How it's read

**Damage dealt = weapon base damage × the matrix cell for (its damage type, the target's armor class).** Base damage and HP are *not* fixed here — they come from the balance pass, tuning the MBT mirror first (`CONTEXT.md`, *Status of the numbers*). This file fixes the **shape** of every counter; tuning sets the magnitude.

## The dial: soft-with-cliffs (ADR 0005)

ADR `0005-answers-not-holes` targets ~even matchups with no capability gaps, which sets the hard↔soft dial (reference Part 4) toward **soft-with-cliffs**:

- **No true 0% against a valid target.** Everything a weapon can hit, it hurts — the lowest multiplier is 0.35, not 0. A basic rifle *chips* a tank; it does not bounce. This keeps a scout's or a stray squad's damage real and stops any single unit being the unconditional answer.
- **But the cliffs are pronounced** (0.35 ↔ 1.75, a 5× spread). Scouting, tech choice, and composition decide fights — bringing the right damage type is worth ~5× its damage. That is the counter system doing its job.
- Hard C&C-style 400%/25% swings are deliberately **not** used: they recreate the select-screen coin flip ADR 0005 exists to avoid.

`—` in a cell means the damage type **cannot be delivered** against that domain at all (no explosive shell tracks an aircraft). That is a targeting fact, not a 0% multiplier — see *Targeting vs. multiplier* below.

## Armor classes

| Class | Applies to | Profile |
|---|---|---|
| **Infantry** | all foot soldiers | soft to bullets and blasts; shrugs off anti-tank overkill |
| **Light** | scouts, buggies, APCs, harvesters, support vehicles, helicopters | soft to autocannon and AP; the "everything mildly hurts it" class |
| **Heavy** | MBTs, superheavies | resists bullets and blasts; killed by dedicated AT (AP, Missile) |
| **Air** | all aircraft | only autocannon / missile / energy reach it at all |
| **Structure** | buildings and static defense | only siege (Explosive) breaks it efficiently |

*(Naval armor — Light naval, Heavy naval, Submerged — is a post-slice extension; see below.)*

## Damage types

| Type | Owns (strong) | Soft against | Carried by (examples) |
|---|---|---|---|
| **Small arms** | Infantry | Heavy, Structure, Air (—) | Rifleman, Conscript |
| **Autocannon** | Light, Air | Heavy | Flak, Skimmer, gatling defenses |
| **AP** | Heavy, Light | Infantry, Air (—) | MBTs, Bulwark, Guard Turret |
| **Explosive** | Infantry, Structure | Heavy, Air (—) | artillery, grenadiers, bombers |
| **Missile** | Air, Heavy | Infantry | AT/AA infantry, SAM Rover, gunships |
| **Energy** | *nothing — flat* | Structure | Ascendant (Glaive, Adept, Rift) |

**Energy is the exotic flat profile (ADR 0005 identity, not a hole).** It ignores armor class — never a bonus, never a penalty (except weak siege) — so the Ascendant's damage is *reliable* where everyone else's is *situational*. It pays for that reliability in HP and cost (the Ascendant global −20% HP, ×1.2 cost) and in never landing the 1.5–1.75 spike a specialist gets. This is why the Disruptor "drains vehicles" and the Anchor "shields" rather than out-damaging: the Ascendant's edge is utility and mobility, not the multiplier.

## The matrix

Rows = damage type, columns = target armor class. Cell = damage multiplier.

| Damage \ Armor | Infantry | Light | Heavy | Air | Structure |
|---|---|---|---|---|---|
| **Small arms** | **1.5** | 0.75 | 0.35 | — | 0.5 |
| **Autocannon** | 1.0 | **1.5** | 0.5 | **1.25** | 0.75 |
| **AP** | 0.5 | **1.25** | **1.25** | — | 0.75 |
| **Explosive** | **1.5** | 1.0 | 0.6 | — | **1.5** |
| **Missile** | 0.6 | 1.0 | **1.5** | **1.75** | 1.0 |
| **Energy** | 1.0 | 1.0 | 1.0 | **1.0** | 0.75 |

Read a column to see how to kill that armor; read a row to see what a weapon is for. The **MBT mirror** (Heavy armor, AP damage) is 1.25 both ways — symmetric, so the mirror stays even while MBT-on-MBT fights resolve fast; the counters live outside the mirror (AT infantry's Missile 1.5 into Heavy, which the MBT's AP answers at only 0.5 into their Infantry armor).

## Targeting vs. multiplier

Two independent facts decide whether unit A can hurt unit B:

1. **Can A's weapon target B's domain?** A per-weapon flag: *targets ground / air / sea*. A Rifleman is ground-only even though nothing in the matrix forbids small arms from grazing a low helicopter — the unit simply has no anti-air fire control. This is where "who can shoot down aircraft" is really decided.
2. **If it lands, how much?** The matrix cell.

The `—` cells encode (1) at the damage-type level: Small arms / AP / Explosive have **no anti-air profile at all** — a tank shell and an artillery shell cannot engage aircraft, so no unit carrying them answers air regardless of its targeting flags. Anti-air therefore always means an **Autocannon, Missile, or Energy** weapon (Flak = Autocannon, Lancer/SAM = Missile, Glaive/Skyward = Energy/Autocannon), which is exactly the AA answer each faction fields.

## The two golden rules, checked against the matrix

Both reference-Part-4 rules must hold for every unit; the matrix is what makes them checkable.

1. **Every unit is hard-countered by something cheaper.** Heavy armor (the expensive MBT/superheavy) is countered by AT infantry (cheap, Missile → Heavy 1.5) and the MBT can barely touch them back (AP → Infantry 0.5). The Colossus superheavy dies to massed AT the same way. A unit whose column has no ≥1.25 counter available below its own cost is a balance bug.
2. **Every unit has a job no faction-mate does better.** The matrix separates the anti-X roles cleanly: Small arms owns anti-infantry (1.5) while Autocannon stays neutral there (1.0) and owns anti-light (1.5) instead; Explosive owns siege (Structure 1.5) while AP does not (0.75). Two units with the same damage type *and* the same targeting set overlap — cut or redesign one.

## Owned-verb hook — Marking

**Marking** (Coalition's owned verb) stacks **on top** of this matrix: a marked target takes a flat bonus multiplier from *all* Coalition sources, applied after the armor/damage cell. Precise magnitude, radius, and duration are specified in the owned-verb rules (`owned-verbs.md`, when written). The matrix is the base; Marking is a modifier on the base, and it is deniable (it stops when the target breaks the sensor's line of sight).

## Post-slice extension — naval

Naval adds three armor classes and two damage types (reference Part 6.6). Shown for completeness; **not yet tuning canon** — build it with the naval slice.

| Damage \ Armor | Light naval | Heavy naval | Submerged |
|---|---|---|---|
| **Autocannon** | 1.25 | 0.5 | — |
| **AP / Naval gun** | 1.25 | 1.25 | — |
| **Missile** | 1.0 | 1.5 | — |
| **Torpedo** | 1.25 | 1.5 | **1.5** |
| **Energy** | 1.0 | 1.0 | 1.0 |

Submerged is reachable only by **Torpedo** (and Energy) — detection is what lets you *target* it at all (reference 6.3: "detection is a unit, not an upgrade"). Surface guns fire on subs only when a detector reveals them, i.e. the `—` cells are targeting, not multiplier.

## Content encoding (future schema bump)

`content/units/*.json` today carries a flat `damage` (`content/CONTEXT.md`). Adopting this matrix is a schema change: add `armor` (one armor class) and `damageType` (one damage type) per unit, bump `schemaVersion`, and update the loader + the combat system to look up the cell — all in one PR, with a balance scenario. Until then this file is design canon the sim does not yet read.

## Expose it in-game (non-negotiable, reference Part 4)

Whatever these numbers end up at after tuning, the matrix ships **visible to the player** — a readable armor/damage table in-game. Hidden counter matrices are the single most reliable source of player complaint. The debug overlay (game issue) is the first place it appears.

## Change impact

Changing any cell re-tunes **all** combat balance and every golden-replay hash once combat reads it — the widest-blast-radius change in the game short of `Fix64`. See `../../map/effects/CONTEXT.md` (counter-matrix row). When a cell changes: re-run every balance scenario, starting from the MBT mirror.
