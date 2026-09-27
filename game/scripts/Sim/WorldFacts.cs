// pax-allow-file: the one place where the world's float fields become the integers the rules use (computed once per world)
using System;
using System.Runtime.CompilerServices;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

/// <summary>
/// Integer facts derived once from a generated world, so the rules never read its float fields: fertility in ‰,
/// the buildings the terrain allows (bit mask by Bld) and whether a province is hilly (ore and quarries).
/// The comparisons are the same float comparisons the terrain rules always made, so nothing shifts at the edges.
/// </summary>
public sealed class WorldFacts
{
    public readonly int[] FertPm;       // fertility 0..1000
    public readonly ushort[] Terrain;   // bit (int)Bld set: the terrain allows that building
    public readonly bool[] Hills;       // mean height above .34: quarries, and geologists may find ore

    static readonly ConditionalWeakTable<WorldData, WorldFacts> Cache = new();

    public static WorldFacts Of(WorldData w) => Cache.GetValue(w, x => new WorldFacts(x));

    WorldFacts(WorldData w)
    {
        int n = w.P;
        FertPm = new int[n]; Terrain = new ushort[n]; Hills = new bool[n];
        for (int p = 0; p < n; p++)
        {
            FertPm[p] = Math.Clamp((int)MathF.Round(w.PFert[p] * 1000f), 0, 1000);
            float h = w.PH[p];
            int b = w.PBiome[p];
            Hills[p] = h > .34f;
            int m = 0;
            if ((b is 9 or 10 or 11 or 13 or 6) && h <= .6f) m |= Bit(Bld.Farm);
            if (b is 5 or 8 or 12) m |= Bit(Bld.Lumber);
            if (h > .34f) m |= Bit(Bld.Quarry);
            if (w.PCoast[p] == 1) m |= Bit(Bld.Fishery);
            if (b is 4 or 6 or 11 or 13) m |= Bit(Bld.Pasture);
            m |= Bit(Bld.Granary) | Bit(Bld.Market) | Bit(Bld.Shrine);
            Terrain[p] = (ushort)m;
        }
    }

    public static int Bit(Bld b) => 1 << (int)b;
    public bool Allows(int p, Bld b) => (Terrain[p] & Bit(b)) != 0;
}
