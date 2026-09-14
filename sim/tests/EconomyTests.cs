using Rts.Sim.Core;
using Rts.Sim.Economy;
using Rts.Sim.Orders;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

/// <summary>Harvest-loop semantics per docs/factions/economy.md, ported byte-exact from
/// tools/refmodel_counter/economy runs (D-001). Deposit/fill radii: sim/economy tuning consts.</summary>
public class EconomyTests
{
    private static UnitProfile Harvester(int faction = 0)
        => new(faction, Fix64.FromInt(100), Fix64.FromInt(8), Fix64.Zero, Fix64.Zero, 0,
            ArmorClass.Light, DamageType.SmallArms, Harvest: true);

    private static (SimWorld w, UnitStore u, Dictionary<EntityId, MoveOrder> o, EconomyStore e) World()
    {
        var w = new SimWorld(1);
        var u = new UnitStore();
        var o = new Dictionary<EntityId, MoveOrder>();
        var e = new EconomyStore();
        var map = new Rts.Sim.World.GameMap(24, 24);
        w.Systems.Add((_, due) => OrderSystem.ApplyCommands(o, due, u.Find, (f, a) => e.TrySpend(f, a)));
        w.Systems.Add((_, due) => EconomySystem.Step(e, u, o, w.Tick));
        w.Systems.Add((_, due) => Rts.Sim.Navigation.NavigationSystem.Step(map, u, o));
        w.Systems.Add((_, due) => u.DespawnDead());
        w.HashMixers.Add(u.Hash);
        w.HashMixers.Add(e.Hash);
        return (w, u, o, e);
    }

    [Fact]
    public void AccruesFivePerTickAndFillsIn140()
    {
        var (w, units, _, econ) = World();
        var veinPos = new FixVec2(Fix64.FromInt(5), Fix64.FromInt(5));
        econ.AddVein(veinPos, EconomyStore.FieldPool);
        units.Spawn(w.Ids.Next(), veinPos, Fix64.One, Fix64.Ratio(3, 10), Harvester());

        for (var i = 0; i < 139; i++) w.Step(Array.Empty<Command>());
        Assert.Equal(695, units.Units[0].Load);
        Assert.Equal(25000 - 695, econ.Veins[0].Pool);
        w.Step(Array.Empty<Command>());
        Assert.Equal(700, units.Units[0].Load);
        Assert.True(units.Units[0].Carrying);
    }

