using System;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// The minimap card: a downsampled world in the current map mode (political tints, fog clouds) in a hard ink frame,
/// the camera rectangle (drawn three times for the horizontal wrap), scout markers, and the bar [−][×3][+] · «Новый мир».
/// </summary>
public partial class Minimap : PanelContainer
{
    public readonly MiniMapView View = new();
    readonly Label _zoom;
    readonly Control _regen, _zbox;

    public Minimap(Action onRegenerate)
    {
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", St.Card().Pad(6));

        var zout = Ui.IconButton("minus", "ZL", 28, 26, 1, () => Game.I.RequestZoom(-1)).Tip("Отдалить", null, "Колесо мыши · клавиша −");
        var zin = Ui.IconButton("plus", "ZR", 28, 26, 1, () => Game.I.RequestZoom(+1)).Tip("Приблизить", null, "Колесо мыши · клавиша +");
        _zoom = Ui.Text("×3", "Semi");
        _zoom.HorizontalAlignment = HorizontalAlignment.Center;
        var zbox = Ui.Panel(new Box().Fill(Pal.Well).Border(Pal.Ln), _zoom, MouseFilterEnum.Stop).MinSize(40, 26);
        zbox.Tip("Масштаб карты", null, "×½ — весь мир, ×8 — пиксель к пикселю");
        var regen = Ui.Button("Новый мир", "refresh", "Ghost", onRegenerate, 1, 26).Tip("Сгенерировать новый мир", null, "Случайное зерно · текущая партия будет потеряна");
        _regen = regen; _zbox = zbox;
        var bar = Ui.HBox(4, zout, zbox, zin, Ui.Expand(), regen);
        AddChild(Ui.VBox(8, View, bar));
    }

    internal Control DebugTarget(string name) => name switch { "regen" => _regen, "zoom" => _zbox, "minimap" => View, _ => null };

    int _shownZoom = -1;
    public void OnCameraMoved()
    {
        if (Game.I.ZoomLevel != _shownZoom) { _shownZoom = Game.I.ZoomLevel; _zoom.Text = _shownZoom <= 0 ? "×½" : "×" + _shownZoom; }
        View.QueueRedraw();
    }
}

/// <summary>The minimap picture itself. Colours are recomputed only on world/mode/ownership/fog events (≈46k samples).</summary>
public partial class MiniMapView : Control
{
    static readonly Color Outline = Pal.Ink;
    // unexplored land and sea: flat graphite in two calm tones (four cloud levels at this size read as dirty speckle)
    static readonly Color[] FogTone = { Pal.Hex(0x1c1c20), Pal.Hex(0x1c1c20), Pal.Hex(0x232327), Pal.Hex(0x232327) };

    int _mw = 288, _mh = 162;
    int[] _sample;           // world pixel index per minimap pixel
    byte[] _cloud;           // fog cloud level per minimap pixel
    byte[] _rgba;
    float[] _mR, _mG, _mB, _mA, _mD, _mF;
    Image _img;
    ImageTexture _tex;
    bool _dragging;

