using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using PaxPixelia.Content;

namespace PaxPixelia.Tests;

/// <summary>
/// ContentCheck: validates game/data (ids, references, texts, weights, tone rules), proves the validator catches
/// broken packs, checks the runner's contract (determinism, choices, jokes off, no allocations between beats) and
/// dry-runs a whole game — nomads to the Future, 25 h at speed 3, ≈5700 calendar years — for four scripted nations,
/// printing a histogram of fired events and the pacing per era. Exit code 0 = all checks passed.
/// </summary>
public static class Program
{
    static int _pass, _fail;
    static readonly List<string> Failures = new();
    static bool _verbose;

    static readonly HashSet<string> MvpSystems = new() { "religion", "trade_routes" };

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        int seed = 1337, pace = 1000;
        foreach (var raw in args)
        {
            var a = raw.TrimStart('-');
            if (a.StartsWith("seed=")) seed = int.Parse(a[5..]);
            else if (a.StartsWith("pace=")) pace = int.Parse(a[5..]);
            else if (a == "verbose") _verbose = true;
        }
        string dataDir = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(m => m.Key == "DataDir").Value;
        dataDir = Path.GetFullPath(dataDir);

        Section($"load {dataDir}");
        var sw = Stopwatch.StartNew();
        var db = ContentDb.LoadDirectory(dataDir);
        Info($"{db.Events.Length} events in {db.EventDefs.Select(e => e.File).Distinct().Count()} files, {db.Flags.Count} flags " +
             $"({db.SystemFlagCount} set by the Sim), {db.Threads.Length} threads, {db.Traits.Count} leader traits, {db.Dogmas.Count} dogmas, " +
             $"{db.Wonders.Lines.Sum(l => l.Tiers.Count) + db.Wonders.Singles.Count + db.Wonders.Parodies.Count} wonders — {sw.ElapsedMilliseconds} ms");
        foreach (var g in db.Events.GroupBy(e => e.Def.File).OrderBy(g => g.Key))
            Info($"  {g.Key,-26} {g.Count(),3}: {g.Count(e => e.Tone == Tone.Serious)} serious, {g.Count(e => e.Tone == Tone.Joke)} jokes, " +
                 $"{g.Count(e => e.IsChoice)} with a choice");

        Section("ContentCheck");
        var rep = ContentCheck.Run(db);
        foreach (var e in rep.Errors) Console.WriteLine("  ERROR " + e);
        foreach (var w in rep.Warnings) Console.WriteLine("  warn  " + w);
        Check(rep.Ok, $"content pack is valid ({rep.Errors.Count} errors, {rep.Warnings.Count} warnings)");
        int serious = db.Events.Count(e => e.Tone == Tone.Serious), jokes = db.Events.Count(e => e.Tone == Tone.Joke);
        Info($"deck: {serious} serious, {jokes} jokes (jokes are one-shots with weight 30–60, so they fire far less often)");
        if (!rep.Ok) return Finish();

        BrokenPack();
        RunnerContract(db, seed);

        Section($"dry run: whole game, all systems, pace {pace}‰");
        var full = DryRun(db, seed, pace, null, jokePercent: 100);
        PrintRun(db, full, detailed: true);
        PacingChecks(db, full);

        Section("dry run: MVP systems only (religion, trade routes)");
        var mvp = DryRun(db, seed, pace, MvpSystems, jokePercent: 100);
        PrintRun(db, mvp, detailed: false);
        Check(mvp.AvgGapSeconds(0) <= 120, $"MVP build still gets an event every {mvp.AvgGapSeconds(0):F0} s on average (≤ 120 s)");

        Section("determinism");
        var again = DryRun(db, seed, pace, null, jokePercent: 100);
        Check(again.Hash == full.Hash, $"same seed → same events ({full.Hash:X16} vs {again.Hash:X16})");
        var other = DryRun(db, seed + 1, pace, null, jokePercent: 100);
        Check(other.Hash != full.Hash, "another seed → another history");

