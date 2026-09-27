using PaxPixelia.Core;

namespace PaxPixelia.Sim;

/// <summary>
/// Stateless deterministic randomness for the simulation: every roll is an integer hash of (world seed, salt, a, b),
/// so lockstep clients agree without sharing a generator. Never use System.Random in Sim.
/// </summary>
public static class SimRng
{
    /// <summary>Uniform 32-bit value for (a, b) under a salted seed (the same hash as Noise.H2).</summary>
    public static uint Hash(int seed, int salt, int a, int b) => Noise.Hash(a, b, unchecked(seed * 31 + salt * 7919));

    /// <summary>Uniform 0..999.</summary>
    public static int Permille(int seed, int salt, int a, int b) => (int)((ulong)Hash(seed, salt, a, b) * 1000 >> 32);

    /// <summary>Index in [0, count), or -1 for an empty range.</summary>
    public static int Pick(int seed, int salt, int a, int b, int count) =>
        count <= 0 ? -1 : (int)((ulong)Hash(seed, salt, a, b) * (uint)count >> 32);

    /// <summary>True with probability num/den (exact: no rounding through a fraction).</summary>
    public static bool Chance(int seed, int salt, int a, int b, int num, int den) =>
        den > 0 && (long)((ulong)Hash(seed, salt, a, b) * (uint)den >> 32) < num;
}
