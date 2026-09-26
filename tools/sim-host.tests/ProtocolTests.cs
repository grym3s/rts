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
    [InlineData("skirmish-10v10.json", "0fbba65f760574ce")]
    [InlineData("counters-6v4.json", "a418be859c7550c7")]
    [InlineData("economy-2h.json", "05829563b6901d19")]
    [InlineData("produce-1t1.json", "780adc3094385fcf")]
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
