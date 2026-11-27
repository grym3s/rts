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
/// repairs them (BridgeSystem). The Weaver repairs its own side's damaged buildings instead of
/// capturing (RepairPerTick while touching, not consumed). The Mindbender channels from range
/// instead of touching and is not consumed: a building is taken for good after ChannelBuildingTicks;
/// an enemy unit is controlled after ChannelUnitTicks (MaxControlled at a time) and goes back to its
/// owner when the Mindbender dies, changes sides or walks beyond Leash of it. Deterministic: store
/// order, no RNG, no floats.</summary>
public static class CaptureSystem
{
    /// <summary>How close an engineer must get to an MCV (centre distance, plus both radii).
    /// A building is touched from any cell next to its footprint (diagonals count).</summary>
    public static readonly Fix64 Reach = Fix64.Ratio(3, 2);

    /// <summary>Weaver repair: hp per tick while touching its own damaged building (25 hp/s).</summary>
    public static readonly Fix64 RepairPerTick = Fix64.Ratio(5, 4);
    /// <summary>Mindbender: channel length on a building (6 s) and on a unit (3 s).</summary>
    public const int ChannelBuildingTicks = 6 * SimWorld.TicksPerSecond;
    public const int ChannelUnitTicks = 3 * SimWorld.TicksPerSecond;
    /// <summary>Mindbender: how far it channels from (to a unit's edge, or a footprint's nearest cell).</summary>
    public static readonly Fix64 ChannelRange = Fix64.FromInt(5);
    /// <summary>Mindbender: controlled units further than this go back to their owner.</summary>
    public static readonly Fix64 Leash = Fix64.FromInt(12);
    /// <summary>Mindbender: units it holds at once; a new one frees the one held longest.</summary>
    public const int MaxControlled = 3;

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

        // Mind control holds only while its Mindbender lives, stays on the same side and is near.
        foreach (var c in units.Units)
        {
            if (c.ControlledBy == EntityId.None || c.Hp.Raw <= 0) continue;
            var m = units.Find(c.ControlledBy);
            if (m is null || m.Hp.Raw <= 0 || m.Faction != c.Faction || (m.Position - c.Position).Length > Leash)
                Release(c, m?.Id ?? EntityId.None, units, orders, refundQueued, events);
        }

