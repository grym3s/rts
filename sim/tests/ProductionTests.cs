using Rts.Sim.Core;
using Rts.Sim.Economy;
using Rts.Sim.Orders;
using Rts.Sim.Production;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Tests;

/// <summary>Buildings + production queues (issue #25): prereqs, atomic spend, construction,
/// queue timing, rally spawn, refunds, raid-destroyed queues. Driven by real content/ catalogs,
/// composed exactly like tools/scenario (step 1 orders -> step 2 economy+production -> cleanup).</summary>
public class ProductionTests
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
        public Rts.Sim.World.UnitStore Units = null!;
        public Dictionary<EntityId, MoveOrder> Orders = null!;
        public EconomyStore Econ = null!;
        public Rts.Sim.World.GameMap Map = null!;

        public void Step(Command[]? cmds = null) => Sim.Step(cmds ?? Array.Empty<Command>());
    }

    private static W World()
    {
        var x = new W
        {
            Sim = new SimWorld(1),
            Units = new Rts.Sim.World.UnitStore(),
            Orders = new Dictionary<EntityId, MoveOrder>(),
            Econ = new EconomyStore(),
            Map = new Rts.Sim.World.GameMap(24, 24),
        };
        x.Sim.Systems.Add((_, due) => OrderSystem.ApplyCommands(x.Orders, due, x.Units.Find, (f, a) => x.Econ.TrySpend(f, a)));
        x.Sim.Systems.Add((_, due) => EconomySystem.Step(x.Econ, x.Units, x.Orders, x.Sim.Tick));
        x.Sim.Systems.Add((_, due) => ProductionSystem.Step(BuildingsCat, UnitsCat, x.Units, x.Map, x.Sim.Ids, due,
            (f, a) => x.Econ.TrySpend(f, a), (f, a) => { x.Econ.Deposit(f, a); return true; }));
        x.Sim.Systems.Add((_, due) => x.Units.DespawnDead());
        return x;
    }

    private static FixVec2 At(int x, int y) => new(Fix64.FromInt(x), Fix64.FromInt(y));

    private void YardUp(W x)
    {
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-construction-yard", At(5, 5)) });
        for (var i = 0; i < 200; i++) x.Step(); // 10s construction done
    }

    private Rts.Sim.World.Unit BarracksUp(W x)
    {
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-barracks", At(8, 5)) });
        for (var i = 0; i < 400; i++) x.Step(); // 20s construction done
        return x.Units.Units.Single(u => u.Building?.Id == "coalition-barracks");
    }

    [Fact]
    public void PlaceRejectsMissingPrereqAndSpendsNothing()
    {
        var x = World();
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-barracks", At(8, 8)) });
        Assert.Equal(EconomyStore.StartingCredits, x.Econ.Credits(0));
        Assert.DoesNotContain(x.Units.Units, u => u.Building != null);
    }

    [Fact]
    public void PlaceYardSpendsOnceAndStartsConstruction()
    {
        var x = World();
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-construction-yard", At(5, 5)) });
        Assert.Equal(2500, x.Econ.Credits(0));
        var yard = x.Units.Units.Single(u => u.Building != null);
        Assert.Equal(199, yard.Building!.ConstructionRemaining); // 10s x 20, first decrement lands on the placement tick
    }

    [Fact]
    public void ConstructionCompletesExactlyAfterBuildTicks()
    {
        var x = World();
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-construction-yard", At(5, 5)) });
        for (var i = 0; i < 198; i++) x.Step();
        Assert.Equal(1, x.Units.Units.Single(u => u.Building != null).Building!.ConstructionRemaining);
        x.Step(); // 200 economy steps counting the placement step = exactly 10s
        Assert.Equal(0, x.Units.Units.Single(u => u.Building != null).Building!.ConstructionRemaining);
    }

    [Fact]
    public void TrainSpendsOnEnqueueAndSpawnsOnTheSixtiethTick()
    {
        var x = World();
        YardUp(x);
        var barracks = BarracksUp(x);
        Assert.Equal(5000 - 2500 - 400, x.Econ.Credits(0));

        var before = x.Units.Units.Count;
        x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, barracks.Id, "rifleman") }); // 6s = 120t, 125cr
        Assert.Equal(5000 - 2500 - 400 - 125, x.Econ.Credits(0)); // spent immediately
        Assert.Equal(before, x.Units.Units.Count); // nothing yet

        for (var i = 0; i < 118; i++) x.Step();
        Assert.Equal(before, x.Units.Units.Count);
        x.Step(); // 120th economy tick after the order
        Assert.Equal(before + 1, x.Units.Units.Count);
        Assert.Contains(x.Units.Units, u => u.Speed == UnitsCat.Get("rifleman").Speed && u.Building == null);
    }

    [Fact]
    public void RallySpawnPicksFirstFreeRingCellDeterministically()
    {
        var x = World();
        // ring r=1 around (5,5) scans (4,4),(5,4),(6,4),... : occupy the first two -> third wins
        x.Units.Spawn(x.Sim.Ids.Next(), At(4, 4), Fix64.One, Fix64.Ratio(3, 10));
        x.Units.Spawn(x.Sim.Ids.Next(), At(5, 4), Fix64.One, Fix64.Ratio(3, 10));
        Assert.Equal(At(6, 4), ProductionRules.FindSpawnCell(x.Map, x.Units, At(5, 5)));
        // blocked map cells are skipped too
        x.Map.Block(6, 4);
        Assert.Equal(At(4, 5), ProductionRules.FindSpawnCell(x.Map, x.Units, At(5, 5)));
    }

    [Fact]
    public void QueueCapsAtFiveAndOverCapTrainsSpendNothing()
    {
        var x = World();
        YardUp(x);
        var barracks = BarracksUp(x);
        for (var i = 0; i < 5; i++)
            x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, barracks.Id, "rifleman") });
        Assert.Equal(5, barracks.Building!.Queue.Count);
        Assert.Equal(2100 - 5 * 125, x.Econ.Credits(0));
        var before = x.Econ.Credits(0);
        x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, barracks.Id, "rifleman") });
        Assert.Equal(5, barracks.Building!.Queue.Count);
        Assert.Equal(before, x.Econ.Credits(0)); // capped: no spend
    }

    [Fact]
    public void CancelRefundsTheNamedSlot()
    {
        var x = World();
        YardUp(x);
        var barracks = BarracksUp(x);
        for (var i = 0; i < 2; i++)
            x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, barracks.Id, "rifleman") });
        Assert.Equal(2100 - 250, x.Econ.Credits(0));
        x.Step(new[] { new CancelProductionCommand(x.Sim.Tick, 0, barracks.Id, 1) });
        Assert.Single(barracks.Building!.Queue);
        Assert.Equal(2100 - 125, x.Econ.Credits(0));
    }

    [Fact]
    public void DestroyedBuildingRefundsQueueOnce()
    {
        var x = World();
        YardUp(x);
        var barracks = BarracksUp(x);
        for (var i = 0; i < 3; i++)
            x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, barracks.Id, "rifleman") });
        var afterTrains = x.Econ.Credits(0);

        barracks.Hp = Fix64.Zero; // raid!
        x.Step();
        Assert.Equal(afterTrains + 3 * 125, x.Econ.Credits(0)); // all three back
        x.Step();
        Assert.Equal(afterTrains + 3 * 125, x.Econ.Credits(0)); // no double refund after cleanup
        Assert.DoesNotContain(x.Units.Units, u => u.Building?.Id == "coalition-barracks");
    }

    [Fact]
    public void TrainRejectsWrongBuildingFactionAndUnderConstruction()
    {
        var x = World();
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-construction-yard", At(5, 5)) });
        var yard = x.Units.Units.Single(u => u.Building != null);
        var credits = x.Econ.Credits(0);

        x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, yard.Id, "rifleman") }); // under construction
        Assert.Equal(credits, x.Econ.Credits(0));

        for (var i = 0; i < 200; i++) x.Step();
        x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, yard.Id, "rifleman") }); // yard fields no units (builtFrom)
        Assert.Equal(credits, x.Econ.Credits(0));
        x.Step(new[] { new TrainCommand(x.Sim.Tick, 1, yard.Id, "rifleman") }); // wrong faction
        Assert.Equal(credits, x.Econ.Credits(0));
    }
}
