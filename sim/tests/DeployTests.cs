using Rts.Sim.Core;
using Rts.Sim.Economy;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Production;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

/// <summary>T32: MCV deploy into a construction yard, and the build radius around a faction's
/// buildings. Same composition as the other production tests, plus navigation.</summary>
public class DeployTests
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
        public void Step(params Command[] cmds) => Sim.Step(cmds);
    }

    private static W World()
    {
        var x = new W
        {
            Sim = new SimWorld(1),
            Units = new UnitStore(),
            Orders = new Dictionary<EntityId, MoveOrder>(),
            Econ = new EconomyStore(),
            Map = new GameMap(40, 40),
        };
        x.Sim.Systems.Add((_, due) => OrderSystem.ApplyCommands(x.Orders, due, x.Units.Find, (f, a) => x.Econ.TrySpend(f, a)));
        x.Sim.Systems.Add((_, due) => ProductionSystem.Step(BuildingsCat, UnitsCat, x.Units, x.Map, x.Sim.Ids, due,
            (f, a) => x.Econ.TrySpend(f, a), (f, a) => { x.Econ.Deposit(f, a); return true; }, null, x.Sim.Tick));
        x.Sim.Systems.Add((_, due) => NavigationSystem.Step(x.Map, x.Units, x.Orders));
        x.Sim.Systems.Add((_, due) => x.Units.DespawnDead());
        x.Sim.HashMixers.Add(x.Units.Hash);
        return x;
    }

    private static FixVec2 At(int x, int y) => new(Fix64.FromInt(x), Fix64.FromInt(y));
    private static FixVec2 Mid(int x, int y) => GameMap.CellCenter(x, y);

    private static Unit Mcv(W x, string id, int cx, int cy, int faction = 0)
    {
        var p = UnitsCat.Get(id);
        return x.Units.Spawn(x.Sim.Ids.Next(), Mid(cx, cy), p.Speed, p.Radius,
            new UnitProfile(faction, p.Hp, p.Sight, p.Damage, p.Range, p.CooldownTicks, p.Armor, p.DamageType, p.Harvest, p.DeploysTo));
    }

    [Fact]
    public void Catalog_mcvs_deploy_into_their_faction_yard()
    {
        Assert.Equal("coalition-construction-yard", UnitsCat.Get("mobile-command").DeploysTo);
        Assert.Equal("hegemony-construction-yard", UnitsCat.Get("foundry-crawler").DeploysTo);
        Assert.Equal("ascendant-construction-yard", UnitsCat.Get("seed-core").DeploysTo);
        Assert.Equal(2, UnitsCat.Get("seed-core").Faction); // Ascendant is faction 2, never Coalition (0)
        Assert.Null(UnitsCat.Get("rifleman").DeploysTo);
        Assert.True(UnitsCat.Builds("coalition-vehicle-bay", "mobile-command"));
    }

    [Fact]
    public void Deploy_replaces_the_mcv_with_a_yard_under_construction_for_free()
    {
        var x = World();
        var mcv = Mcv(x, "mobile-command", 10, 10);
        var credits = x.Econ.Credits(0);
        x.Step(new DeployCommand(x.Sim.Tick, 0, mcv.Id));

        Assert.Null(x.Units.Find(mcv.Id));
        var yard = Assert.Single(x.Units.Units);
        Assert.Equal("coalition-construction-yard", yard.Building!.Id);
        Assert.Equal(Mid(10, 10), yard.Position); // 3x3 centred on the MCV's cell
        Assert.True(x.Map.TryGetFootprint(yard.Id.Value, out var r));
        Assert.Equal((9, 9, 3, 3), r);
        Assert.Equal(credits, x.Econ.Credits(0));
        Assert.InRange(yard.Building.ConstructionRemaining, 1, BuildingsCat.Get("coalition-construction-yard").BuildTicks);
        Assert.DoesNotContain(x.Sim.Events, e => e.Kind == SimEventKind.Death);
    }

    [Fact]
    public void Deploy_refused_for_wrong_faction_non_mcv_blocked_ground_or_a_unit_in_the_way()
    {
        var x = World();
        var mcv = Mcv(x, "mobile-command", 10, 10);
        x.Step(new DeployCommand(x.Sim.Tick, 1, mcv.Id)); // not yours
        var rifle = UnitsCat.Get("rifleman");
        var r = x.Units.Spawn(x.Sim.Ids.Next(), Mid(20, 20), rifle.Speed, rifle.Radius,
            new UnitProfile(0, rifle.Hp, rifle.Sight, rifle.Damage, rifle.Range, rifle.CooldownTicks, rifle.Armor, rifle.DamageType));
        x.Step(new DeployCommand(x.Sim.Tick, 0, r.Id)); // not an MCV
        x.Map.Block(11, 11);
        x.Step(new DeployCommand(x.Sim.Tick, 0, mcv.Id)); // cliff in the footprint
        Assert.DoesNotContain(x.Units.Units, u => u.Building != null);

        var mcv2 = Mcv(x, "mobile-command", 30, 30);
        x.Units.Spawn(x.Sim.Ids.Next(), Mid(29, 31), Fix64.Zero, Fix64.Ratio(3, 10)); // someone standing in it
        x.Step(new DeployCommand(x.Sim.Tick, 0, mcv2.Id));
        Assert.DoesNotContain(x.Units.Units, u => u.Building != null);
        Assert.NotNull(x.Units.Find(mcv.Id));
        Assert.NotNull(x.Units.Find(mcv2.Id));
    }

    [Fact]
    public void Hegemony_mcv_deploys_into_the_hegemony_yard()
    {
        var x = World();
        var mcv = Mcv(x, "foundry-crawler", 10, 10, faction: 1);
        x.Step(new DeployCommand(x.Sim.Tick, 1, mcv.Id));
        Assert.Equal("hegemony-construction-yard", Assert.Single(x.Units.Units).Building!.Id);
    }

    [Fact]
    public void A_moving_mcv_deploys_where_it_stands_and_stops()
    {
        var x = World();
        var mcv = Mcv(x, "mobile-command", 5, 5);
        x.Step(new MoveCommand(x.Sim.Tick, 0, new[] { mcv.Id }, Mid(30, 5), false));
        for (var i = 0; i < 40; i++) x.Step();
        var cellX = GameMap.CellOf(mcv.Position.X);
        Assert.True(cellX > 5, "the MCV should have moved");
        x.Step(new DeployCommand(x.Sim.Tick, 0, mcv.Id));
        var yard = Assert.Single(x.Units.Units);
        Assert.True(x.Map.TryGetFootprint(yard.Id.Value, out var r));
        Assert.InRange(cellX - r.X, 0, 2);
        for (var i = 0; i < 10; i++) x.Step();
        Assert.Single(x.Units.Units);
    }

    [Fact]
    public void Build_radius_allows_near_the_base_and_refuses_far_away_at_no_cost()
    {
        var x = World();
        var mcv = Mcv(x, "mobile-command", 10, 10);
        x.Step(new DeployCommand(x.Sim.Tick, 0, mcv.Id)); // yard cells 9..11
        for (var i = 0; i < 220; i++) x.Step();
        var credits = x.Econ.Credits(0);

        // reactor 2x2 at (25,10) -> origin (24,9): 13 columns from column 11: refused
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-reactor", At(25, 10)));
        Assert.Single(x.Units.Units);
        Assert.Equal(credits, x.Econ.Credits(0));

        // origin (23,9): 12 columns: allowed
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-reactor", At(24, 10)));
        Assert.Equal(2, x.Units.Units.Count);

        // a building extends the radius: (37,10) -> origin (36,9), 12 from the reactor's column 24
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-reactor", At(37, 10)));
        Assert.Equal(3, x.Units.Units.Count);
    }

    [Fact]
    public void Turrets_and_shipyards_reach_further_than_other_buildings()
    {
        Assert.Equal(ProductionSystem.BuildRadius, ProductionSystem.ReachFor("power"));
        Assert.Equal(ProductionSystem.ForwardBuildRadius, ProductionSystem.ReachFor("defense"));
        Assert.Equal(ProductionSystem.ForwardBuildRadius, ProductionSystem.ReachFor("naval"));
        Assert.Equal(ProductionSystem.ForwardBuildRadius, ProductionSystem.ReachFor("offshore"));

        var x = World();
        var mcv = Mcv(x, "mobile-command", 5, 10);
        x.Step(new DeployCommand(x.Sim.Tick, 0, mcv.Id)); // yard cells 4..6
        for (var i = 0; i < 220; i++) x.Step();
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-barracks", At(5, 13))); // the turret's prereq, columns 4..5
        for (var i = 0; i < 420; i++) x.Step();
        Assert.Equal(2, x.Units.Units.Count);
        // origin column 24 is 18 past the yard's column 6: a reactor is refused, a guard turret is not
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-reactor", At(25, 10)));
        Assert.Equal(2, x.Units.Units.Count);
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-guard-turret", At(24, 10))); // 1x1: origin is the cell
        Assert.Equal(3, x.Units.Units.Count);
        // and no further than 18: row 33 is 20 below the barracks (rows 12..13)
        Assert.Equal(ProductionSystem.PlaceVerdict.Radius,
            ProductionSystem.CheckPlace(BuildingsCat, x.Units, x.Map, 0, "coalition-guard-turret", At(5, 33), x.Econ.Credits(0)).Verdict);
    }

    [Fact]
    public void Build_radius_counts_only_your_own_buildings()
    {
        var x = World();
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 1, "hegemony-construction-yard", At(10, 10)));
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-construction-yard", At(30, 30)));
        for (var i = 0; i < 220; i++) x.Step();
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-reactor", At(14, 10))); // next to the enemy yard
        Assert.Equal(2, x.Units.Units.Count);
    }

    [Fact]
    public void Deploy_and_build_replay_to_the_same_hash()
    {
        ulong Run()
        {
            var x = World();
            var mcv = Mcv(x, "mobile-command", 10, 10);
            x.Step(new DeployCommand(x.Sim.Tick, 0, mcv.Id));
            for (var i = 0; i < 220; i++) x.Step();
            x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-reactor", At(14, 10)));
            for (var i = 0; i < 50; i++) x.Step();
            return x.Sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
