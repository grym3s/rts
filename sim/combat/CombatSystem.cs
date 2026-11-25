using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>Tick step 4 (see ../../CONTEXT.md): acquire targets, fire on cooldown, apply damage.
/// Cleanup (step 6) despawns the dead. Deterministic: store order everywhere, no RNG, no floats.</summary>
public static class CombatSystem
{
    /// <summary>One tick of combat. Chase distance: a unit ordered to attack a target outside its
    /// weapons range gets a fresh MoveOrder toward that target each tick it is idle-pathed.
    /// `events` (optional) receives a Shot per hit and a Death per kill (T09).</summary>
    public static void Step(UnitStore units, Dictionary<EntityId, MoveOrder> orders, int tick, SimWorld? events = null)
    {
        foreach (var u in units.Units)
        {
            if (u.Hp.Raw <= 0) continue; // killed earlier this tick: no actions

            // auto-acquire for attack-move: nearest living enemy in sight, first wins on ties (store order)
            if (u.AttackMoving && !HasLiveTarget(u, units))
            {
                if (orders.ContainsKey(u.Id)) u.TargetId = Acquire(u, units);
                else u.AttackMoving = false; // route finished: from here on the unit guards where it stopped
            }

            if (u.Damage.Raw != 0) Guard(u, units, orders, tick);

            if (u.CooldownRemaining > 0) u.CooldownRemaining--;

            if (u.TargetId == EntityId.None || u.Damage.Raw == 0) continue;
            var target = units.Find(u.TargetId);
            // T25: a weapon that can't engage the target's domain (rifle vs jet, torpedo vs tank)
            // drops it like a dead one: the order was impossible, not pending.
            if (target == null || target.Hp.Raw <= 0 || !CanHit(u, target))
            {
                u.TargetId = EntityId.None;
                if (u.AttackMoving) continue; // resume route
                orders.Remove(u.Id);
                continue;
            }

            var dist = (target.Position - u.Position).Length;
            var fireRange = u.Range + u.Radius + target.Radius;
            if (dist <= fireRange)
            {
                if (u.CooldownRemaining == 0)
                {
                    // counter matrix: base damage x canon cell (tools/refmodel_counter_matrix.py).
                    // Chip floor: a live weapon always does >= 1 whole damage — soft-with-cliffs
                    // means chip, never bounce (ADR 0005); floor decided in the reference model.
                    var applied = u.Damage * DamageMatrix.Get(u.DamageType, target.Armor);
                    if (applied.Raw < Fix64.OneRaw) applied = Fix64.One;
                    var wasAlive = target.Hp.Raw > 0;
                    target.Hp -= applied;
                    events?.Emit(SimEventKind.Shot, u.Id, target.Id, applied);
                    if (wasAlive && target.Hp.Raw <= 0)
                        events?.Emit(SimEventKind.Death, target.Id, u.Id, faction: target.Faction);
                    u.CooldownRemaining = u.CooldownTicks;
                }
            }
            else if (u.Speed.Raw == 0)
            {
                if (u.Guarding) { u.TargetId = EntityId.None; u.Guarding = false; } // static guard: out of reach
            }
            else if (!u.AttackMoving)
            {
                // chase: walk to the target unless already pathing there
                var o = orders.TryGetValue(u.Id, out var existing) ? existing : null;
                if (o == null || o.Target != target.Position)
                    orders[u.Id] = new MoveOrder(target.Position, new List<FixVec2>(), tick);
            }
        }
    }

    /// <summary>Leash: a guard gives up a chase once it is this many sight ranges from its anchor.</summary>
    private static readonly Fix64 LeashSights = Fix64.Ratio(3, 2);

    /// <summary>T05 hold / chase / return. A unit with no order and no target holds its spot and
    /// engages the nearest enemy in sight (becoming Guarding, anchored where it stood). It chases
    /// until the leash or until nothing is left to fight in sight, then walks back (Returning,
    /// no acquiring on the way) and holds again. Any player order clears all of this.</summary>
    private static void Guard(Unit u, UnitStore units, Dictionary<EntityId, MoveOrder> orders, int tick)
    {
        var hasOrder = orders.ContainsKey(u.Id);
        if (u.Returning)
        {
            if (hasOrder) return;
            u.Returning = false; // home: hold again
        }
        if (u.AttackMoving) return;
        if (u.BridgeTarget >= 0) return; // T26: a bridge job is a player order: no auto-acquiring

        var hasLive = HasLiveTarget(u, units);
        if (u.Guarding)
        {
            var leash = u.Sight * LeashSights;
            if ((u.Position - u.GuardAnchor).LengthSquared > leash * leash)
            {
                ReturnToAnchor(u, orders, tick);
                return;
            }
            if (hasLive) return;
            var next = Acquire(u, units);
            if (next != EntityId.None) { u.TargetId = next; return; }
            ReturnToAnchor(u, orders, tick);
            return;
        }

        if (hasOrder || hasLive) return; // under a player order or an explicit attack
        var found = Acquire(u, units);
        if (found == EntityId.None) return;
        u.GuardAnchor = u.Position;
        u.Guarding = true;
        u.TargetId = found;
    }

    private static void ReturnToAnchor(Unit u, Dictionary<EntityId, MoveOrder> orders, int tick)
    {
        u.TargetId = EntityId.None;
        u.Guarding = false;
        u.Returning = true;
        orders[u.Id] = new MoveOrder(u.GuardAnchor, new List<FixVec2>(), tick);
    }

    private static bool HasLiveTarget(Unit u, UnitStore units)
    {
        if (u.TargetId == EntityId.None) return false;
        var t = units.Find(u.TargetId);
        return t != null && t.Hp.Raw > 0;
    }

    /// <summary>T25: the weapon's `targets` include the target's domain (buildings are ground).</summary>
    public static bool CanHit(Unit u, Unit target) => Domains.CanEngage(u.Targets, target.Domain);

    /// <summary>Nearest enemy strictly inside sight range that the weapon can engage; store order
    /// breaks distance ties.</summary>
    private static EntityId Acquire(Unit u, UnitStore units)
    {
        var best = EntityId.None;
        var bestD = Fix64.Zero;
        foreach (var e in units.Units)
        {
            if (e.Faction == u.Faction || e.Hp.Raw <= 0 || !CanHit(u, e)) continue;
            var d = (e.Position - u.Position).LengthSquared;
            if (d > u.Sight * u.Sight) continue;
            if (best == EntityId.None || d < bestD)
            {
                best = e.Id;
                bestD = d;
            }
        }
        return best;
    }
}