        return Finish();
    }

    // ------------------------------------------------------------------ validator self-test

    static void BrokenPack()
    {
        Section("ContentCheck catches broken content");
        const string deck = "{ \"icons\": [\"feather\"], \"systems\": [\"space\"], \"buildings\": [\"farm\"], \"axes\": [\"open\"], \"denylist\": [\"христ\"] }";
        const string events = """
        { "events": [
          { "id": "a", "title": "А", "text": "Текст", "weight": 0, "when": { "flags": ["no.such.flag"] } },
          { "id": "a", "title": "Б", "text": "" },
          { "id": "c", "kind": "choice", "title": "В", "text": "{province} горит",
            "options": [ { "text": "Раз", "roll": [ { "permille": 300, "result": "x" }, { "permille": 300, "result": "y" } ] },
                         { "text": "Два", "when": { "requires": ["warp_drive"] } } ] },
          { "id": "d", "tone": "joke", "title": "Г", "text": "Шутка без справки", "effects": { "followUp": { "event": "zzz", "after": 5 } } },
          { "id": "e", "title": "Д", "text": "Христианский текст", "typo": 1 }
        ] }
        """;
        var db = ContentDb.Load(new[] { new ContentSource("core/deck.json", deck), new ContentSource("events/bad.json", events) });
        var rep = ContentCheck.Run(db);
        if (_verbose) foreach (var e in rep.Errors) Console.WriteLine("    " + e);
        bool Has(string s) => rep.Errors.Any(e => e.Contains(s));
        Check(Has("unknown member") || Has("typo"), "a misspelt key is an error (the whole file is rejected)");
        var db2 = ContentDb.Load(new[] { new ContentSource("core/deck.json", deck), new ContentSource("events/bad.json", events.Replace(", \"typo\": 1", "")) });
        var rep2 = ContentCheck.Run(db2);
        if (_verbose) foreach (var e in rep2.Errors) Console.WriteLine("    " + e);
        bool Has2(string s) => rep2.Errors.Any(e => e.Contains(s));
        Check(Has2("duplicate event id «a»"), "duplicate ids");
        Check(Has2("flag «no.such.flag» is never set"), "unknown flags");
        Check(Has2("text is empty"), "empty texts");
        Check(Has2("weight must be > 0"), "zero weights");
        Check(Has2("sum to 1000"), "roll chances that do not add up");
        Check(Has2("{province} needs subject"), "placeholders without a subject");
        Check(Has2("unknown system «warp_drive»"), "unknown systems in requires");
        Check(Has2("needs «lore»") && Has2("fires once"), "jokes without a note or not one-shot");
        Check(Has2("follow-up event «zzz» does not exist"), "dangling follow-ups");
        Check(Has2("(tone rule 3)"), "denylisted words (real faiths, brands, modern politicians)");
    }

    // ------------------------------------------------------------------ runner contract

    static void RunnerContract(ContentDb db, int seed)
    {
        Section("runner contract");
        var sys = new HashSet<string>(db.Deck.Systems);
        var t = new Timeline(1000);

        // Choices: a human gets a window, a wrong option is refused, the deadline takes option 1.
        var runner = new EventRunner(db, seed);
        var n = new FakeNation(0, "Ардания", Terrain.Coast | Terrain.River | Terrain.Plains, 0, sys, seed);
        var m = runner.NewMemory(0, bot: false);
        long tick = 0;
        n.Advance(tick, t, runner, m);
        while (n.Open == null && tick < 20000) { runner.Tick(m, n, n, tick); tick++; if (tick % 1200 == 0) n.Advance(tick, t, runner, m); }
        Check(n.Open != null && m.Pending != null, $"a choice window opens for the human player (tick {tick})");
        if (n.Open is { } open)
        {
            Check(!runner.Choose(m, n, n, tick, 7), "an option that does not exist is refused");
            int logBefore = n.Log.Count;
            long deadline = open.Deadline;
            while (tick <= deadline) { runner.Tick(m, n, n, tick); tick++; }
            var last = n.Log.Count > logBefore ? n.Log[^1] : default;
            Check(m.Pending == null && n.Log.Count > logBefore && last.Event == open.Event && last.Option == 0,
                  "on the deadline the safe option 1 is taken");
        }

        // Hidden options are offered only with their flag.
        int money = db.EventIndex("money_no_smell");
        var m2 = runner.NewMemory(1, bot: true);
        var n2 = new FakeNation(1, "Торн", Terrain.Plains, 0, sys, seed);
        Check(money >= 0 && !runner.IsOffered(m2, n2, money, 2), "a hidden option is not offered without its flag");
        runner.SetFlag(m2, "trait.stingy");
        Check(money >= 0 && runner.IsOffered(m2, n2, money, 2), "…and is offered once the ruler is stingy");

        // Texts: placeholders are resolved by the caller (it knows names and Russian cases).
        string text = EventText.Format("Караван из {foreign:gen} пришёл в провинцию {province}.", (name, form) => name == "foreign" ? (form == "gen" ? "Торна" : "Торн") : "Белоград");
        Check(text == "Караван из Торна пришёл в провинцию Белоград.", $"EventText.Format fills placeholders: «{text}»");

        // Serious chronicler: no jokes at all.
        var serious = DryRun(db, seed, 1000, null, jokePercent: 0);
        int jokesFired = serious.Nations.Sum(x => x.Log.Count(r => db.Events[r.Event].Tone == Tone.Joke));
        Check(jokesFired == 0, $"«Летописец: Серьёзный» deals no jokes ({jokesFired})");

        // Between beats Tick must not allocate: it runs for every nation on every sim tick.
        var r2 = new EventRunner(db, seed);
        var m3 = r2.NewMemory(2, bot: true);
        var n3 = new FakeNation(2, "Ун", Terrain.Steppe, 0, sys, seed);
        n3.Advance(0, t, r2, m3);
        m3.NextDue = long.MaxValue / 2;
        m3.FlagsDirty = false;
        for (long k = 1; k < 200_000; k++) r2.Tick(m3, n3, n3, k);   // let the JIT settle (tiering allocates)
        var clock = Stopwatch.StartNew();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (long k = 200_000; k < 1_200_000; k++) r2.Tick(m3, n3, n3, k);
        double ns = clock.Elapsed.TotalMilliseconds * 1e6 / 1_000_000;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(bytes == 0, $"Tick between beats allocates nothing ({bytes} B over 1M ticks, {ns:F1} ns per tick)");

        // Cost of a beat: dealing one card from the whole deck.
        var m4 = r2.NewMemory(3, bot: true);
        var n4 = new FakeNation(3, "Лура", Terrain.Coast | Terrain.Mountain | Terrain.River, 0, sys, seed);
        n4.Advance(t.EraStart(3), t, r2, m4);
        clock.Restart();
        int beats = 0;
        for (long k = t.EraStart(3); beats < 2000; k += 200) { m4.NextDue = k; r2.Tick(m4, n4, n4, k); beats++; }
        Info($"a beat (deal one card among {db.Events.Length}) costs {clock.Elapsed.TotalMilliseconds * 1000 / beats:F1} µs");
    }

    // ------------------------------------------------------------------ dry run

    sealed class Run
    {
        public FakeNation[] Nations;
        public EventMemory[] Memories;
        public Timeline Time;
        public ulong Hash;
        public int WorldFired, Answered;
        public double AvgGapSeconds(int n) => Nations[n].Log.Count < 2 ? double.PositiveInfinity
            : (double)(Nations[n].Log[^1].Tick - Nations[n].Log[0].Tick) / (Nations[n].Log.Count - 1) / 2.0;
    }

    static Run DryRun(ContentDb db, int seed, int pace, HashSet<string> systems, int jokePercent)
    {
        var sys = systems ?? new HashSet<string>(db.Deck.Systems);
        var t = new Timeline(pace);
        var runner = new EventRunner(db, seed) { JokePercent = jokePercent };
        var run = new Run
        {
            Time = t,
            Nations = new[]
            {
                new FakeNation(0, "Ардания (игрок)", Terrain.Coast | Terrain.River | Terrain.Plains | Terrain.Hills | Terrain.Forest, 0, sys, seed),
                new FakeNation(1, "Скаллия", Terrain.Tundra | Terrain.Forest | Terrain.Coast | Terrain.Island | Terrain.Lake, 0, sys, seed),
                new FakeNation(2, "Бразан", Terrain.Steppe | Terrain.Desert | Terrain.Savanna | Terrain.Mountain, 1, sys, seed),
                new FakeNation(3, "Гошар (отстающий)", Terrain.Jungle | Terrain.River | Terrain.Swamp | Terrain.Plains, 3, sys, seed),
            },
        };
        run.Memories = run.Nations.Select(n => runner.NewMemory(n.Id, bot: n.Id != 0)).ToArray();
        var world = db.Events.Where(e => e.Kind == EventKind.World).ToArray();
        long nextWorld = Timeline.CyclesPerHour / 2;

        for (long tick = 0; tick < t.Total; tick++)
        {
            if (tick % 1200 == 0)
                for (int i = 0; i < run.Nations.Length; i++) run.Nations[i].Advance(tick, t, runner, run.Memories[i]);

            // The world system: every ~50 min one world event for everybody (only where its conditions hold).
            if (tick >= nextWorld && world.Length > 0)
            {
                var e = world[ContentRng.Below(ContentRng.Hash(seed, -2, tick, 1), world.Length)];
                for (int i = 0; i < run.Nations.Length; i++)
                    if (run.Nations[i].Era >= e.EraMin && run.Nations[i].Era <= e.EraMax && runner.Force(run.Memories[i], run.Nations[i], run.Nations[i], tick, e.Index))
                        run.WorldFired++;
                nextWorld = tick + ContentRng.Between(ContentRng.Hash(seed, -2, tick, 2), 5000, 7000);
            }

            for (int i = 0; i < run.Nations.Length; i++) runner.Tick(run.Memories[i], run.Nations[i], run.Nations[i], tick);

            // The human answers after 10–80 s; answers later than the 60 s deadline are too late (advisor took option 1).
            var me = run.Nations[0];
            if (me.Open is { } open)
            {
                if (me.AnswerAt <= open.Tick) me.AnswerAt = open.Tick + ContentRng.Between(ContentRng.Hash(seed, 0, open.Tick, 3), 20, 160);
                if (tick == me.AnswerAt)
                {
                    int opts = db.Events[open.Event].Options.Length;
                    int pick = ContentRng.Below(ContentRng.Hash(seed, 0, tick, 4), opts);
                    if (runner.Choose(run.Memories[0], me, me, tick, pick)) run.Answered++;
                    else if (runner.Choose(run.Memories[0], me, me, tick, 0)) run.Answered++;
                }
            }
        }

        ulong h = 1469598103934665603UL;
        foreach (var n in run.Nations)
            foreach (var r in n.Log)
                h = (h ^ (ulong)(r.Tick * 31 + r.Event * 7 + r.Option * 3 + r.Branch + r.Province * 13 + r.Foreign)) * 1099511628211UL;
        run.Hash = h;
        return run;
    }

    static void PrintRun(ContentDb db, Run run, bool detailed)
    {
        var t = run.Time;
        Info($"{t.Total / (double)Timeline.CyclesPerHour:F1} h at speed 3, {t.Total} cycles, calendar {t.YearAt(0)} … {t.YearAt(t.Total - 1)} " +
             $"({t.YearAt(t.Total - 1) - t.YearAt(0)} years); world events fired {run.WorldFired}");
        foreach (var n in run.Nations)
        {
            int j = n.Log.Count(r => db.Events[r.Event].Tone == Tone.Joke), c = n.Log.Count(r => db.Events[r.Event].IsChoice);
            Info($"  {n.Name,-20} {n.Log.Count,5} events, every {run.AvgGapSeconds(n.Id),5:F0} s; {c,4} choices, {j,3} jokes; " +
                 $"gold {n.Gold,6}, mood {n.Mood,3}, science {n.Science,5}, culture {n.Culture,5}");
        }
        if (!detailed) return;

        Console.WriteLine();
        Info("pacing of the player (Ардания) by era:");
        Info("  era  hours  events  every(s)  choices  jokes  distinct");
        var me = run.Nations[0];
        for (int e = 0; e <= 10; e++)
        {
            var inEra = me.Log.Where(r => t.EraAt(r.Tick) == e).ToList();
            double hours = t.EraLength(e) / (double)Timeline.CyclesPerHour;
            double gap = inEra.Count == 0 ? double.PositiveInfinity : t.EraLength(e) / 2.0 / inEra.Count;
            Info($"  E{e,-3} {hours,5:F1}  {inEra.Count,6}  {gap,8:F0}  {inEra.Count(r => db.Events[r.Event].IsChoice),7}  " +
                 $"{inEra.Count(r => db.Events[r.Event].Tone == Tone.Joke),5}  {inEra.Select(r => r.Event).Distinct().Count(),8}");
        }

        Console.WriteLine();
        Info("histogram (all four nations; # = 10 fires):");
        var counts = new int[db.Events.Length];
        foreach (var n in run.Nations) foreach (var r in n.Log) counts[r.Event]++;
        foreach (var e in db.Events.OrderByDescending(e => counts[e.Index]).ThenBy(e => e.Def.Id))
        {
            if (counts[e.Index] == 0) continue;
            string bar = new string('#', Math.Min(60, (counts[e.Index] + 5) / 10));
            Info($"  {e.Def.Id,-24} {(e.Tone == Tone.Joke ? "шутка" : ""),-5} E{e.EraMin}–{e.EraMax,-2} {counts[e.Index],5} {bar}");
        }
        var never = db.Events.Where(e => counts[e.Index] == 0).Select(e => e.Def.Id).ToList();
        Info($"never fired ({never.Count}): {string.Join(", ", never)}");
    }

    static void PacingChecks(ContentDb db, Run run)
    {
        var t = run.Time;
        double gap = run.AvgGapSeconds(0);
        Check(gap is >= 45 and <= 90, $"the player gets an event every {gap:F0} s on average (target 45–90 s at speed 3)");
        var me = run.Nations[0];
        var slow = new List<string>();
        for (int e = 0; e <= 10; e++)
        {
            int k = me.Log.Count(r => t.EraAt(r.Tick) == e);
            double g = k == 0 ? double.PositiveInfinity : t.EraLength(e) / 2.0 / k;
            if (g > 120) slow.Add($"E{e} {g:F0} s");
        }
        Check(slow.Count == 0, "no era is quieter than one event per 2 minutes" + (slow.Count > 0 ? ": " + string.Join(", ", slow) : ""));
        int all = run.Nations.Sum(n => n.Log.Count);
        int jokes = run.Nations.Sum(n => n.Log.Count(r => db.Events[r.Event].Tone == Tone.Joke));
        double ratio = (all - jokes) / (double)Math.Max(1, jokes);
        Check(ratio >= 3, $"serious events outnumber jokes {ratio:F1}× (tone rule 6: ≈3× or more)");
        var eggs = db.Events.Where(e => e.Tone == Tone.Joke && e.Once).Select(e => e.Index).ToHashSet();
        bool twice = run.Nations.Any(n => n.Log.GroupBy(r => r.Event).Any(g => eggs.Contains(g.Key) && g.Count() > 1));
        Check(!twice, "no one-shot joke fires twice for a nation");
        int threadStages = run.Nations.Sum(n => n.Log.Count(r => db.Events[r.Event].Thread >= 0));
        Check(threadStages > 0, $"memory threads advance ({threadStages} stages over four nations)");
        Info($"the player answered {run.Answered} choices in time; the advisor took option 1 for the rest");
    }

    // ------------------------------------------------------------------ output

    static int Finish()
    {
        Console.WriteLine();
        Console.WriteLine($"{_pass} passed, {_fail} failed");
        foreach (var f in Failures) Console.WriteLine("  FAIL " + f);
        return _fail == 0 ? 0 : 1;
    }

    static void Section(string name) { Console.WriteLine(); Console.WriteLine("== " + name); }
    static void Info(string s) => Console.WriteLine("   " + s);

    static void Check(bool ok, string what)
    {
        if (ok) _pass++; else { _fail++; Failures.Add(what); }
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")} {what}");
    }
}
