using Rts.Sim.Core;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.World;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>
/// Headless coverage of the slice-4 gameplay loop (GRY-296). These exercise the
/// same sim-level behaviour the Unreal GUI play-test (GRY-288) drives: move
/// orders for single and multiple units, arrival and stop behaviour, re-issuing
/// an order mid-move, and same-input determinism over N ticks.
///
/// Sim-only scope: these tests construct only SimWorld, OrderSystem and
/// NavigationSystem. They do not exercise a host, protocol or bridge; host
/// round-trip/state propagation and visible host-failure behaviour remain in
/// GRY-288's acceptance.
///
/// Coverage map (headless vs GUI-only) is documented in
/// docs/superpowers/plans/2026-11-16-slice-4-headless-sim-test-coverage.md.
///
/// Wiring mirrors NavigationTests: OrderSystem + NavigationSystem are registered
/// as static systems over a shared UnitStore and orders dictionary; arrival is
/// signalled by the order being removed from the dictionary.
/// </summary>
public class Slice4GameplayLoopTests
{
    private const int MaxTicks = 200;

    private static Fix64 C(int v) => Fix64.FromInt(v);

    private static (SimWorld world, UnitStore units, Dictionary<EntityId, MoveOrder> orders, GameMap map) Build(
        ulong seed, int w = 12, int h = 12)
    {
        var map = new GameMap(w, h);
        var world = new SimWorld(seed);
        var units = new UnitStore();
        var orders = new Dictionary<EntityId, MoveOrder>();
        world.Systems.Add((wd, due) => OrderSystem.ApplyCommands(orders, due));
        world.Systems.Add((wd, due) => NavigationSystem.Step(map, units, orders));
        world.HashMixers.Add(units.Hash);
        return (world, units, orders, map);
    }

    private static Unit Spawn(UnitStore units, EntityIdAllocator ids, FixVec2 pos, Fix64 speed, Fix64 radius)
    {
        var id = ids.Next();
        return units.Spawn(id, pos, speed, radius);
    }

    /// <summary>Step until the unit's order is cleared (arrival), or the bound is hit.</summary>
    private static bool StepUntilArrived(SimWorld world, Dictionary<EntityId, MoveOrder> orders, EntityId id)
    {
        for (var t = 0; t < MaxTicks; t++)
        {
            world.Step(Array.Empty<Command>());
            if (!orders.ContainsKey(id)) return true;
        }
        return false;
    }

    // ---- Move orders: single unit ----

    [Fact]
    public void Single_unit_move_order_sets_a_move_order()
    {
        var (world, units, orders, _) = Build(1);
        var u = Spawn(units, world.Ids, new FixVec2(C(1), C(1)), C(4), Fix64.Ratio(3, 10));
        var target = new FixVec2(C(9), C(9));
        world.Step(new[] { new MoveCommand(0, 0, new[] { u.Id }, target, false) });
        Assert.True(orders.ContainsKey(u.Id)); // order present
        Assert.Equal(target, orders[u.Id].Target);
    }

    [Fact]
    public void Single_unit_move_order_moves_toward_target_and_arrives()
    {
        var (world, units, orders, _) = Build(1);
        var u = Spawn(units, world.Ids, new FixVec2(C(1), C(1)), C(4), Fix64.Ratio(3, 10));
        var target = new FixVec2(C(9), C(9));
        world.Step(new[] { new MoveCommand(0, 0, new[] { u.Id }, target, false) });
        Assert.True(StepUntilArrived(world, orders, u.Id), "unit did not arrive within " + MaxTicks + " ticks");
        var d = (u.Position - target).Length;
        Assert.True(d <= Fix64.One, $"unit stopped {d} short of target");
    }

    // ---- Move orders: multiple units ----

