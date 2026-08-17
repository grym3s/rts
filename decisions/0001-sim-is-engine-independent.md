---
status: accepted
date: 2026-08-17
---
# 0001 — The simulation is an engine-independent .NET library

`sim/` holds all game state and rules, advances by `Tick(commands)` at a fixed rate, and references nothing but the .NET BCL. `game/` (Godot) and `tools/` consume `SimState`/events and submit `Command`s; they never mutate sim state directly.

Why: headless testing and replays for agent-driven development; the engine choice becomes reversible at the cost of the presentation layer only; AI, tests and (later, maybe) networking all enter through the same door.

Consequences: CI fails if `sim/` references Godot; presentation interpolates between fixed ticks; every player action is a `Command`.
