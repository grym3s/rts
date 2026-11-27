using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.Combat;

/// <summary>Tick step 7, after captures: carriers (Vanguard, Bastion Carrier, Nest) keep a wing of
/// their own aircraft (`Abilities.Launches`, up to `Wing`) in the air, launching one every
/// `RebuildTicks` while short. The aircraft are ordinary units of the carrier's side tagged with
/// `CarrierId`: they take the carrier's attack target, else the nearest enemy within `LaunchRange`
/// of the carrier, never chase beyond it, fly back to it when idle, and are lost with it.
/// Combat does the shooting. The Nest's difference is its fast rebuild (no rearm loop is modelled
/// for anyone). Deterministic: store order, no RNG, no floats.</summary>
public static class Carriers
{
    /// <summary>An idle aircraft further than this from its carrier flies back to it.</summary>
    public static readonly Fix64 HomeRadius = Fix64.FromInt(2);

    public static void Step(UnitStore units, Dictionary<EntityId, MoveOrder> orders, UnitCatalog catalog, GameMap map,
        EntityIdAllocator ids, int tick, SimWorld? events = null)
    {
        List<Unit>? carriers = null;
        foreach (var u in units.Units)
            if (u.Ability.IsCarrier) (carriers ??= new()).Add(u);
        var hasCraft = false;
        foreach (var u in units.Units)
            if (u.CarrierId != EntityId.None) { hasCraft = true; break; }
        if (carriers == null && !hasCraft) return;

        // aircraft: lost with their carrier, else fight within its reach or fly home
        foreach (var a in units.Units.ToList())
        {
            if (a.CarrierId == EntityId.None || a.Hp.Raw <= 0) continue;
            var c = units.Find(a.CarrierId);
            if (c is null || c.Hp.Raw <= 0 || c.Faction != a.Faction)
            {
                a.Hp = Fix64.Zero; // nowhere to land
                events?.Emit(SimEventKind.Death, a.Id, EntityId.None, faction: a.Faction);
                continue;
            }
            var reach = c.Ability.LaunchRange;
            var t = a.TargetId != EntityId.None ? units.Find(a.TargetId) : null;
            if (t != null && (t.Hp.Raw <= 0 || (t.Position - c.Position).Length > reach)) { a.TargetId = EntityId.None; t = null; }
            // the carrier's own attack order comes first
            var focus = c.TargetId != EntityId.None ? units.Find(c.TargetId) : null;
            if (focus is { Hp.Raw: > 0 } && focus.Faction != a.Faction && CombatSystem.CanHit(a, focus)
                && Support.CanSee(a.Faction, focus, tick) && (focus.Position - c.Position).Length <= reach)
                t = focus;
            t ??= Nearest(a, c, reach, units, tick);
            if (t != null)
            {
                a.TargetId = t.Id;
                a.Guarding = a.Returning = a.AttackMoving = false;
                continue;
            }
            a.TargetId = EntityId.None;
            a.Guarding = a.Returning = false;
            if ((a.Position - c.Position).Length > HomeRadius && (!orders.TryGetValue(a.Id, out var o) || (o.Target - c.Position).Length > Fix64.One))
                orders[a.Id] = new MoveOrder(c.Position, new List<FixVec2>(), tick);
        }

        if (carriers == null) return;
        foreach (var c in carriers)
        {
            if (c.Hp.Raw <= 0 || c.Inside != EntityId.None) continue;
            var wing = 0;
            foreach (var a in units.Units)
                if (a.CarrierId == c.Id && a.Hp.Raw > 0) wing++;
            if (wing >= c.Ability.Wing) { c.RebuildLeft = 0; continue; }
            if (c.RebuildLeft == 0) { c.RebuildLeft = Math.Max(1, c.Ability.RebuildTicks); continue; }
            if (--c.RebuildLeft > 0) continue;
            var p = catalog.Get(c.Ability.Launches!);
            var craft = units.Spawn(ids.Next(), c.Position, p.Speed, p.Radius, p.Profile() with { Faction = c.Faction });
            craft.CarrierId = c.Id;
        }
    }

    private static Unit? Nearest(Unit a, Unit c, Fix64 reach, UnitStore units, int tick)
    {
        Unit? best = null;
        var bestD = Fix64.Zero;
        var r2 = reach * reach;
        foreach (var e in units.Units)
        {
            if (e.Faction == a.Faction || e.Faction == Factions.Neutral || e.Hp.Raw <= 0 || e.Inside != EntityId.None) continue;
            if (!CombatSystem.CanHit(a, e) || e.IsPhased(tick) || !Support.CanSee(a.Faction, e, tick)) continue;
            if ((e.Position - c.Position).LengthSquared > r2) continue;
            var d = (e.Position - a.Position).LengthSquared;
            if (best == null || d < bestD) { best = e; bestD = d; }
        }
        return best;
    }
}
