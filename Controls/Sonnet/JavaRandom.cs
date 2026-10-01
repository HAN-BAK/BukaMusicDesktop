namespace BukaMusicDesktop.Controls;

/// <summary>
/// java.util.Random, ported exactly. The Android stage seeds its motion
/// graphics from the shot index, so the desktop has to use the same generator
/// to land on the same particle layout and glitch bands.
/// </summary>
public sealed class JavaRandom
{
    private const long Multiplier = 0x5DEECE66DL;
    private const long Addend = 0xBL;
    private const long Mask = (1L << 48) - 1;

    private long _seed;

    public JavaRandom(long seed) => _seed = (seed ^ Multiplier) & Mask;

    private int Next(int bits)
    {
        _seed = (_seed * Multiplier + Addend) & Mask;
        return (int)((ulong)_seed >> (48 - bits));
    }

    public int NextInt() => Next(32);

    public int NextInt(int bound)
    {
        if (bound <= 0) return 0;
        if ((bound & -bound) == bound) return (int)((bound * (long)Next(31)) >> 31);
        int bits, val;
        do
        {
            bits = Next(31);
            val = bits % bound;
        } while (bits - val + (bound - 1) < 0);
        return val;
    }

    public float NextFloat() => Next(24) / (float)(1 << 24);
}
