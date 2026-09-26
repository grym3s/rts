using System.Text;
using Rts.Tools.SimHost;

// Sim host: newline-delimited JSON-lines over stdin/stdout. One request line in,
// one response line out. The engine (Unreal) owns the process lifecycle; this
// process alone advances SimWorld. See Protocol/Messages.cs for the wire records.

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

// Content root (content/units, content/buildings) resolution order:
// 1. RTS_CONTENT_ROOT env var — set this when running from a packaged location (e.g. an
//    Unreal build) where no checkout exists at or above the executable.
// 2. walk up from the executable looking for RTS.sln (development: repo/tools/bin/...).
var repoRoot = (Environment.GetEnvironmentVariable("RTS_CONTENT_ROOT") is var envRoot && Directory.Exists(envRoot))
    ? envRoot
    : FindRepoRoot(AppContext.BaseDirectory)
        ?? throw new InvalidDataException("no content root: set RTS_CONTENT_ROOT or run under the repo (RTS.sln)");

var session = new SimHostSession(repoRoot);
string? line;
while ((line = Console.ReadLine()) is not null)
{
    if (line.Trim().Length == 0) continue; // blank lines are framing slack, not requests
    Console.WriteLine(session.Handle(line));
    Console.Out.Flush(); // strict request/response: never buffer a reply behind the read
}

static string? FindRepoRoot(string start)
{
    var dir = new DirectoryInfo(start);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RTS.sln"))) dir = dir.Parent;
    return dir?.FullName;
}
