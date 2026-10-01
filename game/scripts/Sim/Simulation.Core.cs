using System;
using System.Collections.Generic;
using PaxPixelia.Core;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

/// <summary>What a run of ticks did, so the Game raises each event at most once per frame.</summary>
public struct TickReport
{
    public int Ticks;
    public bool DayChanged, MonthChanged, YearChanged, Cycle, EraChanged, Tribes;
    public int ScoutSteps, ScoutsFinished;

    public void Add(in TickReport o)
    {
        Ticks += o.Ticks;
        DayChanged |= o.DayChanged; MonthChanged |= o.MonthChanged; YearChanged |= o.YearChanged;
        Cycle |= o.Cycle; EraChanged |= o.EraChanged; Tribes |= o.Tribes;
        ScoutSteps += o.ScoutSteps; ScoutsFinished += o.ScoutsFinished;
    }
}

/// <summary>
/// The rules in ticks (pure C#, integers, deterministic). One tick: scouts move; every <see cref="Clock.CycleTicks"/>th
/// tick the rules cycle runs (budget, growth, mood, research and eras, capital projects, bots, the event deck); then the
/// calendar glides towards the date the leader's progress has earned. Commands are applied between ticks by
/// <see cref="CommandQueue"/>, never inside one.
/// </summary>
public static partial class Simulation
{
    public const int QueuePctPerCycle = 3;
    public const long StartTreasury = 400 * Rules.Cents;

    public sealed record Project(string Name, string DoneText, Bld? Building);

    /// <summary>Each capital builds these in turn, each once: building projects already standing in the capital are skipped,
    /// and so are buildings its knowledge does not open yet (pottery, barter…: they join the queue once learned); the
    /// others (walls, road) are one-off. With nothing to build the queue stands idle (ProjectIndex = -1) and looks again
    /// every cycle.</summary>
    public static readonly Project[] Projects =
    {
        new("Амбар", "В столице построен амбар", Bld.Granary),
        new("Каменные стены", "Столицу обнесли каменными стенами", null),
        new("Рынок", "В столице открылся рынок", Bld.Market),
        new("Святилище", "В столице освятили новое святилище", Bld.Shrine),
        new("Мощёная дорога", "Главную улицу столицы вымостили камнем", null),
    };

