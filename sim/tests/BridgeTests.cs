using System.Text.Json;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

/// <summary>T26: bridges have hit points in their own store (not units). Armed units ordered at
/// one shoot it down; it then turns to water (ships pass, ground units can't, those standing on
/// it fall); an engineer touching it mends it to full and is consumed. Wired like tools/scenario.</summary>
public class BridgeTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RTS.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static UnitCatalog UnitsCat { get; } =
        UnitCatalog.LoadFromDirectory(Path.Combine(RepoRoot(), "content", "units"));

    // 20 x 9: water channel x 9..10, one bridge on row 4; a second, separate bridge on row 8.
    private static readonly string[] Rows =
    {
        ".........~~.........",
        ".........~~.........",
        ".........~~.........",
        ".........~~.........",
        ".........==.........",
        ".........~~.........",
        ".........~~.........",
        ".........~~.........",
        ".........==.........",
    };

    private sealed class W
    {
        public SimWorld Sim = null!;
        public GameMap Map = null!;
        public BridgeStore Bridges = null!;
        public UnitStore Units = null!;
        public Dictionary<EntityId, MoveOrder> Orders = null!;
        public void Step(params Command[] cmds) => Sim.Step(cmds);
        public void RunUntil(Func<bool> done, int max = 4000) { for (var i = 0; i < max && !done(); i++) Step(); }
    }

    private static W World()
    {
        var json = JsonSerializer.Serialize(new { width = 20, height = 9, terrain = Rows });
        var x = new W
        {
            Sim = new SimWorld(1),
            Map = GameMap.FromJson(JsonDocument.Parse(json).RootElement),
            Units = new UnitStore(),
            Orders = new Dictionary<EntityId, MoveOrder>(),
        };
        x.Bridges = BridgeStore.FromMap(x.Map);
        x.Sim.Systems.Add((_, due) => OrderSystem.ApplyCommands(x.Orders, due, x.Units.Find));
        x.Sim.Systems.Add((_, due) => NavigationSystem.Step(x.Map, x.Units, x.Orders));
        x.Sim.Systems.Add((sw, due) => CombatSystem.Step(x.Units, x.Orders, sw.Tick, sw));
        x.Sim.Systems.Add((sw, due) => BridgeSystem.Step(x.Bridges, x.Map, x.Units, x.Orders, sw.Tick, sw));
        x.Sim.Systems.Add((_, due) => x.Units.DespawnDead());
        x.Sim.HashMixers.Add(x.Units.Hash);
        x.Sim.HashMixers.Add(x.Bridges.Hash);
        return x;
    }

    private static Unit Spawn(W x, string id, int faction, int cx, int cy)
    {
        var p = UnitsCat.Get(id);
        return x.Units.Spawn(x.Sim.Ids.Next(), GameMap.CellCenter(cx, cy), p.Speed, p.Radius, p.Profile() with { Faction = faction });
    }

    private BridgeCommand Order(W x, int bridge, params Unit[] us) =>
        new(x.Sim.Tick, us[0].Faction, us.Select(u => u.Id).ToArray(), bridge);

    [Fact]
    public void Bridges_are_found_from_the_tiles_in_row_major_order()
    {
        var x = World();
        Assert.Equal(2, x.Bridges.Bridges.Count);
        Assert.Equal(new[] { (9, 4), (10, 4) }, x.Bridges.Get(0)!.Cells);
        Assert.Equal(new[] { (9, 8), (10, 8) }, x.Bridges.Get(1)!.Cells);
        Assert.Same(x.Bridges.Get(1), x.Bridges.At(10, 8));
        Assert.Null(x.Bridges.At(5, 5));
        Assert.Equal(BridgeStore.BridgeHp, x.Bridges.Get(0)!.Hp);
    }

    [Fact]
    public void Armed_units_shoot_a_bridge_down_and_it_becomes_water()
    {
        var x = World();
        var tank = Spawn(x, "lancer-mbt", 0, 2, 1);
        var before = x.Sim.StateHash();
        x.Step(Order(x, 0, tank));
        x.RunUntil(() => x.Bridges.Get(0)!.Broken);
        var b = x.Bridges.Get(0)!;
        Assert.True(b.Broken);
        Assert.Equal(Terrain.Water, x.Map.TerrainAt(9, 4));
        Assert.Equal(-1, tank.BridgeTarget);
        Assert.False(x.Bridges.Get(1)!.Damaged); // only the ordered one
        // ground can't cross there any more, ships can; the other bridge still carries traffic
        Assert.False(x.Map.IsWalkable(9, 4));
        Assert.True(x.Map.IsWalkable(9, 4, MoveDomain.Naval));
        var detour = Pathfinding.FindPath(x.Map, (5, 4), (14, 4));
        Assert.NotNull(detour);
        Assert.Contains((9, 8), detour!.Select(p => (GameMap.CellOf(p.X), GameMap.CellOf(p.Y))));
        Assert.NotEqual(before, x.Sim.StateHash());
    }

    [Fact]
    public void Land_units_on_a_bridge_fall_with_it_and_ships_under_it_do_not()
    {
        var x = World();
        var tank = Spawn(x, "lancer-mbt", 0, 6, 4);
        var walker = Spawn(x, "rifleman", 0, 10, 4);   // standing on the bridge (own side: falls all the same)
        var ship = Spawn(x, "cutter", 0, 9, 4);        // under it
        x.Step(Order(x, 0, tank));
        x.RunUntil(() => x.Bridges.Get(0)!.Broken);
        Assert.True(x.Bridges.Get(0)!.Broken);
        x.Step();
        Assert.Null(x.Units.Find(walker.Id));
        Assert.NotNull(x.Units.Find(ship.Id));
    }

    [Theory]
    [InlineData("hawk")]
    [InlineData("hind")]
    [InlineData("vulture")]
    [InlineData("lancer-mbt")]
    [InlineData("rifleman")]
    [InlineData("grenadier")]
    [InlineData("heavy-bomber")]
    [InlineData("wraith")]
    [InlineData("bulwark")]
    [InlineData("at-squad")]
    public void Any_ground_hitting_weapon_keeps_shooting_until_the_bridge_falls(string unit)
    {
        var x = World();
        var u = Spawn(x, unit, 0, 2, 1);
        x.Step(Order(x, 0, u));
        x.RunUntil(() => x.Bridges.Get(0)!.Broken, 20000);
        Assert.True(x.Bridges.Get(0)!.Broken, $"{unit}: bridge at {x.Bridges.Get(0)!.Hp} hp, job {u.BridgeTarget}");
    }

    [Fact]
    public void An_engineer_mends_a_fallen_bridge_and_is_used_up()
    {
        var x = World();
        var tank = Spawn(x, "lancer-mbt", 0, 2, 1);
        x.Step(Order(x, 0, tank));
        x.RunUntil(() => x.Bridges.Get(0)!.Broken);
        var eng = Spawn(x, "engineer", 0, 2, 6);
        Assert.True(eng.Engineer);
        x.Step(Order(x, 0, eng));
        x.RunUntil(() => !x.Bridges.Get(0)!.Damaged, 600);
        var b = x.Bridges.Get(0)!;
        Assert.False(b.Broken);
        Assert.Equal(b.MaxHp, b.Hp);
        Assert.Equal(Terrain.Bridge, x.Map.TerrainAt(9, 4));
        x.Step();
        Assert.Null(x.Units.Find(eng.Id));
        Assert.NotNull(Pathfinding.FindPath(x.Map, (5, 3), (14, 3)));
    }

    [Fact]
    public void Each_bridge_gets_a_neutral_hut_at_both_ends_and_an_engineer_sent_there_mends_it()
    {
        var x = World();
        var cat = BuildingCatalog.LoadFromDirectory(Path.Combine(RepoRoot(), "content", "buildings"));
        x.Sim.Systems.Insert(x.Sim.Systems.Count - 1, (sw, due) => CaptureSystem.Step(x.Map, x.Units, x.Orders, sw.Tick, sw, null, x.Bridges));
        Rts.Sim.Production.ProductionSystem.SpawnBridgeHuts(cat, x.Units, x.Map, x.Sim.Ids, x.Bridges);
        var huts = x.Units.Units.Where(u => u.Building?.BridgeId >= 0).ToList();
        Assert.Equal(new[] { (0, 8, 3), (0, 11, 3), (1, 8, 7), (1, 11, 7) },
            huts.Select(h => (h.Building!.BridgeId, GameMap.CellOf(h.Position.X), GameMap.CellOf(h.Position.Y))).ToArray());
        Assert.All(huts, h => Assert.Equal(Factions.Neutral, h.Faction));
        Assert.True(x.Map.IsWalkable(8, 3)); // a marker, not an obstacle

        var eng = Spawn(x, "engineer", 0, 2, 2);
        x.Step(new CaptureCommand(x.Sim.Tick, 0, new[] { eng.Id }, huts[0].Id));
        Assert.Equal(EntityId.None, eng.CaptureTarget); // the bridge is whole: nothing to do

        var tank = Spawn(x, "lancer-mbt", 0, 2, 1);
        x.Step(Order(x, 0, tank));
        x.RunUntil(() => x.Bridges.Get(0)!.Broken);
        x.Step(new CaptureCommand(x.Sim.Tick, 0, new[] { eng.Id }, huts[0].Id));
        x.RunUntil(() => !x.Bridges.Get(0)!.Damaged, 600);
        Assert.False(x.Bridges.Get(0)!.Broken);
        Assert.Equal(Terrain.Bridge, x.Map.TerrainAt(9, 4));
        x.Step();
        Assert.Null(x.Units.Find(eng.Id));                       // used up
        Assert.Equal(Factions.Neutral, huts[0].Faction);          // the hut stays nobody's
    }

    [Fact]
    public void An_engineer_ignores_a_bridge_that_needs_no_repair()
    {
        var x = World();
        var eng = Spawn(x, "engineer", 0, 2, 4);
        x.Step(Order(x, 0, eng));
        x.Step();
        Assert.Equal(-1, eng.BridgeTarget);
        Assert.False(x.Orders.ContainsKey(eng.Id));
        Assert.True(eng.Hp.Raw > 0);
    }

    [Fact]
    public void Anti_air_only_weapons_cannot_attack_a_bridge_and_any_new_order_cancels_the_job()
    {
        var x = World();
        var flak = Spawn(x, "flak-squad", 0, 6, 4);
        x.Step(Order(x, 0, flak));
        x.Step();
        Assert.Equal(-1, flak.BridgeTarget);
        Assert.False(x.Bridges.Get(0)!.Damaged);

        var tank = Spawn(x, "lancer-mbt", 0, 2, 1);
        x.Step(Order(x, 0, tank));
        Assert.Equal(0, tank.BridgeTarget);
        x.Step(new StopCommand(x.Sim.Tick, 0, new[] { tank.Id }));
        Assert.Equal(-1, tank.BridgeTarget);
    }

    [Fact]
    public void Untouched_bridges_add_nothing_to_the_hash()
    {
        var x = World();
        var empty = new BridgeStore();
        Assert.Equal(empty.Hash(42), x.Bridges.Hash(42));
        x.Bridges.Get(1)!.Hp -= Fix64.One;
        Assert.NotEqual(empty.Hash(42), x.Bridges.Hash(42));
    }

    [Fact]
    public void A_causeway_is_land_that_blocks_ships_until_it_falls_and_nothing_may_be_built_on_it()
    {
        // 12 x 3 sea with a land strip across row 1 at x 4..7 joining land at both ends.
        var json = JsonSerializer.Serialize(new
        {
            width = 12,
            height = 3,
            terrain = new[] { "....~~~~....", "............", "....~~~~...." },
            causeways = new[] { new[] { new[] { 4, 1 }, new[] { 5, 1 }, new[] { 6, 1 }, new[] { 7, 1 } } },
        });
        var mapEl = JsonDocument.Parse(json).RootElement;
        var x = World();
        x.Map = GameMap.FromJson(mapEl);
        x.Bridges = BridgeStore.FromMap(x.Map, mapEl);
        var cw = Assert.Single(x.Bridges.Bridges);
        Assert.True(cw.Causeway);
        Assert.Equal(4, cw.Cells.Count);
        Assert.False(x.Map.CanOccupy(5, 1, 1, 1));                                     // never built on
        Assert.Null(Pathfinding.FindPath(x.Map, (5, 0), (5, 2), MoveDomain.Naval));    // ships can't cross it

        var tank = Spawn(x, "lancer-mbt", 0, 1, 1);
        x.Step(Order(x, cw.Id, tank));
        x.RunUntil(() => cw.Broken);
        Assert.True(cw.Broken);
        Assert.Equal(Terrain.Water, x.Map.TerrainAt(5, 1));
        Assert.NotNull(Pathfinding.FindPath(x.Map, (5, 0), (5, 2), MoveDomain.Naval)); // now they can

        var eng = Spawn(x, "engineer", 0, 2, 1);
        x.Step(Order(x, cw.Id, eng));
        x.RunUntil(() => !cw.Broken, 600);
        Assert.Equal(Terrain.Land, x.Map.TerrainAt(5, 1));                             // back as land, not bridge
    }

    [Fact]
    public void A_causeway_cell_that_is_not_land_fails_loudly()
    {
        var json = JsonSerializer.Serialize(new { width = 3, height = 1, terrain = new[] { ".~." }, causeways = new[] { new[] { new[] { 1, 0 } } } });
        var mapEl = JsonDocument.Parse(json).RootElement;
        Assert.Throws<InvalidDataException>(() => BridgeStore.FromMap(GameMap.FromJson(mapEl), mapEl));
    }
}
