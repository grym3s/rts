---
status: proposed
date: 2026-08-30
---
# 0005 — Answers, not holes: every faction can answer every strategy

**Every faction can answer every strategy in the game — detect stealth, kill air, kill armor, kill infantry, break a turtle, reposition, capture objectives, threaten a base. No faction has a capability hole.** Factions differ only in *how well* (a job is rated strong / standard / weak, never absent — weak means worse or clumsier, never *can't*) and *how* (the tool may be a distinctive faction mechanic). Identity comes from **re-profiling** the same role (cheap-mass vs premium vs exotic infantry), **adding** distinctive answers (Tunnel Network, Teleport Network, cloak), and one **owned verb** per faction (Marking / Conscription / Phase) — never from **subtracting** an answer.

Two hard constraints:
1. **Every distinctive answer and owned verb must be deniable** — the opponent can contest it (nodes, sensors, tunnel mouths, Phase cooldown, detection vs cloak). An answer the opponent cannot interact with is a hole seen from the other side.
2. **Matchups target ~even** (~50/50 across all three pairings), not a non-transitive triangle. A faction that reliably beats another via a capability gap is soft rock-paper-scissors.

*Pure options may still differ:* something that answers no strategy — a ground superheavy, a specific superweapon — may exist on one faction and not another, as long as its absence removes no answer (Coalition has no ground superheavy but keeps a top-end finisher via air/naval).

Why: a missing answer is a matchup decided at the faction-select screen — hard rock-paper-scissors, which is pointless to play against. The best-balanced asymmetric RTS give every faction every essential capability and differ in execution (StarCraft races all detect / answer air / cloak; matchups tune to ~even), while balancing by *removing* capabilities is a documented anti-pattern that reduces tactical diversity (Company of Heroes). Distinctive-but-deniable answers (GLA Tunnels, Zerg Creep) are the good kind of asymmetry.

This **refines** the source reference (`docs/rts-unit-roster-design-reference.md`): it overrides the hole-heavy worked roster in Part 7 and the non-transitive-matchup framing in Part 1 (even matchups instead). It also generalizes and replaces an earlier draft that had made only four capabilities (detection/stealth/sniper/carrier) universal — the rule now covers *all* answers.

Detail: `docs/factions/CONTEXT.md` (*Answers, not holes*) and `docs/factions/capability-coverage.md` (the job-by-job matrix, proof of no empty cells).

Known caveat: even matchups and deniability are tuning targets, not free — verify per-matchup balance and that each distinctive answer stays contestable via `tools/scenario` playtesting.
