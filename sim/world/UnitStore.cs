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
            // T28: ownership mixes in only once it changed hands, so uncaptured worlds hash as before
            if (u.Faction != u.SpawnFaction) h = (h ^ (ulong)(uint)u.Faction ^ 0xC0FFEEUL) * 1099511628211UL;
            // Mindbender: channel progress and control mix in only while set
            if (u.ChannelTicks != 0) h = (h ^ (ulong)(uint)u.ChannelTicks ^ 0xC4A7UL) * 1099511628211UL;
            if (u.ControlledBy != EntityId.None)
            {
                h = (h ^ (ulong)(uint)u.ControlledBy.Value ^ 0xB3D0UL) * 1099511628211UL;
                h = (h ^ (ulong)(uint)u.OriginalFaction) * 1099511628211UL;
                h = (h ^ (ulong)(uint)u.ControlledSinceTick) * 1099511628211UL;
            }
        }
        return h;
    }
}

/// <summary>Immutable stat block copied from the catalog at spawn. Weapon null = unarmed.
/// Armor defaults to Infantry, weapon DamageType to SmallArms (only live when Damage > 0).</summary>
public sealed record UnitProfile(int Faction, Fix64 Hp, Fix64 Sight,
    Fix64 Damage, Fix64 Range, int CooldownTicks,
    ArmorClass Armor = ArmorClass.Infantry, DamageType DamageType = DamageType.SmallArms,
    bool Harvest = false, string? DeploysTo = null,
    MoveDomain Domain = MoveDomain.Land, TargetMask Targets = TargetMask.All, bool Engineer = false,
    string? TypeId = null, bool MarksOnHit = false, Fix64 MarkRadius = default,
    bool Conscriptable = false, bool Detonates = false, bool Phases = false, bool CannotCapture = false,
    bool MindControl = false, bool RepairsStructures = false);

/// <summary>T28: faction index of unowned map structures (the oil derrick). Nobody's enemy:
/// never targeted, never counted by the match, captured by an engineer touch.</summary>
public static class Factions
{
    public const int Neutral = -1;
}

/// <summary>Mutable unit state owned by the world.</summary>
public sealed class Unit
{
    public EntityId Id { get; }
    public FixVec2 Position { get; set; }
    public FixVec2 Velocity { get; set; }
    public Fix64 Speed { get; }
    public Fix64 Radius { get; }

    // combat (sim/combat owns mutation; others read)
    /// <summary>Owner. T28: changes when an engineer captures it (sim/combat CaptureSystem).</summary>
    public int Faction { get; set; }
    /// <summary>Owner at spawn (T28: the hash mixes Faction only once it differs).</summary>
    public int SpawnFaction { get; }
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
    /// <summary>MCV-style unit (T32): the construction-yard building id a `deploy` turns it into, else null.</summary>
    public string? DeploysTo { get; }
    /// <summary>T25: how it moves (navigation) and what can shoot it (combat).</summary>
    public MoveDomain Domain { get; }
    /// <summary>T25: domains its weapon can engage. All for hand-built test profiles.</summary>
    public TargetMask Targets { get; }
    /// <summary>T26: an engineer (role `engineer`): repairs bridges in one touch and is consumed.</summary>
    public bool Engineer { get; }
    /// <summary>T26: bridge this unit is attacking or repairing (BridgeStore id); -1 = none.
    /// Set by a `bridge` command, cleared by any other order (sim/combat owns it after that).</summary>
    public int BridgeTarget { get; set; } = -1;
    /// <summary>T28: the Ascendant Weaver repairs but can't capture (canon ascendant.md).</summary>
    public bool CannotCapture { get; }
    /// <summary>T28: building or MCV this engineer is walking to capture; None = no job.
    /// Set by a `capture` command, cleared by any other order (sim/combat owns it after that).</summary>
    public EntityId CaptureTarget { get; set; } = EntityId.None;
    /// <summary>Mindbender: its capture job is a channel (CaptureSystem), not a touch; not consumed.</summary>
    public bool MindControl { get; }
    /// <summary>Weaver: a capture job on its own damaged building repairs it over time.</summary>
    public bool RepairsStructures { get; }
    /// <summary>Mindbender: ticks channelled so far on CaptureTarget (0 = not channelling), and
    /// how many it needs (set each tick it channels; not state, so not hashed).</summary>
    public int ChannelTicks { get; set; }
    public int ChannelNeed { get; set; }
    /// <summary>Mind-controlled by this Mindbender (None = not controlled). It goes back to
    /// OriginalFaction when the Mindbender dies, changes sides or leaves the leash.</summary>
    public EntityId ControlledBy { get; set; } = EntityId.None;
    public int OriginalFaction { get; set; }
    public int ControlledSinceTick { get; set; }

