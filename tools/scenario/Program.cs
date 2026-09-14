using System.Text.Json;
using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Units;
using Rts.Sim.World;

// Headless scenario runner (schema v2): build a world, step N ticks, verify asserts, print the state hash.
// Usage: dotnet run --project tools/scenario -- content/scenarios/<name>.json [--expect <hash>]

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: scenario <path.json> [--expect <hash>]");
    return 2;
}

var scenarioPath = Path.GetFullPath(args[0]);
var repoRoot = FindRepoRoot(scenarioPath);
var doc = JsonDocument.Parse(File.ReadAllText(scenarioPath)).RootElement;
var schema = doc.GetProperty("schemaVersion").GetInt32();
if (schema != 2)
{
    Console.Error.WriteLine($"schemaVersion {schema} not supported by this runner (expects 2)");
    return 2;
}

var seed = doc.GetProperty("seed").GetUInt64();
var ticks = doc.GetProperty("ticks").GetInt32();

// --- map ---
var mapEl = doc.GetProperty("map");
var map = new GameMap(mapEl.GetProperty("width").GetInt32(), mapEl.GetProperty("height").GetInt32());
foreach (var r in mapEl.GetProperty("blocked").EnumerateArray())
    map.BlockRect(r[0].GetInt32(), r[1].GetInt32(), r[2].GetInt32(), r[3].GetInt32());

