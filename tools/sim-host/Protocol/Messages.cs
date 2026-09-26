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
    int Id, int Faction, long X, long Y, long Vx, long Vy, long Hp,
    bool Harvest, int Load, bool Carrying,
    string? BuildingId, int ConstructionRemaining, IReadOnlyList<string>? ProductionQueue);

public sealed record HostFaction(int Faction, int Credits);

public sealed record HostEvent(int Tick, int Subject, int Target);

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
        };

        if (kind is not ("init" or "step"))
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
    private static readonly string[] allowedStep = ["protocolVersion", "kind", "tick", "commands"];

    public static string Encode(HostResponse resp) => JsonSerializer.Serialize(resp, WriteOptions);

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
