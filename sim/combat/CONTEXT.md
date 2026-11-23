# sim/combat — acquire, fire, damage, death, guard

Tick step 4 (see `../CONTEXT.md`). `CombatSystem.Step(units, orders, tick)` per tick, in store order:
1. skip units killed this tick;
2. attack-movers without a live target auto-acquire the nearest enemy in sight (ties → lowest id); an attack-mover whose route has finished stops attack-moving and guards from there;
3. guard (T05, armed units only) — hold, chase, return:
   - idle (no order, no target) → hold; nearest enemy in sight → `Guarding`, `GuardAnchor` = where it stood;
   - guarding past the leash (`1.5 × Sight` from the anchor), or nothing left in sight → `Returning` with a `MoveOrder` to the anchor; no acquiring on the way; arrival → idle again;
   - immobile armed units (speed 0) fire in range only and never chase;
   - any player order (`move`, `attack-move`, `attack`, `stop`) clears target, attack-move and guard state (`OrderSystem.Disengage`);
4. tick down the weapon cooldown;
5. target in `Range + radii` → deal `Damage × DamageMatrix[DamageType, target.Armor]` (floor 1 — chip, never bounce) on cooldown; out of range and not attack-moving → issue/refresh a chase `MoveOrder` to the target (navigation moves them);
6. dead target → clear target (attack-movers resume route; plain attackers drop the order; guards re-acquire or return).

Step 6 cleanup: `UnitStore.DespawnDead()`. No RNG, no floats — fights are replays.
Guard state is not in the state hash directly; it shows up through positions and HP. Scenario: `content/scenarios/guard-hold.json`.
Weapon/HP/sight/faction/armor/damageType numbers come from `content/units/*.json` via `UnitProfile` (sim/units).
`DamageMatrix` cells are design canon (`docs/factions/counter-matrix.md`) — do not tune them here;
naval-extension cells not in canon stay 1.0, cross-domain engagement is a targeting fact, not a multiplier.
Not modelled yet: projectiles, targeting priorities, multi-shot, domain (ground/air) targeting flags.
