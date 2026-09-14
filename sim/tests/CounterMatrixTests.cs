using Rts.Sim.Combat;
using Rts.Sim.Core;
using Rts.Sim.Orders;
using Rts.Sim.Units;
using Rts.Sim.World;
using Xunit;

namespace Rts.Tests;

/// <summary>Counter-matrix behaviour: cells from docs/factions/counter-matrix.md,
/// semantics (chip floor, truncation bound) verified first in tools/refmodel_counter_matrix.py (D-001).</summary>
public class CounterMatrixTests
{
    [Theory]
    // every live cell of the canon table, spot-checked at the Q31.32 encoding bound
    [InlineData(DamageType.SmallArms, ArmorClass.Infantry, 1.5)]
    [InlineData(DamageType.SmallArms, ArmorClass.Light, 0.75)]
    [InlineData(DamageType.SmallArms, ArmorClass.Heavy, 0.35)]
    [InlineData(DamageType.SmallArms, ArmorClass.Structure, 0.5)]
    [InlineData(DamageType.Autocannon, ArmorClass.Infantry, 1.0)]
    [InlineData(DamageType.Autocannon, ArmorClass.Light, 1.5)]
    [InlineData(DamageType.Autocannon, ArmorClass.Heavy, 0.5)]
    [InlineData(DamageType.Autocannon, ArmorClass.Air, 1.25)]
    [InlineData(DamageType.Autocannon, ArmorClass.Structure, 0.75)]
    [InlineData(DamageType.Ap, ArmorClass.Infantry, 0.5)]
    [InlineData(DamageType.Ap, ArmorClass.Light, 1.25)]
    [InlineData(DamageType.Ap, ArmorClass.Heavy, 1.25)]
    [InlineData(DamageType.Ap, ArmorClass.Structure, 0.75)]
    [InlineData(DamageType.Explosive, ArmorClass.Infantry, 1.5)]
    [InlineData(DamageType.Explosive, ArmorClass.Light, 1.0)]
    [InlineData(DamageType.Explosive, ArmorClass.Heavy, 0.6)]
    [InlineData(DamageType.Explosive, ArmorClass.Structure, 1.5)]
    [InlineData(DamageType.Missile, ArmorClass.Infantry, 0.6)]
    [InlineData(DamageType.Missile, ArmorClass.Light, 1.0)]
    [InlineData(DamageType.Missile, ArmorClass.Heavy, 1.5)]
    [InlineData(DamageType.Missile, ArmorClass.Air, 1.75)]
    [InlineData(DamageType.Missile, ArmorClass.Structure, 1.0)]
    [InlineData(DamageType.Energy, ArmorClass.Infantry, 1.0)]
    [InlineData(DamageType.Energy, ArmorClass.Light, 1.0)]
    [InlineData(DamageType.Energy, ArmorClass.Heavy, 1.0)]
    [InlineData(DamageType.Energy, ArmorClass.Air, 1.0)]
    [InlineData(DamageType.Energy, ArmorClass.Structure, 0.75)]
    public void CellMatchesCanon(DamageType dt, ArmorClass ac, double canon)
        => Assert.True(Fix64.Abs(DamageMatrix.Get(dt, ac) - Fix64.FromDouble(canon)).Raw <= 1,
            $"{dt}x{ac} deviates > 1 ulp from canon {canon}");

    [Fact]
    public void NoCellBelowCanonFloor()
    {
        var floor = Fix64.FromDouble(0.35);
        foreach (DamageType dt in Enum.GetValues<DamageType>())
            foreach (ArmorClass ac in Enum.GetValues<ArmorClass>())
                Assert.True(DamageMatrix.Get(dt, ac).Raw >= floor.Raw - 1);
    }

    private static (SimWorld w, UnitStore u, Dictionary<EntityId, MoveOrder> o) World()
    {
        var w = new SimWorld(1);
        var u = new UnitStore();
        var o = new Dictionary<EntityId, MoveOrder>();
        var map = new Rts.Sim.World.GameMap(24, 24);
        w.Systems.Add((_, due) => OrderSystem.ApplyCommands(o, due, u.Find));
        w.Systems.Add((_, due) => Rts.Sim.Navigation.NavigationSystem.Step(map, u, o));
        w.Systems.Add((_, due) => CombatSystem.Step(u, o, w.Tick));
        w.Systems.Add((_, due) => u.DespawnDead());
        return (w, u, o);
    }

