using System.Text.Json;
using Rts.Sim.Core;
using Rts.Sim.Production;
using Rts.Sim.Setup;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

/// <summary>Shared arena for the ability tests: a real MatchWorld (the host's and the scenario
/// runner's exact wiring) on an open map, with helpers to drop units and finished buildings
/// for any side.</summary>
public sealed class Arena
{
    public MatchWorld M { get; }
    public SimWorld W => M.World;
    public List<SimEvent> Seen { get; } = new();

    public Arena(int width = 48, int height = 48, string extra = "", string? map = null)
    {
        map ??= $$"""{"width":{{width}},"height":{{height}},"blocked":[]}""";
        var json = $$"""{"schemaVersion":2,"seed":1,"map":{{map}}{{extra}}}""";
        M = MatchWorld.FromScenario(JsonDocument.Parse(json).RootElement, RepoRoot());
        W.Systems.Insert(W.Systems.Count - 2, (w, _) => Seen.AddRange(w.Events)); // before despawn and match
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RTS.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    public Unit Unit(string id, int faction, double x, double y)
    {
        var p = M.UnitsCatalog.Get(id);
        return M.Units.Spawn(W.Ids.Next(), new FixVec2(Fix64.FromDouble(x), Fix64.FromDouble(y)), p.Speed, p.Radius,
            p.Profile() with { Faction = faction });
    }

    /// <summary>A finished building of `id` for `faction`, centred on the cell.</summary>
    public Unit Building(string id, int faction, int cx, int cy)
    {
        var b = ProductionSystem.SpawnStructure(M.BuildingsCatalog, M.Units, M.Map, W.Ids, id, cx, cy);
        b.Faction = faction;
        if (b.Ability.IsSuperweapon) b.ChargeLeft = b.Ability.ChargeTicks;
        return b;
    }

    public void Step(params Command[] cmds) => W.Step(cmds);
    public void Run(int ticks) { for (var i = 0; i < ticks; i++) W.Step(Array.Empty<Command>()); }
    public void RunUntil(Func<bool> done, int max = 2000) { for (var i = 0; i < max && !done(); i++) W.Step(Array.Empty<Command>()); }
}

public class AbilityTests
{
    // ---- low power, Phase Turret, line damage, Wellspring

    [Fact]
    public void A_defense_holds_fire_while_its_side_is_low_on_power()
    {
        var a = new Arena();
        a.Building("coalition-guard-turret", 0, 20, 20);
        var target = a.Unit("engineer", 1, 26.5, 20.5);
        a.Run(60);
        Assert.Equal(target.MaxHp, target.Hp); // the turret draws 20 and nothing supplies it

        var b = new Arena();
        b.Building("coalition-guard-turret", 0, 20, 20);
        b.Building("coalition-reactor", 0, 10, 10);
        var t2 = b.Unit("engineer", 1, 26.5, 20.5);
        b.Run(60);
        Assert.True(t2.Hp < t2.MaxHp);
    }

    [Fact]
    public void The_phase_turret_phases_out_after_each_shot()
    {
        var a = new Arena();
        var turret = a.Building("ascendant-phase-turret", 2, 20, 20);
        a.Building("ascendant-conduit", 2, 10, 10);
        a.Unit("bulwark", 1, 25.5, 20.5);
        a.RunUntil(() => a.Seen.Any(e => e.Kind == SimEventKind.Shot && e.Subject == turret.Id), 200);
        Assert.True(turret.IsPhased(a.W.Tick)); // just fired: untargetable for half its cooldown
        a.Run(turret.CooldownTicks / 2 + 1);
        Assert.False(turret.IsPhased(a.W.Tick));
    }

    [Fact]
    public void The_rift_node_hits_every_enemy_on_the_line()
    {
        var a = new Arena();
        var node = a.Building("ascendant-rift-node", 2, 10, 20);
        a.Building("ascendant-conduit", 2, 4, 4);
        var near = a.Unit("engineer", 1, 16.5, 20.5);
        var far = a.Unit("engineer", 1, 21.5, 20.5);   // behind it, on the same line
        var aside = a.Unit("engineer", 1, 21.5, 26.5); // off the line
        a.RunUntil(() => a.Seen.Any(e => e.Kind == SimEventKind.Shot && e.Subject == node.Id), 200);
        Assert.True(near.Hp < near.MaxHp);
        Assert.True(far.Hp < far.MaxHp);
        Assert.Equal(aside.MaxHp, aside.Hp);
    }

    [Fact]
    public void A_cultivator_never_drains_its_field()
    {
        var a = new Arena(extra: ""","veins":[{"at":[10.5,10.5],"pool":5000}]""");
        var c = a.Unit("cultivator", 2, 10.5, 10.5);
        var miner = a.Unit("miner", 1, 10.5, 11.5);
        a.Run(40);
        Assert.True(c.Load > 0);
        Assert.True(miner.Load > 0);
        Assert.Equal(5000 - miner.Load, a.M.Econ.Veins[0].Pool); // only the miner's share is gone
    }

    // ---- shields, field repair, infiltrators

    private static CaptureCommand Job(Arena a, Unit target, params Unit[] who) =>
        new(a.W.Tick, who[0].Faction, who.Select(u => u.Id).ToArray(), target.Id);

