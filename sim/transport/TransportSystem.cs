using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Transport;

/// <summary>Tick step 4b (after production, before navigation): riding inside things. See
/// CONTEXT.md. Holders: transports (Capacity on a unit), bunkers (Capacity + Garrison on a
/// building), the Tunnel Network (Tunnel: one shared hold per side across all its tunnels).
/// Teleport Nodes move units near one to another at once. Deterministic: store order, no RNG.</summary>
public static class TransportSystem
{
    /// <summary>How close a boarder must get to a holder: centre distance minus both radii.
    /// Ships sit off the shore, so boarding one from land reaches further.</summary>
    public static readonly Fix64 BoardReach = Fix64.Ratio(3, 2);
    public static readonly Fix64 ShipBoardReach = Fix64.FromInt(3);
    /// <summary>Units within this of an own Teleport Node can be sent to another node.</summary>
    public static readonly Fix64 TeleportReach = Fix64.FromInt(5);
    /// <summary>How far from a holder unloading passengers may be set down (cells).</summary>
    public const int UnloadRings = 5;

    /// <summary>Slots a passenger takes: infantry 1, any other ground unit 4.</summary>
    public static int Slots(Unit p) => p.Armor == ArmorClass.Infantry ? 1 : 4;

    /// <summary>Whether `h` can ever carry `p` (kind and side, not room).</summary>
    public static bool Accepts(Unit h, Unit p)
    {
        if (!h.Ability.Holds || h == p || h.Faction != p.Faction || p.Building != null || p.Ability.Holds) return false;
        if (p.Domain is MoveDomain.Air or MoveDomain.Naval) return false;
        if (h.Building is { ConstructionRemaining: > 0 }) return false;
        return h.Ability.Carries == "ground" || p.Armor == ArmorClass.Infantry;
    }

    /// <summary>Slots used in `h`: its own passengers, or for a tunnel every passenger in any of
    /// its side's tunnels.</summary>
    public static int Used(Unit h, UnitStore units)
    {
        var n = 0;
        foreach (var p in units.Units)
        {
            if (p.Inside == EntityId.None || p.Hp.Raw <= 0) continue;
            if (p.Inside == h.Id || (h.Ability.Tunnel && units.Find(p.Inside) is { Ability.Tunnel: true } t && t.Faction == h.Faction))
                n += Slots(p);
        }
        return n;
    }

    public static void Step(UnitStore units, Dictionary<EntityId, MoveOrder> orders, GameMap map,
        IReadOnlyList<Command> due, int tick, SimWorld? events = null)
    {
        foreach (var c in due)
            switch (c)
            {
                case LoadCommand l:
                    if (units.Find(l.Holder) is not { } holder || holder.Faction != l.Faction || holder.Hp.Raw <= 0) break;
                    foreach (var id in l.Units)
                        if (units.Find(id) is { } p && p.Faction == l.Faction && p.Hp.Raw > 0 && p.Inside == EntityId.None && Accepts(holder, p))
                        {
                            p.BoardTarget = holder.Id;
                            p.TargetId = EntityId.None;
                            p.AttackMoving = p.Guarding = p.Returning = false;
                            p.CaptureTarget = EntityId.None;
                            p.BridgeTarget = -1;
                            orders.Remove(p.Id);
                        }
                    break;
                case UnloadCommand ul:
                    if (units.Find(ul.Holder) is { } h && h.Faction == ul.Faction && h.Hp.Raw > 0) Unload(h, units, orders, map, tick, events);
                    break;
                case TeleportCommand tp:
                    Teleport(tp, units, orders, map, events);
                    break;
            }

        var any = false;
        foreach (var u in units.Units)
            if (u.Inside != EntityId.None || u.BoardTarget != EntityId.None) { any = true; break; }
        if (!any) return;

        // holders that died this tick: passengers die with them (a tunnel's move to another tunnel)
        foreach (var p in units.Units)
        {
            if (p.Inside == EntityId.None || p.Hp.Raw <= 0) continue;
            var h = units.Find(p.Inside);
            if (h is { Hp.Raw: > 0 } && h.Faction == p.Faction) continue;
            Unit? other = null;
            if (h is { Ability.Tunnel: true })
                foreach (var t in units.Units)
                    if (t.Ability.Tunnel && t.Hp.Raw > 0 && t.Faction == p.Faction && t.Building is { ConstructionRemaining: 0 }) { other = t; break; }
            if (other != null) { p.Inside = other.Id; continue; }
            p.Inside = EntityId.None;
            p.Hp = Fix64.Zero;
            events?.Emit(SimEventKind.Death, p.Id, h?.Id ?? EntityId.None, faction: p.Faction);
        }

        // boarders: walk up and climb in while there is room
        foreach (var p in units.Units)
        {
            if (p.BoardTarget == EntityId.None) continue;
            var h = units.Find(p.BoardTarget);
            if (p.Hp.Raw <= 0 || h is null || h.Hp.Raw <= 0 || !Accepts(h, p) || Used(h, units) + Slots(p) > h.Ability.Capacity)
            {
                p.BoardTarget = EntityId.None;
                orders.Remove(p.Id);
                continue;
            }
            if (Reached(p, h, map))
            {
                p.Inside = h.Id;
                p.BoardTarget = EntityId.None;
                orders.Remove(p.Id);
                continue;
            }
            if (!orders.TryGetValue(p.Id, out var o) || (o.Target - h.Position).Length > Fix64.One)
                orders[p.Id] = new MoveOrder(h.Position, new List<FixVec2>(), tick);
        }

        // passengers ride along, out of every other system's way
        foreach (var p in units.Units)
        {
            if (p.Inside == EntityId.None || units.Find(p.Inside) is not { } h) continue;
            p.Position = h.Position;
            p.Velocity = FixVec2.Zero;
            orders.Remove(p.Id);
            p.AttackMoving = p.Guarding = p.Returning = false;
            p.CaptureTarget = EntityId.None;
            p.BridgeTarget = -1;
            if (!h.Ability.Garrison) p.TargetId = EntityId.None;
        }
    }

