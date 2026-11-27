# sim/superweapons — Orbital Lance, Siege Cannon, Rift

Owns: `SuperweaponSystem` — charging (`Unit.ChargeLeft`, from `Abilities.ChargeTicks` when the building finishes; paused while its side is low on power or the building is sabotaged), the `fire` command (only when charged: sets `StrikeAt` / `StrikeTick`, a telegraphed delay), and the strike: `StrikeDamage` to every non-neutral unit and building within `StrikeRadius`, `StrikePulses` times `PulseGapTicks` apart, through the counter matrix as Explosive. Then it charges again.
Reads: `Unit.Ability`, power (`lowPower`).
Writes: hp of whatever the strike hits; `ChargeLeft`, `StrikeAt`, `StrikeTick`, `PulsesLeft`; events `SuperweaponFired` (subject = building, amount = delay ticks) and `Shot` / `Death` for each hit.
Runs at: tick step 7, after captures and carriers.
Tests: `sim/tests/SuperweaponTests.cs`; `make test`.
Do NOT: fire without charge; skip the telegraph (pillar: telegraph power, docs/mechanics-playbook.md).
Change impact: `map/effects/CONTEXT.md` (combat damage)
Known limits (2026-11-28): numbers are tuning, not canon; no interception.
