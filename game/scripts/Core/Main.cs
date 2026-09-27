using Godot;
using PaxPixelia.Dev;
using PaxPixelia.Map;
using PaxPixelia.UI;

namespace PaxPixelia.Core;

/// <summary>
/// Root of Main.tscn. Builds the scene tree in code: MapView (world-space map) + MapCamera + Hud (CanvasLayer UI),
/// then generates the world. Handles CLI screenshot mode (see Cli) and the developer harnesses
/// (--selftest, --perf: scripts/Dev).
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
