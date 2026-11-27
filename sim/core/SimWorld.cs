namespace Rts.Sim.Core;

/// <summary>What a SimEvent reports. Values are part of the host wire (as snake_case names).</summary>
public enum SimEventKind
{
    /// <summary>Subject fired at Target and dealt Amount damage (after the counter matrix and chip floor).</summary>
    Shot,
    /// <summary>Subject died this tick; Target is the killer, Faction the dead unit's faction.</summary>
    Death,
    /// <summary>The match ended this tick; Faction is the winner, -1 for a draw (everyone died together).</summary>
    MatchEnded,
    /// <summary>T26: Subject (a unit) hit bridge Target (bridge id) for Amount.</summary>
    BridgeHit,
    /// <summary>T26: bridge Subject (bridge id) collapsed; Target is the unit that dropped it.</summary>
    BridgeDown,
    /// <summary>T26: bridge Subject (bridge id) was repaired to full by engineer Target (consumed).</summary>
    BridgeRepaired,
    /// <summary>T28: unit Subject (building or MCV) was captured by engineer Target (consumed);
    /// Faction is the new owner.</summary>
    Captured,
    /// <summary>Building Subject was sabotaged by Operative Target (consumed); Amount = ticks offline.</summary>
    Sabotaged,
    /// <summary>Superweapon Subject fired; it lands Amount ticks from now at its StrikeAt.</summary>
    SuperweaponFired,
    /// <summary>Unit Subject teleported, or left a tunnel, at holder / node Target.</summary>
    Teleported,
}

/// <summary>Something that happened during a tick, for the presentation layer / replay consumers.
/// Read-only after emission. Events are not state: they never enter the state hash.</summary>
public readonly record struct SimEvent(int Tick, SimEventKind Kind, EntityId Subject, EntityId Target, Fix64 Amount, int Faction);

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

    /// <summary>Tick-step 2+ systems, registered by the composition layer (tools/scenario, game).
    /// Each receives the commands due this tick. Order matters (see sim/CONTEXT.md); registration order is the tick order.</summary>
    public List<Action<SimWorld, IReadOnlyList<Command>>> Systems { get; } = new();

    /// <summary>Command sources inside the sim (the AI): asked at the start of every step, before
    /// due commands are picked, so what they decide is a pure function of the state.</summary>
    public List<Func<SimWorld, IEnumerable<Command>>> InputSources { get; } = new();

    /// <summary>Hasher hooks (same determinism rules as Fix64): one per state owner, e.g. UnitStore.Hash.</summary>
    public List<Func<ulong, ulong>> HashMixers { get; } = new();

    public SimWorld(ulong seed)
    {
        Rng = new Rng(seed);
    }

    /// <summary>Emit an event for the current tick. Systems call this; the buffer is cleared at the next step's start.</summary>
    public void Emit(SimEventKind kind, EntityId subject, EntityId target, Fix64 amount = default, int faction = -1) =>
        _events.Add(new SimEvent(Tick, kind, subject, target, amount, faction));

    /// <summary>Advance one fixed step. Commands stamped for a later tick are kept; earlier or equal are applied now.</summary>
    public void Step(IReadOnlyList<Command> commands)
    {
        _events.Clear(); // drop the previous tick's events; consumers had one full frame to read them
        foreach (var c in commands) PendingCommands.Add(c);
        foreach (var source in InputSources) PendingCommands.AddRange(source(this));
        // 1. due commands (tick step 1: orders)
        var due = PendingCommands.FindAll(c => c.Tick <= Tick);
        PendingCommands.RemoveAll(c => c.Tick <= Tick);
        foreach (var system in Systems) system(this, due);
        Tick++;
    }

    /// <summary>Cheap state hash for golden-replay tests. Extend as state grows.</summary>
    public ulong StateHash()
    {
        ulong h = 1469598103934665603UL;
        h = (h ^ (ulong)Tick) * 1099511628211UL;
        h = (h ^ (ulong)Ids.Count) * 1099511628211UL;
        foreach (var mixer in HashMixers) h = mixer(h);
        return h;
    }
}
