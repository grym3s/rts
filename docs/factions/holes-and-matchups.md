# Holes & matchups

Every faction has deliberate **holes** — missing slots that create identity (reference Part 5.4). This file audits each one against the two tests a hole must pass, and states the intended matchup triangle the holes are tuned to produce.

## The two tests every hole must pass

1. **Compensated in a different currency.** The faction is paid back for the hole with something it can't buy back the hole with. GLA lost air and got *speed, stealth, economy, structure resilience* — not "better tanks" (reference Part 5.4).
2. **Beatable with counterplay, never an auto-loss.** The opponent exploits the hole through play the defender can answer, not by holding a capability the defender simply lacks. This is why **detection, stealth, sniper, and carrier are never holes** (see `CONTEXT.md`) — lacking one is an auto-loss, not a weakness.

A hole that fails test 2 is a bug, not identity. The universal-capabilities rule exists precisely because "no detection" fails it.

## Coalition — 1 hole

| Hole | Slot | Compensation (different currency) | Counterplay | Why it isn't an auto-loss |
|---|---|---|---|---|
| **Superheavy** | vehicle T3 | Carrier, spotting chain, +2 range everywhere, air dominance | Force a fair-cost ground brawl before air/naval come online; rush before Aerospace Command; mass cheap bodies it can't cost-trade | It still has MBTs, artillery, and air — it just can't win an equal-cost slugfest, so it must fight at range |

Coalition's identity is the **Marking** verb and fragility, not the hole — one hole is enough.

## Hegemony — 2 holes

| Hole | Slot | Compensation | Counterplay | Why it isn't an auto-loss |
|---|---|---|---|---|
| **Light-AT tank destroyer** | vehicle T2 | AT Squad infantry + Bulwark's own AP; cheapest costs out-mass | Armor-heavy pushes force it to answer with fragile AT infantry (dies to AoE, gets run over); no fast mobile tank-killer means inefficient trades | The AT Squad still kills tanks — slower and squishier, but it kills them |
| **Air scout drone** | air | Ground detection (Spotlight Bunker + T2 Halftrack); cheap ground scouts | Timing attacks and expansions it scouts too late; information asymmetry | It still detects stealth (on the ground) and scouts with ground units — it just sees *later* |

Note both holes are about *speed of reaction*, which is the Hegemony's coherent weakness — it out-lasts, it doesn't out-tempo.

## Ascendant — 4 holes, one coherent weakness

The Ascendant carries the most holes, but they **stack into a single weakness** — *it cannot stand and trade in an attritional fight* — rather than four independent auto-losses. Watch this in playtesting: if any two start reading as separate auto-losses, split the compensation.

| Hole | Slot | Compensation | Counterplay | Why it isn't an auto-loss |
|---|---|---|---|---|
| **Transport (all domains)** | logistics | Teleport Network — instant repositioning, better than transports once built | Contest or destroy forward nodes; before a node exists it can't project; kill the node and the army is stranded | The network is a build commitment with a visible kill switch, not free mobility |
| **Interceptor** | air | Glaive (MBT shoots air) + Skyward (AA) + Warden (fleet shield) | Mass air / gunships force it to over-invest ground AA and trade inefficiently | Glaive + Skyward still down aircraft — just not cost-efficiently vs committed air |
| **Heavy infantry** | infantry | Phase + speed (dodge instead of soak); Anchor shields | Force a static hold or choke where kiting fails; a wall of heavy infantry + AoE punishes the fragile line | It avoids the fight instead of holding — it loses the ground-hold game but doesn't have to play it |
| **True submarine** | naval | Cloaked surface hulls (same job, faction verb) | Detection reveals cloaked hulls; being on the surface, they take fire the moment they're seen | Cloak is deterrence like a sub, countered by detection like a sub |

## The intended matchup triangle

Holes and profiles are tuned to produce a **non-transitive triangle** (reference Part 1) — no faction is flatly dominant:

```
        Coalition
        ↗        ↘
  Ascendant  ◀──  Hegemony
```

| Matchup | Who's favoured | Why |
|---|---|---|
| **Coalition > Hegemony** | Coalition | Kites the slow blob, marks it, out-ranges it; artillery and air punish the massed ball the Hegemony wants to form |
| **Hegemony > Ascendant** | Hegemony | The Ascendant can't stand and trade; a cheap, tanky wall with AoE grinds the fragile army, and the Hegemony's own bulk shrugs off phase-harass |
| **Ascendant > Coalition** | Ascendant | Speed + Phase get *inside* Coalition's range advantage and delete its expensive, fragile units before they trade; teleport strikes the flank Coalition can't defend |

These are **tendencies at equal skill**, decided by composition and positioning — not guarantees. Mirrors and the off-diagonal are meant to be close; the triangle is the *tuning target* for `tools/scenario` balance runs, not a scripted outcome. Tune the MBT mirror first, then the three cross-matchups (reference Part 8).
