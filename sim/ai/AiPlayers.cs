using Rts.Sim.Core;
using Rts.Sim.Production;
using Rts.Sim.Setup;
using Rts.Sim.Superweapons;
using Rts.Sim.World;

namespace Rts.Sim.Ai;

/// <summary>The computer opponents of one match: one `AiBrain` per faction in the scenario's
/// `ai` list (see CONTEXT.md). `Think` is a `SimWorld.InputSources` hook.</summary>
public sealed class AiPlayers
{
    private readonly List<AiBrain> _brains = new();
    public IReadOnlyList<int> Factions => _brains.Select(b => b.Faction).ToList();
    /// <summary>Attack waves sent so far, all computer players together.</summary>
    public int Waves => _brains.Sum(b => b.Waves);

    public void Add(int faction)
    {
        if (_brains.All(b => b.Faction != faction)) _brains.Add(new AiBrain(faction));
    }

    public IEnumerable<Command> Think(SimWorld w, MatchWorld m)
    {
        if (_brains.Count == 0 || m.Match.Over) return Array.Empty<Command>();
        var cmds = new List<Command>();
        foreach (var b in _brains)
            if (w.Tick % AiBrain.ThinkTicks == b.Phase) b.Think(w.Tick, m, cmds);
        return cmds;
    }

    /// <summary>The brains' memory (waves sent) is state that decides commands: hash it.</summary>
    public ulong Hash(ulong h)
    {
        foreach (var b in _brains) h = (h ^ (ulong)(uint)(b.Faction * 1000 + b.Waves) ^ 0xA1UL) * 1099511628211UL;
        return h;
    }
}

/// <summary>One computer side. Once a second it: deploys its MCV; places the next building of
/// its faction's build order (power first whenever the next one would sink it) on a lattice
/// around its yard; keeps every production building training (the least-fielded armed unit it
/// can afford) and its refineries at two harvesters; gathers idle army at a rally point toward
/// the map centre and, once the wave is big enough, attack-moves it at the nearest enemy
/// building; pulls the army home when enemies come near its base; fires a charged superweapon
/// at the costliest enemy building. Integer maths and store order only.</summary>
public sealed class AiBrain
{
    public const int ThinkTicks = SimWorld.TicksPerSecond;
    public int Faction { get; }
    public int Phase { get; }
    public int Waves { get; private set; }
    private int _deployTries;

    public AiBrain(int faction)
    {
        Faction = faction;
        Phase = (faction * 7 + 3) % ThinkTicks;
    }

    private static readonly Dictionary<int, string[]> BuildOrder = new()
    {
        [0] = new[] { "coalition-reactor", "coalition-refinery", "coalition-barracks", "coalition-reactor", "coalition-vehicle-bay",
            "coalition-refinery", "coalition-guard-turret", "coalition-reactor", "coalition-sensor-command", "coalition-guard-turret",
            "coalition-reactor", "coalition-airbase", "coalition-reactor", "coalition-aerospace-command", "coalition-reactor",
            "coalition-reactor", "coalition-orbital-lance", "coalition-precision-turret", "coalition-sam-site", "coalition-reactor",
            "coalition-vehicle-bay", "coalition-barracks" },
        [1] = new[] { "hegemony-furnace", "hegemony-refinery", "hegemony-conscription-hall", "hegemony-furnace", "hegemony-war-factory",
            "hegemony-refinery", "hegemony-bunker", "hegemony-furnace", "hegemony-industrial-works", "hegemony-cannon-tower",
            "hegemony-furnace", "hegemony-helipad", "hegemony-furnace", "hegemony-war-academy", "hegemony-furnace",
            "hegemony-furnace", "hegemony-siege-cannon", "hegemony-flak-cannon", "hegemony-spotlight-bunker", "hegemony-furnace",
            "hegemony-war-factory", "hegemony-conscription-hall" },
        [2] = new[] { "ascendant-conduit", "ascendant-wellspring", "ascendant-manifold", "ascendant-conduit", "ascendant-forge",
            "ascendant-wellspring", "ascendant-phase-turret", "ascendant-conduit", "ascendant-nexus", "ascendant-rift-node",
            "ascendant-conduit", "ascendant-aerie", "ascendant-conduit", "ascendant-ascendancy", "ascendant-conduit",
            "ascendant-conduit", "ascendant-rift", "ascendant-skyward-spire", "ascendant-teleport-node", "ascendant-conduit",
            "ascendant-forge", "ascendant-manifold" },
    };

