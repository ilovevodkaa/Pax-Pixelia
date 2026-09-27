using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Core.Nations;
using PaxPixelia.Sim;

namespace PaxPixelia.Dev;

/// <summary>
/// --pacing: fast-simulates the current world headlessly with bots only, once per length preset (Быстрая / Обычная /
/// Эпическая, in parallel), and prints when the leader enters each era — calendar date and real time at speed 3 —
/// then quits. Add --headless to skip the window. --pacing-hours=N caps each run (default 60 h of game time).
/// </summary>
public partial class PacingReport : Node
{
    public override void _Ready()
    {
        Name = "PacingReport";
        Game.I.WorldReady += OnWorldReady;
    }

    public override void _ExitTree() => Game.I.WorldReady -= OnWorldReady;

    async void OnWorldReady()
    {
        Game.I.WorldReady -= OnWorldReady;
        Game.I.SetPaused(true);
        var w = Game.I.World;
        var setup = Game.I.Setup;
        long cap = Clock.TicksFor(Cli.Int("pacing-hours", 60) * 3600L);
        var presets = new[] { ("Быстрая", GameSetup.PaceQuick), ("Обычная", GameSetup.PaceNormal), ("Эпическая", GameSetup.PaceEpic) };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var runs = await Task.WhenAll(presets.Select(p => Task.Run(() =>
            Pacing.Run(w, NationRoster.Build(setup with { PacePermille = p.Item2 }), p.Item2, cap, default, Game.Content))));
        GD.Print($"pacing: seed {w.Seed}, {Game.I.State.Nat.Length} nations, speed 3 = {Clock.TicksPerSecond[Clock.ReferenceSpeed]} ticks/s, {sw.ElapsedMilliseconds} ms");
        for (int i = 0; i < presets.Length; i++)
            GD.Print(Pacing.Format($"{presets[i].Item1} ({presets[i].Item2}‰):", runs[i]));
        GetTree().Quit();
    }
}
