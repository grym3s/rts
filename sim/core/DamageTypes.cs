namespace Rts.Sim.Core;

/// <summary>Weapon damage type — resolves against target ArmorClass (counter matrix lives
/// in sim/combat/DamageMatrix.cs). Values are the content/units schema v2 enums (content/CONTEXT.md).</summary>
public enum DamageType
{
    SmallArms,
    Autocannon,
    Ap,
    Explosive,
    Missile,
    Energy,
    NavalGun,
    Torpedo,
}

/// <summary>Target armor class — content schema v2 `armor` field.</summary>
public enum ArmorClass
{
    Infantry,
    Light,
    Heavy,
    Air,
    Structure,
    LightNaval,
    HeavyNaval,
    Submerged,
}
