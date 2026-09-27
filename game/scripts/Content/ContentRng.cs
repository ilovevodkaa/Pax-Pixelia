namespace PaxPixelia.Content;

/// <summary>
/// Stateless integer randomness for the deck: every roll is a hash of (game seed, nation, tick, salt), so lockstep
/// clients agree without sharing a generator and a replay reproduces every card. No floating point.
/// </summary>
public static class ContentRng
{
    public static uint Hash(int seed, int nation, long tick, int salt)
    {
        ulong x = (uint)seed;
        x = Mix(x ^ ((ulong)(uint)nation << 32 | (uint)salt));
        x = Mix(x ^ (ulong)tick);
        return (uint)(x >> 32);
    }

    /// <summary>[0, n) from a hash without modulo bias worth caring about.</summary>
    public static int Below(uint h, int n) => n <= 0 ? 0 : (int)((ulong)h * (uint)n >> 32);

    /// <summary>[lo, hi] inclusive.</summary>
    public static int Between(uint h, int lo, int hi) => hi <= lo ? lo : lo + Below(h, hi - lo + 1);

    static ulong Mix(ulong z)   // splitmix64 finaliser
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
