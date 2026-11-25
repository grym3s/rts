using Rts.Sim.Core;
using Rts.Sim.Power;
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
        TryOp spend, TryOp refund, PowerStore? power, int tick)
    {
        // T31: destroyed buildings were despawned last tick (cleanup); free their footprints
        // before anything places or paths this tick.
        map.ReleaseStructures(owner => units.Find(new EntityId(owner)) is not { } bu || bu.Hp.Raw <= 0);

        foreach (var c in due)
            switch (c)
            {
                case PlaceBuildingCommand p: Place(buildings, units, map, ids, p, spend); break;
                case TrainCommand t: Train(unitsCatalog, units, t, spend, tick); break;
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
            // Power gate: while net < 0, production countdown ticks only when
            // tick mod 2 != building id mod 2 (integer parity gate, halving without fractions).
            // Phase fixed to match golden: gated completion at cmdTick+240, ungated at cmdTick+120.
            // Recovery immediate at net >= 0, no hysteresis.
            if (power is not null && power.IsLowPower(u.Faction))
            {
                if (!GateOpen(tick, u.Id)) continue;
            }
            if (b.ConstructionRemaining > 0) { b.ConstructionRemaining--; continue; }
            if (b.Queue.Count == 0) continue;
            // Countdown starts the tick AFTER the command: skip the decrement on the enqueue tick.
            if (b.QueueEnqueuedAtTick == tick) continue;
            if (--b.QueueHeadRemaining > 0) continue;

            var unitId = b.Queue[0];
            b.Queue.RemoveAt(0);
            var p = unitsCatalog.Get(unitId);
            var cell = ProductionRules.FindSpawnCell(map, units, u.Position);
            units.Spawn(ids.Next(), cell, p.Speed, p.Radius,
                new UnitProfile(p.Faction, p.Hp, p.Sight, p.Damage, p.Range, p.CooldownTicks, p.Armor, p.DamageType, p.Harvest));
            if (b.Queue.Count > 0) { b.QueueHeadRemaining = unitsCatalog.Get(b.Queue[0]).BuildTicks; b.QueueEnqueuedAtTick = tick; }
        }
    }

    /// <summary>Parity gate: open when tick mod 2 != building id mod 2.
    /// Phase fixed to match golden (CARD 20): gated completion lands at cmdTick+240,
    /// ungated at cmdTick+120. The gate is per-building and per-tick; we use the
    /// building's entity id as the parity key. The gate alternates per tick.</summary>
    private static bool GateOpen(int tick, EntityId buildingId)
    {
        // The gate is open when (tick mod 2) != (building id mod 2).
        return (tick & 1) != (buildingId.Value & 1);
    }

    private static void Place(BuildingCatalog buildings, UnitStore units, Rts.Sim.World.GameMap map,
        EntityIdAllocator ids, PlaceBuildingCommand p, TryOp spend)
    {
        var bp = buildings.Get(p.BuildingId);
        if (bp.Faction != p.Faction) return;
        if (!ProductionRules.PrereqSatisfied(buildings, p.BuildingId,
                req => units.Units.Any(u => u.Faction == p.Faction && u.Hp.Raw > 0 && u.Building?.Id == req)))
            return;
        // T31: the whole footprint must be in bounds, on open terrain, free of other buildings
        // and of units (a unit standing there would be sealed in).
        var (ox, oy) = BuildingCatalog.FootprintOrigin(Cell(p.Cell.X), Cell(p.Cell.Y), bp.FootprintW, bp.FootprintH);
        if (!map.CanOccupy(ox, oy, bp.FootprintW, bp.FootprintH)) return;
        if (units.Units.Any(u => u.Hp.Raw > 0
                && Cell(u.Position.X) >= ox && Cell(u.Position.X) < ox + bp.FootprintW
                && Cell(u.Position.Y) >= oy && Cell(u.Position.Y) < oy + bp.FootprintH)) return;
        if (!spend(p.Faction, bp.Cost)) return; // money last: every earlier failure costs nothing

        // The building stands at its footprint's centre (combat ranges and spawns measure from it).
        var centre = new FixVec2(Fix64.FromInt(ox) + Fix64.Ratio(bp.FootprintW, 2), Fix64.FromInt(oy) + Fix64.Ratio(bp.FootprintH, 2));
        var b = units.Spawn(ids.Next(), centre, Fix64.Zero, UnitCatalog.DefaultRadius,
            new UnitProfile(p.Faction, bp.Hp, Fix64.Zero, Fix64.Zero, Fix64.Zero, 0, ArmorClass.Structure, DamageType.SmallArms));
        b.Building = new BuildingState { Id = p.BuildingId, ConstructionRemaining = bp.BuildTicks };
        map.OccupyStructure(b.Id.Value, ox, oy, bp.FootprintW, bp.FootprintH);
    }

    private static void Train(UnitCatalog unitsCatalog, UnitStore units, TrainCommand t, TryOp spend, int tick)
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
        if (startNow) { b.QueueHeadRemaining = p.BuildTicks; b.QueueEnqueuedAtTick = tick; }
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
