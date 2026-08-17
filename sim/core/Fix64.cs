namespace Rts.Sim.Core;

/// <summary>Q32.32 fixed-point number. The only real-number type allowed inside the sim (ADR 0003).</summary>
public readonly struct Fix64 : IEquatable<Fix64>, IComparable<Fix64>
{
    public const int FractionBits = 32;
    public const long OneRaw = 1L << FractionBits;

    public readonly long Raw;

    private Fix64(long raw) => Raw = raw;

    public static readonly Fix64 Zero = new(0);
    public static readonly Fix64 One = new(OneRaw);
    public static readonly Fix64 Half = new(OneRaw >> 1);
    public static readonly Fix64 MaxValue = new(long.MaxValue);
    public static readonly Fix64 MinValue = new(long.MinValue);

    public static Fix64 FromRaw(long raw) => new(raw);
    public static Fix64 FromInt(int v) => new((long)v << FractionBits);
    /// <summary>Content/authoring only — never call with a runtime-computed float inside the sim.</summary>
    public static Fix64 FromDouble(double v) => new((long)Math.Round(v * OneRaw));
    public static Fix64 Ratio(int numerator, int denominator) => new(((long)numerator << FractionBits) / denominator);

    public int ToIntFloor() => (int)(Raw >> FractionBits);
    public double ToDouble() => (double)Raw / OneRaw;

    public static Fix64 operator +(Fix64 a, Fix64 b) => new(a.Raw + b.Raw);
    public static Fix64 operator -(Fix64 a, Fix64 b) => new(a.Raw - b.Raw);
    public static Fix64 operator -(Fix64 a) => new(-a.Raw);
    public static Fix64 operator *(Fix64 a, Fix64 b) => new((long)(((Int128)a.Raw * b.Raw) >> FractionBits));
    public static Fix64 operator /(Fix64 a, Fix64 b) => new((long)(((Int128)a.Raw << FractionBits) / b.Raw));
    public static Fix64 operator *(Fix64 a, int b) => new(a.Raw * b);
    public static Fix64 operator /(Fix64 a, int b) => new(a.Raw / b);

    public static bool operator ==(Fix64 a, Fix64 b) => a.Raw == b.Raw;
    public static bool operator !=(Fix64 a, Fix64 b) => a.Raw != b.Raw;
    public static bool operator <(Fix64 a, Fix64 b) => a.Raw < b.Raw;
    public static bool operator >(Fix64 a, Fix64 b) => a.Raw > b.Raw;
    public static bool operator <=(Fix64 a, Fix64 b) => a.Raw <= b.Raw;
    public static bool operator >=(Fix64 a, Fix64 b) => a.Raw >= b.Raw;

    public static Fix64 Abs(Fix64 a) => a.Raw < 0 ? new(-a.Raw) : a;
    public static Fix64 Min(Fix64 a, Fix64 b) => a.Raw < b.Raw ? a : b;
    public static Fix64 Max(Fix64 a, Fix64 b) => a.Raw > b.Raw ? a : b;
    public static Fix64 Clamp(Fix64 v, Fix64 lo, Fix64 hi) => Max(lo, Min(hi, v));

    /// <summary>Integer Newton square root; exact to the raw unit. Throws on negative input.</summary>
    public static Fix64 Sqrt(Fix64 a)
    {
        if (a.Raw < 0) throw new ArgumentOutOfRangeException(nameof(a), "Sqrt of negative Fix64");
        if (a.Raw == 0) return Zero;
        // sqrt(raw / 2^32) * 2^32 == sqrt(raw * 2^32)
        var n = (UInt128)(ulong)a.Raw << FractionBits;
        var x = (UInt128)1 << ((128 - (int)UInt128.LeadingZeroCount(n) + 1) / 2);
        while (true)
        {
            var y = (x + n / x) >> 1;
            if (y >= x) return new((long)(ulong)x);
            x = y;
        }
    }

    public bool Equals(Fix64 other) => Raw == other.Raw;
    public override bool Equals(object? obj) => obj is Fix64 f && Equals(f);
    public override int GetHashCode() => Raw.GetHashCode();
    public int CompareTo(Fix64 other) => Raw.CompareTo(other.Raw);
    public override string ToString() => ToDouble().ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>2D fixed-point vector. Sim positions and velocities.</summary>
public readonly record struct FixVec2(Fix64 X, Fix64 Y)
{
    public static readonly FixVec2 Zero = new(Fix64.Zero, Fix64.Zero);
    public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static FixVec2 operator *(FixVec2 a, Fix64 s) => new(a.X * s, a.Y * s);
    public Fix64 LengthSquared => X * X + Y * Y;
    public Fix64 Length => Fix64.Sqrt(LengthSquared);
}
