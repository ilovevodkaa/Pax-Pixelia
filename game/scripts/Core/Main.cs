using Godot;
using PaxPixelia.Dev;
using PaxPixelia.Map;
using PaxPixelia.UI;

namespace PaxPixelia.Core;

/// <summary>
/// Root of Main.tscn. Builds the scene tree in code: MapView (world-space map) + MapCamera + Hud (CanvasLayer UI),
/// then generates the world. Handles CLI screenshot mode (see Cli) and the developer harnesses
/// (--selftest, --perf: scripts/Dev). --noinput ignores the OS mouse and keyboard (screenshots while the desktop is in use).
/// </summary>
public partial class Main : Node
{
    public const int DefaultSeed = 1337;

    public MapView Map { get; private set; }
    public MapCamera Camera { get; private set; }
    public Hud Hud { get; private set; }

    public override async void _Ready()
    {
        Map = new MapView { Name = "MapView" };
        AddChild(Map);
        Camera = new MapCamera { Name = "MapCamera" };
        AddChild(Camera);
        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        if (Cli.Has("selftest")) AddChild(new SelfTest(this));
        if (Cli.Has("perf")) AddChild(new PerfProbe(this));
        if (Cli.Has("qa")) AddChild(new QaTest(this));
        if (Cli.Has("noinput")) AddChild(new InputShield { Name = "InputShield" });   // added last: sees input first

        var mode = Cli.Str("mode");
        if (mode != null) Game.I.SetMode(mode switch { "ter" => MapMode.Terrain, "rel" => MapMode.Religion, "trd" => MapMode.Trade, "fer" => MapMode.Fertility, _ => MapMode.Political });
        if (Cli.Has("nofog")) Game.I.WorldReady += () => { Game.I.State.FogEnabled = false; Game.I.RaiseFogChanged(null); };

        await Game.I.NewWorld(Cli.Int("seed", DefaultSeed));

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
}

/// <summary>Swallows real input events so a screenshot run is not steered by whatever the desktop mouse or keyboard does.</summary>
internal partial class InputShield : Node
{
    public override void _Input(InputEvent e) => GetViewport().SetInputAsHandled();
}
