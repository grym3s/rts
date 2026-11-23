# sim/match — win and lose (T06)

Owns: `MatchState` (over, winner, end tick, factions seen) and `MatchSystem.Step`, the rule for when a match is decided.
Reads: `world/UnitStore` (living units and their factions). Writes: `MatchState` and one `SimEventKind.MatchEnded` event via `SimWorld.Emit`; nothing else.
Rule: once two or more factions have had a living unit (or a unit that died this tick, read from `Death` events), the match ends on the first tick at most one of them has any living unit left (buildings and harvesters count). Winner = that faction, or -1 (draw) if the last units of every side died together. A world that has only ever had one faction never ends.
Runs at: tick step 9, after cleanup (`../CONTEXT.md`) — it reads the store after the dead are despawned.
Tests: `sim/tests/MatchTests.cs`, `make test`; scenario `content/scenarios/match-end.json` (`assert.matchWinner`).
Do NOT: stop the world stepping when the match ends (replays and hashes must not depend on it), put match state in the state hash (it is derived), or add surrender/timer rules here without an ADR.
Change impact: `map/effects/CONTEXT.md` (match rules row) — the host snapshot `match` field and Unreal's win/lose screen (T19).
Known limits (2026-11-24): factions are 0/1 only until T27; no allies or teams; no objective or timer victory.
