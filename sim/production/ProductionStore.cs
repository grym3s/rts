using Rts.Sim.Core;
using Rts.Sim.World;

namespace Rts.Sim.Production;

/// <summary>Per-building mutable production state mounted on the building's Unit
/// (buildings ARE units: speed 0, Structure armor, raidable). Null on non-buildings.</summary>
public sealed class BuildingState
{
    /// <summary>Canon catalog id (`coalition-barracks`).</summary>
    public required string Id { get; init; }
    /// <summary>Construction: ticks remaining until alive; 0 = operational.</summary>
    public int ConstructionRemaining { get; set; }
    /// <summary>Queue of unit catalog ids; index 0 is counting down.</summary>
    public List<string> Queue { get; } = new();
    public int QueueHeadRemaining { get; set; }
}

/// <summary>Production state that isn't per-building: nothing today (queue lives on the
/// unit), but the store exists as the mixer anchor and future home of rally points.</summary>
public sealed class ProductionStore
{
    /// <summary>Queue length cap (tuning; canon is silent).</summary>
    public const int MaxQueue = 5;

    /// <summary>Mix into SimWorld.StateHash: skips everything unless production ever
    /// happened, so pre-production golden hashes stay byte-identical.</summary>
    public static ulong Hash(UnitStore units, ulong h)
    {
        var touched = false;
        foreach (var u in units.Units)
            if (u.Building is { } b && (b.Queue.Count > 0 || b.ConstructionRemaining > 0 || b.QueueHeadRemaining > 0))
            { touched = true; break; }
        if (!touched) return h;
        foreach (var u in units.Units)
        {
            if (u.Building is not { } b) continue;
            h = (h ^ (uint)b.ConstructionRemaining) * 1099511628211UL;
            h = (h ^ (uint)b.QueueHeadRemaining) * 1099511628211UL;
            foreach (var q in b.Queue)
                foreach (var ch in q) h = ((h ^ ch) * 1099511628211UL);
        }
        return h;
    }
}
