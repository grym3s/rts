namespace Rts.Sim.Core;

/// <summary>Seeded xorshift64* — the only source of randomness in the sim. Same seed + same commands = same game.</summary>
public sealed class Rng
{
    private ulong _state;

    public Rng(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    public ulong NextU64()
    {
        _state ^= _state >> 12;
        _state ^= _state << 25;
        _state ^= _state >> 27;
        return _state * 0x2545F4914F6CDD1DUL;
    }

    /// <summary>Uniform int in [0, maxExclusive).</summary>
    public int NextInt(int maxExclusive) => (int)(NextU64() % (ulong)maxExclusive);

    /// <summary>Uniform Fix64 in [0, 1).</summary>
    public Fix64 NextFix() => Fix64.FromRaw((long)(NextU64() >> (64 - Fix64.FractionBits)));
}
