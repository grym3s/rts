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

    public Arena(int width = 48, int height = 48, string extra = "")
    {
        var json = $$"""{"schemaVersion":2,"seed":1,"map":{"width":{{width}},"height":{{height}},"blocked":[]}{{extra}}}""";
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
}
