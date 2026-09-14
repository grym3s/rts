using System;
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
    public static void ApplyCommands(Dictionary<EntityId, MoveOrder> orders, IReadOnlyList<Command> dueCommands,
        Func<EntityId, Rts.Sim.World.Unit?>? unitOf = null)
    {
        foreach (var c in dueCommands)
        {
            switch (c)
            {
                case MoveCommand m:
                    foreach (var unit in m.Units)
                        orders[unit] = new MoveOrder(m.Target, new List<FixVec2>(), m.Tick);
                    break;
                case AttackMoveCommand am:
                    foreach (var unit in am.Units)
                    {
                        orders[unit] = new MoveOrder(am.Target, new List<FixVec2>(), am.Tick);
                        if (unitOf != null && unitOf(unit) is { } u) u.AttackMoving = true;
                    }
                    break;
                case AttackCommand a:
                    foreach (var unit in a.Units)
                        if (unitOf != null && unitOf(unit) is { } u) u.TargetId = a.Target;
                    break;
                case StopCommand:
                    foreach (var unit in ((StopCommand)c).Units)
                    {
                        orders.Remove(unit);
                        if (unitOf != null && unitOf(unit) is { } u)
                        {
                            u.TargetId = EntityId.None;
                            u.AttackMoving = false;
                        }
                    }
                    break;
            }
        }
    }
}
