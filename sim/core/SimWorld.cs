namespace Rts.Sim.Core;

/// <summary>State container and tick driver. Systems (in their own folders) are static functions over this. Order: see sim/CONTEXT.md.</summary>
public sealed class SimWorld
{
    public const int TicksPerSecond = 20;

    public int Tick { get; private set; }
    public Rng Rng { get; }
    public List<Command> PendingCommands { get; } = new();

    public SimWorld(ulong seed)
    {
        Rng = new Rng(seed);
    }

    /// <summary>Advance one fixed step. Commands stamped for a later tick are kept; earlier or equal are applied now.</summary>
    public void Step(IReadOnlyList<Command> commands)
    {
        foreach (var c in commands) PendingCommands.Add(c);
        // 1. apply commands → orders (ghost until sim/orders exists)
        PendingCommands.RemoveAll(c => c.Tick <= Tick);
        // 2–6. systems are added by the issues that create their folders.
        Tick++;
    }

    /// <summary>Cheap state hash for golden-replay tests. Extend as state grows.</summary>
    public ulong StateHash()
    {
        ulong h = 1469598103934665603UL;
        h = (h ^ (ulong)Tick) * 1099511628211UL;
        return h;
    }
}
