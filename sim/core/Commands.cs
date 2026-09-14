namespace Rts.Sim.Core;

public readonly record struct EntityId(int Value)
{
    public static readonly EntityId None = new(-1);
}

/// <summary>Monotonic EntityId allocator. Lives on SimWorld so ids are a pure function of the command stream.</summary>
public sealed class EntityIdAllocator
{
    private int _next;
    public EntityId Next() => new(_next++);
    /// <summary>Current count of issued ids; replay hash input so a forked allocator is visible.</summary>
    public int Count => _next;
}

/// <summary>A tick-stamped input crossing the sim boundary. Everything the player or AI does is one of these.</summary>
public abstract record Command(int Tick, int Faction);

public sealed record MoveCommand(int Tick, int Faction, EntityId[] Units, FixVec2 Target, bool Queue) : Command(Tick, Faction);
public sealed record AttackMoveCommand(int Tick, int Faction, EntityId[] Units, FixVec2 Target, bool Queue) : Command(Tick, Faction);
public sealed record AttackCommand(int Tick, int Faction, EntityId[] Units, EntityId Target, bool Queue) : Command(Tick, Faction);
public sealed record StopCommand(int Tick, int Faction, EntityId[] Units) : Command(Tick, Faction);

/// <summary>Economy step-1 command: atomically deduct credits (fails silently when short —
/// the spend simply does not happen; sim/economy owns the balance).</summary>
public sealed record SpendCommand(int Tick, int Faction, int Amount) : Command(Tick, Faction);

/// <summary>Step-1 command: found a building at a cell (sim/production). Requires the
/// catalog prereq chain and spends cost atomically; construction completes after
/// BuildTicks (step 2). Rejected whole when prereq/cost/cell fail.</summary>
public sealed record PlaceBuildingCommand(int Tick, int Faction, string BuildingId, FixVec2 Cell) : Command(Tick, Faction);

/// <summary>Step-1 command: queue a unit at a producing building (sim/production).
/// spent immediately; refunds on cancel or if the building dies with the queue.</summary>
public sealed record TrainCommand(int Tick, int Faction, EntityId BuildingId, string UnitId) : Command(Tick, Faction);

/// <summary>Step-1 command: remove queue item `Index` at a building, refunding its cost.</summary>
public sealed record CancelProductionCommand(int Tick, int Faction, EntityId BuildingId, int Index) : Command(Tick, Faction);
