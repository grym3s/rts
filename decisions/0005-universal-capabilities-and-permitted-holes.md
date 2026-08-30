---
status: proposed
date: 2026-08-30
---
# 0005 — Universal capabilities and where faction holes are permitted

Faction holes (missing slots) are the strongest identity tool in the genre, but they are constrained. **Four capabilities are universal — every faction fields its own differentiated version and none may lack them: detection, stealth, sniper, and a carrier-equivalent.** Holes are permitted only in *other* slots (superheavy, transport, interceptor, heavy infantry, true submarine) and every hole must pass two tests: compensated in a different currency, and beatable with counterplay rather than an auto-loss.

Why: a hole in a counterplay-critical capability breaks a matchup instead of tilting it. A faction with no detection auto-loses to a single cloaked unit — that is not identity, it is the failure the source reference itself warns against (`docs/rts-unit-roster-design-reference.md` Part 5.4). Detection, stealth, sniper and carrier are each the *answer* to a whole strategic layer; remove one and the faction can't play that layer at all.

This **refines** the worked roster in the design reference Part 7, which had listed detection/sniper/stealth/carrier as Hegemony holes. Those are replaced by differentiated versions (static detector, tunnel-network stealth, dug-in sniper, armored-brute carrier); Hegemony's real holes become the light-AT tank destroyer and the air scout drone.

Detail and the resulting matchup triangle: `docs/factions/CONTEXT.md` (universal-capabilities rule) and `docs/factions/holes-and-matchups.md` (per-faction hole audit).

Known caveat: the Ascendant carries four holes; they are intended to stack into one coherent weakness (can't stand and trade), not four independent auto-losses — a balance risk to verify in `tools/scenario` playtesting.
