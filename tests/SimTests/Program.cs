using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PaxPixelia.Core;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Tests;

/// <summary>
/// Console tests of the simulation: world → nations → fog → scouts over simulated time → claims/buildings/geology →
/// leaderboard → centuries of yearly ticks, with invariants checked after every step, plus a determinism replay.
/// Exit code 0 = all checks passed.
/// </summary>
public static class Program
{
    static int _pass, _fail;
    static readonly List<string> Failures = new();

    public static int Main(string[] args)
    {
        int seed = 1337;
        foreach (var a in args) if (a.TrimStart('-').StartsWith("seed=")) seed = int.Parse(a.TrimStart('-')[5..]);
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var sw = Stopwatch.StartNew();
        var w = WorldGen.Generate(seed, 2560, 1440);
        Console.WriteLine($"world seed={seed}: {w.P} provinces ({Enumerable.Range(0, w.P).Count(p => w.PLand[p] == 1)} land) in {sw.ElapsedMilliseconds} ms");

        var run1 = Scenario(w, verbose: true);
        Section("determinism");
        var w2 = WorldGen.Generate(seed, 2560, 1440);
        var run2 = Scenario(w2, verbose: false);
        Check(run1 == run2, $"same seed, same commands → same state hash ({run1:X16} vs {run2:X16})");

        Console.WriteLine();
        Console.WriteLine($"{_pass} passed, {_fail} failed");
        foreach (var f in Failures) Console.WriteLine("  FAIL " + f);
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>The whole scripted game. Returns a hash of the final state.</summary>
    static ulong Scenario(WorldData w, bool verbose)
    {
        _verbose = verbose;
        var rec = new Recorder();
        var s = NationGen.CreateInitialState(w);
        int me = GameState.LocalPlayer, cap = s.NationCapital[me];

        // ------------------------------------------------------------ fog init
        Section("fog: initial state");
        var sw = Stopwatch.StartNew();
        Simulation.Begin(w, s);
        Info($"Init {sw.Elapsed.TotalMilliseconds:F1} ms");
        FogInvariants(w, s);
        Check(Enumerable.Range(0, w.P).Where(p => s.Owner[p] == me).All(p => s.Fog[p] == 2), "every own province is visible");
        Check(w.Adj[cap].All(q => s.Fog[q] == 2), "the capital's neighbours are visible");
        int explored0 = Explored(s);
        var (tax0, upk0) = Rules.Budget(s);
        Info($"start budget: taxes +{tax0:F1}, upkeep −{upk0:F1} per year; population {Enumerable.Range(0, w.P).Where(p => s.Owner[p] == me).Sum(p => s.Pop[p]):F0}");
        Check(explored0 > 0 && explored0 < w.P, $"explored at start: {explored0} of {w.P}");
        Check(Enumerable.Range(0, w.P).Where(p => s.Fog[p] == 1).Any(), "a stale ring exists (explored, not visible)");
        MetInvariant(w, s);
        var fog = new FogWatch(w, s);

        // ------------------------------------------------------------ vision timing
        sw.Restart();
        for (int k = 0; k < 200; k++) FogOfWar.Recompute(w, s);
        Info($"Recompute avg {sw.Elapsed.TotalMilliseconds / 200:F3} ms");
        Check(fog.Verify(rec, "idle recompute"), "recompute without changes reports nothing");

        // ------------------------------------------------------------ observer mode
        Section("observer mode");
        s.FogEnabled = false;
        Check(Rules.Leaderboard(s).Count == s.NationCapital.Length && Rules.UnmetCount(s) == 0, "observer: everyone listed");
        s.FogEnabled = true;
        Check(Rules.Leaderboard(s).All(r => s.Met[r.nation]), "fog on: leaderboard lists met nations only");

        // ------------------------------------------------------------ scout errors
        Section("scouts: sending");
        int sea = Enumerable.Range(0, w.P).First(p => w.PLand[p] == 0);
        Check(Scouts.Send(w, s, sea, rec, out _) == ScoutError.Sea, "sea target refused");
        Check(Scouts.Send(w, s, cap, rec, out _) == ScoutError.Here, "capital target refused");
        var reach = LandDist(w, cap);
        int island = Enumerable.Range(0, w.P).FirstOrDefault(p => w.PLand[p] == 1 && reach[p] < 0, -1);
        if (island >= 0) Check(Scouts.Send(w, s, island, rec, out _) == ScoutError.Far, "unreachable land (another continent) refused");
        else Info("no second landmass in this world — Far not tested");
        Check(s.Scouts.Count == 0, "failed sends create no party");

        // manual: farthest unexplored land reachable within 25 steps
        int target = Enumerable.Range(0, w.P).Where(p => w.PLand[p] == 1 && reach[p] > 0 && reach[p] <= 25 && !s.Explored[p])
                               .OrderByDescending(p => reach[p]).ThenBy(p => p).FirstOrDefault(-1);
        Check(target >= 0, "an unexplored reachable target exists");
        Check(Scouts.Send(w, s, target, rec, out var manual) == ScoutError.None, $"manual party sent to {Name(w, target)} ({reach[target]} steps)");
        Check(manual.Path[0] == cap && manual.Path[^1] == target && manual.Path.All(p => w.PLand[p] == 1), "manual path: capital → target over land");
        Check(PathIsConnected(w, manual.Path), "manual path is connected");
        Check(Scouts.Send(w, s, -1, rec, out var auto) == ScoutError.None, "auto party sent");
        Check(auto.Path[0] == cap && !s.Explored[auto.Path[^1]], "auto party heads for unexplored land");
        Check(Scouts.Send(w, s, -1, rec, out _) == ScoutError.Max, "third party refused (max 2)");
        Check(fog.Verify(rec, "send"), "fog changes on send are reported");

        // ------------------------------------------------------------ scouts over time
        Section("scouts: walking");
        int frames = 0, steps = 0, overBudget = 0; double advMs = 0;
        var exploredBefore = (bool[])s.Explored.Clone();
        int arrivals = rec.Notes.Count(n => n.icon == "map-2");
        while (s.Scouts.Count > 0 && frames < 200_000)
        {
            var t0 = Stopwatch.GetTimestamp();
            var tick = Scouts.Advance(w, s, 3, rec);          // 3 sub-steps ≈ one 60 fps frame at speed 3
            advMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
            frames++; steps += tick.Steps;
            foreach (var sc in s.Scouts)
            {
                if (sc.Auto && sc.Steps > sc.MaxSteps) overBudget++;
                if (!(sc.Progress >= 0 && sc.Progress < 1 && sc.Step >= 0 && sc.Step < sc.Path.Length)) { Check(false, $"scout {sc.Id} position valid"); goto walked; }
            }
            if (tick.Any)
            {
                if (!Monotonic(exploredBefore, s.Explored)) { Check(false, "explored memory never shrinks"); goto walked; }
                if (!fog.Verify(rec, "scout step")) { Check(false, "every fog change during scouting is reported"); goto walked; }
                if (!FogInvariants(w, s, quiet: true)) { Check(false, "fog invariants hold while scouting"); goto walked; }
            }
        }
        walked:
        Info($"{frames} frames, {steps} steps, Advance avg {advMs / Math.Max(1, frames) * 1000:F1} µs/frame");
        Check(s.Scouts.Count == 0, $"both parties came back (after {frames} frames)");
        Check(overBudget == 0, $"auto party stays within its budget (march to the frontier + {Scouts.AutoSteps})");
        var back = rec.Notes.Where(n => n.icon == "map-2").Skip(arrivals).ToList();
        Check(back.Count == 2, "two «вернулись с картами» notes");
        Check(back.Any(n => n.text.Contains(w.PName[target])), "manual party's note names its target");
        foreach (var n in back) Info("note: " + n.text);
        int explored1 = Explored(s);
        Check(explored1 > explored0, $"scouting explored new land: {explored0} → {explored1}");
        Check(s.Fog[target] == 1 || IsOwnVision(w, s, target), "target is remembered (stale) after the party left");
        FogInvariants(w, s);
        MetInvariant(w, s);

        // ------------------------------------------------------------ batching
        Section("scouts: batching");
        var runs = new List<string>();
        foreach (int batch in new[] { 1, 7, 26 })
        {
            var t = NationGen.CreateInitialState(w);
            Simulation.Begin(w, t);
            Scouts.Send(w, t, -1, null, out _);
            Scouts.Advance(w, t, 39, null);                  // the second party starts 39 sub-steps later
            Scouts.Send(w, t, -1, null, out _);
            for (int k = 0; k < 4000 && t.Scouts.Count > 0; k++) Scouts.Advance(w, t, batch, null);
            runs.Add($"{Explored(t)}/{string.Join(",", t.Fog.Select(f => (int)f).Sum())}");
        }
        Check(runs.Distinct().Count() == 1, $"the same walk whatever the frame batching (explored/fog: {string.Join(" | ", runs)})");

        // ------------------------------------------------------------ auto exploration until done
        Section("scouts: auto until the continent is mapped");
        int rounds = 0, last = Explored(s);
        ScoutError err = ScoutError.None;
        while (rounds < 5000)
        {
            err = Scouts.Send(w, s, -1, rec, out _);
            if (err != ScoutError.None) break;
            while (s.Scouts.Count > 0) Scouts.Advance(w, s, Scouts.SubSteps, rec);
            rounds++;
        }
        Check(err == ScoutError.NoTargets, $"auto parties eventually run out of targets ({rounds} trips, result {err})");
        Check(Enumerable.Range(0, w.P).All(p => w.PLand[p] == 0 || reach[p] < 0 || s.Explored[p]), "all land reachable from the capital is explored");
        Check(fog.Verify(rec, "auto rounds"), "fog changes of auto rounds are reported");
        Info($"explored {last} → {Explored(s)}; met {s.Met.Count(m => m)} of {s.Met.Length}");
        MetInvariant(w, s);

        // ------------------------------------------------------------ claims
        Section("claims");
        int claim = Enumerable.Range(0, w.P).FirstOrDefault(p => Rules.CheckClaim(w, s, p) == ClaimError.None, -1);
        Check(claim >= 0, "a claimable border province exists");
        double gold = s.Gold;
        Rules.Claim(w, s, claim); FogOfWar.Refresh(w, s, rec);
        Check(s.Owner[claim] == me && Math.Abs(s.Gold - (gold - Rules.ClaimCost)) < 1e-9, "claim: owner set, 120 gold paid");
        Check(s.Religion[claim] == Data.Nations[me].Religion, "claim: state religion");
        Check(s.Fog[claim] == 2 && w.Adj[claim].Where(q => w.PLand[q] == 1).All(q => s.Fog[q] == 2), "claim: new border is watched");
        Check(fog.Verify(rec, "claim"), "claim: fog changes reported");
        Check(Rules.CheckClaim(w, s, claim) == ClaimError.Owned, "cannot claim twice");
        Check(Rules.CheckClaim(w, s, sea) == ClaimError.NotLand, "cannot claim sea");
        int far = Enumerable.Range(0, w.P).FirstOrDefault(p => w.PLand[p] == 1 && s.Owner[p] < 0 && s.Explored[p] && !Rules.Borders(w, s, p, me), -1);
        if (far >= 0) Check(Rules.CheckClaim(w, s, far) == ClaimError.NotAdjacent, "cannot claim land away from the border");
        int next = Enumerable.Range(0, w.P).FirstOrDefault(p => Rules.CheckClaim(w, s, p) == ClaimError.None, -1);
        if (next >= 0) { s.Gold = 50; Check(Rules.CheckClaim(w, s, next) == ClaimError.NoGold, "cannot claim without gold"); s.Gold = 1000; }

        // ------------------------------------------------------------ buildings
        Section("buildings");
        int bp = Enumerable.Range(0, w.P).Where(p => s.Owner[p] == me && s.Buildings[p].Count < s.Slots[p]).OrderBy(p => p).FirstOrDefault(-1);
        Check(bp >= 0, "an own province with a free plot");
        var opts = Rules.BuildOptions(w, s, bp);
        Check(opts.Count > 0 && opts.All(b => !s.Buildings[bp].Contains(b)), $"options offered: {string.Join(", ", opts.Select(b => Data.BldName[(int)b]))}");
        Check(opts.All(b => TerrainAllows(w, bp, b)), "options follow the terrain rules");
        s.Gold = 10_000;
        while (Rules.BuildOptions(w, s, bp) is { Count: > 0 } o)
        {
            double g0 = s.Gold;
            Check(Rules.CheckBuild(w, s, bp, o[0]) == BuildError.None, $"can build {Data.BldName[(int)o[0]]}", quietPass: true);
            Rules.Build(s, bp, o[0]);
            Check(s.Gold == g0 - Rules.BuildCost(o[0]), "building costs gold", quietPass: true);
        }
        Check(s.Buildings[bp].Count == s.Slots[bp] || opts.Count < s.Slots[bp], "plots filled");
        var any = Enum.GetValues<Data.Bld>().First(b => TerrainAllows(w, bp, b));
        Check(Rules.CheckBuild(w, s, bp, any) is BuildError.NoSlot or BuildError.AlreadyBuilt, "no building beyond the plots");
        int foreign = Enumerable.Range(0, w.P).First(p => s.Owner[p] > 0);
        Check(Rules.BuildOptions(w, s, foreign).Count == 0 && Rules.CheckBuild(w, s, foreign, Data.Bld.Granary) == BuildError.NotOwned, "no building abroad");

        // ------------------------------------------------------------ geology
        Section("geology");
        int gp = Enumerable.Range(0, w.P).FirstOrDefault(p => s.Owner[p] == me && !s.OreFound[p], -1);
        if (gp >= 0)
        {
            Check(Rules.CheckSurvey(s, gp) == SurveyError.None, "survey allowed");
            Rules.Survey(s, gp);
            Check(s.OreFound[gp] && Rules.CheckSurvey(s, gp) == SurveyError.AlreadyDone, "surveyed once");
        }
        Check(Rules.CheckSurvey(s, foreign) == SurveyError.NotOwned, "no geologists abroad");

        // ------------------------------------------------------------ leaderboard
        Section("leaderboard");
        var lb = Rules.Leaderboard(s);
        Check(lb.Any(r => r.nation == me), "player listed");
        Check(lb.All(r => s.Met[r.nation]), "only met nations");
        Check(lb.Zip(lb.Skip(1)).All(z => z.First.score >= z.Second.score), "sorted by score");
        Check(lb.Count + Rules.UnmetCount(s) == s.NationCapital.Length, $"listed {lb.Count} + unmet {Rules.UnmetCount(s)} = {s.NationCapital.Length}");
        if (verbose) foreach (var (n, sc) in lb.Take(5)) Info($"  {Data.Nations[n].Name,-16} {sc}");

        // ------------------------------------------------------------ centuries
        Section("years");
        var years = new List<int>();
        int owned0 = Enumerable.Range(0, w.P).Count(p => s.Owner[p] >= 0);
        int notes0 = rec.Notes.Count; double gold0 = s.Gold;
        sw.Restart();
        int ticks = 1400, badPop = -1;
        for (int k = 0; k < ticks; k++)
        {
            Simulation.Year(w, s, rec);
            years.Add(s.Year);
            if (k % 50 == 0 && !fog.Verify(rec, "year")) Check(false, "fog changes from bot claims are reported");
            if (badPop < 0)
                for (int p = 0; p < w.P; p++)
                    if (w.PLand[p] == 1 && (!float.IsFinite(s.Pop[p]) || s.Pop[p] < 0 || s.Pop[p] > Simulation.Capacity(w, s, p) * 2.5f)) { badPop = p; break; }
        }
        double yearMs = sw.Elapsed.TotalMilliseconds / ticks;
        Info($"{ticks} years in {sw.ElapsedMilliseconds} ms ({yearMs:F3} ms/year) → {GameState.YearText(s.Year)}");
        Check(!years.Contains(0), "there is no year 0");
        Check(years.Zip(years.Skip(1)).All(z => z.Second == z.First + 1 || (z.First == -1 && z.Second == 1)), "years advance by one (−1 → 1)");
        Check(badPop < 0, badPop < 0 ? "population finite and near capacity" : $"population of {Name(w, badPop)} = {s.Pop[badPop]}");
        Check(Enumerable.Range(0, w.P).All(p => s.Mood[p] <= 100), "mood within 0..100");
        Check(s.LastTaxes > 0 && s.LastUpkeep >= 0, $"budget: taxes {s.LastTaxes:F1}, upkeep {s.LastUpkeep:F1}");
        Check(s.Gold > gold0, $"treasury grew: {gold0:F0} → {s.Gold:F0}");
        var yearNotes = rec.Notes.Skip(notes0).ToList();
        int events = yearNotes.Count(n => n.icon is not ("hammer" or "flag" or "affiliate" or "map-2"));
        Check(events >= ticks / Simulation.EventEveryYears - 2, $"chronicle events fired: {events} (every {Simulation.EventEveryYears} years)");
        Check(yearNotes.All(n => !string.IsNullOrWhiteSpace(n.text) && !n.text.Contains("{")), "event texts are filled in");
        var cards = Enumerable.Range(0, Chronicle.Count).Select(k => Chronicle.CardAt(w.Seed, k)).ToList();
        Check(cards.Distinct().Count() == Chronicle.Count, "one cycle of the deck deals every event once");
        int owned1 = Enumerable.Range(0, w.P).Count(p => s.Owner[p] >= 0);
        Check(owned1 > owned0, $"bots expanded: {owned0} → {owned1} owned provinces");
        Check(rec.Notes.Any(n => n.icon == "hammer" && n.text.Contains("столиц", StringComparison.OrdinalIgnoreCase)), "capital projects complete");
        FogInvariants(w, s);
        MetInvariant(w, s);
        int hidden = Enumerable.Range(0, w.P).Count(p => s.Fog[p] == 1 && s.KnownOwner[p] != s.Owner[p]);
        Info($"stale provinces whose owner changed out of sight (the map still shows the old one): {hidden}");
        int projects = rec.Notes.Count(n => n.icon == "hammer" && Simulation.Projects.Any(pr => pr.DoneText == n.text));
        Check(projects <= Simulation.Projects.Length, $"each capital project is finished at most once ({projects} in {ticks} years)");
        if (verbose)
        {
            foreach (var n in yearNotes.Where(n => n.icon is not ("hammer" or "flag")).Take(12)) Info($"  [{n.icon}] {n.text}");
            Info($"  … {yearNotes.Count} notes in {ticks} years");
        }

        return Hash(w, s, rec);
    }

    // ================================================================ invariants

    static bool FogInvariants(WorldData w, GameState s, bool quiet = false)
    {
        bool ok = true;
        for (int p = 0; p < w.P && ok; p++)
            ok = s.Fog[p] <= 2 && (s.Fog[p] == 0) == !s.Explored[p];
        if (!quiet) Check(ok, "fog: states 0/1/2, unexplored ⇔ state 0");
        return ok;
    }

    static void MetInvariant(WorldData w, GameState s)
    {
        var expect = new bool[s.Met.Length];
        for (int p = 0; p < w.P; p++) if (s.Explored[p] && s.KnownOwner[p] >= 0) expect[s.KnownOwner[p]] = true;
        // Met is monotonic, so a nation may stay met after losing its explored provinces
        Check(Enumerable.Range(0, expect.Length).All(n => !expect[n] || s.Met[n]), $"met ⊇ known owners of explored land ({s.Met.Count(m => m)} met)");
        Check(s.Met[GameState.LocalPlayer], "the player has met itself");
        // the map's memory: visible land shows its real owner; nothing unexplored has a remembered one
        Check(Enumerable.Range(0, w.P).All(p => s.Fog[p] != 2 || s.KnownOwner[p] == s.Owner[p]), "visible provinces show their real owner");
        Check(Enumerable.Range(0, w.P).All(p => s.Explored[p] || s.KnownOwner[p] < 0), "unexplored provinces have no remembered owner");
    }

    static bool Monotonic(bool[] before, bool[] now)
    {
        for (int p = 0; p < now.Length; p++) { if (before[p] && !now[p]) return false; before[p] = now[p]; }
        return true;
    }

    static bool IsOwnVision(WorldData w, GameState s, int p) => s.Fog[p] == 2;

    static bool TerrainAllows(WorldData w, int p, Data.Bld b)
    {
        var l = new List<Data.Bld>(); Rules.TerrainOptions(w, p, l); return l.Contains(b);
    }

    static bool PathIsConnected(WorldData w, int[] path)
    {
        for (int k = 1; k < path.Length; k++) if (!w.Adj[path[k - 1]].Contains(path[k])) return false;
        return true;
    }

    static int[] LandDist(WorldData w, int from)
    {
        var d = Enumerable.Repeat(-1, w.P).ToArray(); var q = new Queue<int>(); d[from] = 0; q.Enqueue(from);
        while (q.Count > 0) { int p = q.Dequeue(); foreach (int n in w.Adj[p]) if (d[n] < 0 && w.PLand[n] == 1) { d[n] = d[p] + 1; q.Enqueue(n); } }
        return d;
    }

    static int Explored(GameState s) => s.Explored.Count(e => e);
    static string Name(WorldData w, int p) => $"{w.PName[p]} #{p}";

    static ulong Hash(WorldData w, GameState s, Recorder rec)
    {
        ulong h = 1469598103934665603UL;
        void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
        Mix(s.Year); Mix(BitConverter.DoubleToInt64Bits(s.Gold));
        for (int p = 0; p < w.P; p++) { Mix(s.Owner[p]); Mix(BitConverter.SingleToInt32Bits(s.Pop[p])); Mix(s.Mood[p]); Mix(s.Fog[p]); Mix(s.Buildings[p].Count); }
        foreach (var n in rec.Notes) Mix(StableHash(n.text));
        return h;
    }

    static long StableHash(string t) { long h = 17; foreach (char c in t) h = h * 31 + c; return h; }

    // ================================================================ helpers

    static bool _verbose = true;

    static void Section(string name) { if (_verbose) Console.WriteLine($"\n== {name}"); }
    static void Info(string msg) { if (_verbose) Console.WriteLine("   " + msg); }

    static void Check(bool ok, string what, bool quietPass = false)
    {
        if (ok) { _pass++; if (_verbose && !quietPass) Console.WriteLine("   ok   " + what); }
        else { _fail++; Failures.Add(what); Console.WriteLine("   FAIL " + what); }
    }

    /// <summary>Records sink traffic like the Game autoload would forward it.</summary>
    sealed class Recorder : ISimSink
    {
        public readonly List<(string icon, string text)> Notes = new();
        public readonly List<IReadOnlyList<int>> Fog = new();
        public readonly List<IReadOnlyList<int>> Provinces = new();
        public void Notify(string icon, string text) => Notes.Add((icon, text));
        public void RaiseProvincesChanged(IReadOnlyList<int> ps) => Provinces.Add(ps);
        public void RaiseFogChanged(IReadOnlyList<int> ps) => Fog.Add(ps);
    }

    /// <summary>Checks that every fog state change since the last check was announced through RaiseFogChanged.</summary>
    sealed class FogWatch
    {
        readonly GameState _s; readonly byte[] _last;
        public FogWatch(WorldData w, GameState s) { _s = s; _last = (byte[])s.Fog.Clone(); }

        public bool Verify(Recorder rec, string when)
        {
            bool all = rec.Fog.Any(l => l == null);
            var told = new HashSet<int>(rec.Fog.Where(l => l != null).SelectMany(l => l));
            bool ok = true;
            for (int p = 0; p < _last.Length; p++)
            {
                if (_s.Fog[p] != _last[p] && !all && !told.Contains(p)) { ok = false; Console.WriteLine($"   unreported fog change at {p} ({when}): {_last[p]} → {_s.Fog[p]}"); break; }
                _last[p] = _s.Fog[p];
            }
            rec.Fog.Clear();
            return ok;
        }
    }
}
