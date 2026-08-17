---
status: accepted
date: 2026-08-17
---
# 0003 — The sim uses fixed-point math and is deterministic from day one

Positions, velocities and rules use `Fix64` (Q32.32) and a seeded `Rng`; no floats in `sim/`. Ticks are fixed-rate; the sim's only inputs are the scenario and the tick-stamped command stream.

Why: deterministic replays are the primary debugging and regression tool (a failing scenario is a file); float determinism across machines is not guaranteed; retrofitting fixed-point touches every line of the sim, adding it now is a few hundred lines. Cross-machine determinism (multiplayer/campaign) is a bonus, not the reason.

Consequences: golden-replay tests in CI; presentation converts to float only at the render boundary.
