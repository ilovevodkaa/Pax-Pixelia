using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PaxPixelia.Core;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>The scripted game: world → nations → fog → scouts in ticks → commands → leaderboard → centuries of cycles.</summary>
public static class ScenarioTests
{
    const int Me = 0;

    public static ulong Run(WorldData w, bool verbose)
    {
        Verbose = verbose;
        var rec = new Recorder();
        var s = NationGen.CreateInitialState(w);
        int cap = s.NationCapital[Me];

        // ------------------------------------------------------------ fog init
        Section("fog: initial state");
        var sw = Stopwatch.StartNew();
        Simulation.Begin(w, s);
        s.Events = new SimEvents(TestContent.Db, w, s);
        Info($"Begin {sw.Elapsed.TotalMilliseconds:F1} ms");
        var fog = s.Nat[Me].Fog;
        Check(fog != null && Enumerable.Range(1, s.Nat.Length - 1).All(n => s.Nat[n].Fog == null), "only the human nation carries a fog map");
        FogInvariants(w, s);
        Check(Enumerable.Range(0, w.P).Where(p => s.Owner[p] == Me).All(p => fog.Fog[p] == 2), "every own province is visible");
        Check(w.Adj[cap].All(q => fog.Fog[q] == 2), "the capital's neighbours are visible");
        int explored0 = Explored(s);
        var (tax0, upk0) = Rules.Budget(s, Me);
        Info($"start budget per cycle: taxes +{tax0 / 100.0:F2}, upkeep −{upk0 / 100.0:F2}; population {Enumerable.Range(0, w.P).Where(p => s.Owner[p] == Me).Sum(p => (long)s.Pop[p])}");
        Check(explored0 > 0 && explored0 < w.P, $"explored at start: {explored0} of {w.P}");
        Check(Enumerable.Range(0, w.P).Any(p => fog.Fog[p] == 1), "a stale ring exists (explored, not visible)");
        Check(s.Controller.SequenceEqual(s.Owner), "Controller equals Owner in the peaceful game");
        MetInvariant(w, s);
        var watch = new FogWatch(s, Me);

        sw.Restart();
        for (int k = 0; k < 200; k++) FogOfWar.Recompute(w, s, Me);
        Info($"Recompute avg {sw.Elapsed.TotalMilliseconds / 200:F3} ms");
        Check(watch.Verify(rec, "idle recompute"), "recompute without changes reports nothing");

        // ------------------------------------------------------------ observer mode
        Section("observer mode");
        s.FogEnabled = false;
        Check(Rules.Leaderboard(s, Me).Count == s.NationCapital.Length && Rules.UnmetCount(s, Me) == 0, "observer: everyone listed");
        s.FogEnabled = true;
        Check(Rules.Leaderboard(s, Me).All(r => fog.Met[r.nation]), "fog on: leaderboard lists met nations only");

        // ------------------------------------------------------------ scout errors
        Section("scouts: sending");
        int sea = Enumerable.Range(0, w.P).First(p => w.PLand[p] == 0);
        Check(Scouts.Send(w, s, Me, sea, rec, out _) == ScoutError.Sea, "sea target refused");
        Check(Scouts.Send(w, s, Me, cap, rec, out _) == ScoutError.Here, "capital target refused");
        var reach = LandDist(w, cap);
        int island = Enumerable.Range(0, w.P).FirstOrDefault(p => w.PLand[p] == 1 && reach[p] < 0, -1);
        if (island >= 0) Check(Scouts.Send(w, s, Me, island, rec, out _) == ScoutError.Far, "unreachable land (another continent) refused");
        else Info("no second landmass in this world — Far not tested");
        Check(s.Scouts.Count == 0, "failed sends create no party");
        Check(Scouts.Send(w, s, 1, -1, rec, out _) == ScoutError.NoTargets, "a nation without a map has no auto targets");

        int target = Enumerable.Range(0, w.P).Where(p => w.PLand[p] == 1 && reach[p] > 0 && reach[p] <= 25 && !fog.Explored[p])
                               .OrderByDescending(p => reach[p]).ThenBy(p => p).FirstOrDefault(-1);
        Check(target >= 0, "an unexplored reachable target exists");
        Check(Scouts.Send(w, s, Me, target, rec, out var manual) == ScoutError.None, $"manual party sent to {Name(w, target)} ({reach[target]} steps)");
        Check(manual.Path[0] == cap && manual.Path[^1] == target && manual.Path.All(p => w.PLand[p] == 1) && manual.Nation == Me, "manual path: capital → target over land");
        Check(PathIsConnected(w, manual.Path), "manual path is connected");
        Check(Scouts.Send(w, s, Me, -1, rec, out var auto) == ScoutError.None, "auto party sent");
        Check(auto.Path[0] == cap && !fog.Explored[auto.Path[^1]], "auto party heads for unexplored land");
        Check(Scouts.Send(w, s, Me, -1, rec, out _) == ScoutError.Max, "third party refused (max 2)");
        Check(watch.Verify(rec, "send"), "fog changes on send are reported");

        // ------------------------------------------------------------ scouts over time
        Section("scouts: walking tick by tick");
        int ticks = 0, steps = 0, overBudget = 0; double advMs = 0;
        var exploredBefore = (bool[])fog.Explored.Clone();
        int arrivals = rec.Notes.Count(n => n.icon == "map-2");
        while (s.Scouts.Count > 0 && ticks < 200_000)
        {
            var t0 = Stopwatch.GetTimestamp();
            var tick = Scouts.Tick(w, s, rec);
            advMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            ticks++; steps += tick.Steps;
            foreach (var sc in s.Scouts)
            {
                if (sc.Auto && sc.Steps > sc.MaxSteps) overBudget++;
                if (!(sc.Sub >= 0 && sc.Sub < Scouts.SubSteps && sc.Step >= 0 && sc.Step < sc.Path.Length)) { Check(false, $"scout {sc.Id} position valid"); goto walked; }
            }
            if (tick.Any)
            {
                if (!Monotonic(exploredBefore, fog.Explored)) { Check(false, "explored memory never shrinks"); goto walked; }
                if (!watch.Verify(rec, "scout step")) { Check(false, "every fog change during scouting is reported"); goto walked; }
                if (!FogInvariants(w, s, quiet: true)) { Check(false, "fog invariants hold while scouting"); goto walked; }
            }
        }
        walked:
        Info($"{ticks} ticks ({ticks / 8.0:F0} s at speed 3), {steps} steps, Tick avg {advMs / Math.Max(1, ticks) * 1000:F1} µs");
        Check(s.Scouts.Count == 0, $"both parties came back (after {ticks} ticks)");
        Check(steps * Scouts.SubSteps <= ticks * Scouts.SubPerTick * 2 + 2 * Scouts.SubSteps, "a party walks at most one province per 2.5 ticks");
        Check(overBudget == 0, $"auto party stays within its budget (march to the frontier + {Scouts.AutoSteps})");
        var back = rec.Notes.Where(n => n.icon == "map-2").Skip(arrivals).ToList();
        Check(back.Count == 2, "two «вернулись с картами» notes");
        Check(back.Any(n => n.text.Contains(w.PName[target])), "manual party's note names its target");
        foreach (var n in back) Info("note: " + n.text);
        int explored1 = Explored(s);
        Check(explored1 > explored0, $"scouting explored new land: {explored0} → {explored1}");
        Check(fog.Fog[target] == 1 || fog.Fog[target] == 2, "target is remembered after the party left");
        FogInvariants(w, s);
        MetInvariant(w, s);

        // ------------------------------------------------------------ auto exploration until done
        Section("scouts: auto until the continent is mapped");
        int rounds = 0, last = Explored(s);
        ScoutError err = ScoutError.None;
        while (rounds < 5000)
        {
            err = Scouts.Send(w, s, Me, -1, rec, out _);
            if (err != ScoutError.None) break;
            while (s.Scouts.Count > 0) Scouts.Tick(w, s, rec);
            rounds++;
        }
        Check(err == ScoutError.NoTargets, $"auto parties eventually run out of targets ({rounds} trips, result {err})");
        Check(Enumerable.Range(0, w.P).All(p => w.PLand[p] == 0 || reach[p] < 0 || fog.Explored[p]), "all land reachable from the capital is explored");
        Check(watch.Verify(rec, "auto rounds"), "fog changes of auto rounds are reported");
        Info($"explored {last} → {Explored(s)}; met {fog.Met.Count(m => m)} of {fog.Met.Length}");
        MetInvariant(w, s);

        // ------------------------------------------------------------ claims through commands
        Section("claims (commands)");
        int claim = Enumerable.Range(0, w.P).FirstOrDefault(p => Rules.CheckClaim(w, s, p, Me) == ClaimError.None, -1);
        Check(claim >= 0, "a claimable border province exists");
        long gold = s.Nat[Me].Treasury;
        int popBefore = s.Pop[claim];
        Check(Commands.Apply(w, s, Cmd.Claim(Me, claim), rec) == 0, "claim command executes");
        Check(s.Owner[claim] == Me && s.Controller[claim] == Me && s.Nat[Me].Treasury == gold - Rules.ClaimCost * Rules.Cents, "claim: owner set, 120 gold paid");
        Check(s.Pop[claim] == (int)((long)popBefore * Rules.ClaimPopBoostPermille / 1000), "claim: census ×2.2 in whole people");
        Check(s.Religion[claim] == s.Nations[Me].Religion, "claim: state religion");
        Check(fog.Fog[claim] == 2 && w.Adj[claim].Where(q => w.PLand[q] == 1).All(q => fog.Fog[q] == 2), "claim: new border is watched");
        Check(rec.Provinces.Any(l => l.Contains(claim)), "claim: ProvincesChanged raised");
        Check(watch.Verify(rec, "claim"), "claim: fog changes reported");
        Check(Commands.Apply(w, s, Cmd.Claim(Me, claim), rec) == (int)ClaimError.Owned, "cannot claim twice (re-validated at execution)");
        Check(Rules.CheckClaim(w, s, sea, Me) == ClaimError.NotLand, "cannot claim sea");
        int far = Enumerable.Range(0, w.P).FirstOrDefault(p => w.PLand[p] == 1 && s.Owner[p] < 0 && fog.Explored[p] && !Rules.Borders(w, s, p, Me), -1);
        if (far >= 0) Check(Rules.CheckClaim(w, s, far, Me) == ClaimError.NotAdjacent, "cannot claim land away from the border");
        int unexplored = Enumerable.Range(0, w.P).FirstOrDefault(p => w.PLand[p] == 1 && s.Owner[p] < 0 && !fog.Explored[p] && Rules.Borders(w, s, p, Me), -1);
        if (unexplored >= 0) Check(Rules.CheckClaim(w, s, unexplored, Me) == ClaimError.Unexplored, "cannot claim unexplored land");
        int next = Enumerable.Range(0, w.P).FirstOrDefault(p => Rules.CheckClaim(w, s, p, Me) == ClaimError.None, -1);
        if (next >= 0)
        {
            s.Nat[Me].Treasury = 50 * Rules.Cents;
            Check(Commands.Apply(w, s, Cmd.Claim(Me, next), rec) == (int)ClaimError.NoGold && s.Owner[next] < 0, "cannot claim without gold");
            s.Nat[Me].Treasury = 1000 * Rules.Cents;
        }

        // ------------------------------------------------------------ buildings
        Section("buildings (commands)");
        int bp = Enumerable.Range(0, w.P).Where(p => s.Owner[p] == Me && s.Buildings[p].Count < s.Slots[p]).OrderBy(p => p).FirstOrDefault(-1);
        Check(bp >= 0, "an own province with a free plot");
        var opts = Rules.BuildOptions(w, s, bp, Me);
        Check(opts.Count > 0 && opts.All(b => !s.Buildings[bp].Contains(b)), $"options offered: {string.Join(", ", opts.Select(b => Data.BldName[(int)b]))}");
        Check(opts.All(b => TerrainAllows(w, bp, b)), "options follow the terrain rules");
        Check(Enumerable.Range(0, w.P).Where(p => w.PLand[p] == 1).All(p => SameTerrainAsMockup(w, p)), "integer terrain facts match the float terrain rules everywhere");
        s.Nat[Me].Treasury = 10_000 * Rules.Cents;
        s.Nat[Me].Materials = 1000;
        while (Rules.BuildOptions(w, s, bp, Me) is { Count: > 0 } o)
        {
            long g0 = s.Nat[Me].Treasury, m0 = s.Nat[Me].Materials;
            if (!Check(Commands.Apply(w, s, Cmd.Build(Me, bp, o[0]), rec) == 0, $"can build {Data.BldName[(int)o[0]]}", quietPass: true)) break;
            Check(s.Nat[Me].Treasury == g0 - Rules.BuildCost(o[0]) * Rules.Cents, "building costs gold", quietPass: true);
            Check(s.Nat[Me].Materials == m0 - Rules.BuildMaterials(o[0]), "building costs materials", quietPass: true);
        }
        Check(s.Buildings[bp].Count == s.Slots[bp] || opts.Count < s.Slots[bp], "plots filled");
        var any = Enum.GetValues<Data.Bld>().First(b => TerrainAllows(w, bp, b));
        Check(Rules.CheckBuild(w, s, bp, any, Me) is BuildError.NoSlot or BuildError.AlreadyBuilt, "no building beyond the plots");
        int foreign = Enumerable.Range(0, w.P).First(p => s.Owner[p] > 0);
        Check(Rules.BuildOptions(w, s, foreign, Me).Count == 0 && Commands.Apply(w, s, Cmd.Build(Me, foreign, Data.Bld.Granary), rec) == (int)BuildError.NotOwned, "no building abroad");
        Check(Commands.Apply(w, s, new Cmd(0, Me, 0, CmdType.Build, bp, 77), rec) == (int)BuildError.NotAllowed, "a malformed building id is refused, not thrown");

        // ------------------------------------------------------------ geology
        Section("geology (commands)");
        int gp = Enumerable.Range(0, w.P).FirstOrDefault(p => s.Owner[p] == Me && !s.OreFound[p], -1);
        if (gp >= 0)
        {
            Check(Commands.Apply(w, s, Cmd.Survey(Me, gp), rec) == 0, "survey done");
            Check(s.OreFound[gp] && Rules.CheckSurvey(s, gp, Me) == SurveyError.AlreadyDone, "surveyed once");
        }
        Check(Rules.CheckSurvey(s, foreign, Me) == SurveyError.NotOwned, "no geologists abroad");

        // ------------------------------------------------------------ leaderboard
        Section("leaderboard");
        var lb = Rules.Leaderboard(s, Me);
        Check(lb.Any(r => r.nation == Me), "player listed");
        Check(lb.All(r => fog.Met[r.nation]), "only met nations");
        Check(lb.Zip(lb.Skip(1)).All(z => z.First.score >= z.Second.score), "sorted by score");
        Check(lb.Count + Rules.UnmetCount(s, Me) == s.NationCapital.Length, $"listed {lb.Count} + unmet {Rules.UnmetCount(s, Me)} = {s.NationCapital.Length}");
        if (verbose) foreach (var (n, sc) in lb.Take(5)) Info($"  {s.Nations[n].Name,-16} {sc}");

        // ------------------------------------------------------------ centuries of cycles
        Section("rules over time (1400 cycles = 5600 ticks, ≈ 12 min at speed 3)");
        var dates = new List<long>();
        int owned0 = Enumerable.Range(0, w.P).Count(p => s.Owner[p] >= 0);
        int notes0 = rec.Notes.Count, events0 = s.Nat[Me].EventCount, botEvents0 = s.Nat.Skip(1).Sum(n => n.EventCount);
        long gold0 = s.Nat[Me].Treasury, prog0 = s.Nat[Me].Progress;
        sw.Restart();
        int cycles = 1400, badPop = -1;
        long tick0 = s.Tick;
        for (int k = 0; k < cycles * Clock.CycleTicks; k++)
        {
            Simulation.Step(w, s, rec);
            dates.Add(s.Day256);
            if (k % 200 == 0 && !watch.Verify(rec, "cycle")) Check(false, "fog changes from bot claims are reported");
            if (badPop < 0 && Clock.IsCycleTick(s.Tick - 1))
                for (int p = 0; p < w.P; p++)
                    if (w.PLand[p] == 1 && (s.Pop[p] < 10 || s.Pop[p] > (long)Simulation.Capacity(w, s, p) * 5 / 2 + 10)) { badPop = p; break; }
        }
        double tickUs = sw.Elapsed.TotalMilliseconds * 1000 / (cycles * Clock.CycleTicks);
        Info($"{cycles * Clock.CycleTicks} ticks in {sw.ElapsedMilliseconds} ms ({tickUs:F1} µs/tick) → {Calendar.Text(s.Date, true)}, era {Eras.Name(s.Nat[Me].Era)}");
        Check(s.Tick - tick0 == cycles * Clock.CycleTicks, "one Step = one tick");
        Check(dates.Zip(dates.Skip(1)).All(z => z.Second >= z.First), "the calendar never goes back");
        Check(dates[^1] > dates[0], $"the calendar advanced: {Calendar.Text(Calendar.DateOf(dates[0]), true)} → {Calendar.Text(s.Date, true)}");
        Check(badPop < 0, badPop < 0 ? "population ≥ 10 and near capacity" : $"population of {Name(w, badPop)} = {s.Pop[badPop]} (cap {Simulation.Capacity(w, s, badPop)})");
        Check(Enumerable.Range(0, w.P).All(p => s.Mood[p] <= 100), "mood within 0..100");
        Check(s.Nat[Me].LastTaxes > 0 && s.Nat[Me].LastUpkeep >= 0, $"budget per cycle: taxes {s.Nat[Me].LastTaxes / 100.0:F2}, upkeep {s.Nat[Me].LastUpkeep / 100.0:F2}");
        Check(s.Nat[Me].Treasury > gold0, $"treasury grew: {gold0 / 100} → {s.Nat[Me].Treasury / 100}");
        Check(s.Nat.All(n => n.Treasury >= 0), "no nation's treasury below zero (bots pay for their claims)");
        Check(s.Nat[Me].Progress > prog0 && s.Nat[Me].ScienceRate >= Science.Sages, $"research: +{s.Nat[Me].ScienceRate}/cycle, stock {s.Nat[Me].Progress}");
        var yearNotes = rec.Notes.Skip(notes0).ToList();
        int events = s.Nat[Me].EventCount - events0, botEvents = s.Nat.Skip(1).Sum(n => n.EventCount) - botEvents0;
        Check(events >= 5, $"event deck: {events} events for the player in {cycles} cycles (one per 90–180 cycles), {botEvents} for the bots");
        Check(botEvents > 0, "bots draw from the deck too (they answer choices by AI weights)");
        Check(yearNotes.All(n => !string.IsNullOrWhiteSpace(n.text) && !n.text.Contains('{')), "event texts are filled in");
        EventChoice(w, s, rec);
        int owned1 = Enumerable.Range(0, w.P).Count(p => s.Owner[p] >= 0);
        Check(owned1 > owned0, $"bots expanded: {owned0} → {owned1} owned provinces");
        Check(s.Controller.SequenceEqual(s.Owner), "Controller still equals Owner");
        Check(rec.Notes.Any(n => n.icon == "hammer" && n.text.Contains("столиц", StringComparison.OrdinalIgnoreCase)), "capital projects complete");
        Check(s.Nat.Skip(1).Any(n => n.ProjectIndex != Simulation.Projects.Length - 1 || n.ProjectsDone != 0), "bots build their capital projects too");
        FogInvariants(w, s);
        MetInvariant(w, s);
        int hidden = Enumerable.Range(0, w.P).Count(p => fog.Fog[p] == 1 && fog.KnownOwner[p] != s.Owner[p]);
        Info($"stale provinces whose owner changed out of sight (the map still shows the old one): {hidden}");
        int projects = rec.Notes.Count(n => n.icon == "hammer" && Simulation.Projects.Any(pr => pr.DoneText == n.text));
        Check(projects <= Simulation.Projects.Length, $"each capital project is finished at most once ({projects} in {cycles} cycles)");
        if (verbose)
        {
            foreach (var n in yearNotes.Where(n => n.icon is not ("hammer" or "flag")).Take(8)) Info($"  [{n.icon}] {n.text}");
            Info($"  … {yearNotes.Count} notes in {cycles} cycles");
        }

        return s.Hash().All ^ rec.NotesHash();
    }

