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
    /// <summary>GameMap.StructureEpoch when Path was planned (T31): navigation re-checks it after
    /// buildings appear or vanish.</summary>
    public int PlannedEpoch { get; set; }

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
        Func<EntityId, Rts.Sim.World.Unit?>? unitOf = null, Action<int, int>? spend = null)
    {
        foreach (var c in dueCommands)
        {
            switch (c)
            {
                case SpendCommand s:
                    spend?.Invoke(s.Faction, s.Amount); // atomic TrySpend lives in EconomyStore
                    break;
                case MoveCommand m:
                    foreach (var unit in m.Units)
                    {
                        orders[unit] = new MoveOrder(m.Target, new List<FixVec2>(), m.Tick);
                        // a plain move overrides any fight; otherwise combat's chase re-paths it
                        if (unitOf != null && unitOf(unit) is { } u) Disengage(u);
                    }
                    break;
                case AttackMoveCommand am:
                    foreach (var unit in am.Units)
                    {
                        orders[unit] = new MoveOrder(am.Target, new List<FixVec2>(), am.Tick);
                        if (unitOf != null && unitOf(unit) is { } u)
                        {
                            Disengage(u);
                            u.AttackMoving = true;
                        }
                    }
                    break;
                case AttackCommand a:
                    foreach (var unit in a.Units)
                    {
                        if (unitOf == null || unitOf(unit) is not { } u) continue;
                        orders.Remove(unit); // stand and fire when in range; combat issues the chase
                        Disengage(u);
                        u.TargetId = a.Target;
                    }
                    break;
                case BridgeCommand bc:
                    foreach (var unit in bc.Units)
                    {
                        if (unitOf == null || unitOf(unit) is not { } u || u.Faction != bc.Faction) continue;
                        orders.Remove(unit); // BridgeSystem walks it there
                        Disengage(u);
                        u.BridgeTarget = bc.Bridge;
                    }
                    break;
                case CaptureCommand cc:
                    // T28: engineers only; CaptureSystem walks them there. The Weaver can't capture,
                    // but a bridge hut is a repair job, not a capture, so any engineer may take it,
                    // and the Weaver repairs its own side's buildings. A Mindbender channels instead.
                    var capTarget = unitOf?.Invoke(cc.Target);
                    var hutJob = capTarget?.Building is { BridgeId: >= 0 };
                    foreach (var unit in cc.Units)
                    {
                        if (unitOf == null || unitOf(unit) is not { } u || u.Faction != cc.Faction) continue;
                        var repairJob = u.RepairsStructures && capTarget?.Faction == u.Faction;
                        var can = u.MindControl || u.Ability.Sabotages || u.Ability.Hijacks
                            || (u.Engineer && (!u.CannotCapture || hutJob || repairJob));
                        if (!can || u.Hp.Raw <= 0) continue;
                        orders.Remove(unit);
                        Disengage(u);
                        u.CaptureTarget = cc.Target;
                    }
                    break;
                case PhaseCommand ph:
                    // T29: not a move order: the unit keeps doing what it was doing, untargetable
                    foreach (var unit in ph.Units)
                    {
                        if (unitOf == null || unitOf(unit) is not { } u) continue;
                        if (u.Faction != ph.Faction || !u.Phases || u.Hp.Raw <= 0 || ph.Tick < u.PhaseReadyTick) continue;
                        u.PhaseUntilTick = ph.Tick + Rts.Sim.Combat.OwnedVerbs.PhaseTicks;
                        u.PhaseReadyTick = u.PhaseUntilTick + Rts.Sim.Combat.OwnedVerbs.PhaseCooldownTicks;
                    }
                    break;
                case StopCommand:
                    foreach (var unit in ((StopCommand)c).Units)
                    {
                        orders.Remove(unit);
                        if (unitOf != null && unitOf(unit) is { } u) Disengage(u);
                    }
                    break;
            }
        }
    }

    /// <summary>Every player order ends the unit's current fight: target, attack-move and guard state (T05).</summary>
    private static void Disengage(Rts.Sim.World.Unit u)
    {
        u.TargetId = EntityId.None;
        u.AttackMoving = false;
        u.Guarding = false;
        u.Returning = false;
        u.BridgeTarget = -1;
        u.CaptureTarget = EntityId.None;
        u.ChannelTicks = 0;
    }
}
