using System;
using Godot;
using PaxPixelia.Core.Save;
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
        EraSkin.Apply(EraSkin.ForGroup(0));   // the HUD wears its era's skin (a loaded save may switch it at WorldReady)
        Audio.SoundBank.Group = 1;
        _skinGroup = 0;
        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Game.I.EraChanged += OnEraChanged;
        Game.I.WorldReady += OnSkinWorldReady;
        if (Cli.Has("selftest")) AddChild(new SelfTest(this));
        if (Cli.Has("perf")) AddChild(new PerfProbe(this));
        if (Cli.Has("qa")) AddChild(new QaTest(this));
        if (Cli.Has("pacing")) AddChild(new PacingReport());
        if (Cli.Has("sfxprobe")) AddChild(new SfxProbe(this));
        if (Cli.Has("savetest")) AddChild(new SaveTest(this));
        if (Cli.Has("noinput")) AddChild(new InputShield { Name = "InputShield" });   // added last: sees input first

        var mode = Cli.Str("mode");
        if (mode != null) Game.I.SetMode(mode switch { "ter" => MapMode.Terrain, "rel" => MapMode.Religion, "trd" => MapMode.Trade, "fer" => MapMode.Fertility, _ => MapMode.Political });

        // «ПРОДОЛЖИТЬ» / «ЗАГРУЗИТЬ» hand over a save; --load=latest|file loads one from the command line
        var load = pending != null ? pending.LoadPath : Cli.Has("load") ? SaveStore.Resolve(Cli.Str("load")) : null;
        if (load != null)
        {
            var r = await Game.I.LoadGame(load, pending?.World);
            if (!r.Ok && !r.Superseded)
            {
                GD.PushWarning($"load failed: {r.Error}");
                if (pending != null) { BackToMenu(r.Error); return; }
                await Game.I.NewGame(SetupFromCli());   // the command line's run goes on with its own game
            }
        }
        else
        {
            if (pending == null && Cli.Has("load")) GD.PushWarning("--load: no save to load, starting a new game");
            await Game.I.NewGame(pending?.Setup ?? SetupFromCli(), pending?.World);
        }

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

    int _skinGroup = -1;

    void OnEraChanged(int era) => Callable.From(() => SyncSkin(carry: true)).CallDeferred();   // the same game goes on
    void OnSkinWorldReady() => Callable.From(() => SyncSkin(carry: false)).CallDeferred();     // a new world or a save

    /// <summary>The era group changed (a new era, a loaded save): the interface takes the group's skin — Костёр,
    /// Глина, Перо, Латунь, Сигнал — and is rebuilt in the same place of the tree (input order unchanged).</summary>
    void SyncSkin(bool carry)
    {
        if (!IsInsideTree() || Game.I == null || !Game.I.IsReady) return;
        int g = EraSkin.Group(Game.I.EraIndex);
        if (g == _skinGroup) return;
        _skinGroup = g;
        EraSkin.Apply(EraSkin.ForGroup(g));
        Audio.SoundBank.Group = g + 1;   // the sounds with a material follow the same era group
        if (Hud == null) return;
        int at = Hud.GetIndex();
        var old = Hud;
        var kept = carry ? old.TakeCarry() : ((System.Collections.Generic.List<(string, string, string)>, int)?)null;
        RemoveChild(old);
        old.QueueFree();
        Hud = new Hud { Name = "Hud", Carry = kept };
        AddChild(Hud);
        MoveChild(Hud, at);
        GD.Print($"skin: {EraSkin.Current.Name} for {Game.I.EraName}");
    }

    public override void _ExitTree()
    {
        if (Game.I == null) return;
        Game.I.EraChanged -= OnEraChanged;
        Game.I.WorldReady -= OnSkinWorldReady;
    }

    /// <summary>A save chosen in the menu could not be loaded: back to the title, which shows the reason.</summary>
    void BackToMenu(string error)
    {
        Game.I.EndGame();
        Session.Notice = error;
        Session.ReturnedFromGame = true;
        GetTree().ChangeSceneToFile(PauseMenu.FrontScene);
    }

    /// <summary>
    /// The game the command line asks for: --seed=N, --nations=2..16, --nofog, --pace=quick|normal|epic|‰, --pause, --nomad.
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
            Nomad = Cli.Has("nomad"),   // runs with flags start settled (tests, screenshots); --nomad = the tribe start
        };
    }
}

/// <summary>Swallows real input events so a screenshot run is not steered by whatever the desktop mouse or keyboard does.</summary>
internal partial class InputShield : Node
{
    public override void _Input(InputEvent e) => GetViewport().SetInputAsHandled();
}
