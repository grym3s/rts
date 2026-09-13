using System.Diagnostics;
using Rts.Sim.Core;
using Rts.Sim.Navigation;
using Rts.Sim.Orders;
using Rts.Sim.Units;
using Rts.Sim.World;

// Scale bench: N units crossing an open field with obstacles, M ticks. Reports ms/tick.
// Usage: dotnet run --project tools/bench -- [units=500] [ticks=1000]

var n = args.Length > 0 ? int.Parse(args[0]) : 500;
var ticks = args.Length > 1 ? int.Parse(args[1]) : 1000;

var repoRoot = FindRepoRoot();
var map = new GameMap(64, 64);
for (var i = 0; i < 30; i++) // scattered 2x2 obstacles, deterministic
{
    var x = (i * 7 + 3) % 60;
    var y = (i * 11 + 5) % 60;
    map.BlockRect(x, y, x + 1, y + 1);
}

var catalog = UnitCatalog.LoadFromDirectory(Path.Combine(repoRoot, "content", "units"));
var rifleman = catalog.Get("rifleman");
var (speed, radius) = (rifleman.Speed, rifleman.Radius);
var world = new SimWorld(1);
var units = new UnitStore();
var rng = new Rng(1234);

// deterministic scatter inside free cells
bool Free(int x, int y) => map.IsWalkable(x, y);
var starts = new List<(int x, int y)>();
for (var x = 1; x < 32 && starts.Count < n / 2; x++)
    for (var y = 1; y < 63 && starts.Count < n / 2; y++)
        if (Free(x, y)) starts.Add((x, y));
var goals = new List<(int x, int y)>();
for (var x = 33; x < 63 && goals.Count < n / 2; x++)
    for (var y = 1; y < 63 && goals.Count < n / 2; y++)
        if (Free(x, y)) goals.Add((x, y));

for (var i = 0; i < n; i++)
{
    var s = starts[i % starts.Count];
    units.Spawn(world.Ids.Next(), GameMap.CellCenter(s.x, s.y), speed, radius);
}

var orders = new Dictionary<EntityId, MoveOrder>();
world.Systems.Add((w, due) => OrderSystem.ApplyCommands(orders, due));
world.Systems.Add((w, due) => NavigationSystem.Step(map, units, orders));
world.HashMixers.Add(units.Hash);

var commands = new List<Command>();
for (var i = 0; i < n; i += 10) // groups of 10 per command
{
    var g = goals[(i / 10) % goals.Count];
    var ids = Enumerable.Range(i, Math.Min(10, n - i)).Select(k => units.Units[k].Id).ToArray();
    commands.Add(new MoveCommand(0, 0, ids, GameMap.CellCenter(g.x, g.y), false));
}

var none = Array.Empty<Command>();
var sw = Stopwatch.StartNew();
for (var t = 0; t < ticks; t++) world.Step(none);
sw.Stop();

var arrived = units.Units.Count(u => !orders.ContainsKey(u.Id));
Console.WriteLine($"units={n} ticks={ticks} ms/avg={(double)sw.ElapsedMilliseconds / ticks:F3} arrived={arrived}/{n} hash={world.StateHash():x16}");

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RTS.sln"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("RTS.sln not found above bench");
}