    /// <summary>Prepare a freshly generated state for play: fog of the human nations, first projects, science rates and the date.</summary>
    public static void Begin(WorldData w, GameState s)
    {
        FogOfWar.Init(w, s);
        Cities.Init(w, s);
        for (int n = 0; n < s.Nat.Length; n++) { Character.Init(s.Nat[n]); Character.SeedFromLand(w, s, n); }
        Firsts.Init(s);
        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            nat.ProjectsDone = 0;
            nat.ProjectIndex = NextProject(s, n, Projects.Length - 1);   // skip what the capital already has
        }
        var sc = Tally(w, s);
        for (int n = 0; n < s.Nat.Length; n++) { s.Nat[n].LastTaxes = sc.Taxes[n]; s.Nat[n].LastUpkeep = sc.Upkeep[n]; s.Nat[n].LastMaterials = sc.Materials[n]; }
        Rates(s, sc);
        PlanDate(s);
    }

    /// <summary>One tick of the rules.</summary>
    public static TickReport Step(WorldData w, GameState s, ISimSink sink)
    {
        var r = new TickReport { Ticks = 1 };
        var mv = Scouts.Tick(w, s, sink);
        r.ScoutSteps = mv.Steps; r.ScoutsFinished = mv.Finished;
        if (s.AnyNomads)
        {
            List<int> settled = null; bool moved = false;
            Nomads.Tick(w, s, sink, ref settled, ref moved);
            r.Tribes = moved || settled != null;
            if (settled != null)
            {
                sink?.RaiseProvincesChanged(settled.ToArray());
                FogOfWar.Refresh(w, s, sink);
            }
        }
        if (Clock.IsCycleTick(s.Tick)) { Cycle(w, s, Clock.CycleOf(s.Tick), sink, ref r); r.Cycle = true; }
        AdvanceDate(s, ref r);
        s.Tick++;
        return r;
    }

    static void Cycle(WorldData w, GameState s, int cycle, ISimSink sink, ref TickReport r)
    {
        var sc = Tally(w, s);
        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            nat.LastTaxes = sc.Taxes[n]; nat.LastUpkeep = sc.Upkeep[n];
            nat.Treasury += sc.Taxes[n] - sc.Upkeep[n];
            nat.LastMaterials = sc.Materials[n];
            nat.Materials += sc.Materials[n];
        }
        Grow(w, s, cycle);
        Moods(w, s, cycle, sc);
        if (Research(w, s, sc, sink)) r.EraChanged = true;
        List<int> changed = null;
        for (int n = 0; n < s.Nat.Length; n++) Queue(s, n, sink, ref changed);
        Cities.Grow(w, s, sink, ref changed);
        Bots.Act(w, s, cycle, sc, sink, ref changed);
        s.Events?.Cycle(cycle, sink);
        Policy.Deeds(s);   // governing by edicts shapes the people, like any deed
        Character.Cycle(w, s, cycle, sink);
        Firsts.Cycle(w, s, cycle, sink);
        PlanDate(s);

        if (changed != null)
        {
            sink?.RaiseProvincesChanged(changed.ToArray());
            FogOfWar.Refresh(w, s, sink);     // new owners may reveal nations we already see
        }
    }

    /// <summary>One pass over the provinces: land, shrines, taxes and upkeep per nation (this cycle's scratch tallies).</summary>
    static SimScratch Tally(WorldData w, GameState s)
    {
        var sc = SimScratch.For(w, s);
        Array.Clear(sc.Provinces); Array.Clear(sc.Shrines); Array.Clear(sc.Taxes); Array.Clear(sc.Upkeep); Array.Clear(sc.Materials); Array.Clear(sc.CityN);
        for (int p = 0; p < w.P; p++)
        {
            int o = s.Owner[p];
            if (o < 0) continue;
            sc.Provinces[o]++;
            if (Cities.IsCity(s, p)) sc.CityN[o]++;
            sc.Taxes[o] += Rules.ProvinceTax(s, p);
            sc.Upkeep[o] += Rules.ProvinceUpkeep(s, p);
            sc.Materials[o] += Rules.ProvinceMaterials(s, p);
            foreach (var b in s.Buildings[p]) if (b == Bld.Shrine) sc.Shrines[o]++;
        }
        for (int n = 0; n < s.Nat.Length; n++)   // the administration (its overextension) and the edicts take their share (Policy)
        {
            var nat = s.Nat[n];
            int limit = Policy.AdminLimit(nat, sc.CityN[n]);
            sc.OverPct[n] = Policy.OverPct(sc.Provinces[n], limit);
            sc.Upkeep[n] += Policy.AdminUpkeep(sc.Provinces[n], limit) + Policy.EdictCost(nat, sc.Taxes[n]);
            if (Policy.MaterialsPct(nat) > 0) sc.Materials[n] += (sc.Materials[n] * Policy.MaterialsPct(nat) + 50) / 100;   // rounded: a small store still feels it
        }
        return sc;
    }

    // ------------------------------------------------------------------ people

    /// <summary>Population cap of p: fertile land feeds more, farms and granaries raise it, the capital draws people.</summary>
    public static int Capacity(WorldData w, GameState s, int p) => Capacity(WorldFacts.Of(w), w, s, p);

    static int Capacity(WorldFacts facts, WorldData w, GameState s, int p)
    {
        long baseCap = (long)w.PSize[p] * Math.Max(50, facts.FertPm[p]) * 55 / 1000;
        if (s.Owner[p] < 0) return (int)(baseCap * 3 / 10);    // nomad tribes
        int m = 1600;
        foreach (var b in s.Buildings[p])
            m += b switch { Bld.Farm => 300, Bld.Granary => 200, Bld.Fishery => 150, Bld.Pasture => 100, _ => 0 };
        m += Nomads.MythCapacity(w, s, s.Owner[p], p);
        var own = s.Nat[s.Owner[p]];
        m += Techs.Sum(own, TechFx.CapPermille) * 1600 / 1000 + (w.PRiver[p] != 0 ? Techs.Sum(own, TechFx.RiverCap) * 1600 / 1000 : 0);
        if (s.CapitalOf[p] >= 0) m = m * 5 / 2;
        return (int)Math.Min(int.MaxValue, baseCap * m / 1000);
    }

    const long Trillion = 1_000_000_000_000;

    /// <summary>Logistic growth: ~0.3–1.5 % a cycle by fertility and mood, slowing towards Capacity. The fraction of a person
    /// is rounded up with the matching chance, so small provinces grow on average instead of stalling.</summary>
    static void Grow(WorldData w, GameState s, int cycle)
    {
        var facts = WorldFacts.Of(w);
        var fert = facts.FertPm;
        for (int p = 0; p < w.P; p++)
        {
            if (w.PLand[p] != 1) continue;
            long pop = s.Pop[p];
            if (pop <= 0) continue;
            long cap = Math.Max(1, Capacity(facts, w, s, p));
            long rPpm = 3000 + 12L * fert[p];                                 // growth rate, per million
            long moodPm = IntMath.Clamp((s.Mood[p] - 30) * 20, -500, 1200);   // (mood − 30) / 50
            long roomPm = 1000 - pop * 1000 / cap;
            // over capacity it shrinks whatever the mood; the product is people × 10¹²
            long num = roomPm >= 0 ? pop * rPpm * moodPm * roomPm : pop * rPpm * roomPm * 1000;
            long whole = num / Trillion, frac = Math.Abs(num % Trillion) / 1_000_000;   // fraction in millionths
            if (frac > 0 && (long)((ulong)SimRng.Hash(w.Seed, 4, p, cycle) * 1_000_000 >> 32) < frac) whole += Math.Sign(num);
            s.Pop[p] = (int)Math.Clamp(pop + whole, 10, int.MaxValue);
        }
    }

    /// <summary>Mood drifts one point a cycle towards what the province has: shrines, markets, granaries, salt, the right faith.</summary>
    static void Moods(WorldData w, GameState s, int cycle, SimScratch sc)
    {
        for (int p = 0; p < w.P; p++)
        {
            int o = s.Owner[p];
            int target = 60;
            if (o >= 0)
            {
                int shrine = 8 + Techs.Sum(s.Nat[o], TechFx.ShrineMood);
                foreach (var b in s.Buildings[p])
                    target += b switch { Bld.Shrine => shrine, Bld.Market => 3, Bld.Granary => 4, _ => 0 };
                target += Techs.Sum(s.Nat[o], TechFx.Mood) + Policy.MoodOf(s.Nat[o]) + Policy.OverMood(sc.OverPct[o]);
                if (s.CapitalOf[p] >= 0) target += 5;
                if (Rules.KnownOre(s, p) == Rules.OreSalt) target += Rules.SaltMood;
                target += Nomads.MythMood(s, o);
                if (s.Religion[p] >= 0 && s.Religion[p] != s.Nations[o].Religion) target -= 10;
            }
            target = IntMath.Clamp(target, 5, 95);
            int m = s.Mood[p];
            m += Math.Sign(target - m);
            int j = SimRng.Permille(w.Seed, 5, p, cycle);   // rare ±1 so equal provinces do not all move in unison
            if (j < 40) m--; else if (j >= 960) m++;
            s.Mood[p] = (byte)IntMath.Clamp(m, 0, 100);
        }
    }

    // ------------------------------------------------------------------ research, eras and the date

    /// <summary>Everyone adds this cycle's science to the era stock and to the technology it studies; returns true when
    /// some nation entered a new era. A nation enters the era its stock has reached only with enough of its technologies
    /// (<see cref="Techs.EraCap"/>): the calendar still follows the stock.</summary>
    static bool Research(WorldData w, GameState s, SimScratch sc, ISimSink sink)
    {
        int leaderEra = Science.LeaderEra(s);
        Rates(s, sc);
        bool any = false;
        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            long before = nat.Progress;
            nat.Progress += nat.ScienceRate;
            int learned = Techs.Advance(w.Seed, n, nat, Techs.ResearchRate(nat), s.Pace);
            if (learned >= 0) { Character.OnLearn(s, n, learned); Firsts.OnLearn(w, s, n, learned, sink); }
            if (learned >= 0 && nat.Human && sink != null)
            {
                var d = Techs.All[learned];
                sink.Notify("atom", $"Изучено: «{d.Name}». {d.Effect}" + (Techs.HasOpen(nat) ? ". Выберите, чему учиться дальше" : ""));
            }
            int reached = Eras.EraOf(nat.Progress, s.Pace);
            int e = Math.Min(reached, Techs.EraCap(nat));
            if (nat.Human && sink != null && reached > nat.Era && e == nat.Era && Eras.EraOf(before, s.Pace) <= nat.Era)
            {
                int left = Techs.Required(nat.Era) - Techs.KnownIn(nat, nat.Era);
                sink.Notify("atom", $"{(nat.Era == 0 ? "Род готов" : "Держава готова")} к эпохе «{Eras.Name(nat.Era + 1)}», но знаний мало: изучите ещё {left} {Ru.Plural(left, "технологию", "технологии", "технологий")}");
            }
            if (e <= nat.Era) continue;
            nat.Era = (byte)e;
            any = true;
            if (sink == null) continue;
            if (nat.Human) sink.Notify("history", $"{s.Nations[n].Name} вступает в эпоху «{Eras.Name(e)}»");
            else if (e > leaderEra && MetByHuman(s, n))
                sink.Notify("history", $"{s.Nations[n].Name} первой в мире вступает в эпоху «{Eras.Name(e)}»");
            if (e > leaderEra) leaderEra = e;
        }
        return any;
    }

    static void Rates(GameState s, SimScratch sc)
    {
        int leaderEra = Science.LeaderEra(s);
        for (int n = 0; n < s.Nat.Length; n++)
            s.Nat[n].ScienceRate = Science.Of(sc.Provinces[n], sc.Shrines[n], s.Nat[n].Era < leaderEra, Nomads.IsNomad(s.Nat[n])).Total;   // the era stock: knowledge only speeds studies
    }

    internal static bool MetByHumanPublic(GameState s, int n) => MetByHuman(s, n);

    static bool MetByHuman(GameState s, int n)
    {
        foreach (var nat in s.Nat) if (nat.Fog is { } f && n < f.Met.Length && f.Met[n]) return true;
        return false;
    }

    /// <summary>Aim the date at where the leader's progress will stand after the next cycle, and glide there in equal steps.</summary>
    public static void PlanDate(GameState s)
    {
        var lead = s.Nat[Science.Leader(s)];
        s.DateTarget = Math.Max(s.Day256, Calendar.DayAt(lead.Progress + lead.ScienceRate, s.Pace));
        s.DateStep = (s.DateTarget - s.Day256 + Clock.CycleTicks - 1) / Clock.CycleTicks;
    }

    static void AdvanceDate(GameState s, ref TickReport r)
    {
        if (s.Day256 >= s.DateTarget) return;
        long d0 = s.Day256 / Calendar.DayUnit;
        s.Day256 = Math.Min(s.DateTarget, s.Day256 + s.DateStep);
        long d1 = s.Day256 / Calendar.DayUnit;
        if (d1 == d0) return;
        r.DayChanged = true;
        var a = Calendar.DateOf(d0 * Calendar.DayUnit);
        var b = Calendar.DateOf(d1 * Calendar.DayUnit);
        if (a.MonthIndex != b.MonthIndex) r.MonthChanged = true;
        if (a.AstroYear != b.AstroYear) r.YearChanged = true;
    }

    /// <summary>Debug jump (console / --era): every nation reaches at least era e, the calendar jumps to its first day.</summary>
    public static void JumpToEra(GameState s, int era)
    {
        era = IntMath.Clamp(era, 0, Eras.Last);
        long need = Eras.Threshold(era, s.Pace);
        foreach (var nat in s.Nat)
        {
            if (nat.Progress < need) nat.Progress = need;
            Techs.GrantBefore(nat, era);
            nat.Era = (byte)Math.Min(Eras.EraOf(nat.Progress, s.Pace), Techs.EraCap(nat));
        }
        s.Day256 = Math.Max(s.Day256, Calendar.DayAt(s.Nat[Science.Leader(s)].Progress, s.Pace));
        PlanDate(s);
    }

    // ------------------------------------------------------------------ capital projects

    static void Queue(GameState s, int n, ISimSink sink, ref List<int> changed)
    {
        SyncQueue(s, n);
        var nat = s.Nat[n];
        if (nat.ProjectIndex < 0 || s.NationCapital[n] < 0) return;   // no capital yet (nomads): nothing to build
        nat.QueuePct += QueuePctPerCycle;
        if (nat.QueuePct < 100) return;
        var pr = Projects[nat.ProjectIndex];
        int cap = s.NationCapital[n];
        if (pr.Building is Bld b)
        {
            if (s.Buildings[cap].Count >= s.Slots[cap]) s.Slots[cap]++;   // the project brings its own plot
            s.Buildings[cap].Add(b);
            (changed ??= new List<int>()).Add(cap);
        }
        else nat.ProjectsDone |= 1 << nat.ProjectIndex;
        Character.OnProject(s, n, pr.Building);
        if (nat.Human) sink?.Notify("hammer", pr.DoneText);
        nat.QueuePct = 0;
        Advance(s, n);
    }

    /// <summary>
    /// If nation n's current project already stands in its capital (built by hand) or is not open to its knowledge (an
    /// older save), move on to the next project quietly, keeping the progress made so far. An idle queue takes up a
    /// project a new technology has opened.
    /// </summary>
    public static void SyncQueue(GameState s, int n)
    {
        var nat = s.Nat[n];
        if (nat.ProjectIndex < 0) nat.ProjectIndex = NextProject(s, n, Projects.Length - 1);
        else if (!Available(s, n, nat.ProjectIndex)) Advance(s, n);
    }

    /// <summary>Is the capital idle only because some building project waits for a technology?</summary>
    public static bool QueueWaitsForKnowledge(GameState s, int n)
    {
        int cap = s.NationCapital[n];
        foreach (var pr in Projects)
            if (pr.Building is Bld b && !(cap >= 0 && s.Buildings[cap].Contains(b)) && !Techs.Allows(s.Nat[n], b)) return true;
        return false;
    }

    static void Advance(GameState s, int n)
    {
        var nat = s.Nat[n];
        nat.ProjectIndex = NextProject(s, n, nat.ProjectIndex);
        if (nat.ProjectIndex < 0) nat.QueuePct = 0;
    }

    /// <summary>The next project after `from` that nation n can do now, or -1 when there is none.</summary>
    static int NextProject(GameState s, int n, int from)
    {
        for (int k = 1; k <= Projects.Length; k++)
        {
            int i = ((from < 0 ? Projects.Length - 1 : from) + k) % Projects.Length;
            if (Available(s, n, i)) return i;
        }
        return -1;
    }

    /// <summary>Project i is still to do and nation n's knowledge allows it.</summary>
    static bool Available(GameState s, int n, int i)
    {
        if (Projects[i].Building is not Bld b) return (s.Nat[n].ProjectsDone & (1 << i)) == 0;
        int cap = s.NationCapital[n];
        return !(cap >= 0 && s.Buildings[cap].Contains(b)) && Techs.Allows(s.Nat[n], b);
    }

    /// <summary>World-wrapped distance between province anchors in whole pixels.</summary>
    public static int Distance(WorldData w, int a, int b)
    {
        long dx = Math.Abs(w.PCX[a] - w.PCX[b]);
        if (dx > w.W / 2) dx = w.W - dx;
        long dy = w.PCY[a] - w.PCY[b];
        return (int)IntMath.Isqrt(dx * dx + dy * dy);
    }
}