    [Fact]
    public void An_anchor_takes_thirty_percent_off_what_its_allies_are_hit_for()
    {
        Fix64 Taken(bool anchor)
        {
            var a = new Arena();
            var ally = a.Unit("adept", 2, 20.5, 20.5);
            if (anchor) a.Unit("anchor", 2, 18.5, 20.5);
            a.Unit("bulwark", 1, 24.5, 20.5);
            a.RunUntil(() => a.Seen.Any(e => e.Kind == SimEventKind.Shot && e.Target == ally.Id), 200);
            return a.Seen.First(e => e.Kind == SimEventKind.Shot && e.Target == ally.Id).Amount;
        }
        var bare = Taken(false);
        var shielded = Taken(true);
        Assert.Equal((bare * Fix64.Ratio(70, 100)).Raw, shielded.Raw);
    }

    [Fact]
    public void Field_repair_heals_nearby_units_but_not_itself_or_buildings()
    {
        var a = new Arena();
        var fr = a.Unit("field-repair", 1, 20.5, 20.5);
        var tank = a.Unit("bulwark", 1, 22.5, 20.5);
        var far = a.Unit("bulwark", 1, 35.5, 20.5);
        tank.Hp = far.Hp = fr.Hp = Fix64.FromInt(50);
        a.Run(20); // one second: 12 hp
        Assert.True(Fix64.Abs(tank.Hp - Fix64.FromInt(62)) < Fix64.Ratio(1, 100), tank.Hp.ToString());
        Assert.Equal(Fix64.FromInt(50), far.Hp);
        Assert.Equal(Fix64.FromInt(50), fr.Hp);
    }

    [Fact]
    public void An_operative_shuts_a_building_down_for_thirty_seconds_and_is_used_up()
    {
        var a = new Arena();
        var reactor = a.Building("hegemony-furnace", 1, 20, 20);
        a.Building("hegemony-cannon-tower", 1, 30, 30);
        var op = a.Unit("operative", 0, 10.5, 20.5);
        a.Step(Job(a, reactor, op));
        a.RunUntil(() => reactor.IsSabotaged(a.W.Tick), 400);
        Assert.True(reactor.IsSabotaged(a.W.Tick));
        a.Step();
        Assert.Null(a.M.Units.Find(op.Id));
        Assert.True(a.M.Power.IsLowPower(1)); // the furnace makes nothing: the tower's draw sinks Hegemony
        a.Run(CaptureSystemTicks());
        Assert.False(a.M.Power.IsLowPower(1));
    }

    private static int CaptureSystemTicks() => Rts.Sim.Combat.CaptureSystem.SabotageTicks + 2;

    [Fact]
    public void A_hijacker_steals_a_vehicle_but_not_infantry()
    {
        var a = new Arena();
        var tank = a.Unit("lancer-mbt", 0, 20.5, 20.5);
        var rifle = a.Unit("rifleman", 0, 20.5, 30.5);
        var hj = a.Unit("hijacker", 1, 10.5, 20.5);
        var hj2 = a.Unit("hijacker", 1, 10.5, 30.5);
        tank.Hp = rifle.Hp = Fix64.FromInt(1000); // nobody dies to the fight around them
        a.Step(Job(a, tank, hj), Job(a, rifle, hj2));
        Assert.Equal(EntityId.None, hj2.CaptureTarget); // infantry can't be hijacked
        a.RunUntil(() => tank.Faction == 1, 400);
        Assert.Equal(1, tank.Faction);
    }

    // ---- carriers

    private static Arena Sea() =>
        new(map: """{"width":40,"height":40,"terrain":[""" + string.Join(",", Enumerable.Repeat("\"" + new string('~', 40) + "\"", 40)) + "]}");

    [Fact]
    public void A_carrier_launches_its_wing_one_at_a_time_and_keeps_it_full()
    {
        var a = Sea();
        var nest = a.Unit("nest", 2, 20.5, 20.5);
        int Wing() => a.M.Units.Units.Count(u => u.CarrierId == nest.Id && u.Hp.Raw > 0);
        a.Run(nest.Ability.RebuildTicks * 2 + 2);
        Assert.Equal(2, Wing());
        a.Run(nest.Ability.RebuildTicks * 6);
        Assert.Equal(nest.Ability.Wing, Wing());
        var drone = a.M.Units.Units.First(u => u.CarrierId == nest.Id);
        Assert.Equal("nest-drone", drone.TypeId);
        Assert.Equal(MoveDomain.Air, drone.Domain);
    }

    [Fact]
    public void Carrier_aircraft_fight_within_its_reach_and_are_lost_with_it()
    {
        var a = Sea();
        var vg = a.Unit("vanguard", 0, 10.5, 20.5);
        var near = a.Unit("gunboat", 1, 20.5, 20.5);   // within launch range 16
        var far = a.Unit("gunboat", 1, 36.5, 36.5);
        near.Hp = Fix64.FromInt(5000);
        a.RunUntil(() => a.Seen.Any(e => e.Kind == SimEventKind.Shot && e.Target == near.Id), 600);
        Assert.Contains(a.Seen, e => e.Kind == SimEventKind.Shot && e.Target == near.Id);
        Assert.Equal(far.MaxHp, far.Hp);
        vg.Hp = Fix64.Zero;
        a.Run(2);
        Assert.DoesNotContain(a.M.Units.Units, u => u.CarrierId == vg.Id && u.Hp.Raw > 0);
    }
}
