using System;
using System.Collections.Generic;
using PaxPixelia.Core;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

public enum ClaimError { None, NotLand, Owned, NotAdjacent, NoGold, Unexplored }
public enum BuildError { None, NotOwned, NoSlot, NotAllowed, AlreadyBuilt, NoGold }
public enum SurveyError { None, NotOwned, AlreadyDone, NoGold }

/// <summary>
/// Player actions as pure rules (validation + effects) and economy formulas. The Game partial (GameActions.cs)
/// wraps them with toasts and events; the console tests call them directly. `n` is the acting nation.
/// </summary>
public static class Rules
{
    public const int ClaimCost = 120, SurveyCost = 30;
    /// <summary>Gold per year a building costs to maintain («Содержание» in the top-bar tooltip).</summary>
    public const double UpkeepPerBuilding = 0.2;
    /// <summary>Population multiplier when tribes join a state (settled life, census).</summary>
    public const float ClaimPopBoost = 2.2f;

    static readonly int[] Cost = { 60, 50, 70, 60, 40, 80, 90, 50 }; // by Bld
    public static int BuildCost(Bld b) => Cost[(int)b];

    // ---------------------------------------------------------------- claim

    public static ClaimError CheckClaim(WorldData w, GameState s, int p, int n = GameState.LocalPlayer)
    {
        if (p < 0 || p >= w.P || w.PLand[p] != 1) return ClaimError.NotLand;
        if (s.Owner[p] >= 0) return ClaimError.Owned;
        if (n == GameState.LocalPlayer && !s.Explored[p]) return ClaimError.Unexplored;   // never FogEnabled: observer mode is a local view
        if (!Borders(w, s, p, n)) return ClaimError.NotAdjacent;
        if (n == GameState.LocalPlayer && s.Gold < ClaimCost) return ClaimError.NoGold;
        return ClaimError.None;
    }

    public static bool Borders(WorldData w, GameState s, int p, int n)
    {
        foreach (int q in w.Adj[p]) if (s.Owner[q] == n) return true;
        return false;
    }

    /// <summary>Tribes of p join nation n. Caller validated with CheckClaim. Gold is charged for the local player only.</summary>
    public static void Claim(WorldData w, GameState s, int p, int n = GameState.LocalPlayer)
    {
        if (n == GameState.LocalPlayer) s.Gold -= ClaimCost;
        s.Owner[p] = (short)n;
        s.Religion[p] = (sbyte)Data.Nations[n].Religion;
        s.Pop[p] *= ClaimPopBoost;
        s.Mood[p] = (byte)Math.Min((int)s.Mood[p], 58);   // new subjects are wary at first
    }

    // ---------------------------------------------------------------- buildings

    /// <summary>What the terrain allows in p (port of the mockup's bOpts, plus a shrine anywhere).</summary>
    public static void TerrainOptions(WorldData w, int p, List<Bld> into)
    {
        int b = w.PBiome[p]; float h = w.PH[p];
        if ((b is 9 or 10 or 11 or 13 or 6) && h <= .6f) into.Add(Bld.Farm);
        if (b is 5 or 8 or 12) into.Add(Bld.Lumber);
        if (h > .34f) into.Add(Bld.Quarry);
        if (w.PCoast[p] == 1) into.Add(Bld.Fishery);
        if (b is 4 or 6 or 11 or 13) into.Add(Bld.Pasture);
        into.Add(Bld.Granary); into.Add(Bld.Market); into.Add(Bld.Shrine);
    }

    /// <summary>Buildings the local player can start in p now: owned, a free slot, allowed by terrain, not built yet.</summary>
    public static List<Bld> BuildOptions(WorldData w, GameState s, int p)
    {
        var list = new List<Bld>(8);
        if (p < 0 || p >= w.P || s.Owner[p] != GameState.LocalPlayer || s.Buildings[p].Count >= s.Slots[p]) return list;
        TerrainOptions(w, p, list);
        list.RemoveAll(s.Buildings[p].Contains);
        return list;
    }

