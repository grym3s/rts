# factions — the working faction definitions (design canon)

Each faction is fully identified here: **its buildings, its units, what each building produces, and the timings** (construction time per building, production time per unit). This is design canon — the source of truth from which `content/*.json` is authored and against which the sim is built. The md's describe the whole faction; `content/` carries the machine-readable subset the sim actually loads.

| File | Faction | Axis | Owned verb |
|---|---|---|---|
| `coalition.md` | **Coalition** | Precision & information | **Marking** — spotted units take bonus damage |
| `hegemony.md` | **Hegemony** | Mass & attrition | **Conscription** — infantry sacrificed for resources/effects |
| `ascendant.md` | **Ascendant** | Exotic tech & mobility | **Phase** — units briefly untargetable |
| `capability-coverage.md` | — | the job-by-job matrix proving every faction answers every strategy | — |
| `counter-matrix.md` | — | the armor × damage-type table every unit's `Armor / Attack` pair resolves against | — |
| `owned-verbs.md` | — | buildable rules for Marking / Conscription / Phase — radius, duration, cooldown, magnitude, power-budget tax | — |

Identity, roster grid and the naval design live in `../rts-unit-roster-design-reference.md` (genre *why*, Parts 5–7; ADR 0005 overrides its holes). The **counter rules are now project canon in `counter-matrix.md`** — that is what the `Armor / Attack` columns in each faction file resolve against. These files do **not** restate the reference's reasoning — they instantiate it into concrete build orders and timings. Read the reference for *why*; read these for *what*.

## Status of the numbers

**Everything numeric here is v0 first-pass**, derived from the reference's anchors (range ladder in Part 6.1, MBT-as-baseline in Part 4, 4–6 min rig payback in Part 6.7) plus genre convention. Combat balance (exact per-unit HP/damage) is deliberately **not fixed here** — the reference is explicit that you build the counter matrix as a spreadsheet and tune the MBT mirror first (Part 8). What *is* fixed here: the building→unit production graph, tier gating, prerequisites, costs, and times. Tune numbers via `tools/scenario`; when a number here changes, the balance scenario that covers it must be re-run.

## The shared spine (all three factions have equivalents)

Factions differ by *profile and distinctive answers* — never by lacking an answer — and not by having entirely different building sets (reference Part 5). Every faction names these differently and tunes them per its axis, but the graph is shared:

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

## Answers, not holes

**Every faction can answer every strategy in the game.** No faction lacks a way to detect stealth, kill air, kill armor, kill infantry, break a turtle, reposition across the map, capture an objective, or threaten a base. A missing *answer* is a broken matchup — a coin flip decided at the faction-select screen — which is what makes hard rock-paper-scissors pointless to play. This rule **supersedes** the hole-heavy roster in `../rts-unit-roster-design-reference.md` Part 7; the decision is recorded in ADR `../../decisions/0005-answers-not-holes.md`.

Evidence: the best-balanced asymmetric RTS give every faction every essential capability and differ only in *how* — StarCraft's races all detect, all answer air, all cloak, and its matchups tune to ~even. Removing capabilities to balance (Company of Heroes' faction-specific missing mortars/MG teams) is a documented anti-pattern that *reduces* tactical diversity.

Factions differ in **how well** and **how** they answer each job, three ways:

- **Rating** — strong / standard / weak at a job, never absent. Weak means *worse or clumsier*, never *can't*. The same role carries a different profile per faction: e.g. basic infantry is the dirt-cheap spammable **Conscript** (Hegemony), the premium marks-on-hit **Rifleman** (Coalition), or the expensive phasing **Adept** (Ascendant) — one job, three cost/quality profiles.
- **Distinctive answers** — the tool can be a unique faction mechanic (Hegemony's **Tunnel Network**, the Ascendant's **Teleport Network**, Coalition's **Marking**). These are *encouraged* — they are the good kind of asymmetry.
- **Additive verbs** — one owned verb per faction (Marking / Conscription / Phase). Identity comes from *adding* and *re-profiling*, never from *subtracting* an answer.

**The one hard constraint on every distinctive answer and owned verb: it must be deniable.** GLA Tunnels and Zerg Creep are fair because the opponent can contest them; Marking dies with its sensors, the Teleport Network with its nodes, Phase on its cooldown. An answer the opponent *cannot* interact with is the same failure as a hole, seen from the other side.

*Options may still differ.* A pure **option** that is not an answer to any strategy — a ground superheavy, a specific superweapon — may exist on one faction and not another, as long as its absence removes no answer (Coalition has no ground superheavy but still has a top-end finisher via air/naval).

The full job-by-job coverage matrix — proving no empty cells — is `capability-coverage.md`. Each faction file has a **"Where it's strong and weak (never absent)"** section.

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

Each file has: **one-line pitch → global modifier → economy → building table → unit tables (by domain) → capability profile → build orders → where it's strong and weak**. The building table's "Produces" column is the production graph. The unit tables give tier, built-from, production time, cost, and counter-class; exact combat HP/damage is left to the balance pass (reference Part 8).

## Scope note

The **vertical slice** (`../game-design-pillars.md`) needs only T1 ground: workers, basic infantry, scout, MBT, and the buildings that make them. Air, naval, superweapons, and the owned faction verbs are documented here in full so the faction is *identified*, but they are **post-slice** — build the ground core first.

## Change impact

Faction data feeds `content/units/` and (when they land) `content/buildings/`, `content/scenarios/`. See `../../content/CONTEXT.md` and `../../map/effects/CONTEXT.md`. A new building/unit or a timing change → update the faction file **and** the content JSON in the same PR.
