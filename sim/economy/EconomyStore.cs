using Rts.Sim.Core;

namespace Rts.Sim.Economy;

/// <summary>
/// Economy state that isn't on units: credit balances per faction, finite vein pools,
/// refinery positions (slice 1: refineries are scenario-placed points, not entities).
/// Integer credits only — every canon quantity in docs/factions/economy.md is a whole
/// credit; no fixed-point needed. Determinism: factions iterate 0..FactionCount-1,
/// veins/refineries in list order (scenario order = list order).
/// </summary>
public sealed class EconomyStore
{
    public const int StartingCredits = 5000;   // economy.md: skirmish starting credits
    public const int FieldPool = 25_000;       // economy.md: resource field pool

    private readonly int[] _credits = { StartingCredits, StartingCredits };

    public int Credits(int faction) => _credits[faction];

    /// <summary>Atomic spend: succeeds and deducts only if the balance covers it.</summary>
    public bool TrySpend(int faction, int amount)
    {
        if (amount < 0 || _credits[faction] < amount) return false;
        _credits[faction] -= amount;
        return true;
    }

    public void Deposit(int faction, int amount) => _credits[faction] += amount;

    /// <summary>Resource fields: finite pools (economy.md: 25,000 per field). Pool drains as harvesters fill.</summary>
    public sealed class Vein
    {
        public Vein(FixVec2 position, int pool) { Position = position; Pool = pool; }
        public FixVec2 Position { get; }
        public int Pool { get; set; }
        public int Id { get; internal set; }
    }

    public List<Vein> Veins { get; } = new();

    public sealed record Refinery(int Faction, FixVec2 Position);

    public List<Refinery> Refineries { get; } = new();

    public void AddVein(FixVec2 position, int pool) => Veins.Add(new Vein(position, pool) { Id = Veins.Count });

    public Vein? FindVein(int id)
    {
        foreach (var v in Veins) if (v.Id == id) return v;
        return null;
    }

    /// <summary>Mix into SimWorld.StateHash — balances, pools; refinery list is static after load.
    /// A never-touched economy (no veins, no refineries, starting balances) mixes nothing, so
    /// pre-economy golden hashes stay byte-identical.</summary>
    public ulong Hash(ulong h)
    {
        if (Veins.Count == 0 && Refineries.Count == 0 &&
            _credits.All(c => c == StartingCredits)) return h;
        foreach (var c in _credits)
            h = (h ^ (uint)c) * 1099511628211UL;
        foreach (var v in Veins)
        {
            h = (h ^ (uint)v.Pool) * 1099511628211UL;
            h = (h ^ (ulong)v.Position.X.Raw) * 1099511628211UL;
            h = (h ^ (ulong)v.Position.Y.Raw) * 1099511628211UL;
        }
        return h;
    }
}
