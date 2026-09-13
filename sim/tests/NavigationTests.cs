using Rts.Sim.Core;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

public class NavigationTests
{
    private static GameMap EmptyMap(int w = 12, int h = 12) => new(w, h);
    private static Fix64 C(int v) => Fix64.FromInt(v);

    [Fact]
    public void Straight_path_on_empty_map_hits_goal_centre()
    {
        var map = EmptyMap();
        var path = Pathfinding.FindPath(map, (1, 1), (5, 1))!;
        Assert.NotEmpty(path);
        Assert.Equal(GameMap.CellCenter(5, 1), path[^1]);
    }

    [Fact]
    public void Path_goes_around_a_wall_and_never_crosses_it()
    {
        var map = EmptyMap();
        map.BlockRect(4, 0, 4, 3); // wall with a gap at rows 4-11
        var path = Pathfinding.FindPath(map, (1, 1), (8, 1))!;
        foreach (var p in path)
            Assert.False(map.IsBlocked(GameMap.CellOf(p.X), GameMap.CellOf(p.Y)));
        Assert.Equal(GameMap.CellCenter(8, 1), path[^1]);
    }

    [Fact]
    public void Unreachable_goal_returns_null()
    {
        var map = EmptyMap();
        map.BlockRect(4, 0, 4, 11); // full-height wall
        Assert.Null(Pathfinding.FindPath(map, (1, 1), (8, 1)));
    }

    [Fact]
    public void Unit_moves_to_target_and_clears_its_order()
    {
        var (world, units, orders, _) = Build(EmptyMap());
        var u = units.Spawn(world.Ids.Next(), new FixVec2(C(1), C(1)), C(4), Fix64.Ratio(3, 10));
        var cmd = new[] { new MoveCommand(0, 0, new[] { u.Id }, new FixVec2(C(9), C(9)), false) };
        for (var t = 0; t < 120; t++) world.Step(cmd.Where(_ => t == 0).ToArray());
        var d = (u.Position - new FixVec2(C(9), C(9))).Length;
        Assert.True(d <= Fix64.One, $"unit stopped {d} short");
        Assert.False(orders.ContainsKey(u.Id));
    }

    [Fact]
    public void Two_units_head_on_never_end_overlapping()
    {
        var map = EmptyMap();
        var (world, units, _, _) = Build(map);
        var a = units.Spawn(world.Ids.Next(), new FixVec2(C(1), C(1)), C(3), Fix64.Ratio(3, 10));
        var b = units.Spawn(world.Ids.Next(), new FixVec2(C(10), C(1)), C(3), Fix64.Ratio(3, 10));
        var cmds = new Command[]
        {
            new MoveCommand(0, 0, new[] { a.Id }, new FixVec2(C(10), C(1)), false),
            new MoveCommand(0, 0, new[] { b.Id }, new FixVec2(C(1), C(1)), false),
        };
        for (var t = 0; t < 100; t++) world.Step(cmds.Where(_ => t == 0).ToArray());
        Assert.True((a.Position - b.Position).Length >= a.Radius + b.Radius - Fix64.Ratio(1, 20));
    }

    private static (SimWorld, UnitStore, Dictionary<EntityId, MoveOrder>, GameMap) Build(GameMap map)
    {
        var world = new SimWorld(1);
        var units = new UnitStore();
        var orders = new Dictionary<EntityId, MoveOrder>();
        world.Systems.Add((w, due) => OrderSystem.ApplyCommands(orders, due));
        world.Systems.Add((w, due) => NavigationSystem.Step(map, units, orders));
        world.HashMixers.Add(units.Hash);
        return (world, units, orders, map);
    }
}
