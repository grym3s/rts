using System.Text.Json;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Power;
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
    public required Rts.Sim.Match.MatchState Match { get; init; }
    /// <summary>Live move orders (read-only use: debug paths). Owned by the sim systems.</summary>
    public required Dictionary<EntityId, MoveOrder> Orders { get; init; }
    /// <summary>Catalogs and power ledger, read for snapshot detail (T30). Owned by the sim systems.</summary>
    public required UnitCatalog UnitsCatalog { get; init; }
    public required BuildingCatalog BuildingsCatalog { get; init; }
    public required PowerStore Power { get; init; }

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
        var map = GameMap.FromJson(scenario.GetProperty("map")); // T25: terrain rows and/or blocked rects

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
                    p.Profile());
            }
        var roster = new EntityId[units.Units.Count];
        for (var i = 0; i < units.Units.Count; i++) roster[i] = units.Units[i].Id;

        var orders = new Dictionary<EntityId, MoveOrder>();
        var econ = new Rts.Sim.Economy.EconomyStore();
        var buildingsCat = BuildingCatalog.LoadFromDirectory(Path.Combine(repoRoot, "content", "buildings"));
        if (scenario.TryGetProperty("veins", out var veinsEl))
            foreach (var v in veinsEl.EnumerateArray())
            {
                econ.AddVein(new FixVec2(Fix64.FromDouble(v.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(v.GetProperty("at")[1].GetDouble())),
                    v.TryGetProperty("pool", out var pool) ? pool.GetInt32() : Rts.Sim.Economy.EconomyStore.FieldPool);
                map.MarkResource(GameMap.CellOf(econ.Veins[^1].Position.X), GameMap.CellOf(econ.Veins[^1].Position.Y)); // T33: no building on ore
            }
        if (scenario.TryGetProperty("refineries", out var refsEl))
            foreach (var r in refsEl.EnumerateArray())
                econ.Refineries.Add(new Rts.Sim.Economy.EconomyStore.Refinery(
                    r.GetProperty("faction").GetInt32(),
                    new FixVec2(Fix64.FromDouble(r.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(r.GetProperty("at")[1].GetDouble()))));

        // Canonical tick order (see Wiki/Projects/rts-development.md, 2026-11-02): Power(1)
        // Income(2) Order(3) Production(4) Navigation(5) Deposit(6) Combat(7) Despawn(8) Match(9).
        // Economy is split: IncomeStep credits this tick's harvest before OrderSystem/Production
        // spend; DepositStep credits arrivals after NavigationSystem has moved units, so refinery
        // proximity reads post-move positions.
        var power = new PowerStore();
        world.Systems.Add((w, due) => PowerSystem.Step(power, buildingsCat, units));
        world.Systems.Add((w, due) => Rts.Sim.Economy.EconomySystem.IncomeStep(econ, units, orders, w.Tick, map, catalog.IsRefinery, due));
        world.Systems.Add((w, due) => OrderSystem.ApplyCommands(orders, due, units.Find, (f, a) => econ.TrySpend(f, a)));
        world.Systems.Add((w, due) => Rts.Sim.Production.ProductionSystem.Step(
            buildingsCat, catalog, units, map, w.Ids, due,
            (f, a) => econ.TrySpend(f, a),
            (f, a) => { econ.Deposit(f, a); return true; }, power, w.Tick,
            (id, at) => orders[id] = new MoveOrder(at, new List<FixVec2>(), w.Tick)));
        world.Systems.Add((w, due) => NavigationSystem.Step(map, units, orders));
        world.Systems.Add((w, due) => Rts.Sim.Economy.EconomySystem.DepositStep(econ, units, orders, w.Tick, map, catalog.IsRefinery));
        world.Systems.Add((w, due) => CombatSystem.Step(units, orders, w.Tick, w));
        world.Systems.Add((w, due) => units.DespawnDead());
        var match = new Rts.Sim.Match.MatchState();
        world.Systems.Add((w, due) => Rts.Sim.Match.MatchSystem.Step(w, units, match));
        world.HashMixers.Add(units.Hash);
        world.HashMixers.Add(econ.Hash);
        world.HashMixers.Add(h => Rts.Sim.Production.ProductionStore.Hash(units, h));
        world.HashMixers.Add(power.Hash);

        return new HostedWorld
        {
            World = world,
            Units = units,
            Econ = econ,
            Map = map,
            Match = match,
            Orders = orders,
            UnitsCatalog = catalog,
            BuildingsCatalog = buildingsCat,
            Power = power,
            _initialRoster = roster
        };
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
                "deploy" => new DeployCommand(tick, faction, new EntityId(c.GetProperty("unit").GetInt32())),
                "cancel" => new CancelProductionCommand(tick, faction, new EntityId(c.GetProperty("building").GetInt32()), c.GetProperty("index").GetInt32()),
                "harvest" => new HarvestCommand(tick, faction, unitIds, c.GetProperty("vein").GetInt32()),
                "rally" => new RallyCommand(tick, faction, new EntityId(c.GetProperty("building").GetInt32()),
                    new FixVec2(Fix64.FromDouble(c.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(c.GetProperty("at")[1].GetDouble()))),
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
    /// <summary>T20: per-unit state, target and remaining path for the debug overlays.
    /// Reads sim state only; State names mirror the guard/order flags in sim/combat and sim/orders.</summary>
    /// <summary>T36: both catalogs for the build menus (init only). Ordinal id order.</summary>
    public HostCatalog CatalogBlock() => new(
        BuildingsCatalog.All.OrderBy(b => b.Id, StringComparer.Ordinal)
            .Select(b => new HostBuildingType(b.Id, b.Name, b.Faction, b.Category, b.Cost, b.BuildTicks, b.Prereq, b.Power, b.FootprintW, b.FootprintH))
            .ToList(),
        UnitsCatalog.All
            .Select(u => new HostUnitType(u.Id, u.Name, u.Faction, u.Cost, u.BuildTicks, u.BuiltFrom, u.DeploysTo, u.Harvest,
                Rts.Sim.World.Domains.Name(u.Domain)))
            .ToList());

    /// <summary>T36: would `place` of this building at `at` be accepted right now? The sim's own
    /// check (ProductionSystem.CheckPlace), read-only.</summary>
    public HostQueryResult QueryPlace(int faction, string buildingId, FixVec2 at)
    {
        var (verdict, x, y, w, h) = Rts.Sim.Production.ProductionSystem.CheckPlace(
            BuildingsCatalog, Units, Map, faction, buildingId, at, Econ.Credits(faction));
        var reason = verdict switch
        {
            Rts.Sim.Production.ProductionSystem.PlaceVerdict.Ok => "ok",
            Rts.Sim.Production.ProductionSystem.PlaceVerdict.WrongFaction => "wrong_faction",
            Rts.Sim.Production.ProductionSystem.PlaceVerdict.Prereq => "prereq",
            Rts.Sim.Production.ProductionSystem.PlaceVerdict.Blocked => "blocked",
            Rts.Sim.Production.ProductionSystem.PlaceVerdict.Occupied => "occupied",
            Rts.Sim.Production.ProductionSystem.PlaceVerdict.Radius => "radius",
            Rts.Sim.Production.ProductionSystem.PlaceVerdict.Water => "water",
            Rts.Sim.Production.ProductionSystem.PlaceVerdict.Shore => "shore",
            _ => "credits",
        };
        return new HostQueryResult(verdict == Rts.Sim.Production.ProductionSystem.PlaceVerdict.Ok, reason, x, y, w, h);
    }

    public HostDebug DebugBlock(long stepMicros)
    {
        var list = new List<HostDebugUnit>(Units.Units.Count);
        foreach (var u in Units.Units)
        {
            var path = new List<long[]>();
            if (Orders.TryGetValue(u.Id, out var o))
                for (var i = Math.Max(0, o.Waypoint); i < o.Path.Count && path.Count < HostDebug.MaxPathPoints; i++)
                    path.Add([o.Path[i].X.Raw, o.Path[i].Y.Raw]);
            var state = u.Returning ? "returning"
                : u.Guarding ? "guard"
                : u.AttackMoving ? "attack-move"
                : u.TargetId != EntityId.None ? "attack"
                : o is not null ? "move"
                : "idle";
            list.Add(new HostDebugUnit(u.Id.Value, state, u.TargetId == EntityId.None ? -1 : u.TargetId.Value, path));
        }
        return new HostDebug(stepMicros, list);
    }

    public HostState Snapshot()
    {
        var units = new List<HostUnit>(Units.Units.Count);
        foreach (var u in Units.Units)
        {
            units.Add(new HostUnit(
                u.Id.Value, u.Faction,
                u.Position.X.Raw, u.Position.Y.Raw,
                u.Velocity.X.Raw, u.Velocity.Y.Raw,
                u.Hp.Raw, u.MaxHp.Raw,
                u.Harvest, u.Load, u.Carrying,
                u.Building?.Id, u.Building?.ConstructionRemaining ?? 0,
                u.Building is { } b && b.Queue.Count > 0 ? b.Queue.ToList() : null,
                u.Building is { } bc ? BuildingsCatalog.Get(bc.Id).BuildTicks : 0,
                u.Building is { } bq && bq.Queue.Count > 0 ? bq.QueueHeadRemaining : 0,
                u.Building is { } bt && bt.Queue.Count > 0 ? UnitsCatalog.Get(bt.Queue[0]).BuildTicks : 0,
                u.DeploysTo,
                u.Building is { } bf ? BuildingsCatalog.Get(bf.Id).FootprintW : 0,
                u.Building is { } bh ? BuildingsCatalog.Get(bh.Id).FootprintH : 0,
                u.Building?.Rally is { } rp ? new[] { rp.X.Raw, rp.Y.Raw } : null,
                u.Harvest ? Rts.Sim.Economy.EconomySystem.Capacity : 0,
                u.Domain == Rts.Sim.World.MoveDomain.Land ? null : Rts.Sim.World.Domains.Name(u.Domain)));
        }

        var factions = new List<HostFaction>(2);
        for (var f = 0; f < Rts.Sim.Economy.EconomyStore.FactionCount; f++)
            factions.Add(new HostFaction(f, Econ.Credits(f), Power.Supply(f), Power.Draw(f)));
        var veins = new List<HostVein>(Econ.Veins.Count);
        foreach (var v in Econ.Veins) veins.Add(new HostVein(v.Id, v.Position.X.Raw, v.Position.Y.Raw, v.Pool));
        var refineries = new List<HostRefinery>(Econ.Refineries.Count);
        foreach (var r in Econ.Refineries) refineries.Add(new HostRefinery(r.Faction, r.Position.X.Raw, r.Position.Y.Raw));

        return new HostState
        {
            Hash = World.StateHash(),
            Units = units,
            Factions = factions,
            Match = new HostMatch(Match.Over, Match.Winner, Match.EndTick),
            Veins = veins,
            Refineries = refineries,
        };
    }

    public List<HostEvent> CaptureEvents()
    {
        var evs = new List<HostEvent>(World.Events.Count);
        foreach (var e in World.Events)
            evs.Add(new HostEvent(e.Tick, KindName(e.Kind), e.Subject.Value, e.Target.Value, e.Amount.Raw, e.Faction));
        return evs;
    }

    /// <summary>Wire names for SimEventKind (protocol v1, additive). New kinds get a new name here.</summary>
    private static string KindName(SimEventKind k) => k switch
    {
        SimEventKind.Shot => "shot",
        SimEventKind.Death => "death",
        SimEventKind.MatchEnded => "match_end",
        _ => throw new ArgumentOutOfRangeException(nameof(k), k, "no wire name"),
    };
}
