namespace Rts.Sim.Core;

public readonly record struct EntityId(int Value)
{
    public static readonly EntityId None = new(-1);
}

/// <summary>A tick-stamped input crossing the sim boundary. Everything the player or AI does is one of these.</summary>
public abstract record Command(int Tick, int Faction);

public sealed record MoveCommand(int Tick, int Faction, EntityId[] Units, FixVec2 Target, bool Queue) : Command(Tick, Faction);
public sealed record AttackMoveCommand(int Tick, int Faction, EntityId[] Units, FixVec2 Target, bool Queue) : Command(Tick, Faction);
public sealed record AttackCommand(int Tick, int Faction, EntityId[] Units, EntityId Target, bool Queue) : Command(Tick, Faction);
public sealed record StopCommand(int Tick, int Faction, EntityId[] Units) : Command(Tick, Faction);
