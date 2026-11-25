using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Economy;
using Rts.Sim.Match;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Power;
using Rts.Sim.Production;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

/// <summary>T28 captures: an engineer touching an enemy building or MCV takes it (consumed,
/// instant). A captured building fields its own units for the new owner; owning another tree's
/// yard or MCV opens that tree's buildings; a neutral oil derrick nobody can shoot drips credits
/// once captured. Wired like tools/scenario.</summary>
public class CaptureTests
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
        public PowerStore Power = null!;
        public GameMap Map = null!;
        public MatchState Match = null!;
        public List<SimEvent> Seen = new();
        public void Step(params Command[] cmds) => Sim.Step(cmds);
        public void RunUntil(Func<bool> done, int max = 2000) { for (var i = 0; i < max && !done(); i++) Step(); }
    }

    private static W World()
    {
        var x = new W
        {
            Sim = new SimWorld(1),
            Units = new UnitStore(),
            Orders = new Dictionary<EntityId, MoveOrder>(),
            Econ = new EconomyStore(),
            Power = new PowerStore(),
            Map = new GameMap(40, 40),
            Match = new MatchState(),
        };
        x.Sim.Systems.Add((_, due) => PowerSystem.Step(x.Power, BuildingsCat, x.Units));
        x.Sim.Systems.Add((w, due) => EconomySystem.DripStep(x.Econ, x.Units, id => BuildingsCat.Get(id).OilIncome, w.Tick));
        x.Sim.Systems.Add((_, due) => OrderSystem.ApplyCommands(x.Orders, due, x.Units.Find, (f, a) => x.Econ.TrySpend(f, a)));
        x.Sim.Systems.Add((w, due) => ProductionSystem.Step(BuildingsCat, UnitsCat, x.Units, x.Map, x.Sim.Ids, due,
            (f, a) => x.Econ.TrySpend(f, a), (f, a) => { x.Econ.Deposit(f, a); return true; }, x.Power, w.Tick, null, w));
        x.Sim.Systems.Add((_, due) => NavigationSystem.Step(x.Map, x.Units, x.Orders));
        x.Sim.Systems.Add((w, due) => CombatSystem.Step(x.Units, x.Orders, w.Tick, w));
        x.Sim.Systems.Add((w, due) => CaptureSystem.Step(x.Map, x.Units, x.Orders, w.Tick, w, (f, id) => x.Econ.Deposit(f, UnitsCat.Get(id).Cost)));
        x.Sim.Systems.Add((w, due) => x.Seen.AddRange(w.Events));
        x.Sim.Systems.Add((_, due) => x.Units.DespawnDead());
        x.Sim.Systems.Add((w, due) => MatchSystem.Step(w, x.Units, x.Match));
        x.Sim.HashMixers.Add(x.Units.Hash);
        return x;
    }

    private static Unit Spawn(W x, string id, int faction, int cx, int cy)
    {
        var p = UnitsCat.Get(id);
        return x.Units.Spawn(x.Sim.Ids.Next(), GameMap.CellCenter(cx, cy), p.Speed, p.Radius, p.Profile() with { Faction = faction });
    }

    /// <summary>A finished building of `id` for `faction`, centred on the cell (scenario bootstrap).</summary>
    private static Unit Building(W x, string id, int faction, int cx, int cy)
    {
        var b = ProductionSystem.SpawnStructure(BuildingsCat, x.Units, x.Map, x.Sim.Ids, id, cx, cy);
        b.Faction = faction; // test setup only: as if it had been built by `faction`
        return b;
    }

    private static CaptureCommand Capture(W x, Unit target, params Unit[] engineers) =>
        new(x.Sim.Tick, engineers[0].Faction, engineers.Select(e => e.Id).ToArray(), target.Id);

    [Fact]
    public void An_engineer_walks_to_an_enemy_building_takes_it_and_is_used_up()
    {
        var x = World();
        var hall = Building(x, "hegemony-conscription-hall", 1, 20, 20);
        var eng = Spawn(x, "engineer", 0, 5, 20);
        x.Step(Capture(x, hall, eng));
        x.RunUntil(() => hall.Faction == 0);
        Assert.Equal(0, hall.Faction);
        Assert.Equal(hall.MaxHp, hall.Hp);
        var ev = Assert.Single(x.Seen, e => e.Kind == SimEventKind.Captured);
        Assert.Equal(hall.Id, ev.Subject);
        Assert.Equal(eng.Id, ev.Target);
        Assert.Equal(0, ev.Faction);
        x.Step();
        Assert.Null(x.Units.Find(eng.Id)); // consumed
        Assert.DoesNotContain(x.Seen, e => e.Kind == SimEventKind.Death && e.Subject == eng.Id);
    }

    [Fact]
    public void A_captured_barracks_trains_its_own_units_for_the_new_owner()
    {
        var x = World();
        var hall = Building(x, "hegemony-conscription-hall", 1, 20, 20);
        x.Step(new TrainCommand(x.Sim.Tick, 0, hall.Id, "conscript")); // not ours yet: refused
        Assert.Empty(hall.Building!.Queue);
        x.Step(new TrainCommand(x.Sim.Tick, 1, hall.Id, "conscript")); // Hegemony queues one...
        Assert.Single(hall.Building!.Queue);
        var heg = x.Econ.Credits(1);
        var eng = Spawn(x, "engineer", 0, 18, 20); // right next to it
        x.Step(Capture(x, hall, eng));
        Assert.Equal(0, hall.Faction);
        Assert.Empty(hall.Building!.Queue);                                      // ...lost with the hall
        Assert.Equal(heg + UnitsCat.Get("conscript").Cost, x.Econ.Credits(1));   // and refunded to Hegemony

        x.Step(new TrainCommand(x.Sim.Tick, 0, hall.Id, "conscript"));
        Assert.Single(hall.Building!.Queue);
        x.RunUntil(() => x.Units.Units.Any(u => u.TypeId == "conscript"), 1000);
        var trained = x.Units.Units.Single(u => u.TypeId == "conscript");
        Assert.Equal(0, trained.Faction);                                        // a Coalition conscript
        Assert.True(trained.Conscriptable);
        // and a Hegemony-only unit from some other building is still not on offer
        x.Step(new TrainCommand(x.Sim.Tick, 0, hall.Id, "bulwark"));
        Assert.Empty(hall.Building!.Queue);
    }

    [Fact]
    public void A_captured_mcv_opens_its_whole_tree_while_you_hold_it_or_its_yard()
    {
        var x = World();
        Building(x, "coalition-construction-yard", 0, 5, 5);
        var mcv = Spawn(x, "foundry-crawler", 1, 12, 5);
        FixVec2 At(int cx, int cy) => GameMap.CellCenter(cx, cy);
        var check = () => ProductionSystem.CheckPlace(BuildingsCat, x.Units, x.Map, 0, "hegemony-furnace", At(9, 9), 99999).Verdict;
        Assert.Equal(ProductionSystem.PlaceVerdict.WrongFaction, check());
        Assert.Equal(new[] { 0 }, ProductionSystem.Trees(BuildingsCat, x.Units, 0));

        var eng = Spawn(x, "engineer", 0, 11, 5);
        x.Step(Capture(x, mcv, eng));
        Assert.Equal(0, mcv.Faction);
        Assert.Equal(new[] { 0, 1 }, ProductionSystem.Trees(BuildingsCat, x.Units, 0));
        Assert.Equal(ProductionSystem.PlaceVerdict.Prereq, check()); // tree open, but no Hegemony yard yet

        x.Step(new DeployCommand(x.Sim.Tick, 0, mcv.Id));
        var yard = x.Units.Units.Single(u => u.Building?.Id == "hegemony-construction-yard");
        Assert.Equal(0, yard.Faction);
        Assert.Equal(ProductionSystem.PlaceVerdict.Ok, check());
        x.RunUntil(() => yard.Building!.ConstructionRemaining == 0);
        var before = x.Econ.Credits(0);
        x.Step(new PlaceBuildingCommand(x.Sim.Tick, 0, "hegemony-furnace", At(9, 9)));
        var furnace = x.Units.Units.Single(u => u.Building?.Id == "hegemony-furnace");
        Assert.Equal(0, furnace.Faction);
        Assert.Equal(before - BuildingsCat.Get("hegemony-furnace").Cost, x.Econ.Credits(0));

        yard.Hp = Fix64.Zero; // lose the yard: the tree closes again (what you built stays yours)
        x.Step();
        Assert.Equal(ProductionSystem.PlaceVerdict.WrongFaction, check());
        Assert.NotNull(x.Units.Find(furnace.Id));
    }

    [Fact]
    public void The_neutral_derrick_is_nobodys_target_and_pays_its_captor_with_diminishing_returns()
    {
        var x = World();
        var d1 = ProductionSystem.SpawnStructure(BuildingsCat, x.Units, x.Map, x.Sim.Ids, "neutral-oil-derrick", 20, 20);
        var d2 = ProductionSystem.SpawnStructure(BuildingsCat, x.Units, x.Map, x.Sim.Ids, "neutral-oil-derrick", 30, 20);
        Assert.Equal(Factions.Neutral, d1.Faction);
        Assert.Equal(0, d1.Building!.ConstructionRemaining);
        var tank = Spawn(x, "lancer-mbt", 0, 17, 20);
        var foe = Spawn(x, "lancer-mbt", 1, 33, 33);
        x.Step(new AttackCommand(x.Sim.Tick, 0, new[] { tank.Id }, d1.Id, false));
        for (var i = 0; i < 60; i++) x.Step();
        Assert.Equal(d1.MaxHp, d1.Hp);              // can't be shot
        Assert.False(x.Match.Over);                  // and it is not a side
        var start0 = x.Econ.Credits(0);
        for (var i = 0; i < 40; i++) x.Step();
        Assert.Equal(start0, x.Econ.Credits(0));     // unowned: pays nobody

        var e1 = Spawn(x, "engineer", 0, 18, 20);
        var e2 = Spawn(x, "engineer", 0, 28, 20);
        x.Step(Capture(x, d1, e1), Capture(x, d2, e2));
        Assert.Equal(0, d1.Faction);
        Assert.Equal(0, d2.Faction);
        while (x.Sim.Tick % SimWorld.TicksPerSecond != 0) x.Step();
        var c = x.Econ.Credits(0);
        for (var i = 0; i < SimWorld.TicksPerSecond * 10; i++) x.Step();
        Assert.Equal(c + 10 * (5 + 5 * 70 / 100), x.Econ.Credits(0)); // 5/s, then 70% of 5 (floor 3)

        // once owned it is an ordinary building: the enemy can shoot it
        x.Step(new AttackCommand(x.Sim.Tick, 1, new[] { foe.Id }, d2.Id, false));
        x.RunUntil(() => d2.Hp < d2.MaxHp, 600);
        Assert.True(d2.Hp < d2.MaxHp);
    }

    [Fact]
    public void Only_engineers_capture_and_only_buildings_or_mcvs_of_someone_else()
    {
        var x = World();
        var hall = Building(x, "hegemony-conscription-hall", 1, 20, 20);
        var own = Building(x, "coalition-construction-yard", 0, 5, 5);
        var weaver = Spawn(x, "weaver", 2, 18, 20);
        var rifle = Spawn(x, "rifleman", 0, 3, 36);
        var foe = Spawn(x, "conscript", 1, 36, 36);
        var e1 = Spawn(x, "engineer", 0, 8, 8);
        var e2 = Spawn(x, "engineer", 0, 3, 30);
        Assert.True(weaver.CannotCapture);
        x.Step(Capture(x, hall, weaver), Capture(x, hall, rifle), Capture(x, own, e1), Capture(x, foe, e2));
        x.Step();
        Assert.Equal(1, hall.Faction);
        Assert.Equal(EntityId.None, weaver.CaptureTarget);
        Assert.Equal(EntityId.None, rifle.CaptureTarget);
        Assert.Equal(EntityId.None, e1.CaptureTarget); // already ours
        Assert.Equal(EntityId.None, e2.CaptureTarget); // a soldier is not capturable
        Assert.True(e1.Hp.Raw > 0 && e2.Hp.Raw > 0);

        // any other order cancels the walk
        x.Step(Capture(x, hall, e1));
        Assert.Equal(hall.Id, e1.CaptureTarget);
        x.Step(new StopCommand(x.Sim.Tick, 0, new[] { e1.Id }));
        Assert.Equal(EntityId.None, e1.CaptureTarget);
    }

    [Fact]
    public void Ownership_enters_the_hash_only_once_something_changes_hands()
    {
        var x = World();
        var hall = ProductionSystem.SpawnStructure(BuildingsCat, x.Units, x.Map, x.Sim.Ids, "hegemony-conscription-hall", 20, 20);
        var before = x.Units.Hash(7);
        hall.Faction = 0;
        Assert.NotEqual(before, x.Units.Hash(7));
        hall.Faction = 1; // back with its first owner: hashes as if it never moved
        Assert.Equal(before, x.Units.Hash(7));
    }

    [Fact]
    public void Neutrals_are_not_a_side_in_the_match()
    {
        var x = World();
        ProductionSystem.SpawnStructure(BuildingsCat, x.Units, x.Map, x.Sim.Ids, "neutral-oil-derrick", 20, 20);
        var a = Spawn(x, "rifleman", 0, 5, 5);
        var b = Spawn(x, "rifleman", 1, 35, 35);
        x.Step();
        Assert.False(x.Match.Over);
        b.Hp = Fix64.Zero;
        x.Step();
        Assert.True(x.Match.Over);
        Assert.Equal(0, x.Match.Winner);
        Assert.NotNull(a);
    }
}
