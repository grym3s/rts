using Rts.Sim.Core;

namespace Rts.Sim.Orders;

/// <summary>What a unit is doing between ticks. Owned by this folder; navigation reads it, never writes it.</summary>
public sealed class MoveOrder
{
    public FixVec2 Target { get; }
    /// <summary>Remaining path waypoints after the one the unit is walking to (cell centres; last is the target).
    /// Replaced wholesale when A* completes; navigation advances the cursor, nobody else.</summary>
    public List<FixVec2> Path { get; set; }
    /// <summary>Waypoint index currently being pursued.</summary>
    public int Waypoint { get; set; }
    public int IssuedTick { get; }
    public int? PlannedPathLength { get; set; }

    public MoveOrder(FixVec2 target, List<FixVec2> path, int issuedTick)
    {
        Target = target;
        Path = path;
        IssuedTick = issuedTick;
    }

    public FixVec2? CurrentWaypoint => Waypoint < Path.Count ? Path[Waypoint] : null;
}

/// <summary>Tick-1 system: drain due `Command`s into per-unit orders (tick step 1, see ../../CONTEXT.md).</summary>
public static class OrderSystem
{
    /// <summary>Unit id → its current order. Absent = idle. Owned by the composition layer, passed in — never static —
    /// so multiple worlds can live in one process (tests) without cross-contamination.
    /// Re-issuing Move replaces the old order (no shift-queue until the game layer defines it).</summary>
    public static void ApplyCommands(Dictionary<EntityId, MoveOrder> orders, IReadOnlyList<Command> dueCommands)
    {
        foreach (var c in dueCommands)
            if (c is MoveCommand m)
                foreach (var unit in m.Units)
                    orders[unit] = new MoveOrder(m.Target, new List<FixVec2>(), m.Tick);
        // AttackMove/Attack/Stop become real orders with their systems; until then they are consumed as no-ops
        // so a stray combat command cannot leave the inbox unbounded.
    }
}
