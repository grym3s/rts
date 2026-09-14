using Rts.Sim.Core;

namespace Rts.Sim.Combat;

/// <summary>
/// The counter matrix: damage multiplier per (DamageType, ArmorClass). Cells are canon —
/// see docs/factions/counter-matrix.md for the design dial (soft-with-cliffs, ADR 0005);
/// do not restate or tune numbers here. Deterministic Q31.32 constants: every cell is
/// FromDouble of its decimal (verified in tools/refmodel_counter_matrix.py, D-001).
/// "-" cells in canon are targeting facts (domain engagement), not multipliers — no entry needed here.
/// </summary>
public static class DamageMatrix
{
    /// <summary>Multiplier for a hit of dtype on armor. Total lookup error vs the decimal
    /// canon cell is bounded by 1 Q31.32 ulp of the cell value (FromDouble, round-half-even).</summary>
    public static Fix64 Get(DamageType dtype, ArmorClass armor) => (dtype, armor) switch
    {
        (DamageType.SmallArms, ArmorClass.Infantry) => Cell(1.5),
        (DamageType.SmallArms, ArmorClass.Light) => Cell(0.75),
        (DamageType.SmallArms, ArmorClass.Heavy) => Cell(0.35),
        (DamageType.SmallArms, ArmorClass.Air) => Fix64.One,   // canon "-": no air unit may exist
        (DamageType.SmallArms, ArmorClass.Structure) => Cell(0.5),

        (DamageType.Autocannon, ArmorClass.Infantry) => Cell(1.0),
        (DamageType.Autocannon, ArmorClass.Light) => Cell(1.5),
        (DamageType.Autocannon, ArmorClass.Heavy) => Cell(0.5),
        (DamageType.Autocannon, ArmorClass.Air) => Cell(1.25),
        (DamageType.Autocannon, ArmorClass.Structure) => Cell(0.75),

        (DamageType.Ap, ArmorClass.Infantry) => Cell(0.5),
        (DamageType.Ap, ArmorClass.Light) => Cell(1.25),
        (DamageType.Ap, ArmorClass.Heavy) => Cell(1.25),        // MBT mirror
        (DamageType.Ap, ArmorClass.Air) => Fix64.One,           // canon "-"
        (DamageType.Ap, ArmorClass.Structure) => Cell(0.75),

        (DamageType.Explosive, ArmorClass.Infantry) => Cell(1.5),
        (DamageType.Explosive, ArmorClass.Light) => Cell(1.0),
        (DamageType.Explosive, ArmorClass.Heavy) => Cell(0.6),
        (DamageType.Explosive, ArmorClass.Air) => Fix64.One,    // canon "-"
        (DamageType.Explosive, ArmorClass.Structure) => Cell(1.5),

        (DamageType.Missile, ArmorClass.Infantry) => Cell(0.6),
        (DamageType.Missile, ArmorClass.Light) => Cell(1.0),
        (DamageType.Missile, ArmorClass.Heavy) => Cell(1.5),
        (DamageType.Missile, ArmorClass.Air) => Cell(1.75),
        (DamageType.Missile, ArmorClass.Structure) => Cell(1.0),

        (DamageType.Energy, ArmorClass.Infantry) => Fix64.One,  // flat by design (ADR 0005)
        (DamageType.Energy, ArmorClass.Light) => Fix64.One,
        (DamageType.Energy, ArmorClass.Heavy) => Fix64.One,
        (DamageType.Energy, ArmorClass.Air) => Fix64.One,
        (DamageType.Energy, ArmorClass.Structure) => Cell(0.75),

        // naval extension — cells exactly as tabled in counter-matrix.md (naval rows);
        // cells absent from canon stay 1.0 pending the naval slice's tuning pass.
        (DamageType.NavalGun, ArmorClass.Infantry) => Cell(0.5),   // shares the AP column
        (DamageType.NavalGun, ArmorClass.Light) => Cell(1.25),
        (DamageType.NavalGun, ArmorClass.Heavy) => Cell(1.25),
        (DamageType.NavalGun, ArmorClass.Air) => Fix64.One,
        (DamageType.NavalGun, ArmorClass.Structure) => Cell(0.75),
        (DamageType.NavalGun, ArmorClass.LightNaval) => Cell(1.25),
        (DamageType.NavalGun, ArmorClass.HeavyNaval) => Cell(1.25),
        (DamageType.NavalGun, ArmorClass.Submerged) => Fix64.One,  // canon "-": targeting fact

        (DamageType.Torpedo, ArmorClass.LightNaval) => Cell(1.25),
        (DamageType.Torpedo, ArmorClass.HeavyNaval) => Cell(1.5),
        (DamageType.Torpedo, ArmorClass.Submerged) => Cell(1.5),

        (DamageType.Autocannon, ArmorClass.LightNaval) => Cell(1.25),
        (DamageType.Autocannon, ArmorClass.HeavyNaval) => Cell(0.5),
        (DamageType.Missile, ArmorClass.LightNaval) => Cell(1.0),
        (DamageType.Missile, ArmorClass.HeavyNaval) => Cell(1.5),

        (DamageType.Energy, ArmorClass.LightNaval) => Fix64.One,
        (DamageType.Energy, ArmorClass.HeavyNaval) => Fix64.One,
        (DamageType.Energy, ArmorClass.Submerged) => Fix64.One,

        // land types vs naval and similar cross-domain pairs appear in no canon cell;
        // cross-domain engagement is a targeting fact (post-slice), stay flat meanwhile.
        _ => Fix64.One,
    };

    private static Fix64 Cell(double canonDecimal) => Fix64.FromDouble(canonDecimal);

    /// <summary>Parses content-schema strings (kebab-case). Throws on unknown — content errors must be loud at load.</summary>
    public static DamageType ParseDamageType(string s) => s switch
    {
        "small-arms" => DamageType.SmallArms,
        "autocannon" => DamageType.Autocannon,
        "ap" => DamageType.Ap,
        "explosive" => DamageType.Explosive,
        "missile" => DamageType.Missile,
        "energy" => DamageType.Energy,
        "naval-gun" => DamageType.NavalGun,
        "torpedo" => DamageType.Torpedo,
        _ => throw new ArgumentException($"unknown damageType '{s}'"),
    };

    public static ArmorClass ParseArmor(string s) => s switch
    {
        "infantry" => ArmorClass.Infantry,
        "light" => ArmorClass.Light,
        "heavy" => ArmorClass.Heavy,
        "air" => ArmorClass.Air,
        "structure" => ArmorClass.Structure,
        "light-naval" => ArmorClass.LightNaval,
        "heavy-naval" => ArmorClass.HeavyNaval,
        "submerged" => ArmorClass.Submerged,
        _ => throw new ArgumentException($"unknown armor '{s}'"),
    };
}
