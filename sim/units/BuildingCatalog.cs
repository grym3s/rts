using System.Text.Json;
using Rts.Sim.Core;

namespace Rts.Sim.Units;

/// <summary>Loads content/buildings/*.json (schema v1) into placement parameters.
/// Building ids are faction-namespaced (`coalition-barracks`) — content/CONTEXT.md.
/// Category is the raw canon string; ProductionRules interprets what it can produce.</summary>
public sealed class BuildingCatalog
{
    public record PlacementParams(string Id, int Faction, string Category, int Cost, int BuildTicks, Fix64 Hp, string? Prereq);

    private readonly Dictionary<string, PlacementParams> _byId = new();

    public static BuildingCatalog LoadFromDirectory(string path)
    {
        var cat = new BuildingCatalog();
        foreach (var file in Directory.GetFiles(path, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var factionStr = root.GetProperty("faction").GetString();
            // same 0/1 mapping as UnitCatalog for the two ground-slice factions; ascendant arrives with its slice
            var faction = factionStr == "hegemony" ? 1 : 0;
            cat._byId[root.GetProperty("id").GetString()!] = new(
                root.GetProperty("id").GetString()!,
                faction,
                root.GetProperty("category").GetString()!,
                root.GetProperty("cost").GetInt32(),
                root.GetProperty("buildTimeSeconds").GetInt32() * SimWorld.TicksPerSecond,
                Fix64.FromDouble(root.GetProperty("stats").GetProperty("hp").GetDouble()),
                root.GetProperty("prereq").ValueKind == JsonValueKind.Null ? null : root.GetProperty("prereq").GetString());
        }
        return cat;
    }

    public PlacementParams Get(string id) =>
        _byId.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"building '{id}' not in catalog");
}
