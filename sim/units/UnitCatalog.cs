using System.Text.Json;
using Rts.Sim.Combat;
using Rts.Sim.Core;

namespace Rts.Sim.Units;

/// <summary>Loads content/units/*.json (schema v2) into spawn parameters. Authoring doubles become Fix64 here only (ADR 0003).</summary>
public sealed class UnitCatalog
{
    public record SpawnParams(string Id, Fix64 Speed, Fix64 Radius, Fix64 Hp, Fix64 Sight,
        int Faction, Fix64 Damage, Fix64 Range, int CooldownTicks,
        ArmorClass Armor, DamageType DamageType, bool Harvest,
        int Cost, int BuildTicks, string BuiltFrom, string? DeploysTo = null);

    /// <summary>True when unitId names a unit this building (catalog id) fields:
    /// canon link is the unit's `builtFrom` (content/CONTEXT.md), nothing else.</summary>
    public bool Builds(string buildingCatalogId, string unitId) =>
        _byId.TryGetValue(unitId, out var p) && p.BuiltFrom == buildingCatalogId;

    private readonly Dictionary<string, SpawnParams> _byId = new();
    public static readonly Fix64 DefaultRadius = Fix64.Ratio(3, 10); // 0.3 cells: unit radius, tuning value until unit.json grows one

    public static UnitCatalog LoadFromDirectory(string path)
    {
        var cat = new UnitCatalog();
        foreach (var file in Directory.GetFiles(path, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var stats = root.GetProperty("stats");
            var speed = stats.GetProperty("speed").GetDouble();
            var hp = stats.GetProperty("hp").GetDouble();
            var sight = stats.TryGetProperty("sight", out var sg) ? sg.GetDouble() : 0.0;
            var faction = root.GetProperty("faction").GetString() == "hegemony" ? 1 : 0;
            var armor = DamageMatrix.ParseArmor(root.GetProperty("armor").GetString()!);
            var harvest = root.GetProperty("role").GetString() == "harvester"; // sim/economy income
            Fix64 dmg = Fix64.Zero, rng = Fix64.Zero; var cd = 0;
            var dtype = DamageType.SmallArms; // inert unless Damage > 0
            if (root.TryGetProperty("weapon", out var wp))
            {
                dmg = Fix64.FromDouble(wp.GetProperty("damage").GetDouble());
                rng = Fix64.FromDouble(wp.GetProperty("range").GetDouble());
                cd = wp.GetProperty("cooldownTicks").GetInt32();
                dtype = DamageMatrix.ParseDamageType(wp.GetProperty("damageType").GetString()!);
            }
            cat._byId[root.GetProperty("id").GetString()!] = new(
                root.GetProperty("id").GetString()!, Fix64.FromDouble(speed), DefaultRadius,
                Fix64.FromDouble(hp), Fix64.FromDouble(sight), faction, dmg, rng, cd, armor, dtype, harvest,
                root.GetProperty("cost").GetInt32(),
                root.GetProperty("buildTimeSeconds").GetInt32() * Rts.Sim.Core.SimWorld.TicksPerSecond,
                root.GetProperty("builtFrom").GetString()!,
                root.TryGetProperty("deploysTo", out var dep) ? dep.GetString() : null);
        }
        return cat;
    }

    /// <summary>T33: the harvester a refinery ships and drops off at (a harvester unit whose
    /// `builtFrom` is this building), first by ordinal id; null when the building is no refinery.</summary>
    public string? HarvesterFor(string buildingCatalogId) =>
        _byId.Values.Where(p => p.Harvest && p.BuiltFrom == buildingCatalogId)
            .Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>T33: true when this building is a harvester drop-off (some harvester is built from it).</summary>
    public bool IsRefinery(string buildingCatalogId) => HarvesterFor(buildingCatalogId) != null;

    public SpawnParams Get(string id) =>
        _byId.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"unit '{id}' not in catalog");
}