    [Fact]
    public void Multiple_units_move_order_drives_all_units_to_the_shared_target()
    {
        var (world, units, orders, _) = Build(1);
        var a = Spawn(units, world.Ids, new FixVec2(C(1), C(1)), C(4), Fix64.Ratio(3, 10));
        var b = Spawn(units, world.Ids, new FixVec2(C(2), C(2)), C(4), Fix64.Ratio(3, 10));
        var target = new FixVec2(C(9), C(9));
        world.Step(new[] { new MoveCommand(0, 0, new[] { a.Id, b.Id }, target, false) });
        Assert.True(orders.ContainsKey(a.Id)); // both get the order
        Assert.True(orders.ContainsKey(b.Id));
        Assert.Equal(target, orders[a.Id].Target);
        Assert.Equal(target, orders[b.Id].Target);
        Assert.True(StepUntilArrived(world, orders, a.Id), "unit a did not arrive");
        Assert.True(StepUntilArrived(world, orders, b.Id), "unit b did not arrive");
        Assert.True((a.Position - target).Length <= Fix64.One);
        Assert.True((b.Position - target).Length <= Fix64.One);
    }

    [Fact]
    public void Multiple_units_with_distinct_targets_each_reach_their_own_target()
    {
        // One MoveCommand applies one target to all listed units; distinct
        // destinations require separate commands. Verify each unit reaches its own.
        var (world, units, orders, _) = Build(1);
        var a = Spawn(units, world.Ids, new FixVec2(C(1), C(1)), C(4), Fix64.Ratio(3, 10));
        var b = Spawn(units, world.Ids, new FixVec2(C(2), C(2)), C(4), Fix64.Ratio(3, 10));
        var targetA = new FixVec2(C(9), C(1)); // +X
        var targetB = new FixVec2(C(1), C(9)); // +Y
        world.Step(new[]
        {
            new MoveCommand(0, 0, new[] { a.Id }, targetA, false),
            new MoveCommand(0, 1, new[] { b.Id }, targetB, false),
        });
        Assert.True(StepUntilArrived(world, orders, a.Id), "unit a did not arrive");
        Assert.True(StepUntilArrived(world, orders, b.Id), "unit b did not arrive");
        Assert.True((a.Position - targetA).Length <= Fix64.One);
        Assert.True((b.Position - targetB).Length <= Fix64.One);
    }

    // ---- Arrival and stop behaviour ----

    [Fact]
    public void Stop_command_halts_a_moving_unit_and_keeps_it_put()
    {
        var (world, units, orders, _) = Build(1);
        var u = Spawn(units, world.Ids, new FixVec2(C(1), C(1)), C(4), Fix64.Ratio(3, 10));
        var target = new FixVec2(C(10), C(1)); // far away
        world.Step(new[] { new MoveCommand(0, 0, new[] { u.Id }, target, false) });
        world.Step(Array.Empty<Command>()); // move one tick
        var posAfterMove = u.Position;
        Assert.NotEqual(new FixVec2(C(1), C(1)), posAfterMove); // it actually moved
        world.Step(new[] { new StopCommand(1, 0, new[] { u.Id }) });
        Assert.False(orders.ContainsKey(u.Id)); // order removed on stop
        var posAfterStop = u.Position;
        for (var t = 0; t < 5; t++) world.Step(Array.Empty<Command>());
        Assert.Equal(posAfterStop, u.Position); // stayed put
    }

    [Fact]
    public void Stop_command_on_idle_unit_is_a_noop_that_stays_put()
    {
        var (world, units, orders, _) = Build(1);
        var u = Spawn(units, world.Ids, new FixVec2(C(1), C(1)), C(4), Fix64.Ratio(3, 10));
        world.Step(new[] { new StopCommand(0, 0, new[] { u.Id }) });
        Assert.False(orders.ContainsKey(u.Id)); // no order was ever present
        for (var t = 0; t < 5; t++) world.Step(Array.Empty<Command>());
        Assert.Equal(new FixVec2(C(1), C(1)), u.Position); // stayed put
    }

    // ---- Re-issuing an order mid-move ----

    [Fact]
    public void Re_issuing_move_mid_move_redirects_the_unit_to_the_new_target()
    {
        var (world, units, orders, _) = Build(1);
        var u = Spawn(units, world.Ids, new FixVec2(C(1), C(1)), C(4), Fix64.Ratio(3, 10));
        var first = new FixVec2(C(10), C(1));
        world.Step(new[] { new MoveCommand(0, 0, new[] { u.Id }, first, false) });
        world.Step(Array.Empty<Command>()); // move one tick toward 'first'
        var posMid = u.Position;
        Assert.NotEqual(new FixVec2(C(1), C(1)), posMid); // it moved off the origin
        var second = new FixVec2(C(1), C(10));
        world.Step(new[] { new MoveCommand(1, 0, new[] { u.Id }, second, false) });
        Assert.True(orders.ContainsKey(u.Id)); // order replaced, not cleared
        Assert.Equal(second, orders[u.Id].Target); // new target
        Assert.True(StepUntilArrived(world, orders, u.Id), "unit did not reach the new target");
        Assert.True((u.Position - second).Length <= Fix64.One);
        Assert.True((u.Position - first).Length > Fix64.One); // did not end at the old target
    }

