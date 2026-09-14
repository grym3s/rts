# sim/combat — acquire, fire, damage, death

Tick step 4 (see `../CONTEXT.md`). `CombatSystem.Step(units, orders, tick)` per tick, in store order:
1. skip units killed this tick;
2. attack-movers without a live target auto-acquire the nearest enemy in sight (ties → lowest id);
3. tick down the weapon cooldown;
4. target in `Range + radii` → deal `Damage` on cooldown; out of range and not attack-moving → issue/refresh a chase `MoveOrder` to the target (navigation moves them);
5. dead target → clear target (attack-movers resume route; plain attackers drop the order).

Step 6 cleanup: `UnitStore.DespawnDead()`. No RNG, no floats — fights are replays.
Weapon/HP/sight/faction numbers come from `content/units/*.json` via `UnitProfile` (sim/units).
Not modelled yet: armor multipliers, projectiles, targeting priorities, multi-shot.
