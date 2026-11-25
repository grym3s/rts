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

/// <summary>Step-1 command (T32): an MCV-style unit (`Unit.DeploysTo`) becomes its construction
/// yard where it stands — free (paid when trained), the yard's footprint must be clear, the yard
/// then builds for its BuildTicks like any placed building. Rejected whole otherwise.</summary>
public sealed record DeployCommand(int Tick, int Faction, EntityId Unit) : Command(Tick, Faction);

/// <summary>Step-1 command (T37): set a producing building's rally point. Units it trains from
/// then on walk there (a plain move order) as they spawn. Own, live buildings only.</summary>
public sealed record RallyCommand(int Tick, int Faction, EntityId BuildingId, FixVec2 Target) : Command(Tick, Faction);

/// <summary>Step-2 command (T38): send own harvesters to a vein. It becomes their home vein; an
/// empty or part-filled one walks there now, a carrying one delivers first and then returns to it.
/// Ignored for a dry or unknown vein and for units that are not your live harvesters.</summary>
public sealed record HarvestCommand(int Tick, int Faction, EntityId[] Units, int Vein) : Command(Tick, Faction);

/// <summary>T29 Phase: phase-capable units go untargetable for 2 s (10 s cooldown after).</summary>
public sealed record PhaseCommand(int Tick, int Faction, EntityId[] Units) : Command(Tick, Faction);

/// <summary>T29 Conscription: cash in conscriptable infantry (50% of cost near a hall, else 30%;
/// a Sapper detonates instead).</summary>
public sealed record ConscriptCommand(int Tick, int Faction, EntityId[] Units) : Command(Tick, Faction);

/// <summary>T28: engineers walk to `Target` (an enemy or neutral building, or an enemy MCV) and
/// capture it on touch: it changes owner and the engineer is consumed.</summary>
public sealed record CaptureCommand(int Tick, int Faction, EntityId[] Units, EntityId Target) : Command(Tick, Faction);

/// <summary>T26: work on bridge `Bridge` (BridgeStore id): armed units attack it, engineers repair it.</summary>
public sealed record BridgeCommand(int Tick, int Faction, EntityId[] Units, int Bridge) : Command(Tick, Faction);
