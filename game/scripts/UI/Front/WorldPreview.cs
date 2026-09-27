using System;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Core.Nations;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.UI.Front;

/// <summary>Numbers shown under the preview.</summary>
public readonly record struct WorldStats(int LandPercent, int Provinces, int Nations);

/// <summary>
/// «Новая игра» preview of the next world (MAIN_MENU.md §3.2): the real terrain (every 4th / 5th pixel of
/// WorldData.BaseColor), capital dots where NationGen will place them, drag to spin it around X. While a new world is
/// generating the old picture dims through a Bayer pattern and a status with 3 running cells shows; a finished world
/// is revealed through the same pattern in 4 steps. All CPU work (decimation, land share, capitals) runs in Task.Run.
/// </summary>
public partial class WorldPreview : Control
{
    /// <summary>The world and capitals last analysed (the «Народ» screen crops the land around the capital from it).</summary>
    public static WorldData CachedWorld { get; private set; }
    public static int[] CachedCapitals { get; private set; }

    /// <summary>Caches a world analysed elsewhere (the «Народ» screen opened without «Новая игра»).</summary>
    public static void Remember(WorldData w, int[] capitals) { CachedWorld = w; CachedCapitals = capitals; }

    public readonly int Factor, MapW, MapH;
    const int Border = 2;
    const float StepSec = .06f;

    readonly TextureRect _map;
    readonly DrawLayer _over;   // capital dots and running cells above the shaded map
    readonly ShaderMaterial _mat;
    readonly Label _status;
    WorldData _world;
    int[] _capitals = Array.Empty<int>();
    int _count = 16;
    (byte R, byte G, byte B) _player = (190, 72, 60);
    int _job, _anim;           // newer requests supersede running ones
    bool _generating;
    int _cellPhase = -1;
    double _clock;
    float _offset;
    bool _dragging;

    public event Action<WorldStats> Analysed;

    /// <summary>Compact = 512×288 (world ÷ 5) for windows lower than 900 px.</summary>
    public WorldPreview(bool compact)
    {
        Factor = compact ? 5 : 4;
        MapW = Game.WorldWidth / Factor;
        MapH = Game.WorldHeight / Factor;
        CustomMinimumSize = new Vector2(MapW + Border * 2, MapH + Border * 2);
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.Drag;
        TooltipText = "Потяните мышью — мир повернётся";

        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/front/world_preview.gdshader") };
        _mat.SetShaderParameter("map_w", (float)MapW);
        _mat.SetShaderParameter("reveal", 0f);
        _map = new TextureRect
        {
            Position = new Vector2(Border, Border), Size = new Vector2(MapW, MapH), Material = _mat,
            TextureFilter = TextureFilterEnum.Nearest, MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        AddChild(_map);
        _over = new DrawLayer(DrawOverlay) { Size = CustomMinimumSize };
        AddChild(_over);
        _status = PixelKit.Label("", 16, PixelKit.Text, HorizontalAlignment.Center);
        PixelKit.TextShadow(_status);
        _status.Position = new Vector2(Border, Border + MapH / 2f - 30);
        _status.Size = new Vector2(MapW, 24);
        _status.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_status);
    }

    public void SetPlayerColour((byte R, byte G, byte B) c) { _player = c; QueueRedraw(); }

    /// <summary>A new world is being generated: dim the current picture and show the status.</summary>
    public void ShowGenerating(string status)
    {
        _status.Text = status;
        if (_generating) return;   // already dimmed: only the status line changes
        _job++;
        _generating = true;
        _status.Visible = true;
        Analysed?.Invoke(default);
        if (_map.Texture != null) StepUniform("dim", 0f, .65f);
        _over.QueueRedraw();
    }

    /// <summary>Shows <paramref name="w"/> (no-op if it is already shown) with <paramref name="nations"/> capitals.</summary>
    public async void ShowWorld(WorldData w, int nations)
    {
        _count = nations;
        if (w == null) return;
        if (w == _world && !_generating) { SetNationCount(nations); return; }
        int job = ++_job;
        int factor = Factor, mw = MapW, mh = MapH;
        var (pixels, land, caps) = await Task.Run(() => (Decimate(w, factor, mw, mh), LandPercent(w), NationGen.PlaceCapitals(w, nations)));
        if (job != _job) return;
        _world = w; _capitals = caps; _generating = false;
        CachedWorld = w; CachedCapitals = caps;
        _map.Texture = ImageTexture.CreateFromImage(Image.CreateFromData(mw, mh, false, Image.Format.Rgba8, pixels));
        _mat.SetShaderParameter("map", _map.Texture);
        _mat.SetShaderParameter("dim", 0f);
        _status.Visible = false;
        _stats = new WorldStats(land, w.P, caps.Length);
        Analysed?.Invoke(_stats);
        StepUniform("reveal", 0f, 1f);
        _over.QueueRedraw();
    }

    WorldStats _stats;

    /// <summary>Only the dots move: the world itself is not regenerated when the number of nations changes.</summary>
    public async void SetNationCount(int n)
    {
        _count = n;
        if (_world == null || _generating) return;
        int job = ++_job;
        var w = _world;
        var caps = await Task.Run(() => NationGen.PlaceCapitals(w, n));
        if (job != _job) return;
        _capitals = caps;
        CachedCapitals = caps;
        _stats = _stats with { Nations = caps.Length };
        Analysed?.Invoke(_stats);
        _over.QueueRedraw();
    }

