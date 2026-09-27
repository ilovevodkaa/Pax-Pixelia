using System;
using Godot;
using PaxPixelia.Dev;
using PaxPixelia.Map;
using PaxPixelia.UI;

namespace PaxPixelia.Core;

/// <summary>
/// Root of Main.tscn. Takes the game prepared by the front-end (Session) or builds one from the command line, builds
/// the scene tree in code — MapView (world-space map) + MapCamera + Hud (CanvasLayer UI) — then starts the game.
/// Handles CLI screenshot mode (see Cli) and the developer harnesses (--selftest, --perf, --qa, --pacing: scripts/Dev).
/// --noinput ignores the OS mouse and keyboard (screenshots while the desktop is in use).
/// </summary>
public partial class Main : Node
{
    public const int DefaultSeed = 1337;

    public MapView Map { get; private set; }
    public MapCamera Camera { get; private set; }
    public Hud Hud { get; private set; }

    public override async void _Ready()
    {
        var pending = Session.Take();   // first: the chapter card needs Session.LaunchedFromMenu

        Map = new MapView { Name = "MapView" };
        AddChild(Map);
        Camera = new MapCamera { Name = "MapCamera" };
        AddChild(Camera);
        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        if (Cli.Has("selftest")) AddChild(new SelfTest(this));
        if (Cli.Has("perf")) AddChild(new PerfProbe(this));
        if (Cli.Has("qa")) AddChild(new QaTest(this));
        if (Cli.Has("pacing")) AddChild(new PacingReport());
        if (Cli.Has("noinput")) AddChild(new InputShield { Name = "InputShield" });   // added last: sees input first

        var mode = Cli.Str("mode");
        if (mode != null) Game.I.SetMode(mode switch { "ter" => MapMode.Terrain, "rel" => MapMode.Religion, "trd" => MapMode.Trade, "fer" => MapMode.Fertility, _ => MapMode.Political });

        await Game.I.NewGame(pending?.Setup ?? SetupFromCli(), pending?.World);

        var shot = Cli.Str("shot");
        if (shot != null)
        {
            await ToSignal(GetTree().CreateTimer(Cli.Float("shot-delay", 2.0f)), SceneTreeTimer.SignalName.Timeout);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(shot);
            GD.Print($"screenshot saved: {shot}");
            if (Cli.Has("quit")) GetTree().Quit();
        }
        else if (Cli.Has("quit")) GetTree().Quit();
    }

    /// <summary>
    /// The game the command line asks for: --seed=N, --nations=2..16, --nofog, --pace=quick|normal|epic|‰, --pause.
    /// </summary>
    public static GameSetup SetupFromCli()
    {
        int seed = Cli.Int("seed", DefaultSeed);
        int pace = Cli.Str("pace") switch
        {
            null or "normal" => GameSetup.PaceNormal,
            "quick" => GameSetup.PaceQuick,
            "epic" => GameSetup.PaceEpic,
            var v => int.TryParse(v, out int pm) ? pm : GameSetup.PaceNormal,
        };
        return GameSetup.Default(seed) with
        {
            NationCount = Math.Clamp(Cli.Int("nations", Data.Nations.Length), 2, Data.Nations.Length),
            Fog = !Cli.Has("nofog"),
            PacePermille = pace,
            StartPaused = Cli.Has("pause"),
        };
    }
}

/// <summary>Swallows real input events so a screenshot run is not steered by whatever the desktop mouse or keyboard does.</summary>
internal partial class InputShield : Node
{
    public override void _Input(InputEvent e) => GetViewport().SetInputAsHandled();
}