    // in formation range on an empty grid (radius 0.3): shots land the order tick
    private void FightOneTick(SimWorld w, UnitStore u, Unit a, Unit b)
        => w.Step(new Command[] { new AttackCommand(w.Tick, 0, new[] { a.Id }, b.Id, false) });

    [Fact]
    public void SmallArmsChipsHeavyAtMatrixRate()
    {
        var (w, units, _) = World();
        var a = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(2), Fix64.FromInt(2)), Fix64.One, Fix64.Ratio(3, 10),
            new UnitProfile(0, Fix64.FromInt(100), Fix64.FromInt(8), Fix64.FromInt(8), Fix64.FromInt(4), 12, ArmorClass.Infantry, DamageType.SmallArms));
        var b = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(4), Fix64.FromInt(2)), Fix64.One, Fix64.Ratio(3, 10),
            new UnitProfile(1, Fix64.FromInt(100), Fix64.FromInt(8), Fix64.Zero, Fix64.Zero, 0, ArmorClass.Heavy, DamageType.SmallArms));

        FightOneTick(w, units, a, b);
        // 8 x 0.35 = 2.8 (Q31.32 truncation: 2.800000000745058, ref model worked example)
        Assert.True(Math.Abs(b.Hp.ToDouble() - 97.2) < 1e-6, $"hp was {b.Hp.ToDouble()}");
    }

    [Fact]
    public void MissileSpikeKillsFasterThanMirror()
    {
        var (w, units, _) = World();
        var at = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(2), Fix64.FromInt(2)), Fix64.One, Fix64.Ratio(3, 10),
            new UnitProfile(0, Fix64.FromInt(100), Fix64.FromInt(8), Fix64.FromInt(8), Fix64.FromInt(4), 1, ArmorClass.Infantry, DamageType.Missile));
        var mbt = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(3), Fix64.FromInt(2)), Fix64.One, Fix64.Ratio(3, 10),
            new UnitProfile(1, Fix64.FromInt(100), Fix64.FromInt(8), Fix64.FromInt(8), Fix64.FromInt(4), 1, ArmorClass.Heavy, DamageType.Ap));

        FightOneTick(w, units, at, mbt); // AT fires first (lower id)
        Assert.True(Math.Abs(mbt.Hp.ToDouble() - 88.0) < 1e-6);  // 8 x 1.5 = 12
        FightOneTick(w, units, mbt, at); // mirror answer: 8 x 0.5 = 4
        Assert.True(Math.Abs(at.Hp.ToDouble() - 96.0) < 1e-6);
    }

    [Fact]
    public void ChipFloorNeverBounces()
    {
        var (w, units, _) = World();
        var a = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(2), Fix64.FromInt(2)), Fix64.One, Fix64.Ratio(3, 10),
            new UnitProfile(0, Fix64.FromInt(100), Fix64.FromInt(8), Fix64.FromInt(2), Fix64.FromInt(4), 1, ArmorClass.Infantry, DamageType.SmallArms));
        var b = units.Spawn(w.Ids.Next(), new FixVec2(Fix64.FromInt(4), Fix64.FromInt(2)), Fix64.One, Fix64.Ratio(3, 10),
            new UnitProfile(1, Fix64.FromInt(100), Fix64.FromInt(8), Fix64.Zero, Fix64.Zero, 0, ArmorClass.Heavy, DamageType.SmallArms));

        FightOneTick(w, units, a, b); // 2 x 0.35 = 0.7 -> floors at 1
        Assert.Equal(99.0, b.Hp.ToDouble());
    }

    [Fact]
    public void CatalogLoadsArmorAndDamageType()
    {
        // walk up from the test binary to the repo checkout (same convention as CI scenario runs)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "content", "units"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var catalog = UnitCatalog.LoadFromDirectory(Path.Combine(dir!.FullName, "content", "units"));
        var rifle = catalog.Get("rifleman");
        Assert.Equal(ArmorClass.Infantry, rifle.Armor);
        Assert.Equal(DamageType.SmallArms, rifle.DamageType);
        var at = catalog.Get("at-squad");
        Assert.Equal(ArmorClass.Infantry, at.Armor);
        Assert.Equal(DamageType.Missile, at.DamageType);
    }
}
