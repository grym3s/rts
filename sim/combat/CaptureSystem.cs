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
/// derrick drips credits (`EconomySystem.DripStep`). A destroyed derrick is a neutral wreck
/// until an engineer restores it (full hp, the restorer owns it). Bridges are not captured: an engineer
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
        SimWorld? events = null, Action<int, string>? refundQueued = null, BridgeStore? bridges = null)
    {
        // A restorable building (the oil derrick) killed this tick stays as a neutral wreck:
        // 1 hp so cleanup keeps it (and its footprint), untargetable, paying nobody.
        foreach (var w in units.Units)
            if (w.Hp.Raw <= 0 && w.Building is { Restorable: true, Wrecked: false } wb)
            {
                wb.Wrecked = true;
                wb.Queue.Clear();
                wb.QueueHeadRemaining = 0;
                wb.Rally = null;
                w.Hp = Fix64.One;
                w.Faction = Factions.Neutral;
            }

        foreach (var u in units.Units)
        {
            if (u.CaptureTarget == EntityId.None || u.Hp.Raw <= 0) continue;
            var t = units.Find(u.CaptureTarget);
            if (t is null || !Capturable(t) || t.Faction == u.Faction) { Drop(u, orders); continue; }
            // T28: a bridge hut is never taken: an engineer entering it repairs its bridge, and
            // one sent while the bridge is whole has nothing to do
            Bridge? hutBridge = null;
            if (t.Building is { BridgeId: >= 0 } hut)
            {
                hutBridge = bridges?.Get(hut.BridgeId);
                if (hutBridge is not { Damaged: true }) { Drop(u, orders); continue; }
            }
            if (hutBridge != null && Touching(u, t, map))
            {
                BridgeSystem.Repair(hutBridge, map);
                u.Hp = Fix64.Zero; // consumed: leaves the roster at cleanup
                u.CaptureTarget = EntityId.None;
                orders.Remove(u.Id);
                events?.Emit(SimEventKind.BridgeRepaired, new EntityId(hutBridge.Id), u.Id);
                continue;
            }
            if (Touching(u, t, map))
            {
                Take(t, u.Faction, units, orders, refundQueued);
                u.Hp = Fix64.Zero; // consumed: leaves the roster at cleanup (no death event)
                u.CaptureTarget = EntityId.None;
                orders.Remove(u.Id);
                events?.Emit(SimEventKind.Captured, t.Id, u.Id, faction: u.Faction);
                continue;
            }
            // An MCV: walk at it, re-issued whenever it has moved.
            if (t.Building == null || !map.TryGetFootprint(t.Id.Value, out var fp))
            {
                if (!orders.TryGetValue(u.Id, out var o) || o.Target != t.Position)
                    orders[u.Id] = new MoveOrder(t.Position, new List<FixVec2>(), tick);
                continue;
            }
            // A building: walk to a cell touching its footprint that a path actually reaches
            // (the centre is blocked, and the open cell nearest it can be an island: the derrick's
            // one-cell bridge stubs on Tri-Point). Planned once; kept while it still touches.
            if (orders.TryGetValue(u.Id, out var cur) && Touches(GameMap.CellOf(cur.Target.X), GameMap.CellOf(cur.Target.Y), fp))
                continue;
            if (Approach(u, fp, map) is { } cell)
                orders[u.Id] = new MoveOrder(GameMap.CellCenter(cell.X, cell.Y), new List<FixVec2>(), tick);
            else
                Drop(u, orders); // nowhere beside it can be reached
        }
    }

    private static bool Touches(int cx, int cy, (int X, int Y, int W, int H) r) =>
        cx >= r.X - 1 && cx <= r.X + r.W && cy >= r.Y - 1 && cy <= r.Y + r.H
        && !(cx >= r.X && cx < r.X + r.W && cy >= r.Y && cy < r.Y + r.H);

    /// <summary>The reachable cell around the footprint nearest the engineer (ties: row, then
    /// column), or null when none can be reached.</summary>
    private static (int X, int Y)? Approach(Unit u, (int X, int Y, int W, int H) r, GameMap map)
    {
        var from = (GameMap.CellOf(u.Position.X), GameMap.CellOf(u.Position.Y));
        var ring = new List<(int D, int X, int Y)>();
        for (var y = r.Y - 1; y <= r.Y + r.H; y++)
            for (var x = r.X - 1; x <= r.X + r.W; x++)
                if (Touches(x, y, r) && map.IsWalkable(x, y, u.Domain))
                    ring.Add(((x - from.Item1) * (x - from.Item1) + (y - from.Item2) * (y - from.Item2), x, y));
        foreach (var (_, x, y) in ring.OrderBy(c => c.D).ThenBy(c => c.Y).ThenBy(c => c.X))
            if ((x, y) == from || Rts.Sim.Navigation.Pathfinding.FindPath(map, from, (x, y), u.Domain) != null)
                return (x, y);
        return null;
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
            if (b.Wrecked) // restored: back to full hp for whoever fixed it
            {
                b.Wrecked = false;
                t.Hp = t.MaxHp;
            }
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
