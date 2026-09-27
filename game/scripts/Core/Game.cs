using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Core;

public enum MapMode { Terrain, Political, Religion, Trade, Fertility }

/// <summary>How the UI dresses a toast: neutral info, the scout-target prompt (crosshair), or a refusal (red).</summary>
public enum ToastKind { Info, Pick, Error }

/// <summary>
/// Autoload singleton (registered in project.godot as "Game"). The hub between modules:
/// owns WorldData + GameState, the clock, and typed C# events. Modules never reference each other directly —
/// they talk through Game.I (map ⇄ UI ⇄ sim ⇄ fog).
/// </summary>
public partial class Game : Node
{
    // NOTE: gameplay actions (claim, build, scouts, leaderboard…) live in Sim/GameActions.cs (partial class).
    public static Game I { get; private set; }

    public const int WorldWidth = 2560, WorldHeight = 1440;

    public WorldData World { get; private set; }
    public GameState State { get; private set; }
    public bool IsReady => World != null && State != null;
    /// <summary>Seed of the world being generated (or shown).</summary>
    public int Seed { get; private set; }
    /// <summary>Wall-clock milliseconds the last NewWorld took (generation + nations + fog).</summary>
    public long LastGenerationMs { get; private set; }

    public MapMode Mode { get; private set; } = MapMode.Political;
    public int Hovered { get; private set; } = -1;
    public int Selected { get; private set; } = -1;

    // ---- events (subscribe in _Ready, unsubscribe in _ExitTree) ----
    public event Action<string> GenerationProgress;     // Russian status text while generating
    public event Action WorldReady;                     // new World + State available (also after «Новый мир»)
    public event Action<int> ProvinceHovered;           // -1 = none
    public event Action<int> ProvinceSelected;          // -1 = deselect
    public event Action<MapMode> MapModeChanged;
    public event Action<IReadOnlyList<int>> ProvincesChanged; // ownership/buildings/religion changed → recolour these
    public event Action<IReadOnlyList<int>> FogChanged;       // fog state changed for these provinces (null = all)
    public event Action YearTick;                       // one game year passed
    public event Action<bool, int> TimeControlChanged;  // paused, speed
    public event Action<string, string> Notified;       // icon key, text
    public event Action<string, float, ToastKind> Toast; // text, seconds, kind

    public override void _EnterTree() => I = this;

    public override void _Ready() => AttachSimulation();   // Sim/GameActions.cs

    int _generation;   // a newer «Новый мир» supersedes a generation still running

    public async Task NewWorld(int seed)
    {
        int gen = ++_generation;
        World = null; State = null; Seed = seed;
        Hovered = -1; Selected = -1;
        // Progress marshals to the main thread; drop reports of a superseded run or ones arriving after WorldReady
        var progress = new Progress<string>(s => { if (gen == _generation && World == null) GenerationProgress?.Invoke(s); });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        WorldData world; GameState state;
        try
        {
            (world, state) = await Task.Run(() =>
            {
                var w = WorldGen.Generate(seed, WorldWidth, WorldHeight, s => ((IProgress<string>)progress).Report(s));
                ((IProgress<string>)progress).Report("Державы и границы…");
                var st = NationGen.CreateInitialState(w);
                Simulation.Begin(w, st);
                return (w, st);
            });
        }
        catch (Exception e)
        {
            // a degenerate seed must not leave the player on the loading screen: log it and roll the next one
            GD.PushError($"world generation failed for seed {seed}: {e}");
            if (gen == _generation) await NewWorld(seed + 1);
            return;
        }
        if (gen != _generation) return;
        World = world; State = state;
        _acc = 0;
        LastGenerationMs = sw.ElapsedMilliseconds;
        GD.Print($"world seed={seed} provinces={world.P} ms={LastGenerationMs}");
        WorldReady?.Invoke();
    }

    // ---- camera bridge (MapCamera writes, UI/minimap reads & requests) ----
    public Rect2 CameraRect { get; set; }               // visible world rect; X may lie outside [0,W) (wrap)
    public int ZoomLevel { get; set; } = 3;             // one of MapCamera.Levels
    public event Action CameraMoved;                    // raised by MapCamera when CameraRect/ZoomLevel change
    public event Action<Vector2> CameraJumpRequested;   // centre camera on a world position (minimap click, «show capital»)
    public event Action<int> ZoomRequested;             // +1 / -1 zoom step around screen centre (UI zoom buttons)
    public void RaiseCameraMoved() => CameraMoved?.Invoke();
    public void JumpCamera(Vector2 world) => CameraJumpRequested?.Invoke(world);
    public void RequestZoom(int dir) => ZoomRequested?.Invoke(dir);

    public void SetMode(MapMode m) { if (m == Mode) return; Mode = m; MapModeChanged?.Invoke(m); }
    public void Hover(int p) { if (p == Hovered) return; Hovered = p; ProvinceHovered?.Invoke(p); }
    public void Select(int p) { Selected = p; ProvinceSelected?.Invoke(p); }
    public void RaiseProvincesChanged(IReadOnlyList<int> ps) => ProvincesChanged?.Invoke(ps);
    public void RaiseFogChanged(IReadOnlyList<int> ps) => FogChanged?.Invoke(ps);
    public void Notify(string icon, string text) => Notified?.Invoke(icon, text);
    public void ShowToast(string text, float seconds = 3.8f, ToastKind kind = ToastKind.Info) => Toast?.Invoke(text, seconds, kind);
    /// <summary>A refused action: red toast with the reason.</summary>
    public void ShowRefusal(string text) => ShowToast(text, 3.8f, ToastKind.Error);

    // ---- clock: 1 tick = 1 year in the ancient era ----
    static readonly double[] TickSeconds = { 0, 2.0, 1.0, 0.5, 0.25, 0.1 };
    double _acc;
    public void SetPaused(bool p) { if (!IsReady) return; State.Paused = p; TimeControlChanged?.Invoke(p, State.Speed); }
    public void SetSpeed(int s) { if (!IsReady) return; State.Speed = Math.Clamp(s, 1, 5); TimeControlChanged?.Invoke(State.Paused, State.Speed); }

    public override void _Process(double delta)
    {
        if (!IsReady || State.Paused) return;
        _acc += Math.Min(delta, 0.25);   // a long hitch must not fire a burst of years
        double need = TickSeconds[State.Speed];
        while (_acc >= need) { _acc -= need; Simulation.YearTick(this); YearTick?.Invoke(); }
    }
}
