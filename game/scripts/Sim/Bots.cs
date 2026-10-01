using System.Collections.Generic;
using PaxPixelia.Core;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

/// <summary>
/// Bots of the peaceful MVP «просто развиваются»: every few cycles each bot nation persuades one neighbouring tribe to
/// join (the same scoring as the initial borders in the mockup's genNations). Bigger states expand more slowly.
/// A bot issues the very command a player would (<see cref="Cmd.Claim"/> through <see cref="Commands.Apply"/>),
/// so it pays the same price and passes the same checks; its commands are not journaled — they follow from the state.
/// </summary>
public static class Bots
{
    /// <summary>A nation with k provinces tries once in 9 + k/3 cycles on average: chance 3 / (27 + k).</summary>
    public static bool WantsToClaim(int seed, int nation, int cycle, int provinces) => SimRng.Chance(seed, 21, nation, cycle, 3, 27 + provinces);

    internal static void Act(WorldData w, GameState s, int cycle, SimScratch tally, ISimSink sink, ref List<int> changed)
    {
        var count = tally.Provinces;
        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            if (nat.Control != NationControl.Bot || count[n] == 0) continue;
            TryClaim(w, s, n, cycle, count, sink, ref changed);
            if (WantsToBuild(w.Seed, n, cycle)) TryBuild(w, s, n, tally.Shrines[n], sink, ref changed);
            if (WantsEdicts(w.Seed, n, cycle)) ChooseEdicts(w, s, n, tally.OverPct[n], sink);
            Wonders.BotChoose(w, s, n, cycle);
            Wonders.BotInvest(w, s, n, cycle);
            Unrest.BotCare(w, s, n, cycle);
            Diplomacy.BotAct(w, s, n, cycle, sink);
        }
    }

    static void TryClaim(WorldData w, GameState s, int n, int cycle, int[] count, ISimSink sink, ref List<int> changed)
    {
        if (!WantsToClaim(w.Seed, n, cycle, count[n])) return;
        var (prov, _, over) = Policy.Admin(s, n);
        if (over >= BotOverMax) return;   // a bot does not sprawl far past what it can govern (its cities still grow)
        if (s.Nat[n].Treasury < Rules.ClaimPriceFor(prov, s.Nat[n].Era, over) * Rules.Cents) return;
        int q = BestClaim(w, s, n);
        if (q < 0) { TryFoundCity(w, s, n, sink, ref changed); return; }
        changed ??= new List<int>();
        if (Commands.Apply(w, s, Cmd.Claim(n, q), sink, changed) != 0) return;
        count[n]++;
        if (sink == null) return;
        for (int h = 0; h < s.Nat.Length; h++)
            if (s.Nat[h].Human && Rules.Borders(w, s, q, h))
            {
                sink.Notify("flag", $"Провинция {w.PName[q]} у наших границ вошла в состав {Ru.Genitive(s.Nations[n].Name)}");
                break;
            }
    }

    /// <summary>Overextension % at which a bot stops buying land.</summary>
    public const int BotOverMax = 25;

    // ------------------------------------------------------------------ edicts

    /// <summary>A bot reviews its edicts about once in 40 cycles (≈ 20 s at speed 3).</summary>
    public static bool WantsEdicts(int seed, int nation, int cycle) => SimRng.Chance(seed, 71, nation, cycle, 1, 40);

    /// <summary>
    /// The edicts a bot wants, most needed first: festivals while its people sulk or it sprawls, corvée while the store is
    /// low, the levy while the treasury is thin and people are content, otherwise sages while it studies and envoys while
    /// its cities can grow. It repeals what it no longer wants and fills its slots through <see cref="Cmd.Edict"/>.
    /// </summary>
    static void ChooseEdicts(WorldData w, GameState s, int n, int overPct, ISimSink sink)
    {
        var nat = s.Nat[n];
        long mood = 0, people = 0;
        for (int p = 0; p < w.P; p++) if (s.Owner[p] == n) { mood += (long)s.Mood[p] * s.Pop[p]; people += s.Pop[p]; }
        int avg = people > 0 ? (int)(mood / people) : 60;
        var want = new List<int>(Policy.Count);
        bool sick = false;
        for (int p = 0; p < w.P && !sick; p++) if (s.Owner[p] == n && Unrest.Sick(s, p)) sick = true;
        if (sick && nat.Era >= Policy.Edicts[Policy.Index("quarantine")].MinEra) want.Add(Policy.Index("quarantine"));
        if (avg < 52 || overPct >= 15) want.Add(Policy.Index("feasts"));
        if (nat.Materials < BotMaterialsLow && avg >= 58) want.Add(Policy.Index("corvee"));
        if (nat.Treasury < Rules.ClaimPrice(s, n) * Rules.Cents && avg >= 62) want.Add(Policy.Index("levy"));
        // then the bot's own taste: every nation has its favourite way to govern in calm times (so peoples differ)
        string[] calm = { "sages", "envoys", "feasts" };
        int fav = (int)((uint)SimRng.Hash(w.Seed, 72, n, 0) % (uint)calm.Length);
        for (int k = 0; k < calm.Length; k++)
        {
            int e = Policy.Index(calm[(fav + k) % calm.Length]);
            if (!want.Contains(e)) want.Add(e);
        }
        int slots = Policy.Slots(nat.Era);
        if (want.Count > slots) want.RemoveRange(slots, want.Count - slots);
        for (int e = 0; e < Policy.Count; e++)
            if (Policy.On(nat, e) && !want.Contains(e)) Commands.Apply(w, s, Cmd.Edict(n, e, false), sink);
        foreach (int e in want)
            if (!Policy.On(nat, e)) Commands.Apply(w, s, Cmd.Edict(n, e, true), sink);
    }

    // ------------------------------------------------------------------ building

    /// <summary>A bot thinks about building once in 4 cycles on average (≈ every 2 s at speed 3).</summary>
    public static bool WantsToBuild(int seed, int nation, int cycle) => SimRng.Chance(seed, 61, nation, cycle, 1, 4);

    /// <summary>Below this store a bot wants lumber mills and quarries first (a town takes Cities.FoundMaterials).</summary>
    public const int BotMaterialsLow = 600;

    /// <summary>
    /// A bot builds one thing: the best (province, building) its rules allow, through the very <see cref="Cmd.Build"/> a
    /// player issues. It keeps the price of a claim in reserve and builds only while its income covers the new upkeep
    /// with room to spare. Now and then it sends geologists to its hills instead.
    /// </summary>
    static void TryBuild(WorldData w, GameState s, int n, int shrines, ISimSink sink, ref List<int> changed)
    {
        var nat = s.Nat[n];
        if (nat.LastTaxes - nat.LastUpkeep < Rules.UpkeepPerBuilding * 3) return;   // cannot carry another building yet
        long spare = nat.Treasury - Rules.ClaimPrice(s, n) * Rules.Cents;
        if (spare < Rules.SurveyPrice(nat) * Rules.Cents) return;   // the cheapest thing a bot can order
        if (WantsToSurvey(w.Seed, n, Clock.CycleOf(s.Tick)) && Techs.Known(nat, Techs.SurveyTech) && SurveySite(w, s, n) is var q and >= 0)
        {
            Commands.Apply(w, s, Cmd.Survey(n, q), sink, changed ??= new List<int>());
            return;
        }
        if (spare < CheapestBuilding * Policy.EraPermille(nat.Era) / 1000 * Rules.Cents) return;
        var (p, b) = BestBuild(w, s, n, shrines);
        if (p >= 0 && Rules.BuildPrice(b, nat) * Rules.Cents <= spare) Commands.Apply(w, s, Cmd.Build(n, p, b), sink, changed ??= new List<int>());
    }

    static readonly int CheapestBuilding = CheapestCost();

    static int CheapestCost()
    {
        int c = int.MaxValue;
        for (int k = 0; k < Data.BldName.Length; k++) c = System.Math.Min(c, Rules.BuildCost((Bld)k));
        return c;
    }

    /// <summary>One building turn in 5 goes to the geologists instead (when the nation knows how and has hills left).</summary>
    public static bool WantsToSurvey(int seed, int nation, int cycle) => SimRng.Chance(seed, 64, nation, cycle, 1, 5);

    /// <summary>
    /// The building nation n wants most and where, or (-1, _). Every candidate passes <see cref="Rules.CheckBuild"/>
    /// except for gold (the caller checks the reserve). Score: materials while the store is low (mills, quarries, a mine on
    /// a surveyed vein), food where people press on the cap (farm, fishery, pasture, granary), a shrine where mood sags or
    /// science wants temples (`shrines` = the nation's count), a market in a crowded province; plus a salted roll so bots differ.
    /// </summary>
    public static (int province, Bld building) BestBuild(WorldData w, GameState s, int n, int shrines)
    {
        var nat = s.Nat[n];
        var facts = WorldFacts.Of(w);
        bool lowMaterials = nat.Materials < BotMaterialsLow;
        int[] land = null;   // the land's fertility, worked out once if a crowding check needs it
        int best = -1; Bld bestB = default; long bs = 0;
        for (int p = 0; p < w.P; p++)
        {
            if (s.Owner[p] != n || s.Buildings[p].Count >= s.Slots[p]) continue;
            int crowd = -1;   // pop ‰ of capacity, counted only when a food building is on the table
            for (int k = 0; k < Data.BldName.Length; k++)
            {
                var b = (Bld)k;
                if (s.Buildings[p].Contains(b) || !facts.Allows(p, b) || !Techs.Allows(nat, b)) continue;
                if (nat.Materials < Rules.BuildMaterials(b, nat)) continue;
                long sc;
                switch (b)
                {
                    case Bld.Lumber:
                        sc = lowMaterials ? 900 : 150;
                        break;
                    case Bld.Quarry:
                        sc = (lowMaterials ? 850 : 140) + (Rules.KnownOre(s, p) is Rules.OreCopper or Rules.OreTin or Rules.OreIron ? 600 : 0);
                        break;
                    case Bld.Farm: case Bld.Fishery: case Bld.Pasture: case Bld.Granary:
                        if (crowd < 0) crowd = (int)System.Math.Min(1000, (long)s.Pop[p] * 1000 / System.Math.Max(1, Simulation.Capacity(land ??= Commons.FertNow(w, s), w, s, p)));
                        sc = 200 + crowd * 6 / 10 + (b == Bld.Farm ? facts.FertPm[p] / 4 : 0) - (b == Bld.Pasture ? 80 : 0);
                        break;
                    case Bld.Shrine:
                        sc = 120 + System.Math.Max(0, 60 - s.Mood[p]) * 15 + (shrines < Science.ShrinesPerPoint * Science.ShrinesMax ? 300 : 0);
                        break;
                    case Bld.Market:
                        sc = 150 + System.Math.Min(500, s.Pop[p] / 40);
                        break;
                    default: continue;
                }
                sc += SimRng.Permille(w.Seed, 62, p, n * 8 + k) / 5 + Taste(nat, b);
                if (sc > bs) { bs = sc; best = p; bestB = b; }
            }
        }
        return (best, bestB);
    }

    /// <summary>A bot builds what suits its people: +150 per level of the scale pole the building feeds (CONTENT §10.1),
    /// so the first push of a myth grows into a character instead of every bot ending up alike.</summary>
    static int Taste(NationState nat, Bld b)
    {
        var (scale, right) = b switch
        {
            Bld.Farm or Bld.Pasture or Bld.Fishery => (Character.Agri, false),
            Bld.Market => (Character.Agri, true),
            Bld.Granary => (Character.Commune, false),
            Bld.Shrine => (Character.Faith, false),
            _ => (Character.Openness, false),   // mills and quarries: a people that makes do with its own
        };
        int lvl = Character.Level(nat, scale);
        return lvl != 0 && lvl > 0 == right ? 150 * System.Math.Abs(lvl) : 0;
    }

    /// <summary>An unsurveyed own province that may hold ore (hills, mountains, or a known outcrop), or -1; a salted pick
    /// among the first candidates so geologists do not always start at the lowest index.</summary>
    public static int SurveySite(WorldData w, GameState s, int n)
    {
        int best = -1, bs = -1;
        for (int p = 0; p < w.P; p++)
        {
            if (s.Owner[p] != n || s.OreFound[p] || !Rules.MayHaveOre(w, s, p)) continue;
            int sc = SimRng.Permille(w.Seed, 63, p, n);
            if (sc > bs) { bs = sc; best = p; }
        }
        return best;
    }

    /// <summary>All spheres full: a bot that can pay founds a town on its best free site — only when its land is really
    /// used up (about 6 provinces per city) and rarely (≈ once in 30 s at speed 3 at most).</summary>
    static void TryFoundCity(WorldData w, GameState s, int n, ISimSink sink, ref List<int> changed)
    {
        if (s.Nat[n].Treasury < ((long)Cities.FoundPrice(s, n) + Rules.ClaimPrice(s, n)) * Rules.Cents || s.Nat[n].Materials < Cities.FoundMaterialsFor(s.Nat[n])) return;
        int cities = 0, provinces = 0;
        for (int q = 0; q < w.P; q++) if (s.Owner[q] == n) { provinces++; if (Cities.IsCity(s, q)) cities++; }
        if (provinces < cities * 6) return;
        if (!SimRng.Chance(w.Seed, 33, n, Clock.CycleOf(s.Tick), 1, 60)) return;
        int p = Cities.BotFoundSite(w, s, n);
        if (p < 0) return;
        changed ??= new List<int>();
        Commands.Apply(w, s, Cmd.FoundCity(n, p), sink, changed);
    }

    /// <summary>The unowned land province next to nation n that it wants most, or -1. Score in 1/10000:
    /// fertility ×0.6, a salted roll ×0.2, own neighbours ×0.35, minus distance to the capital / 55 px, big land +0.1.</summary>
    public static int BestClaim(WorldData w, GameState s, int n)
    {
        int cap = s.NationCapital[n], best = -1;
        long bs = long.MinValue;
        if (cap < 0) return -1;
        var fert = WorldFacts.Of(w).FertPm;
        var mark = SimScratch.For(w, s).Mark;
        int ok = Cities.MarkAbsorbable(w, s, n);
        for (int p = 0; p < w.P; p++)
        {
            if (s.Owner[p] != n) continue;
            foreach (int q in w.Adj[p])
            {
                if (s.Owner[q] >= 0 || w.PLand[q] != 1) continue;
                if (s.City != null && mark[q] != ok) continue;   // every city nearby is full
                int nb = 0;
                foreach (int r in w.Adj[q]) if (s.Owner[r] == n) nb++;
                long sc = fert[q] * 6L + SimRng.Permille(w.Seed, 22, q, n) * 2L + nb * 3500L
                        - Simulation.Distance(w, q, cap) * 10_000L / 55 + (w.PSize[q] > 30 ? 1000 : 0);
                if (sc > bs || (sc == bs && q < best)) { bs = sc; best = q; }
            }
        }
        return best;
    }
}
