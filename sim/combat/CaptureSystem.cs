using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>T28, tick step 7 right after BridgeSystem: engineers with a capture job
/// (`Unit.CaptureTarget`, set by a `capture` command) walk to it and take it on touch, instant,
/// and are consumed. What can be captured: any enemy or neutral building, and an enemy MCV
/// (a unit that deploys into a construction yard). The captured thing changes owner and keeps
/// its hit points; what that ownership then allows lives where the rules already are:
/// production fields a captured building's units for its new owner, and owning a yard (or MCV)
/// of another tree opens that tree's buildings (`ProductionSystem.HasTree`); an owned oil
/// derrick drips credits (`EconomySystem.DripStep`). Bridges are not captured: an engineer
/// repairs them (BridgeSystem). Deterministic: store order, no RNG, no floats.</summary>
public static class CaptureSystem
{
    /// <summary>How close an engineer must get to an MCV (centre distance, plus both radii).
    /// A building is touched from any cell next to its footprint (diagonals count).</summary>
    public static readonly Fix64 Reach = Fix64.Ratio(3, 2);

    /// <summary>What an engineer may take: a live building, or a live MCV that isn't a building.</summary>
    public static bool Capturable(Unit t) => t.Hp.Raw > 0 && (t.Building != null || t.DeploysTo != null);

    /// <summary>`refundQueued(oldOwner, unitId)` returns a captured building's queued units'
    /// cost to the side that paid for them; the queue itself is cleared.</summary>
    public static void Step(GameMap map, UnitStore units, Dictionary<EntityId, MoveOrder> orders, int tick,
        SimWorld? events = null, Action<int, string>? refundQueued = null)
    {
        foreach (var u in units.Units)
        {
            if (u.CaptureTarget == EntityId.None || u.Hp.Raw <= 0) continue;
            var t = units.Find(u.CaptureTarget);
            if (t is null || !Capturable(t) || t.Faction == u.Faction) { Drop(u, orders); continue; }
            if (Touching(u, t, map))
            {
                Take(t, u.Faction, units, orders, refundQueued);
                u.Hp = Fix64.Zero; // consumed: leaves the roster at cleanup (no death event)
                u.CaptureTarget = EntityId.None;
                orders.Remove(u.Id);
                events?.Emit(SimEventKind.Captured, t.Id, u.Id, faction: u.Faction);
                continue;
            }
            // walk there (a building's centre is blocked: navigation ends next to the footprint);
            // re-issue when an MCV has moved
            if (!orders.TryGetValue(u.Id, out var o) || o.Target != t.Position)
                orders[u.Id] = new MoveOrder(t.Position, new List<FixVec2>(), tick);
        }
    }

    private static void Drop(Unit u, Dictionary<EntityId, MoveOrder> orders)
    {
        u.CaptureTarget = EntityId.None;
        orders.Remove(u.Id);
    }

    private static bool Touching(Unit u, Unit t, GameMap map)
    {
        if (t.Building != null && map.TryGetFootprint(t.Id.Value, out var r))
        {
            var cx = GameMap.CellOf(u.Position.X);
            var cy = GameMap.CellOf(u.Position.Y);
            return cx >= r.X - 1 && cx <= r.X + r.W && cy >= r.Y - 1 && cy <= r.Y + r.H;
        }
        return (t.Position - u.Position).Length <= Reach + u.Radius + t.Radius;
    }

    /// <summary>Hand `t` to `faction`: a building's queue is refunded to the old owner and cleared,
    /// its rally dropped; an MCV stops where it is. The new owner's units stop shooting it; the
    /// old owner's units are now its enemies and acquire it like any other.</summary>
    private static void Take(Unit t, int faction, UnitStore units, Dictionary<EntityId, MoveOrder> orders,
        Action<int, string>? refundQueued)
    {
        if (t.Building is { } b)
        {
            if (t.Faction != Factions.Neutral)
                foreach (var q in b.Queue) refundQueued?.Invoke(t.Faction, q);
            b.Queue.Clear();
            b.QueueHeadRemaining = 0;
            b.Rally = null;
        }
        else
        {
            orders.Remove(t.Id);
            t.TargetId = EntityId.None;
            t.AttackMoving = t.Guarding = t.Returning = false;
            t.CaptureTarget = EntityId.None;
            t.BridgeTarget = -1;
        }
        t.Faction = faction;
        foreach (var v in units.Units)
            if (v.Faction == faction && v.TargetId == t.Id) v.TargetId = EntityId.None;
    }
}
