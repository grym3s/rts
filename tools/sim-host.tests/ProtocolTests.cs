using System.Text.Json;
using Rts.Tools.SimHost.Protocol;
using Xunit;

namespace Rts.Tools.SimHost.Tests;

public class ProtocolTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RTS.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static readonly string MiniScenario =
        """
        { "schemaVersion": 2, "seed": 1, "map": { "width": 4, "height": 4, "blocked": [] } }
        """;

    // Wire tick semantics (see SimHostSession): a step request names the sim tick it
    // executes AT. After init the host is at tick 0, so the first step carries tick 0
    // and the response reports the world having LEFT tick 1. Same loop as the scenario
    // runner: t = 0 .. ticks-1, commands stamped t execute inside step t.

    // ---------- codec ----------

    [Fact]
    public void Decodes_step_with_one_command()
    {
        var req = Codec.DecodeRequest("""
            {"protocolVersion":1,"kind":"step","tick":3,
             "commands":[{"tick":3,"faction":0,"units":[0],"type":"move","target":[1.5,2]}]}
            """);
        Assert.Equal(1, req.ProtocolVersion);
        Assert.Equal("step", req.Kind);
        Assert.Equal(3, req.Tick);
        Assert.Single(req.Commands!.Value.EnumerateArray());
    }

    [Fact]
    public void Decodes_step_with_empty_command_list()
    {
        var req = Codec.DecodeRequest("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}""");
        Assert.Empty(req.Commands!.Value.EnumerateArray());
    }

    [Fact]
    public void Rejects_malformed_json_unknown_fields_and_bad_types()
    {
        Assert.ThrowsAny<JsonException>(() => Codec.DecodeRequest("not json"));
        var e1 = Assert.Throws<ProtocolException>(() => Codec.DecodeRequest("""{"protocolVersion":1,"kind":"step","tick":1,"nope":2}"""));
        Assert.Equal("malformed_request", e1.Code);
        var e2 = Assert.Throws<ProtocolException>(() => Codec.DecodeRequest("""{"protocolVersion":1,"kind":"step","tick":"one"}"""));
        Assert.Equal("malformed_request", e2.Code);
        var e3 = Assert.Throws<ProtocolException>(() => Codec.DecodeRequest("""{"kind":"step","tick":1}"""));
        Assert.Equal("malformed_request", e3.Code);
    }

    [Fact]
    public void Rejects_unknown_kind_and_commands_block_in_init()
    {
        var e = Assert.Throws<ProtocolException>(() => Codec.DecodeRequest("""{"protocolVersion":1,"kind":"launch"}"""));
        Assert.Equal("unknown_kind", e.Code);
        var e2 = Assert.Throws<ProtocolException>(() => Codec.DecodeRequest(
            """{"protocolVersion":1,"kind":"init","scenario":{"schemaVersion":2,"commands":[]}}"""));
        Assert.Equal("malformed_request", e2.Code);
    }

    // ---------- host session ----------

    [Fact]
    public void Init_then_step_returns_snapshot_and_advances_one_tick()
    {
        var s = new SimHostSession(RepoRoot());
        var init = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{MiniScenario}}}"""));
        Assert.True(init.Ok);
        Assert.Equal(0, init.Tick);
        Assert.NotNull(init.State);
        Assert.Empty(init.State!.Units);

        var step = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}"""));
        Assert.True(step.Ok);
        Assert.Equal(1, step.Tick);
        Assert.NotNull(step.State);
        Assert.Empty(step.Events!);
    }

    [Fact]
    public void Step_with_one_move_command_moves_a_unit()
    {
        var s = new SimHostSession(RepoRoot());
        s.Handle("""{"protocolVersion":1,"kind":"init","scenario":{"schemaVersion":2,"seed":1,"map":{"width":8,"height":8,"blocked":[]},"units":[{"unit":"rifleman","at":[1,1]}]}}""");
        for (var t = 0; t < 20; t++)
        {
            var cmds = t == 1 ? """[{"tick":1,"faction":0,"units":[0],"type":"move","target":[5,1]}]""" : "[]";
            var r = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"step","tick":{{t}},"commands":{{cmds}}}"""));
            Assert.True(r.Ok, r.Error);
        }
        var last = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":20,"commands":[]}"""));
        var u = Assert.Single(last.State!.Units);
        Assert.True(u.X > Fix64Raw(1.0), $"unit should have moved: x raw {u.X}");
    }

    private static long Fix64Raw(double v) => (long)Math.Round(v * (1L << 32));

    [Fact]
    public void Empty_roster_steps_without_error()
    {
        var s = new SimHostSession(RepoRoot());
        s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{MiniScenario}}}""");
        for (var t = 0; t < 5; t++)
        {
            var r = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"step","tick":{{t}},"commands":[]}"""));
            Assert.True(r.Ok, r.Error);
            Assert.Empty(r.State!.Units);
        }
    }

    [Fact]
    public void Malformed_request_is_a_bounded_error_not_a_crash()
    {
        var s = new SimHostSession(RepoRoot());
        var r = Resp(s.Handle("}{"));
        Assert.False(r.Ok);
        Assert.Equal("malformed_request", r.Code);
        // session stays usable
        var init = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{MiniScenario}}}"""));
        Assert.True(init.Ok);
    }

    [Fact]
    public void Unsupported_protocol_version_is_rejected()
    {
        var s = new SimHostSession(RepoRoot());
        var r = Resp(s.Handle($$"""{"protocolVersion":99,"kind":"init","scenario":{{MiniScenario}}}"""));
        Assert.False(r.Ok);
        Assert.Equal("unsupported_protocol_version", r.Code);
    }

    [Fact]
    public void Duplicate_tick_is_rejected_without_advancing()
    {
        var s = new SimHostSession(RepoRoot());
        s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{MiniScenario}}}""");
        Assert.True(Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}""")).Ok);
        var dup = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}"""));
        Assert.False(dup.Ok);
        Assert.Equal("tick_behind", dup.Code);
        // world did not advance twice: the next accepted step is still tick 1
        Assert.True(Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":1,"commands":[]}""")).Ok);
    }

    [Fact]
    public void Skipped_tick_is_rejected_without_advancing()
    {
        var s = new SimHostSession(RepoRoot());
        s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{MiniScenario}}}""");
        var gap = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":5,"commands":[]}"""));
        Assert.False(gap.Ok);
        Assert.Equal("tick_gap", gap.Code);
        Assert.True(Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}""")).Ok);
    }

    [Fact]
    public void Command_stamped_wrong_tick_is_rejected_without_advancing()
    {
        var s = new SimHostSession(RepoRoot());
        s.Handle("""{"protocolVersion":1,"kind":"init","scenario":{"schemaVersion":2,"seed":1,"map":{"width":4,"height":4,"blocked":[]},"units":[{"unit":"rifleman","at":[1,1]}]}}""");
        var bad = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[{"tick":7,"faction":0,"units":[0],"type":"move","target":[2,2]}]}"""));
        Assert.False(bad.Ok);
        Assert.Equal("command_tick_mismatch", bad.Code);
        Assert.True(Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}""")).Ok);
    }

    [Fact]
    public void Step_before_init_is_an_error()
    {
        var s = new SimHostSession(RepoRoot());
        var r = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}"""));
        Assert.False(r.Ok);
        Assert.Equal("not_initialized", r.Code);
    }

    [Fact]
    public void Unknown_command_type_is_a_bounded_error()
    {
        var s = new SimHostSession(RepoRoot());
        s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{MiniScenario}}}""");
        var r = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[{"tick":0,"faction":0,"type":"teleport"}]}"""));
        Assert.False(r.Ok);
        Assert.Equal("unknown_command_type", r.Code);
    }

    // ---------- attack target decoding ----------

    private const string TwoRiflemen =
        """{"schemaVersion":2,"seed":3,"map":{"width":8,"height":8,"blocked":[]},"units":[{"unit":"rifleman","at":[1,1]},{"unit":"rifleman","at":[2,1]}]}""";

    [Fact]
    public void Attack_with_integer_target_advances_and_engages()
    {
        // Identical runs with and without the attack order must diverge — two riflemen at
        // (1,1)/(2,1) are in weapon range, so the order has to bite within 60 ticks.
        // (Hash alone moves every tick; the difference between runs is the real assertion.)
        ulong Run(bool attack)
        {
            var s = new SimHostSession(RepoRoot());
            var init = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{TwoRiflemen}}}"""));
            Assert.True(init.Ok, init.Error);
            for (var t = 0; t < 60; t++)
            {
                var cmds = t == 1 && attack ? """[{"tick":1,"faction":0,"units":[0],"type":"attack","target":1}]""" : "[]";
                var r = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"step","tick":{{t}},"commands":{{cmds}}}"""));
                Assert.True(r.Ok, $"tick {t}: {r.Error}");
            }
            var last = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":60,"commands":[]}"""));
            Assert.Contains(last.State!.Units, u => u.Id == 0); // attacker alive
            return last.State.Hash;
        }
        Assert.NotEqual(Run(false), Run(true));
    }

    [Theory]
    [InlineData("""{"tick":1,"faction":0,"units":[0],"type":"attack","target":[5,1]}""")] // coord array: wrong shape
    [InlineData("""{"tick":1,"faction":0,"units":[0],"type":"attack","target":"1"}""")] // string id
    [InlineData("""{"tick":1,"faction":0,"units":[0],"type":"attack","target":1.5}""")] // non-integer
    [InlineData("""{"tick":1,"faction":0,"units":[0],"type":"attack"}""")] // missing target
    public void Attack_with_non_integer_target_is_rejected_without_advancing(string cmd)
    {
        var s = new SimHostSession(RepoRoot());
        s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{TwoRiflemen}}}""");
        Assert.True(Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}""")).Ok);

        var bad = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"step","tick":1,"commands":[{{cmd}}]}"""));
        Assert.False(bad.Ok);
        Assert.Equal("malformed_request", bad.Code);
        // world untouched: the same tick is still acceptable with a valid command
        Assert.True(Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":1,"commands":[]}""")).Ok);
    }

    // ---------- world preservation across rejected requests ----------

    [Fact]
    public void Rejected_step_command_decode_preserves_the_world()
    {
        var s = new SimHostSession(RepoRoot());
        s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{TwoRiflemen}}}""");
        Assert.True(Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}""")).Ok);

        var bad = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":1,"commands":[{"tick":1,"faction":0,"type":"teleport"}]}"""));
        Assert.False(bad.Ok);
        Assert.Equal("unknown_command_type", bad.Code);

        // Old behaviour nuked the world here (next step said not_initialized). It must survive:
        var ok = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":1,"commands":[]}"""));
        Assert.True(ok.Ok, ok.Error);
        Assert.Equal(2, ok.Tick);
    }

    // ---------- restart (T07) ----------

    [Fact]
    public void Second_init_without_reset_is_refused_and_keeps_the_world()
    {
        var s = new SimHostSession(RepoRoot());
        s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{TwoRiflemen}}}""");
        Assert.True(Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}""")).Ok);
        var again = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{TwoRiflemen}}}"""));
        Assert.Equal("already_initialized", again.Code);
        Assert.True(Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":1,"commands":[]}""")).Ok);
    }

    [Fact]
    public void Reset_then_init_replays_exactly_like_a_new_process()
    {
        string Play(SimHostSession s)
        {
            var init = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{TwoRiflemen}}}"""));
            Assert.True(init.Ok, init.Error);
            HostResponse last = init;
            for (var t = 0; t < 40; t++)
            {
                var cmds = t == 1 ? """[{"tick":1,"faction":0,"units":[0],"type":"attack","target":1}]""" : "[]";
                last = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"step","tick":{{t}},"commands":{{cmds}}}"""));
                Assert.True(last.Ok, $"tick {t}: {last.Error}");
            }
            return last.State!.Hash.ToString("x16");
        }

        var fresh = Play(new SimHostSession(RepoRoot()));
        var s = new SimHostSession(RepoRoot());
        Play(s);
        var reset = Resp(s.Handle("""{"protocolVersion":1,"kind":"reset"}"""));
        Assert.True(reset.Ok, reset.Error);
        Assert.Equal("not_initialized", Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}""")).Code);
        Assert.Equal(fresh, Play(s));
    }

    [Fact]
    public void Reset_rejects_extra_fields_and_is_harmless_before_init()
    {
        var e = Assert.Throws<ProtocolException>(() => Codec.DecodeRequest("""{"protocolVersion":1,"kind":"reset","tick":3}"""));
        Assert.Equal("malformed_request", e.Code);
        var s = new SimHostSession(RepoRoot());
        Assert.True(Resp(s.Handle("""{"protocolVersion":1,"kind":"reset"}""")).Ok);
    }

    [Fact]
    public void Rejected_init_leaves_the_session_uninitialized_and_retriable()
    {
        var s = new SimHostSession(RepoRoot());
        var bad = Resp(s.Handle("""{"protocolVersion":1,"kind":"init","scenario":{"schemaVersion":99,"seed":1,"map":{"width":4,"height":4,"blocked":[]}}}"""));
        Assert.False(bad.Ok);
        Assert.Equal("unsupported_scenario_schema", bad.Code);

        var step = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}"""));
        Assert.Equal("not_initialized", step.Code); // no half-built world leaked

        var retry = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{MiniScenario}}}"""));
        Assert.True(retry.Ok, retry.Error);
    }

    // ---------- deterministic replay parity ----------

    /// <summary>Driving every golden scenario through the host — init (scenario minus
    /// commands/ticks/assert) then one step per tick, same loop bounds as
    /// tools/scenario/Program.cs — must reproduce the runner's final state hash exactly.
    /// Hashes pinned from CI (.github/workflows/ci.yml) and confirmed against a live
    /// scenario-runner run.</summary>
    [Theory]
    [InlineData("chokepoint-30.json", "1727e6d2adb5efb8")]
    [InlineData("skirmish-10v10.json", "e97bc24d4f7fd6c3")] // re-pinned for T05 guard (idle units hold, chase, return): 0fbba65f760574ce -> e97bc24d4f7fd6c3
    [InlineData("tri-point-30.json", "c209ac08ca9974f4")] // re-pinned for T34 fields: 5526cd76fdce4754 -> c209ac08ca9974f4 (ore pools hashed; same battle)
    [InlineData("tri-point-skirmish.json", "67de1d03ad12b797")] // T36 skirmish start (MCV deploy + reactor)
    [InlineData("counters-6v4.json", "39ac951e73e99076")] // re-pinned for T05 guard (idle units hold, chase, return): a418be859c7550c7 -> 39ac951e73e99076
    [InlineData("guard-hold.json", "e7a820c12422dad2")]
    [InlineData("match-end.json", "3c75053acd9d4698")]
    [InlineData("economy-2h.json", "05829563b6901d19")]
    [InlineData("produce-1t1.json", "5752e0ece297f09b")] // re-pinned for T33: 2cb5e49325482ab9 -> 5752e0ece297f09b (refinery building ships a harvester; spawns at cell centres)
    public void Host_replay_matches_scenario_runner_golden_hash(string scenarioFile, string expectedHex)
    {
        Assert.Equal(expectedHex, ReplayThroughHost(scenarioFile).ToString("x16"));
    }

    [Fact]
    public void Host_replay_is_repeatable()
    {
        Assert.Equal(ReplayThroughHost("produce-1t1.json"), ReplayThroughHost("produce-1t1.json"));
    }

    private static ulong ReplayThroughHost(string scenarioFile)
    {
        var path = Path.Combine(RepoRoot(), "content", "scenarios", scenarioFile);
        var doc = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        var ticks = doc.GetProperty("ticks").GetInt32();

        // scenario for init: every top-level field except commands/ticks/assert.
        var scen = new Dictionary<string, JsonElement>();
        foreach (var p in doc.EnumerateObject())
            if (p.Name is not ("ticks" or "commands" or "assert")) scen[p.Name] = p.Value.Clone();

        var byTick = new Dictionary<int, List<JsonElement>>();
        if (doc.TryGetProperty("commands", out var cmds))
            foreach (var c in cmds.EnumerateArray())
            {
                var t = c.GetProperty("tick").GetInt32();
                if (!byTick.TryAdd(t, new List<JsonElement>())) byTick[t].Add(c.Clone()); else byTick[t].Add(c.Clone());
            }

        var s = new SimHostSession(RepoRoot());
        var initResp = Resp(s.Handle(JsonSerializer.Serialize(new { protocolVersion = 1, kind = "init", scenario = scen })));
        Assert.True(initResp.Ok, initResp.Error);

        HostResponse last = initResp;
        for (var t = 0; t < ticks; t++) // same loop bounds as tools/scenario/Program.cs
        {
            var list = byTick.TryGetValue(t, out var l) ? l : new List<JsonElement>();
            last = Resp(s.Handle(JsonSerializer.Serialize(new { protocolVersion = 1, kind = "step", tick = t, commands = list })));
            Assert.True(last.Ok, $"tick {t}: {last.Error}");
        }

        Assert.Equal(ticks, last.Tick);
        return last.State!.Hash;
    }

    // ---------- Tri-Point resource fields (T34) ----------

    [Fact]
    public void Tri_point_ships_nine_fields_of_ore_on_walkable_ground()
    {
        var path = Path.Combine(RepoRoot(), "content", "scenarios", "tri-point-30.json");
        var doc = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        var scen = new Dictionary<string, JsonElement>();
        foreach (var p in doc.EnumerateObject())
            if (p.Name is not ("ticks" or "commands" or "assert")) scen[p.Name] = p.Value.Clone();
        var s = new SimHostSession(RepoRoot());
        var init = Resp(s.Handle(JsonSerializer.Serialize(new { protocolVersion = 1, kind = "init", scenario = scen })));
        Assert.True(init.Ok, init.Error);

        var veins = init.State!.Veins!;
        Assert.Equal(9 * 5, veins.Count);
        Assert.Equal(9 * 25_000, veins.Sum(v => v.Pool));

        var map = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "content", "maps", "tri-point.json"))).RootElement;
        var rows = map.GetProperty("rows").EnumerateArray().Select(r => r.GetString()!).ToArray();
        foreach (var v in veins)
        {
            var (cx, cy) = ((int)(v.X >> 32), (int)(v.Y >> 32));
            Assert.Equal('.', rows[cy][cx]);
        }
        var fields = map.GetProperty("features").EnumerateArray().Where(f => f.GetProperty("type").GetString() == "field").ToList();
        Assert.Equal(3, fields.Count(f => f.GetProperty("kind").GetString() == "main"));
        Assert.Equal(3, fields.Count(f => f.GetProperty("kind").GetString() == "natural"));
        Assert.Equal(3, fields.Count(f => f.GetProperty("kind").GetString() == "contested"));
    }

    // ---------- build menu support (T36): catalog on init, read-only place query ----------

    [Fact]
    public void Init_carries_the_catalogs_for_build_menus()
    {
        var s = new SimHostSession(RepoRoot());
        var init = Resp(s.Handle("""{"protocolVersion":1,"kind":"init","scenario":{"schemaVersion":2,"seed":1,"map":{"width":8,"height":8,"blocked":[]}}}"""));
        Assert.True(init.Ok, init.Error);
        var yard = init.Catalog!.Buildings.Single(b => b.Id == "coalition-construction-yard");
        Assert.Equal("Construction Yard", yard.Name);
        Assert.Equal((3, 3), (yard.FootprintW, yard.FootprintH));
        Assert.Null(yard.Prereq);
        var barracks = init.Catalog.Buildings.Single(b => b.Id == "coalition-barracks");
        Assert.Equal("coalition-construction-yard", barracks.Prereq);
        Assert.Equal(1, init.Catalog.Buildings.Single(b => b.Id == "hegemony-conscription-hall").Faction);
        var rifle = init.Catalog.Units.Single(u => u.Id == "rifleman");
        Assert.Equal("coalition-barracks", rifle.BuiltFrom);
        Assert.True(init.Catalog.Units.Single(u => u.Id == "collector").Harvest);
        Assert.Equal("coalition-construction-yard", init.Catalog.Units.Single(u => u.Id == "mobile-command").DeploysTo);

        var step = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}"""));
        Assert.Null(step.Catalog); // init only
    }

    [Fact]
    public void Place_query_answers_with_the_sims_verdict_and_never_advances_the_world()
    {
        var s = new SimHostSession(RepoRoot());
        Assert.True(Resp(s.Handle("""{"protocolVersion":1,"kind":"init","scenario":{"schemaVersion":2,"seed":1,"map":{"width":40,"height":40,"blocked":[[20,0,20,39]]},"veins":[{"at":[12.5,5.5],"pool":5000}]}}""")).Ok);
        HostQueryResult Ask(string building, double x, double y)
        {
            var r = Resp(s.Handle($$$"""{"protocolVersion":1,"kind":"query","query":{"type":"place","faction":0,"building":"{{{building}}}","at":[{{{x}}},{{{y}}}]}}"""));
            Assert.True(r.Ok, r.Error);
            Assert.Null(r.State);
            return r.Query!;
        }

        Assert.Equal("prereq", Ask("coalition-reactor", 9, 5).Reason);
        var yardQ = Ask("coalition-construction-yard", 5, 5);
        Assert.True(yardQ.Allowed);
        Assert.Equal((4, 4, 3, 3), (yardQ.X, yardQ.Y, yardQ.W, yardQ.H));
        Assert.Equal("wrong_faction", Ask("hegemony-construction-yard", 5, 5).Reason);

        // the query did not advance: tick 0 is still next
        Assert.True(Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[{"tick":0,"faction":0,"type":"place","building":"coalition-construction-yard","at":[5,5]}]}""")).Ok);
        for (var t = 1; t <= 205; t++)
            Assert.True(Resp(s.Handle($$"""{"protocolVersion":1,"kind":"step","tick":{{t}},"commands":[]}""")).Ok);

        Assert.True(Ask("coalition-reactor", 9, 5).Allowed);
        Assert.Equal("blocked", Ask("coalition-reactor", 5, 5).Reason);   // on the yard
        Assert.Equal("blocked", Ask("coalition-reactor", 12, 5).Reason);  // covers the ore at (12,5)
        Assert.Equal("blocked", Ask("coalition-reactor", 20, 10).Reason); // the wall
        Assert.Equal("radius", Ask("coalition-reactor", 30, 30).Reason);
        // 2500 left after the yard: spend 2200, then a 400 reactor is short
        Assert.True(Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":206,"commands":[{"tick":206,"faction":0,"type":"spend","amount":2200}]}""")).Ok);
        Assert.Equal("credits", Ask("coalition-reactor", 9, 5).Reason);
    }

    // ---------- MCV deploy (T32) ----------

    [Fact]
    public void Deploy_turns_the_mcv_into_a_yard_that_the_snapshot_shows_with_its_footprint()
    {
        var s = new SimHostSession(RepoRoot());
        var init = Resp(s.Handle("""{"protocolVersion":1,"kind":"init","scenario":{"schemaVersion":2,"seed":3,"map":{"width":24,"height":24,"blocked":[]},"units":[{"unit":"mobile-command","at":[8.5,8.5]}]}}"""));
        Assert.True(init.Ok, init.Error);
        var mcv = Assert.Single(init.State!.Units);
        Assert.Equal("coalition-construction-yard", mcv.DeploysTo);
        Assert.Null(mcv.BuildingId);
        Assert.Equal(0, mcv.FootprintW);

        var r = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"step","tick":0,"commands":[{"tick":0,"faction":0,"type":"deploy","unit":{{mcv.Id}}}]}"""));
        Assert.True(r.Ok, r.Error);
        var yard = Assert.Single(r.State!.Units); // the MCV left the roster in the same tick
        Assert.Equal("coalition-construction-yard", yard.BuildingId);
        Assert.Null(yard.DeploysTo);
        Assert.Equal((3, 3), (yard.FootprintW, yard.FootprintH));
        Assert.Equal(8.5, yard.X / 4294967296.0);
        Assert.Equal(5000, r.State.Factions![0].Credits); // deploying is free
        Assert.DoesNotContain(r.Events ?? [], e => e.Kind == "death");
    }

    // ---------- economy in the snapshot (T30) ----------

    [Fact]
    public void Snapshot_carries_credits_power_veins_refineries_and_build_progress()
    {
        var s = new SimHostSession(RepoRoot());
        var init = Resp(s.Handle("""{"protocolVersion":1,"kind":"init","scenario":{"schemaVersion":2,"seed":31,"map":{"width":24,"height":24,"blocked":[]},"veins":[{"at":[6,9],"pool":5000}],"refineries":[{"faction":0,"at":[5,9]}]}}"""));
        Assert.True(init.Ok, init.Error);
        var st = init.State!;
        Assert.Equal(2, st.Factions!.Count);
        Assert.All(st.Factions, f => Assert.Equal(5000, f.Credits));
        var vein = Assert.Single(st.Veins!);
        Assert.Equal(5000, vein.Pool);
        Assert.Equal(6.0, vein.X / 4294967296.0);
        var refinery = Assert.Single(st.Refineries!);
        Assert.Equal(0, refinery.Faction);

        HostResponse Step(int t, string cmds = "[]")
        {
            var r = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"step","tick":{{t}},"commands":{{cmds}}}"""));
            Assert.True(r.Ok, $"tick {t}: {r.Error}");
            return r;
        }

        // Construction yard: credits drop by its cost, construction counts down from its total.
        var placed = Step(0, """[{"tick":0,"faction":0,"type":"place","building":"coalition-construction-yard","at":[5,5]}]""");
        var yard = placed.State!.Units.Single(u => u.BuildingId == "coalition-construction-yard");
        Assert.True(yard.ConstructionTotal > 0);
        Assert.InRange(yard.ConstructionRemaining, 1, yard.ConstructionTotal);
        Assert.True(placed.State.Factions![0].Credits < 5000);

        var t = 1;
        for (; t <= 210; t++) Step(t);
        // Reactor powers the grid: supply shows once it is finished.
        Step(t++, $$"""[{"tick":{{t - 1}},"faction":0,"type":"place","building":"coalition-reactor","at":[9,5]}]""");
        HostResponse last = null!;
        for (var i = 0; i < 450; i++) last = Step(t++);
        var f0 = last.State!.Factions![0];
        Assert.Equal(100, f0.PowerSupply);
        Assert.Equal(0, f0.PowerDraw);

        // Barracks + one rifleman queued: queue head carries remaining and total train ticks.
        Step(t, $$"""[{"tick":{{t}},"faction":0,"type":"place","building":"coalition-barracks","at":[5,12]}]""");
        t++;
        for (var i = 0; i < 450; i++) last = Step(t++);
        var barracks = last.State!.Units.Single(u => u.BuildingId == "coalition-barracks");
        Assert.Equal(0, barracks.ConstructionRemaining);
        Assert.Equal(20, last.State.Factions![0].PowerDraw);
        Step(t, $$"""[{"tick":{{t}},"faction":0,"type":"train","building":{{barracks.Id}},"unit":"rifleman"}]""");
        t++;
        var queued = Step(t++).State!.Units.Single(u => u.Id == barracks.Id);
        Assert.Equal(["rifleman"], queued.ProductionQueue!);
        Assert.True(queued.QueueHeadTotal > 0);
        Assert.InRange(queued.QueueHeadRemaining, 1, queued.QueueHeadTotal);
        Assert.Null(queued.Rally);

        // T37: rally point on the snapshot; the rifleman walks there once trained.
        Step(t, $$"""[{"tick":{{t}},"faction":0,"type":"rally","building":{{barracks.Id}},"at":[18.5,18.5]}]""");
        t++;
        var rallied = Step(t++).State!.Units.Single(u => u.Id == barracks.Id);
        Assert.Equal(new[] { (long)(18.5 * 4294967296.0), (long)(18.5 * 4294967296.0) }, rallied.Rally!);
        for (var i = 0; i < 500; i++) last = Step(t++);
        var rifle = last.State!.Units.Single(u => u.BuildingId == null && u.Faction == 0 && !u.Harvest);
        Assert.Equal((18, 18), ((int)(rifle.X >> 32), (int)(rifle.Y >> 32)));
    }

    // ---------- harvest command (T38) ----------

    [Fact]
    public void Harvest_command_sends_a_collector_to_the_named_vein_and_the_row_shows_its_capacity()
    {
        var s = new SimHostSession(RepoRoot());
        var init = Resp(s.Handle("""{"protocolVersion":1,"kind":"init","scenario":{"schemaVersion":2,"seed":1,"map":{"width":32,"height":32,"blocked":[]},"veins":[{"at":[6.5,6.5],"pool":5000},{"at":[24.5,24.5],"pool":5000}],"refineries":[{"faction":0,"at":[4.5,4.5]}],"units":[{"unit":"collector","at":[10.5,10.5]}]}}"""));
        Assert.True(init.Ok, init.Error);
        var c = Assert.Single(init.State!.Units);
        Assert.True(c.Harvest);
        Assert.Equal(700, c.LoadCapacity);
        var r = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"step","tick":0,"commands":[{"tick":0,"faction":0,"type":"harvest","units":[{{c.Id}}],"vein":1}]}"""));
        Assert.True(r.Ok, r.Error);
        for (var t = 1; t < 600; t++) r = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"step","tick":{{t}},"commands":[]}"""));
        Assert.Equal(5000, r.State!.Veins![0].Pool);   // the near vein was never touched
        Assert.True(r.State.Veins[1].Pool < 5000);      // the named one was
    }

    // ---------- debug overlays (T20) ----------

    [Fact]
    public void Debug_block_is_absent_unless_the_step_asks_for_it()
    {
        var s = new SimHostSession(RepoRoot());
        s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{TwoRiflemen}}}""");
        var plain = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[]}"""));
        Assert.Null(plain.State!.Debug);
        var off = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":1,"commands":[],"debug":false}"""));
        Assert.Null(off.State!.Debug);
    }

    [Fact]
    public void Debug_block_reports_move_path_attack_target_and_step_time()
    {
        // Two riflemen of the same faction (TwoRiflemen: both faction 0): one moves, one idles.
        var s = new SimHostSession(RepoRoot());
        s.Handle("""{"protocolVersion":1,"kind":"init","scenario":{"schemaVersion":2,"seed":1,"map":{"width":16,"height":8,"blocked":[]},"units":[{"unit":"rifleman","at":[1,1]},{"unit":"rifleman","at":[1,6]}]}}""");
        Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":0,"commands":[{"tick":0,"faction":0,"units":[0],"type":"move","target":[12,1]}]}"""));
        var r = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":1,"commands":[],"debug":true}"""));
        Assert.True(r.Ok, r.Error);
        var dbg = r.State!.Debug!;
        Assert.True(dbg.StepMicros >= 0);
        var mover = dbg.Units.Single(u => u.Id == 0);
        Assert.Equal("move", mover.State);
        Assert.Equal(-1, mover.Target);
        Assert.NotEmpty(mover.Path);
        Assert.Equal(12.0, mover.Path[^1][0] / 4294967296.0, 1);
        var idle = dbg.Units.Single(u => u.Id == 1);
        Assert.Equal("idle", idle.State);
        Assert.Empty(idle.Path);

        // Same-faction attack is still an order the sim honours: the target id must show.
        var a = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":2,"commands":[{"tick":2,"faction":0,"units":[1],"type":"attack","target":0}],"debug":true}"""));
        var attacker = a.State!.Debug!.Units.Single(u => u.Id == 1);
        Assert.Equal(0, attacker.Target);
        Assert.Equal("attack", attacker.State);
    }

    [Fact]
    public void Debug_flag_must_be_a_boolean()
    {
        var e = Assert.Throws<ProtocolException>(() => Codec.DecodeRequest("""{"protocolVersion":1,"kind":"step","tick":0,"debug":1}"""));
        Assert.Equal("malformed_request", e.Code);
    }

    // ---------- events and match (T09, T06) ----------

    [Fact]
    public void Snapshot_units_carry_hp_and_max_hp_for_health_bars()
    {
        // T15: bars need both values. maxHp is the catalog hp, fixed for the unit's life.
        var s = new SimHostSession(RepoRoot());
        var init = Resp(s.Handle("""{"protocolVersion":1,"kind":"init","scenario":{"schemaVersion":2,"seed":1,"map":{"width":12,"height":8,"blocked":[]},"units":[{"unit":"rifleman","at":[2,3]},{"unit":"conscript","at":[5,3]}]}}"""));
        Assert.True(init.Ok, init.Error);
        foreach (var u in init.State!.Units)
        {
            Assert.True(u.MaxHp > 0);
            Assert.Equal(u.MaxHp, u.Hp);
        }
        var target = init.State.Units[1];
        var hurt = false;
        for (var t = 0; t < 200 && !hurt; t++)
        {
            var r = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"step","tick":{{t}},"commands":[]}"""));
            var c = r.State!.Units.FirstOrDefault(u => u.Id == target.Id);
            if (c is null || c.Hp < c.MaxHp)
            {
                hurt = true;
                if (c is not null) Assert.Equal(target.MaxHp, c.MaxHp);
            }
        }
        Assert.True(hurt, "the conscript never took damage");
    }

    [Fact]
    public void Shots_deaths_and_match_end_travel_as_events_and_match_state()
    {
        var s = new SimHostSession(RepoRoot());
        var init = Resp(s.Handle("""{"protocolVersion":1,"kind":"init","scenario":{"schemaVersion":2,"seed":1,"map":{"width":12,"height":8,"blocked":[]},"units":[{"unit":"rifleman","at":[2,3]},{"unit":"rifleman","at":[2,4]},{"unit":"rifleman","at":[2,5]},{"unit":"conscript","at":[5,4]}]}}"""));
        Assert.True(init.Ok, init.Error);
        Assert.False(init.State!.Match!.Over);

        var kinds = new HashSet<string>();
        HostEvent? death = null, end = null;
        string? endLine = null;
        HostResponse last = init;
        for (var t = 0; t < 400 && end == null; t++)
        {
            var line = s.Handle($$"""{"protocolVersion":1,"kind":"step","tick":{{t}},"commands":[]}""");
            last = Resp(line);
            Assert.True(last.Ok, last.Error);
            foreach (var e in last.Events!)
            {
                kinds.Add(e.Kind);
                if (e.Kind == "shot") Assert.True(e.Amount > 0);
                if (e.Kind == "death") death = e;
                if (e.Kind == "match_end") { end = e; endLine = line; }
            }
        }

        Assert.Contains("shot", kinds);
        Assert.NotNull(death);
        Assert.Equal(3, death!.Subject); // the conscript
        Assert.Equal(1, death.Faction);
        Assert.NotNull(end);
        Assert.Equal(0, end!.Faction);
        Assert.Contains("\"kind\":\"match_end\"", endLine);
        Assert.True(last.State!.Match!.Over);
        Assert.Equal(0, last.State.Match.Winner);
        Assert.Equal(last.Tick - 1, last.State.Match.EndTick);
    }

    // ---------- hash wire format ----------

    [Fact]
    public void State_hash_travels_as_exact_16_hex_digit_string()
    {
        var s = new SimHostSession(RepoRoot());
        var line = s.Handle($$"""{"protocolVersion":1,"kind":"init","scenario":{{MiniScenario}}}""");

        // Raw wire check (not the round-tripped record): hash must be a JSON *string* token
        // of exactly 16 lowercase hex digits — the runner/CI format, safe for JS consumers
        // (a raw number > 2^53-1 silently corrupts there).
        using var doc = JsonDocument.Parse(line);
        var hash = doc.RootElement.GetProperty("state").GetProperty("hash");
        Assert.Equal(JsonValueKind.String, hash.ValueKind);
        var text = hash.GetString()!;
        Assert.Matches("^[0-9a-f]{16}$", text);
        // and the deserialized record agrees with the hex text
        Assert.Equal(text, Resp(line).State!.Hash.ToString("x16"));
    }

    [Fact]
    public void Hash_string_round_trips_and_rejects_wrong_shapes()
    {
        Assert.Equal(0x1727e6d2adb5efb8UL, ulong.Parse("1727e6d2adb5efb8", System.Globalization.NumberStyles.HexNumber));
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        Assert.Equal(0x1727e6d2adb5efb8UL,
            JsonSerializer.Deserialize<HostState>("""{"hash":"1727e6d2adb5efb8","units":[]}""", opts)!.Hash);
        // raw numbers and wrong-length strings are protocol violations
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<HostState>("""{"hash":1727,"units":[]}""", opts));
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<HostState>("""{"hash":"1727e6d2adb5efb","units":[]}""", opts));
    }

    [Fact]
    public void Two_identical_command_streams_give_identical_hashes()
    {
        var stream = """[{"tick":1,"faction":0,"units":[0,1],"type":"attack-move","target":[6,6]}]""";
        Assert.Equal(RunMini(stream), RunMini(stream));
    }

    private static ulong RunMini(string tick1Commands)
    {
        var s = new SimHostSession(RepoRoot());
        s.Handle("""{"protocolVersion":1,"kind":"init","scenario":{"schemaVersion":2,"seed":5,"map":{"width":12,"height":12,"blocked":[]},"units":[{"unit":"rifleman","at":[1,1]},{"unit":"rifleman","at":[2,1]}]}}""");
        for (var t = 0; t < 40; t++)
        {
            var cmds = t == 1 ? tick1Commands : "[]";
            var r = Resp(s.Handle($$"""{"protocolVersion":1,"kind":"step","tick":{{t}},"commands":{{cmds}}}"""));
            Assert.True(r.Ok, r.Error);
        }
        var last = Resp(s.Handle("""{"protocolVersion":1,"kind":"step","tick":40,"commands":[]}"""));
        return last.State!.Hash;
    }

    private static HostResponse Resp(string line) =>
        JsonSerializer.Deserialize<HostResponse>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}
