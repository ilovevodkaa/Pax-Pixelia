using System;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// Map camera: integer zoom levels ×1..×8 (pixel-perfect at rest, a short eased glide between levels), wheel zoom
/// around the cursor, drag / WASD / arrows pan, +/- keys, horizontal wrap (centre x kept in [0, W)), vertical clamp
/// below the top bar. Only map input is handled here (_UnhandledInput), so UI controls keep theirs.
/// Hover → Game.Hover, click → Game.Select or Game.SendScout while scout targeting, Esc → cancel / deselect.
/// Publishes Game.CameraRect / ZoomLevel (+ CameraMoved) and serves JumpCamera / RequestZoom.
/// Not a Camera2D: it drives MapView's world transform directly, so the screen-space overlays stay in sync.
/// </summary>
public partial class MapCamera : Node
{
    public static readonly int[] Levels = { 1, 2, 3, 4, 5, 6, 8 };
    public int ZoomLevel => Levels[_li];

    const float TopBar = 54, Slack = 40, DragThreshold = 4, PanSpeed = 900, ZoomGlide = .16f;
    static readonly StringName PanLeft = "map_pan_left", PanRight = "map_pan_right", PanUp = "map_pan_up", PanDown = "map_pan_down";

    int _li = 2;                 // target level index
    float _z = 3;                // displayed zoom
    Vector2 _c;                  // world point at the screen centre
    bool _hasWorld;

    // zoom glide: keep world point _anchorW under screen point _anchorS while _z moves from _z0 to the target
    float _z0, _zt = 1;
    Vector2 _anchorW, _anchorS;
    bool _smooth = true;

    // jump glide (minimap clicks, «show capital»)
    Vector2 _jumpFrom, _jumpTo;
    float _jt = 1;

    // mouse
    bool _pressed, _dragging, _mouseOnMap, _wasOnMap;
    Vector2 _pressPos, _pressCenter, _mouse;
    MouseButton _dragButton;

    MapViewport _last;
    Rect2 _lastRect;
    int _lastLevel = -1;

    public override void _Ready()
    {
        ProcessPriority = -100;            // update the view before MapView and the overlays process/draw
        _smooth = !Cli.Has("nosmooth");
        var g = Game.I;
        g.WorldReady += OnWorldReady;
        g.CameraJumpRequested += OnJump;
        g.ZoomRequested += OnZoomRequested;
        g.TargetingChanged += OnTargeting;
        GetViewport().SizeChanged += OnResized;
        GetWindow().MouseExited += OnMouseLeftWindow;
        if (g.IsReady) OnWorldReady();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= OnResized;
        GetWindow().MouseExited -= OnMouseLeftWindow;
        var g = Game.I;
        if (g == null) return;
        g.WorldReady -= OnWorldReady;
        g.CameraJumpRequested -= OnJump;
        g.ZoomRequested -= OnZoomRequested;
        g.TargetingChanged -= OnTargeting;
    }

    void OnWorldReady()
    {
        var g = Game.I; var w = g.World; var s = g.State;
        _hasWorld = w != null;
        if (!_hasWorld) return;
        _li = LevelIndex(Cli.Int("zoom", 3));
        _z = Levels[_li]; _zt = 1; _jt = 1;
        int cap = s.NationCapital != null && s.NationCapital.Length > 0 ? s.NationCapital[0] : 0;
        var cam = Cli.Str("cam");
        if (cam != null && TryParseXY(cam, out var cv)) _c = cv;
        // the province panel covers the right side: shift the capital a little left of centre (as in the mockup)
        else _c = new Vector2(w.PCX[cap] + .5f + 170f / _z, w.PCY[cap] + .5f);
        ApplyView(true);
        MapDebug.OnWorldReady(this);
    }

    static int LevelIndex(int zoom)
    {
        int best = 0;
        for (int i = 1; i < Levels.Length; i++) if (Math.Abs(Levels[i] - zoom) < Math.Abs(Levels[best] - zoom)) best = i;
        return best;
    }

    internal static bool TryParseXY(string v, out Vector2 r)
    {
        r = default;
        var parts = v.Split(',');
        if (parts.Length != 2) return false;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (!float.TryParse(parts[0], System.Globalization.NumberStyles.Float, ci, out float x) ||
            !float.TryParse(parts[1], System.Globalization.NumberStyles.Float, ci, out float y)) return false;
        r = new Vector2(x, y);
        return true;
    }

    Vector2 ScreenSize => GetViewport().GetVisibleRect().Size;

    // ---------------- zoom / move API ----------------

