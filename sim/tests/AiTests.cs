using System.Text.Json;
using System.Text.Json.Nodes;
using Rts.Sim.Core;
using Rts.Sim.Setup;
using Xunit;

namespace Rts.Tests;

/// <summary>sim/ai: computer players on the real Tri-Point skirmish, scripted commands removed.</summary>
public class AiTests
{
    private static MatchWorld Skirmish(params int[] ai)
    {
        var path = Path.Combine(Arena.RepoRoot(), "content", "scenarios", "tri-point-skirmish.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["commands"] = new JsonArray();
        root["ai"] = new JsonArray(ai.Select(f => (JsonNode)f).ToArray());
        return MatchWorld.FromScenario(JsonDocument.Parse(root.ToJsonString()).RootElement, Arena.RepoRoot());
    }

    private static void Run(MatchWorld m, int ticks)
    {
        for (var i = 0; i < ticks && !m.Match.Over; i++) m.World.Step(Array.Empty<Command>());
    }

    [Fact]
    public void Computer_players_deploy_build_a_base_train_an_army_and_attack()
    {
        var m = Skirmish(0, 1, 2);
        Run(m, 8 * 60 * SimWorld.TicksPerSecond);
        foreach (var f in new[] { 0, 1, 2 })
        {
            var mine = m.Units.Units.Where(u => u.Faction == f && u.Hp.Raw > 0).ToList();
            var buildings = mine.Where(u => u.Building != null).Select(u => m.BuildingsCatalog.Get(u.Building!.Id)).ToList();
            Assert.Contains(buildings, b => b.Category == "construction-yard");
            Assert.Contains(buildings, b => b.Power > 0);
            Assert.Contains(buildings, b => m.UnitsCatalog.IsRefinery(b.Id));
            Assert.True(buildings.Count >= 5, $"faction {f}: {buildings.Count} buildings");
            Assert.Contains(mine, u => u.Building == null && u.Harvest);
        }
        Assert.True(m.Ai.Waves > 0, "no attack wave was sent");
    }

    [Fact]
    public void A_computer_match_is_deterministic()
    {
        var a = Skirmish(0, 1, 2);
        var b = Skirmish(0, 1, 2);
        Run(a, 3 * 60 * SimWorld.TicksPerSecond);
        Run(b, 3 * 60 * SimWorld.TicksPerSecond);
        Assert.Equal(a.World.StateHash(), b.World.StateHash());
    }

    [Fact]
    public void Without_computer_players_nothing_is_sent()
    {
        var m = Skirmish();
        Run(m, 200);
        Assert.Contains(m.Units.Units, u => u.DeploysTo != null); // the MCVs never deployed
    }
}
