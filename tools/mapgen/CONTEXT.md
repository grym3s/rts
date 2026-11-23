# tools/mapgen — generate hand-designed maps as data

Owns: `tri-point.mjs`, the generator for the Tri-Point map (design: Notion "Map Design: Tri-Point (3-Player Skirmish)", 2026-10-07). It rasterises the design's geometry with 120° rotational symmetry into typed tiles (land / water / cliff / bridge), measures the design's distance table, and emits the map and the 30v30 scenario.
Reads: nothing (geometry constants live in the script). Writes: `content/maps/tri-point.json`, `content/scenarios/tri-point-30.json`, optional `--preview <file.svg>`.
Run: `node tools/mapgen/tri-point.mjs [--preview out.svg]` (Node 18+). It prints a distance table and exits non-zero if any required route (land or naval) is unreachable or a spawn lands off land.
Tests: none of its own. The output scenario's replay hash is pinned in CI and `tools/sim-host.tests` — regenerating with different geometry changes the hash, so re-pin both in the same PR.
Do NOT: hand-edit the generated JSON (edit the script and regenerate); add game rules; make the output depend on time, randomness or locale.
Change impact: `map/effects/CONTEXT.md` (content / scenarios — golden hashes).
Known limits (2026-11-24):
- The sim has no water yet, so the scenario's `blocked` = water + cliff; bridges and land are walkable. Ships, beaches, capture, destructible causeways and the third faction are map *data* only (`features`), waiting on sim domains.
- Distance targets are only partly met: Seams are equidistant per pair and mains ≥2.0 apart, but island/Seam is ~0.9 (design asks 1.25). With Seams on the outer ring and straight causeways the geometry caps that ratio near 1.15; a bent causeway or inner Seams would be needed.
- A square grid can't be exactly 120°-symmetric: octile distances differ ~5–8% between the top corner and the two lower ones (incl. harbour→rig).
- Ridges flanking causeways are not modelled (causeways are already surrounded by water).