    // T29 owned verbs (docs/factions/owned-verbs.md; rules in sim/combat/OwnedVerbs.cs)
    /// <summary>Catalog id (unit or building) it was spawned from; null for hand-built test units.</summary>
    public string? TypeId { get; }
    /// <summary>Marking: its hits mark the struck target (Rifleman).</summary>
    public bool MarksOnHit { get; }
    /// <summary>Marking: aura radius (Comms Rig, Spotter Drone, Sensor Command); zero = none.</summary>
    public Fix64 MarkRadius { get; }
    /// <summary>Marked by faction MarkedBy through tick MarkedUntilTick (-1 = never).</summary>
    public int MarkedUntilTick { get; set; } = -1;
    public int MarkedBy { get; set; } = -1;
    /// <summary>Conscription: can be cashed in (Hegemony infantry); Detonates = Sapper blast.</summary>
    public bool Conscriptable { get; }
    public bool Detonates { get; }
    /// <summary>Phase: can phase; phased (untargetable, can't fire) while tick &lt; PhaseUntilTick;
    /// may phase again from PhaseReadyTick.</summary>
    public bool Phases { get; }
    public int PhaseUntilTick { get; set; }
    public int PhaseReadyTick { get; set; }
    public bool IsPhased(int tick) => tick < PhaseUntilTick;
    public bool IsMarked(int tick) => tick <= MarkedUntilTick;
    /// <summary>Harvest load in whole credits; Carrying = full, trip to refinery.</summary>
    public int Load { get; set; }
    public bool Carrying { get; set; }
    /// <summary>Vein being harvested (return target after deposit); -1 = none.</summary>
    public int HomeVein { get; set; } = -1;
    // production (sim/production owns mutation): non-null = this unit is a building
    public Rts.Sim.Production.BuildingState? Building { get; set; }
    /// <summary>Unit being attacked (Attack order / auto-acquired). EntityId.None = no target.</summary>
    public EntityId TargetId { get; set; } = EntityId.None;
    /// <summary>True for units under an AttackMove order: auto-engage enemies along the route.</summary>
    public bool AttackMoving { get; set; }
    /// <summary>Guard (T05): an idle unit that auto-engaged. It chases within a leash of
    /// GuardAnchor, then walks back (Returning) and holds there again. Cleared by any order.</summary>
    public bool Guarding { get; set; }
    /// <summary>Walking back to GuardAnchor after a guard chase; does not acquire on the way.</summary>
    public bool Returning { get; set; }
    /// <summary>Where a guarding unit was standing when it engaged.</summary>
    public FixVec2 GuardAnchor { get; set; }

    public Unit(EntityId id, FixVec2 position, Fix64 speed, Fix64 radius, UnitProfile? profile = null)
    {
        Id = id;
        Position = position;
        Speed = speed;
        Radius = radius;
        Faction = profile?.Faction ?? 0;
        SpawnFaction = Faction;
        Hp = profile?.Hp ?? Fix64.FromInt(100);
        MaxHp = Hp;
        Sight = profile?.Sight ?? Fix64.Zero;
        Damage = profile?.Damage ?? Fix64.Zero;
        Range = profile?.Range ?? Fix64.Zero;
        CooldownTicks = profile?.CooldownTicks ?? 0;
        Armor = profile?.Armor ?? ArmorClass.Infantry;
        DamageType = profile?.DamageType ?? DamageType.SmallArms;
        Harvest = profile?.Harvest ?? false;
        DeploysTo = profile?.DeploysTo;
        Domain = profile?.Domain ?? MoveDomain.Land;
        Targets = profile?.Targets ?? TargetMask.All;
        Engineer = profile?.Engineer ?? false;
        TypeId = profile?.TypeId;
        MarksOnHit = profile?.MarksOnHit ?? false;
        MarkRadius = profile?.MarkRadius ?? Fix64.Zero;
        Conscriptable = profile?.Conscriptable ?? false;
        Detonates = profile?.Detonates ?? false;
        Phases = profile?.Phases ?? false;
        CannotCapture = profile?.CannotCapture ?? false;
        MindControl = profile?.MindControl ?? false;
        RepairsStructures = profile?.RepairsStructures ?? false;
    }
}
