namespace Rts.Sim.Power;

/// <summary>Per-faction net power (sum(supply) − sum(draw) over alive, construction-complete
/// buildings). Recomputed at tick step 2. While net &lt; 0, production countdown ticks only
/// when tick mod 2 != building id mod 2 (integer parity gate, halving without fractions).
/// Recovery immediate at net &gt;= 0, no hysteresis.</summary>
public sealed class PowerStore
{
    private readonly Dictionary<int, int> _net = new();

    public int Net(int faction) => _net.TryGetValue(faction, out var n) ? n : 0;

    public bool IsLowPower(int faction) => Net(faction) < 0;

    public void Set(int faction, int net) => _net[faction] = net;

    /// <summary>Mix into SimWorld.StateHash: skips everything unless power ever
    /// went negative, so pre-power golden hashes stay byte-identical.</summary>
    public ulong Hash(ulong h)
    {
        var touched = false;
        foreach (var n in _net.Values) if (n < 0) { touched = true; break; }
        if (!touched) return h;
        foreach (var (f, n) in _net.OrderBy(kv => kv.Key))
        {
            h = (h ^ (uint)f) * 1099511628211UL;
            h = (h ^ (uint)(uint)n) * 1099511628211UL;
        }
        return h;
    }
}