// --- units (spawn order == EntityId order) ---
var catalog = UnitCatalog.LoadFromDirectory(Path.Combine(repoRoot, "content", "units"));
var world = new SimWorld(seed);
var units = new UnitStore();
if (doc.TryGetProperty("units", out var unitEls))
    foreach (var u in unitEls.EnumerateArray())
    {
        var p = catalog.Get(u.GetProperty("unit").GetString()!);
        units.Spawn(world.Ids.Next(),
            new FixVec2(Fix64.FromDouble(u.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(u.GetProperty("at")[1].GetDouble())),
            p.Speed, p.Radius,
            new UnitProfile(p.Faction, p.Hp, p.Sight, p.Damage, p.Range, p.CooldownTicks, p.Armor, p.DamageType, p.Harvest));
    }

// --- orders + economy + production + navigation wired to the tick (steps 1-4 of sim/CONTEXT.md) ---
var orders = new Dictionary<EntityId, MoveOrder>();
var econ = new Rts.Sim.Economy.EconomyStore();
var buildingsCat = BuildingCatalog.LoadFromDirectory(Path.Combine(repoRoot, "content", "buildings"));
if (doc.TryGetProperty("veins", out var veinsEl))
    foreach (var v in veinsEl.EnumerateArray())
        econ.AddVein(new FixVec2(Fix64.FromDouble(v.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(v.GetProperty("at")[1].GetDouble())),
            v.TryGetProperty("pool", out var pool) ? pool.GetInt32() : Rts.Sim.Economy.EconomyStore.FieldPool);
if (doc.TryGetProperty("refineries", out var refsEl))
    foreach (var r in refsEl.EnumerateArray())
        econ.Refineries.Add(new Rts.Sim.Economy.EconomyStore.Refinery(
            r.GetProperty("faction").GetInt32(),
            new FixVec2(Fix64.FromDouble(r.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(r.GetProperty("at")[1].GetDouble()))));
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

// --- commands ---
var commands = new List<Command>();
if (doc.TryGetProperty("commands", out var cmds))
{
    var allUnits = new EntityId[units.Units.Count];
    for (var i = 0; i < units.Units.Count; i++) allUnits[i] = units.Units[i].Id;
    foreach (var c in cmds.EnumerateArray())
    {
        var tick = c.GetProperty("tick").GetInt32();
        var faction = c.GetProperty("faction").GetInt32();
        var unitIds = c.TryGetProperty("units", out var sel)
            ? sel.EnumerateArray().Select(i => new EntityId(i.GetInt32())).ToArray()
            : allUnits;
        var hasTgt = c.TryGetProperty("target", out var target);
        var tgt = hasTgt ? new FixVec2(Fix64.FromDouble(target[0].GetDouble()), Fix64.FromDouble(target[1].GetDouble())) : FixVec2.Zero;
        commands.Add(c.GetProperty("type").GetString() switch
        {
            "move" => new MoveCommand(tick, faction, unitIds, tgt, false),
            "attack-move" => new AttackMoveCommand(tick, faction, unitIds, tgt, false),
            "stop" => new StopCommand(tick, faction, unitIds),
            "spend" => new SpendCommand(tick, faction, c.GetProperty("amount").GetInt32()),
            "place" => new PlaceBuildingCommand(tick, faction, c.GetProperty("building").GetString()!,
                new FixVec2(Fix64.FromDouble(c.GetProperty("at")[0].GetDouble()), Fix64.FromDouble(c.GetProperty("at")[1].GetDouble()))),
            "train" => new TrainCommand(tick, faction, new EntityId(c.GetProperty("building").GetInt32()), c.GetProperty("unit").GetString()!),
            "cancel" => new CancelProductionCommand(tick, faction, new EntityId(c.GetProperty("building").GetInt32()), c.GetProperty("index").GetInt32()),
            var t => throw new InvalidDataException($"unknown command type '{t}'")
        });
    }
}

// --- asserts ---
var assert = doc.TryGetProperty("assert", out var a) ? a : default;
var arriveBy = assert.ValueKind != JsonValueKind.Undefined && assert.TryGetProperty("arriveByTick", out var abEl) ? abEl.GetInt32() : 0;
Fix64 arriveR = assert.ValueKind != JsonValueKind.Undefined && assert.TryGetProperty("arriveWithin", out var awEl) ? Fix64.FromDouble(awEl.GetDouble()) : Fix64.One;
FixVec2 arriveTarget = assert.ValueKind != JsonValueKind.Undefined && assert.TryGetProperty("arriveTarget", out var atEl)
    ? new FixVec2(Fix64.FromDouble(atEl[0].GetDouble()), Fix64.FromDouble(atEl[1].GetDouble()))
    : FixVec2.Zero;
Fix64 maxOverlapDepth = assert.ValueKind != JsonValueKind.Undefined && assert.TryGetProperty("maxOverlapDepth", out var moEl) ? Fix64.FromDouble(moEl.GetDouble()) : Fix64.MaxValue;
var creditIsFaction = -1;
var creditIsValue = 0;
if (assert.ValueKind != JsonValueKind.Undefined && assert.TryGetProperty("creditIs", out var credEl))
{
    creditIsFaction = credEl.GetProperty("faction").GetInt32();
    creditIsValue = credEl.GetProperty("value").GetInt32();
}

// --- run ---
var none = Array.Empty<Command>();
for (var t = 0; t < ticks; t++)
{
    var dueNow = commands.FindAll(c => c.Tick == t);
    world.Step(dueNow.Count > 0 ? dueNow : none);

    if (arriveBy > 0 && t + 1 == arriveBy)
    {
        foreach (var u in units.Units)
        {
            var d = (u.Position - arriveTarget).Length;
            if (d > arriveR)
            {
                Console.Error.WriteLine($"ASSERT FAIL tick {world.Tick}: unit {u.Id.Value} at {u.Position} is {d} from target (limit {arriveR})");
                return 1;
            }
        }
    }
}

// final overlap: deepest pairwise overlap = max(0, rA+rB-dist)
Fix64 worst = Fix64.Zero;
for (var i = 0; i < units.Units.Count; i++)
    for (var j = i + 1; j < units.Units.Count; j++)
    {
        var ua = units.Units[i]; var ub = units.Units[j];
        var minDist = ua.Radius + ub.Radius;
        var d = (ub.Position - ua.Position).Length;
        if (d < minDist) worst = Fix64.Max(worst, minDist - d);
    }
if (worst > maxOverlapDepth)
{
    Console.Error.WriteLine($"ASSERT FAIL: worst overlap {worst} exceeds {maxOverlapDepth}");
    return 1;
}
if (creditIsFaction >= 0 && econ.Credits(creditIsFaction) != creditIsValue)
{
    Console.Error.WriteLine($"ASSERT FAIL: faction {creditIsFaction} credits {econ.Credits(creditIsFaction)} != {creditIsValue}");
    return 1;
}
if (assert.ValueKind != JsonValueKind.Undefined && assert.TryGetProperty("buildingCountIs", out var bcEl))
{
    var want = bcEl.GetProperty("count").GetInt32();
    var alive = units.Units.Count(u => u.Building != null && u.Hp.Raw > 0);
    var constructing = units.Units.Count(u => u.Building is { ConstructionRemaining: > 0 });
    if (alive != want)
    {
        Console.Error.WriteLine($"ASSERT FAIL: buildings alive {alive} (constructing {constructing}) != {want}");
        return 1;
    }
}

var hash = world.StateHash();
Console.WriteLine($"{Path.GetFileName(args[0])}: tick={world.Tick} units={units.Units.Count} hash={hash:x16}");

var expectIdx = Array.IndexOf(args, "--expect");
if (expectIdx >= 0 && expectIdx + 1 < args.Length)
{
    var expected = Convert.ToUInt64(args[expectIdx + 1], 16);
    if (expected != hash) { Console.Error.WriteLine($"MISMATCH expected {expected:x16}"); return 1; }
    Console.WriteLine("hash matches");
}
return 0;

static string FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(Path.GetDirectoryName(start)!);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RTS.sln"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidDataException("RTS.sln not found above the scenario file");
}
