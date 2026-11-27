using System.Text.Json;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Economy;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Power;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Sim.Setup;

/// <summary>One match's world built from a scenario-v2 object, with every system wired in the
/// canonical tick order. The single place the order lives: tools/scenario and tools/sim-host both
/// build through here, so they can never drift. Plumbing only: rules stay in their systems.</summary>
public sealed class MatchWorld
{
    public required SimWorld World { get; init; }
    public required UnitStore Units { get; init; }
    public required EconomyStore Econ { get; init; }
    public required GameMap Map { get; init; }
    public required Rts.Sim.Match.MatchState Match { get; init; }
    public required Dictionary<EntityId, MoveOrder> Orders { get; init; }
    public required UnitCatalog UnitsCatalog { get; init; }
    public required BuildingCatalog BuildingsCatalog { get; init; }
    public required PowerStore Power { get; init; }
    public required BridgeStore Bridges { get; init; }
    /// <summary>The scenario's own units at load (not the map structures), in spawn order: the
    /// default selection of a command with no `units`.</summary>
    public required EntityId[] InitialRoster { get; init; }
    /// <summary>Sides played by the sim's AI (scenario `ai: [faction, ...]`); empty = none.</summary>
    public required Rts.Sim.Ai.AiPlayers Ai { get; init; }

