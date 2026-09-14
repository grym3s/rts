using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>Tick step 4 (see ../../CONTEXT.md): acquire targets, fire on cooldown, apply damage.
/// Cleanup (step 6) despawns the dead. Deterministic: store order everywhere, no RNG, no floats.</summary>
public static class CombatSystem
{
    /// <summary>One tick of combat. Chase distance: a unit ordered to attack a target outside its
    /// weapons range gets a fresh MoveOrder toward that target each tick it is idle-pathed.</summary>
    public static void Step(UnitStore units, Dictionary<EntityId, MoveOrder> orders, int tick)
    {
        foreach (var u in units.Units)
        {
            if (u.Hp.Raw <= 0) continue; // killed earlier this tick: no actions

            // auto-acquire for attack-move: nearest living enemy in sight, first wins on ties (store order)
            if (u.AttackMoving && !HasLiveTarget(u, units))
                u.TargetId = Acquire(u, units);

            if (u.CooldownRemaining > 0) u.CooldownRemaining--;

            if (u.TargetId == EntityId.None || u.Damage.Raw == 0) continue;
            var target = units.Find(u.TargetId);
            if (target == null || target.Hp.Raw <= 0)
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
                    target.Hp -= u.Damage;
                    u.CooldownRemaining = u.CooldownTicks;
                }
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

    private static bool HasLiveTarget(Unit u, UnitStore units)
    {
        if (u.TargetId == EntityId.None) return false;
        var t = units.Find(u.TargetId);
        return t != null && t.Hp.Raw > 0;
    }

    /// <summary>Nearest enemy strictly inside sight range; store order breaks distance ties.</summary>
    private static EntityId Acquire(Unit u, UnitStore units)
    {
        var best = EntityId.None;
        var bestD = Fix64.Zero;
        foreach (var e in units.Units)
        {
            if (e.Faction == u.Faction || e.Hp.Raw <= 0) continue;
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
