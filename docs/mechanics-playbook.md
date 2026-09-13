# Mechanics playbook — what we take, and what we refuse

Distilled from source-verified study of Command & Conquer (Tiberian Dawn through C&C3), Dust Front, and the 2026 RTS landscape. Reference material, not decisions — anything here that constrains `sim/` becomes an ADR before it is implemented.

Every entry follows the four questions from `design-direction.md`: inspiration → why it works → what we change → why ours is distinctive.

---

## The four to take first

### 1. Cost-derived build time — decide this before `sim/` grows

**Inspiration.** C&C derives build time from cost with one global coefficient: `time = Cost × BuildSpeedBias × TICKS_PER_MINUTE/1000`. RA1's `BuildSpeed=.8` means 1,000 credits is always 48 seconds.

**Why it works.** One dial tunes the entire game's tempo. Cost becomes intuitively legible *as time*, so a printed price teaches two things. And it is structurally impossible to ship a unit that is cost-efficient but tempo-broken.

**What we change.** Nothing structural — adopt it directly. But make the coefficient per-faction-capable from day one: C&C3 used build time itself as a faction trait ("build times on all Nod units are faster than their GDI counterparts"), which is asymmetry for free.

**Why it matters now.** This is an architectural choice, not a tuning knob. Retrofitting it after production code exists means rewriting every cost. **ADR candidate, before `sim/` gains a production system.**

### 2. One systemic resource besides money, and make it a cliff

**Inspiration.** C&C power. Below 100%: defences do not fire *at all*, radar goes dark, superweapon countdowns freeze, and buildings take chip damage. Prices are brutal — an Obelisk costs −150 power against a Power Plant's +100, so one turret costs one and a half plants to run.

**Why it works.** A *threshold* produces one unambiguous player state with one obvious remedy; a gradient produces unreadable mild degradation. It converts a cheap, correctly-targeted raid into a 10× outcome, giving attackers a high-leverage target that is not the enemy army. And it is the best anti-turtle mechanism in the series precisely because it preserves the *fantasy* of the impregnable base while making it structurally brittle in one specific, attackable, visible way.

**What we change.** Keep the cliff for binary systems; use C&C's own scaled variant (aircraft reload, floored at 50%) for anything genuinely continuous. Tie our industrial aesthetic to it — a browning-out base should *look* browned out.

### 3. Charge-and-drain batteries for big abilities

**Inspiration.** Tiberian Sun's Firestorm Defense: `RechargeTime=3`, `ChargeToDrainRatio=.333` — three minutes of charging buys exactly sixty seconds of barrier, toggleable at will, partial charges give proportionally shorter uptime, over-draining locks you out until full.

**Why it works.** It changes the decision from *"where do I aim this"* to **"when, and for how long, do I spend this"** — richer, more repeated, and more skill-expressive. It gives the ability a resource economy instead of a cooldown. And it is readable to the opponent, who can count the seconds.

**What we change.** Apply the pattern beyond defence — it generalises to any large effect. Pair it with pre-committed spatial counters (go under it, disable it, shoot out its unrepairable segments) so the answer is preparation, not reaction time.

### 4. Finite resources, so the map becomes the tech tree

**Inspiration.** Generals' Supply Docks: finite (~$30,000) and **invulnerable**, with a hidden throttle — only one gatherer unloads at a time, so the correct investment is a *second dock*, not a fourth harvester.

**Why it works.** Expansion stops being optional and becomes **scheduled** — both players know roughly when the other must move, producing timing windows with no timer UI. Invulnerability is the underrated half: resources can be contested but not griefed, so pressure stays positional. It also creates a genuine endgame phase change.

**What we change.** This is the fix for C&C's worst structural flaw, so take it wholesale. RA1's gems (double value, no regrowth) are the seed for a middle option if pure-finite proves too harsh.

---

## Also worth taking

**Charge production incrementally, and stall rather than fail.** C&C deducts per tick and, when funds run out, steps the build back one stage and keeps counting. Income and production become one continuous system; running low is a gradient you can feel and recover from, with no "insufficient funds" rejection wall.

**Income is a visible, vulnerable, spatial object.** The harvester makes economy a *map* problem, attackable without being deletable, and its lengthening trip is a free continuous "you need to expand" signal. It is also the reason mid-map fights happen at all in the first five minutes. **Keep the vulnerability, delete the chore** — see the refusals below.

