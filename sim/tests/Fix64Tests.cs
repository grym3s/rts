using Rts.Sim.Core;
using Xunit;

namespace Rts.Sim.Tests;

public class Fix64Tests
{
    [Fact]
    public void Arithmetic_is_exact_for_integers()
    {
        var a = Fix64.FromInt(6);
        var b = Fix64.FromInt(4);
        Assert.Equal(Fix64.FromInt(10), a + b);
        Assert.Equal(Fix64.FromInt(2), a - b);
        Assert.Equal(Fix64.FromInt(24), a * b);
        Assert.Equal(Fix64.Ratio(3, 2), a / b);
    }

    [Fact]
    public void Sqrt_of_perfect_squares_is_exact()
    {
        Assert.Equal(Fix64.FromInt(4), Fix64.Sqrt(Fix64.FromInt(16)));
        Assert.Equal(Fix64.Zero, Fix64.Sqrt(Fix64.Zero));
        Assert.Equal(Fix64.Half, Fix64.Sqrt(Fix64.Ratio(1, 4)));
    }

    [Fact]
    public void Sqrt_of_two_is_close()
    {
        var s = Fix64.Sqrt(Fix64.FromInt(2));
        Assert.InRange(s.ToDouble(), 1.41421356 - 1e-8, 1.41421356 + 1e-8);
    }

    [Fact]
    public void Vector_length_uses_fixed_math()
    {
        var v = new FixVec2(Fix64.FromInt(3), Fix64.FromInt(4));
        Assert.Equal(Fix64.FromInt(5), v.Length);
    }

    [Fact]
    public void Rng_is_deterministic_per_seed()
    {
        var a = new Rng(42);
        var b = new Rng(42);
        for (var i = 0; i < 100; i++) Assert.Equal(a.NextU64(), b.NextU64());
        Assert.NotEqual(new Rng(1).NextU64(), new Rng(2).NextU64());
    }
}
