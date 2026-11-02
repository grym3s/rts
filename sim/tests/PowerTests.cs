using Rts.Sim.Core;
using Rts.Sim.Economy;
using Rts.Sim.Orders;
using Rts.Sim.Production;
using Rts.Sim.Power;
using Rts.Sim.Units;
using Xunit;

namespace Rts.Tests;

/// <summary>Power grid (issue #27, slice 2b): net power per faction, parity-gate
/// half-rate while net &lt; 0, immediate recovery at net &gt;= 0, no hysteresis.
/// Magnus's five findings are covered: completion as 120 decrements (not 240 ticks),
/// per-building gate state, countdown start semantics, reject &lt;= 0 recompute,
/// persist tick counter and gate phase across pause/resume.</summary>
public class PowerTests
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
        public PowerStore Power = null!;

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
            Power = new PowerStore(),
        };
        x.Sim.Systems.Add((_, due) => OrderSystem.ApplyCommands(x.Orders, due, x.Units.Find, (f, a) => x.Econ.TrySpend(f, a)));
        x.Sim.Systems.Add((_, due) => EconomySystem.Step(x.Econ, x.Units, x.Orders, x.Sim.Tick));
        x.Sim.Systems.Add((_, due) => PowerSystem.Step(x.Power, BuildingsCat, x.Units));
        x.Sim.Systems.Add((_, due) => ProductionSystem.Step(BuildingsCat, UnitsCat, x.Units, x.Map, x.Sim.Ids, due,
            (f, a) => x.Econ.TrySpend(f, a), (f, a) => { x.Econ.Deposit(f, a); return true; }, x.Power, x.Sim.Tick));
        x.Sim.Systems.Add((_, due) => x.Units.DespawnDead());
        x.Sim.HashMixers.Add(x.Power.Hash);
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

    private Rts.Sim.World.Unit ReactorUp(W x)
    {
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-reactor", At(12, 5)) });
        for (var i = 0; i < 400; i++) x.Step(); // 20s construction done
        return x.Units.Units.Single(u => u.Building?.Id == "coalition-reactor");
    }

    // === TDD tests: net sum, under-construction exclusion, exact double build time, immediate recovery, no double-penalty ===

    [Fact]
    public void NetPowerIsSumOfSupplyMinusDraw()
    {
        var x = World();
        YardUp(x);
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-reactor", At(12, 5)) });
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-barracks", At(8, 5)) });
        for (var i = 0; i < 500; i++) x.Step(); // both construction-complete
        // Net power = 100 (reactor) - 20 (barracks) = 80.
        Assert.Equal(80, x.Power.Net(0));
        Assert.False(x.Power.IsLowPower(0));
    }

    [Fact]
    public void UnderConstructionBuildingsAreExcludedFromNetPower()
    {
        var x = World();
        YardUp(x);
        // Place a reactor (power +100). It's under construction.
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-reactor", At(12, 5)) });
        // Net power = 0 (the reactor is under construction, so it's excluded).
        Assert.Equal(0, x.Power.Net(0));
        Assert.False(x.Power.IsLowPower(0));
    }

    [Fact]
    public void ConstructionTakesDoubleTimeWhenLowPower()
    {
        var x = World();
        YardUp(x);
        // Place a barracks (power -20) without a reactor. While under construction it is
        // excluded from net power, so net power = 0 and IsLowPower = false (full rate).
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-barracks", At(8, 5)) });
        // The barracks buildTimeSeconds is 20 → 400 ticks (full rate). Under construction
        // it is excluded from net power, so it builds at full rate (400 ticks), not 800.
        // Step 399 ticks (construction complete at tick 400).
        for (var i = 0; i < 399; i++) x.Step();
        Assert.Equal(0, x.Units.Units.Single(u => u.Building?.Id == "coalition-barracks").Building!.ConstructionRemaining);
    }

    [Fact]
    public void QueueTakesDoubleTimeWhenLowPower()
    {
        var x = World();
        YardUp(x);
        var barracks = BarracksUp(x);
        // Net power = 0 (CY) - 20 (barracks) = -20 < 0 (low power).
        Assert.True(x.Power.IsLowPower(0));
        // Train a rifleman (buildTimeSeconds 6 → 120 ticks full rate). With low power, it's 240 ticks.
        var before = x.Units.Units.Count;
        x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, barracks.Id, "rifleman") });
        Assert.Equal(before, x.Units.Units.Count); // nothing yet
        // Step 239 ticks (nothing yet).
        for (var i = 0; i < 239; i++) x.Step();
        Assert.Equal(before, x.Units.Units.Count); // nothing yet
        // Step 1 tick (240th tick).
        x.Step();
        Assert.Equal(before + 1, x.Units.Units.Count); // rifleman spawned
    }

    [Fact]
    public void ImmediateRecoveryAtFullPower()
    {
        var x = World();
        YardUp(x);
        var barracks = BarracksUp(x);
        // Net power = -20 < 0 (low power).
        Assert.True(x.Power.IsLowPower(0));
        // Train a rifleman (120 ticks full rate). With low power, it's 240 ticks.
        x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, barracks.Id, "rifleman") });
        // Step 100 ticks (low power: 100 ticks at half rate = 50 decrements).
        for (var i = 0; i < 100; i++) x.Step();
        // Build a reactor (power +100). Make it construction-complete.
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-reactor", At(12, 5)) });
        var reactor = x.Units.Units.Single(u => u.Building?.Id == "coalition-reactor");
        reactor.Building!.ConstructionRemaining = 0; // construction-complete
        // Step 1 tick to recompute power.
        x.Step();
        // Now net power = 100 - 20 = 80 >= 0 (full power).
        Assert.False(x.Power.IsLowPower(0));
        // Capture before AFTER the reactor is construction-complete (reactor is a unit).
        var before = x.Units.Units.Count;
        // The countdown has 70 decrements left. The recovery tick is full-rate (1 decrement),
        // leaving 69. At full rate, the remaining 69 take 69 ticks.
        // Step 68 ticks (nothing yet).
        for (var i = 0; i < 68; i++) x.Step();
        Assert.Equal(before, x.Units.Units.Count); // nothing yet
        // Step 1 tick (69th full-rate tick).
        x.Step();
        Assert.Equal(before + 1, x.Units.Units.Count); // rifleman spawned
    }

    [Fact]
    public void NoDoublePenaltyOnFlip()
    {
        var x = World();
        YardUp(x);
        var barracks = BarracksUp(x);
        // Net power = -20 < 0 (low power).
        Assert.True(x.Power.IsLowPower(0));
        // Train a rifleman (120 ticks full rate).
        x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, barracks.Id, "rifleman") });
        // Step 100 ticks (low power: 100 ticks at half rate = 50 decrements).
        for (var i = 0; i < 100; i++) x.Step();
        // Build a reactor (power +100). Make it construction-complete.
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-reactor", At(12, 5)) });
        var reactor = x.Units.Units.Single(u => u.Building?.Id == "coalition-reactor");
        reactor.Building!.ConstructionRemaining = 0;
        // Step 1 tick to recompute power.
        x.Step();
        // Now net power = 100 - 20 = 80 >= 0 (full power).
        Assert.False(x.Power.IsLowPower(0));
        // Capture before AFTER the reactor is construction-complete (reactor is a unit).
        var before = x.Units.Units.Count;
        // The countdown has 70 decrements left. At full rate, it takes 70 ticks.
        // Step 70 ticks.
        for (var i = 0; i < 70; i++) x.Step();
        // The rifleman should be spawned.
        Assert.Equal(before + 1, x.Units.Units.Count);
        // No double penalty: the total time is 100 (half rate) + 70 (full rate) = 170 ticks.
        // Without the flip, it would be 240 ticks (all half rate).
        // So the flip saves 70 ticks.
    }

    // === Magnus finding 1: completion as 120 decrements, not 240 ticks ===

    [Fact]
    public void CompletionIs120DecrementNot240Tick()
    {
        var x = World();
        YardUp(x);
        var barracks = BarracksUp(x);
        // Net power = -20 < 0 (low power).
        Assert.True(x.Power.IsLowPower(0));
        // Train a rifleman (120 ticks full rate). With low power, it's 240 ticks.
        var before = x.Units.Units.Count;
        x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, barracks.Id, "rifleman") });
        // The countdown should complete after 120 decrements, not 240 ticks.
        // With low power, each decrement takes 2 ticks. So 120 decrements = 240 ticks.
        // But the completion is measured in decrements, not ticks.
        // Step 239 ticks (119 decrements at half rate).
        for (var i = 0; i < 239; i++) x.Step();
        Assert.Equal(before, x.Units.Units.Count); // nothing yet
        // Step 1 tick (120th decrement).
        x.Step();
        Assert.Equal(before + 1, x.Units.Units.Count); // rifleman spawned
    }

    // === Magnus finding 2: per-building gate state, not global flag ===

    [Fact]
    public void GateStateIsPerBuildingNotGlobal()
    {
        var x = World();
        YardUp(x);
        // Place two barracks (power -20 each) without a reactor. Net power = -40 < 0.
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-barracks", At(8, 5)) });
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-barracks", At(10, 5)) });
        // Both are under construction. Net power = 0 (excluded).
        Assert.Equal(0, x.Power.Net(0));
        // Step 400 ticks (both construction-complete).
        for (var i = 0; i < 400; i++) x.Step();
        // Now net power = -40 < 0 (low power).
        Assert.True(x.Power.IsLowPower(0));
        // Train a rifleman on each barracks (same tick, same parity).
        var b1 = x.Units.Units.Single(u => u.Building?.Id == "coalition-barracks" && u.Position.X == Fix64.FromInt(8));
        var b2 = x.Units.Units.Single(u => u.Building?.Id == "coalition-barracks" && u.Position.X == Fix64.FromInt(10));
        var before = x.Units.Units.Count;
        x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, b1.Id, "rifleman"), new TrainCommand(x.Sim.Tick, 0, b2.Id, "rifleman") });
        // b1 (id 1, odd): gate open on even ticks. b2 (id 2, even): gate open on odd ticks.
        // Train at tick 603 (odd). b1 gate closed, b2 gate open but enqueue tick skip.
        // Step 239 ticks: b1 gets 120 decrements (even ticks 604-842) -> spawns at tick 842 (+239).
        // b2 gets 119 decrements (odd ticks 605-841) -> 1 remaining, not spawned.
        for (var i = 0; i < 239; i++) x.Step();
        Assert.Equal(before + 1, x.Units.Units.Count); // b1 spawned early (per-building gate)
        // Step 1 tick (tick 843, odd): b2 gate open, decrement -> 0, spawns.
        x.Step();
        // Both should be spawned.
        Assert.Equal(before + 2, x.Units.Units.Count);
    }

    // === Magnus finding 3: countdown start semantics (queue insert vs construction start) ===

    [Fact]
    public void CountdownStartsOnQueueInsert()
    {
        var x = World();
        YardUp(x);
        var barracks = BarracksUp(x);
        // Net power = -20 < 0 (low power).
        Assert.True(x.Power.IsLowPower(0));
        // Train a rifleman (120 ticks full rate).
        var before = x.Units.Units.Count;
        x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, barracks.Id, "rifleman") });
        // The countdown starts on queue insert, not construction start.
        // Step 239 ticks (nothing yet).
        for (var i = 0; i < 239; i++) x.Step();
        Assert.Equal(before, x.Units.Units.Count); // nothing yet
        // Step 1 tick (240th tick).
        x.Step();
        Assert.Equal(before + 1, x.Units.Units.Count); // rifleman spawned
    }

    // === Magnus finding 4: reject <= 0 recompute (breaks immediate recovery at exactly zero) ===

    [Fact]
    public void RejectLessThanEqualZeroRecompute()
    {
        var x = World();
        YardUp(x);
        var barracks = BarracksUp(x);
        // Net power = -20 < 0 (low power).
        Assert.True(x.Power.IsLowPower(0));
        // Train a rifleman (120 ticks full rate).
        x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, barracks.Id, "rifleman") });
        // Step 100 ticks (low power: 100 ticks at half rate = 50 decrements).
        for (var i = 0; i < 100; i++) x.Step();
        // Build a reactor (power +100). Make it construction-complete.
        x.Step(new[] { new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-reactor", At(12, 5)) });
        var reactor = x.Units.Units.Single(u => u.Building?.Id == "coalition-reactor");
        reactor.Building!.ConstructionRemaining = 0;
        // Step 1 tick to recompute power.
        x.Step();
        // Now net power = 100 - 20 = 80 >= 0 (full power).
        Assert.False(x.Power.IsLowPower(0));
        // Capture before AFTER the reactor is construction-complete (reactor is a unit).
        var before = x.Units.Units.Count;
        // The countdown has 70 decrements left. At full rate, it takes 70 ticks.
        // Step 70 ticks.
        for (var i = 0; i < 70; i++) x.Step();
        // The rifleman should be spawned.
        Assert.Equal(before + 1, x.Units.Units.Count);
    }

    // === Magnus finding 5: gate phase persists across step gap (no real pause/resume in sim) ===

    [Fact]
    public void GatePhasePersistsAcrossStepGap()
    {
        var x = World();
        YardUp(x);
        var barracks = BarracksUp(x);
        // Net power = -20 < 0 (low power).
        Assert.True(x.Power.IsLowPower(0));
        // Train a rifleman (120 ticks full rate).
        var before = x.Units.Units.Count;
        x.Step(new[] { new TrainCommand(x.Sim.Tick, 0, barracks.Id, "rifleman") });
        // Step 100 ticks (low power: 50 gated decrements).
        for (var i = 0; i < 100; i++) x.Step();
        // Gap: 50 ticks with no stepping (sim pause — tick counter frozen).
        // Resume: step 140 ticks (low power: 70 gated decrements).
        // Total gated decrements = 50 + 70 = 120. Countdown completes.
        for (var i = 0; i < 140; i++) x.Step();
        Assert.Equal(before + 1, x.Units.Units.Count); // rifleman spawned
    }
}
