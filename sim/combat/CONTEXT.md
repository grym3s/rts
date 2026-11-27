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

Events (T09, when given the world): one `Shot` per hit (subject = shooter, target = victim, amount = damage applied) and one `Death` per kill (subject = dead unit, target = killer, faction = dead unit's).
Step 6 cleanup: `UnitStore.DespawnDead()`. No RNG, no floats — fights are replays.
Guard state is not in the state hash directly; it shows up through positions and HP. Scenario: `content/scenarios/guard-hold.json`.
Weapon/HP/sight/faction/armor/damageType numbers come from `content/units/*.json` via `UnitProfile` (sim/units).
`DamageMatrix` cells are design canon (`docs/factions/counter-matrix.md`) — do not tune them here;
naval-extension cells not in canon stay 1.0, cross-domain engagement is a targeting fact, not a multiplier.
Targeting (T25): `CanHit` = the weapon's `targets` (unit file `weapon.targets`: ground / air / sea; a
weapon without the field is ground-only) include the target's domain — air needs `air`, naval needs
`sea`, hover is hit by `ground` or `sea`, land units and buildings need `ground`. Acquire skips what
it can't hit; an explicit attack on one is dropped. Hand-built test profiles hit everything.
Bridges (T26, `BridgeSystem.Step`, right after CombatSystem in step 7): bridges live in
`../world` `BridgeStore`, not the roster — no faction, never auto-acquired, never counted by the
match. A unit with `BridgeTarget` (set by a `bridge` command) and no unit target works on it:
armed units that can hit ground walk into `Range + radius + 0.5` of the nearest bridge cell and
fire on cooldown (`Damage × DamageMatrix[type, Structure]`, floor 1; event `BridgeHit`); at 0 hp
it collapses (event `BridgeDown`): cells turn to water (`GameMap.SetBridgeIntact`, ships still
pass, paths across re-plan), land units standing on it die (`Death`, killer = the last shooter),
attackers drop the job. Engineers (`Unit.Engineer`, role `engineer`) walk to a damaged or fallen
bridge and within 1.5 cells mend it to full in one touch and are consumed (event
`BridgeRepaired`); on a sound bridge they drop the job. A bridge job is an order: no guard
auto-acquire while it lasts. Static guns never chase a bridge. Tests: `../tests/BridgeTests.cs`.
Owned verbs (T29, `docs/factions/owned-verbs.md`, knobs in `OwnedVerbs.cs`): **Marking** — the step
opens with `RefreshAuras` (post-move positions): aura sources (`MarkRadius`: Comms Rig 8, Spotter
Drone 7, Sensor Command 14) mark living unphased enemies in radius until tick + 30, but only while
their faction has a finished aura building (the Sensor Command network gate); a `MarksOnHit`
weapon (Rifleman) marks what it hits until tick + 40. A hit on a target marked by the shooter's
faction does ×1.25 after the matrix cell (before the chip floor). Binary, no stacking. Line of
sight is not modelled yet (radius only). **Phase** — a phased unit (tick < `PhaseUntilTick`) is
never acquired, can't be hit (an attack on it waits, it isn't dropped) and can't fire (bridges
included); it still moves. Tests: `../tests/OwnedVerbsTests.cs`.
Captures (T28, `CaptureSystem.Step`, right after BridgeSystem in step 7): an engineer with a
`CaptureTarget` (set by `CaptureCommand`; not the Weaver, `CannotCapture`) walks to it and takes it
on touch, instant: any cell next to a building's footprint (diagonals count), or within 1.5 of an
MCV. Capturable = a live building of anyone else (neutral included) or an enemy MCV; anything else,
or something already yours, drops the job. The captured unit's `Faction` changes (hp kept; the hash
mixes Faction only once it differs from `SpawnFaction`); a building's queue is refunded to the old
owner and cleared, its rally dropped; an MCV stops; the new owner's units stop shooting it. The
engineer is consumed (no death event); event `Captured` (subject = captured, target = engineer,
faction = new owner). Neutrals (`Factions.Neutral` = -1, the oil derrick) are never a target
(`CanHit` refuses them; no marks, no Sapper blast); once captured they are ordinary buildings.
A destroyed restorable building (the derrick, `BuildingState.Restorable`) is not removed: at the
start of the capture step it becomes a wreck (`Wrecked`, 1 hp, neutral, queue cleared), so it is
untargetable and pays nobody; capturing the wreck restores it to full hp for the engineer's side.
Bridge huts: `ProductionSystem.SpawnBridgeHuts` puts a neutral `neutral-bridge-hut` on each
landing of every bridge and causeway (`Bridge.Landings`: open land touching it, one per 8-connected
end, its first cell row-major). A hut marks no footprint (units walk past) and is never a target; an
engineer sent to it (same `capture` command) repairs its bridge on touch (`BridgeSystem.Repair`,
event `BridgeRepaired`, engineer consumed) and the hut stays neutral; with the bridge whole the job
drops. Engineers walk to a cell touching a building's footprint that a path reaches (not the
open cell nearest its centre, which can be an island: the Tri-Point derrick's bridge stubs).
What ownership then allows lives in production (captured trees) and economy (the drip). Tests:
`../tests/CaptureTests.cs`.
Not modelled yet: projectiles, targeting priorities, multi-shot, domain (ground/air) targeting flags.
