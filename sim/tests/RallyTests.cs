using Rts.Sim.Core;
using Rts.Sim.Economy;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Production;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

/// <summary>T37: a producing building's rally point. Trained units walk there as they spawn;
/// only the owner of a live building may set it; no rally = they stay at the door; the rally
/// enters the hash only once set. Wired like tools/scenario (orders, production with the
/// rally callback, navigation).</summary>
public class RallyTests
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
        public UnitStore Units = null!;
        public Dictionary<EntityId, MoveOrder> Orders = null!;
        public EconomyStore Econ = null!;
        public GameMap Map = null!;
        public void Step(params Command[] cmds) => Sim.Step(cmds);
        public void Run(int ticks) { for (var i = 0; i < ticks; i++) Step(); }
    }

    private static W World()
    {
        var x = new W
        {
            Sim = new SimWorld(1),
            Units = new UnitStore(),
            Orders = new Dictionary<EntityId, MoveOrder>(),
            Econ = new EconomyStore(),
            Map = new GameMap(32, 32),
        };
        x.Sim.Systems.Add((_, due) => OrderSystem.ApplyCommands(x.Orders, due, x.Units.Find, (f, a) => x.Econ.TrySpend(f, a)));
        x.Sim.Systems.Add((sw, due) => ProductionSystem.Step(BuildingsCat, UnitsCat, x.Units, x.Map, x.Sim.Ids, due,
            (f, a) => x.Econ.TrySpend(f, a), (f, a) => { x.Econ.Deposit(f, a); return true; }, null, sw.Tick,
            (id, at) => x.Orders[id] = new MoveOrder(at, new List<FixVec2>(), sw.Tick)));
        x.Sim.Systems.Add((_, due) => NavigationSystem.Step(x.Map, x.Units, x.Orders));
        x.Sim.Systems.Add((_, due) => x.Units.DespawnDead());
        x.Sim.HashMixers.Add(x.Units.Hash);
        x.Sim.HashMixers.Add(h => ProductionStore.Hash(x.Units, h));
        return x;
    }

    private static FixVec2 At(int cx, int cy) => new(Fix64.FromInt(cx), Fix64.FromInt(cy));

    private static Unit Barracks(W x)
    {
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-construction-yard", At(5, 5)));
        x.Run(200);
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "coalition-barracks", At(9, 5)));
        x.Run(400);
        return x.Units.Units.Single(u => u.Building?.Id == "coalition-barracks");
    }

    private static Unit TrainOne(W x, Unit barracks)
    {
        var before = x.Units.Units.Select(u => u.Id).ToHashSet();
        x.Step(new TrainCommand(x.Sim.Tick, 0, barracks.Id, "rifleman"));
        x.Run(UnitsCat.Get("rifleman").BuildTicks + 1);
        return x.Units.Units.Single(u => !before.Contains(u.Id));
    }

    [Fact]
    public void Trained_unit_walks_to_the_rally_point()
    {
        var x = World();
        var barracks = Barracks(x);
        var rally = GameMap.CellCenter(20, 20);
        x.Step(new RallyCommand(x.Sim.Tick, 0, barracks.Id, rally));
        Assert.Equal(rally, barracks.Building!.Rally);
        var rifle = TrainOne(x, barracks);
        Assert.True(x.Orders.ContainsKey(rifle.Id), "spawned with a move order");
        x.Run(400);
        Assert.Equal((20, 20), (GameMap.CellOf(rifle.Position.X), GameMap.CellOf(rifle.Position.Y)));
    }

    [Fact]
    public void Without_a_rally_the_unit_stays_at_the_door()
    {
        var x = World();
        var barracks = Barracks(x);
        var rifle = TrainOne(x, barracks);
        Assert.False(x.Orders.ContainsKey(rifle.Id));
        var p = rifle.Position;
        x.Run(100);
        Assert.Equal(p, rifle.Position);
    }

    [Fact]
    public void Only_the_owner_of_a_live_building_sets_its_rally()
    {
        var x = World();
        var barracks = Barracks(x);
        x.Step(new RallyCommand(x.Sim.Tick, 1, barracks.Id, At(20, 20))); // enemy
        Assert.Null(barracks.Building!.Rally);
        var rifle = TrainOne(x, barracks);
        x.Step(new RallyCommand(x.Sim.Tick, 0, rifle.Id, At(20, 20))); // not a building: ignored
        Assert.Null(barracks.Building.Rally);
    }

    [Fact]
    public void Setting_a_rally_changes_the_hash_and_replays_identically()
    {
        ulong Run(bool rally)
        {
            var x = World();
            var barracks = Barracks(x);
            if (rally) x.Step(new RallyCommand(x.Sim.Tick, 0, barracks.Id, At(20, 20)));
            else x.Step();
            TrainOne(x, barracks);
            x.Run(100);
            return x.Sim.StateHash();
        }
        Assert.Equal(Run(true), Run(true));
        Assert.NotEqual(Run(true), Run(false));
    }
}