    public MiniMapView()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        ClipContents = true;
        TextureFilter = TextureFilterEnum.Nearest;
        CustomMinimumSize = new Vector2(_mw + 4, _mh + 4);
    }

    /// <summary>288×162 normally, 240×135 on short screens (≤800px).</summary>
    public void SetMapSize(int w, int h)
    {
        if (w == _mw && h == _mh) return;
        _mw = w; _mh = h;
        CustomMinimumSize = new Vector2(w + 4, h + 4);
        if (Game.I.IsReady) Resample();
    }

    public void Resample()
    {
        var wd = Game.I.World;
        if (wd == null) return;
        int n = _mw * _mh;
        _sample = new int[n];
        _cloud = new byte[n];
        _rgba = new byte[n * 4];
        double sx = (double)wd.W / _mw, sy = (double)wd.H / _mh;
        for (int y = 0; y < _mh; y++)
        {
            int wy = Math.Min(wd.H - 1, (int)(y * sy + sy / 2));
            for (int x = 0; x < _mw; x++)
            {
                int wx = Math.Min(wd.W - 1, (int)(x * sx + sx / 2));
                int i = y * _mw + x;
                _sample[i] = wy * wd.W + wx;
                double v = Core.Noise.Fbm(wx, wy, 6, 2, wd.Seed + 911, wd.W);   // broad, soft cloud banks
                _cloud[i] = (byte)Math.Clamp((int)((v - .28) / .44 * 4), 0, 3);
            }
        }
        _img = Image.CreateEmpty(_mw, _mh, false, Image.Format.Rgba8);
        _tex = ImageTexture.CreateFromImage(_img);
        Recolor();
    }

    public void Recolor()
    {
        var wd = Game.I.World; var s = Game.I.State;
        if (wd == null || s == null || _sample == null) return;
        ModeParams(wd, s);
        bool fogOn = s.FogEnabled && s.Fog != null;
        var bc = wd.BaseColor;
        for (int i = 0; i < _sample.Length; i++)
        {
            int wi = _sample[i], p = wd.Prov[wi], o = i * 4;
            if (fogOn && s.Fog[p] == 0)
            {
                var t = FogTone[_cloud[i]];
                _rgba[o] = (byte)(t.R * 255); _rgba[o + 1] = (byte)(t.G * 255); _rgba[o + 2] = (byte)(t.B * 255); _rgba[o + 3] = 255;
                continue;
            }
            float r = bc[wi * 4], g = bc[wi * 4 + 1], b = bc[wi * 4 + 2];
            float d = _mD[p];
            if (d > 0) { float m = (r + g + b) / 3; r += (m - r) * d; g += (m - g) * d; b += (m - b) * d; }
            float a = _mA[p];
            if (a > 0) { r += (_mR[p] - r) * a; g += (_mG[p] - g) * a; b += (_mB[p] - b) * a; }
            float f = _mF[p];
            _rgba[o] = (byte)Math.Clamp(r * f, 0, 255); _rgba[o + 1] = (byte)Math.Clamp(g * f, 0, 255); _rgba[o + 2] = (byte)Math.Clamp(b * f, 0, 255); _rgba[o + 3] = 255;
        }
        _img.SetData(_mw, _mh, false, Image.Format.Rgba8, _rgba);
        _tex.Update(_img);
        QueueRedraw();
    }

    /// <summary>Per-province tint/desaturation/brightness — a port of modeParams() in the mockup's render.js.</summary>
    void ModeParams(World.WorldData wd, GameState s)
    {
        int P = wd.P;
        if (_mR == null || _mR.Length != P) { _mR = new float[P]; _mG = new float[P]; _mB = new float[P]; _mA = new float[P]; _mD = new float[P]; _mF = new float[P]; }
        var mode = Game.I.Mode;
        bool fogOn = s.FogEnabled && s.Fog != null;
        for (int p = 0; p < P; p++)
        {
            bool L = wd.PLand[p] == 1; int o = s.VisibleOwner(p);
            float a = 0, d = 0, f = 1; float cr = 0, cg = 0, cb = 0;
            switch (mode)
            {
                case MapMode.Political:
                    if (L && o >= 0) { Nat(o, out cr, out cg, out cb); a = .56f; } else if (L) { d = .18f; f = .94f; }
                    break;
                case MapMode.Religion:
                    if (L && s.Religion[p] >= 0) { var rl = Data.Religions[s.Religion[p]]; cr = rl.R; cg = rl.G; cb = rl.B; a = .56f; } else if (L) d = .6f;
                    break;
                case MapMode.Trade:
                    d = .62f; f = .85f;
                    if (L && o >= 0) { Nat(o, out cr, out cg, out cb); a = .18f; }
                    break;
                case MapMode.Fertility:
                    if (L) { FertColor(wd.PFert[p], out cr, out cg, out cb); a = .62f; } else d = .5f;
                    break;
            }
            if (fogOn && s.Fog[p] == 1) { d = Math.Max(d, L ? .45f : .3f); f *= L ? .78f : .85f; }   // = MapTextures.StaleDesat/StaleDim
            _mR[p] = cr; _mG[p] = cg; _mB[p] = cb; _mA[p] = a; _mD[p] = d; _mF[p] = f;
        }
    }

    static void Nat(int n, out float r, out float g, out float b) { var d = Game.I.Nations[n]; r = d.R; g = d.G; b = d.B; }
    static void FertColor(float f, out float r, out float g, out float b)
    {
        if (f < .5f) { r = 200; g = 90 + f * 2 * 120; b = 70; }
        else { r = 200 - (f - .5f) * 2 * 120; g = 210; b = 70 + (f - .5f) * 2 * 20; }
    }

    /// <summary>The fertility mode's colour for f in 0..1 (same ramp as the map).</summary>
    public static Color FertilityColor(float f) { FertColor(f, out float r, out float g, out float b); return new Color(r / 255f, g / 255f, b / 255f); }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, _mw + 4, _mh + 4), Outline);
        if (_tex == null) { DrawRect(new Rect2(2, 2, _mw, _mh), Pal.Surface); return; }
        DrawTextureRect(_tex, new Rect2(2, 2, _mw, _mh), false);
        var wd = Game.I.World; var s = Game.I.State;
        if (wd == null || s == null) return;
        float kx = (float)_mw / wd.W, ky = (float)_mh / wd.H;

        foreach (var sc in s.Scouts)
        {
            if (sc.Path == null || sc.Path.Length == 0) continue;
            int p = sc.Path[Math.Clamp(sc.Step, 0, sc.Path.Length - 1)];
            float mx = Mathf.Round(wd.PCX[p] * kx) + 2, my = Mathf.Round(wd.PCY[p] * ky) + 2;
            DrawRect(new Rect2(mx - 2, my - 2, 5, 5), Pal.Ink);
            DrawRect(new Rect2(mx - 1, my - 1, 3, 3), Pal.Hi);
        }

        var cam = Game.I.CameraRect;
        if (cam.Size.X <= 0) return;
        float x0 = cam.Position.X * kx, y0 = cam.Position.Y * ky, w = Mathf.Round(cam.Size.X * kx), h = Mathf.Round(cam.Size.Y * ky);
        for (int k = -1; k <= 1; k++)
        {
            var r = new Rect2(Mathf.Round(x0 + k * _mw) + 2, Mathf.Round(y0) + 2, w, h);
            FrameRect(r.Grow(1), Outline);
            FrameRect(r, Pal.Hi);
        }
    }

    void FrameRect(Rect2 r, Color c)
    {
        DrawRect(new Rect2(r.Position.X, r.Position.Y, r.Size.X, 1), c);
        DrawRect(new Rect2(r.Position.X, r.End.Y - 1, r.Size.X, 1), c);
        DrawRect(new Rect2(r.Position.X, r.Position.Y + 1, 1, r.Size.Y - 2), c);
        DrawRect(new Rect2(r.End.X - 1, r.Position.Y + 1, 1, r.Size.Y - 2), c);
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                _dragging = mb.Pressed;
                if (mb.Pressed) Jump(mb.Position);
                AcceptEvent();
                break;
            case InputEventMouseMotion mm when _dragging:
                Jump(mm.Position);
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                Game.I.RequestZoom(+1); AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                Game.I.RequestZoom(-1); AcceptEvent();
                break;
        }
    }

    void Jump(Vector2 local)
    {
        var wd = Game.I.World;
        if (wd == null) return;
        float wx = Mathf.PosMod((local.X - 2) / _mw * wd.W, wd.W);
        float wy = Mathf.Clamp((local.Y - 2) / _mh * wd.H, 0, wd.H - 1);
        Game.I.JumpCamera(new Vector2(wx, wy));
    }
}
