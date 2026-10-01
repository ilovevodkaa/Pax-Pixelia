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
    /// <summary>Base costs in whole gold (as the UI shows them); the treasury counts hundredths. What a nation pays grows
    /// with the era and, for land, with the size of the realm and its overextension: <see cref="ClaimPrice"/>,
    /// <see cref="SurveyPrice"/>, <see cref="BuildPrice"/>.</summary>
    public const int ClaimCost = 120, SurveyCost = 30;
    public const long Cents = 100;
    /// <summary>Upkeep of one building per rules cycle, in hundredths («Постройки» in the treasury tooltip).</summary>
    public const long UpkeepPerBuilding = 2;
    /// <summary>Population multiplier when tribes join a state (settled life, census), ‰.</summary>
    public const int ClaimPopBoostPermille = 2200;

    static readonly int[] Cost = { 60, 50, 70, 60, 40, 80, 90, 50 }; // by Bld
    /// <summary>Base gold price of b (Первобытная).</summary>
    public static int BuildCost(Bld b) => Cost[(int)b];
    /// <summary>What b costs nation nat in its era: a quarter dearer every era.</summary>
    public static int BuildPrice(Bld b, NationState nat) => (int)((long)Cost[(int)b] * Policy.EraPermille(nat.Era) / 1000 * (100 - Leader.BuildPct(nat)) / 100);

    /// <summary>Materials (wood and stone) a building takes, by Bld: the lumber mill and the quarry cost none, so a
    /// nation that ran out can always dig itself out.</summary>
    static readonly int[] MaterialCost = { 100, 0, 0, 100, 50, 300, 250, 200 };
    public static int BuildMaterials(Bld b) => MaterialCost[(int)b];

    /// <summary>What b takes from nation n's store in its era, after its technologies (Каменное строительство).</summary>
    public static int BuildMaterials(Bld b, NationState nat) =>
        (int)((long)MaterialCost[(int)b] * Policy.EraPermille(nat.Era) / 1000 * (100 - Math.Min(90, Techs.Sum(nat, TechFx.MaterialDiscount))) / 100);

    /// <summary>The gold nation n pays for b in p now: dearer while other jobs stand there (Construction.SurchargePct).</summary>
    public static int BuildPriceAt(GameState s, int p, Bld b, int n) =>
        (int)((long)BuildPrice(b, s.Nat[n]) * (100 + Construction.SurchargePct(s, p)) / 100);

    public static int BuildMaterialsAt(GameState s, int p, Bld b, int n) =>
        (int)((long)BuildMaterials(b, s.Nat[n]) * (100 + Construction.SurchargePct(s, p)) / 100);

    // ---------------------------------------------------------------- materials and ore

    /// <summary>Materials every nation starts with, and what sources yield per rules cycle (whole units).</summary>
    public const int StartMaterials = 300, CapitalMaterials = 1, LumberMaterials = 2, QuarryMaterials = 2, MineMaterials = 2;

    /// <summary>Data.Ores indices: copper, tin and iron are dug by a quarry (a mine); gold fills the treasury; salt cheers people.</summary>
    public const int OreCopper = 0, OreTin = 1, OreIron = 2, OreGold = 3, OreSalt = 4;
    /// <summary>A gold vein adds this to the province's taxes per cycle (hundredths); salt raises the mood target.</summary>
    public const long GoldVeinTax = 10;
    public const int SaltMood = 4;

    /// <summary>Surveyed ore of p (Data.Ores index), or -1 when unknown or none: undiscovered ore gives nothing.</summary>
    public static int KnownOre(GameState s, int p) => s.OreFound[p] ? s.Ore[p] : -1;

    /// <summary>Does p's quarry dig a surveyed metal vein (copper, tin, iron)?</summary>
    public static bool IsMine(GameState s, int p) => KnownOre(s, p) is OreCopper or OreTin or OreIron && s.Buildings[p].Contains(Bld.Quarry);

    /// <summary>Materials p yields per cycle: lumber mill, quarry, a mine on a metal vein, and the capital's workshops.</summary>
    public static int ProvinceMaterials(GameState s, int p)
    {
        if (!Unrest.Works(s, p)) return 0;   // on strike
        int m = s.CapitalOf[p] >= 0 ? CapitalMaterials : 0;
        foreach (var b in s.Buildings[p])
            m += b switch { Bld.Lumber => LumberMaterials - (s.Owner[p] >= 0 && Faith.Has(s.Nat[s.Owner[p]], Faith.Index("sacred_groves")) ? 1 : 0), Bld.Quarry => QuarryMaterials, _ => 0 };
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
        if (s.Nat[n].Treasury < ClaimPrice(s, n) * Cents) return ClaimError.NoGold;
        return ClaimError.None;
    }

    /// <summary>
    /// What a province costs nation n now: the base price, +2.5% for every province the realm already holds, a quarter
    /// more every era, and the overextension % on top — a sprawling empire pays more for each new acre.
    /// </summary>
    public static int ClaimPrice(GameState s, int n)
    {
        var (prov, _, over) = Policy.Admin(s, n);
        return ClaimPriceFor(prov, s.Nat[n].Era, over);
    }

    public static int ClaimPriceFor(int provinces, int era, int overPct) =>
        (int)((long)ClaimCost * (40 + provinces) / 40 * Policy.EraPermille(era) / 1000 * (100 + overPct) / 100);

    public static bool Borders(WorldData w, GameState s, int p, int n)
    {
        foreach (int q in w.Adj[p]) if (s.Owner[q] == n) return true;
        return false;
    }

    /// <summary>Tribes of p join nation n, which pays for it. Caller validated with CheckClaim.</summary>
    public static void Claim(WorldData w, GameState s, int p, int n)
    {
        s.Nat[n].Treasury -= ClaimPrice(s, n) * Cents;
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

    /// <summary>Buildings nation n can start in p now: owned, a free slot, allowed by terrain and knowledge, neither
    /// standing nor going up there.</summary>
    public static List<Bld> BuildOptions(WorldData w, GameState s, int p, int n)
    {
        var list = new List<Bld>(8);
        if (p < 0 || p >= w.P || s.Owner[p] != n || Construction.Occupied(s, p) >= s.Slots[p]) return list;
        TerrainOptions(w, p, list);
        list.RemoveAll(b => s.Buildings[p].Contains(b) || Construction.Has(s, p, b) || !Techs.Allows(s.Nat[n], b));
        return list;
    }

    /// <summary>What the terrain of p would allow but nation n does not know yet (the build menu shows them locked).</summary>
    public static List<Bld> LockedOptions(WorldData w, GameState s, int p, int n)
    {
        var list = new List<Bld>(8);
        if (p < 0 || p >= w.P || s.Owner[p] != n || Construction.Occupied(s, p) >= s.Slots[p]) return list;
        TerrainOptions(w, p, list);
        list.RemoveAll(b => s.Buildings[p].Contains(b) || Construction.Has(s, p, b) || Techs.Allows(s.Nat[n], b));
        return list;
    }

    public static BuildError CheckBuild(WorldData w, GameState s, int p, Bld b, int n)
    {
        if (p < 0 || p >= w.P || s.Owner[p] != n) return BuildError.NotOwned;
        if ((uint)b >= (uint)Cost.Length) return BuildError.NotAllowed;
        if (Construction.Occupied(s, p) >= s.Slots[p]) return BuildError.NoSlot;
        if (s.Buildings[p].Contains(b) || Construction.Has(s, p, b)) return BuildError.AlreadyBuilt;
        if (!WorldFacts.Of(w).Allows(p, b)) return BuildError.NotAllowed;
        if (!Techs.Allows(s.Nat[n], b)) return BuildError.NeedTech;
        if (s.Nat[n].Treasury < BuildPriceAt(s, p, b, n) * Cents) return BuildError.NoGold;
        if (s.Nat[n].Materials < BuildMaterialsAt(s, p, b, n)) return BuildError.NoMaterials;
        return BuildError.None;
    }

    /// <summary>Pay for b in p and lay its foundation: it stands when Construction says so.</summary>
    public static void Build(GameState s, int p, Bld b, int n)
    {
        long gold = BuildPriceAt(s, p, b, n) * Cents;
        int mats = BuildMaterialsAt(s, p, b, n);
        s.Nat[n].Treasury -= gold;
        s.Nat[n].Materials -= mats;
        Construction.Start(s, p, b, n, gold, mats);
    }

    // ---------------------------------------------------------------- geology

    public static SurveyError CheckSurvey(GameState s, int p, int n)
    {
        if (p < 0 || p >= s.Owner.Length || s.Owner[p] != n) return SurveyError.NotOwned;
        if (s.OreFound[p]) return SurveyError.AlreadyDone;
        if (!Techs.Known(s.Nat[n], Techs.SurveyTech)) return SurveyError.NeedTech;
        if (s.Nat[n].Treasury < SurveyPrice(s.Nat[n]) * Cents) return SurveyError.NoGold;
        return SurveyError.None;
    }

    /// <summary>Geologists survey p. OreFound means «surveyed»: with Ore = -1 the result was «nothing here».</summary>
    public static void Survey(GameState s, int p, int n)
    {
        s.Nat[n].Treasury -= SurveyPrice(s.Nat[n]) * Cents;
        s.OreFound[p] = true;
    }

    public static int SurveyPrice(NationState nat) => (int)((long)SurveyCost * Policy.EraPermille(nat.Era) / 1000);

    /// <summary>Hills and mountains may hold ore; flat land rarely does (the panel shows «залежей не ожидается»).</summary>
    public static bool MayHaveOre(WorldData w, GameState s, int p) => s.Ore[p] >= 0 || WorldFacts.Of(w).Hills[p];

    // ---------------------------------------------------------------- economy (per rules cycle, hundredths)

    /// <summary>Taxes of p per cycle (hundredths): population × mood factor (mood 70 → 1 gold per 70 000 people),
    /// markets and the capital add a flat sum; technologies and the levy edict add their share.</summary>
    public static long ProvinceTax(GameState s, int p)
    {
        long t = (long)s.Pop[p] * (650 + 5 * s.Mood[p]) / 700_000;
        foreach (var b in s.Buildings[p])
        {
            if (b == Bld.Market) t += MarketTax;
            else if (b == Bld.Shrine && s.Owner[p] >= 0) t += Faith.ShrineTax(s.Nat[s.Owner[p]]);   // pilgrims
        }
        if (s.CapitalOf[p] >= 0) t += CapitalTax;
        if (KnownOre(s, p) == OreGold) t += GoldVeinTax;
        if (s.Owner[p] >= 0)
        {
            var nat = s.Nat[s.Owner[p]];
            t += t * (Techs.Sum(nat, TechFx.TaxPermille) + 10 * Policy.TaxPct(nat)) / 1000;
            t = t * Unrest.TaxPermille(s, p) / 1000;   // grumbling pays less, strikes pay nothing
        }
        return t;
    }

    /// <summary>Flat taxes of a market and of the capital per cycle (hundredths).</summary>
    public const long MarketTax = 5, CapitalTax = 20;

    public static long ProvinceUpkeep(GameState s, int p) => s.Buildings[p].Count * UpkeepPerBuilding;

    /// <summary>Taxes and upkeep of nation n per cycle, in hundredths.</summary>
    public static (long taxes, long upkeep) Budget(GameState s, int n)
    {
        var b = Policy.BudgetOf(s, n);
        return (b.Taxes, b.Upkeep);
    }

    // ---------------------------------------------------------------- leaderboard

    /// <summary>score = population / 800 + provinces × 9 (mockup formula) + glory × 5 (wonders, world firsts, legacy).</summary>
    public static int[] Scores(GameState s)
    {
        int nN = s.NationCapital.Length;
        var pop = new long[nN]; var cnt = new int[nN];
        for (int p = 0; p < s.Owner.Length; p++) { int o = s.Owner[p]; if (o >= 0 && o < nN) { pop[o] += s.Pop[p]; cnt[o]++; } }
        var sc = new int[nN];
        for (int n = 0; n < nN; n++) sc[n] = (int)((pop[n] + 400) / 800) + cnt[n] * 9 + (n < s.Nat.Length ? s.Nat[n].Glory * Wonders.GloryScore : 0);
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
