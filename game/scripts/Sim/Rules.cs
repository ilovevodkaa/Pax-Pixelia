using System;
using System.Collections.Generic;
using PaxPixelia.Core;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

public enum ClaimError { None, NotLand, Owned, NotAdjacent, NoGold, Unexplored, CityFull }
public enum BuildError { None, NotOwned, NoSlot, NotAllowed, AlreadyBuilt, NoGold, NoMaterials, NeedTech }
public enum SurveyError { None, NotOwned, AlreadyDone, NoGold, NeedTech }

/// <summary>
/// Player actions as pure rules (validation + effects) and the economy formulas, in integers: gold in hundredths,
/// people as whole persons, shares in ‰. `n` is the acting nation — every nation plays by the same rules.
/// Commands.Apply is the only caller that changes state; the UI asks the Check* functions for instant feedback.
/// </summary>
public static class Rules
{
    /// <summary>Costs in whole gold (as the UI shows them); the treasury counts hundredths.</summary>
    public const int ClaimCost = 120, SurveyCost = 30;
    public const long Cents = 100;
    /// <summary>Upkeep of one building per rules cycle, in hundredths («Содержание» in the top-bar tooltip).</summary>
    public const long UpkeepPerBuilding = 20;
    /// <summary>Population multiplier when tribes join a state (settled life, census), ‰.</summary>
    public const int ClaimPopBoostPermille = 2200;

    static readonly int[] Cost = { 60, 50, 70, 60, 40, 80, 90, 50 }; // by Bld
    public static int BuildCost(Bld b) => Cost[(int)b];

    /// <summary>Materials (wood and stone) a building takes, by Bld: the lumber mill and the quarry cost none, so a
    /// nation that ran out can always dig itself out.</summary>
    static readonly int[] MaterialCost = { 10, 0, 0, 10, 5, 30, 25, 20 };
    public static int BuildMaterials(Bld b) => MaterialCost[(int)b];

    /// <summary>What b takes from nation n's store, after its technologies (Каменное строительство).</summary>
    public static int BuildMaterials(Bld b, NationState nat) => MaterialCost[(int)b] * (100 - Math.Min(90, Techs.Sum(nat, TechFx.MaterialDiscount))) / 100;

    // ---------------------------------------------------------------- materials and ore

    /// <summary>Materials every nation starts with, and what sources yield per rules cycle (whole units).</summary>
    public const int StartMaterials = 40, CapitalMaterials = 1, LumberMaterials = 2, QuarryMaterials = 2, MineMaterials = 2;

    /// <summary>Data.Ores indices: copper, tin and iron are dug by a quarry (a mine); gold fills the treasury; salt cheers people.</summary>
    public const int OreCopper = 0, OreTin = 1, OreIron = 2, OreGold = 3, OreSalt = 4;
    /// <summary>A gold vein adds this to the province's taxes per cycle (hundredths); salt raises the mood target.</summary>
    public const long GoldVeinTax = 100;
    public const int SaltMood = 4;

    /// <summary>Surveyed ore of p (Data.Ores index), or -1 when unknown or none: undiscovered ore gives nothing.</summary>
    public static int KnownOre(GameState s, int p) => s.OreFound[p] ? s.Ore[p] : -1;

    /// <summary>Does p's quarry dig a surveyed metal vein (copper, tin, iron)?</summary>
    public static bool IsMine(GameState s, int p) => KnownOre(s, p) is OreCopper or OreTin or OreIron && s.Buildings[p].Contains(Bld.Quarry);

    /// <summary>Materials p yields per cycle: lumber mill, quarry, a mine on a metal vein, and the capital's workshops.</summary>
    public static int ProvinceMaterials(GameState s, int p)
    {
        int m = s.CapitalOf[p] >= 0 ? CapitalMaterials : 0;
        foreach (var b in s.Buildings[p])
            m += b switch { Bld.Lumber => LumberMaterials, Bld.Quarry => QuarryMaterials, _ => 0 };
        if (IsMine(s, p)) m += MineMaterials + (s.Owner[p] >= 0 ? Techs.Sum(s.Nat[s.Owner[p]], TechFx.MineMaterials) : 0);
        if (s.Owner[p] >= 0)
        {
            m += Nomads.MythMaterials(s, s.Owner[p], p);
            if (s.Buildings[p].Contains(Bld.Quarry)) m += Techs.Sum(s.Nat[s.Owner[p]], TechFx.QuarryMaterials);
        }
        return m;
    }

