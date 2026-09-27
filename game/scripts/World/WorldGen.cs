using System;
using System.Runtime.CompilerServices;

namespace PaxPixelia.World;

/// <summary>
/// World generation (pure C#, no Godot types). Deterministic: the same seed and size give bit-identical output on
/// any machine and thread count — multiplayer clients regenerate the world from the seed. It is a faithful port of
/// the approved mockup generator (docs/mockups/js/worldgen.js) and reproduces its output exactly.
/// Stages live in WorldBuilder.*.cs.
/// </summary>
public static class WorldGen
{
    /// <summary>Worker threads for the per-pixel passes (-1 = all cores). The output never depends on it.</summary>
    public static int MaxThreads = -1;

    /// <param name="w">World width in pixels; must be a multiple of 64 (province seed grid).</param>
    public static WorldData Generate(int seed, int w, int h, Action<string> progress = null)
    {
        if (w % 64 != 0 || w <= 0 || h <= 0) throw new ArgumentException($"world size {w}x{h}: width must be a positive multiple of 64");
        return new WorldBuilder(seed, w, h, MaxThreads).Build(progress);
    }

    /// <summary>Counting-sort index of pixels per province (PixOffset/PixList); each province's pixels stay in row-major order.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void BuildPixelIndex(WorldData d)
    {
        var off = new int[d.P + 1];
        var prov = d.Prov;
        for (int i = 0; i < d.N; i++) off[prov[i] + 1]++;
        for (int p = 0; p < d.P; p++) off[p + 1] += off[p];
        var fill = (int[])off.Clone();
        var list = new int[d.N];
        for (int i = 0; i < d.N; i++) list[fill[prov[i]]++] = i;
        d.PixOffset = off; d.PixList = list;
    }
}
