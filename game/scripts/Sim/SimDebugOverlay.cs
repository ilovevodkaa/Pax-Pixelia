using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Sim;

/// <summary>
/// Developer overlay (--simdebug, --simdebug-zoom=Z): paints the raw fog states and the scout parties over whatever map is running,
/// so the simulation can be checked without the real renderer. Not part of the game's look.
/// </summary>
public partial class SimDebugOverlay : Node2D
{
    static readonly Color Party = new(1f, .86f, .2f), PathCol = new(1f, .86f, .2f, .55f);
    Image _img; ImageTexture _tex; byte[] _px;
    readonly float _zoom;

    public SimDebugOverlay() { }
    public SimDebugOverlay(float zoom) { _zoom = zoom; }

    public override void _Ready()
    {
        ZIndex = 100;
        TextureFilter = TextureFilterEnum.Nearest;
        Game.I.WorldReady += OnWorld;
        Game.I.FogChanged += OnFog;
        if (Game.I.IsReady) OnWorld();
    }

    public override void _ExitTree()
    {
        if (Game.I == null) return;
        Game.I.WorldReady -= OnWorld;
        Game.I.FogChanged -= OnFog;
    }

    void OnWorld()
    {
        var w = Game.I.World;
        _px = new byte[w.N * 4];
        Paint(null);
        _img = Image.CreateFromData(w.W, w.H, false, Image.Format.Rgba8, _px);
        _tex = ImageTexture.CreateFromImage(_img);
        if (_zoom > 0 && GetViewport().GetCamera2D() is { } cam) cam.Zoom = new Vector2(_zoom, _zoom);
        QueueRedraw();
    }

    void OnFog(IReadOnlyList<int> changed)
    {
        if (_tex == null) return;
        Paint(changed);
        _img.SetData(Game.I.World.W, Game.I.World.H, false, Image.Format.Rgba8, _px);
        _tex.Update(_img);
    }

    void Paint(IReadOnlyList<int> changed)
    {
        var w = Game.I.World; var s = Game.I.State;
        if (changed == null) for (int p = 0; p < w.P; p++) PaintProvince(w, s, p);
        else foreach (int p in changed) PaintProvince(w, s, p);
    }

    void PaintProvince(World.WorldData w, GameState s, int p)
    {
        // unexplored: near-black; stale: violet veil (easy to tell apart from dark terrain); visible: clear
        int f = s.FogEnabled ? s.Fog[p] : 2;
        byte r = f == 1 ? (byte)70 : (byte)14, g = f == 1 ? (byte)40 : (byte)16, b = f == 1 ? (byte)110 : (byte)22;
        byte a = f switch { 0 => 235, 1 => 120, _ => 0 };
        for (int k = w.PixOffset[p]; k < w.PixOffset[p + 1]; k++)
        {
            int i = w.PixList[k] * 4;
            _px[i] = r; _px[i + 1] = g; _px[i + 2] = b; _px[i + 3] = a;
        }
    }

    public override void _Process(double delta)
    {
        if (Game.I.IsReady && Game.I.State.Scouts.Count > 0) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_tex == null || !Game.I.IsReady) return;
        DrawTexture(_tex, Vector2.Zero);
        var w = Game.I.World;
        foreach (var sc in Game.I.State.Scouts)
        {
            for (int k = sc.Step; k < sc.Path.Length - 1; k++)
            {
                var p0 = Anchor(w, sc.Path[k]);
                DrawLine(p0, Near(w, p0, Anchor(w, sc.Path[k + 1])), PathCol, 1f);
            }
            var a = Anchor(w, sc.Path[sc.Step]);
            var b = sc.Step + 1 < sc.Path.Length ? Near(w, a, Anchor(w, sc.Path[sc.Step + 1])) : a;
            var at = a.Lerp(b, sc.Progress);
            DrawCircle(at, 4f, Colors.Black);
            DrawCircle(at, 3f, Party);
        }
    }

    static Vector2 Anchor(World.WorldData w, int p) => new(w.PCX[p] + .5f, w.PCY[p] + .5f);

    /// <summary>b shifted by ±W so it lies next to a across the horizontal wrap.</summary>
    static Vector2 Near(World.WorldData w, Vector2 a, Vector2 b) =>
        b.X - a.X > w.W / 2f ? b - new Vector2(w.W, 0) : a.X - b.X > w.W / 2f ? b + new Vector2(w.W, 0) : b;
}
