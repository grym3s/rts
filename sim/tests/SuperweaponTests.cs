using Rts.Sim.Core;
using Rts.Sim.Superweapons;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

public class SuperweaponTests
{
    private static FireCommand Fire(Arena a, Unit sw, double x, double y) =>
        new(a.W.Tick, sw.Faction, sw.Id, new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y)));

    [Fact]
    public void A_superweapon_charges_then_strikes_after_its_warning_and_charges_again()
    {
        var a = new Arena();
        var lance = a.Building("coalition-orbital-lance", 0, 8, 8);
        for (var i = 0; i < 3; i++) a.Building("coalition-reactor", 0, 4 + 3 * i, 20);
        var tank = a.Unit("bulwark", 1, 35.5, 35.5);
        tank.Hp = Fix64.FromInt(5000);
        a.Step(Fire(a, lance, 35.5, 35.5));
        Assert.Equal(0, lance.StrikeTick); // not charged yet
        a.Run(lance.Ability.ChargeTicks);
        Assert.True(SuperweaponSystem.Ready(lance, a.W.Tick));
        a.Step(Fire(a, lance, 35.5, 35.5));
        Assert.Contains(a.Seen, e => e.Kind == SimEventKind.SuperweaponFired && e.Subject == lance.Id);
        var before = tank.Hp;
        a.Run(lance.Ability.StrikeDelayTicks - 1);
        Assert.Equal(before, tank.Hp); // the telegraph: nothing lands yet
        a.Run(2);
        Assert.True(tank.Hp < before);
        Assert.Equal(lance.Ability.ChargeTicks, lance.ChargeLeft + 1);
    }

    [Fact]
    public void Charging_stops_at_low_power_and_a_barrage_lands_every_pulse()
    {
        var a = new Arena();
        var cannon = a.Building("hegemony-siege-cannon", 1, 8, 8);
        a.Run(100);
        Assert.Equal(cannon.Ability.ChargeTicks, cannon.ChargeLeft); // -150 power, nothing supplies it
        cannon.ChargeLeft = 0;
        var tank = a.Unit("lancer-mbt", 0, 30.5, 30.5);
        tank.Hp = Fix64.FromInt(50000);
        a.Step(Fire(a, cannon, 30.5, 30.5));
        a.Run(cannon.Ability.StrikeDelayTicks + cannon.Ability.PulseGapTicks * cannon.Ability.StrikePulses + 2);
        Assert.Equal(cannon.Ability.StrikePulses, a.Seen.Count(e => e.Kind == SimEventKind.Shot && e.Subject == cannon.Id && e.Target == tank.Id));
    }

    [Fact]
    public void A_placed_rift_is_built_on_site_then_charges_and_cannot_fire_before_that()
    {
        // the player's path: a place order at a base with the whole prereq chain, not a spawned building
        var a = new Arena();
        foreach (var (id, x, y) in new[] { ("ascendant-construction-yard", 10, 10), ("ascendant-forge", 16, 10), ("ascendant-nexus", 22, 10),
            ("ascendant-ascendancy", 16, 4), ("ascendant-conduit", 7, 16), ("ascendant-conduit", 10, 16), ("ascendant-conduit", 13, 16) })
            a.Building(id, 2, x, y);
        a.M.Econ.Deposit(2, 10_000);
        a.Step(new Rts.Sim.Core.PlaceBuildingCommand(a.W.Tick, 2, "ascendant-rift", new FixVec2(Fix64.FromInt(16), Fix64.FromInt(16))));
        var rift = Assert.Single(a.M.Units.Units, u => u.Building?.Id == "ascendant-rift");
        Assert.True(rift.Building!.ConstructionRemaining > 0);
        a.Step(Fire(a, rift, 30.5, 30.5));
        Assert.Equal(0, rift.StrikeTick); // a construction site never fires
        a.RunUntil(() => rift.Building.ConstructionRemaining == 0, 5000);
        Assert.Equal(rift.Ability.ChargeTicks, rift.ChargeLeft + 1); // starts charging once built
        a.Step(Fire(a, rift, 30.5, 30.5));
        Assert.Equal(0, rift.StrikeTick);
        a.Run(rift.Ability.ChargeTicks);
        a.Step(Fire(a, rift, 30.5, 30.5));
        Assert.Contains(a.Seen, e => e.Kind == SimEventKind.SuperweaponFired && e.Subject == rift.Id);
    }
}