    /// <summary>scenario = schemaVersion 2, seed, map, optional units / veins / structures /
    /// refineries / ai. `commands` are not read here.</summary>
    public static MatchWorld FromScenario(JsonElement scenario, string repoRoot)
    {
        var seed = scenario.GetProperty("seed").GetUInt64();
        var map = GameMap.FromJson(scenario.GetProperty("map")); // T25: terrain rows and/or blocked rects
        var bridges = BridgeStore.FromMap(map, scenario.GetProperty("map")); // T26

        var catalog = UnitCatalog.LoadFromDirectory(Path.Combine(repoRoot, "content", "units"));
        var buildingsCat = BuildingCatalog.LoadFromDirectory(Path.Combine(repoRoot, "content", "buildings"));
        var world = new SimWorld(seed);
        var units = new UnitStore();
        if (scenario.TryGetProperty("units", out var unitEls))
            foreach (var u in unitEls.EnumerateArray())
            {
                var p = catalog.Get(u.GetProperty("unit").GetString()!);
                units.Spawn(world.Ids.Next(),
                    new FixVec2(Fix64.FromDouble(u.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(u.GetProperty("at")[1].GetDouble())),
                    p.Speed, p.Radius, p.Profile());
            }
        var roster = units.Units.Select(u => u.Id).ToArray();

        var orders = new Dictionary<EntityId, MoveOrder>();
        var econ = new EconomyStore();
        if (scenario.TryGetProperty("veins", out var veinsEl))
            foreach (var v in veinsEl.EnumerateArray())
            {
                econ.AddVein(new FixVec2(Fix64.FromDouble(v.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(v.GetProperty("at")[1].GetDouble())),
                    v.TryGetProperty("pool", out var pool) ? pool.GetInt32() : EconomyStore.FieldPool);
                map.MarkResource(GameMap.CellOf(econ.Veins[^1].Position.X), GameMap.CellOf(econ.Veins[^1].Position.Y)); // T33
            }
        // T28: unowned map structures (the oil derrick, Seam radars), after the units
        if (scenario.TryGetProperty("structures", out var structEls))
            foreach (var s in structEls.EnumerateArray())
                Production.ProductionSystem.SpawnStructure(buildingsCat, units, map, world.Ids,
                    s.GetProperty("building").GetString()!, s.GetProperty("at")[0].GetInt32(), s.GetProperty("at")[1].GetInt32());
        Production.ProductionSystem.SpawnBridgeHuts(buildingsCat, units, map, world.Ids, bridges); // T28
        if (scenario.TryGetProperty("refineries", out var refsEl))
            foreach (var r in refsEl.EnumerateArray())
                econ.Refineries.Add(new EconomyStore.Refinery(r.GetProperty("faction").GetInt32(),
                    new FixVec2(Fix64.FromDouble(r.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(r.GetProperty("at")[1].GetDouble()))));
        var ai = new Rts.Sim.Ai.AiPlayers();
        if (scenario.TryGetProperty("ai", out var aiEl))
            foreach (var f in aiEl.EnumerateArray()) ai.Add(f.GetInt32());

        var m = new MatchWorld
        {
            World = world,
            Units = units,
            Econ = econ,
            Map = map,
            Match = new Rts.Sim.Match.MatchState(),
            Orders = orders,
            UnitsCatalog = catalog,
            BuildingsCatalog = buildingsCat,
            Power = new PowerStore(),
            Bridges = bridges,
            InitialRoster = roster,
            Ai = ai,
        };
        m.Wire();
        return m;
    }

    /// <summary>The canonical tick order (sim/CONTEXT.md): AI commands join the step's input;
    /// Power; Income + oil drip; Orders; Production; Transport; Navigation; Deposit; Combat,
    /// Bridges, Captures, Carriers, Superweapons; Despawn; Match. Economy is split so harvest is
    /// credited before spending and deposits read post-move positions.</summary>
    private void Wire()
    {
        var (units, econ, map, orders, catalog, bcat, power, bridges) = (Units, Econ, Map, Orders, UnitsCatalog, BuildingsCatalog, Power, Bridges);
        World.InputSources.Add(w => Ai.Think(w, this));
        World.Systems.Add((w, due) => PowerSystem.Step(power, bcat, units, w.Tick));
        World.Systems.Add((w, due) => EconomySystem.IncomeStep(econ, units, orders, w.Tick, map, catalog.IsRefinery, due));
        World.Systems.Add((w, due) => EconomySystem.DripStep(econ, units, id => bcat.Get(id).OilIncome, w.Tick)); // T28
        World.Systems.Add((w, due) => OrderSystem.ApplyCommands(orders, due, units.Find, (f, a) => econ.TrySpend(f, a)));
        World.Systems.Add((w, due) => Production.ProductionSystem.Step(bcat, catalog, units, map, w.Ids, due,
            (f, a) => econ.TrySpend(f, a), (f, a) => { econ.Deposit(f, a); return true; }, power, w.Tick,
            (id, at) => orders[id] = new MoveOrder(at, new List<FixVec2>(), w.Tick), w));
        World.Systems.Add((w, due) => Transport.TransportSystem.Step(units, orders, map, due, w.Tick, w));
        World.Systems.Add((w, due) => NavigationSystem.Step(map, units, orders));
        World.Systems.Add((w, due) => EconomySystem.DepositStep(econ, units, orders, w.Tick, map, catalog.IsRefinery));
        World.Systems.Add((w, due) => CombatSystem.Step(units, orders, w.Tick, w, power.IsLowPower));
        World.Systems.Add((w, due) => BridgeSystem.Step(bridges, map, units, orders, w.Tick, w)); // T26
        World.Systems.Add((w, due) => CaptureSystem.Step(map, units, orders, w.Tick, w, (f, id) => econ.Deposit(f, catalog.Get(id).Cost), bridges)); // T28
        World.Systems.Add((w, due) => Control.Step(units, orders, w.Tick, w)); // Tri-Point rig hold-to-capture
        World.Systems.Add((w, due) => Carriers.Step(units, orders, catalog, map, w.Ids, w.Tick, w));
        World.Systems.Add((w, due) => Superweapons.SuperweaponSystem.Step(units, due, w.Tick, power.IsLowPower, w));
        World.Systems.Add((w, due) => units.DespawnDead());
        World.Systems.Add((w, due) => Rts.Sim.Match.MatchSystem.Step(w, units, this.Match));
        World.HashMixers.Add(units.Hash);
        World.HashMixers.Add(econ.Hash);
        World.HashMixers.Add(h => Production.ProductionStore.Hash(units, h));
        World.HashMixers.Add(power.Hash);
        World.HashMixers.Add(bridges.Hash); // T26: adds nothing until a bridge is hit
    }
}