    // ---------------------------------------------------------------- claim

    public static ClaimError CheckClaim(WorldData w, GameState s, int p, int n)
    {
        if (p < 0 || p >= w.P || w.PLand[p] != 1) return ClaimError.NotLand;
        if (s.Owner[p] >= 0) return ClaimError.Owned;
        if (s.Nat[n].Fog is { } f && !f.Explored[p]) return ClaimError.Unexplored;   // fog, never FogEnabled: observer mode is a view
        if (!Borders(w, s, p, n)) return ClaimError.NotAdjacent;
        if (s.City != null && Cities.Absorber(w, s, p, n) < 0) return ClaimError.CityFull;   // gold speeds growth, never beats the limit
        if (s.Nat[n].Treasury < ClaimCost * Cents) return ClaimError.NoGold;
        return ClaimError.None;
    }

    public static bool Borders(WorldData w, GameState s, int p, int n)
    {
        foreach (int q in w.Adj[p]) if (s.Owner[q] == n) return true;
        return false;
    }

    /// <summary>Tribes of p join nation n, which pays for it. Caller validated with CheckClaim.</summary>
    public static void Claim(WorldData w, GameState s, int p, int n)
    {
        s.Nat[n].Treasury -= ClaimCost * Cents;
        if (s.City != null) s.City[p] = Cities.Absorber(w, s, p, n);
        s.Owner[p] = s.Controller[p] = (short)n;
        s.Religion[p] = (sbyte)s.Nations[n].Religion;
        s.Pop[p] = (int)Math.Min(int.MaxValue, (long)s.Pop[p] * ClaimPopBoostPermille / 1000);
        s.Mood[p] = Math.Min(s.Mood[p], (byte)58);   // new subjects are wary at first
    }

    // ---------------------------------------------------------------- buildings

    /// <summary>The order the build menu lists what the terrain allows (the mockup's bOpts, plus a shrine anywhere).</summary>
    static readonly Bld[] MenuOrder = { Bld.Farm, Bld.Lumber, Bld.Quarry, Bld.Fishery, Bld.Pasture, Bld.Granary, Bld.Market, Bld.Shrine };

    /// <summary>What the terrain allows in p.</summary>
    public static void TerrainOptions(WorldData w, int p, List<Bld> into)
    {
        var f = WorldFacts.Of(w);
        foreach (var b in MenuOrder) if (f.Allows(p, b)) into.Add(b);
    }

    /// <summary>Buildings nation n can start in p now: owned, a free slot, allowed by terrain and knowledge, not built yet.</summary>
    public static List<Bld> BuildOptions(WorldData w, GameState s, int p, int n)
    {
        var list = new List<Bld>(8);
        if (p < 0 || p >= w.P || s.Owner[p] != n || s.Buildings[p].Count >= s.Slots[p]) return list;
        TerrainOptions(w, p, list);
        list.RemoveAll(b => s.Buildings[p].Contains(b) || !Techs.Allows(s.Nat[n], b));
        return list;
    }

    /// <summary>What the terrain of p would allow but nation n does not know yet (the build menu shows them locked).</summary>
    public static List<Bld> LockedOptions(WorldData w, GameState s, int p, int n)
    {
        var list = new List<Bld>(8);
        if (p < 0 || p >= w.P || s.Owner[p] != n || s.Buildings[p].Count >= s.Slots[p]) return list;
        TerrainOptions(w, p, list);
        list.RemoveAll(b => s.Buildings[p].Contains(b) || Techs.Allows(s.Nat[n], b));
        return list;
    }

    public static BuildError CheckBuild(WorldData w, GameState s, int p, Bld b, int n)
    {
        if (p < 0 || p >= w.P || s.Owner[p] != n) return BuildError.NotOwned;
        if ((uint)b >= (uint)Cost.Length) return BuildError.NotAllowed;
        if (s.Buildings[p].Count >= s.Slots[p]) return BuildError.NoSlot;
        if (s.Buildings[p].Contains(b)) return BuildError.AlreadyBuilt;
        if (!WorldFacts.Of(w).Allows(p, b)) return BuildError.NotAllowed;
        if (!Techs.Allows(s.Nat[n], b)) return BuildError.NeedTech;
        if (s.Nat[n].Treasury < BuildCost(b) * Cents) return BuildError.NoGold;
        if (s.Nat[n].Materials < BuildMaterials(b, s.Nat[n])) return BuildError.NoMaterials;
        return BuildError.None;
    }

