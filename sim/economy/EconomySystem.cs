using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;

namespace Rts.Sim.Economy;

/// <summary>Tick step 2 (see ../CONTEXT.md): harvest income — fill, round-trip, deposit.
/// Semantics ported byte-exact from tools/refmodel_economy.py (D-001). Deterministic:
/// store order, integer credits, no RNG, no floats. Movement itself is navigation's job;
/// this system only issues MoveOrders (same pattern as CombatSystem chase).</summary>
public static class EconomySystem
{
    // canon tuning (docs/factions/economy.md harvest loop table)
    public const int Capacity = 700;        // Collector load per trip
    public const int FillTicks = 140;       // ~7s at 20 tps
    public const int Rate = Capacity / FillTicks; // 5 cr/t; canon requires exact division

    // engagement radii (tuning values until content grows them)
    public static readonly Fix64 FillRadius = Fix64.Ratio(3, 2);    // 1.5 cells of a vein
    public static readonly Fix64 DepositRadius = Fix64.FromInt(2);      // 2 cells of a refinery

    public static void Step(EconomyStore store, UnitStore units, Dictionary<EntityId, MoveOrder> orders, int tick)
    {
        foreach (var u in units.Units)
        {
            if (!u.Harvest || u.Hp.Raw <= 0) continue;

            // carrying a full load: deposit on arrival at the nearest refinery, else head there
            if (u.Carrying)
            {
                var ref_ = NearestRefinery(store, u);
                if (ref_ == null) continue; // no refinery: hold the load (bank on unit)
                if (Dist(u, ref_.Position) <= DepositRadius)
                {
                    store.Deposit(u.Faction, u.Load);
                    u.Load = 0;
                    u.Carrying = false;
                    // resume to home vein if we still have one worth returning to
                    var home = store.FindVein(u.HomeVein);
                    if (home != null && home.Pool > 0)
                        orders[u.Id] = new MoveOrder(home.Position, new List<FixVec2>(), tick);
                    else
                        orders.Remove(u.Id);
                }
                else if (!HasOrderTo(orders, u.Id, ref_.Position))
                    orders[u.Id] = new MoveOrder(ref_.Position, new List<FixVec2>(), tick);
                continue;
            }

            // filling: pick the nearest live vein within radius (list order breaks ties)
            var vein = NearestLiveVein(store, u);
            if (vein == null)
            {
                // full but not carrying yet, with no vein in reach: start the trip
                if (u.Load >= Capacity) u.Carrying = true;
                continue;
            }
            u.HomeVein = vein.Id;
            var take = Math.Min(Rate, Math.Min(Capacity - u.Load, vein.Pool));
            u.Load += take;
            vein.Pool -= take;
            if (u.Load >= Capacity) u.Carrying = true;
        }
    }

    private static Fix64 Dist(Unit u, FixVec2 p) => (p - u.Position).Length;

    private static bool HasOrderTo(Dictionary<EntityId, MoveOrder> orders, EntityId id, FixVec2 target) =>
        orders.TryGetValue(id, out var o) && o.Target == target;

    private static EconomyStore.Refinery? NearestRefinery(EconomyStore store, Unit u)
    {
        EconomyStore.Refinery? best = null;
        var bestD = Fix64.Zero;
        foreach (var r in store.Refineries)
        {
            if (r.Faction != u.Faction) continue;
            var d = Dist(u, r.Position);
            if (best == null || d < bestD) { best = r; bestD = d; }
        }
        return best;
    }

    private static EconomyStore.Vein? NearestLiveVein(EconomyStore store, Unit u)
    {
        EconomyStore.Vein? best = null;
        var bestD = Fix64.Zero;
        foreach (var v in store.Veins)
        {
            if (v.Pool <= 0) continue;
            var d = Dist(u, v.Position);
            if (d > FillRadius) continue;
            if (best == null || d < bestD) { best = v; bestD = d; }
        }
        return best;
    }
}
