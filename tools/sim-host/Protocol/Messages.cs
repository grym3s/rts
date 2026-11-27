using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rts.Tools.SimHost.Protocol;

/// <summary>The wire is newline-delimited UTF-8 JSON objects, one request in, one response out.
/// Exactly one of kind == "init" | "step" per line; every request carries `protocolVersion`.
/// Fixes the contract details the design spec left open (see CONTEXT.md "protocol decisions").</summary>
public sealed record HostRequest
{
    public const int CurrentProtocolVersion = 1;

    public int ProtocolVersion { get; init; }
    public string? Kind { get; init; }

    /// <summary>init: a scenario-v2 object (schemaVersion, seed, map, optional units/veins/refineries), commands excluded.</summary>
    public JsonElement? Scenario { get; init; }

    /// <summary>step: the next tick to complete (exactly host tick + 1; no gaps, no replays)
    /// and the commands stamped with that tick. Absent commands = empty list.</summary>
    public int? Tick { get; init; }
    public JsonElement? Commands { get; init; }

    /// <summary>step: true = include `state.debug` (T20 overlays). Opt-in, so normal play pays nothing.</summary>
    public bool Debug { get; init; }

    /// <summary>query (T36): a read-only question about the current world, never advancing it.
    /// `{"type":"place","faction":0,"building":"coalition-reactor","at":[x,y]}` asks whether
    /// that `place` would be accepted now and where its footprint would land.</summary>
    public JsonElement? Query { get; init; }
}

/// <summary>Response envelope. ok=true carries the completed tick, read-only state snapshot and
/// that tick's events; ok=false carries error + code and means the world did NOT advance.</summary>
public sealed record HostResponse
{
    public required bool Ok { get; init; }
    public int ProtocolVersion { get; init; } = HostRequest.CurrentProtocolVersion;
    public long Tick { get; init; }
    public HostState? State { get; init; }
    public IReadOnlyList<HostEvent>? Events { get; init; }
    public string? Error { get; init; }
    public string? Code { get; init; }
    /// <summary>init only (T36): the building and unit catalogs, for build menus.</summary>
    public HostCatalog? Catalog { get; init; }
    /// <summary>query only (T36): the answer.</summary>
    public HostQueryResult? Query { get; init; }

    public static HostResponse Fail(string code, string error) =>
        new() { Ok = false, Code = code, Error = error };
}

/// <summary>Read-only snapshot after the completed tick. Fix64 values travel as Q32.32 raw
/// longs (lossless; presentation divides by 2^32). Hash is SimWorld.StateHash as 16 hex chars.</summary>
public sealed record HostState
{
    /// <summary>Wire format is an exact lowercase 16-hex-digit JSON string (e.g. "1727e6d2adb5efb8") —
    /// the same text the scenario runner and CI print and compare, and immune to JSON number
    /// precision loss in non-.NET consumers. (JS Number is exact only to 2^53-1; a 64-bit hash
    /// as a raw number silently corrupts above that.) Decoders accept hex only.</summary>
    [JsonConverter(typeof(HexHashConverter))]
    public ulong Hash { get; init; }
    public required IReadOnlyList<HostUnit> Units { get; init; }
    public IReadOnlyList<HostFaction>? Factions { get; init; }
    /// <summary>Match outcome (T06): over, winner faction (-1 = draw or not over), end tick (-1 while running).</summary>
    public HostMatch? Match { get; init; }
    /// <summary>T30: veins with their remaining pools, and refinery drop-off points.</summary>
    public IReadOnlyList<HostVein>? Veins { get; init; }
    public IReadOnlyList<HostRefinery>? Refineries { get; init; }
    /// <summary>T26: the map's bridges (absent when it has none).</summary>
    public IReadOnlyList<HostBridge>? Bridges { get; init; }
    /// <summary>T20 debug block, present only when the step asked for it.</summary>
    public HostDebug? Debug { get; init; }
}

/// <summary>Serializes a ulong as an exact lowercase 16-hex-char JSON string; parses the same
/// shape back (case-insensitive, must be exactly 16 hex digits). Format is part of protocol v1.</summary>
public sealed class HexHashConverter : JsonConverter<ulong>
{
    public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("hash must be a 16-hex-digit string");
        var s = reader.GetString()!;
        if (s.Length != 16)
            throw new JsonException($"hash must be exactly 16 hex chars, got \"{s}\"");
        return ulong.Parse(s, System.Globalization.NumberStyles.HexNumber);
    }

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("x16"));
}