    [Fact]
    public void FullLoadTravelsAndDepositsExact700()
    {
        var (w, units, orders, econ) = World();
        econ.AddVein(new FixVec2(Fix64.FromInt(4), Fix64.FromInt(4)), EconomyStore.FieldPool);
        econ.Refineries.Add(new EconomyStore.Refinery(0, new FixVec2(Fix64.FromInt(12), Fix64.FromInt(4))));
        var h = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(4), Fix64.FromInt(4)), Fix64.Ratio(3, 1), Fix64.Ratio(3, 10), Harvester());

        for (var i = 0; i < 140; i++) w.Step(Array.Empty<Command>()); // fill, become carrying
        Assert.True(h.Carrying);
        w.Step(Array.Empty<Command>()); // next economy step: carrying => issued trip order
        Assert.True(orders.ContainsKey(h.Id)); // heading to the refinery

        // travel 8 cells at speed 1.3, deposit radius 2: assert it happens, not the tick count
        var start = econ.Credits(0);
        for (var i = 0; i < 400 && h.Carrying; i++) w.Step(Array.Empty<Command>());
        Assert.False(h.Carrying);
        Assert.Equal(700, econ.Credits(0) - start);
        Assert.Equal(0, h.Load);
    }

    [Fact]
    public void DepletedVeinStopsIncomeAndRidesTheLastPartial()
    {
        var (w, units, _, econ) = World();
        econ.AddVein(new FixVec2(Fix64.FromInt(5), Fix64.FromInt(5)), 750); // 150 ticks
        units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(5), Fix64.FromInt(5)), Fix64.One, Fix64.Ratio(3, 10), Harvester());

        for (var i = 0; i < 400; i++) w.Step(Array.Empty<Command>());
        var u = units.Units[0];
        // 700 fills, becomes carrying; with no refinery it banks on the unit forever
        // (ref model: "banked on unit"), fill stops, 50 stays in the depleted-approaching pool
        Assert.Equal(700, u.Load);
        Assert.True(u.Carrying);
        Assert.Equal(50, econ.Veins[0].Pool);
        Assert.Equal(5000, econ.Credits(0)); // nothing deposited: no refinery on the map
    }

    [Fact]
    public void SpendIsAtomicAndClampsToBalance()
    {
        var (w, _, _, econ) = World();
        Assert.True(econ.TrySpend(0, 4000));
        Assert.Equal(1000, econ.Credits(0));
        Assert.False(econ.TrySpend(0, 1001)); // short: nothing moves
        Assert.Equal(1000, econ.Credits(0));
        // through the command path (step 1)
        w.Step(new Command[] { new SpendCommand(w.Tick, 0, 1000) });
        Assert.Equal(0, econ.Credits(0));
        Assert.Equal(EconomyStore.StartingCredits, econ.Credits(1)); // per-faction
    }

    [Fact]
    public void DespawnedHarvesterStopsEarning()
    {
        var (w, units, _, econ) = World();
        var veinPos = new FixVec2(Fix64.FromInt(5), Fix64.FromInt(5));
        econ.AddVein(veinPos, EconomyStore.FieldPool);
        units.Spawn(w.Ids.Next(), veinPos, Fix64.One, Fix64.Ratio(3, 10), Harvester());
        var b = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(5), Fix64.FromInt(6)), Fix64.One, Fix64.Ratio(3, 10), Harvester());

        for (var i = 0; i < 50; i++) w.Step(Array.Empty<Command>());
        Assert.Equal(2 * 50 * 5, EconomyStore.FieldPool - econ.Veins[0].Pool); // two earning
        b.Hp = Fix64.Zero; // combat kills it; cleanup despawns at step 6
        w.Step(Array.Empty<Command>());
        var drain = EconomyStore.FieldPool - econ.Veins[0].Pool;
        for (var i = 0; i < 50; i++) w.Step(Array.Empty<Command>());
        Assert.Equal(drain + 50 * 5, EconomyStore.FieldPool - econ.Veins[0].Pool); // half rate: b is gone
    }

    [Fact]
    public void CatalogFlagsHarvestersByRole()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "content", "units"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var catalog = Rts.Sim.Units.UnitCatalog.LoadFromDirectory(Path.Combine(dir!.FullName, "content", "units"));
        Assert.True(catalog.Get("collector").Harvest);
        Assert.True(catalog.Get("miner").Harvest);
        Assert.True(catalog.Get("cultivator").Harvest);
        Assert.False(catalog.Get("rifleman").Harvest);
    }

    [Fact]
    public void ReplayHashStableWithEconomy()
    {
        var (w1, u1, _, e1) = World();
        e1.AddVein(new FixVec2(Fix64.FromInt(4), Fix64.FromInt(4)), 5000);
        e1.AddVein(new FixVec2(Fix64.FromInt(8), Fix64.FromInt(8)), 3000);
        e1.Refineries.Add(new EconomyStore.Refinery(0, new FixVec2(Fix64.FromInt(12), Fix64.FromInt(4))));
        u1.Spawn(w1.Ids.Next(), new FixVec2(Fix64.FromInt(4), Fix64.FromInt(4)), Fix64.Ratio(3, 1), Fix64.Ratio(3, 10), Harvester());
        u1.Spawn(w1.Ids.Next(), new FixVec2(Fix64.FromInt(8), Fix64.FromInt(8)), Fix64.Ratio(3, 1), Fix64.Ratio(3, 10), Harvester());
        for (var i = 0; i < 300; i++) w1.Step(Array.Empty<Command>());

        var (w2, u2, _, e2) = World();
        e2.AddVein(new FixVec2(Fix64.FromInt(4), Fix64.FromInt(4)), 5000);
        e2.AddVein(new FixVec2(Fix64.FromInt(8), Fix64.FromInt(8)), 3000);
        e2.Refineries.Add(new EconomyStore.Refinery(0, new FixVec2(Fix64.FromInt(12), Fix64.FromInt(4))));
        u2.Spawn(w2.Ids.Next(), new FixVec2(Fix64.FromInt(4), Fix64.FromInt(4)), Fix64.Ratio(3, 1), Fix64.Ratio(3, 10), Harvester());
        u2.Spawn(w2.Ids.Next(), new FixVec2(Fix64.FromInt(8), Fix64.FromInt(8)), Fix64.Ratio(3, 1), Fix64.Ratio(3, 10), Harvester());
        for (var i = 0; i < 300; i++) w2.Step(Array.Empty<Command>());

        Assert.Equal(w1.StateHash(), w2.StateHash());
        Assert.NotEqual(0ul, w1.StateHash());
    }
}
