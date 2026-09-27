using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Sim;

/// <summary>
/// Child of the Game autoload: turns real time into scout sub-steps at the pace of the game clock
/// (Scouts.SubStepsPerYear per game year, so the speed changes only the wall-clock pace; frozen while paused)
/// and applies the simulation's command-line switches once the first world is ready:
///   --autoscout[=N]    send N (default 1) auto parties        --scout-to=P | x,y   send a party to province P / world pixel
///   --speed=N          game speed 1..5                         --pause              start paused
///   --years=N          fast-forward N years before play        --autoclaim=N        claim N border provinces
///   --simlog           echo chronicle entries and toasts to stdout
///   --simdebug         raw fog + scout overlay (developer aid); --simdebug-zoom=Z also sets the camera zoom
/// </summary>
public partial class SimDriver : Node
{
    double _acc;
    bool _cliDone;

    public void Reset() => _acc = 0;

    public override void _Ready()
    {
        var g = Game.I;
        g.WorldReady += OnWorldReady;
        if (Cli.Has("simdebug")) AddChild(new SimDebugOverlay(Cli.Float("simdebug-zoom", 0)) { Name = "SimDebugOverlay" });
        if (Cli.Has("simlog"))
        {
            g.Notified += (icon, text) => GD.Print($"[note:{icon}] {text}");
            g.Toast += (text, _, kind) => GD.Print($"[toast:{kind}] {text}");
        }
    }

    public override void _ExitTree()
    {
        if (Game.I != null) Game.I.WorldReady -= OnWorldReady;
    }

    public override void _Process(double delta)
    {
        var g = Game.I;
        if (g == null || !g.IsReady || g.State.Paused || g.State.Scouts.Count == 0) { _acc = 0; return; }
        _acc += Math.Min(delta, Game.MaxCatchUp) / Game.TickSeconds[g.State.Speed] * Scouts.SubStepsPerYear;
        int n = (int)_acc;
        if (n == 0) return;
        _acc -= n;
        var t = Scouts.Advance(g.World, g.State, n, g);
        if (t.Any) g.RaiseScoutsChanged();
    }

    void OnWorldReady()
    {
        if (_cliDone) return;
        _cliDone = true;
        var g = Game.I; var w = g.World; var s = g.State;

        int years = Cli.Int("years", 0);
        if (years > 0)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < years; k++) Simulation.YearTick(g);
            GD.Print($"sim: fast-forwarded {years} years in {sw.ElapsedMilliseconds} ms → {GameState.YearText(s.Year)}, gold {s.Gold:F0}");
        }

        int claims = Cli.Int("autoclaim", 0);
        for (int k = 0; k < claims; k++)
        {
            int best = -1;
            for (int p = 0; p < w.P; p++)
                if (g.CanClaim(p) && (best < 0 || w.PFert[p] > w.PFert[best])) best = p;
            if (best < 0) break;
            g.Claim(best);
        }

        if (Cli.Has("speed")) g.SetSpeed(Cli.Int("speed", 2));
        if (Cli.Has("pause")) g.SetPaused(true);

        var to = Cli.Str("scout-to");
        if (to != null)
        {
            int p = -1;
            var parts = to.Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y) && y >= 0 && y < w.H)
                p = w.Prov[y * w.W + Mathf.PosMod(x, w.W)];
            else if (int.TryParse(to, out int id)) p = id;
            if (p >= 0 && p < w.P) g.SendScout(p);
        }

        if (Cli.Has("autoscout"))
            for (int k = Math.Clamp(Cli.Int("autoscout", 1), 1, Scouts.Max); k > 0; k--) g.SendScoutAuto();
    }
}