    /// <summary>Steps a shader uniform from → to in 4 even steps of 60 ms (pixel law: no smooth fades).</summary>
    async void StepUniform(string name, float from, float to)
    {
        int anim = ++_anim;
        for (int k = 0; k <= 4; k++)
        {
            if (anim != _anim || !IsInsideTree()) return;
            _mat.SetShaderParameter(name, Mathf.Lerp(from, to, k / 4f));
            if (k < 4) await ToSignal(GetTree().CreateTimer(StepSec), SceneTreeTimer.SignalName.Timeout);
        }
    }

    static byte[] Decimate(WorldData w, int f, int mw, int mh)
    {
        var dst = new byte[mw * mh * 4];
        var src = w.BaseColor;
        int o = f / 2;   // sample the middle of each block: edges of a block are often a coast outline
        for (int y = 0; y < mh; y++)
        {
            int row = (y * f + o) * w.W;
            for (int x = 0; x < mw; x++)
            {
                int s = (row + x * f + o) * 4, d = (y * mw + x) * 4;
                dst[d] = src[s]; dst[d + 1] = src[s + 1]; dst[d + 2] = src[s + 2]; dst[d + 3] = 255;
            }
        }
        return dst;
    }

    static int LandPercent(WorldData w)
    {
        long land = 0;
        foreach (byte b in w.Land) land += b;
        return (int)Math.Round(100.0 * land / w.Land.Length);
    }

    /// <summary>The piece of land around the player's future capital (full resolution, wraps across the seam).</summary>
    public static ImageTexture CapitalChunk(int width, int height)
    {
        var w = CachedWorld;
        if (w == null || CachedCapitals is not { Length: > 0 }) return null;
        int cap = CachedCapitals[0];
        int x0 = w.PCX[cap] - width / 2, y0 = Math.Clamp(w.PCY[cap] - height / 2, 0, w.H - height);
        var px = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int sx = ((x0 + x) % w.W + w.W) % w.W;
                int s = ((y0 + y) * w.W + sx) * 4, d = (y * width + x) * 4;
                px[d] = w.BaseColor[s]; px[d + 1] = w.BaseColor[s + 1]; px[d + 2] = w.BaseColor[s + 2]; px[d + 3] = 255;
            }
        return ImageTexture.CreateFromImage(Image.CreateFromData(width, height, false, Image.Format.Rgba8, px));
    }

    // ---- input: drag spins the world ----
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) _dragging = mb.Pressed;
        else if (e is InputEventMouseMotion mm && _dragging && _world != null)
        {
            _offset = Mathf.PosMod(_offset - mm.Relative.X, MapW);
            _mat.SetShaderParameter("offset_px", Mathf.Floor(_offset));
            _over.QueueRedraw();
        }
    }

    public override void _Process(double delta)
    {
        if (!_generating) return;
        _clock += delta;
        int phase = (int)(_clock / .08) % 10;
        if (phase != _cellPhase) { _cellPhase = phase; _over.QueueRedraw(); }
    }

    public override void _Draw()
    {
        var frame = new Rect2(0, 0, MapW + Border * 2, MapH + Border * 2);
        DrawRect(new Rect2(frame.Position + PixelKit.ShadowOffset, frame.Size), PixelKit.Shadow);
        DrawRect(frame, PixelKit.Ink);
        DrawRect(frame, PixelKit.PanelBorder, false, Border);
    }

    void DrawOverlay(Control c)
    {
        if (_world != null && !_generating) DrawCapitals(c);
        if (_generating) DrawCells(c);
    }

    void DrawCapitals(Control c)
    {
        int off = (int)Mathf.Floor(_offset);
        for (int i = _capitals.Length - 1; i >= 0; i--)   // the player's dot last, on top
        {
            int p = _capitals[i];
            int x = Mathf.PosMod(_world.PCX[p] / Factor - off, MapW) + Border, y = _world.PCY[p] / Factor + Border;
            if (i == 0)
            {
                c.DrawRect(new Rect2(x - 4, y - 4, 9, 9), PixelKit.AccentLight);
                c.DrawRect(new Rect2(x - 3, y - 3, 7, 7), PixelKit.Ink);
                c.DrawRect(new Rect2(x - 2, y - 2, 5, 5), ToColor(_player));
            }
            else
            {
                var n = Data.Nations[i % Data.Nations.Length];
                c.DrawRect(new Rect2(x - 2, y - 2, 5, 5), PixelKit.Ink);
                c.DrawRect(new Rect2(x - 1, y - 1, 3, 3), Color.Color8(n.R, n.G, n.B));
            }
        }
    }

    /// <summary>3 lit cells running along a row of 10 under the status (one step per 80 ms).</summary>
    void DrawCells(Control canvas)
    {
        const int cell = 8, gap = 4, count = 10;
        float w = count * (cell + gap) - gap;
        var o = new Vector2(Border + (MapW - w) / 2f, Border + MapH / 2f + 6);
        for (int i = 0; i < count; i++)
        {
            int d = (i - _cellPhase + count) % count;
            var c = d < 3 ? (d == 0 ? PixelKit.AccentLight : d == 1 ? PixelKit.Accent : PixelKit.AccentDark) : PixelKit.Surface;
            canvas.DrawRect(new Rect2(o.X + i * (cell + gap), o.Y, cell, cell), c);
        }
    }

    public static Color ToColor((byte R, byte G, byte B) c) => Color.Color8(c.R, c.G, c.B);
}

/// <summary>A mouse-transparent child that draws through a callback (to paint above sibling controls).</summary>
public partial class DrawLayer : Control
{
    readonly Action<Control> _draw;
    public DrawLayer(Action<Control> draw) { _draw = draw; MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw() => _draw(this);
}
