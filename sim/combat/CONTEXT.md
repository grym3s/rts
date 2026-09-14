# sim/combat — acquire, fire, damage, death

Tick step 4 (see `../CONTEXT.md`). `CombatSystem.Step(units, orders, tick)` per tick, in store order:
1. skip units killed this tick;
2. attack-movers without a live target auto-acquire the nearest enemy in sight (ties → lowest id);
3. tick down the weapon cooldown;
4. target in `Range + radii` → deal `Damage × DamageMatrix[DamageType, target.Armor]` (floor 1 — chip, never bounce) on cooldown; out of range and not attack-moving → issue/refresh a chase `MoveOrder` to the target (navigation moves them);
5. dead target → clear target (attack-movers resume route; plain attackers drop the order).

Step 6 cleanup: `UnitStore.DespawnDead()`. No RNG, no floats — fights are replays.
Weapon/HP/sight/faction/armor/damageType numbers come from `content/units/*.json` via `UnitProfile` (sim/units).
`DamageMatrix` cells are design canon (`docs/factions/counter-matrix.md`) — do not tune them here;
naval-extension cells not in canon stay 1.0, cross-domain engagement is a targeting fact, not a multiplier.
Not modelled yet: projectiles, targeting priorities, multi-shot, domain (ground/air) targeting flags.
