using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.Dev;

/// <summary>
/// --perf: measures the real game with vsync off and prints a report, then quits.
///   generation time (per stage), frame times while panning at ×1 / ×3 / ×8 (avg, p95, p99, worst, fps),
///   a fog-heavy phase (two auto scouts at speed 5), draw calls and memory (managed heap, Godot static, VRAM, process),
///   and a headless benchmark of the rules: µs per tick and per rules-cycle tick, worst tick, bytes allocated per tick.
///   --perf-seconds=N   seconds measured per phase (default 4)
/// </summary>
public partial class PerfProbe : Node
{
    readonly Main _main;
    readonly double _phase = Cli.Float("perf-seconds", 4);
    readonly List<double> _frames = new(4096);
    bool _recording;
    float _panSpeed;   // screen px per second
    Vector2 _center;

    public PerfProbe(Main main) => _main = main;

    public override void _Ready()
    {
        Name = "PerfProbe";
        ProcessPriority = -200;   // pan before the camera applies the view
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Run();
    }

    public override void _Process(double delta)
    {
        if (!_recording) return;
        _frames.Add(delta * 1000);
        if (_panSpeed != 0 && Game.I.IsReady)
        {
            // own float centre: the camera snaps its origin to whole pixels at rest, so re-reading CameraRect
            // every frame would swallow sub-pixel steps at thousands of fps
            _center.X += _panSpeed * (float)delta / Math.Max(.5f, Game.I.ZoomLevel);
            _main.Camera.CenterOn(_center, glide: false);
        }
    }

    async void Run()
    {
        var g = Game.I;
        while (!g.IsReady) await Frames(1);
        var w = g.World;
        GD.Print($"perf: window {GetViewport().GetVisibleRect().Size}, world {w.W}×{w.H}, {w.P} provinces");
        GD.Print($"perf: generation {g.LastGenerationMs} ms (worldgen + nations + fog, first run in the process)");
        foreach (var (stage, ms) in w.GenTimings) GD.Print($"perf:   {stage,-28} {ms,7:F1} ms");
        await Seconds(1.5);   // loading fade, first chunk recording, glyph cache
        Memory("after start");

        g.SetPaused(true);    // map-only phases: no ticks
        SimBench(w);
        foreach (int level in new[] { 1, 3, 8 })
        {
            await ZoomTo(level);
            await Measure($"×{level} still", 0);
            await Measure($"×{level} panning", 900);
        }

        await ZoomTo(3);
        g.SetPaused(false);
        g.SetSpeed(5);
        g.SendScoutAuto(); g.SendScoutAuto();
        await Measure("×3 scouts + ticks at speed 5", 0);
        Memory("end");
        GetTree().Quit();
    }

    async Task Measure(string name, float panSpeed)
    {
        await Seconds(.4);   // settle: the zoom glide and chunk re-recording are measured by ZoomTo
        _frames.Clear();
        _panSpeed = panSpeed;
        _recording = true;
        var draws = 0.0;
        var t0 = Time.GetTicksUsec();
        _center = Game.I.CameraRect.GetCenter();
        float x0 = _center.X;
        int samples = 0;
        while ((Time.GetTicksUsec() - t0) / 1e6 < _phase)
        {
            await Frames(1);
            draws += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            samples++;
        }
        _recording = false;
        _panSpeed = 0;
        float moved = _center.X - x0;
        if (_frames.Count < 2) return;
        _frames.RemoveAt(0);
        var sorted = _frames.ToArray();
        Array.Sort(sorted);
        double sum = 0; foreach (var f in sorted) sum += f;
        double avg = sum / sorted.Length;
        double P(double q) => sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
        GD.Print($"perf: {name,-30} frames={sorted.Length,5}  avg={avg,6:F2} ms ({1000 / avg,5:F0} fps)  p95={P(.95),6:F2}  p99={P(.99),6:F2}  worst={sorted[^1],6:F2} ms  draws≈{draws / Math.Max(1, samples):F0}{(panSpeed != 0 ? $"  panned {moved:F0} world px" : "")}");
    }

    /// <summary>The rules alone on a fresh copy of this world's game: 2 hours of play at speed 3, all nations bots.</summary>
    static void SimBench(World.WorldData w)
    {
        var s = NationGen.CreateInitialState(w, Game.I.Nations);
        Simulation.Begin(w, s);
        for (int k = 0; k < 400; k++) Simulation.Step(w, s, null);   // warm-up (JIT, scratch buffers)
        int ticks = (int)Clock.TicksFor(2 * 3600);
        var times = new double[ticks];
        long bytes0 = GC.GetAllocatedBytesForCurrentThread();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double cycleSum = 0; int cycles = 0;
        for (int k = 0; k < ticks; k++)
        {
            bool cycle = Clock.IsCycleTick(s.Tick);
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Simulation.Step(w, s, null);
            times[k] = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1000;
            if (cycle) { cycleSum += times[k]; cycles++; }
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - bytes0;
        Array.Sort(times);
        GD.Print($"perf: sim {ticks} ticks (2 h at speed 3) in {sw.ElapsedMilliseconds} ms: avg {sw.Elapsed.TotalMilliseconds * 1000 / ticks:F1} µs/tick, " +
                 $"cycle ticks avg {cycleSum / Math.Max(1, cycles):F0} µs, p99 {times[(int)(ticks * .99)]:F0} µs, worst {times[^1]:F0} µs, " +
                 $"{bytes / ticks} B allocated per tick → {Calendar.Text(s.Date, true)}, leader era {Eras.Name(Science.LeaderEra(s))}");
    }

    /// <summary>Step to a zoom level and report the worst frame of the glides (overlays re-record after a zoom change).</summary>
    async Task ZoomTo(int level)
    {
        if (Game.I.ZoomLevel == level) return;
        double worst = 0;
        for (int i = 0; i < 10 && Game.I.ZoomLevel != level; i++)
        {
            Game.I.RequestZoom(Game.I.ZoomLevel < level ? 1 : -1);
            for (int k = 0; k < 20; k++)
            {
                var f0 = Time.GetTicksUsec();
                await Frames(1);
                worst = Math.Max(worst, (Time.GetTicksUsec() - f0) / 1000.0);
            }
        }
        GD.Print($"perf: zoom → ×{level}: worst frame during the glides {worst:F2} ms");
    }

    static void Memory(string when)
    {
        GC.Collect(); GC.WaitForPendingFinalizers();
        double mb(double b) => b / (1024 * 1024);
        GD.Print($"perf: memory {when}: managed {mb(GC.GetTotalMemory(false)):F0} MB, Godot static {mb(OS.GetStaticMemoryUsage()):F0} MB, " +
                 $"VRAM {mb(Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed)):F0} MB (textures {mb(Performance.GetMonitor(Performance.Monitor.RenderTextureMemUsed)):F0}), " +
                 $"process working set {mb(System.Environment.WorkingSet):F0} MB, objects {Performance.GetMonitor(Performance.Monitor.ObjectCount):F0}, nodes {Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):F0}");
    }

    async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    async Task Seconds(double s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
}