    // ================================================================ invariants

    /// <summary>A choice opens for the player (forced if the deck has none open) and the Choose command answers it.</summary>
    static void EventChoice(WorldData w, GameState s, Recorder rec)
    {
        var ev = s.Events;
        if (ev.Pending(Me) == null)
            for (int e = 0; e < ev.Db.Events.Length && ev.Pending(Me) == null; e++)
                if (ev.Db.Events[e].IsChoice) ev.Force(s, Me, e, rec);
        if (!Check(ev.Pending(Me) is { } r, "a choice window opens for the player")) return;
        int notes = rec.Notes.Count;
        Check(Commands.Apply(w, s, Cmd.Choose(Me, 99), rec) == Commands.BadCommand, "an option that is not offered is refused");
        Check(Commands.Apply(w, s, Cmd.Choose(Me, 0), rec) == 0 && ev.Pending(Me) == null, "Choose answers it and closes the window");
        Check(rec.Notes.Count > notes && !rec.Notes[^1].text.Contains('{'), $"the outcome goes to the chronicle: «{(rec.Notes.Count > notes ? rec.Notes[^1].text : "")}»");
    }

    static bool FogInvariants(WorldData w, GameState s, bool quiet = false)
    {
        var f = s.Nat[Me].Fog;
        bool ok = true;
        for (int p = 0; p < w.P && ok; p++)
            ok = f.Fog[p] <= 2 && (f.Fog[p] == 0) == !f.Explored[p];
        if (!quiet) Check(ok, "fog: states 0/1/2, unexplored ⇔ state 0");
        return ok;
    }

