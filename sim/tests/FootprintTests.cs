using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Economy;
using Rts.Sim.Match;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Production;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

/// <summary>T31: building footprints occupy map cells. Placement checks the whole footprint,
/// units path around it, attackers stop beside it, and a destroyed building frees its cells.
/// Composed in the real tick order (orders, production, navigation, combat, despawn, match).</summary>
public class FootprintTests
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
        public MatchState Match = null!;
        public void Step(params Command[] cmds) => Sim.Step(cmds);
    }

    private static W World(int size = 24)
    {
        var x = new W
        {
            Sim = new SimWorld(1),
            Units = new UnitStore(),
            Orders = new Dictionary<EntityId, MoveOrder>(),
            Econ = new EconomyStore(),
            Map = new GameMap(size, size),
            Match = new MatchState(),
        };
        x.Sim.Systems.Add((_, due) => OrderSystem.ApplyCommands(x.Orders, due, x.Units.Find, (f, a) => x.Econ.TrySpend(f, a)));
        x.Sim.Systems.Add((_, due) => ProductionSystem.Step(BuildingsCat, UnitsCat, x.Units, x.Map, x.Sim.Ids, due,
            (f, a) => x.Econ.TrySpend(f, a), (f, a) => { x.Econ.Deposit(f, a); return true; }, null, x.Sim.Tick));
        x.Sim.Systems.Add((_, due) => NavigationSystem.Step(x.Map, x.Units, x.Orders));
        x.Sim.Systems.Add((sw, due) => CombatSystem.Step(x.Units, x.Orders, sw.Tick, sw));
        x.Sim.Systems.Add((_, due) => x.Units.DespawnDead());
        x.Sim.Systems.Add((sw, due) => MatchSystem.Step(sw, x.Units, x.Match));
        x.Sim.HashMixers.Add(x.Units.Hash);
        return x;
    }

    private static FixVec2 At(int x, int y) => new(Fix64.FromInt(x), Fix64.FromInt(y));
    private static FixVec2 Mid(int x, int y) => GameMap.CellCenter(x, y);

    private static Unit Yard(W x, int cx = 5, int cy = 5, int faction = 0)
    {
        var id = faction == 0 ? "coalition-construction-yard" : "hegemony-construction-yard";
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, faction, id, At(cx, cy)));
        return x.Units.Units.Last(u => u.Building?.Id == id);
    }

    [Fact]
    public void Catalog_gives_every_building_a_footprint_and_barracks_override_to_2x2()
    {
        Assert.All(BuildingsCat.All, b => Assert.True(b.FootprintW >= 1 && b.FootprintH >= 1));
        var yard = BuildingsCat.Get("coalition-construction-yard");
        Assert.Equal((3, 3), (yard.FootprintW, yard.FootprintH));
        var barracks = BuildingsCat.Get("coalition-barracks");
        Assert.Equal((2, 2), (barracks.FootprintW, barracks.FootprintH));
    }

    [Fact]
    public void Placed_yard_covers_its_3x3_footprint_and_stands_at_its_centre()
    {
        var x = World();
        var yard = Yard(x);
        Assert.True(x.Map.TryGetFootprint(yard.Id.Value, out var r));
        Assert.Equal((4, 4, 3, 3), r);
        for (var cy = 3; cy <= 7; cy++)
            for (var cx = 3; cx <= 7; cx++)
            {
                var inside = cx is >= 4 and <= 6 && cy is >= 4 and <= 6;
                Assert.Equal(inside ? yard.Id.Value : -1, x.Map.StructureAt(cx, cy));
                Assert.Equal(!inside, x.Map.IsWalkable(cx, cy));
                Assert.True(x.Map.IsTerrainWalkable(cx, cy));
            }
        Assert.Equal(new FixVec2(Fix64.Ratio(11, 2), Fix64.Ratio(11, 2)), yard.Position);
    }

    [Fact]
    public void Placement_refused_on_blocked_terrain_overlap_map_edge_or_a_unit_and_costs_nothing()
    {
        var x = World();
        var start = x.Econ.Credits(0);

        x.Map.Block(11, 11); // one blocked cell inside the 3x3 around (10,10)
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-construction-yard", At(10, 10)));
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-construction-yard", At(0, 5))); // off the edge
        x.Units.Spawn(x.Sim.Ids.Next(), Mid(16, 16), Fix64.One, Fix64.Ratio(3, 10));
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-construction-yard", At(17, 17))); // unit at a corner
        Assert.DoesNotContain(x.Units.Units, u => u.Building != null);
        Assert.Equal(start, x.Econ.Credits(0));

        var first = Yard(x);
        var afterFirst = x.Econ.Credits(0);
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-construction-yard", At(7, 5))); // overlaps column 6
        Assert.Single(x.Units.Units, u => u.Building != null);
        Assert.Equal(afterFirst, x.Econ.Credits(0));

        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-construction-yard", At(8, 5))); // flush beside it: fine
        Assert.Equal(2, x.Units.Units.Count(u => u.Building != null));
        Assert.NotEqual(first.Id, x.Units.Units.Last(u => u.Building != null).Id);
    }

    [Fact]
    public void Path_goes_around_a_footprint_and_never_enters_it()
    {
        var x = World();
        Yard(x, 6, 6); // cells 5..7
        var path = Pathfinding.FindPath(x.Map, (2, 6), (10, 6))!;
        Assert.NotNull(path);
        foreach (var p in path)
            Assert.True(x.Map.IsWalkable(GameMap.CellOf(p.X), GameMap.CellOf(p.Y)), $"path enters {p}");
    }

    [Fact]
    public void Unit_walks_around_a_building_to_the_far_side_without_entering_it()
    {
        var x = World();
        var yard = Yard(x, 6, 6);
        var u = x.Units.Spawn(x.Sim.Ids.Next(), Mid(2, 6), Fix64.FromInt(4), Fix64.Ratio(3, 10));
        x.Step(new MoveCommand(x.Sim.Tick, 0, new[] { u.Id }, Mid(10, 6), false));
        for (var t = 0; t < 200; t++)
        {
            x.Step();
            Assert.NotEqual(yard.Id.Value, x.Map.StructureAt(GameMap.CellOf(u.Position.X), GameMap.CellOf(u.Position.Y)));
        }
        Assert.True((u.Position - Mid(10, 6)).Length <= Fix64.One, $"stopped at {u.Position}");
    }

    [Fact]
    public void Move_into_a_building_ends_beside_it()
    {
        var x = World();
        Yard(x, 6, 6);
        var u = x.Units.Spawn(x.Sim.Ids.Next(), Mid(1, 6), Fix64.FromInt(4), Fix64.Ratio(3, 10));
        x.Step(new MoveCommand(x.Sim.Tick, 0, new[] { u.Id }, Mid(6, 6), false));
        for (var t = 0; t < 200; t++) x.Step();
        Assert.False(x.Orders.ContainsKey(u.Id)); // arrived, did not grind against the wall
        Assert.Equal((4, 6), (GameMap.CellOf(u.Position.X), GameMap.CellOf(u.Position.Y)));
    }

    [Fact]
    public void A_building_placed_across_a_planned_path_forces_a_replan()
    {
        var x = World();
        var u = x.Units.Spawn(x.Sim.Ids.Next(), Mid(1, 6), Fix64.FromInt(2), Fix64.Ratio(3, 10));
        x.Step(new MoveCommand(x.Sim.Tick, 0, new[] { u.Id }, Mid(14, 6), false));
        x.Step();
        var yard = Yard(x, 8, 6); // straight across the planned line
        for (var t = 0; t < 300; t++)
        {
            x.Step();
            Assert.NotEqual(yard.Id.Value, x.Map.StructureAt(GameMap.CellOf(u.Position.X), GameMap.CellOf(u.Position.Y)));
        }
        Assert.True((u.Position - Mid(14, 6)).Length <= Fix64.One, $"stopped at {u.Position}");
    }

    [Fact]
    public void Buildings_can_be_attacked_destroyed_and_free_their_footprint()
    {
        var x = World();
        var yard = Yard(x, 6, 6, faction: 1);
        var shooter = x.Units.Spawn(x.Sim.Ids.Next(), Mid(1, 6), Fix64.FromInt(3), Fix64.Ratio(3, 10),
            new UnitProfile(0, Fix64.FromInt(100), Fix64.FromInt(3), Fix64.FromInt(400), Fix64.FromInt(3), 4,
                ArmorClass.Infantry, DamageType.Explosive));
        x.Step(new AttackCommand(x.Sim.Tick, 0, new[] { shooter.Id }, yard.Id, false));
        var died = -1;
        for (var t = 0; t < 600 && died < 0; t++)
        {
            x.Step();
            if (x.Units.Find(yard.Id) is null) died = x.Sim.Tick;
        }
        Assert.True(died > 0, "the yard never died");
        Assert.True(x.Map.TryGetFootprint(yard.Id.Value, out _)); // freed at the next production step
        x.Step();
        Assert.False(x.Map.TryGetFootprint(yard.Id.Value, out _));
        for (var cy = 5; cy <= 7; cy++)
            for (var cx = 5; cx <= 7; cx++)
                Assert.True(x.Map.IsWalkable(cx, cy));
        Assert.True(x.Match.Over);
        Assert.Equal(0, x.Match.Winner);
    }

    [Fact]
    public void A_lone_building_keeps_its_side_in_the_match()
    {
        var x = World();
        Yard(x, 6, 6, faction: 1);
        x.Units.Spawn(x.Sim.Ids.Next(), Mid(20, 20), Fix64.One, Fix64.Ratio(3, 10),
            new UnitProfile(0, Fix64.FromInt(60), Fix64.One, Fix64.Zero, Fix64.FromInt(4), 12, ArmorClass.Infantry, DamageType.Energy));
        for (var t = 0; t < 20; t++) x.Step();
        Assert.False(x.Match.Over);
    }

    [Fact]
    public void Same_commands_same_hash_with_buildings_on_the_map()
    {
        ulong Run()
        {
            var x = World();
            Yard(x, 6, 6);
            var u = x.Units.Spawn(x.Sim.Ids.Next(), Mid(1, 6), Fix64.FromInt(4), Fix64.Ratio(3, 10));
            x.Step(new MoveCommand(x.Sim.Tick, 0, new[] { u.Id }, Mid(6, 6), false));
            for (var t = 0; t < 120; t++) x.Step();
            return x.Sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
