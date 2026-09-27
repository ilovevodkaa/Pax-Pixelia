namespace PaxPixelia.Sim;

/// <summary>Integer helpers for the rules (the simulation never touches floating point).</summary>
public static class IntMath
{
    /// <summary>⌊√v⌋ for v ≥ 0 (Newton's method on integers).</summary>
    public static long Isqrt(long v)
    {
        if (v < 2) return v < 0 ? 0 : v;
        long x = v, y = (x + 1) / 2;
        while (y < x) { x = y; y = (x + v / x) / 2; }
        return x;
    }

    public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    public static long Clamp(long v, long lo, long hi) => v < lo ? lo : v > hi ? hi : v;

    /// <summary>a·b/c without overflowing the product (c &gt; 0), rounded towards zero.</summary>
    public static long MulDiv(long a, long b, long c) => (long)((System.Int128)a * b / c);
}
