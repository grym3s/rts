using System.Text.Json;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Economy;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Production;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

/// <summary>T25: the map knows land, water, cliff and bridge; units move by domain (land on land
/// and bridges, naval on water and under bridges, hover on both, air anywhere); weapons engage
/// only the domains their `targets` name; naval buildings stand on water by the shore.</summary>
public class TerrainDomainTests
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

    // 20 x 9. Land west (x<9) and east (x>10); a water channel x 9..10 runs top to bottom,
    // crossed by a bridge on row 4. A cliff wall on the west at x=3, rows 0..6.
    private static readonly string[] Rows =
    {
        "...#.....~~.........",
        "...#.....~~.........",
        "...#.....~~.........",
        "...#.....~~.........",
        "...#.....==.........",
        "...#.....~~.........",
        "...#.....~~.........",
        ".........~~.........",
        ".........~~.........",
    };

    private static GameMap Map()
    {
        var json = JsonSerializer.Serialize(new { width = 20, height = 9, terrain = Rows });
        return GameMap.FromJson(JsonDocument.Parse(json).RootElement);
    }

    private static List<(int x, int y)> Cells(List<FixVec2> path) =>
        path.Select(p => (GameMap.CellOf(p.X), GameMap.CellOf(p.Y))).ToList();

    [Fact]
    public void Map_rows_load_as_terrain_and_cliffs_block()
    {
        var m = Map();
        Assert.Equal(Terrain.Land, m.TerrainAt(0, 0));
        Assert.Equal(Terrain.Water, m.TerrainAt(9, 0));
        Assert.Equal(Terrain.Bridge, m.TerrainAt(10, 4));
        Assert.Equal(Terrain.Cliff, m.TerrainAt(3, 2));
        Assert.True(m.IsBlocked(3, 2));
        Assert.False(m.IsWalkable(9, 0));                       // land unit: water is not ground
        Assert.True(m.IsWalkable(9, 0, MoveDomain.Naval));
        Assert.True(m.IsWalkable(9, 4, MoveDomain.Naval));      // under the bridge
        Assert.True(m.IsWalkable(9, 4));                        // over the bridge
        Assert.False(m.IsWalkable(5, 5, MoveDomain.Naval));
        Assert.True(m.IsWalkable(3, 2, MoveDomain.Air));        // aircraft fly over cliffs
        Assert.False(m.IsWalkable(3, 2, MoveDomain.Hover));
    }

    [Fact]
    public void Bad_terrain_rows_fail_loudly()
    {
        var json = JsonSerializer.Serialize(new { width = 3, height = 1, terrain = new[] { "..x" } });
        Assert.Throws<InvalidDataException>(() => GameMap.FromJson(JsonDocument.Parse(json).RootElement));
        json = JsonSerializer.Serialize(new { width = 3, height = 2, terrain = new[] { "..." } });
        Assert.Throws<InvalidDataException>(() => GameMap.FromJson(JsonDocument.Parse(json).RootElement));
    }

    [Fact]
    public void Land_units_cross_water_only_on_the_bridge()
    {
        var path = Pathfinding.FindPath(Map(), (6, 0), (14, 0));
        Assert.NotNull(path);
        var cells = Cells(path!);
        Assert.Contains((9, 4), cells);
        Assert.All(cells, c => Assert.NotEqual(Terrain.Water, Map().TerrainAt(c.x, c.y)));
    }

    [Fact]
    public void Ships_sail_the_channel_under_the_bridge_and_never_land()
    {
        var m = Map();
        var path = Pathfinding.FindPath(m, (9, 0), (10, 8), MoveDomain.Naval);
        Assert.NotNull(path);
        Assert.All(Cells(path!), c => Assert.True(m.IsWalkable(c.x, c.y, MoveDomain.Naval)));
        Assert.Contains(Cells(path!), c => c.y == 4); // passed the bridge row
        // sent onto land: it stops at the nearest water instead
        var shore = Pathfinding.FindPath(m, (9, 0), (12, 0), MoveDomain.Naval);
        Assert.NotNull(shore);
        Assert.Equal((10, 0), Cells(shore!)[^1]);
    }

    [Fact]
    public void Hover_crosses_water_and_land_and_air_flies_straight_over_everything()
    {
        var m = Map();
        var hover = Pathfinding.FindPath(m, (6, 0), (14, 0), MoveDomain.Hover);
        Assert.NotNull(hover);
        Assert.DoesNotContain((9, 4), Cells(hover!)); // no detour to the bridge
        Assert.Equal(8, hover!.Count);
        var air = Pathfinding.FindPath(m, (0, 2), (14, 2), MoveDomain.Air);
        Assert.NotNull(air);
        Assert.Equal(14, air!.Count);                 // straight across the cliff and the channel
        Assert.Contains((3, 2), Cells(air));
    }

    [Fact]
    public void Content_domains_come_from_armor_unless_a_unit_says_otherwise()
    {
        Assert.Equal(MoveDomain.Land, UnitsCat.Get("rifleman").Domain);
        Assert.Equal(MoveDomain.Naval, UnitsCat.Get("cutter").Domain);
        Assert.Equal(MoveDomain.Naval, UnitsCat.Get("pike").Domain);   // submerged
        Assert.Equal(MoveDomain.Air, UnitsCat.Get("falcon").Domain);
        Assert.Equal(MoveDomain.Hover, UnitsCat.Get("skimmer").Domain);
        Assert.Equal(MoveDomain.Hover, UnitsCat.Get("ripple").Domain);
        Assert.Equal(MoveDomain.Hover, UnitsCat.Get("sever").Domain);
        Assert.Equal(TargetMask.Ground, UnitsCat.Get("rifleman").Targets);
        Assert.Equal(TargetMask.Air, UnitsCat.Get("flak-squad").Targets);
        Assert.Equal(TargetMask.Ground | TargetMask.Air, UnitsCat.Get("ifv").Targets);
        Assert.Equal(TargetMask.Sea | TargetMask.Ground, UnitsCat.Get("pike").Targets);
    }

    [Theory]
    // surface combatants shoot ship-to-ship and at the shore (design reference: "Surface
    // combatant"); subs surface to hit land; bombardment ships are built for it
    [InlineData("cutter", true)]
    [InlineData("gunboat", true)]
    [InlineData("ripple", true)]
    [InlineData("sentinel", true)]
    [InlineData("bastion", true)]
    [InlineData("sever", true)]
    [InlineData("silent", true)]
    [InlineData("pike", true)]
    [InlineData("longbow", true)]
    [InlineData("siege-barge", true)]
    [InlineData("rift-hull", true)]
    // AA cruisers are the fleet's air umbrella and nothing else
    [InlineData("aegis", false)]
    [InlineData("flak-barge", false)]
    public void Armed_ships_hit_land_by_their_type(string ship, bool hitsLand)
    {
        var p = UnitsCat.Get(ship);
        Assert.True((p.Targets & TargetMask.Sea) != 0 || !hitsLand, $"{ship} cannot hit ships");
        Assert.Equal(hitsLand, (p.Targets & TargetMask.Ground) != 0);
    }

    private static Unit Spawn(UnitStore units, EntityIdAllocator ids, string id, int faction, int cx, int cy)
    {
        var p = UnitsCat.Get(id);
        var prof = p.Profile() with { Faction = faction };
        return units.Spawn(ids.Next(), GameMap.CellCenter(cx, cy), p.Speed, p.Radius, prof);
    }

    [Fact]
    public void Weapons_engage_only_the_domains_they_target()
    {
        var w = new SimWorld(1);
        var units = new UnitStore();
        var orders = new Dictionary<EntityId, MoveOrder>();
        var rifle = Spawn(units, w.Ids, "rifleman", 0, 5, 5);
        var flak = Spawn(units, w.Ids, "flak-squad", 0, 5, 7);
        var jet = Spawn(units, w.Ids, "falcon", 1, 6, 6);
        for (var t = 0; t < 40; t++) CombatSystem.Step(units, orders, t);
        Assert.NotEqual(jet.Id, rifle.TargetId); // never picked the jet
        Assert.True(jet.Hp < jet.MaxHp, "flak never fired at the jet");
        Assert.False(CombatSystem.CanHit(rifle, jet));
        Assert.True(CombatSystem.CanHit(flak, jet));

        // an explicit attack on an unhittable target is dropped, not chased forever
        rifle.TargetId = jet.Id;
        CombatSystem.Step(units, orders, 41);
        Assert.Equal(EntityId.None, rifle.TargetId);
    }

    [Fact]
    public void Aircraft_do_not_push_ground_units()
    {
        var m = new GameMap(10, 10);
        var units = new UnitStore();
        var ids = new EntityIdAllocator();
        var tank = Spawn(units, ids, "lancer-mbt", 0, 5, 5);
        var jet = Spawn(units, ids, "falcon", 0, 5, 5);
        var before = tank.Position;
        NavigationSystem.Step(m, units, new Dictionary<EntityId, MoveOrder>());
        Assert.Equal(before, tank.Position);
        Assert.Equal(before, jet.Position);
    }

    [Fact]
    public void Ships_launch_onto_water()
    {
        var m = Map();
        var at = GameMap.CellCenter(7, 2); // on land, two cells from the channel
        var land = ProductionRules.FindSpawnCell(m, new UnitStore(), at);
        var sea = ProductionRules.FindSpawnCell(m, new UnitStore(), at, MoveDomain.Naval);
        Assert.Equal(Terrain.Land, m.TerrainAt(GameMap.CellOf(land.X), GameMap.CellOf(land.Y)));
        Assert.Equal(Terrain.Water, m.TerrainAt(GameMap.CellOf(sea.X), GameMap.CellOf(sea.Y)));
    }

    [Fact]
    public void Shipyards_go_on_water_beside_land_and_coastal_guns_on_land_beside_water()
    {
        // 30 x 20: land x < 15, sea x >= 15.
        var rows = Enumerable.Range(0, 20).Select(_ => new string('.', 15) + new string('~', 15)).ToArray();
        var m = GameMap.FromJson(JsonDocument.Parse(JsonSerializer.Serialize(new { width = 30, height = 20, terrain = rows })).RootElement);
        var units = new UnitStore();
        var w = new SimWorld(1);
        var econ = new EconomyStore();
        w.Systems.Add((sw, due) => ProductionSystem.Step(BuildingsCat, UnitsCat, units, m, sw.Ids, due,
            (f, a) => econ.TrySpend(f, a), (f, a) => { econ.Deposit(f, a); return true; }, null, sw.Tick));
        w.Step(new Command[] { new PlaceBuildingCommand(w.Tick, 0, "coalition-construction-yard", GameMap.CellCenter(11, 10)) });
        PlaceVerdictOf("coalition-shipyard", 16, 10, out var ok);
        Assert.Equal(ProductionSystem.PlaceVerdict.Ok, ok);
        PlaceVerdictOf("coalition-shipyard", 8, 10, out var onLand);
        Assert.Equal(ProductionSystem.PlaceVerdict.Water, onLand);
        PlaceVerdictOf("coalition-shipyard", 14, 10, out var straddle);   // footprint 13..15: half on land
        Assert.Equal(ProductionSystem.PlaceVerdict.Water, straddle);
        PlaceVerdictOf("coalition-shipyard", 19, 10, out var offshore);   // 18..20: no land beside it
        Assert.Equal(ProductionSystem.PlaceVerdict.Shore, offshore);
        PlaceVerdictOf("coalition-reactor", 16, 4, out var reactorAtSea);
        Assert.Equal(ProductionSystem.PlaceVerdict.Blocked, reactorAtSea);

        void PlaceVerdictOf(string id, int cx, int cy, out ProductionSystem.PlaceVerdict v) =>
            v = ProductionSystem.CheckPlace(BuildingsCat, units, m, 0, id, GameMap.CellCenter(cx, cy), int.MaxValue).Verdict;
    }
}
