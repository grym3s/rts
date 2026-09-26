using System.Text.Json;
using Rts.Sim.Core;
using Rts.Tools.SimHost.Protocol;

namespace Rts.Tools.SimHost;

/// <summary>Request/response loop, one request = one response. Testable without stdio:
/// Handle(line) returns the response line. The host alone advances ticks: a `step`
/// advances EXACTLY one tick (world.Tick + 1); anything else is an error and the
/// world stays put, per the migration design ("tick gaps produce a recoverable error").</summary>
public sealed class SimHostSession(string repoRoot)
{
    private HostedWorld? _world;

    public string Handle(string line)
    {
        // The wire tick is the sim tick the commands execute AT. SimWorld applies due
        // commands while its own Tick counter equals that tick and increments at the end
        // of Step (identical to the scenario runner's loop). So request tick T is the
        // world.Step() that LEAVES Tick == T; commands stamped T execute inside it.
        HostRequest req;
        try
        {
            req = Codec.DecodeRequest(line);
        }
        catch (JsonException e)
        {
            return Codec.Encode(HostResponse.Fail("malformed_request", e.Message));
        }
        catch (ProtocolException e)
        {
            return Codec.Encode(HostResponse.Fail(e.Code, e.Message));
        }

        if (req.ProtocolVersion != HostRequest.CurrentProtocolVersion)
            return Codec.Encode(HostResponse.Fail("unsupported_protocol_version",
                $"host speaks protocolVersion {HostRequest.CurrentProtocolVersion}, request says {req.ProtocolVersion}"));

        try
        {
            return req.Kind switch
            {
                "init" => Init(req),
                "step" => Step(req),
                _ => Codec.Encode(HostResponse.Fail("unknown_kind", $"kind '{req.Kind}'")),
            };
        }
        catch (ProtocolException e)
        {
            // A rejected init never installed a world (Build throws before assignment).
            // A rejected step is always bounded to BEFORE World.Step — protocol/decode/
            // stamp errors cannot leave the world mid-advance — so an initialized world
            // survives and the client may continue with the next valid request.
            return Codec.Encode(HostResponse.Fail(e.Code, e.Message));
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidDataException or FormatException or IndexOutOfRangeException or OverflowException)
        {
            // Bad catalog id, malformed scenario JSON shape, etc. — recoverable error, no crash.
            // Same reasoning: these fire during init Build (no world installed) or during
            // command decoding before Step; the world is never advanced by a failing request.
            return Codec.Encode(HostResponse.Fail("sim_error", e.Message));
        }
    }

    private string Init(HostRequest req)
    {
        if (_world is not null)
            return Codec.Encode(HostResponse.Fail("already_initialized", "world exists; a new process is the way to reset"));

        _world = HostedWorld.Build(req.Scenario!.Value, repoRoot);

        return Codec.Encode(new HostResponse
        {
            Ok = true,
            Tick = _world.World.Tick,
            State = _world.Snapshot(),
            Events = [],
        });
    }

    private string Step(HostRequest req)
    {
        if (_world is null)
            return Codec.Encode(HostResponse.Fail("not_initialized", "send init before step"));

        var want = req.Tick!.Value;
        // World.Tick is the number of completed steps AND the stamp of the commands the
        // NEXT step applies (SimWorld semantics, same loop as the scenario runner):
        // request tick T == the world's current tick, so commands stamped T execute now.
        var current = _world.World.Tick;
        if (want < current)
            return Codec.Encode(HostResponse.Fail("tick_behind",
                $"tick {want} already simulated (host completed {current})"));
        if (want > current)
            return Codec.Encode(HostResponse.Fail("tick_gap",
                $"tick {want} skips ticks (host is at {current})"));

        var commands = _world.DecodeCommands(req.Commands);
        // Validate stamps against the step boundary before advancing (the scenario runner
        // filters by tick; here the stamp must equal the request tick or the wire is lying).
        foreach (var c in commands)
            if (c.Tick != want)
                return Codec.Encode(HostResponse.Fail("command_tick_mismatch",
                    $"command stamped tick {c.Tick} in step {want}"));

        _world.World.Step(commands);
        var events = _world.CaptureEvents(); // before the next Step flushes them

        return Codec.Encode(new HostResponse
        {
            Ok = true,
            Tick = _world.World.Tick,
            State = _world.Snapshot(),
            Events = events,
        });
    }
}
