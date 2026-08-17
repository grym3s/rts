using System.Text.Json;
using Rts.Sim.Core;

// Headless scenario runner: load a scenario, step N ticks, print the state hash.
// Usage: dotnet run --project tools/scenario -- content/scenarios/smoke.json [--expect <hash>]

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: scenario <path.json> [--expect <hash>]");
    return 2;
}

var doc = JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;
var seed = doc.GetProperty("seed").GetUInt64();
var ticks = doc.GetProperty("ticks").GetInt32();
var world = new SimWorld(seed);
var none = Array.Empty<Command>();
for (var i = 0; i < ticks; i++) world.Step(none);

var hash = world.StateHash();
Console.WriteLine($"{Path.GetFileName(args[0])}: tick={world.Tick} hash={hash:x16}");

var expectIdx = Array.IndexOf(args, "--expect");
if (expectIdx >= 0 && expectIdx + 1 < args.Length)
{
    var expected = Convert.ToUInt64(args[expectIdx + 1], 16);
    if (expected != hash) { Console.Error.WriteLine($"MISMATCH expected {expected:x16}"); return 1; }
    Console.WriteLine("hash matches");
}
return 0;