public sealed record HostUnit(
    int Id, int Faction, long X, long Y, long Vx, long Vy, long Hp, long MaxHp,
    bool Harvest, int Load, bool Carrying,
    string? BuildingId, int ConstructionRemaining, IReadOnlyList<string>? ProductionQueue,
    int ConstructionTotal = 0, int QueueHeadRemaining = 0, int QueueHeadTotal = 0,
    string? DeploysTo = null, int FootprintW = 0, int FootprintH = 0,
    long[]? Rally = null, // T37: [x, y] Q32.32 raw, buildings with a rally point only
    int LoadCapacity = 0, // T38: a harvester's full load (credits), 0 for everything else
    string? Domain = null, // T25: "naval" / "hover" / "air"; absent = land (and buildings)
    bool? Engineer = null, // T26: true for engineers (repair bridges); absent otherwise
    string? Type = null, // T29: catalog id (unit or building) it was spawned from
    bool? Marked = null, // T29: marked (by an enemy's Marking) this tick
    bool? Conscriptable = null, // T29: can be cashed in (`conscript`)
    int? PhaseTicks = null, // T29: phase-capable: ticks left phased (0 = not phased)
    int? PhaseCooldown = null, // T29: phase-capable: ticks until it may phase again (0 = ready)
    bool? CanCapture = null, // T28: an engineer that may capture (not the Weaver)
    bool? Wrecked = null, // T28: a destroyed derrick waiting for an engineer (neutral, 1 hp)
    int? Bridge = null, // T28: a bridge hut: the bridge id an engineer sent here repairs
    bool? MindControl = null, // Mindbender: capture is a channel (not consumed)
    bool? Repairs = null, // Weaver: capture on its own damaged building repairs it
    int? ChannelTarget = null, // Mindbender: entity it is channelling on (absent = none)
    int? Channel = null, // ticks channelled so far
    int? ChannelNeed = null, // ticks the channel takes in all
    int? ControlledBy = null, // mind-controlled by this Mindbender id
    long Sight = 0, // sight radius, Q32.32 raw (fog is presentation: the client draws it)
    long? Radar = null, // Seam Radar: its owner also sees this far around it (raw)
    int? Inside = null, // riding in this transport / bunker / tunnel id (hide it)
    bool? Stealth = null, // stealthy (cloaks on its own)
    bool? Cloaked = null, // cloaked this tick: enemies see it only when Detected has their bit
    int? Detected = null, // bit f set = faction f has a detector on it this tick
    int? Shield = null, // damage cut this tick by an Anchor / Warden, percent
    int? Sabotaged = null, // ticks it stays shut down by an Operative
    bool? Sabotages = null, // Operative: capture an enemy building = shut it down
    bool? Hijacks = null, // Hijacker: capture an enemy vehicle = steal it
    int? Carrier = null, // carrier aircraft: the carrier it belongs to
    string? Holds = null, // "transport" / "garrison" / "tunnel": you can `load` units into it
    string? Carries = null, // "infantry" / "ground"
    int? Capacity = null, // passenger slots in all (infantry 1, others 4)
    int? Used = null, // slots taken (a tunnel: across its side's tunnels)
    bool? Teleport = null, // Teleport Node
    int? Charge = null, // superweapon: ticks left to charge (0 = ready)
    int? ChargeNeed = null, // superweapon: full charge time, ticks
    long? StrikeRadius = null, // superweapon: strike radius (raw)
    long[]? Strike = null, // superweapon: [x, y] raw of a strike on its way
    int? StrikeIn = null, // ticks until it lands
    string? Control = null, // a hold-to-capture rig: neutral / occupied / contested / held / fortified
    int? HoldFaction = null, // the side taking it
    int? Hold = null, // ticks it has held
    int? HoldNeed = null); // ticks it needs

/// <summary>T30: credits and the power ledger (net = supply − draw; low power when net &lt; 0).</summary>
public sealed record HostFaction(int Faction, int Credits, int PowerSupply = 0, int PowerDraw = 0,
    IReadOnlyList<int>? Trees = null); // T28: factions whose buildings it may place, own first; absent = own only

/// <summary>T30: a resource vein (position Q32.32 raw) and the credits left in it.</summary>
public sealed record HostVein(int Id, long X, long Y, int Pool);

/// <summary>T30: a refinery drop-off point (scenario-placed until T33 makes them buildings).</summary>
public sealed record HostRefinery(int Faction, long X, long Y);

/// <summary>T26: a bridge: id (the `bridge` command's target), hp / maxHp (Q32.32 raw), broken
/// (its cells are water: ground units can't cross, ships pass), cells [[x, y], ...], causeway
/// (land when standing, so ships can't pass it; a bridge is `=`).</summary>
public sealed record HostBridge(int Id, long Hp, long MaxHp, bool Broken, IReadOnlyList<int[]> Cells, bool Causeway = false);

public sealed record HostMatch(bool Over, int Winner, int EndTick);

/// <summary>T36 catalogs (init only). Buildings: footprint in cells, power + supply / − draw,
/// prereq = building id or null. Units: builtFrom = producing building id.</summary>
public sealed record HostCatalog(IReadOnlyList<HostBuildingType> Buildings, IReadOnlyList<HostUnitType> Units);
public sealed record HostBuildingType(string Id, string Name, int Faction, string Category, int Cost, int BuildTicks,
    string? Prereq, int Power, int FootprintW, int FootprintH, string Description = "");
public sealed record HostUnitType(string Id, string Name, int Faction, int Cost, int BuildTicks, string BuiltFrom,
    string? DeploysTo, bool Harvest, string Domain = "land", string Description = "", // T25: land / naval / hover / air
    string Armor = ""); // infantry / light / heavy / building / aircraft (presentation: how big its death looks)