    /// <summary>Step the zoom level by dir around a screen point (keeps the world point under it fixed).</summary>
    public void ZoomAt(int dir, Vector2 screenPt)
    {
        int ni = Math.Clamp(_li + dir, 0, Levels.Length - 1);
        if (ni == _li || !_hasWorld) return;
        _anchorS = screenPt;
        _anchorW = ScreenToWorld(screenPt);
        _li = ni;
        _jt = 1;
        if (_smooth) { _z0 = _z; _zt = 0; }
        else { _z = Levels[_li]; _c = _anchorW + (ScreenSize / 2 - _anchorS) / _z; }
        ApplyView(false);
    }

    public void CenterOn(Vector2 world, bool glide = true)
    {
        if (!_hasWorld) return;
        _zt = 1;
        var w = Game.I.World;
        // take the short way round the cylinder
        float dx = world.X - _c.X;
        dx -= MathF.Round(dx / w.W) * w.W;
        if (glide && _smooth) { _jumpFrom = _c; _jumpTo = new Vector2(_c.X + dx, world.Y); _jt = 0; }
        else { _c = new Vector2(_c.X + dx, world.Y); _jt = 1; }
        ApplyView(false);
    }

    void OnJump(Vector2 world) => CenterOn(world);
    void OnZoomRequested(int dir) => ZoomAt(dir, ScreenSize / 2);

    Vector2 ScreenToWorld(Vector2 s) => _last.Zoom > 0 ? _last.ToWorld(s) : _c + (s - ScreenSize / 2) / _z;

    // ---------------- per frame ----------------

    public override void _Process(double delta)
    {
        if (!_hasWorld || !Game.I.IsReady) return;
        float dt = (float)delta;
        bool moved = false;
        if (_zt < 1)
        {
            _zt = Math.Min(1, _zt + dt / ZoomGlide);
            float e = 1 - (1 - _zt) * (1 - _zt) * (1 - _zt);     // ease-out cubic
            _z = Mathf.Lerp(_z0, Levels[_li], e);
            if (_zt >= 1) _z = Levels[_li];
            _c = _anchorW + (ScreenSize / 2 - _anchorS) / _z;
            moved = true;
        }
        if (_jt < 1)
        {
            _jt = Math.Min(1, _jt + dt / .35f);
            float e = 1 - (1 - _jt) * (1 - _jt) * (1 - _jt);
            _c = _jumpFrom.Lerp(_jumpTo, e);
            moved = true;
        }
        MapDebug.CycleZoom(this, delta);
        if (MapDebug.AutoPan != 0) { _c.X += MapDebug.AutoPan * dt / _z; moved = true; }
        if (_pressed && !_dragging && !Input.IsMouseButtonPressed(_dragButton)) _pressed = false;   // release eaten by the UI
        var pan = KeyboardPan();
        if (pan != Vector2.Zero)
        {
            float speed = PanSpeed * (Input.IsKeyPressed(Key.Shift) ? 2.2f : 1f);
            _c += pan * speed * dt / _z;
            _jt = 1;
            moved = true;
        }
        if (moved) ApplyView(false);
        // the pointer left the map for a UI control (motion no longer reaches _UnhandledInput): drop our hover once
        if (_wasOnMap && !_mouseOnMap && !_dragging) Game.I.Hover(-1);
        else if (moved && _mouseOnMap && !_dragging) UpdateHover();
        _wasOnMap = _mouseOnMap;
    }

    Vector2 KeyboardPan()
    {
        var focus = GetViewport().GuiGetFocusOwner();
        if (focus is LineEdit or TextEdit) return Vector2.Zero;
        return Input.GetVector(PanLeft, PanRight, PanUp, PanDown);
    }

    void ClampCenter()
    {
        var w = Game.I.World; var scr = ScreenSize;
        _c.X = Mathf.PosMod(_c.X, w.W);
        float worldPx = w.H * _z, avail = scr.Y - TopBar;
        if (worldPx < avail) _c.Y = w.H / 2f - (TopBar / 2) / _z;          // whole world fits: centre it below the top bar
        else
        {
            float minC = (scr.Y / 2 - TopBar - Slack) / _z, maxC = w.H - (scr.Y / 2 - Slack) / _z;
            _c.Y = Math.Clamp(_c.Y, minC, maxC);
        }
    }

    void ApplyView(bool force)
    {
        var w = Game.I.World; if (w == null) return;
        ClampCenter();
        var scr = ScreenSize;
        var origin = scr / 2 - _c * _z;
        bool atRest = _zt >= 1 && MathF.Abs(_z - MathF.Round(_z)) < 1e-4f;
        if (atRest) { _z = MathF.Round(_z); origin = new Vector2(MathF.Round(origin.X), MathF.Round(origin.Y)); }  // pixel-perfect at rest
        var v = new MapViewport { Zoom = _z, Level = Levels[_li], Origin = origin, Screen = scr, W = w.W, H = w.H };
        if (!force && v.Zoom == _last.Zoom && v.Origin == _last.Origin && v.Screen == _last.Screen && v.Level == _last.Level) return;
        _last = v;
        MapView.Current?.SetView(v);
        var rect = new Rect2(-origin / _z, scr / _z);
        var g = Game.I;
        if (rect != _lastRect || Levels[_li] != _lastLevel)
        {
            _lastRect = rect; _lastLevel = Levels[_li];
            g.CameraRect = rect;
            g.ZoomLevel = Levels[_li];
            g.RaiseCameraMoved();
        }
    }

