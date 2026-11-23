using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Match;
using Rts.Sim.Orders;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

public class MatchTests
{
    private static UnitProfile Profile(int faction, int hp = 60, int dmg = 10, int cd = 12)
        => new(faction, Fix64.FromInt(hp), Fix64.FromInt(8), Fix64.FromInt(dmg), Fix64.FromInt(4), cd,
            ArmorClass.Infantry, DamageType.Energy);

    private static (SimWorld w, UnitStore u, MatchState m) World()
    {
        var w = new SimWorld(1);
        var u = new UnitStore();
        var o = new Dictionary<EntityId, MoveOrder>();
        var m = new MatchState();
        var map = new GameMap(24, 24);
        w.Systems.Add((_, due) => OrderSystem.ApplyCommands(o, due, u.Find));
        w.Systems.Add((_, due) => Rts.Sim.Navigation.NavigationSystem.Step(map, u, o));
        w.Systems.Add((sw, due) => CombatSystem.Step(u, o, sw.Tick, sw));
        w.Systems.Add((_, due) => u.DespawnDead());
        w.Systems.Add((sw, due) => MatchSystem.Step(sw, u, m));
        return (w, u, m);
    }

    private static FixVec2 At(int x, int y) => new(Fix64.FromInt(x), Fix64.FromInt(y));

    [Fact]
    public void LastFactionStandingWinsAndOneEventIsEmitted()
    {
        var (w, units, m) = World();
        units.Spawn(w.Ids.Next(), At(2, 2), Fix64.One, Fix64.FromInt(1), Profile(0, dmg: 30));
        units.Spawn(w.Ids.Next(), At(5, 2), Fix64.One, Fix64.FromInt(1), Profile(1, hp: 30, dmg: 0));

        w.Step(Array.Empty<Command>()); // guard fires, one shot kills
        Assert.True(m.Over);
        Assert.Equal(0, m.Winner);
        Assert.Equal(0, m.EndTick);
        Assert.Contains(w.Events, e => e.Kind == SimEventKind.MatchEnded && e.Faction == 0);

        w.Step(Array.Empty<Command>());
        Assert.DoesNotContain(w.Events, e => e.Kind == SimEventKind.MatchEnded); // emitted once
        Assert.True(m.Over);
    }

    [Fact]
    public void LosingYourLastUnitLosesTheMatch()
    {
        var (w, units, m) = World();
        units.Spawn(w.Ids.Next(), At(2, 2), Fix64.One, Fix64.FromInt(1), Profile(0, hp: 10, dmg: 0));
        units.Spawn(w.Ids.Next(), At(5, 2), Fix64.One, Fix64.FromInt(1), Profile(1, dmg: 10));
        w.Step(Array.Empty<Command>());
        Assert.True(m.Over);
        Assert.Equal(1, m.Winner);
    }

    [Fact]
    public void StoreOrderDecidesAnEvenTradeAndPresetDeadDoNotCount()
    {
        var (w, units, m) = World();
        units.Spawn(w.Ids.Next(), At(2, 2), Fix64.One, Fix64.FromInt(1), Profile(0, hp: 10, dmg: 10));
        units.Spawn(w.Ids.Next(), At(5, 2), Fix64.One, Fix64.FromInt(1), Profile(1, hp: 10, dmg: 10));
        w.Step(Array.Empty<Command>());
        // unit 0 fires first and kills 1 before it can answer: not a draw under store order
        Assert.True(m.Over);
        Assert.Equal(0, m.Winner);

        var (w2, units2, m2) = World();
        units2.Spawn(w2.Ids.Next(), At(2, 2), Fix64.One, Fix64.FromInt(1), Profile(0, hp: 10, dmg: 0));
        units2.Spawn(w2.Ids.Next(), At(5, 2), Fix64.One, Fix64.FromInt(1), Profile(1, hp: 10, dmg: 0));
        units2.Units[0].Hp = Fix64.Zero; // both sides wiped in the same tick
        units2.Units[1].Hp = Fix64.Zero;
        w2.Step(Array.Empty<Command>());
        Assert.False(m2.Over); // nobody was ever seen alive: not a match yet
    }

    [Fact]
    public void BothSidesGoneAfterBeingSeenIsADraw()
    {
        var (w, units, m) = World();
        var a = units.Spawn(w.Ids.Next(), At(2, 2), Fix64.One, Fix64.FromInt(1), Profile(0, dmg: 0));
        var b = units.Spawn(w.Ids.Next(), At(20, 20), Fix64.One, Fix64.FromInt(1), Profile(1, dmg: 0));
        w.Step(Array.Empty<Command>());
        Assert.False(m.Over);
        a.Hp = Fix64.Zero;
        b.Hp = Fix64.Zero;
        w.Step(Array.Empty<Command>());
        Assert.True(m.Over);
        Assert.Equal(-1, m.Winner);
    }

    [Fact]
    public void SingleFactionWorldNeverEnds()
    {
        var (w, units, m) = World();
        units.Spawn(w.Ids.Next(), At(2, 2), Fix64.One, Fix64.FromInt(1), Profile(0));
        for (var i = 0; i < 5; i++) w.Step(Array.Empty<Command>());
        Assert.False(m.Over);
    }

    [Fact]
    public void CombatEmitsShotWithAmountAndDeathWithKiller()
    {
        var (w, units, _) = World();
        var a = units.Spawn(w.Ids.Next(), At(2, 2), Fix64.One, Fix64.FromInt(1), Profile(0, dmg: 25, cd: 1));
        var b = units.Spawn(w.Ids.Next(), At(5, 2), Fix64.One, Fix64.FromInt(1), Profile(1, hp: 40, dmg: 0));

        w.Step(Array.Empty<Command>());
        var shot = Assert.Single(w.Events, e => e.Kind == SimEventKind.Shot);
        Assert.Equal(a.Id, shot.Subject);
        Assert.Equal(b.Id, shot.Target);
        Assert.Equal(Fix64.FromInt(25), shot.Amount);
        Assert.DoesNotContain(w.Events, e => e.Kind == SimEventKind.Death);

        w.Step(Array.Empty<Command>()); // cooldown 1: fires again next tick, 15 hp left -> dead
        var death = Assert.Single(w.Events, e => e.Kind == SimEventKind.Death);
        Assert.Equal(b.Id, death.Subject);
        Assert.Equal(a.Id, death.Target);
        Assert.Equal(1, death.Faction);
    }
}
