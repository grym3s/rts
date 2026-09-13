using Rts.Sim.Core;
using Xunit;

namespace Rts.Sim.Tests;

/// <summary>CORDIC trig is deterministic by construction (integer ops only); these pin accuracy.</summary>
public class TrigTests
{
    private const double Tol = 1e-6;

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(0.0, -1.0)]
    [InlineData(-1.0, 0.0)]
    [InlineData(1.0, 1.0)]
    [InlineData(-1.0, 1.0)]
    [InlineData(1.0, -1.0)]
    [InlineData(-1.0, -1.0)]
    [InlineData(3.7, -2.25)]
    [InlineData(-0.5, 0.125)]
    public void Atan2_matches_double(double y, double x)
    {
        var got = Fix64.Atan2(Fix64.FromDouble(y), Fix64.FromDouble(x)).ToDouble();
        Assert.InRange(got, Math.Atan2(y, x) - Tol, Math.Atan2(y, x) + Tol);
    }

    [Fact]
    public void Atan2_of_zero_zero_is_zero() =>
        Assert.Equal(Fix64.Zero, Fix64.Atan2(Fix64.Zero, Fix64.Zero));

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(0.7853981633974483)]   // π/4
    [InlineData(1.5707963267948966)]   // π/2
    [InlineData(2.5)]
    [InlineData(3.14159265358979)]     // ~π
    [InlineData(-1.0)]
    [InlineData(-2.9)]
    public void Sin_Cos_match_double(double angle)
    {
        var a = Fix64.FromDouble(angle);
        Assert.InRange(Fix64.Cos(a).ToDouble(), Math.Cos(angle) - Tol, Math.Cos(angle) + Tol);
        Assert.InRange(Fix64.Sin(a).ToDouble(), Math.Sin(angle) - Tol, Math.Sin(angle) + Tol);
    }

    [Fact]
    public void Sin2_plus_Cos2_is_one()
    {
        for (var i = -20; i <= 20; i++)
        {
            var a = Fix64.FromDouble(i * 0.15);
            var s = Fix64.Sin(a); var c = Fix64.Cos(a);
            Assert.InRange((s * s + c * c).ToDouble(), 1.0 - 1e-5, 1.0 + 1e-5);
        }
    }
}

public class FixVec2Tests
{
    [Fact]
    public void Dot_and_Cross_are_exact_for_integers()
    {
        var a = new FixVec2(Fix64.FromInt(1), Fix64.FromInt(2));
        var b = new FixVec2(Fix64.FromInt(3), Fix64.FromInt(4));
        Assert.Equal(Fix64.FromInt(11), a.Dot(b));    // 1*3 + 2*4
        Assert.Equal(Fix64.FromInt(-2), a.Cross(b));  // 1*4 - 2*3
    }

    [Fact]
    public void Normalized_gives_unit_length_keeping_direction()
    {
        var v = new FixVec2(Fix64.FromInt(3), Fix64.FromInt(-4));
        var n = v.Normalized();
        Assert.InRange(n.Length.ToDouble(), 1.0 - 1e-8, 1.0 + 1e-8);
        Assert.True(n.Cross(v) <= Fix64.FromInt(0) && n.Cross(v) >= Fix64.FromInt(-1)); // collinear (tolerance: fraction bits)
        Assert.True(n.X > Fix64.Zero && n.Y < Fix64.Zero); // direction kept
    }

    [Fact]
    public void Normalized_of_zero_is_zero() =>
        Assert.Equal(FixVec2.Zero, FixVec2.Zero.Normalized());

    [Fact]
    public void Angle_matches_Atan2_of_components()
    {
        var v = new FixVec2(Fix64.FromInt(-2), Fix64.FromInt(5));
        Assert.InRange(v.Angle().ToDouble(), Math.Atan2(5, -2) - 1e-6, Math.Atan2(5, -2) + 1e-6);
    }
}

public class EntityIdAndEventTests
{
    [Fact]
    public void Allocator_is_monotonic_from_zero()
    {
        var w = new SimWorld(1);
        Assert.Equal(new EntityId(0), w.Ids.Next());
        Assert.Equal(new EntityId(1), w.Ids.Next());
        Assert.Equal(2, w.Ids.Count);
    }

    [Fact]
    public void Events_are_visible_after_the_step_and_flushed_by_the_next()
    {
        var w = new SimWorld(1);
        var a = w.Ids.Next();
        var b = w.Ids.Next();
        w.Emit(a, b);
        Assert.Single(w.Events);
        w.Step(Array.Empty<Command>()); // clears the previous tick's buffer at its start
        Assert.Empty(w.Events);
    }

    [Fact]
    public void StateHash_changes_with_tick_and_with_id_allocation()
    {
        var w = new SimWorld(7);
        var h0 = w.StateHash();
        w.Step(Array.Empty<Command>());
        var h1 = w.StateHash();
        Assert.NotEqual(h0, h1);
        w.Ids.Next();
        Assert.NotEqual(h1, w.StateHash());
    }

    [Fact]
    public void Same_seed_same_commands_same_hash()
    {
        var a = new SimWorld(99);
        var b = new SimWorld(99);
        for (var t = 0; t < 50; t++)
        {
            a.Step(new[] { new MoveCommand(t, 0, new[] { a.Ids.Next() }, FixVec2.Zero, false) });
            b.Step(new[] { new MoveCommand(t, 0, new[] { b.Ids.Next() }, FixVec2.Zero, false) });
        }
        Assert.Equal(a.StateHash(), b.StateHash());
    }
}