    static void MetInvariant(WorldData w, GameState s)
    {
        var f = s.Nat[Me].Fog;
        var expect = new bool[f.Met.Length];
        for (int p = 0; p < w.P; p++) if (f.Explored[p] && f.KnownOwner[p] >= 0) expect[f.KnownOwner[p]] = true;
        // Met is monotonic, so a nation may stay met after losing its explored provinces
        Check(Enumerable.Range(0, expect.Length).All(n => !expect[n] || f.Met[n]), $"met ⊇ known owners of explored land ({f.Met.Count(m => m)} met)");
        Check(f.Met[Me], "the player has met itself");
        Check(Enumerable.Range(0, w.P).All(p => f.Fog[p] != 2 || f.KnownOwner[p] == s.Owner[p]), "visible provinces show their real owner");
        Check(Enumerable.Range(0, w.P).All(p => f.Explored[p] || f.KnownOwner[p] < 0), "unexplored provinces have no remembered owner");
    }

    static bool Monotonic(bool[] before, bool[] now)
    {
        for (int p = 0; p < now.Length; p++) { if (before[p] && !now[p]) return false; before[p] = now[p]; }
        return true;
    }

    static bool TerrainAllows(WorldData w, int p, Data.Bld b)
    {
        var l = new List<Data.Bld>(); Rules.TerrainOptions(w, p, l); return l.Contains(b);
    }

