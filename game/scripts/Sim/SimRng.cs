using System;
using PaxPixelia.Core;

namespace PaxPixelia.Sim;

/// <summary>
/// Stateless deterministic randomness for the simulation: every roll is a hash of (world seed, year, salt, …),
/// so lockstep clients agree without sharing a generator. Never use System.Random in Sim.
/// </summary>
public static class SimRng
{
    /// <summary>Uniform [0,1) for (a, b) under a salted seed.</summary>
    public static double U(int seed, int salt, int a, int b) => Noise.H2(a, b, unchecked(seed * 31 + salt * 7919));

    /// <summary>Index in [0, count).</summary>
    public static int Pick(int seed, int salt, int a, int b, int count) => count <= 0 ? -1 : Math.Min(count - 1, (int)(U(seed, salt, a, b) * count));
}
