# Design direction — what we are making and what we borrow from

Durable creative reference. Set 2026-08-23. Changed by PR, like everything else in `docs/`.

This is **not** `game-design-pillars.md`. That file asks one narrow question — is the vertical slice fun to control — and explicitly defers factions, economy, art and sound. This file is the direction those deferred things resolve toward when their time comes.

## The lineage, stated plainly

```
Command & Conquer          Dust Front
readable mechanics,        huge oppressive machinery,
very strong faction        industrial warfare, scale,
identity                   atmosphere, modern presentation
        └──────────┬───────────────┘
                   ▼
             our own universe
   more asymmetric factions, modern simulation,
   destruction, battlefield persistence — systems
   that were not practical in a 1990s/2000s RTS
```

Goal phrase: **familiar design DNA + original execution.**

## The mandate

We *want* recognisable RTS DNA. Inventing every mechanic from first principles is a failure mode, not a virtue — it is how two-person teams stall. Where C&C or Dust Front already solved an RTS design problem well, the job is to identify *what makes that solution effective* and adapt the underlying principle.

Reference set: Dust Front; Command & Conquer (Tiberian Dawn, Red Alert 1/2, Tiberian Sun, Generals, C&C3); other classic and modern RTS where useful.

Draw actively from: faction design philosophy · visual design language · industrial and military aesthetics · unit silhouettes · scale · battlefield atmosphere · base-building · resource systems · production · technology progression · counter systems · veterancy · superweapons · asymmetric faction mechanics · combined-arms warfare · map control · expansion · defensive structures · pacing · UI and readability · sound and visual feedback philosophy.

## The four questions

Every mechanic proposal answers these, in order, out loud:

1. **What inspiration is being taken?** Name the game and the specific mechanic.
2. **Why does that idea work?** Mechanically or perceptually — not "it's iconic".
3. **What do we change?**
4. **How does our implementation become distinctive?**

Say it directly: *"This works in C&C because…"*, *"Dust Front handles this well by…"*, *"We take the underlying idea but adapt it as…"*.

## Creative boundary

Inspiration may be strong. Borrow freely: mechanical concepts, structural ideas, broad visual language, genre conventions, pacing, faction archetypes, industrial design principles, atmosphere, gameplay loops.

Turn those influences into a **coherent new setting**. Never reproduce an existing faction or unit one-for-one.

| | |
|---|---|
| ✅ | Take Dust Front's imposing industrial mass and battlefield readability, combine with C&C's faction readability, and create a new heavy mechanised faction. |
| ✅ | Use C&C-style harvesting as a starting point, then alter how territory, processing and logistics work. |
| ✅ | Take inspiration from Dust Front's enormous tracked machinery, but establish our own recurring hull shapes, armour geometry, weapons and engineering philosophy. |
| ❌ | Make a Mammoth Tank with another name. |
| ❌ | Recreate an existing Dust Front vehicle and change the colour. |

## Priorities, in order

**FUN · READABILITY · FACTION IDENTITY · SPECTACLE**

Do not pursue originality for its own sake. When two options tie, the one higher on this list wins.

## Status

Reference studies (Dust Front, C&C, the 2026 RTS landscape) are in progress. Faction rosters, aesthetic rules, economy and unit specifics land here as they are decided — each as its own section or its own file, per `docs/CONTEXT.md`. Decisions that constrain the sim get an ADR in `decisions/`.
