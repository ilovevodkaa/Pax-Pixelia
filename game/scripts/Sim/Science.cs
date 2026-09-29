using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

/// <summary>A nation's science per rules cycle, split by source (the top-bar tooltip shows the same parts the rules add up).</summary>
public readonly record struct ScienceParts(int Sages, int Lands, int Shrines, int CatchUp)
{
    public int Total => Sages + Lands + Shrines + CatchUp;
}

/// <summary>
/// Placeholder research until the tech tree (IDEAS A-2): every cycle a nation adds these points to its progress stock,
/// and the stock decides its era (<see cref="Eras"/>). Sages give the base, land and shrines add a little, and a nation
/// behind the leading era learns a quarter faster from its neighbours (the leader gets glory, the laggard speed).
/// A typical leader makes ≈ 8 points a cycle — the rate the era costs are tuned for.
/// </summary>
public static class Science
{
    public const int Sages = 4, ProvincesPerPoint = 5, LandsMax = 4, ShrinesPerPoint = 2, ShrinesMax = 2, CatchUpPermille = 250;

    public static ScienceParts Of(int provinces, int shrines, bool behindLeader, bool nomad = false)
    {
        if (provinces <= 0 && !nomad) return default;   // a tribe still has its sages (the shamans)
        int lands = System.Math.Min(LandsMax, provinces / ProvincesPerPoint);
        int temples = System.Math.Min(ShrinesMax, shrines / ShrinesPerPoint);
        int sum = Sages + lands + temples;
        return new ScienceParts(Sages, lands, temples, behindLeader ? sum * CatchUpPermille / 1000 : 0);
    }

    /// <summary>The parts for nation n right now (counts its land; used by tooltips, the rules count in their own pass).</summary>
    public static ScienceParts Of(GameState s, int n)
    {
        int provinces = 0, shrines = 0;
        for (int p = 0; p < s.Owner.Length; p++)
        {
            if (s.Owner[p] != n) continue;
            provinces++;
            foreach (var b in s.Buildings[p]) if (b == Bld.Shrine) shrines++;
        }
        return Of(provinces, shrines, s.Nat[n].Era < LeaderEra(s), Nomads.IsNomad(s.Nat[n]));
    }

    public static int LeaderEra(GameState s)
    {
        int e = 0;
        foreach (var n in s.Nat) if (n.Era > e) e = n.Era;
        return e;
    }

    /// <summary>The nation with the most progress (lowest index on ties): the calendar follows it.</summary>
    public static int Leader(GameState s)
    {
        int best = 0;
        for (int n = 1; n < s.Nat.Length; n++) if (s.Nat[n].Progress > s.Nat[best].Progress) best = n;
        return best;
    }
}