    void OnResized() { if (_hasWorld && Game.I.IsReady) ApplyView(true); }

    void OnMouseLeftWindow()
    {
        if (!_mouseOnMap) return;
        _mouseOnMap = _wasOnMap = false;
        if (Game.I.IsReady) Game.I.Hover(-1);
    }

    // ---------------- input ----------------

    public override void _Input(InputEvent e)
    {
        // Every motion first lands here; if the GUI does not consume it, _UnhandledInput sets this back to true.
        if (e is InputEventMouseMotion) _mouseOnMap = false;
        // a drag that started on the map keeps going over UI panels
        if (_dragging && e is InputEventMouseMotion mm) { DragTo(mm.Position); GetViewport().SetInputAsHandled(); }
        else if (_pressed && e is InputEventMouseButton mb && mb.ButtonIndex == _dragButton && !mb.Pressed && _dragging)
        {
            EndDrag();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (!_hasWorld || !Game.I.IsReady) return;
        switch (e)
        {
            case InputEventMouseMotion mm:
                _mouseOnMap = true;
                _mouse = mm.Position;
                if (_pressed && !_dragging && mm.Position.DistanceTo(_pressPos) > DragThreshold)
                {
                    _dragging = true;
                    Input.SetDefaultCursorShape(Input.CursorShape.Drag);
                }
                if (_dragging) DragTo(mm.Position);
                else UpdateHover();
                break;
            case InputEventMouseButton mb:
                _mouse = mb.Position;
                if (mb.Pressed && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown))
                {
                    ZoomAt(mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1, mb.Position);
                    GetViewport().SetInputAsHandled();
                }
                else if (mb.ButtonIndex is MouseButton.Left or MouseButton.Middle)
                {
                    if (mb.Pressed) { _pressed = true; _dragging = false; _pressPos = mb.Position; _pressCenter = _c; _dragButton = mb.ButtonIndex; }
                    else if (_pressed)
                    {
                        bool click = !_dragging && mb.ButtonIndex == MouseButton.Left;
                        EndDrag();
                        if (click) Click(mb.Position);
                    }
                    GetViewport().SetInputAsHandled();
                }
                else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed && Game.I.IsTargeting)
                {
                    Game.I.CancelScoutTargeting();
                    GetViewport().SetInputAsHandled();
                }
                break;
            case InputEventPanGesture pg:
                _c += pg.Delta * 12f / _z;
                _jt = 1;
                ApplyView(false);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMagnifyGesture mg:
                if (mg.Factor > 1.08f) ZoomAt(1, mg.Position); else if (mg.Factor < .92f) ZoomAt(-1, mg.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey k when k.Pressed && !k.Echo:
                OnKey(k);
                break;
        }
    }

    void OnKey(InputEventKey k)
    {
        var key = k.Keycode;
        if (key is Key.Equal or Key.Plus or Key.KpAdd) { ZoomAt(1, ScreenSize / 2); GetViewport().SetInputAsHandled(); }
        else if (key is Key.Minus or Key.KpSubtract) { ZoomAt(-1, ScreenSize / 2); GetViewport().SetInputAsHandled(); }
        else if (key == Key.Escape)
        {
            if (Game.I.IsTargeting) Game.I.CancelScoutTargeting();
            else if (Game.I.Selected >= 0) Game.I.Select(-1);
            else return;
            GetViewport().SetInputAsHandled();
        }
    }

    void DragTo(Vector2 pos)
    {
        _c = _pressCenter - (pos - _pressPos) / _z;
        _jt = 1;
        ApplyView(false);
    }

    void EndDrag()
    {
        if (_dragging) Input.SetDefaultCursorShape(Game.I.IsTargeting ? Input.CursorShape.Cross : Input.CursorShape.Arrow);
        _pressed = _dragging = false;
    }

    void Click(Vector2 screen)
    {
        int p = ProvinceAtScreen(screen);
        if (Game.I.IsTargeting) { if (p >= 0) Game.I.SendScout(p); }
        else Game.I.Select(p);
    }

    void UpdateHover() => Game.I.Hover(ProvinceAtScreen(_mouse));

    int ProvinceAtScreen(Vector2 screen) => MapView.Current?.ProvinceAt(ScreenToWorld(screen)) ?? -1;

    void OnTargeting(bool on) => Input.SetDefaultCursorShape(on ? Input.CursorShape.Cross : Input.CursorShape.Arrow);
}