    /// <summary>Units the army waits for before the first attack; each later wave asks for 3 more, up to 24.</summary>
    public const int FirstWave = 6;
    public int WaveSize => Math.Min(24, FirstWave + 3 * Waves);
    /// <summary>Enemies this close to any of its buildings pull the army home.</summary>
    public static readonly Fix64 DefendRadius = Fix64.FromInt(16);

    public void Think(int tick, MatchWorld m, List<Command> cmds)
    {
        var units = m.Units;
        var mine = units.Units.Where(u => u.Faction == Faction && u.Hp.Raw > 0).ToList();
        if (mine.Count == 0) return;
        var yard = mine.FirstOrDefault(u => u.Building is { } b && m.BuildingsCatalog.Get(b.Id).Category == "construction-yard");
        var credits = m.Econ.Credits(Faction);

        // 1. no base yet: deploy (move off a blocked spot after a few tries)
        if (yard == null)
        {
            var mcv = mine.FirstOrDefault(u => u.Building == null && u.DeploysTo != null && u.Inside == EntityId.None);
            if (mcv != null)
            {
                if (++_deployTries % 4 == 0)
                    cmds.Add(new MoveCommand(tick, Faction, new[] { mcv.Id }, Toward(mcv.Position, Centre(m), 4), false));
                else cmds.Add(new DeployCommand(tick, Faction, mcv.Id));
                return;
            }
        }
        var home = yard?.Position ?? mine.FirstOrDefault(u => u.Building != null)?.Position ?? mine[0].Position;

        // 2. build: one at a time, power first when the next building would sink it
        if (yard != null && !mine.Any(u => u.Building is { ConstructionRemaining: > 0 }))
            if (NextBuilding(m, mine) is { } next)
            {
                var bp = m.BuildingsCatalog.Get(next);
                var near = m.UnitsCatalog.IsRefinery(next) && NearestVein(m, home) is { } v && (v.Position - home).Length <= Fix64.FromInt(48)
                    ? v.Position : (FixVec2?)null;
                if (credits >= bp.Cost && Spot(m, next, near ?? home, near == null && bp.Category == "defense") is { } at)
                {
                    cmds.Add(new PlaceBuildingCommand(tick, Faction, next, at));
                    credits -= bp.Cost;
                }
            }

        // 3. idle harvesters with nothing in reach go to the nearest live field
        foreach (var hv in mine)
        {
            if (!hv.Harvest || hv.Building != null || hv.Carrying || m.Orders.ContainsKey(hv.Id)) continue;
            if (NearestVein(m, hv.Position) is { } v && (v.Position - hv.Position).Length > Fix64.FromInt(2))
                cmds.Add(new HarvestCommand(tick, Faction, new[] { hv.Id }, v.Id));
        }

        // 4. train: harvesters up to two per refinery, then the army (deeper queues when rich)
        var depth = credits >= 3000 ? 4 : 2;
        var harvesters = mine.Count(u => u.Harvest && u.Building == null);
        var refineries = mine.Count(u => u.Building != null && m.UnitsCatalog.IsRefinery(u.Building.Id));
        foreach (var b in mine)
        {
            if (b.Building is not { ConstructionRemaining: 0 } bs || bs.Queue.Count >= depth) continue;
            var options = m.UnitsCatalog.All.Where(p => p.BuiltFrom == bs.Id).ToList();
            if (options.Count == 0) continue;
            if (options.FirstOrDefault(p => p.Harvest) is { } hv)
            {
                if (harvesters < refineries * 2 && credits >= hv.Cost + 300)
                {
                    cmds.Add(new TrainCommand(tick, Faction, b.Id, hv.Id));
                    credits -= hv.Cost;
                    harvesters++;
                }
                continue;
            }
            var army = options.Where(p => (p.Damage.Raw > 0 || p.Ability is { IsCarrier: true } || p.Ability is { ShieldPercent: > 0 })
                && !p.Engineer && p.DeploysTo == null && !p.Harvest && p.Cost > 0 && p.Cost <= credits).ToList();
            if (army.Count == 0) continue;
            var pick = army.OrderBy(p => mine.Count(u => u.TypeId == p.Id)).ThenBy(p => p.Id, StringComparer.Ordinal).First();
            cmds.Add(new TrainCommand(tick, Faction, b.Id, pick.Id));
            credits -= pick.Cost;
        }

        // 5. army: defend, else gather and send waves
        var soldiers = mine.Where(u => u.Building == null && u.Inside == EntityId.None && u.CarrierId == EntityId.None
            && (u.Damage.Raw > 0 || u.Ability.IsCarrier || u.Ability.ShieldPercent > 0) && !u.Harvest && u.DeploysTo == null).ToList();
        var threat = Threat(m, mine);
        if (threat != null)
        {
            var idle = soldiers.Where(u => !u.AttackMoving && u.TargetId == EntityId.None).Select(u => u.Id).ToArray();
            if (idle.Length > 0) cmds.Add(new AttackMoveCommand(tick, Faction, idle, threat.Position, false));
        }
        else
        {
            var rally = Toward(home, Centre(m), 10);
            var idle = soldiers.Where(u => !m.Orders.ContainsKey(u.Id) && u.TargetId == EntityId.None && !u.Guarding).ToList();
            var away = idle.Where(u => (u.Position - rally).Length > Fix64.FromInt(6)).Select(u => u.Id).ToArray();
            if (away.Length > 0) cmds.Add(new MoveCommand(tick, Faction, away, rally, false));
            var gathered = soldiers.Count(u => (u.Position - rally).Length <= Fix64.FromInt(8));
            if (gathered >= WaveSize && Target(m, home) is { } target)
            {
                cmds.Add(new AttackMoveCommand(tick, Faction, soldiers.Select(u => u.Id).ToArray(), target.Position, false));
                Waves++;
            }
        }

        // 6. superweapons
        foreach (var sw in mine)
            if (SuperweaponSystem.Ready(sw, tick) && Costliest(m) is { } t)
                cmds.Add(new FireCommand(tick, Faction, sw.Id, t.Position));
    }

