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

    /// <summary>`due` = this tick's commands (the production ones are handled here,
    /// others ignored). Call AFTER OrderSystem.ApplyCommands in the same step-1 slot.</summary>
    public static void Step(
        BuildingCatalog buildings, UnitCatalog unitsCatalog,
        UnitStore units, Rts.Sim.World.GameMap map,
        EntityIdAllocator ids,
        IReadOnlyList<Command> due,
        TryOp spend, TryOp refund, PowerStore? power, int tick,
        Action<EntityId, FixVec2>? rallyTo = null, SimWorld? events = null)
    {
        // T31: destroyed buildings were despawned last tick (cleanup); free their footprints
        // before anything places or paths this tick.
        map.ReleaseStructures(owner => units.Find(new EntityId(owner)) is not { } bu || bu.Hp.Raw <= 0);

        foreach (var c in due)
            switch (c)
            {
                case PlaceBuildingCommand p: Place(buildings, units, map, ids, p, spend); break;
                case TrainCommand t: Train(unitsCatalog, units, t, spend, tick); break;
                case DeployCommand d: Deploy(buildings, units, map, ids, d); break;
                case CancelProductionCommand x: Cancel(unitsCatalog, units, x, refund); break;
                case RallyCommand r: Rally(units, r); break;
                case ConscriptCommand k: Conscript(buildings, unitsCatalog, units, k, refund, events); break;
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
            if (b.ConstructionRemaining > 0)
            {
                // T33: canon "the Refinery ships one harvester on completion" (economy.md), free.
                if (--b.ConstructionRemaining == 0 && unitsCatalog.HarvesterFor(b.Id) is { } harvester)
                    SpawnUnit(unitsCatalog.Get(harvester), map, units, ids, u.Position, u.Faction);
                continue;
            }
            if (b.Queue.Count == 0) continue;
            // Countdown starts the tick AFTER the command: skip the decrement on the enqueue tick.
            if (b.QueueEnqueuedAtTick == tick) continue;
            if (--b.QueueHeadRemaining > 0) continue;

            var unitId = b.Queue[0];
            b.Queue.RemoveAt(0);
            var trained = SpawnUnit(unitsCatalog.Get(unitId), map, units, ids, u.Position, u.Faction);
            if (b.Rally is { } rally) rallyTo?.Invoke(trained.Id, rally); // T37: walk to the rally point
            if (b.Queue.Count > 0) { b.QueueHeadRemaining = unitsCatalog.Get(b.Queue[0]).BuildTicks; b.QueueEnqueuedAtTick = tick; }
        }
    }

    /// <summary>T28: the unit belongs to the building's owner, whoever built the building type
    /// (a captured enemy barracks fields its enemy units for its new owner).</summary>
    private static Unit SpawnUnit(UnitCatalog.SpawnParams p, Rts.Sim.World.GameMap map, UnitStore units, EntityIdAllocator ids, FixVec2 at, int owner)
    {
        var cell = ProductionRules.FindSpawnCell(map, units, at, p.Domain);
        return units.Spawn(ids.Next(), cell, p.Speed, p.Radius, p.Profile() with { Faction = owner });
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

    /// <summary>Why a placement would be refused, in the order `place` checks (T36: the host's
    /// read-only `query` answers with this so the build ghost never re-implements the rules).</summary>
    public enum PlaceVerdict { Ok, WrongFaction, Prereq, Blocked, Occupied, Radius, Credits, Water, Shore }

    /// <summary>The `place` rules as a pure check: faction, prereq chain, footprint in bounds on
    /// open terrain with no building or ore (Blocked) and no live unit (Occupied), build radius
    /// (not for a construction yard), then credits. Also returns the footprint rect.</summary>
    public static (PlaceVerdict Verdict, int X, int Y, int W, int H) CheckPlace(BuildingCatalog buildings, UnitStore units,
        Rts.Sim.World.GameMap map, int faction, string buildingId, FixVec2 at, int credits)
    {
        var bp = buildings.Get(buildingId);
        var (ox, oy) = BuildingCatalog.FootprintOrigin(Cell(at.X), Cell(at.Y), bp.FootprintW, bp.FootprintH);
        (PlaceVerdict, int, int, int, int) R(PlaceVerdict v) => (v, ox, oy, bp.FootprintW, bp.FootprintH);
        if (bp.Faction != faction && !HasTree(buildings, units, faction, bp.Faction)) return R(PlaceVerdict.WrongFaction);
        if (!ProductionRules.PrereqSatisfied(buildings, buildingId,
                req => units.Units.Any(u => u.Faction == faction && u.Hp.Raw > 0 && u.Building?.Id == req)))
            return R(PlaceVerdict.Prereq);
        // T25: naval/offshore buildings stand wholly on water; `requiresShore` ones need the
        // other ground beside them (a shipyard touches land, a coastal gun touches water).
        if (bp.OnWater && !map.AllTerrain(ox, oy, bp.FootprintW, bp.FootprintH, Rts.Sim.World.Terrain.Water)) return R(PlaceVerdict.Water);
        if (bp.RequiresShore && !map.RingTouches(ox, oy, bp.FootprintW, bp.FootprintH,
                bp.OnWater ? Rts.Sim.World.Terrain.Land : Rts.Sim.World.Terrain.Water)) return R(PlaceVerdict.Shore);
        // T31: the whole footprint must be in bounds, on open terrain, free of other buildings
        // and ore, and of units (a unit standing there would be sealed in).
        if (!map.CanOccupy(ox, oy, bp.FootprintW, bp.FootprintH, bp.OnWater)) return R(PlaceVerdict.Blocked);
        if (!FootprintClear(units, map, ox, oy, bp.FootprintW, bp.FootprintH, EntityId.None, bp.OnWater)) return R(PlaceVerdict.Occupied);
        // T32: build radius. Anything but a construction yard must come within BuildRadius of one
        // of the faction's own live buildings. (A yard by `place` is the scenario bootstrap; in
        // play yards come from deploying the MCV, see Deploy.)
        if (bp.Category != "construction-yard" && !InBuildRadius(units, map, faction, ox, oy, bp.FootprintW, bp.FootprintH))
            return R(PlaceVerdict.Radius);
        if (credits < bp.Cost) return R(PlaceVerdict.Credits);
        return R(PlaceVerdict.Ok);
    }

    /// <summary>T28: `faction` may build `tree`'s buildings while it owns a live construction yard
    /// of that tree or an MCV that deploys into one (captured, or deployed from a captured MCV).
    /// Its own tree needs nothing (unchanged rules).</summary>
    public static bool HasTree(BuildingCatalog buildings, UnitStore units, int faction, int tree)
    {
        if (tree == faction) return true;
        foreach (var u in units.Units)
        {
            if (u.Faction != faction || u.Hp.Raw <= 0) continue;
            if (u.Building is { } b && buildings.Get(b.Id) is { Category: "construction-yard" } yard && yard.Faction == tree) return true;
            if (u.Building is null && u.DeploysTo is { } y && buildings.Get(y).Faction == tree) return true;
        }
        return false;
    }

    /// <summary>T28: every tree `faction` may build from now (its own first, then ascending); the
    /// host sends it so build menus list captured trees.</summary>
    public static List<int> Trees(BuildingCatalog buildings, UnitStore units, int faction)
    {
        var trees = new List<int> { faction };
        for (var f = 0; f < 3; f++)
            if (f != faction && HasTree(buildings, units, faction, f)) trees.Add(f);
        return trees;
    }

    /// <summary>T28: an unowned map structure (scenario `structures`, e.g. the central oil
    /// derrick), finished, at full hp, its footprint centred on `cell` and marked on the map.
    /// Map furniture: no terrain or radius checks, but it must fit on the map.</summary>
    public static Unit SpawnStructure(BuildingCatalog buildings, UnitStore units, Rts.Sim.World.GameMap map,
        EntityIdAllocator ids, string buildingId, int cellX, int cellY)
    {
        var bp = buildings.Get(buildingId);
        var (ox, oy) = BuildingCatalog.FootprintOrigin(cellX, cellY, bp.FootprintW, bp.FootprintH);
        if (ox < 0 || oy < 0 || ox + bp.FootprintW > map.Width || oy + bp.FootprintH > map.Height)
            throw new InvalidDataException($"structure '{buildingId}' at ({cellX},{cellY}) does not fit on the map");
        var u = SpawnBuilding(units, map, ids, bp, bp.Faction, ox, oy);
        u.Building!.ConstructionRemaining = 0;
        return u;
    }

    private static void Place(BuildingCatalog buildings, UnitStore units, Rts.Sim.World.GameMap map,
        EntityIdAllocator ids, PlaceBuildingCommand p, TryOp spend)
    {
        var (verdict, ox, oy, _, _) = CheckPlace(buildings, units, map, p.Faction, p.BuildingId, p.Cell, int.MaxValue);
        if (verdict != PlaceVerdict.Ok) return;
        var bp = buildings.Get(p.BuildingId);
        if (!spend(p.Faction, bp.Cost)) return; // money last: every earlier failure costs nothing
        SpawnBuilding(units, map, ids, bp, p.Faction, ox, oy);
    }

    /// <summary>A building unit at its footprint's centre (combat ranges and spawns measure from
    /// it), under construction for its BuildTicks, its footprint marked on the map.</summary>
    private static Unit SpawnBuilding(UnitStore units, Rts.Sim.World.GameMap map, EntityIdAllocator ids,
        BuildingCatalog.PlacementParams bp, int faction, int ox, int oy)
    {
        var centre = new FixVec2(Fix64.FromInt(ox) + Fix64.Ratio(bp.FootprintW, 2), Fix64.FromInt(oy) + Fix64.Ratio(bp.FootprintH, 2));
        var b = units.Spawn(ids.Next(), centre, Fix64.Zero, UnitCatalog.DefaultRadius,
            new UnitProfile(faction, bp.Hp, Fix64.Zero, Fix64.Zero, Fix64.Zero, 0, ArmorClass.Structure, DamageType.SmallArms,
                TypeId: bp.Id, MarkRadius: bp.MarkRadius));
        b.Building = new BuildingState { Id = bp.Id, ConstructionRemaining = bp.BuildTicks };
        map.OccupyStructure(b.Id.Value, ox, oy, bp.FootprintW, bp.FootprintH);
        return b;
    }

    /// <summary>Build radius (T32, our tuning const; canon says the yard gives "the base's build
    /// radius" without a number): a new footprint must be within this many cells (Chebyshev, 1 =
    /// touching) of a footprint of one of the faction's live buildings.</summary>
    public const int BuildRadius = 6;

    public static bool InBuildRadius(UnitStore units, Rts.Sim.World.GameMap map, int faction, int ox, int oy, int w, int h)
    {
        foreach (var u in units.Units)
        {
            if (u.Faction != faction || u.Building == null || u.Hp.Raw <= 0) continue;
            if (!map.TryGetFootprint(u.Id.Value, out var r)) continue;
            var dx = Math.Max(0, Math.Max(r.X - (ox + w - 1), ox - (r.X + r.W - 1)));
            var dy = Math.Max(0, Math.Max(r.Y - (oy + h - 1), oy - (r.Y + r.H - 1)));
            if (Math.Max(dx, dy) <= BuildRadius) return true;
        }
        return false;
    }

    /// <summary>Footprint in bounds, open terrain, no building, no live unit standing in it
    /// (a unit there would be sealed in) other than `except`.</summary>
    private static bool FootprintClear(UnitStore units, Rts.Sim.World.GameMap map, int ox, int oy, int w, int h, EntityId except, bool onWater = false)
    {
        if (!map.CanOccupy(ox, oy, w, h, onWater)) return false;
        return !units.Units.Any(u => u.Hp.Raw > 0 && u.Id != except
            && Cell(u.Position.X) >= ox && Cell(u.Position.X) < ox + w
            && Cell(u.Position.Y) >= oy && Cell(u.Position.Y) < oy + h);
    }

    /// <summary>T32: an MCV becomes its construction yard where it stands. Free (paid when the
    /// MCV was trained); the yard footprint is centred on the MCV's cell and must be clear but
    /// for the MCV itself; no build radius (this is how a base starts). The MCV leaves the roster
    /// at cleanup with no death event; the yard then builds for its BuildTicks.</summary>
    private static void Deploy(BuildingCatalog buildings, UnitStore units, Rts.Sim.World.GameMap map,
        EntityIdAllocator ids, DeployCommand d)
    {
        var mcv = units.Find(d.Unit);
        if (mcv is null || mcv.Hp.Raw <= 0 || mcv.Faction != d.Faction || mcv.DeploysTo is null) return;
        var bp = buildings.Get(mcv.DeploysTo); // T28: a captured MCV deploys into its own tree's yard
        var (ox, oy) = BuildingCatalog.FootprintOrigin(Cell(mcv.Position.X), Cell(mcv.Position.Y), bp.FootprintW, bp.FootprintH);
        if (!FootprintClear(units, map, ox, oy, bp.FootprintW, bp.FootprintH, mcv.Id)) return;
        mcv.Hp = Fix64.Zero; // gone at cleanup (step 8); combat skips it this tick
        SpawnBuilding(units, map, ids, bp, d.Faction, ox, oy);
    }

    /// <summary>T29 Conscription: each valid unit (own, alive, conscriptable, catalog-spawned)
    /// leaves the roster at cleanup. A Sapper detonates (Explosive, radius 3, through the matrix,
    /// enemies only); everyone else refunds floor(cost × 50%) within 10 of the faction's own
    /// finished Conscription Hall, else 30%.</summary>
    private static void Conscript(BuildingCatalog buildings, UnitCatalog unitsCatalog, UnitStore units,
        ConscriptCommand k, TryOp refund, SimWorld? events)
    {
        foreach (var id in k.Units)
        {
            if (units.Find(id) is not { } u || u.Faction != k.Faction || u.Hp.Raw <= 0 || !u.Conscriptable || u.TypeId is null) continue;
            u.Hp = Fix64.Zero;
            if (u.Detonates)
            {
                var r2 = Rts.Sim.Combat.OwnedVerbs.SapperRadius * Rts.Sim.Combat.OwnedVerbs.SapperRadius;
                foreach (var e in units.Units)
                {
                    if (e.Faction == u.Faction || e.Faction == Rts.Sim.World.Factions.Neutral || e.Hp.Raw <= 0
                        || (e.Position - u.Position).LengthSquared > r2) continue;
                    var dmg = Rts.Sim.Combat.OwnedVerbs.SapperDamage * Rts.Sim.Combat.DamageMatrix.Get(DamageType.Explosive, e.Armor);
                    if (dmg.Raw < Fix64.OneRaw) dmg = Fix64.One;
                    e.Hp -= dmg;
                    events?.Emit(SimEventKind.Shot, u.Id, e.Id, dmg);
                    if (e.Hp.Raw <= 0) events?.Emit(SimEventKind.Death, e.Id, u.Id, faction: e.Faction);
                }
                continue;
            }
            var atHall = units.Units.Any(h => h.Faction == u.Faction && h.Hp.Raw > 0 && h.Building is { ConstructionRemaining: 0 } hb
                && buildings.Get(hb.Id).ConscriptionHall
                && (h.Position - u.Position).LengthSquared <= Rts.Sim.Combat.OwnedVerbs.HallRadius * Rts.Sim.Combat.OwnedVerbs.HallRadius);
            refund(u.Faction, Rts.Sim.Combat.OwnedVerbs.Refund(unitsCatalog.Get(u.TypeId).Cost, atHall));
        }
    }

    private static void Train(UnitCatalog unitsCatalog, UnitStore units, TrainCommand t, TryOp spend, int tick)
    {
        if (units.Find(t.BuildingId) is not { Building: { } b } bu) return;
        if (bu.Faction != t.Faction || bu.Hp.Raw <= 0 || b.ConstructionRemaining > 0) return;
        if (b.Queue.Count >= ProductionStore.MaxQueue) return;
        if (!unitsCatalog.Builds(b.Id, t.UnitId)) return;
        // T28: no faction check on the unit: you own this building, so you may field what it
        // builds, including an enemy tree's units from a captured (or captured-tree) building.
        var p = unitsCatalog.Get(t.UnitId);
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

    private static void Rally(UnitStore units, RallyCommand r)
    {
        if (units.Find(r.BuildingId) is not { Building: { } b } bu) return;
        if (bu.Faction != r.Faction || bu.Hp.Raw <= 0) return;
        b.Rally = r.Target;
    }

    private static int Cell(Fix64 v) => (int)(v.Raw >> Fix64.FractionBits);
}