    private static bool Reached(Unit p, Unit h, GameMap map)
    {
        if (h.Building != null && map.TryGetFootprint(h.Id.Value, out var r))
        {
            var cx = GameMap.CellOf(p.Position.X);
            var cy = GameMap.CellOf(p.Position.Y);
            return cx >= r.X - 1 && cx <= r.X + r.W && cy >= r.Y - 1 && cy <= r.Y + r.H;
        }
        var reach = h.Domain == MoveDomain.Naval ? ShipBoardReach : BoardReach;
        return (h.Position - p.Position).Length <= reach + p.Radius + h.Radius;
    }

    /// <summary>Set every passenger down on the nearest free cells it can stand on; any that
    /// find none (a ship far from shore) stay aboard.</summary>
    private static void Unload(Unit h, UnitStore units, Dictionary<EntityId, MoveOrder> orders, GameMap map, int tick, SimWorld? events)
    {
        var taken = new HashSet<(int, int)>();
        foreach (var p in units.Units)
        {
            if (p.Inside == EntityId.None || p.Hp.Raw <= 0) continue;
            var mine = p.Inside == h.Id
                || (h.Ability.Tunnel && units.Find(p.Inside) is { Ability.Tunnel: true } t && t.Faction == h.Faction);
            if (!mine) continue;
            if (FreeCell(map, units, h.Position, p.Domain, taken) is not { } cell) continue;
            taken.Add((GameMap.CellOf(cell.X), GameMap.CellOf(cell.Y)));
            var from = p.Inside;
            p.Inside = EntityId.None;
            p.Position = cell;
            p.TargetId = EntityId.None;
            orders.Remove(p.Id);
            if (from != h.Id) events?.Emit(SimEventKind.Teleported, p.Id, h.Id, faction: p.Faction); // out of another tunnel
        }
    }

    /// <summary>Teleport Nodes: every listed own unit within TeleportReach of another own node
    /// (finished, not sabotaged) appears beside node `tp.Node`.</summary>
    private static void Teleport(TeleportCommand tp, UnitStore units, Dictionary<EntityId, MoveOrder> orders, GameMap map, SimWorld? events)
    {
        if (units.Find(tp.Node) is not { } dest || !Node(dest, tp.Faction, tp.Tick)) return;
        var taken = new HashSet<(int, int)>();
        var r2 = TeleportReach * TeleportReach;
        foreach (var id in tp.Units)
        {
            if (units.Find(id) is not { } u || u.Faction != tp.Faction || u.Hp.Raw <= 0 || u.Building != null || u.Inside != EntityId.None) continue;
            if ((u.Position - dest.Position).LengthSquared <= r2) continue; // already there
            var near = false;
            foreach (var n in units.Units)
                if (n != dest && Node(n, tp.Faction, tp.Tick) && (u.Position - n.Position).LengthSquared <= r2) { near = true; break; }
            if (!near || FreeCell(map, units, dest.Position, u.Domain, taken) is not { } cell) continue;
            taken.Add((GameMap.CellOf(cell.X), GameMap.CellOf(cell.Y)));
            u.Position = cell;
            u.Velocity = FixVec2.Zero;
            orders.Remove(u.Id);
            u.TargetId = EntityId.None;
            u.Guarding = u.Returning = u.AttackMoving = false;
            events?.Emit(SimEventKind.Teleported, u.Id, dest.Id, faction: u.Faction);
        }
    }

    private static bool Node(Unit n, int faction, int tick) =>
        n.Ability.Teleport && n.Faction == faction && n.Hp.Raw > 0 && n.Building is { ConstructionRemaining: 0 } && !n.IsSabotaged(tick);

    /// <summary>The nearest cell centre within UnloadRings of `at` that `domain` can stand on and
    /// nobody stands on (ring scan order breaks ties); null when there is none.</summary>
    public static FixVec2? FreeCell(GameMap map, UnitStore units, FixVec2 at, MoveDomain domain, HashSet<(int, int)> taken)
    {
        var bx = GameMap.CellOf(at.X);
        var by = GameMap.CellOf(at.Y);
        for (var r = 0; r <= UnloadRings; r++)
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    var (cx, cy) = (bx + dx, by + dy);
                    if (!map.IsWalkable(cx, cy, domain) || taken.Contains((cx, cy))) continue;
                    var occupied = false;
                    foreach (var u in units.Units)
                        if (u.Inside == EntityId.None && u.Building == null && u.Hp.Raw > 0
                            && GameMap.CellOf(u.Position.X) == cx && GameMap.CellOf(u.Position.Y) == cy) { occupied = true; break; }
                    if (!occupied) return GameMap.CellCenter(cx, cy);
                }
        return null;
    }
}