    /// <summary>The live vein nearest `at` (list order breaks ties).</summary>
    private static Rts.Sim.Economy.EconomyStore.Vein? NearestVein(MatchWorld m, FixVec2 at)
    {
        Rts.Sim.Economy.EconomyStore.Vein? best = null;
        var bestD = Fix64.Zero;
        foreach (var v in m.Econ.Veins)
        {
            if (v.Pool <= 0) continue;
            var d = (v.Position - at).LengthSquared;
            if (best == null || d < bestD) { best = v; bestD = d; }
        }
        return best;
    }

    /// <summary>Production buildings it adds once the build order is done and money piles up.</summary>
    public const int RichCredits = 3000;
    public const int MaxExtraFactories = 4;

    private string? NextBuilding(MatchWorld m, List<Unit> mine)
    {
        var next = OrderedBuilding(m, mine);
        if (next != null || m.Econ.Credits(Faction) < RichCredits || !BuildOrder.TryGetValue(Faction, out var order)) return next;
        // past the order and rich: another unit-producing building (the cheapest kind it has fewest of)
        var factories = order.Distinct().Where(id => m.UnitsCatalog.All.Any(p => p.BuiltFrom == id && !p.Harvest && p.Cost > 0)).ToList();
        var extra = mine.Count(u => u.Building is { } b && factories.Contains(b.Id)) - factories.Sum(id => order.Count(o => o == id));
        if (extra >= MaxExtraFactories) return null;
        var pick = factories.OrderBy(id => mine.Count(u => u.Building?.Id == id)).ThenBy(id => id, StringComparer.Ordinal).FirstOrDefault();
        if (pick == null) return null;
        var bp = m.BuildingsCatalog.Get(pick);
        return bp.Power < 0 && m.Power.Net(Faction) + bp.Power < 0 ? order.FirstOrDefault(o => m.BuildingsCatalog.Get(o).Power > 0) : pick;
    }