    public static void Build(GameState s, int p, Bld b, int n)
    {
        s.Nat[n].Treasury -= BuildCost(b) * Cents;
        s.Nat[n].Materials -= BuildMaterials(b, s.Nat[n]);
        s.Buildings[p].Add(b);
    }

    // ---------------------------------------------------------------- geology

    public static SurveyError CheckSurvey(GameState s, int p, int n)
    {
        if (p < 0 || p >= s.Owner.Length || s.Owner[p] != n) return SurveyError.NotOwned;
        if (s.OreFound[p]) return SurveyError.AlreadyDone;
        if (!Techs.Known(s.Nat[n], Techs.SurveyTech)) return SurveyError.NeedTech;
        if (s.Nat[n].Treasury < SurveyCost * Cents) return SurveyError.NoGold;
        return SurveyError.None;
    }

    /// <summary>Geologists survey p. OreFound means «surveyed»: with Ore = -1 the result was «nothing here».</summary>
    public static void Survey(GameState s, int p, int n)
    {
        s.Nat[n].Treasury -= SurveyCost * Cents;
        s.OreFound[p] = true;
    }

    /// <summary>Hills and mountains may hold ore; flat land rarely does (the panel shows «залежей не ожидается»).</summary>
    public static bool MayHaveOre(WorldData w, GameState s, int p) => s.Ore[p] >= 0 || WorldFacts.Of(w).Hills[p];

    // ---------------------------------------------------------------- economy (per rules cycle, hundredths)

    /// <summary>Taxes of p per cycle: population × mood factor (mood 70 → ×1 per 7000 people), markets and the capital add a flat sum.</summary>
    public static long ProvinceTax(GameState s, int p)
    {
        long t = (long)s.Pop[p] * (650 + 5 * s.Mood[p]) / 70_000;
        foreach (var b in s.Buildings[p]) if (b == Bld.Market) t += 50;
        if (s.CapitalOf[p] >= 0) t += 200;
        if (KnownOre(s, p) == OreGold) t += GoldVeinTax;
        if (s.Owner[p] >= 0) t += t * Techs.Sum(s.Nat[s.Owner[p]], TechFx.TaxPermille) / 1000;
        return t;
    }

    public static long ProvinceUpkeep(GameState s, int p) => s.Buildings[p].Count * UpkeepPerBuilding;

    /// <summary>Taxes and upkeep of nation n per cycle, in hundredths.</summary>
    public static (long taxes, long upkeep) Budget(GameState s, int n)
    {
        long t = 0, u = 0;
        for (int p = 0; p < s.Owner.Length; p++)
            if (s.Owner[p] == n) { t += ProvinceTax(s, p); u += ProvinceUpkeep(s, p); }
        return (t, u);
    }

    // ---------------------------------------------------------------- leaderboard

    /// <summary>score = population / 800 + provinces × 9 (mockup formula).</summary>
    public static int[] Scores(GameState s)
    {
        int nN = s.NationCapital.Length;
        var pop = new long[nN]; var cnt = new int[nN];
        for (int p = 0; p < s.Owner.Length; p++) { int o = s.Owner[p]; if (o >= 0 && o < nN) { pop[o] += s.Pop[p]; cnt[o]++; } }
        var sc = new int[nN];
        for (int n = 0; n < nN; n++) sc[n] = (int)((pop[n] + 400) / 800) + cnt[n] * 9;
        return sc;
    }

    /// <summary>Has viewer met nation n? Everyone in observer mode, always itself.</summary>
    public static bool Met(GameState s, int viewer, int n) =>
        !s.FogEnabled || n == viewer || s.Nat[viewer].Fog is not { } f || (n < f.Met.Length && f.Met[n]);

    /// <summary>Nations the viewer has met, sorted by score (desc), ties by nation index. The viewer is always listed.</summary>
    public static List<(int nation, int score)> Leaderboard(GameState s, int viewer)
    {
        var sc = Scores(s);
        var rows = new List<(int nation, int score)>(sc.Length);
        for (int n = 0; n < sc.Length; n++) if (Met(s, viewer, n)) rows.Add((n, sc[n]));
        rows.Sort((a, b) => a.score != b.score ? b.score.CompareTo(a.score) : a.nation.CompareTo(b.nation));
        return rows;
    }

    public static int UnmetCount(GameState s, int viewer)
    {
        int k = 0;
        for (int n = 0; n < s.NationCapital.Length; n++) if (!Met(s, viewer, n)) k++;
        return k;
    }
}
