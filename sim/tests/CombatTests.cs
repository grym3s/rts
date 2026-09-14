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
        var a = units.Spawn(w.Ids.Next(), FixVec2.Zero, Fix64.One, Fix64.FromInt(1), Profile(0));
        var b = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(3), Fix64.Zero), Fix64.One, Fix64.FromInt(1), Profile(1, dmg: 0));

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
