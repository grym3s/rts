using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>T26, runs right after CombatSystem in tick step 7: units with a bridge job
/// (`Unit.BridgeTarget`, set by a `bridge` command) work on it. Armed units that can hit ground
/// walk into range and shoot it (damage x the Structure column, floor 1); at 0 hp it collapses:
/// its cells turn to water (ships still pass under), paths across it re-plan, and land units
/// standing on it die. Engineers walk up to a damaged or fallen bridge and repair it to full in
/// one touch, and are consumed. Deterministic: store order, no RNG, no floats.</summary>
public static class BridgeSystem
{
    /// <summary>How close an engineer must get (cell-centre distance, plus its radius).</summary>
    public static readonly Fix64 RepairReach = Fix64.Ratio(3, 2);

    public static void Step(BridgeStore bridges, GameMap map, UnitStore units,
        Dictionary<EntityId, MoveOrder> orders, int tick, SimWorld? events = null)
    {
        if (bridges.Bridges.Count == 0) return;
        foreach (var u in units.Units)
        {
            if (u.BridgeTarget < 0 || u.Hp.Raw <= 0) continue;
            if (u.TargetId != EntityId.None) continue; // fighting a unit first (guard / explicit)
            var b = bridges.Get(u.BridgeTarget);
            if (b == null) { u.BridgeTarget = -1; continue; }
            var aim = b.NearestCell(u.Position);
            var dist = (aim - u.Position).Length;

            if (u.Engineer)
            {
                if (!b.Damaged) { Done(u, b, orders); continue; }
                if (dist <= RepairReach + u.Radius)
                {
                    Repair(b, map);
                    u.Hp = Fix64.Zero; // consumed: leaves the roster at cleanup
                    u.BridgeTarget = -1;
                    orders.Remove(u.Id);
                    events?.Emit(SimEventKind.BridgeRepaired, new EntityId(b.Id), u.Id);
                    continue;
                }
                Chase(u, b, aim, orders, tick);
                continue;
            }

            if (b.Broken || u.Damage.Raw == 0 || (u.Targets & TargetMask.Ground) == 0) { Done(u, b, orders); continue; }
            if (dist <= u.Range + u.Radius + Fix64.Half)
            {
                orders.Remove(u.Id); // in range: stand and fire
                if (u.CooldownRemaining != 0) continue;
                var applied = u.Damage * DamageMatrix.Get(u.DamageType, ArmorClass.Structure);
                if (applied.Raw < Fix64.OneRaw) applied = Fix64.One;
                b.Hp -= applied;
                u.CooldownRemaining = u.CooldownTicks;
                events?.Emit(SimEventKind.BridgeHit, u.Id, new EntityId(b.Id), applied);
                if (b.Hp.Raw <= 0) Collapse(b, map, units, u, events);
            }
            else if (u.Speed.Raw != 0)
                Chase(u, b, aim, orders, tick);
            else
                u.BridgeTarget = -1; // a static gun can't reach it
        }
    }

    /// <summary>Job over (bridge down, mended, or not this unit's to do): drop it and any walk to it.</summary>
    private static void Done(Unit u, Bridge b, Dictionary<EntityId, MoveOrder> orders)
    {
        u.BridgeTarget = -1;
        if (orders.TryGetValue(u.Id, out var o) && b.Covers(GameMap.CellOf(o.Target.X), GameMap.CellOf(o.Target.Y)))
            orders.Remove(u.Id);
    }

    /// <summary>Walk toward the bridge unless already walking to one of its cells.</summary>
    private static void Chase(Unit u, Bridge b, FixVec2 aim, Dictionary<EntityId, MoveOrder> orders, int tick)
    {
        if (orders.TryGetValue(u.Id, out var o) && b.Covers(GameMap.CellOf(o.Target.X), GameMap.CellOf(o.Target.Y))) return;
        orders[u.Id] = new MoveOrder(aim, new List<FixVec2>(), tick);
    }

    private static void Collapse(Bridge b, GameMap map, UnitStore units, Unit by, SimWorld? events)
    {
        b.Hp = Fix64.Zero;
        b.Broken = true;
        map.SetBridgeIntact(b.Cells, false, b.IntactTerrain);
        events?.Emit(SimEventKind.BridgeDown, new EntityId(b.Id), by.Id);
        foreach (var v in units.Units)
        {
            if (v.BridgeTarget == b.Id && !v.Engineer) v.BridgeTarget = -1; // nothing left to shoot
            if (v.Hp.Raw <= 0 || v.Domain != MoveDomain.Land) continue;
            if (!b.Covers(GameMap.CellOf(v.Position.X), GameMap.CellOf(v.Position.Y))) continue;
            v.Hp = Fix64.Zero; // fell with it
            events?.Emit(SimEventKind.Death, v.Id, by.Id, faction: v.Faction);
        }
    }

    private static void Repair(Bridge b, GameMap map)
    {
        if (b.Broken) map.SetBridgeIntact(b.Cells, true, b.IntactTerrain);
        b.Broken = false;
        b.Hp = b.MaxHp;
    }
}
