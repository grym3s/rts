# factions — the working faction definitions (design canon)

Each faction is fully identified here: **its buildings, its units, what each building produces, and the timings** (construction time per building, production time per unit). This is design canon — the source of truth from which `content/*.json` is authored and against which the sim is built. The md's describe the whole faction; `content/` carries the machine-readable subset the sim actually loads.

| File | Faction | Axis | Owned verb |
|---|---|---|---|
| `coalition.md` | **Coalition** | Precision & information | **Marking** — spotted units take bonus damage |
| `hegemony.md` | **Hegemony** | Mass & attrition | **Conscription** — infantry sacrificed for resources/effects |
| `ascendant.md` | **Ascendant** | Exotic tech & mobility | **Phase** — units briefly untargetable |
| `holes-and-matchups.md` | — | per-faction hole audit + the intended non-transitive matchup triangle | — |

The rule that detection/stealth/sniper/carrier are never holes is recorded as ADR `../../decisions/0005-universal-capabilities-and-permitted-holes.md`.

Identity, roster grid, counter rules and the naval design all live in `../rts-unit-roster-design-reference.md` (Parts 4–7). These files do **not** restate that reasoning — they instantiate it into concrete build orders and timings. Read the reference for *why*; read these for *what*.

## Status of the numbers

**Everything numeric here is v0 first-pass**, derived from the reference's anchors (range ladder in Part 6.1, MBT-as-baseline in Part 4, 4–6 min rig payback in Part 6.7) plus genre convention. Combat balance (exact per-unit HP/damage) is deliberately **not fixed here** — the reference is explicit that you build the counter matrix as a spreadsheet and tune the MBT mirror first (Part 8). What *is* fixed here: the building→unit production graph, tier gating, prerequisites, costs, and times. Tune numbers via `tools/scenario`; when a number here changes, the balance scenario that covers it must be re-run.

## The shared spine (all three factions have equivalents)

Factions differ by *profile and holes*, not by having entirely different building sets (reference Part 5). Every faction names these differently and tunes them per its axis, but the graph is shared:

| # | Structure (generic) | Tier | Produces / unlocks |
|---|---|---|---|
| 1 | **Construction Yard** (from MCV deploy) | T1 | all structures; the base's build radius |
| 2 | **Power Plant** | T1 | power (structures go offline when power is negative) |
| 3 | **Refinery** | T1 | processes credits; ships with one Harvester |
| 4 | **Barracks** | T1 | infantry |
| 5 | **Vehicle Bay** | T1 | ground vehicles |
| 6 | **Tech Center I** | T2 gate | unlocks T2 units + the Airfield |
| 7 | **Airfield / Helipad** | T2 | aircraft |
| 8 | **Tech Center II** | T3 gate | unlocks T3 units + the Superweapon |
| 9 | **Superweapon** | T3 | the faction finisher power |
| — | **Defenses** (basic / AA / advanced) | T1–T2 | static defense |
| — | **Shipyard / Coastal Battery / Sea Platform** | T1–T3 | naval (post-slice; see reference Part 6) |

## Universal capabilities (no faction may lack these)

Four capabilities are **counterplay-critical** — remove one and a matchup breaks rather than tilts. **Every faction must field its own version of each, differentiated by profile and delivery, never absent:**

| Capability | Why it can't be a hole | How factions differ |
|---|---|---|
| **Detection** | A faction that can't reveal stealth *auto-loses* to a single cloaked unit (reference Part 5.4). | Coalition: mobile network (drones + marking). Hegemony: static/positional (fortified detector). Ascendant: phasing recon. |
| **Stealth** | If only some factions can hide, the ones that can't are permanently on the back foot in the info game — a whole strategic layer they can't play. | Coalition: individual stealth units. Hegemony: mass concealment (tunnels). Ascendant: phase + area cloak. |
| **Sniper** | The long-range infantry-killer / detector is the answer to garrisons and mass infantry; lacking it leaves a faction helpless to a spam it can't approach. | Coalition: mobile assassin. Hegemony: dug-in anti-materiel team. Ascendant: phasing energy marksman. |
| **Carrier-equivalent** | Mobile air infrastructure gates air projection on water maps; no version means naval maps are a coin flip. | Coalition: range-extending station. Hegemony: armored brute (no range bonus). Ascendant: drone-spawning, self-rearming. |

Holes remain the strongest identity tool (reference Part 5.4) — but they go in slots where absence is a *strategic* weakness with counterplay: superheavy, transport, interceptor, heavy infantry, true submarine. **Not** in the four above. Each faction file has a **"The four universal capabilities"** subsection making its version of each explicit.

## Tiers

| Tier | Gate | Contains |
|---|---|---|
| **T1** | Barracks / Vehicle Bay | worker, engineer, basic infantry, scout vehicle, MBT |
| **T2** | Tech Center I | AT/AA specialists, APC, mobile AA, gunship, transport, first specialist |
| **T3** | Tech Center II | artillery, superheavy, bomber, elite specialists, superweapon |

## Timing ladders (base values, before faction modifiers)

Sim runs at **20 ticks/second** (`SimWorld.TicksPerSecond`). Times below are seconds; multiply by 20 for ticks. Each faction applies a global modifier (stated at the top of its file) on top of these bases.

**Building construction (seconds):**

| Structure | Base | | Structure | Base |
|---|---|---|---|---|
| Construction Yard (deploy) | 10 | | Airfield / Helipad | 40 |
| Power Plant | 20 | | Tech Center II | 75 |
| Refinery (+Harvester) | 30 | | Superweapon | 120 |
| Barracks | 20 | | Basic defense | 15 |
| Vehicle Bay | 35 | | AA defense | 20 |
| Tech Center I | 50 | | Advanced defense | 30 |
| Shipyard | 45 | | Coastal Battery | 40 |
| Sea Platform | 25 | | | |

**Unit production (seconds):**

| Unit class | Base | | Unit class | Base |
|---|---|---|---|---|
| Basic infantry | 6 | | Support vehicle | 22 |
| AT / AA infantry | 9 | | Superheavy | 55 |
| Heavy / AoE infantry | 11 | | Scout drone | 15 |
| Sniper / specialist | 16 | | Gunship | 28 |
| Worker / harvester | 12 | | Interceptor | 24 |
| Engineer | 14 | | Bomber | 40 |
| Commando (pop-capped) | 45 | | Air transport | 22 |
| Scout vehicle | 12 | | Corvette | 20 |
| MBT | 20 | | Destroyer | 40 |
| Light AT | 16 | | Submarine | 30 |
| APC | 18 | | AA cruiser | 38 |
| Mobile AA | 16 | | Bombardment ship | 50 |
| Artillery | 30 | | Carrier | 70 |
| | | | Sea transport | 30 |

## How to read a faction file

Each file has: **one-line pitch → global modifier → economy → building table → unit tables (by domain) → build orders → holes & compensation**. The building table's "Produces" column is the production graph. The unit tables give tier, built-from, production time, cost, and counter-class; exact combat HP/damage is left to the balance pass (reference Part 8).

## Scope note

The **vertical slice** (`../game-design-pillars.md`) needs only T1 ground: workers, basic infantry, scout, MBT, and the buildings that make them. Air, naval, superweapons, and the owned faction verbs are documented here in full so the faction is *identified*, but they are **post-slice** — build the ground core first.

## Change impact

Faction data feeds `content/units/` and (when they land) `content/buildings/`, `content/scenarios/`. See `../../content/CONTEXT.md` and `../../map/effects/CONTEXT.md`. A new building/unit or a timing change → update the faction file **and** the content JSON in the same PR.
