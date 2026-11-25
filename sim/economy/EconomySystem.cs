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

    [Obsolete("Use IncomeStep + DepositStep at their canonical tick positions (2 and 6). " +
              "Step couples deposit-proximity to pre-move positions and income to post-spend order.")]
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

    /// <summary>How far an idle harvester looks for a live vein when its own is gone (T33, cells;
    /// tuning). A fresh harvester (shipped by a refinery) uses it to find its first field.</summary>
    public static readonly Fix64 SeekRadius = Fix64.FromInt(12);

    /// <summary>Income pass (canonical tick position 2): fill at veins, flip to carrying,
    /// issue head-to-refinery orders. Runs BEFORE OrderSystem so a SpendCommand due this
    /// tick sees this tick's income. Deposit-on-arrival lives in DepositStep, which must
    /// run AFTER NavigationSystem so proximity reads post-move positions.
    /// T33: with `map` + `isRefinery`, completed live refinery buildings of the harvester's
    /// faction are drop-offs too (nearest wins; scenario points first on ties), and an idle
    /// harvester out of reach of any vein walks back to its vein or the nearest live one.
    /// T38: `due` carries this tick's HarvestCommands, applied first.</summary>
    public static void IncomeStep(EconomyStore store, UnitStore units, Dictionary<EntityId, MoveOrder> orders, int tick,
        GameMap? map = null, Func<string, bool>? isRefinery = null, IReadOnlyList<Command>? due = null)
    {
        if (due != null)
            foreach (var c in due)
                if (c is HarvestCommand h) Harvest(store, units, orders, h, tick);

        foreach (var u in units.Units)
        {
            if (!u.Harvest || u.Hp.Raw <= 0) continue;

            if (u.Carrying)
            {
                var drop = NearestDropOff(store, units, u, isRefinery);
                if (drop == null) continue; // no refinery: hold the load (bank on unit)
                if (!AtDropOff(u, drop.Value, map) && !HasOrderTo(orders, u.Id, drop.Value.Target))
                    orders[u.Id] = new MoveOrder(drop.Value.Target, new List<FixVec2>(), tick);
                continue;
            }

            var vein = NearestLiveVein(store, u);
            if (vein == null)
            {
                if (u.Load >= Capacity) { u.Carrying = true; continue; }
                if (map != null && !orders.ContainsKey(u.Id)) SeekVein(store, u, orders, tick);
                continue;
            }
            // T38: walking to a live home vein (sent there by a harvest command, or back after a
            // deposit), it fills only at home, not at whatever vein it passes on the way.
            if (vein.Id != u.HomeVein && store.FindVein(u.HomeVein) is { Pool: > 0 } homeVein
                && orders.TryGetValue(u.Id, out var going) && going.Target == homeVein.Position) continue;
            u.HomeVein = vein.Id;
            var take = Math.Min(Rate, Math.Min(Capacity - u.Load, vein.Pool));
            u.Load += take;
            vein.Pool -= take;
            if (u.Load >= Capacity) u.Carrying = true;
        }
    }

    /// <summary>Deposit pass (canonical tick position 6, after NavigationSystem): credits
    /// a carrying unit that has arrived at a drop-off, using post-move positions: within
    /// DepositRadius of a scenario point, or (T33) standing next to a refinery's footprint.
    /// Paired with IncomeStep; do not call from the same tick position.</summary>
    public static void DepositStep(EconomyStore store, UnitStore units, Dictionary<EntityId, MoveOrder> orders, int tick,
        GameMap? map = null, Func<string, bool>? isRefinery = null)
    {
        foreach (var u in units.Units)
        {
            if (!u.Harvest || u.Hp.Raw <= 0 || !u.Carrying) continue;
            var drop = NearestDropOff(store, units, u, isRefinery);
            if (drop == null) continue; // no refinery: hold the load (bank on unit)
            if (!AtDropOff(u, drop.Value, map)) continue;
            store.Deposit(u.Faction, u.Load);
            u.Load = 0;
            u.Carrying = false;
            var home = store.FindVein(u.HomeVein);
            if (home != null && home.Pool > 0)
                orders[u.Id] = new MoveOrder(home.Position, new List<FixVec2>(), tick);
            else
                orders.Remove(u.Id);
        }
    }

    /// <summary>T38: point own live harvesters at a live vein (see HarvestCommand).</summary>
    private static void Harvest(EconomyStore store, UnitStore units, Dictionary<EntityId, MoveOrder> orders, HarvestCommand h, int tick)
    {
        var vein = store.FindVein(h.Vein);
        if (vein is not { Pool: > 0 }) return;
        foreach (var id in h.Units)
        {
            if (units.Find(id) is not { Harvest: true } u || u.Faction != h.Faction || u.Hp.Raw <= 0) continue;
            u.HomeVein = vein.Id;
            if (!u.Carrying) orders[u.Id] = new MoveOrder(vein.Position, new List<FixVec2>(), tick);
        }
    }

    /// <summary>Idle, empty-handed, nowhere to fill: go back to the home vein while it lasts,
    /// else to the nearest live vein within SeekRadius (list order breaks ties). Nothing in
    /// reach and something in the hold: take the partial load home.</summary>
    private static void SeekVein(EconomyStore store, Unit u, Dictionary<EntityId, MoveOrder> orders, int tick)
    {
        var home = store.FindVein(u.HomeVein);
        EconomyStore.Vein? target = home is { Pool: > 0 } ? home : null;
        if (target == null)
        {
            var bestD = Fix64.Zero;
            foreach (var v in store.Veins)
            {
                if (v.Pool <= 0) continue;
                var d = Dist(u, v.Position);
                if (d > SeekRadius) continue;
                if (target == null || d < bestD) { target = v; bestD = d; }
            }
        }
        if (target != null)
        {
            u.HomeVein = target.Id;
            orders[u.Id] = new MoveOrder(target.Position, new List<FixVec2>(), tick);
        }
        else if (u.Load > 0) u.Carrying = true;
    }

    /// <summary>A drop-off: a scenario point (Building = -1) or a refinery building (its id).</summary>
    private readonly record struct DropOff(FixVec2 Target, int Building);

    private static DropOff? NearestDropOff(EconomyStore store, UnitStore units, Unit u, Func<string, bool>? isRefinery)
    {
        DropOff? best = null;
        var bestD = Fix64.Zero;
        foreach (var r in store.Refineries)
        {
            if (r.Faction != u.Faction) continue;
            var d = Dist(u, r.Position);
            if (best == null || d < bestD) { best = new DropOff(r.Position, -1); bestD = d; }
        }
        if (isRefinery == null) return best;
        foreach (var b in units.Units)
        {
            if (b.Faction != u.Faction || b.Hp.Raw <= 0 || b.Building is not { ConstructionRemaining: 0 } bs || !isRefinery(bs.Id)) continue;
            var d = Dist(u, b.Position);
            if (best == null || d < bestD) { best = new DropOff(b.Position, b.Id.Value); bestD = d; }
        }
        return best;
    }

    /// <summary>Scenario point: within DepositRadius. Refinery building: the harvester's cell
    /// touches the footprint (Chebyshev 1, diagonals count), where a path to it ends.</summary>
    private static bool AtDropOff(Unit u, DropOff drop, GameMap? map)
    {
        if (drop.Building < 0 || map == null || !map.TryGetFootprint(drop.Building, out var r))
            return Dist(u, drop.Target) <= DepositRadius;
        var cx = GameMap.CellOf(u.Position.X);
        var cy = GameMap.CellOf(u.Position.Y);
        return cx >= r.X - 1 && cx <= r.X + r.W && cy >= r.Y - 1 && cy <= r.Y + r.H;
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