        foreach (var u in units.Units)
        {
            if (u.CaptureTarget == EntityId.None || u.Hp.Raw <= 0) { u.ChannelTicks = 0; continue; }
            var t = units.Find(u.CaptureTarget);
            if (u.MindControl) { Channel(u, t, map, units, orders, tick, events, refundQueued); continue; }
            if (u.Ability.Sabotages || u.Ability.Hijacks) { Infiltrate(u, t, map, units, orders, tick, events, refundQueued); continue; }
            if (u.RepairsStructures && t is { Building: { BridgeId: < 0, Wrecked: false } } && t.Faction == u.Faction)
            {
                Mend(u, t, map, orders, tick);
                continue;
            }
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
            WalkBeside(u, fp, map, orders, tick);
        }
    }

    /// <summary>A building: walk to a cell touching its footprint that a path actually reaches
    /// (the centre is blocked, and the open cell nearest it can be an island: the derrick's
    /// one-cell bridge stubs on Tri-Point). Planned once; kept while it still touches.</summary>
    private static void WalkBeside(Unit u, (int X, int Y, int W, int H) fp, GameMap map,
        Dictionary<EntityId, MoveOrder> orders, int tick)
    {
        if (orders.TryGetValue(u.Id, out var cur) && Touches(GameMap.CellOf(cur.Target.X), GameMap.CellOf(cur.Target.Y), fp))
            return;
        if (Approach(u, fp, map) is { } cell)
            orders[u.Id] = new MoveOrder(GameMap.CellCenter(cell.X, cell.Y), new List<FixVec2>(), tick);
        else
            Drop(u, orders); // nowhere beside it can be reached
    }

    /// <summary>Operative sabotage: how long a building stays offline (30 s).</summary>
    public const int SabotageTicks = 30 * SimWorld.TicksPerSecond;

    /// <summary>A hijacker's prize: an enemy ground vehicle (not infantry, aircraft, ships or buildings).</summary>
    public static bool Hijackable(Unit t) =>
        t.Building == null && t.Hp.Raw > 0 && t.Armor is ArmorClass.Light or ArmorClass.Heavy
        && t.Domain is MoveDomain.Land or MoveDomain.Hover && t.Inside == EntityId.None;

    /// <summary>Operative: touch an enemy building and shut it down for SabotageTicks (no power,
    /// production or fire). Hijacker: touch an enemy ground vehicle and drive it off for good.
    /// Both are used up doing it.</summary>
    private static void Infiltrate(Unit u, Unit? t, GameMap map, UnitStore units, Dictionary<EntityId, MoveOrder> orders,
        int tick, SimWorld? events, Action<int, string>? refundQueued)
    {
        var ok = t is { Hp.Raw: > 0 } && t.Faction != u.Faction && t.Faction != Factions.Neutral
            && (u.Ability.Sabotages ? t.Building is { BridgeId: < 0, Wrecked: false } : Hijackable(t));
        if (!ok) { Drop(u, orders); return; }
        if (Touching(u, t!, map))
        {
            if (u.Ability.Sabotages)
            {
                t!.SabotagedUntilTick = tick + SabotageTicks;
                events?.Emit(SimEventKind.Sabotaged, t.Id, u.Id, Fix64.FromInt(SabotageTicks), u.Faction);
            }
            else
            {
                t!.ControlledBy = EntityId.None; // stolen outright, whoever held it
                Take(t, u.Faction, units, orders, refundQueued);
                events?.Emit(SimEventKind.Captured, t.Id, u.Id, faction: u.Faction);
            }
            u.Hp = Fix64.Zero; // used up: leaves the roster at cleanup
            Drop(u, orders);
            return;
        }
        if (t!.Building != null && map.TryGetFootprint(t.Id.Value, out var fp)) WalkBeside(u, fp, map, orders, tick);
        else if (!orders.TryGetValue(u.Id, out var o) || o.Target != t.Position)
            orders[u.Id] = new MoveOrder(t.Position, new List<FixVec2>(), tick);
    }

    /// <summary>Weaver: walk beside its own damaged, finished building and repair it to full.</summary>
    private static void Mend(Unit u, Unit t, GameMap map, Dictionary<EntityId, MoveOrder> orders, int tick)
    {
        if (t.Hp.Raw <= 0 || t.Hp >= t.MaxHp || t.Building!.ConstructionRemaining > 0
            || !map.TryGetFootprint(t.Id.Value, out var fp)) { Drop(u, orders); return; }
        if (!Touching(u, t, map)) { WalkBeside(u, fp, map, orders, tick); return; }
        orders.Remove(u.Id);
        t.Hp = Fix64.Min(t.MaxHp, t.Hp + RepairPerTick);
        if (t.Hp >= t.MaxHp) Drop(u, orders);
    }

    /// <summary>Mindbender: what it may channel on: a live enemy or neutral unit or building, not a
    /// bridge hut, a wreck or a phased unit.</summary>
    public static bool Channelable(Unit u, Unit? t, int tick) =>
        t is { Hp.Raw: > 0 } && t.Faction != u.Faction && !t.IsPhased(tick)
        && t.Building is not ({ BridgeId: >= 0 } or { Wrecked: true });

    private static void Channel(Unit u, Unit? t, GameMap map, UnitStore units, Dictionary<EntityId, MoveOrder> orders,
        int tick, SimWorld? events, Action<int, string>? refundQueued)
    {
        if (!Channelable(u, t, tick)) { u.ChannelTicks = 0; Drop(u, orders); return; }
        var fp = (X: 0, Y: 0, W: 0, H: 0);
        var isBuilding = t!.Building != null && map.TryGetFootprint(t.Id.Value, out fp);
        u.ChannelNeed = t.Building != null ? ChannelBuildingTicks : ChannelUnitTicks;
        if (!InChannelRange(u, t, isBuilding, fp))
        {
            u.ChannelTicks = 0; // walked or pushed out of range: start over
            if (isBuilding) WalkBeside(u, fp, map, orders, tick);
            else if (!orders.TryGetValue(u.Id, out var o) || o.Target != t.Position)
                orders[u.Id] = new MoveOrder(t.Position, new List<FixVec2>(), tick);
            return;
        }
        orders.Remove(u.Id); // stand still and channel
        if (++u.ChannelTicks < u.ChannelNeed) return;
        u.ChannelTicks = 0;
        u.CaptureTarget = EntityId.None;
        if (t.Building != null) Take(t, u.Faction, units, orders, refundQueued); // for good
        else if (t.ControlledBy != EntityId.None && t.OriginalFaction == u.Faction)
        {
            Release(t, u.Id, units, orders, refundQueued, events); // its own side takes it back
            return;
        }
        else
        {
            Unit? oldest = null;
            var held = 0;
            foreach (var c in units.Units)
                if (c.ControlledBy == u.Id && c.Hp.Raw > 0)
                {
                    held++;
                    if (oldest is null || c.ControlledSinceTick < oldest.ControlledSinceTick) oldest = c;
                }
            if (held >= MaxControlled) Release(oldest!, u.Id, units, orders, refundQueued, events);
            if (t.ControlledBy == EntityId.None) t.OriginalFaction = t.Faction;
            Take(t, u.Faction, units, orders, refundQueued);
            t.ControlledBy = u.Id;
            t.ControlledSinceTick = tick;
        }
        events?.Emit(SimEventKind.Captured, t.Id, u.Id, faction: u.Faction);
    }

    private static bool InChannelRange(Unit u, Unit t, bool isBuilding, (int X, int Y, int W, int H) fp)
    {
        if (!isBuilding) return (t.Position - u.Position).Length <= ChannelRange + t.Radius;
        var cx = GameMap.CellOf(u.Position.X);
        var cy = GameMap.CellOf(u.Position.Y);
        var dx = Math.Max(0, Math.Max(fp.X - cx, cx - (fp.X + fp.W - 1)));
        var dy = Math.Max(0, Math.Max(fp.Y - cy, cy - (fp.Y + fp.H - 1)));
        var r = ChannelRange.ToIntFloor();
        return dx * dx + dy * dy <= r * r;
    }

    /// <summary>A mind-controlled unit goes back to the side it was taken from.</summary>
    private static void Release(Unit c, EntityId by, UnitStore units, Dictionary<EntityId, MoveOrder> orders,
        Action<int, string>? refundQueued, SimWorld? events)
    {
        c.ControlledBy = EntityId.None;
        c.ControlledSinceTick = 0;
        var to = c.OriginalFaction;
        c.OriginalFaction = 0;
        Take(c, to, units, orders, refundQueued);
        events?.Emit(SimEventKind.Captured, c.Id, by, faction: to);
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
    internal static void Take(Unit t, int faction, UnitStore units, Dictionary<EntityId, MoveOrder> orders,
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