**Hard counters, printed on the unit card.** C&C's matrix is five armour types × eight warheads. The critical detail: machine guns do **25%** to heavy armour and cannon does **30%** to infantry — *neither is zero*, and those two numbers are the entire combined-arms system. Never set a counter to 0% except for deliberate specialists. The historic cost of hard counters — punishing scouting failure — is a **UI problem, not a balance problem**: the matrix already lives in a data file, so render it.

**Spread as an independent axis.** Fire has spread 8, sniper rounds have spread 1. That encodes "good against clumps" without touching damage at all.

**Terrain movement coefficients as a quiet second counter layer.** Wheeled units move at 60% on clear ground and 40% on rough, tracked at 80/70, everything at 100% on road. Wheels become fast-on-roads and helpless off it — and **roads become genuine tactical infrastructure**.

**Telegraph power.** The Obelisk visibly and audibly charges before firing. Telegraphing is not a tax on power; it is what makes power *feel* powerful, and it creates the counterplay window.

**Warhead-specific death animations.** A soldier killed by machine gun, artillery, napalm and electricity dies four visibly different ways. **The weapon that killed something is readable from the corpse** — a diegetic combat log for the cost of a few animation sets. Pairs directly with our worldspace-state rule in `readability.md`.

**Feedback weight tracks emotional significance, not numeric significance.** A $300 building dies in a multi-stage collapse with debris and a screen-scale flash — two to four seconds of spectacle for a small event. That is correct.

**Announce state changes in three channels, and rate-limit them.** Every C&C EVA line maps to a state change the player must act on, is one to three words, and is spoken *plus* printed *plus* flashed on the radar. The forgotten detail is the throttle — a two-minute repeat delay is the difference between an advisor and a nag. And the lines explain **causes, not effects**: the superweapon suspension announces *no power*, not *superweapon offline*.

**Production never requires leaving the fight.** C&C's sidebar is always visible, always in the same place, queueable from anywhere on the map, and doubles as the tech tree — new buttons *appearing* is how you learn you have teched. This is the single biggest reason C&C feels less stressful than StarCraft.

**Tech is a visible, scoutable, killable building, and each tier introduces a new silhouette.** Escalation is legible because it is a thing you *see arriving*, and capstone units are a different category of object rather than a stat bump.

---

## Refusals — do not carry forward

| Refusal | Why |
|---|---|
| **Regenerating infinite resources** | Root cause of C&C turtling. A defended refinery becomes a permanent annuity, so the correct play is turtle-and-outlast. |
| **Superweapons with no interception** | RA2 shipped a lobby checkbox to switch superweapons off — a designer admitting the mechanic does not fit every match. |
| **Harvester micromanagement** | Wandering into fire, choosing bad fields, C&C3 shipping harvesters that abandoned full loads. Keep the vulnerability; automate the chore. |
| **Scalar-only veterancy in a selection box** | Tiberian Sun's +25% scalars were widely reported as unnoticeable; its discrete *capability* unlocks were not. See `readability.md` §6. |
| **One faction owning the entire information layer** | RA1's Allies get Gap Generator, Spy, Thief and GPS; the Soviets get nothing. Asymmetry must be different answers to the same question — not one side lacking a question. |
| **Deploy-to-fire as a pacing brake** | It *works* — it killed the Red Alert tank rush — but the bill arrives months after launch, when nobody is testing first impressions. See the tempo warning below. |
| **Adjacency-based placement where walls count** | Produces the $50 sandbag crawl. But radius-based placement is *also* an exploit surface — C&C3's patch history is a chronicle of stripping build radius off structures. Neither is solved: pick one and audit every structure that projects it. |

---

## The tempo warning

**The single most important methodological finding in the research**, and it bears directly on how `game-design-pillars.md` proposes to validate the vertical slice.

Tiberian Sun deliberately spent Red Alert's pace to kill the tank rush, and it worked. It also scored PC Gamer 92, PC Zone 90%, and sold 2.4 million copies. **No contemporaneous 1999 review describes it as slow.** The pacing criticism that now defines its reputation is *entirely retrospective* — it emerged from competitive play and long-horizon familiarity, months and years after launch.

**Tempo problems do not surface in first-session testing.** Our pillars document proposes to judge the slice by playing it, which is right — but that method is structurally blind to this specific failure mode. Budget for the pace you spend, and treat any mechanic that trades tempo for depth (deploy-to-fire, long build times, slow projectiles) as a decision requiring *sustained* play to evaluate, not a first impression.

One honest caveat on all C&C3 numbers, from the person who balanced it: its development cycle was around eleven months and shipped "first pass work as final across all areas, balance and design included." Take C&C3's *intent* seriously; do not treat its specific values as considered design.
