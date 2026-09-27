using System;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// Map CLI switches for screenshots and tests (after "--"):
///   --zoom=N        start zoom (1..8, snapped to a level)        --cam=x,y      start view centre (world px)
///   --hover=P       hover a province: capital | x,y | id        --select=P      select a province: capital | x,y | id
///   --nosmooth      no zoom/jump glides
///   --mapstats      print fps / frame time every second         --pan=px/s      auto-pan horizontally (perf test)
///   --zoomcycle     zoom in/out every 1.5 s (perf test)         --regen=SEED    replace the world once at start
///   --inputtest     inject mouse/keyboard events and print OK/FAIL for hover, wheel zoom, drag, click, Esc, keys, jump
/// Scouts and speed are set up by the sim's switches (--autoscout, --scout-to, --speed; Sim/SimDriver.cs),
/// scout targeting by the UI's --targeting (UI/UiDebug.cs).
/// </summary>
internal static class MapDebug
{
    /// <summary>Apply --hover/--select once the camera has placed the view.</summary>
    public static void OnWorldReady(MapCamera cam)
    {
        var g = Game.I;
        // deferred: every module must have handled WorldReady first (the UI resets its panel there)
        Callable.From(() =>
        {
            if (!g.IsReady) return;
            int h = Resolve(Cli.Str("hover")); if (h >= 0) g.Hover(h);
            int s = Resolve(Cli.Str("select")); if (s >= 0) g.Select(s);
        }).CallDeferred();
        if (Cli.Has("inputtest") && !_inputTested) { _inputTested = true; RunInputTest(cam); }
        int regen = Cli.Int("regen", 0);         // --regen=SEED: immediately replace the world once (tests «Новый мир»)
        if (regen != 0 && !_regenerated) { _regenerated = true; Callable.From(() => g.RegenerateWorld(regen)).CallDeferred(); }
    }

    static int Resolve(string v)
    {
        if (v == null) return -1;
        var g = Game.I;
        if (v == "capital") return g.State.NationCapital[0];
        if (MapCamera.TryParseXY(v, out var xy)) return MapView.Current?.ProvinceAt(xy) ?? -1;
        return int.TryParse(v, out int p) && p >= 0 && p < g.World.P ? p : -1;
    }

    static readonly bool StatsOn = Cli.Has("mapstats");
    static bool _regenerated, _inputTested;

