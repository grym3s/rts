# sim/ai — the computer opponent

Owns: `AiPlayers` / `AiBrain` — for each faction listed in a scenario's `ai`, decides Commands from the current state once a second (deploy, build order, harvest, train, attack waves, defend), deterministically: no RNG beyond `SimWorld.Rng`, no floats, store order. It plays through the same Commands a player sends, so it can't cheat the rules.
Reads: the match world (units, economy, power, catalogs, map) — read-only.
Writes: Commands only, through `SimWorld.InputSources` (joined to the tick's input before due commands are picked).
Runs at: the start of every step, before tick step 1.
Tests: `sim/tests/AiTests.cs`; `make test`.
Do NOT: mutate state directly; read wall-clock time; know anything the rules don't give a player (it does see the whole map: no fog in the sim).
Change impact: `map/effects/CONTEXT.md` (commands)
Known limits (2026-11-28): one build order per faction, simple wave timing, no micro beyond attack-move.
