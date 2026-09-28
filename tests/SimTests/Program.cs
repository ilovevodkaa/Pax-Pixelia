using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Tests;

/// <summary>
/// Console tests of the simulation (no Godot): the scripted game (fog, scouts, commands, economy, centuries of cycles),
/// the clock and calendar, determinism across tick batching and frame rates, journal replay, the nation roster,
/// saves (round trips at awkward moments, broken files), the determinism lint and a pacing check. Exit code 0 = all checks passed.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        int seed = 1337;
        bool pacing = false;
        foreach (var a in args)
        {
            var s = a.TrimStart('-');
            if (s.StartsWith("seed=")) seed = int.Parse(s[5..]);
            if (s == "pacing") pacing = true;
        }
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var sw = Stopwatch.StartNew();
        var w = WorldGen.Generate(seed, 2560, 1440);
        Console.WriteLine($"world seed={seed}: {w.P} provinces ({Enumerable.Range(0, w.P).Count(p => w.PLand[p] == 1)} land) in {sw.ElapsedMilliseconds} ms");

        if (args.Any(a => a.TrimStart('-') == "bench")) { Bench.Run(w); return 0; }
        if (args.Any(a => a.TrimStart('-') == "saves")) { SaveTests.Run(w); return Report(); }
        LintTests.Run();
        TimeTests.Run();
        var run1 = ScenarioTests.Run(w, verbose: true);
        T.Section("determinism: the scripted game twice");
        var run2 = ScenarioTests.Run(WorldGen.Generate(seed, 2560, 1440), verbose: false);
        T.Verbose = true;
        T.Check(run1 == run2, $"same seed, same commands → same state hash ({run1:X16} vs {run2:X16})");
        ReplayTests.Run(w);
        SaveTests.Run(w);
        RosterTests.Run(w);
        CityTests.Run(w);
        MaterialTests.Run(w);
        PacingTests.Run(w, full: pacing);

        return Report();
    }

    static int Report()
    {
        Console.WriteLine();
        Console.WriteLine($"{T.Pass} passed, {T.Fail} failed");
        foreach (var f in T.Failures) Console.WriteLine("  FAIL " + f);
        return T.Fail == 0 ? 0 : 1;
    }
}

/// <summary>Check / report helpers shared by the test groups.</summary>
public static class T
{
    public static int Pass, Fail;
    public static readonly List<string> Failures = new();
    public static bool Verbose = true;

    public static void Section(string name) { if (Verbose) Console.WriteLine($"\n== {name}"); }
    public static void Info(string msg) { if (Verbose) Console.WriteLine("   " + msg); }

    public static bool Check(bool ok, string what, bool quietPass = false)
    {
        if (ok) { Pass++; if (Verbose && !quietPass) Console.WriteLine("   ok   " + what); }
        else { Fail++; Failures.Add(what); Console.WriteLine("   FAIL " + what); }
        return ok;
    }
}

/// <summary>Records sink traffic like the Game autoload would forward it.</summary>
public sealed class Recorder : ISimSink
{
    public readonly List<(string icon, string text)> Notes = new();
    public readonly List<IReadOnlyList<int>> Fog = new();
    public readonly List<IReadOnlyList<int>> Provinces = new();
    public void Notify(string icon, string text) => Notes.Add((icon, text));
    public void RaiseProvincesChanged(IReadOnlyList<int> ps) => Provinces.Add(ps);
    public void RaiseFogChanged(IReadOnlyList<int> ps) => Fog.Add(ps);

    public ulong NotesHash()
    {
        var h = new Fnv();
        foreach (var (i, t) in Notes) { foreach (char c in i) h.Add((long)c); foreach (char c in t) h.Add((long)c); h.Add(-1L); }
        return h.Value;
    }
}

/// <summary>Checks that every fog state change of a nation since the last check was announced through RaiseFogChanged.</summary>
public sealed class FogWatch
{
    readonly byte[] _fog, _last;
    public FogWatch(GameState s, int n) { _fog = s.Nat[n].Fog.Fog; _last = (byte[])_fog.Clone(); }

    public bool Verify(Recorder rec, string when)
    {
        bool all = rec.Fog.Any(l => l == null);
        var told = new HashSet<int>(rec.Fog.Where(l => l != null).SelectMany(l => l));
        bool ok = true;
        for (int p = 0; p < _last.Length; p++)
        {
            if (_fog[p] != _last[p] && !all && !told.Contains(p)) { ok = false; Console.WriteLine($"   unreported fog change at {p} ({when}): {_last[p]} → {_fog[p]}"); break; }
            _last[p] = _fog[p];
        }
        rec.Fog.Clear();
        return ok;
    }
}