    public static BuildError CheckBuild(WorldData w, GameState s, int p, Bld b)
    {
        if (p < 0 || p >= w.P || s.Owner[p] != GameState.LocalPlayer) return BuildError.NotOwned;
        if (s.Buildings[p].Count >= s.Slots[p]) return BuildError.NoSlot;
        if (s.Buildings[p].Contains(b)) return BuildError.AlreadyBuilt;
        var opts = new List<Bld>(8); TerrainOptions(w, p, opts);
        if (!opts.Contains(b)) return BuildError.NotAllowed;
        if (s.Gold < BuildCost(b)) return BuildError.NoGold;
        return BuildError.None;
    }

    public static void Build(GameState s, int p, Bld b)
    {
        s.Gold -= BuildCost(b);
        s.Buildings[p].Add(b);
    }

    // ---------------------------------------------------------------- geology

    public static SurveyError CheckSurvey(GameState s, int p)
    {
        if (p < 0 || p >= s.Owner.Length || s.Owner[p] != GameState.LocalPlayer) return SurveyError.NotOwned;
        if (s.OreFound[p]) return SurveyError.AlreadyDone;
        if (s.Gold < SurveyCost) return SurveyError.NoGold;
        return SurveyError.None;
    }

    /// <summary>Geologists survey p. OreFound means «surveyed»: with Ore = -1 the result was «nothing here».</summary>
    public static void Survey(GameState s, int p)
    {
        s.Gold -= SurveyCost;
        s.OreFound[p] = true;
    }

    /// <summary>Hills and mountains may hold ore; flat land rarely does (the panel shows «залежей не ожидается»).</summary>
    public static bool MayHaveOre(WorldData w, GameState s, int p) => s.Ore[p] >= 0 || w.PH[p] > .34f;

    // ---------------------------------------------------------------- economy

    /// <summary>Taxes a province pays per year: population × mood factor (mood 70 → ×1), markets and the capital add a flat sum.</summary>
    public static double ProvinceTax(GameState s, int p)
    {
        double t = s.Pop[p] / 7000.0 * (0.65 + s.Mood[p] / 200.0);
        foreach (var b in s.Buildings[p]) if (b == Bld.Market) t += 0.5;
        if (s.CapitalOf[p] >= 0) t += 2;
        return t;
    }

    public static double ProvinceUpkeep(GameState s, int p) => s.Buildings[p].Count * UpkeepPerBuilding;

    /// <summary>Taxes and upkeep of nation n per year.</summary>
    public static (double taxes, double upkeep) Budget(GameState s, int n = GameState.LocalPlayer)
    {
        double t = 0, u = 0;
        for (int p = 0; p < s.Owner.Length; p++)
            if (s.Owner[p] == n) { t += ProvinceTax(s, p); u += ProvinceUpkeep(s, p); }
        return (t, u);
    }

    // ---------------------------------------------------------------- leaderboard

    /// <summary>score = population / 800 + provinces × 9 (mockup formula).</summary>
    public static int[] Scores(GameState s)
    {
        int nN = s.NationCapital.Length;
        var pop = new double[nN]; var cnt = new int[nN];
        for (int p = 0; p < s.Owner.Length; p++) { int o = s.Owner[p]; if (o >= 0 && o < nN) { pop[o] += s.Pop[p]; cnt[o]++; } }
        var sc = new int[nN];
        for (int n = 0; n < nN; n++) sc[n] = (int)Math.Round(pop[n] / 800 + cnt[n] * 9);
        return sc;
    }

    public static bool Met(GameState s, int n) => !s.FogEnabled || n == GameState.LocalPlayer || (s.Met != null && n < s.Met.Length && s.Met[n]);

    /// <summary>Met nations sorted by score (desc), ties by nation index. The local player is always listed.</summary>
    public static List<(int nation, int score)> Leaderboard(GameState s)
    {
        var sc = Scores(s);
        var rows = new List<(int nation, int score)>(sc.Length);
        for (int n = 0; n < sc.Length; n++) if (Met(s, n)) rows.Add((n, sc[n]));
        rows.Sort((a, b) => a.score != b.score ? b.score.CompareTo(a.score) : a.nation.CompareTo(b.nation));
        return rows;
    }

    public static int UnmetCount(GameState s)
    {
        int k = 0;
        for (int n = 0; n < s.NationCapital.Length; n++) if (!Met(s, n)) k++;
        return k;
    }
}
