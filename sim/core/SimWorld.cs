namespace Rts.Sim.Core;

/// <summary>Something that happened during a tick, for the presentation layer / replay consumers. Read-only after emission.</summary>
public readonly record struct SimEvent(int Tick, EntityId Subject, EntityId Target);

/// <summary>State container and tick driver. Systems (in their own folders) are static functions over this. Order: see sim/CONTEXT.md.</summary>
public sealed class SimWorld
{
    public const int TicksPerSecond = 20;

    public int Tick { get; private set; }
    public Rng Rng { get; }
    public EntityIdAllocator Ids { get; } = new();
    public List<Command> PendingCommands { get; } = new();

    private readonly List<SimEvent> _events = new();
    /// <summary>Events emitted during the current tick; systems write via Emit, cleanup flushes at tick end.</summary>
    public IReadOnlyList<SimEvent> Events => _events;

    public SimWorld(ulong seed)
    {
        Rng = new Rng(seed);
    }

    /// <summary>Emit an event for the current tick. Systems call this; the buffer is cleared each tick end.</summary>
    public void Emit(EntityId subject, EntityId target = default) =>
        _events.Add(new SimEvent(Tick, subject, target));

    /// <summary>Advance one fixed step. Commands stamped for a later tick are kept; earlier or equal are applied now.</summary>
    public void Step(IReadOnlyList<Command> commands)
    {
        _events.Clear(); // drop the previous tick's events; consumers had one full frame to read them
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
        h = (h ^ (ulong)Ids.Count) * 1099511628211UL;
        return h;
    }
}
