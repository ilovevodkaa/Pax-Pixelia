using System;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Sim;

/// <summary>
/// Child of the Game autoload: applies the simulation's command-line switches once the first world is ready.
/// (The clock itself lives in Game._Process: one integer tick pump, no second accumulator.)
///   --era=N            jump to era N (0 Первобытная … 10 Будущее): progress and calendar, e.g. --era=6 → 1855
///   --years=N          fast-forward N rules cycles (the former «год»: 0.5 s each at speed 3)
///   --ticks=N          fast-forward N ticks          --minutes=N   fast-forward N real minutes at speed 3
///   --autoclaim=N      claim N border provinces      --speed=N     game speed 1..5 (--pause is part of the setup)
///   --autoscout[=N]    send N (default 1) auto parties        --scout-to=P | x,y   send a party to province P / world pixel
///   --simlog           echo chronicle entries and toasts to stdout
///   --simdebug         raw fog + scout overlay (developer aid); --simdebug-zoom=Z also sets the camera zoom
/// </summary>
public partial class SimDriver : Node
{
    bool _cliDone;

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

    void OnWorldReady()
    {
        if (_cliDone) return;
        _cliDone = true;
        var g = Game.I; var w = g.World; var s = g.State;

        if (Cli.Int("era", -1) >= 0) g.Issue(Cmd.CheatEra(g.Viewer, Cli.Int("era", 0)));   // --era=mix is the map's screenshot switch

        long ticks = Cli.Int("ticks", 0) + (long)Cli.Int("years", 0) * Clock.CycleTicks + Clock.TicksFor(Cli.Int("minutes", 0) * 60L);
        if (ticks > 0)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            g.FastForward(ticks);
            GD.Print($"sim: fast-forwarded {ticks} ticks in {sw.ElapsedMilliseconds} ms → {g.DateText}, era {g.EraName}, gold {s.Gold:F0}");
        }

        int claims = Cli.Int("autoclaim", 0);
        for (int k = 0; k < claims; k++)
        {
            int best = -1;
            var fert = WorldFacts.Of(w).FertPm;
            for (int p = 0; p < w.P; p++)
                if (g.CanClaim(p) && (best < 0 || fert[p] > fert[best])) best = p;
            if (best < 0) break;
            g.Claim(best);
        }

        if (Cli.Has("speed")) g.SetSpeed(Cli.Int("speed", 2));

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