/// <summary>T36 `place` query answer: allowed, else reason (wrong_faction, prereq, blocked,
/// occupied, radius, credits; T25 water = a naval/offshore footprint not wholly on water, shore =
/// a `requiresShore` building with no land / water beside it); the footprint rect either way.</summary>
public sealed record HostQueryResult(bool Allowed, string Reason, int X, int Y, int W, int H);

/// <summary>T20 overlays. StepMicros = wall time of the World.Step that produced this snapshot.
/// Per unit: State (idle, move, attack-move, attack, guard, returning), Target (-1 = none) and the
/// remaining path as [x, y] Q32.32 raw pairs (capped at MaxPathPoints). Presentation only; never hashed.</summary>
public sealed record HostDebug(long StepMicros, IReadOnlyList<HostDebugUnit> Units)
{
    public const int MaxPathPoints = 32;
}

public sealed record HostDebugUnit(int Id, string State, int Target, IReadOnlyList<long[]> Path);

/// <summary>One sim event (T09). kind: "shot" (subject hit target for amount, Q32.32 raw),
/// "death" (subject died, target = killer, faction = dead unit's), "match_end" (faction = winner,
/// -1 = draw; subject/target -1). Ids are -1 where they do not apply.</summary>
public sealed record HostEvent(int Tick, string Kind, int Subject, int Target, long Amount, int Faction);

/// <summary>Strict decoder/encoder. Unknown fields, missing fields, and wrong types are
/// errors — a presentation bug must surface, not silently drive the sim wrong.</summary>
public static class Codec
{
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    public static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static HostRequest DecodeRequest(string line)
    {
        using var doc = JsonDocument.Parse(line, ParseOptions);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new ProtocolException("malformed_request", "request must be a JSON object");

        var version = GetInt(root, "protocolVersion");
        var kind = GetString(root, "kind");
        var req = new HostRequest
        {
            ProtocolVersion = version,
            Kind = kind,
            Scenario = kind == "init" ? GetObject(root, "scenario") : null,
            Tick = kind == "step" ? GetInt(root, "tick") : null,
            Commands = kind == "step" && root.TryGetProperty("commands", out var cmds) && cmds.ValueKind == JsonValueKind.Array
                ? cmds.Clone() : null,
            Debug = kind == "step" && root.TryGetProperty("debug", out var dbg) && GetBool(dbg, "debug"),
            Query = kind == "query" ? GetObject(root, "query") : null,
        };

        if (kind is not ("init" or "step" or "reset" or "query"))
            throw new ProtocolException("unknown_kind", $"kind '{kind}' not supported");

        // Kind-specific shape validation (version is checked by the host so it can echo its own).
        if (kind == "init")
        {
            if (req.Scenario is null)
                throw new ProtocolException("malformed_request", "init requires a 'scenario' object");
            if (req.Scenario.Value.TryGetProperty("commands", out _))
                throw new ProtocolException("malformed_request", "scenario 'commands' not allowed in init; send them via step");
            Require(root, allowedInit);
        }
        else if (kind == "reset")
        {
            Require(root, allowedReset);
        }
        else if (kind == "query")
        {
            Require(root, allowedQuery);
        }
        else
        {
            if (root.TryGetProperty("commands", out var stepCmds) && stepCmds.ValueKind != JsonValueKind.Array)
                throw new ProtocolException("malformed_request", "step 'commands' must be an array");
            Require(root, allowedStep);
        }

        return req;

        static void Require(JsonElement root, string[] allowed)
        {
            foreach (var p in root.EnumerateObject())
                if (!allowed.Contains(p.Name))
                    throw new ProtocolException("malformed_request", $"unexpected field '{p.Name}'");
        }
    }

    private static readonly string[] allowedInit = ["protocolVersion", "kind", "scenario"];
    private static readonly string[] allowedStep = ["protocolVersion", "kind", "tick", "commands", "debug"];
    private static readonly string[] allowedReset = ["protocolVersion", "kind"];
    private static readonly string[] allowedQuery = ["protocolVersion", "kind", "query"];

    public static string Encode(HostResponse resp) => JsonSerializer.Serialize(resp, WriteOptions);

    private static bool GetBool(JsonElement el, string name) => el.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new ProtocolException("malformed_request", $"'{name}' must be a boolean"),
    };

    private static int GetInt(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var el))
            throw new ProtocolException("malformed_request", $"missing '{name}'");
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var v))
            throw new ProtocolException("malformed_request", $"'{name}' must be an integer");
        return v;
    }

    private static string GetString(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
            throw new ProtocolException("malformed_request", $"missing '{name}' string");
        return el.GetString()!;
    }

    private static JsonElement? GetObject(JsonElement o, string name)
    {
        if (!o.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind != JsonValueKind.Object)
            throw new ProtocolException("malformed_request", $"'{name}' must be an object");
        return el.Clone();
    }
}

public sealed class ProtocolException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
