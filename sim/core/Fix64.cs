using System.Numerics;

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

    // ---- Trig: CORDIC, deterministic integer math (ADR 0003). Verified worst error ~3e-9 ----

    private const long PiRaw = 0x3243F6A88L;      // π << 32 (33 bits — do not truncate)
    private const long PiOver2Raw = 0x1921FB544L; // π/2 << 32 (33 bits — do not truncate)
    private const long InvCordicGainRaw = 0x9B74EDA8L; // (1 / Π√(1+2^-2i)) << 32
    // atan(2^-i) << 32, i = 0..29
    private static readonly long[] AtanTable =
    {
        0xC90FDAA2L, 0x76B19C16L, 0x3EB6EBF2L, 0x1FD5BA9BL, 0x0FFAADDCL,
        0x07FF556FL, 0x03FFEAABL, 0x01FFFD55L, 0x00FFFFABL, 0x007FFFF5L,
        0x003FFFFFL, 0x00200000L, 0x00100000L, 0x00080000L, 0x00040000L,
        0x00020000L, 0x00010000L, 0x00008000L, 0x00004000L, 0x00002000L,
        0x00001000L, 0x00000800L, 0x00000400L, 0x00000200L, 0x00000100L,
        0x00000080L, 0x00000040L, 0x00000020L, 0x00000010L, 0x00000008L,
    };

    /// <summary>Angle in [-π, π], Q32.32. Vectoring CORDIC; worst error ~3e-9 rad.</summary>
    public static Fix64 Atan2(Fix64 y, Fix64 x)
    {
        if (x.Raw == 0 && y.Raw == 0) return Zero;
        long xa = x.Raw, ya = y.Raw, a;
        if (xa < 0)
        {
            // base angle from the ORIGINAL y (fixup π ± θ); no signed zero, so y==0 -> +π
            a = ya > 0 ? PiRaw : ya < 0 ? -PiRaw : PiRaw;
            (xa, ya) = (-xa, -ya);
        }
        else a = 0;
        // normalise to ~Q29 magnitude: first x+y cannot overflow long, and shift truncation stays below 1e-9 rad
        var m = Math.Max((ulong)Fix64.Abs(x).Raw, (ulong)Fix64.Abs(y).Raw);
        var sh = BitOperations.LeadingZeroCount(m) - 2;
        if (sh > 0) { xa <<= sh; ya <<= sh; }
        else if (sh < 0) { xa >>= -sh; ya >>= -sh; }
        for (var i = 0; i < AtanTable.Length; i++)
        {
            long nx, ny;
            if (ya >= 0) { nx = xa + (ya >> i); ny = ya - (xa >> i); a += AtanTable[i]; }
            else { nx = xa - (ya >> i); ny = ya + (xa >> i); a -= AtanTable[i]; }
            (xa, ya) = (nx, ny);
        }
        return new(a);
    }

    /// <summary>Cosine, worst error ~4e-9. Sine via Sin = Cos(π/2 − a) inherits it.</summary>
    public static Fix64 Cos(Fix64 angle)
    {
        long a = angle.Raw, sgn = 1;
        // wrap into [-π, π] before folding (angles can arrive outside ±π/2 already)
        if (a > PiRaw) a -= 2 * PiRaw; else if (a < -PiRaw) a += 2 * PiRaw;
        if (a > PiOver2Raw) { a = PiRaw - a; sgn = -1; }
        else if (a < -PiOver2Raw) { a = -PiRaw - a; sgn = -1; }
        long x = OneRaw, y = 0, z = a;
        for (var i = 0; i < AtanTable.Length; i++)
        {
            var s = z >= 0 ? 1L : -1L;
            var nx = x - s * (y >> i);
            var ny = y + s * (x >> i);
            x = nx; y = ny;
            z -= s * AtanTable[i];
        }
        // x is Q32 (loop starts at OneRaw, no normalisation), INV is (1/gain)<<32
        return FromRaw(sgn * (long)(((Int128)x * InvCordicGainRaw) >> 32));
    }

    /// <summary>Sine, worst error ~4e-9.</summary>
    public static Fix64 Sin(Fix64 angle) => Cos(new Fix64(PiOver2Raw - angle.Raw));

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
    public Fix64 Dot(FixVec2 o) => X * o.X + Y * o.Y;
    public Fix64 Cross(FixVec2 o) => X * o.Y - Y * o.X;
    /// <summary>Unit vector; Zero maps to Zero (callers guard degenerate headings).</summary>
    public FixVec2 Normalized()
    {
        var len = Length;
        return len.Raw == 0 ? Zero : this * (Fix64.One / len);
    }
    /// <summary>Heading angle in [-π, π] — the angle a unit facing +X rotates by to align with this vector.</summary>
    public Fix64 Angle() => Fix64.Atan2(Y, X);
}