    /// <summary>NationGen's float terrain rules (the mockup's bOpts) against the integer facts the rules use.</summary>
    static bool SameTerrainAsMockup(WorldData w, int p)
    {
        var gen = NationGen.BuildOptions(w, p);
        var mine = new List<Data.Bld>(); Rules.TerrainOptions(w, p, mine);
        mine.Remove(Data.Bld.Shrine);
        return gen.OrderBy(b => b).SequenceEqual(mine.OrderBy(b => b));
    }

    static bool PathIsConnected(WorldData w, int[] path)
    {
        for (int k = 1; k < path.Length; k++) if (!w.Adj[path[k - 1]].Contains(path[k])) return false;
        return true;
    }

    public static int[] LandDist(WorldData w, int from)
    {
        var d = Enumerable.Repeat(-1, w.P).ToArray(); var q = new Queue<int>(); d[from] = 0; q.Enqueue(from);
        while (q.Count > 0) { int p = q.Dequeue(); foreach (int n in w.Adj[p]) if (d[n] < 0 && w.PLand[n] == 1) { d[n] = d[p] + 1; q.Enqueue(n); } }
        return d;
    }

    static int Explored(GameState s) => s.Nat[Me].Fog.Explored.Count(e => e);
    static string Name(WorldData w, int p) => $"{w.PName[p]} #{p}";
}
