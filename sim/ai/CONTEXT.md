# sim/ai — the computer opponent

Owns: `AiPlayers` / `AiBrain` — for each faction listed in a scenario's `ai`, decides Commands from the current state once a second (deploy, build order, harvest, train, attack waves, defend), deterministically: no RNG beyond `SimWorld.Rng`, no floats, store order. It plays through the same Commands a player sends, so it can't cheat the rules.
Reads: the match world (units, economy, power, catalogs, map) — read-only.
Writes: Commands only, through `SimWorld.InputSources` (joined to the tick's input before due commands are picked).
Runs at: the start of every step, before tick step 1.
Tests: `sim/tests/AiTests.cs`; `make test`.
Do NOT: mutate state directly; read wall-clock time; know anything the rules don't give a player (it does see the whole map: no fog in the sim).
Change impact: `map/effects/CONTEXT.md` (commands)
State: each brain's `Waves` count is mixed into the world hash (`AiPlayers.Hash`; nothing without computer players, so scripted hashes are untouched).
Behaviour: deploy the MCV; one building at a time from the faction build order (power first when it would go negative; refineries beside the nearest ore; defenses toward the map centre), then extra factories when rich; idle harvesters sent to the nearest live vein; queues kept topped up; army gathers toward the centre and attack-moves the nearest enemy building once the wave (6, +3 a wave, cap 24) is gathered; visible enemies near its base pull idle army home; a charged superweapon fires at the costliest enemy building.
Known limits (2026-11-28): one build order per faction, simple wave timing, no micro beyond attack-move, no transports / teleports / abilities beyond what units do on their own.
