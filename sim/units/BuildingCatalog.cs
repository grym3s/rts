using System.Text.Json;
using Rts.Sim.Core;

namespace Rts.Sim.Units;

/// <summary>Loads content/buildings/*.json (schema v1) into placement parameters.
/// Building ids are faction-namespaced (`coalition-barracks`) — content/CONTEXT.md.
/// Category is the raw canon string; ProductionRules interprets what it can produce.</summary>
public sealed class BuildingCatalog
{
    public record PlacementParams(string Id, int Faction, string Category, int Cost, int BuildTicks, Fix64 Hp, string? Prereq, int Power,
        int FootprintW = 1, int FootprintH = 1, string Name = "", bool RequiresShore = false)
    {
        /// <summary>T25: naval and offshore buildings stand on water (shipyards, sea platforms).</summary>
        public bool OnWater => Category is "naval" or "offshore";
    }

    /// <summary>T31 footprint in cells when a file has no `footprint: [w, h]`. Tuning, not canon
    /// (canon gives no sizes): big production/economy 3x3, support 2x2, defences 1x1.</summary>
    public static (int W, int H) DefaultFootprint(string category) => category switch
    {
        "construction-yard" or "production" or "economy" or "naval" or "air" or "superweapon" => (3, 3),
        "power" or "tech" or "logistics" or "offshore" => (2, 2),
        _ => (1, 1), // defense and anything new
    };

    /// <summary>Bottom-left cell of a w x h footprint placed at `at`: the footprint is centred
    /// on at's cell (odd sizes) or on its lower-left corner point (even sizes).</summary>
    public static (int X, int Y) FootprintOrigin(int atCellX, int atCellY, int w, int h) => (atCellX - w / 2, atCellY - h / 2);

    private readonly Dictionary<string, PlacementParams> _byId = new();

    public static BuildingCatalog LoadFromDirectory(string path)
    {
        var cat = new BuildingCatalog();
        foreach (var file in Directory.GetFiles(path, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var factionStr = root.GetProperty("faction").GetString();
            // same mapping as UnitCatalog: coalition 0, hegemony 1, ascendant 2 (not playable yet, but it
            // must never count as coalition: that put Ascendant buildings in the Coalition build menu)
            var faction = UnitCatalog.FactionIndex(factionStr);
            var category = root.GetProperty("category").GetString()!;
            var (fpW, fpH) = root.TryGetProperty("footprint", out var fpEl)
                ? (fpEl[0].GetInt32(), fpEl[1].GetInt32())
                : DefaultFootprint(category);
            cat._byId[root.GetProperty("id").GetString()!] = new(
                root.GetProperty("id").GetString()!,
                faction,
                root.GetProperty("category").GetString()!,
                root.GetProperty("cost").GetInt32(),
                root.GetProperty("buildTimeSeconds").GetInt32() * SimWorld.TicksPerSecond,
                Fix64.FromDouble(root.GetProperty("stats").GetProperty("hp").GetDouble()),
                root.GetProperty("prereq").ValueKind == JsonValueKind.Null ? null : root.GetProperty("prereq").GetString(),
                root.TryGetProperty("power", out var powerEl) ? powerEl.GetInt32() : 0,
                fpW, fpH,
                root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "",
                root.TryGetProperty("requiresShore", out var shoreEl) && shoreEl.GetBoolean());
        }
        return cat;
    }

    public IEnumerable<PlacementParams> All => _byId.Values;

    public PlacementParams Get(string id) =>
        _byId.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"building '{id}' not in catalog");
}
