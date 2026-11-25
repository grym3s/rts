using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Economy;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Power;
using Rts.Sim.Production;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

/// <summary>T33: refineries are buildings. A finished refinery ships one harvester; harvesters
/// find a vein, fill, return to the nearest live friendly refinery, unload and repeat; they
/// re-route when it dies, and buildings may not cover ore. Wired exactly like tools/scenario.</summary>
public class RefineryTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RTS.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static BuildingCatalog BuildingsCat { get; } =
        BuildingCatalog.LoadFromDirectory(Path.Combine(RepoRoot(), "content", "buildings"));
    private static UnitCatalog UnitsCat { get; } =
        UnitCatalog.LoadFromDirectory(Path.Combine(RepoRoot(), "content", "units"));

    private sealed class W
    {
        public SimWorld Sim = null!;
        public UnitStore Units = null!;
        public Dictionary<EntityId, MoveOrder> Orders = null!;
        public EconomyStore Econ = null!;
        public GameMap Map = null!;
        public PowerStore Power = null!;
        public void Step(params Command[] cmds) => Sim.Step(cmds);
        public void Run(int ticks) { for (var i = 0; i < ticks; i++) Step(); }

        public void AddVein(int cx, int cy, int pool)
        {
            Econ.AddVein(new FixVec2(Fix64.FromInt(cx), Fix64.FromInt(cy)), pool);
            Map.MarkResource(cx, cy);
        }

        public Unit Place(string id, int cx, int cy, int faction = 0)
        {
            var before = Units.Units.Count(u => u.Building != null);
            Step(new PlaceBuildingCommand(Sim.Tick, faction, id, new FixVec2(Fix64.FromInt(cx), Fix64.FromInt(cy))));
            Assert.True(Units.Units.Count(u => u.Building != null) == before + 1, $"{id} at ({cx},{cy}) refused");
            return Units.Units.Last(u => u.Building?.Id == id);
        }

        public IEnumerable<Unit> Harvesters => Units.Units.Where(u => u.Harvest);
    }

    private static W World(int size = 32)
    {
        var x = new W
        {
            Sim = new SimWorld(1),
            Units = new UnitStore(),
            Orders = new Dictionary<EntityId, MoveOrder>(),
            Econ = new EconomyStore(),
            Map = new GameMap(size, size),
            Power = new PowerStore(),
        };
        x.Sim.Systems.Add((_, due) => PowerSystem.Step(x.Power, BuildingsCat, x.Units));
        x.Sim.Systems.Add((sw, due) => EconomySystem.IncomeStep(x.Econ, x.Units, x.Orders, sw.Tick, x.Map, UnitsCat.IsRefinery, due));
        x.Sim.Systems.Add((_, due) => OrderSystem.ApplyCommands(x.Orders, due, x.Units.Find, (f, a) => x.Econ.TrySpend(f, a)));
        x.Sim.Systems.Add((sw, due) => ProductionSystem.Step(BuildingsCat, UnitsCat, x.Units, x.Map, x.Sim.Ids, due,
            (f, a) => x.Econ.TrySpend(f, a), (f, a) => { x.Econ.Deposit(f, a); return true; }, x.Power, sw.Tick));
        x.Sim.Systems.Add((_, due) => NavigationSystem.Step(x.Map, x.Units, x.Orders));
        x.Sim.Systems.Add((sw, due) => EconomySystem.DepositStep(x.Econ, x.Units, x.Orders, sw.Tick, x.Map, UnitsCat.IsRefinery));
        x.Sim.Systems.Add((sw, due) => CombatSystem.Step(x.Units, x.Orders, sw.Tick, sw));
        x.Sim.Systems.Add((_, due) => x.Units.DespawnDead());
        x.Sim.HashMixers.Add(x.Units.Hash);
        x.Sim.HashMixers.Add(x.Econ.Hash);
        return x;
    }

    /// <summary>Yard at (5,5) built, reactor beside it so nothing is power-gated, refinery at
    /// (rx,ry) built. Returns the refinery.</summary>
    private static Unit Base(W x, int rx = 9, int ry = 9)
    {
        x.Place("coalition-construction-yard", 5, 5);
        x.Run(200);
        x.Place("coalition-reactor", 9, 5);
        x.Run(400);
        var refinery = x.Place("coalition-refinery", rx, ry);
        x.Run(BuildingsCat.Get("coalition-refinery").BuildTicks + 1);
        return refinery;
    }

    [Fact]
    public void Catalog_knows_which_buildings_are_refineries()
    {
        Assert.Equal("collector", UnitsCat.HarvesterFor("coalition-refinery"));
        Assert.Equal("miner", UnitsCat.HarvesterFor("hegemony-refinery"));
        Assert.True(UnitsCat.IsRefinery("ascendant-wellspring"));
        Assert.False(UnitsCat.IsRefinery("coalition-barracks"));
    }

    [Fact]
    public void Finished_refinery_ships_one_free_harvester_and_not_before()
    {
        var x = World();
        x.Place("coalition-construction-yard", 5, 5);
        x.Run(200);
        x.Place("coalition-reactor", 9, 5);
        x.Run(400);
        x.Place("coalition-refinery", 9, 10);
        var credits = x.Econ.Credits(0);
        x.Run(BuildingsCat.Get("coalition-refinery").BuildTicks - 5);
        Assert.Empty(x.Harvesters);
        x.Run(6);
        var h = Assert.Single(x.Harvesters);
        Assert.Equal(0, h.Faction);
        Assert.Equal(credits, x.Econ.Credits(0));
        Assert.True(x.Map.IsWalkable(GameMap.CellOf(h.Position.X), GameMap.CellOf(h.Position.Y)));
        x.Run(100);
        Assert.Single(x.Harvesters); // one, not one per tick
    }

    [Fact]
    public void Shipped_harvester_finds_the_vein_fills_returns_and_credits_rise_per_unload()
    {
        var x = World();
        x.AddVein(14, 10, 25_000);
        var refinery = Base(x, 9, 10);
        var h = Assert.Single(x.Harvesters);
        var start = x.Econ.Credits(0);

        var deposits = new List<int>();
        var last = start;
        for (var t = 0; t < 1200; t++)
        {
            x.Step();
            var c = x.Econ.Credits(0);
            if (c != last) { deposits.Add(c - last); last = c; }
        }
        Assert.True(deposits.Count >= 2, $"deposits {deposits.Count}; harvester at {h.Position}, load {h.Load}, carrying {h.Carrying}");
        Assert.All(deposits, d => Assert.Equal(EconomySystem.Capacity, d));
        Assert.Equal(25_000 - deposits.Count * EconomySystem.Capacity - h.Load, x.Econ.FindVein(0)!.Pool);
        Assert.NotNull(x.Units.Find(refinery.Id));
    }

    [Fact]
    public void Harvester_reroutes_to_the_other_refinery_when_its_refinery_dies()
    {
        var x = World(40);
        x.AddVein(16, 10, 25_000);
        var near = Base(x, 12, 10);
        x.Econ.Deposit(0, 5000); // second refinery
        var far = x.Place("coalition-refinery", 12, 16);
        x.Run(BuildingsCat.Get("coalition-refinery").BuildTicks + 1);
        var harvesters = x.Harvesters.ToList();
        Assert.Equal(2, harvesters.Count);

        near.Hp = Fix64.Zero; // raided
        x.Step();
        var before = x.Econ.Credits(0);
        x.Run(900);
        Assert.Null(x.Units.Find(near.Id));
        Assert.True(x.Econ.Credits(0) >= before + EconomySystem.Capacity, $"credits {x.Econ.Credits(0)} vs {before}");
        Assert.NotNull(x.Units.Find(far.Id));
    }

    [Fact]
    public void With_no_refinery_left_a_full_harvester_holds_its_load()
    {
        var x = World();
        x.AddVein(14, 10, 25_000);
        var refinery = Base(x, 9, 10);
        var h = Assert.Single(x.Harvesters);
        refinery.Hp = Fix64.Zero;
        x.Run(600);
        Assert.Equal(EconomySystem.Capacity, h.Load);
        Assert.True(h.Carrying);
    }

    [Fact]
    public void Harvesters_can_be_killed()
    {
        var x = World();
        x.AddVein(14, 10, 25_000);
        Base(x, 9, 10);
        var h = Assert.Single(x.Harvesters);
        var raider = x.Units.Spawn(x.Sim.Ids.Next(), GameMap.CellCenter(20, 20), Fix64.FromInt(3), Fix64.Ratio(3, 10),
            new UnitProfile(1, Fix64.FromInt(200), Fix64.FromInt(8), Fix64.FromInt(60), Fix64.FromInt(5), 10,
                ArmorClass.Light, DamageType.Ap));
        x.Step(new AttackCommand(x.Sim.Tick, 1, new[] { raider.Id }, h.Id, false));
        x.Run(600);
        Assert.Null(x.Units.Find(h.Id));
    }

    [Fact]
    public void A_harvester_moves_on_to_the_next_vein_when_its_own_runs_dry()
    {
        var x = World();
        x.AddVein(14, 10, 700);   // exactly one load
        x.AddVein(14, 14, 25_000);
        Base(x, 9, 10);
        var h = Assert.Single(x.Harvesters);
        x.Run(1500);
        Assert.Equal(0, x.Econ.FindVein(0)!.Pool);
        Assert.True(x.Econ.FindVein(1)!.Pool < 25_000, $"second vein untouched; harvester at {h.Position}");
    }

    [Fact]
    public void Buildings_may_not_cover_a_vein()
    {
        var x = World();
        x.AddVein(10, 10, 25_000);
        x.Place("coalition-construction-yard", 5, 5);
        x.Run(200);
        var credits = x.Econ.Credits(0);
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-reactor", new FixVec2(Fix64.FromInt(10), Fix64.FromInt(10))));
        Assert.Equal(credits, x.Econ.Credits(0));
        Assert.Single(x.Units.Units, u => u.Building != null);
        Assert.True(x.Map.IsWalkable(10, 10)); // ore is still walkable ground
    }

    [Fact]
    public void Harvest_loop_with_refinery_buildings_replays_to_the_same_hash()
    {
        ulong Run()
        {
            var x = World();
            x.AddVein(14, 10, 25_000);
            Base(x, 9, 10);
            x.Run(800);
            return x.Sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    // ---------- T38: send harvesters to a vein ----------

    [Fact]
    public void Harvest_command_sends_a_harvester_to_another_vein_which_becomes_its_home()
    {
        var x = World(40);
        x.AddVein(14, 10, 25_000);
        x.AddVein(14, 24, 25_000);
        Base(x, 9, 10);
        var h = Assert.Single(x.Harvesters);
        x.Run(60); // settled on the near vein
        Assert.Equal(0, h.HomeVein);
        x.Step(new HarvestCommand(x.Sim.Tick, 0, new[] { h.Id }, 1));
        Assert.Equal(1, h.HomeVein);
        x.Run(1500);
        Assert.True(x.Econ.FindVein(1)!.Pool < 25_000, $"far vein untouched; harvester at {h.Position}");
        Assert.Equal(1, h.HomeVein);
    }

    [Fact]
    public void A_carrying_harvester_delivers_before_it_goes_to_the_new_vein()
    {
        var x = World(40);
        x.AddVein(14, 10, 25_000);
        x.AddVein(14, 24, 25_000);
        Base(x, 9, 10);
        var h = Assert.Single(x.Harvesters);
        for (var i = 0; i < 600 && !h.Carrying; i++) x.Step();
        Assert.True(h.Carrying);
        var credits = x.Econ.Credits(0);
        x.Step(new HarvestCommand(x.Sim.Tick, 0, new[] { h.Id }, 1));
        for (var i = 0; i < 300 && x.Econ.Credits(0) == credits; i++) x.Step();
        Assert.Equal(credits + EconomySystem.Capacity, x.Econ.Credits(0)); // delivered the load
        x.Run(900);
        Assert.True(x.Econ.FindVein(1)!.Pool < 25_000);
    }

    [Fact]
    public void Harvest_command_ignores_enemies_dry_veins_and_non_harvesters()
    {
        var x = World(40);
        x.AddVein(14, 10, 25_000);
        x.AddVein(14, 24, 0); // dry
        x.AddVein(20, 24, 25_000);
        var refinery = Base(x, 9, 10);
        var h = Assert.Single(x.Harvesters);
        x.Run(60);
        x.Step(new HarvestCommand(x.Sim.Tick, 0, new[] { h.Id }, 1)); // dry vein
        Assert.Equal(0, h.HomeVein);
        x.Step(new HarvestCommand(x.Sim.Tick, 1, new[] { h.Id }, 2)); // not your harvester
        Assert.Equal(0, h.HomeVein);
        x.Step(new HarvestCommand(x.Sim.Tick, 0, new[] { refinery.Id }, 2)); // not a harvester
        Assert.False(x.Orders.ContainsKey(refinery.Id));
        x.Step(new HarvestCommand(x.Sim.Tick, 0, new[] { h.Id }, 99)); // unknown vein
        Assert.Equal(0, h.HomeVein);
    }
}
