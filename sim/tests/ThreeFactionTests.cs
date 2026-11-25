using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Economy;
using Rts.Sim.Match;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Production;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

/// <summary>T27: Ascendant is faction 2, a side of its own. It has its own credits and builds only
/// its own content, its units fight both other sides, and a three-way match ends only when one
/// side is left. Wired like tools/scenario.</summary>
public class ThreeFactionTests
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
        public MatchState Match = null!;
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
            Map = new GameMap(48, 48),
            Match = new MatchState(),
        };
        x.Sim.Systems.Add((_, due) => OrderSystem.ApplyCommands(x.Orders, due, x.Units.Find, (f, a) => x.Econ.TrySpend(f, a)));
        x.Sim.Systems.Add((sw, due) => ProductionSystem.Step(BuildingsCat, UnitsCat, x.Units, x.Map, x.Sim.Ids, due,
            (f, a) => x.Econ.TrySpend(f, a), (f, a) => { x.Econ.Deposit(f, a); return true; }, null, sw.Tick));
        x.Sim.Systems.Add((_, due) => NavigationSystem.Step(x.Map, x.Units, x.Orders));
        x.Sim.Systems.Add((sw, due) => CombatSystem.Step(x.Units, x.Orders, sw.Tick, sw));
        x.Sim.Systems.Add((_, due) => x.Units.DespawnDead());
        x.Sim.Systems.Add((sw, due) => MatchSystem.Step(sw, x.Units, x.Match));
        x.Sim.HashMixers.Add(x.Units.Hash);
        x.Sim.HashMixers.Add(x.Econ.Hash);
        return x;
    }

    private static FixVec2 At(int cx, int cy) => new(Fix64.FromInt(cx), Fix64.FromInt(cy));

    private static Unit Soldier(W x, int faction, int cx, int cy) =>
        x.Units.Spawn(x.Sim.Ids.Next(), GameMap.CellCenter(cx, cy), Fix64.FromInt(2), Fix64.Ratio(3, 10),
            new UnitProfile(faction, Fix64.FromInt(100), Fix64.FromInt(8), Fix64.FromInt(20), Fix64.FromInt(5), 10,
                ArmorClass.Light, DamageType.Ap));

    [Fact]
    public void Catalogs_put_each_faction_on_its_own_index()
    {
        Assert.Equal(0, BuildingsCat.Get("coalition-construction-yard").Faction);
        Assert.Equal(1, BuildingsCat.Get("hegemony-construction-yard").Faction);
        Assert.Equal(2, BuildingsCat.Get("ascendant-construction-yard").Faction);
        Assert.Equal(2, UnitsCat.Get("seed-core").Faction);
        Assert.Throws<InvalidDataException>(() => UnitCatalog.FactionIndex("pirates"));
    }

    [Fact]
    public void Ascendant_spends_its_own_credits_and_builds_only_its_own_content()
    {
        var x = World();
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 2, "ascendant-construction-yard", At(30, 30)));
        Assert.Single(x.Units.Units, u => u.Building?.Id == "ascendant-construction-yard" && u.Faction == 2);
        Assert.True(x.Econ.Credits(2) < EconomyStore.StartingCredits);
        Assert.Equal(EconomyStore.StartingCredits, x.Econ.Credits(0));
        Assert.Equal(EconomyStore.StartingCredits, x.Econ.Credits(1));

        // Coalition may not place an Ascendant building, and Ascendant may not place a Coalition one.
        var before = x.Units.Units.Count;
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "ascendant-construction-yard", At(8, 8)));
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 2, "coalition-construction-yard", At(8, 30)));
        Assert.Equal(before, x.Units.Units.Count);
        Assert.Equal(EconomyStore.StartingCredits, x.Econ.Credits(0));
    }

    [Fact]
    public void Ascendant_units_fight_both_other_sides()
    {
        var x = World();
        var asc = Soldier(x, 2, 20, 20);
        var coal = Soldier(x, 0, 23, 20);
        var heg = Soldier(x, 1, 20, 23);
        x.Run(5);
        Assert.True(coal.Hp < coal.MaxHp || heg.Hp < heg.MaxHp, "Ascendant fired at nobody");
        Assert.True(asc.Hp < asc.MaxHp, "nobody fired at Ascendant");
    }

    [Fact]
    public void A_three_way_match_ends_only_when_one_side_is_left()
    {
        var x = World();
        var coal = Soldier(x, 0, 2, 2);
        var heg = Soldier(x, 1, 45, 2);
        var asc = Soldier(x, 2, 2, 45);
        x.Step();
        heg.Hp = Fix64.Zero;
        x.Step();
        Assert.False(x.Match.Over); // two sides still standing
        asc.Hp = Fix64.Zero;
        x.Step();
        Assert.True(x.Match.Over);
        Assert.Equal(0, x.Match.Winner);
        Assert.NotNull(x.Units.Find(coal.Id));
    }

    [Fact]
    public void Two_faction_worlds_hash_as_before_until_ascendant_credits_move()
    {
        var a = new EconomyStore();
        var b = new EconomyStore();
        a.Deposit(0, 100);
        b.Deposit(0, 100);
        Assert.Equal(a.Hash(17), b.Hash(17));
        b.Deposit(2, 1);
        Assert.NotEqual(a.Hash(17), b.Hash(17));
    }
}
