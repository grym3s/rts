using Rts.Sim.Core;

namespace Rts.Sim.World;

/// <summary>Per-tick state that isn't a map: the unit roster. Spawn is deterministic: scenario order = id order.</summary>
public sealed class UnitStore
{
    private readonly List<Unit> _units = new();
    public IReadOnlyList<Unit> Units => _units;

    public Unit Spawn(EntityId id, FixVec2 position, Fix64 speed, Fix64 radius,
        UnitProfile? profile = null)
    {
        var u = new Unit(id, position, speed, radius, profile);
        _units.Add(u);
        return u;
    }

    /// <summary>Remove dead units (combat step 6 cleanup). Deterministic: store order.</summary>
    public void DespawnDead() => _units.RemoveAll(u => u.Hp.Raw <= 0);

    public Unit? Find(EntityId id)
    {
        foreach (var u in _units) if (u.Id == id) return u;
        return null;
    }

    /// <summary>Feed into SimWorld.StateHashExtender; order-independent mix (sorted by id via list order = spawn order).</summary>
    public ulong Hash(ulong h)
    {
        foreach (var u in _units)
        {
            h = (h ^ (ulong)(uint)u.Id.Value) * 1099511628211UL;
            h = (h ^ (ulong)u.Position.X.Raw) * 1099511628211UL;
            h = (h ^ (ulong)u.Position.Y.Raw) * 1099511628211UL;
            h = (h ^ (ulong)u.Hp.Raw) * 1099511628211UL;
        }
        return h;
    }
}

/// <summary>Immutable stat block copied from the catalog at spawn. Weapon null = unarmed.
/// Armor defaults to Infantry, weapon DamageType to SmallArms (only live when Damage > 0).</summary>
public sealed record UnitProfile(int Faction, Fix64 Hp, Fix64 Sight,
    Fix64 Damage, Fix64 Range, int CooldownTicks,
    ArmorClass Armor = ArmorClass.Infantry, DamageType DamageType = DamageType.SmallArms,
    bool Harvest = false);

/// <summary>Mutable unit state owned by the world.</summary>
public sealed class Unit
{
    public EntityId Id { get; }
    public FixVec2 Position { get; set; }
    public FixVec2 Velocity { get; set; }
    public Fix64 Speed { get; }
    public Fix64 Radius { get; }

    // combat (sim/combat owns mutation; others read)
    public int Faction { get; }
    public Fix64 Hp { get; set; }
    public Fix64 MaxHp { get; }
    public Fix64 Sight { get; }
    public Fix64 Damage { get; }
    public Fix64 Range { get; }
    public int CooldownTicks { get; }
    public int CooldownRemaining { get; set; }
    public ArmorClass Armor { get; }
    public DamageType DamageType { get; }
    // economy (sim/economy owns mutation; others read)
    public bool Harvest { get; }
    /// <summary>Harvest load in whole credits; Carrying = full, trip to refinery.</summary>
    public int Load { get; set; }
    public bool Carrying { get; set; }
    /// <summary>Vein being harvested (return target after deposit); -1 = none.</summary>
    public int HomeVein { get; set; } = -1;
    /// <summary>Unit being attacked (Attack order / auto-acquired). EntityId.None = no target.</summary>
    public EntityId TargetId { get; set; } = EntityId.None;
    /// <summary>True for units under an AttackMove order: auto-engage enemies along the route.</summary>
    public bool AttackMoving { get; set; }

    public Unit(EntityId id, FixVec2 position, Fix64 speed, Fix64 radius, UnitProfile? profile = null)
    {
        Id = id;
        Position = position;
        Speed = speed;
        Radius = radius;
        Faction = profile?.Faction ?? 0;
        Hp = profile?.Hp ?? Fix64.FromInt(100);
        MaxHp = Hp;
        Sight = profile?.Sight ?? Fix64.Zero;
        Damage = profile?.Damage ?? Fix64.Zero;
        Range = profile?.Range ?? Fix64.Zero;
        CooldownTicks = profile?.CooldownTicks ?? 0;
        Armor = profile?.Armor ?? ArmorClass.Infantry;
        DamageType = profile?.DamageType ?? DamageType.SmallArms;
        Harvest = profile?.Harvest ?? false;
    }
}
