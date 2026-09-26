using System.Text.Json;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Units;
using Rts.Sim.World;
using Rts.Tools.SimHost.Protocol;

namespace Rts.Tools.SimHost;

/// <summary>Builds a composed SimWorld from a scenario-v2 object (minus `commands`), using the
/// exact system wiring of tools/scenario/Program.cs. Rules live in sim/; this is plumbing only.
/// Deterministic replay parity with the scenario runner is asserted by tests — if wiring ever
/// drifts, the parity test fails, so the duplication is safe.</summary>
public sealed class HostedWorld
{
    public required SimWorld World { get; init; }
    public required UnitStore Units { get; init; }
    public required Rts.Sim.Economy.EconomyStore Econ { get; init; }
    public required GameMap Map { get; init; }

    /// <summary>Roster at load time. The scenario runner resolves a command with no explicit
    /// `units` against THIS list (computed once at scenario load), not the live roster —
    /// units spawned mid-run are never auto-selected. Parity requires the same freeze here.</summary>
    private EntityId[] _initialRoster = [];

    /// <summary>scenario = JSON object with schemaVersion(2), seed, map, optional units/veins/refineries.
    /// `commands` must have been excluded by the caller (host takes commands per step).</summary>
    public static HostedWorld Build(JsonElement scenario, string repoRoot)
    {
        var schema = scenario.GetProperty("schemaVersion").GetInt32();
        if (schema != 2)
            throw new ProtocolException("unsupported_scenario_schema",
                $"schemaVersion {schema} not supported (expects 2)");

        var seed = scenario.GetProperty("seed").GetUInt64();
        var mapEl = scenario.GetProperty("map");
        var map = new GameMap(mapEl.GetProperty("width").GetInt32(), mapEl.GetProperty("height").GetInt32());
        foreach (var r in mapEl.GetProperty("blocked").EnumerateArray())
            map.BlockRect(r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32(), r[3].GetInt32());

        var catalog = UnitCatalog.LoadFromDirectory(Path.Combine(repoRoot, "content", "units"));
        var world = new SimWorld(seed);
        var units = new UnitStore();
        if (scenario.TryGetProperty("units", out var unitEls))
            foreach (var u in unitEls.EnumerateArray())
            {
                var p = catalog.Get(u.GetProperty("unit").GetString()!);
                units.Spawn(world.Ids.Next(),
                    new FixVec2(Fix64.FromDouble(u.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(u.GetProperty("at")[1].GetDouble())),
                    p.Speed, p.Radius,
                    new UnitProfile(p.Faction, p.Hp, p.Sight, p.Damage, p.Range, p.CooldownTicks, p.Armor, p.DamageType, p.Harvest));
            }
        var roster = new EntityId[units.Units.Count];
        for (var i = 0; i < units.Units.Count; i++) roster[i] = units.Units[i].Id;

        var orders = new Dictionary<EntityId, MoveOrder>();
        var econ = new Rts.Sim.Economy.EconomyStore();
        var buildingsCat = BuildingCatalog.LoadFromDirectory(Path.Combine(repoRoot, "content", "buildings"));
        if (scenario.TryGetProperty("veins", out var veinsEl))
            foreach (var v in veinsEl.EnumerateArray())
                econ.AddVein(new FixVec2(Fix64.FromDouble(v.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(v.GetProperty("at")[1].GetDouble())),
                    v.TryGetProperty("pool", out var pool) ? pool.GetInt32() : Rts.Sim.Economy.EconomyStore.FieldPool);
        if (scenario.TryGetProperty("refineries", out var refsEl))
            foreach (var r in refsEl.EnumerateArray())
                econ.Refineries.Add(new Rts.Sim.Economy.EconomyStore.Refinery(
                    r.GetProperty("faction").GetInt32(),
                    new FixVec2(Fix64.FromDouble(r.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(r.GetProperty("at")[1].GetDouble()))));

        // Same registration order as tools/scenario = the tick order in sim/CONTEXT.md.
        world.Systems.Add((w, due) => OrderSystem.ApplyCommands(orders, due, units.Find, (f, a) => econ.TrySpend(f, a)));
        world.Systems.Add((w, due) => Rts.Sim.Economy.EconomySystem.Step(econ, units, orders, w.Tick));
        world.Systems.Add((w, due) => Rts.Sim.Production.ProductionSystem.Step(
            buildingsCat, catalog, units, map, w.Ids, due,
            (f, a) => econ.TrySpend(f, a),
            (f, a) => { econ.Deposit(f, a); return true; }));
        world.Systems.Add((w, due) => NavigationSystem.Step(map, units, orders));
        world.Systems.Add((w, due) => CombatSystem.Step(units, orders, w.Tick));
        world.Systems.Add((w, due) => units.DespawnDead());
        world.HashMixers.Add(units.Hash);
        world.HashMixers.Add(econ.Hash);
        world.HashMixers.Add(h => Rts.Sim.Production.ProductionStore.Hash(units, h));

        return new HostedWorld { World = world, Units = units, Econ = econ, Map = map, _initialRoster = roster };
    }

    /// <summary>Decode scenario-format command objects (tick-stamped). `allUnits` fills the
    /// default roster when a command omits `units` — same semantics as the scenario runner.</summary>
    public List<Command> DecodeCommands(JsonElement? commandsEl)
    {
        var commands = new List<Command>();
        if (commandsEl is not { } cmds) return commands;

        var allUnits = _initialRoster;

        foreach (var c in cmds.EnumerateArray())
        {
            var tick = c.GetProperty("tick").GetInt32();
            var faction = c.GetProperty("faction").GetInt32();
            var unitIds = c.TryGetProperty("units", out var sel)
                ? sel.EnumerateArray().Select(i => new EntityId(i.GetInt32())).ToArray()
                : allUnits;
            var hasTgt = c.TryGetProperty("target", out var target);
            // `target` is polymorphic by command type: move/attack-move carry an [x, y]
            // coordinate pair; `attack` carries a target unit id (EntityId), matching the
            // C# AttackCommand record. Reading either shape as the other is the ambiguity
            // this branch removes — an attack target must be a positive integer, never a
            // coordinate array or a silent Zero-vector fallback.
            var tgt = hasTgt && target.ValueKind == JsonValueKind.Array
                ? new FixVec2(Fix64.FromDouble(target[0].GetDouble()), Fix64.FromDouble(target[1].GetDouble())) : FixVec2.Zero;
            commands.Add(c.GetProperty("type").GetString() switch
            {
                "move" => new MoveCommand(tick, faction, unitIds, tgt, false),
                "attack-move" => new AttackMoveCommand(tick, faction, unitIds, tgt, false),
                "attack" => new AttackCommand(tick, faction, unitIds, DecodeAttackTarget(hasTgt ? target : null), false),
                "stop" => new StopCommand(tick, faction, unitIds),
                "spend" => new SpendCommand(tick, faction, c.GetProperty("amount").GetInt32()),
                "place" => new PlaceBuildingCommand(tick, faction, c.GetProperty("building").GetString()!,
                    new FixVec2(Fix64.FromDouble(c.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(c.GetProperty("at")[1].GetDouble()))),
                "train" => new TrainCommand(tick, faction, new EntityId(c.GetProperty("building").GetInt32()), c.GetProperty("unit").GetString()!),
                "cancel" => new CancelProductionCommand(tick, faction, new EntityId(c.GetProperty("building").GetInt32()), c.GetProperty("index").GetInt32()),
                var t => throw new ProtocolException("unknown_command_type", $"unknown command type '{t}'"),
            });
        }

        return commands;
    }

    /// <summary>An `attack` command's `target` is an entity id — strictly a JSON integer
    /// (non-negative). Anything else (coordinate array, string, float, missing) is a
    /// protocol error, not a silently-misinterpreted target. No sim rule here: this only
    /// pins the wire shape to the sim's AttackCommand(EntityId Target) record.</summary>
    private static EntityId DecodeAttackTarget(JsonElement? target)
    {
        if (target is not { } t)
            throw new ProtocolException("malformed_request", "attack command requires an integer 'target' entity id");
        if (t.ValueKind != JsonValueKind.Number || !t.TryGetInt32(out var id) || id < 0)
            throw new ProtocolException("malformed_request",
                $"attack 'target' must be an integer entity id, got {t.GetRawText()}");
        return new EntityId(id);
    }

    /// <summary>Read-only snapshot of post-step state. Events must be captured BEFORE the next
    /// Step (SimWorld flushes the buffer at step start).</summary>
    public HostState Snapshot()
    {
        var units = new List<HostUnit>(Units.Units.Count);
        foreach (var u in Units.Units)
        {
            units.Add(new HostUnit(
                u.Id.Value, u.Faction,
                u.Position.X.Raw, u.Position.Y.Raw,
                u.Velocity.X.Raw, u.Velocity.Y.Raw,
                u.Hp.Raw,
                u.Harvest, u.Load, u.Carrying,
                u.Building?.Id, u.Building?.ConstructionRemaining ?? 0,
                u.Building is { } b && b.Queue.Count > 0 ? b.Queue.ToList() : null));
        }

        var factions = new List<HostFaction>(2);
        for (var f = 0; f < 2; f++) factions.Add(new HostFaction(f, Econ.Credits(f)));

        return new HostState { Hash = World.StateHash(), Units = units, Factions = factions };
    }

    public List<HostEvent> CaptureEvents()
    {
        var evs = new List<HostEvent>(World.Events.Count);
        foreach (var e in World.Events) evs.Add(new HostEvent(e.Tick, e.Subject.Value, e.Target.Value));
        return evs;
    }
}