    /// <summary>--inputtest: feed synthetic mouse/key events through Input and print what the camera did.</summary>
    static async void RunInputTest(MapCamera cam)
    {
        var tree = cam.GetTree();
        async System.Threading.Tasks.Task Frames(int n) { for (int i = 0; i < n; i++) await cam.ToSignal(tree, SceneTree.SignalName.ProcessFrame); }
        void Motion(Vector2 at, MouseButtonMask held = 0) => Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = held });
        void Button(Vector2 at, MouseButton b, bool down) => Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = b, Pressed = down, ButtonMask = down && b == MouseButton.Left ? MouseButtonMask.Left : 0 });
        var g = Game.I; var map = MapView.Current;
        await Frames(5);
        var scr = cam.GetViewport().GetVisibleRect().Size;
        var pt = new Vector2(scr.X * .45f, scr.Y * .55f);
        Motion(pt); await Frames(2);
        int expect = map.ProvinceAt(map.View.ToWorld(pt));
        GD.Print($"inputtest hover: hovered={g.Hovered} expected={expect} {(g.Hovered == expect && expect >= 0 ? "OK" : "FAIL")}");
        var before = map.View.ToWorld(pt);
        Button(pt, MouseButton.WheelUp, true); Button(pt, MouseButton.WheelUp, false);
        await Frames(30);
        var after = map.View.ToWorld(pt);
        GD.Print($"inputtest wheel: level={g.ZoomLevel} zoom={map.View.Zoom} anchor drift={before.DistanceTo(after):F3} world px {(g.ZoomLevel == 4 && before.DistanceTo(after) < .6f ? "OK" : "FAIL")}");
        var o0 = map.View.Origin; int sel0 = g.Selected;   // a drag must not change the selection
        Button(pt, MouseButton.Left, true); await Frames(1);
        for (int i = 1; i <= 10; i++) { Motion(pt + new Vector2(12 * i, 6 * i), MouseButtonMask.Left); await Frames(1); }
        Button(pt + new Vector2(120, 60), MouseButton.Left, false); await Frames(2);
        var moved = map.View.Origin - o0;
        GD.Print($"inputtest drag: origin moved {moved} (expected ~(120, 60)) selection kept={g.Selected == sel0} {(moved.DistanceTo(new Vector2(120, 60)) < 2 && g.Selected == sel0 ? "OK" : "FAIL")}");
        var cp = new Vector2(scr.X * .5f, scr.Y * .5f);
        Motion(cp); await Frames(1);
        Button(cp, MouseButton.Left, true); await Frames(1); Button(cp, MouseButton.Left, false); await Frames(2);
        int want = map.ProvinceAt(map.View.ToWorld(cp));
        GD.Print($"inputtest click: selected={g.Selected} expected={want} {(g.Selected == want ? "OK" : "FAIL")}");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true }); await Frames(2);
        GD.Print($"inputtest esc: selected={g.Selected} {(g.Selected == -1 ? "OK" : "FAIL")}");
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.Minus, PhysicalKeycode = Key.Minus, Pressed = true }); await Frames(30);
        GD.Print($"inputtest key minus: level={g.ZoomLevel} {(g.ZoomLevel == 3 ? "OK" : "FAIL")}");
        g.JumpCamera(new Vector2(10, 700)); await Frames(40);
        DisplayServer.WindowSetSize(new Vector2I(1280, 720)); await Frames(6);
        var sz = g.CameraRect.Size * g.ZoomLevel;
        GD.Print($"inputtest resize: camera rect covers {sz} screen px {(sz.DistanceTo(cam.GetViewport().GetVisibleRect().Size) < 1 ? "OK" : "FAIL")}");
        GD.Print($"inputtest jump: rect={g.CameraRect} centre x={(g.CameraRect.Position.X + g.CameraRect.Size.X / 2):F1} (≈10) {(MathF.Abs(g.CameraRect.Position.X + g.CameraRect.Size.X / 2 - 10) < 2 ? "OK" : "FAIL")}");
    }

    static double _acc, _worst, _chunkMs, _chunkMax;
    static int _frames, _chunks;

    public static void ChunkRecorded(double ms) { _chunks++; _chunkMs += ms; if (ms > _chunkMax) _chunkMax = ms; }

    public static float AutoPan { get; } = Cli.Float("pan", 0);
    static readonly bool ZoomCycle = Cli.Has("zoomcycle");
    static double _zc;
    static int _zdir = 1;

    /// <summary>--zoomcycle: step the zoom in and out every 1.5 s (measures re-recording cost at zoom changes).</summary>
    public static void CycleZoom(MapCamera cam, double delta)
    {
        if (!ZoomCycle || (_zc += delta) < 1.5) return;
        _zc = 0;
        cam.ZoomAt(_zdir, cam.GetViewport().GetVisibleRect().Size / 2);
        _zdir = -_zdir;
    }

    /// <summary>--mapstats: one line per second with fps, average and worst frame time.</summary>
    public static void Stats(double delta)
    {
        if (!StatsOn) return;
        if (_frames == 0 && _acc == 0 && DisplayServer.WindowGetVsyncMode() != DisplayServer.VSyncMode.Disabled)
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);   // measure real throughput
        _acc += delta; _frames++; _worst = Math.Max(_worst, delta);
        if (_acc < 1) return;
        GD.Print($"mapstats: fps={Engine.GetFramesPerSecond()} avg={_acc / _frames * 1000:F2}ms worst={_worst * 1000:F2}ms " +
                 $"process={Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:F2}ms draws={Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame)} zoom={Game.I.ZoomLevel} chunks={_chunks} ({_chunkMs:F1}ms, max {_chunkMax:F1})");
        _acc = 0; _frames = 0; _worst = 0; _chunks = 0; _chunkMs = 0; _chunkMax = 0;
    }
}
