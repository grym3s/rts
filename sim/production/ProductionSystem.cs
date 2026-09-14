using Rts.Sim.Core;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.Production;

/// <summary>Tick step 2 (economy step, docs/factions/economy.md): production commands,
/// construction countdown, queue countdown, completion spawns. Buildings are raidable
/// Units; their state hangs on Unit.Building. Deterministic: store order, integer
/// ticks/credits, no RNG. Money moves only through the passed spend/refund callbacks
/// (composition wires EconomyStore.TrySpend / Deposit); id source and map likewise
/// explicit — no globals, several worlds may live in one process.</summary>
public static class ProductionSystem
{
    public delegate bool TryOp(int faction, int amount);

    /// <summary>`due` = this tick's commands (the three production ones are handled here,
    /// others ignored). Call AFTER OrderSystem.ApplyCommands in the same step-1 slot.</summary>
    public static void Step(
        BuildingCatalog buildings, UnitCatalog unitsCatalog,
        UnitStore units, Rts.Sim.World.GameMap map,
        EntityIdAllocator ids,
        IReadOnlyList<Command> due,
        TryOp spend, TryOp refund)
    {
        foreach (var c in due)
            switch (c)
            {
                case PlaceBuildingCommand p: Place(buildings, units, map, ids, p, spend); break;
                case TrainCommand t: Train(unitsCatalog, units, t, spend); break;
                case CancelProductionCommand x: Cancel(unitsCatalog, units, x, refund); break;
            }

        // progress: snapshot the roster — completions append units, must not iterate live
        foreach (var u in units.Units.ToList())
        {
            if (u.Building is not { } b) continue;
            if (u.Hp.Raw <= 0)
            {
                // died (raid!) — queued money returns once, then cleanup claims the husk
                foreach (var q in b.Queue) refund(u.Faction, unitsCatalog.Get(q).Cost);
                b.Queue.Clear();
                b.QueueHeadRemaining = 0;
                continue;
            }
            if (b.ConstructionRemaining > 0) { b.ConstructionRemaining--; continue; }
            if (b.Queue.Count == 0) continue;
            if (--b.QueueHeadRemaining > 0) continue;

            var unitId = b.Queue[0];
            b.Queue.RemoveAt(0);
            var p = unitsCatalog.Get(unitId);
            var cell = ProductionRules.FindSpawnCell(map, units, u.Position);
            units.Spawn(ids.Next(), cell, p.Speed, p.Radius,
                new UnitProfile(p.Faction, p.Hp, p.Sight, p.Damage, p.Range, p.CooldownTicks, p.Armor, p.DamageType, p.Harvest));
            if (b.Queue.Count > 0) b.QueueHeadRemaining = unitsCatalog.Get(b.Queue[0]).BuildTicks;
        }
    }

    private static void Place(BuildingCatalog buildings, UnitStore units, Rts.Sim.World.GameMap map,
        EntityIdAllocator ids, PlaceBuildingCommand p, TryOp spend)
    {
        var bp = buildings.Get(p.BuildingId);
        if (bp.Faction != p.Faction) return;
        if (!ProductionRules.PrereqSatisfied(buildings, p.BuildingId,
                req => units.Units.Any(u => u.Faction == p.Faction && u.Hp.Raw > 0 && u.Building?.Id == req)))
            return;
        var cellX = Cell(p.Cell.X);
        var cellY = Cell(p.Cell.Y);
        if (!map.IsWalkable(cellX, cellY)) return;
        if (units.Units.Any(u => Cell(u.Position.X) == cellX && Cell(u.Position.Y) == cellY)) return;
        if (!spend(p.Faction, bp.Cost)) return; // money last: every earlier failure costs nothing

        var b = units.Spawn(ids.Next(), p.Cell, Fix64.Zero, UnitCatalog.DefaultRadius,
            new UnitProfile(p.Faction, bp.Hp, Fix64.Zero, Fix64.Zero, Fix64.Zero, 0, ArmorClass.Structure, DamageType.SmallArms));
        b.Building = new BuildingState { Id = p.BuildingId, ConstructionRemaining = bp.BuildTicks };
    }

    private static void Train(UnitCatalog unitsCatalog, UnitStore units, TrainCommand t, TryOp spend)
    {
        if (units.Find(t.BuildingId) is not { Building: { } b } bu) return;
        if (bu.Faction != t.Faction || bu.Hp.Raw <= 0 || b.ConstructionRemaining > 0) return;
        if (b.Queue.Count >= ProductionStore.MaxQueue) return;
        if (!unitsCatalog.Builds(b.Id, t.UnitId)) return;
        var p = unitsCatalog.Get(t.UnitId);
        if (p.Faction != t.Faction) return;
        if (!spend(t.Faction, p.Cost)) return;
        var startNow = b.Queue.Count == 0;
        b.Queue.Add(t.UnitId);
        if (startNow) b.QueueHeadRemaining = p.BuildTicks;
    }

    private static void Cancel(UnitCatalog unitsCatalog, UnitStore units, CancelProductionCommand x, TryOp refund)
    {
        if (units.Find(x.BuildingId) is not { Building: { } b } bu) return;
        if (bu.Faction != x.Faction) return;
        if (x.Index < 0 || x.Index >= b.Queue.Count) return;
        refund(x.Faction, unitsCatalog.Get(b.Queue[x.Index]).Cost);
        b.Queue.RemoveAt(x.Index);
        b.QueueHeadRemaining = x.Index == 0 && b.Queue.Count > 0
            ? unitsCatalog.Get(b.Queue[0]).BuildTicks : b.QueueHeadRemaining;
    }

    private static int Cell(Fix64 v) => (int)(v.Raw >> Fix64.FractionBits);
}
