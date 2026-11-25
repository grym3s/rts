using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Economy;
using Rts.Sim.Orders;
using Rts.Sim.Production;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

/// <summary>T29 owned verbs (docs/factions/owned-verbs.md): Coalition Marking (+25% from
/// Coalition on marked targets; on-hit 2 s, aura while in radius + 1.5 s, aura gated on a finished
/// Sensor Command), Hegemony Conscription (cash in infantry: 50% near a hall, 30% in the field;
/// a Sapper detonates), Ascendant Phase (2 s untargetable and can't fire, 10 s cooldown after).</summary>
public class OwnedVerbsTests
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
        public List<SimEvent> Events = new();
        public void Step(params Command[] cmds)
        {
            Sim.Step(cmds);
            Events.AddRange(Sim.Events);
        }
        public void Run(int n) { for (var i = 0; i < n; i++) Step(); }
    }

    private static W World()
    {
        var x = new W { Sim = new SimWorld(1), Units = new UnitStore(), Orders = new(), Econ = new EconomyStore() };
        var map = new GameMap(48, 48);
        x.Sim.Systems.Add((_, due) => OrderSystem.ApplyCommands(x.Orders, due, x.Units.Find));
        x.Sim.Systems.Add((sw, due) => ProductionSystem.Step(BuildingsCat, UnitsCat, x.Units, map, sw.Ids, due,
            (f, a) => x.Econ.TrySpend(f, a), (f, a) => { x.Econ.Deposit(f, a); return true; }, null, sw.Tick, null, sw));
        x.Sim.Systems.Add((sw, due) => CombatSystem.Step(x.Units, x.Orders, sw.Tick, sw));
        x.Sim.Systems.Add((_, due) => x.Units.DespawnDead());
        return x;
    }

    private static Unit Spawn(W x, string id, int faction, double cx, double cy)
    {
        var p = UnitsCat.Get(id);
        return x.Units.Spawn(x.Sim.Ids.Next(), new FixVec2(Fix64.FromDouble(cx), Fix64.FromDouble(cy)), p.Speed, p.Radius,
            p.Profile() with { Faction = faction });
    }

    /// <summary>A plain gun: 10 AP damage, range 5, fires every 10 ticks, hits everything.</summary>
    private static Unit Gun(W x, int faction, double cx, double cy, bool marks = false) =>
        x.Units.Spawn(x.Sim.Ids.Next(), new FixVec2(Fix64.FromDouble(cx), Fix64.FromDouble(cy)), Fix64.Zero, Fix64.Ratio(3, 10),
            new UnitProfile(faction, Fix64.FromInt(10000), Fix64.FromInt(8), Fix64.FromInt(10), Fix64.FromInt(5), 10,
                ArmorClass.Heavy, DamageType.Ap, MarksOnHit: marks));

    private static Unit Dummy(W x, int faction, double cx, double cy) =>
        x.Units.Spawn(x.Sim.Ids.Next(), new FixVec2(Fix64.FromDouble(cx), Fix64.FromDouble(cy)), Fix64.Zero, Fix64.Ratio(3, 10),
            new UnitProfile(faction, Fix64.FromInt(100000), Fix64.Zero, Fix64.Zero, Fix64.Zero, 0, ArmorClass.Heavy));

    private static Unit FinishedBuilding(W x, string id, int faction, double cx, double cy)
    {
        var bp = BuildingsCat.Get(id);
        var b = x.Units.Spawn(x.Sim.Ids.Next(), new FixVec2(Fix64.FromDouble(cx), Fix64.FromDouble(cy)), Fix64.Zero, Fix64.Ratio(3, 10),
            new UnitProfile(faction, bp.Hp, Fix64.Zero, Fix64.Zero, Fix64.Zero, 0, ArmorClass.Structure, TypeId: id, MarkRadius: bp.MarkRadius));
        b.Building = new BuildingState { Id = id, ConstructionRemaining = 0 };
        return b;
    }

    private static List<Fix64> ShotsAt(W x, Unit target) =>
        x.Events.Where(e => e.Kind == SimEventKind.Shot && e.Target == target.Id).Select(e => e.Amount).ToList();

    [Fact]
    public void Content_carries_each_factions_verb()
    {
        Assert.True(UnitsCat.Get("rifleman").MarksOnHit);
        Assert.Equal(Fix64.FromInt(8), UnitsCat.Get("comms-rig").MarkRadius);
        Assert.Equal(Fix64.FromInt(7), UnitsCat.Get("spotter-drone").MarkRadius);
        Assert.Equal(Fix64.FromInt(14), BuildingsCat.Get("coalition-sensor-command").MarkRadius);
        Assert.True(BuildingsCat.Get("hegemony-conscription-hall").ConscriptionHall);
        Assert.True(UnitsCat.Get("conscript").Conscriptable);
        Assert.True(UnitsCat.Get("sapper").Detonates);
        Assert.False(UnitsCat.Get("conscript").Detonates);
        Assert.True(UnitsCat.Get("adept").Phases);
        Assert.False(UnitsCat.Get("rifleman").Phases);
        Assert.Equal(37, OwnedVerbs.Refund(75, atHall: true)); // canon: Conscript 75 -> +37
    }

    [Fact]
    public void A_marking_hit_makes_the_next_coalition_shots_hit_a_quarter_harder()
    {
        var x = World();
        var marker = Gun(x, 0, 10, 10, marks: true);
        var target = Dummy(x, 1, 13, 10);
        x.Step(new AttackCommand(0, 0, new[] { marker.Id }, target.Id, false));
        x.Run(25);
        var shots = ShotsAt(x, target);
        Assert.True(shots.Count >= 2);
        Assert.Equal(shots[0] * OwnedVerbs.MarkBonus, shots[1]);
        Assert.True(target.IsMarked(x.Sim.Tick) && target.MarkedBy == 0);
    }

    [Fact]
    public void A_mark_only_helps_the_faction_that_made_it_and_wears_off()
    {
        var x = World();
        var target = Dummy(x, 1, 13, 10);
        OwnedVerbs.Mark(target, 0, 5);
        var other = Gun(x, 2, 10, 10); // Ascendant: not the marker
        x.Step(new AttackCommand(0, 2, new[] { other.Id }, target.Id, false));
        x.Run(25);
        var shots = ShotsAt(x, target);
        Assert.True(shots.Count >= 2);
        Assert.All(shots, s => Assert.Equal(shots[0], s));
        Assert.False(target.IsMarked(x.Sim.Tick));
    }

    [Fact]
    public void Aura_marks_need_the_sensor_network_and_linger_after_the_source_dies()
    {
        var x = World();
        var rig = Spawn(x, "comms-rig", 0, 10, 10);
        var near = Dummy(x, 1, 16, 10);   // 6 away: inside the rig's 8
        var far = Dummy(x, 1, 20, 10);    // 10 away: outside
        x.Run(2);
        Assert.False(near.IsMarked(x.Sim.Tick)); // no Sensor Command yet: no network

        FinishedBuilding(x, "coalition-sensor-command", 0, 40, 40); // far from both
        x.Run(2);
        Assert.True(near.IsMarked(x.Sim.Tick));
        Assert.False(far.IsMarked(x.Sim.Tick));

        rig.Hp = Fix64.Zero;
        x.Step();
        var lostAt = x.Sim.Tick;
        x.Run(OwnedVerbs.AuraLingerTicks - 3);
        Assert.True(near.IsMarked(x.Sim.Tick));
        x.Run(5);
        Assert.False(near.IsMarked(x.Sim.Tick));
        Assert.True(x.Sim.Tick - lostAt > OwnedVerbs.AuraLingerTicks - 3);
    }

    [Fact]
    public void Phased_units_cannot_be_targeted_or_fire_and_phase_again_only_after_the_cooldown()
    {
        var x = World();
        var adept = Spawn(x, "adept", 2, 10, 10);
        var gun = Gun(x, 0, 13, 10);
        var rifle = Spawn(x, "rifleman", 2, 10, 12); // can't phase
        x.Step(new PhaseCommand(0, 2, new[] { adept.Id, rifle.Id }));
        Assert.True(adept.IsPhased(x.Sim.Tick));
        Assert.False(rifle.IsPhased(x.Sim.Tick));
        var hpBefore = adept.Hp;
        var gunHpBefore = gun.Hp;
        x.Run(OwnedVerbs.PhaseTicks - 2);
        Assert.Equal(hpBefore, adept.Hp);                                   // nobody hit it
        Assert.DoesNotContain(x.Events, e => e.Kind == SimEventKind.Shot && e.Subject == adept.Id); // it didn't fire
        Assert.NotEqual(adept.Id, gun.TargetId);

        x.Run(4);
        Assert.False(adept.IsPhased(x.Sim.Tick));
        x.Step(new PhaseCommand(x.Sim.Tick, 2, new[] { adept.Id }));        // still cooling down
        Assert.False(adept.IsPhased(x.Sim.Tick));
        x.Run(OwnedVerbs.PhaseCooldownTicks);
        x.Step(new PhaseCommand(x.Sim.Tick, 2, new[] { adept.Id }));
        Assert.True(adept.IsPhased(x.Sim.Tick) || adept.Hp.Raw <= 0);
        Assert.True(gun.Hp < gunHpBefore || adept.Hp < hpBefore, "the fight never resumed after Phase");
    }

    [Fact]
    public void Conscription_refunds_30_percent_in_the_field_and_50_near_a_finished_hall()
    {
        var x = World();
        var field = Spawn(x, "conscript", 1, 5, 5);
        var cost = UnitsCat.Get("conscript").Cost;
        var start = x.Econ.Credits(1);
        x.Step(new ConscriptCommand(0, 1, new[] { field.Id }));
        Assert.Equal(start + cost * 30 / 100, x.Econ.Credits(1));
        Assert.Null(x.Units.Find(field.Id));

        FinishedBuilding(x, "hegemony-conscription-hall", 1, 30, 30);
        var atHall = Spawn(x, "conscript", 1, 34, 30);
        var rifle = Spawn(x, "rifleman", 0, 33, 30);    // not conscriptable
        var enemyIssued = Spawn(x, "conscript", 1, 35, 30);
        var before = x.Econ.Credits(1);
        x.Step(new ConscriptCommand(x.Sim.Tick, 1, new[] { atHall.Id, rifle.Id }),
            new ConscriptCommand(x.Sim.Tick, 0, new[] { enemyIssued.Id })); // wrong faction: ignored
        Assert.Equal(before + cost * 50 / 100, x.Econ.Credits(1));
        Assert.NotNull(x.Units.Find(rifle.Id));
        Assert.NotNull(x.Units.Find(enemyIssued.Id));
    }

    [Fact]
    public void A_conscripted_sapper_detonates_on_enemies_in_radius_and_refunds_nothing()
    {
        var x = World();
        var sapper = Spawn(x, "sapper", 1, 20, 20);
        var close = Spawn(x, "rifleman", 0, 22, 20);
        var away = Spawn(x, "rifleman", 0, 26, 20);
        var friend = Spawn(x, "conscript", 1, 21, 20);
        var credits = x.Econ.Credits(1);
        var (closeHp, awayHp, friendHp) = (close.Hp, away.Hp, friend.Hp);
        x.Step(new ConscriptCommand(0, 1, new[] { sapper.Id }));
        Assert.Null(x.Units.Find(sapper.Id));
        Assert.True(close.Hp < closeHp);
        Assert.Equal(awayHp, away.Hp);
        Assert.Equal(friendHp, friend.Hp);
        Assert.Equal(credits, x.Econ.Credits(1));
    }
}