    private string? OrderedBuilding(MatchWorld m, List<Unit> mine)
    {
        if (!BuildOrder.TryGetValue(Faction, out var order)) return null;
        var have = new Dictionary<string, int>();
        foreach (var u in mine)
            if (u.Building is { } b) have[b.Id] = have.GetValueOrDefault(b.Id) + 1;
        var seen = new Dictionary<string, int>();
        foreach (var id in order)
        {
            seen[id] = seen.GetValueOrDefault(id) + 1;
            if (have.GetValueOrDefault(id) >= seen[id]) continue;
            if (!ProductionRules.PrereqSatisfied(m.BuildingsCatalog, id, req => have.ContainsKey(req))) continue;
            var bp = m.BuildingsCatalog.Get(id);
            if (bp.Power < 0 && m.Power.Net(Faction) + bp.Power < 0)
                return order.FirstOrDefault(o => m.BuildingsCatalog.Get(o).Power > 0);
            return id;
        }
        return null;
    }

    /// <summary>The first cell on a 4-cell lattice around `home` (rings outward, scan order) where
    /// the placement rules say yes; defenses look on the side facing the map centre first.</summary>
    private FixVec2? Spot(MatchWorld m, string id, FixVec2 home, bool forward)
    {
        var centre = forward ? Toward(home, Centre(m), 8) : home;
        var cx = GameMap.CellOf(centre.X);
        var cy = GameMap.CellOf(centre.Y);
        for (var r = 1; r <= 5; r++)
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    var (x, y) = (cx + dx * 4, cy + dy * 4);
                    if (!m.Map.InBounds(x, y)) continue;
                    var at = GameMap.CellCenter(x, y);
                    var v = ProductionSystem.CheckPlace(m.BuildingsCatalog, m.Units, m.Map, Faction, id, at, int.MaxValue);
                    if (v.Verdict == ProductionSystem.PlaceVerdict.Ok) return at;
                }
        return null;
    }

    private Unit? Threat(MatchWorld m, List<Unit> mine)
    {
        var r2 = DefendRadius * DefendRadius;
        foreach (var e in m.Units.Units)
        {
            if (e.Faction == Faction || e.Faction == Factions.Neutral || e.Hp.Raw <= 0 || e.Building != null || e.Inside != EntityId.None) continue;
            if (!Rts.Sim.Combat.Support.CanSee(Faction, e, m.World.Tick)) continue;
            foreach (var b in mine)
                if (b.Building != null && (e.Position - b.Position).LengthSquared <= r2) return e;
        }
        return null;
    }

    /// <summary>The nearest enemy building (not neutral), else the nearest enemy unit.</summary>
    private Unit? Target(MatchWorld m, FixVec2 from)
    {
        foreach (var buildings in new[] { true, false })
        {
            Unit? best = null;
            var bestD = Fix64.Zero;
            foreach (var e in m.Units.Units)
            {
                if (e.Faction == Faction || e.Faction == Factions.Neutral || e.Hp.Raw <= 0 || e.Inside != EntityId.None) continue;
                if ((e.Building != null) != buildings) continue;
                var d = (e.Position - from).LengthSquared;
                if (best == null || d < bestD) { best = e; bestD = d; }
            }
            if (best != null) return best;
        }
        return null;
    }

    private Unit? Costliest(MatchWorld m)
    {
        Unit? best = null;
        var bestCost = -1;
        foreach (var e in m.Units.Units)
        {
            if (e.Faction == Faction || e.Faction == Factions.Neutral || e.Hp.Raw <= 0 || e.Building is not { } b) continue;
            var cost = m.BuildingsCatalog.Get(b.Id).Cost;
            if (cost > bestCost) { best = e; bestCost = cost; }
        }
        return best;
    }

    private static FixVec2 Centre(MatchWorld m) => new(Fix64.FromInt(m.Map.Width / 2), Fix64.FromInt(m.Map.Height / 2));

    private static FixVec2 Toward(FixVec2 from, FixVec2 to, int cells)
    {
        var d = to - from;
        var len = d.Length;
        if (len.Raw == 0) return from;
        var step = Fix64.Min(Fix64.FromInt(cells), len);
        return new FixVec2(from.X + d.X * step / len, from.Y + d.Y * step / len);
    }
}
