namespace Ongenet.Core.Audio.Dsp;

/// <summary>
/// A tiny, allocation-free xorshift PRNG suitable for the audio thread (where <see cref="System.Random"/>
/// is undesirable — it allocates and isn't reproducible per voice). Seed per voice for independent streams.
/// </summary>
public struct FastRandom
{
    private uint _state;

    public FastRandom(uint seed) => _state = seed == 0 ? 0x9E3779B9u : seed;

    private uint NextBits()
    {
        // xorshift32
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    /// <summary>Next value in [0, 1).</summary>
    public float NextFloat() => (NextBits() >> 8) * (1.0f / 16777216.0f); // 24-bit mantissa

    /// <summary>Next value in [-1, 1).</summary>
    public float NextBipolar() => NextFloat() * 2.0f - 1.0f;

    /// <summary>Next integer in [0, count).</summary>
    public int NextInt(int count) => count <= 1 ? 0 : (int)(NextBits() % (uint)count);

    /// <summary>True with probability <paramref name="p"/> (0..1).</summary>
    public bool Chance(float p) => NextFloat() < p;
}
