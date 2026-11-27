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
        int Cost, int BuildTicks, string BuiltFrom, string? DeploysTo = null, string Name = "",
        Rts.Sim.World.MoveDomain Domain = Rts.Sim.World.MoveDomain.Land,
        Rts.Sim.World.TargetMask Targets = Rts.Sim.World.TargetMask.None, bool Engineer = false,
        bool MarksOnHit = false, Fix64 MarkRadius = default, bool Conscriptable = false, bool Detonates = false,
        bool Phases = false, bool CannotCapture = false, string Description = "",
        bool MindControl = false, bool RepairsStructures = false, Rts.Sim.World.Abilities? Ability = null)
    {
        /// <summary>The spawn-time stat block (every spawner builds the same one).</summary>
        public Rts.Sim.World.UnitProfile Profile() => new(Faction, Hp, Sight, Damage, Range, CooldownTicks,
            Armor, DamageType, Harvest, DeploysTo, Domain, Targets, Engineer,
            Id, MarksOnHit, MarkRadius, Conscriptable, Detonates, Phases, CannotCapture,
            MindControl, RepairsStructures, Ability);
    }

    /// <summary>True when unitId names a unit this building (catalog id) fields:
    /// canon link is the unit's `builtFrom` (content/CONTEXT.md), nothing else.</summary>
    public bool Builds(string buildingCatalogId, string unitId) =>
        _byId.TryGetValue(unitId, out var p) && p.BuiltFrom == buildingCatalogId;

    private readonly Dictionary<string, SpawnParams> _byId = new();
    public static readonly Fix64 DefaultRadius = Fix64.Ratio(3, 10); // 0.3 cells: unit radius, tuning value until unit.json grows one

    /// <summary>Content faction name -> sim faction index: coalition 0, hegemony 1, ascendant 2.
    /// Unknown names fail loudly rather than silently joining coalition. T28: `neutral` (map
    /// structures such as the oil derrick) is Factions.Neutral, -1.</summary>
    public static int FactionIndex(string? name) => name switch
    {
        "neutral" => Rts.Sim.World.Factions.Neutral,
        "coalition" => 0,
        "hegemony" => 1,
        "ascendant" => 2,
        _ => throw new InvalidDataException($"unknown faction '{name}'"),
    };

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
            var faction = FactionIndex(root.GetProperty("faction").GetString());
            var armorName = root.GetProperty("armor").GetString()!;
            var armor = DamageMatrix.ParseArmor(armorName);
            // T25: explicit `domain` (hover units) wins; otherwise armor says air / naval / land
            var domain = root.TryGetProperty("domain", out var dm)
                ? Rts.Sim.World.Domains.Parse(dm.GetString()!)
                : Rts.Sim.World.Domains.FromArmor(armorName);
            var targets = Rts.Sim.World.TargetMask.None;
            var harvest = root.GetProperty("role").GetString() == "harvester"; // sim/economy income
            Fix64 dmg = Fix64.Zero, rng = Fix64.Zero; var cd = 0;
            var dtype = DamageType.SmallArms; // inert unless Damage > 0
            if (root.TryGetProperty("weapon", out var wp))
            {
                dmg = Fix64.FromDouble(wp.GetProperty("damage").GetDouble());
                rng = Fix64.FromDouble(wp.GetProperty("range").GetDouble());
                cd = wp.GetProperty("cooldownTicks").GetInt32();
                dtype = DamageMatrix.ParseDamageType(wp.GetProperty("damageType").GetString()!);
                targets = wp.TryGetProperty("targets", out var tg)
                    ? Rts.Sim.World.Domains.ParseTargets(tg)
                    : Rts.Sim.World.TargetMask.Ground;
            }
            cat._byId[root.GetProperty("id").GetString()!] = new(
                root.GetProperty("id").GetString()!, Fix64.FromDouble(speed), DefaultRadius,
                Fix64.FromDouble(hp), Fix64.FromDouble(sight), faction, dmg, rng, cd, armor, dtype, harvest,
                root.GetProperty("cost").GetInt32(),
                root.GetProperty("buildTimeSeconds").GetInt32() * Rts.Sim.Core.SimWorld.TicksPerSecond,
                root.GetProperty("builtFrom").GetString()!,
                root.TryGetProperty("deploysTo", out var dep) ? dep.GetString() : null,
                root.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                domain, targets, root.GetProperty("role").GetString() == "engineer",
                Verb(root, "marksOnHit"),
                root.TryGetProperty("verbs", out var vb) && vb.TryGetProperty("markRadius", out var mr) ? Fix64.FromDouble(mr.GetDouble()) : Fix64.Zero,
                Verb(root, "conscriptable"),
                root.TryGetProperty("verbs", out var vb2) && vb2.TryGetProperty("conscriptEffect", out var ce) && ce.GetString() == "detonate",
                Verb(root, "phases"),
                Verb(root, "cannotCapture"),
                Text(root, "description"),
                Verb(root, "mindControl"),
                Verb(root, "repairsStructures"),
                ParseAbilities(root));
        }
        return cat;
    }

    /// <summary>The `verbs` keys that make an Abilities record (content/CONTEXT.md); absent = None.
    /// Seconds become ticks here.</summary>
    internal static Rts.Sim.World.Abilities ParseAbilities(JsonElement root)
    {
        if (!root.TryGetProperty("verbs", out var v) || v.ValueKind != JsonValueKind.Object) return Rts.Sim.World.Abilities.None;
        bool B(string n) => v.TryGetProperty(n, out var f) && f.ValueKind == JsonValueKind.True;
        Fix64 F(string n) => v.TryGetProperty(n, out var f) && f.ValueKind == JsonValueKind.Number ? Fix64.FromDouble(f.GetDouble()) : Fix64.Zero;
        int I(string n, int d = 0) => v.TryGetProperty(n, out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : d;
        int Ticks(string n) => v.TryGetProperty(n, out var f) && f.ValueKind == JsonValueKind.Number
            ? (int)Math.Round(f.GetDouble() * Rts.Sim.Core.SimWorld.TicksPerSecond) : 0;
        string? S(string n) => v.TryGetProperty(n, out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() : null;
        var a = new Rts.Sim.World.Abilities
        {
            PhasesBetweenShots = B("phasesBetweenShots"),
            LineDamage = B("lineDamage"),
            NeverDepletes = B("neverDepletes"),
            ShieldRadius = F("shieldRadius"),
            ShieldPercent = I("shieldPercent"),
            ShieldNavalOnly = B("shieldNavalOnly"),
            HealRadius = F("healRadius"),
            HealPerTick = F("healPerSecond") / Fix64.FromInt(Rts.Sim.Core.SimWorld.TicksPerSecond),
            Sabotages = B("sabotages"),
            Hijacks = B("hijacks"),
            Launches = S("launches"),
            Wing = I("wing"),
            RebuildTicks = Ticks("rebuildSeconds"),
            LaunchRange = F("launchRange"),
            Capacity = I("capacity"),
            Carries = S("carries") ?? "infantry",
            Garrison = B("garrison"),
            Tunnel = B("tunnel"),
            Teleport = B("teleport"),
            Stealth = B("stealth"),
            CloakRadius = F("cloakRadius"),
            DetectRadius = F("detectRadius"),
            RadarRadius = F("radarRadius"),
            HoldCapture = B("holdCapture"),
            HoldRadius = F("holdRadius"),
            ChargeTicks = Ticks("chargeSeconds"),
            StrikeRadius = F("strikeRadius"),
            StrikeDamage = F("strikeDamage"),
            StrikePulses = I("strikePulses", 1),
            StrikeDelayTicks = Ticks("strikeDelaySeconds"),
            PulseGapTicks = v.TryGetProperty("pulseGapSeconds", out _) ? Ticks("pulseGapSeconds") : 10,
        };
        return a == new Rts.Sim.World.Abilities() ? Rts.Sim.World.Abilities.None : a;
    }

    /// <summary>An optional top-level string (absent = "").</summary>
    internal static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    /// <summary>T29: a boolean owned-verb flag under `verbs` (absent = false).</summary>
    internal static bool Verb(JsonElement root, string name) =>
        root.TryGetProperty("verbs", out var v) && v.TryGetProperty(name, out var f) && f.ValueKind == JsonValueKind.True;

    /// <summary>T33: the harvester a refinery ships and drops off at (a harvester unit whose
    /// `builtFrom` is this building), first by ordinal id; null when the building is no refinery.</summary>
    public string? HarvesterFor(string buildingCatalogId) =>
        _byId.Values.Where(p => p.Harvest && p.BuiltFrom == buildingCatalogId)
            .Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>T33: true when this building is a harvester drop-off (some harvester is built from it).</summary>
    public bool IsRefinery(string buildingCatalogId) => HarvesterFor(buildingCatalogId) != null;

    /// <summary>All unit types, ordinal id order (T36: the host lists them for the build menus).</summary>
    public IEnumerable<SpawnParams> All => _byId.Values.OrderBy(p => p.Id, StringComparer.Ordinal);

    public SpawnParams Get(string id) =>
        _byId.TryGetValue(id, out var p) ? p : throw new KeyNotFoundException($"unit '{id}' not in catalog");
}
