using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

public class CombatTests
{
    // Energy is the canon flat multiplier (docs/factions/counter-matrix.md): these tests
    // exercise order/cooldown choreography, damage math per matrix cell is CounterMatrixTests.
    private static UnitProfile Profile(int faction, int hp = 60, int dmg = 10, int range = 4, int cd = 12, int sight = 8)
        => new(faction, Fix64.FromInt(hp), Fix64.FromInt(sight), Fix64.FromInt(dmg), Fix64.FromInt(range), cd,
            ArmorClass.Infantry, DamageType.Energy);

    private static (SimWorld w, UnitStore u, Dictionary<EntityId, MoveOrder> o) World()
    {
        var w = new SimWorld(1);
        var u = new UnitStore();
        var o = new Dictionary<EntityId, MoveOrder>();
        var map = new Rts.Sim.World.GameMap(24, 24);
        w.Systems.Add((_, due) => OrderSystem.ApplyCommands(o, due, u.Find));
        w.Systems.Add((_, due) => Rts.Sim.Navigation.NavigationSystem.Step(map, u, o));
        w.Systems.Add((_, due) => CombatSystem.Step(u, o, w.Tick));
        w.Systems.Add((_, due) => u.DespawnDead());
        return (w, u, o);
    }

    [Fact]
    public void AttackOrdersKillTargetOnCooldownTicks()
    {
        var (w, units, orders) = World();
        var a = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.Zero, Fix64.Zero), Fix64.One, Fix64.FromInt(1), Profile(0));
        var b = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(3), Fix64.Zero), Fix64.One, Fix64.FromInt(1), Profile(1, hp: 25, dmg: 0));

        // tick-1 inbox: Attack a -> b (dmg 10, cd 12): shots land on due tick, then 13, 26...
        w.Step(new Command[] { new AttackCommand(w.Tick, 0, new[] { a.Id }, b.Id, false) });
        Assert.Equal(15, b.Hp.ToDouble()); // 1 shot (dmg 10) applied in the same tick as the order
        for (var i = 0; i < 11; i++) w.Step(Array.Empty<Command>());
        Assert.Equal(15, b.Hp.ToDouble()); // still cooling
        w.Step(Array.Empty<Command>());
        Assert.Equal(5, b.Hp.ToDouble());

        for (var i = 0; i < 25 && units.Units.Count > 1; i++) w.Step(Array.Empty<Command>());
        Assert.Single(units.Units); // b dead and despawned
        Assert.Equal(a.Id, units.Units[0].Id);
    }

    [Fact]
    public void ChaseThenFireWhenOutOfRange()
    {
        var (w, units, orders) = World();
        var a = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(2), Fix64.FromInt(2)), Fix64.FromInt(2), Fix64.FromInt(1), Profile(0));
        var b = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(12), Fix64.FromInt(2)), Fix64.One, Fix64.FromInt(1), Profile(1, dmg: 0, sight: 0));

        w.Step(new Command[] { new AttackCommand(w.Tick, 0, new[] { a.Id }, b.Id, false) });
        Assert.True(orders.ContainsKey(a.Id)); // chase order issued
        Assert.Equal((double)60, b.Hp.ToDouble()); // out of range: no damage yet

        for (var i = 0; i < 60; i++) w.Step(Array.Empty<Command>());
        Assert.True(b.Hp < Fix64.FromInt(60)); // closed in and fired
    }

    [Fact]
    public void IdleUnitHoldsWhenNothingInSight()
    {
        var (w, units, orders) = World();
        var g = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(2), Fix64.FromInt(2)), Fix64.One, Fix64.FromInt(1), Profile(1));
        units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(20), Fix64.FromInt(2)), Fix64.One, Fix64.FromInt(1), Profile(0, dmg: 0));

        for (var i = 0; i < 20; i++) w.Step(Array.Empty<Command>());
        Assert.False(g.Guarding);
        Assert.False(orders.ContainsKey(g.Id));
        Assert.Equal(Fix64.FromInt(2), g.Position.X);
    }

    [Fact]
    public void GuardChasesIntruderInSightAndReturnsWhenItDies()
    {
        var (w, units, orders) = World();
        var anchor = new FixVec2(Fix64.FromInt(2), Fix64.FromInt(2));
        var g = units.Spawn(w.Ids.Next(), anchor, Fix64.One, Fix64.FromInt(1), Profile(1, dmg: 20, cd: 2));
        // in sight (8) but out of reach (range 4 + radii 2): the guard must walk to it
        var intruder = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(9), Fix64.FromInt(2)), Fix64.One, Fix64.FromInt(1), Profile(0, hp: 40, dmg: 0));

        w.Step(Array.Empty<Command>());
        Assert.True(g.Guarding);
        Assert.Equal(intruder.Id, g.TargetId);
        Assert.Equal(anchor, g.GuardAnchor);
        Assert.True(orders.ContainsKey(g.Id)); // chasing

        for (var i = 0; i < 40 && units.Find(intruder.Id) != null; i++) w.Step(Array.Empty<Command>());
        Assert.Null(units.Find(intruder.Id));
        w.Step(Array.Empty<Command>());
        Assert.True(g.Returning);

        for (var i = 0; i < 40 && g.Returning; i++) w.Step(Array.Empty<Command>());
        Assert.False(g.Returning);
        Assert.True((g.Position - anchor).Length < Fix64.One); // back on its spot
    }

    [Fact]
    public void GuardGivesUpChaseAtLeashAndWalksHome()
    {
        var (w, units, orders) = World();
        var anchor = new FixVec2(Fix64.FromInt(2), Fix64.FromInt(2));
        // slow guard, weapon never reaches (range 0), sight 4 -> leash 6 cells from anchor
        var g = units.Spawn(w.Ids.Next(), anchor, Fix64.One, Fix64.Ratio(3, 10), Profile(1, range: 0, sight: 4));
        var bait = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(5), Fix64.FromInt(2)), Fix64.One, Fix64.Ratio(3, 10), Profile(0, dmg: 0));
        // bait runs away along the row
        w.Step(new Command[] { new MoveCommand(w.Tick, 0, new[] { bait.Id }, new FixVec2(Fix64.FromInt(22), Fix64.FromInt(2)), false) });
        Assert.True(g.Guarding);

        var maxOut = Fix64.Zero;
        for (var i = 0; i < 200 && !g.Returning; i++)
        {
            w.Step(Array.Empty<Command>());
            maxOut = Fix64.Max(maxOut, (g.Position - anchor).Length);
        }
        Assert.True(g.Returning);
        Assert.True(maxOut <= Fix64.FromInt(7)); // leash 6 plus at most one step
        for (var i = 0; i < 200 && g.Returning; i++) w.Step(Array.Empty<Command>());
        Assert.True((g.Position - anchor).Length < Fix64.One);
        Assert.False(orders.ContainsKey(g.Id));
    }

    [Fact]
    public void MoveOrderOverridesGuardAndAttack()
    {
        var (w, units, orders) = World();
        var a = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(2), Fix64.FromInt(2)), Fix64.One, Fix64.FromInt(1), Profile(0));
        var e = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(8), Fix64.FromInt(2)), Fix64.One, Fix64.FromInt(1), Profile(1, dmg: 0, sight: 0));
        w.Step(Array.Empty<Command>());
        Assert.True(a.Guarding);

        var dest = new FixVec2(Fix64.FromInt(2), Fix64.FromInt(20));
        w.Step(new Command[] { new MoveCommand(w.Tick, 0, new[] { a.Id }, dest, false) });
        Assert.False(a.Guarding);
        Assert.Equal(EntityId.None, a.TargetId);
        Assert.True(orders.TryGetValue(a.Id, out var o) && o.Target == dest);
        Assert.True(e.Hp.Raw > 0);
    }

    [Fact]
    public void AttackMoveAcquiresNearestEnemyAndResumesRoute()
    {
        var (w, units, orders) = World();
        var a = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.Zero, Fix64.Zero), Fix64.One, Fix64.FromInt(1), Profile(0, dmg: 60));
        var enemy = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(3), Fix64.Zero), Fix64.One, Fix64.FromInt(1), Profile(1, dmg: 0));

        var dest = new FixVec2(Fix64.FromInt(9), Fix64.Zero);
        w.Step(new Command[] { new AttackMoveCommand(w.Tick, 0, new[] { a.Id }, dest, false) });
        Assert.True(a.AttackMoving);

        w.Step(Array.Empty<Command>()); // acquire + immediate shot (dmg 60 kills hp 60)
        Assert.True(units.Find(enemy.Id) == null || units.Find(enemy.Id)!.Hp.Raw <= 0);
        w.Step(Array.Empty<Command>()); // cleanup + resume
        Assert.Equal(EntityId.None, a.TargetId);
        Assert.True(orders.TryGetValue(a.Id, out var o) && o.Target == dest);
        Assert.True(a.AttackMoving); // still attack-moving to destination
    }

    [Fact]
    public void StopClearsTargetAndOrder()
    {
        var (w, units, orders) = World();
        // b beyond sight (8): after Stop, a idles and has nothing to guard against
        var a = units.Spawn(w.Ids.Next(), FixVec2.Zero, Fix64.One, Fix64.FromInt(1), Profile(0));
        var b = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(12), Fix64.Zero), Fix64.One, Fix64.FromInt(1), Profile(1, dmg: 0));

        w.Step(new Command[] { new AttackCommand(w.Tick, 0, new[] { a.Id }, b.Id, false) });
        Assert.NotEqual(EntityId.None, a.TargetId);
        w.Step(new Command[] { new StopCommand(w.Tick, 0, new[] { a.Id }) });
        Assert.Equal(EntityId.None, a.TargetId);
        Assert.False(orders.ContainsKey(a.Id));
    }

    [Fact]
    public void DeadUnitsHashStablyAcrossReplays()
    {
        static ulong Run()
        {
            var (w, units, _) = World();
            var a = units.Spawn(w.Ids.Next(), FixVec2.Zero, Fix64.One, Fix64.FromInt(1), Profile(0));
            var b = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(2), Fix64.Zero), Fix64.One, Fix64.FromInt(1), Profile(1));
            w.Step(new Command[] { new AttackCommand(w.Tick, 0, new[] { a.Id }, b.Id, false) });
            w.Step(new Command[] { new AttackCommand(w.Tick, 1, new[] { b.Id }, a.Id, false) });
            for (var i = 0; i < 40; i++) w.Step(Array.Empty<Command>());
            return w.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