    [Fact]
    public void Re_issuing_move_after_arrival_resumes_movement_from_rest()
    {
        var (world, units, orders, _) = Build(1);
        var u = Spawn(units, world.Ids, new FixVec2(C(1), C(1)), C(4), Fix64.Ratio(3, 10));
        var first = new FixVec2(C(9), C(1));
        world.Step(new[] { new MoveCommand(0, 0, new[] { u.Id }, first, false) });
        Assert.True(StepUntilArrived(world, orders, u.Id), "unit did not reach the first target");
        var restPos = u.Position;
        var second = new FixVec2(C(1), C(9));
        world.Step(new[] { new MoveCommand(1, 0, new[] { u.Id }, second, false) });
        Assert.True(orders.ContainsKey(u.Id)); // order re-issued
        Assert.True(StepUntilArrived(world, orders, u.Id), "unit did not reach the second target");
        Assert.True((u.Position - second).Length <= Fix64.One);
        Assert.True((u.Position - restPos).Length > Fix64.One); // left the rest position
    }

    // ---- Same-input determinism (identical state hashes over N ticks) ----

    [Fact]
    public void Same_input_over_N_ticks_gives_identical_state_hashes()
    {
        var (wa, ua, oa, _) = Build(42);
        var (wb, ub, ob, _) = Build(42);
        var ua1 = Spawn(ua, wa.Ids, new FixVec2(C(1), C(1)), C(4), Fix64.Ratio(3, 10));
        var ub1 = Spawn(ub, wb.Ids, new FixVec2(C(1), C(1)), C(4), Fix64.Ratio(3, 10));
        var ua2 = Spawn(ua, wa.Ids, new FixVec2(C(2), C(2)), C(4), Fix64.Ratio(3, 10));
        var ub2 = Spawn(ub, wb.Ids, new FixVec2(C(2), C(2)), C(4), Fix64.Ratio(3, 10));
        var targetA = new FixVec2(C(9), C(1));
        var targetB = new FixVec2(C(1), C(9));
        for (var t = 0; t < 60; t++)
        {
            Command[] cmds;
            if (t == 0)
                cmds = new[]
                {
                    new MoveCommand(0, 0, new[] { ua1.Id, ua2.Id }, targetA, false),
                    new MoveCommand(0, 1, new[] { ua1.Id, ua2.Id }, targetB, false),
                };
            else if (t == 30) cmds = new[] { new StopCommand(30, 0, new[] { ua1.Id, ua2.Id }) };
            else if (t == 45) cmds = new[] { new MoveCommand(45, 0, new[] { ua1.Id, ua2.Id }, targetB, false) };
            else cmds = Array.Empty<Command>();
            wa.Step(cmds);
            wb.Step(cmds);
            Assert.Equal(wa.StateHash(), wb.StateHash()); // identical every tick
        }
        Assert.Equal(wa.Tick, wb.Tick);
    }

    // ---- Sim-only: an uncommanded world moves nothing (no host/bridge exercised) ----

    [Fact]
    public void Idle_world_does_not_fabricate_movement()
    {
        var (world, units, orders, _) = Build(1);
        var u = Spawn(units, world.Ids, new FixVec2(C(1), C(1)), C(4), Fix64.Ratio(3, 10));
        var h0 = world.StateHash();
        for (var t = 0; t < 20; t++) world.Step(Array.Empty<Command>());
        Assert.Equal(new FixVec2(C(1), C(1)), u.Position); // no spurious movement without commands
        // Proves only that the hash differs across ticks (the hash mixes the tick
        // counter). It does not isolate which fields changed, and no host failure
        // is exercised here.
        Assert.NotEqual(h0, world.StateHash());
    }
}
