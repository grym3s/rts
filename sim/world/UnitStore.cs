using Rts.Sim.Core;

namespace Rts.Sim.World;

/// <summary>Per-tick state that isn't a map: the unit roster. Spawn is deterministic: scenario order = id order.</summary>
public sealed class UnitStore
{
    private readonly List<Unit> _units = new();
    public IReadOnlyList<Unit> Units => _units;

    public Unit Spawn(EntityId id, FixVec2 position, Fix64 speed, Fix64 radius)
    {
        var u = new Unit(id, position, speed, radius);
        _units.Add(u);
        return u;
    }

    /// <summary>Feed into SimWorld.StateHashExtender; order-independent mix (sorted by id via list order = spawn order).</summary>
    public ulong Hash(ulong h)
    {
        foreach (var u in _units)
        {
            h = (h ^ (ulong)(uint)u.Id.Value) * 1099511628211UL;
            h = (h ^ (ulong)u.Position.X.Raw) * 1099511628211UL;
            h = (h ^ (ulong)u.Position.Y.Raw) * 1099511628211UL;
        }
        return h;
    }
}

/// <summary>Mutable unit state owned by the world. No HP/combat fields until sim/combat lands.</summary>
public sealed class Unit
{
    public EntityId Id { get; }
    public FixVec2 Position { get; set; }
    public FixVec2 Velocity { get; set; }
    public Fix64 Speed { get; }
    public Fix64 Radius { get; }

    public Unit(EntityId id, FixVec2 position, Fix64 speed, Fix64 radius)
    {
        Id = id;
        Position = position;
        Speed = speed;
        Radius = radius;
    }
}
