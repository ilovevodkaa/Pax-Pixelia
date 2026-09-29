using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// The technologies screen over the whole map (SCREENS §4 «Партитура», first cut): era columns, branch rows, a card
/// per technology linked to its prerequisites, the Great Fork in its frame. What the nation has not reached yet stays
/// hidden: «???» cards and whole eras under a veil; a card opens up once its era comes or its prerequisites are known.
/// Click an open card to study it. Esc or × closes.
/// </summary>
public partial class TechScreen : Control
{
    const int LaneW = 150, CardW = 226, CardH = 58, GapX = 40, RowH = 74, EraHead = 52, EraPad = 22, HiddenEraW = 150, Top = 18;

    readonly Label _era = Ui.Text("", "Semi");
    readonly Label _study = Ui.Text("", "SmallMu");
    readonly ScrollContainer _scroll;
    readonly Canvas _canvas;
    readonly Dictionary<int, Node> _nodes = new();
    readonly List<(int era, float x, float w)> _cols = new();
    bool _scrolled;

    public TechScreen()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);

        var backdrop = Ui.Panel(new Box().Fill(Pal.Hex(0x0f0f11)).Dither(Pal.Hex(0x16161a), Pal.Hex(0x0f0f11), 120).Grain(), null, MouseFilterEnum.Stop);
        AddChild(backdrop);
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);

        var title = Ui.Text("Технологии", "LeadTitle");
        title.Uppercase = true;
        var close = Ui.IconButton("x", "Ib", 34, 34, 1, () => Hud.CloseTech());
        close.Tip("Закрыть", null, "Esc");
        var head = Ui.Panel(St.Header(64).Pad(20, 12, 14, 10),
            Ui.HBox(14, Ui.Icon("atom", 2, Pal.Ac), Ui.VBox(2, title, _era), Ui.Expand(), _study, close));
        head.SetAnchorsPreset(LayoutPreset.TopWide);
        AddChild(head);

        _scroll = new ScrollContainer { MouseFilter = MouseFilterEnum.Stop };
        AddChild(_scroll);
        _scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        _scroll.OffsetTop = 64;
        _canvas = new Canvas(this);
        _scroll.AddChild(_canvas);
    }

    /// <summary>The HUD that owns this screen (set by Hud); the × button asks it to close.</summary>
    internal Hud Hud;

    public bool Toggle()
    {
        Visible = !Visible;
        if (Visible) { _scrolled = false; Build(); }
        return Visible;
    }

    // ------------------------------------------------------------------ layout

    float CardX(TechDef d) => ColX(d.Era) + EraPad + d.Order * (CardW + GapX);
    float CardY(TechDef d) => Top + EraHead + d.Lane * RowH + (RowH - CardH) / 2f;

    float ColX(int era)
    {
        foreach (var c in _cols) if (c.era == era) return c.x;
        return 0;
    }

    /// <summary>Columns (the eras with technologies are as wide as their cards; later ones are narrow veils) and the cards.</summary>
    void Build()
    {
        if (!Game.I.IsReady) return;
        _cols.Clear();
        float x = LaneW;
        for (int e = 0; e < Eras.Count; e++)
        {
            int orders = 0;
            foreach (var d in Techs.All) if (d.Era == e) orders = Math.Max(orders, d.Order + 1);
            float w = orders > 0 ? orders * (CardW + GapX) - GapX + EraPad * 2 : HiddenEraW;
            _cols.Add((e, x, w));
            x += w;
        }
        _canvas.CustomMinimumSize = new Vector2(x + 40, Top + EraHead + Techs.Lanes.Length * RowH + 60);

        foreach (var n in _nodes.Values) n.QueueFree();
        _nodes.Clear();
        foreach (var v in Game.I.TechViews())
        {
            var node = new Node(v);
            _canvas.AddChild(node);
            node.Position = new Vector2(CardX(v.Def), CardY(v.Def));
            _nodes[v.Id] = node;
        }
        Refresh();
    }

    /// <summary>States, progress and the header, in place (the cards under the cursor stay).</summary>
    public void Refresh()
    {
        if (!Visible || !Game.I.IsReady) return;
        var g = Game.I;
        foreach (var v in g.TechViews()) if (_nodes.TryGetValue(v.Id, out var n)) n.Set(v);
        var (known, needed, total) = g.EraKnowledge;
        _era.Text = needed > 0
            ? $"{g.EraName} · изучено {known} из {total}, для эпохи «{g.NextEraName}» нужно {needed}"
            : $"{g.EraName} · дерево следующих эпох ещё растёт";
        int r = g.Researching;
        _study.Text = r >= 0 ? $"Изучается: {Techs.All[r].Name}"
            : g.ResearchIdle ? (g.TechPool > 0 ? $"Ничего не изучается · в запасе {g.TechPool} очков" : "Ничего не изучается — выберите технологию")
            : "Всё доступное изучено";
        _study.AddThemeColorOverride("font_color", r >= 0 ? Pal.Tx : g.ResearchIdle ? Pal.Warn : Pal.Mu);
        _canvas.QueueRedraw();
        if (!_scrolled) { _scrolled = true; Callable.From(ScrollToEra).CallDeferred(); }
    }

    void ScrollToEra()
    {
        float x = ColX(Game.I.EraIndex) - LaneW - 40;
        _scroll.ScrollHorizontal = (int)Math.Max(0, x);
    }

    // ------------------------------------------------------------------ the canvas: eras, lanes, links, the fork

    sealed partial class Canvas : Control
    {
        readonly TechScreen _s;
        public Canvas() { }
        public Canvas(TechScreen s) { _s = s; MouseFilter = MouseFilterEnum.Pass; }

        public override void _Draw()
        {
            if (!Game.I.IsReady) return;
            var g = Game.I;
            var font = UiFonts.Spaced(1);
            var plain = GetThemeDefaultFont();
            int era = g.EraIndex;
            float h = Size.Y;

            // era columns: headers and separators; later eras under a veil
            foreach (var (e, x, w) in _s._cols)
            {
                bool hidden = e > era + 1 || Techs.CountIn(e) == 0 && e > era;
                if (e == era) DrawRect(new Rect2(x, 0, w, h), new Color(1, 1, 1, .025f));
                DrawRect(new Rect2(x, 0, 2, h), Pal.Ln);
                string name = hidden ? "???" : Eras.Name(e);
                var col = e == era ? Pal.Hi : e < era ? Pal.Mu : hidden ? Pal.Mu2 : Pal.Sec;
                DrawString(font, new Vector2(x + 16, Top + 18), name.ToUpperInvariant(), HorizontalAlignment.Left, w - 24, UiFonts.Small, col);
                string sub = e < era ? "пройдена" : e == era ? "сейчас" : hidden ? "не открыта" : "следующая";
                DrawString(plain, new Vector2(x + 16, Top + 36), sub, HorizontalAlignment.Left, w - 24, UiFonts.Small, Pal.Mu2);
                if (hidden)
                {
                    for (float yy = Top + EraHead; yy < h; yy += 6)
                        for (float xx = x + 4 + (yy / 6 % 2) * 3; xx < x + w - 2; xx += 6) DrawRect(new Rect2(xx, yy, 2, 2), new Color(1, 1, 1, .035f));
                    DrawString(font, new Vector2(x, Top + EraHead + Techs.Lanes.Length * RowH / 2f), "?", HorizontalAlignment.Center, w, 40, Pal.Ln2);
                }
            }

            // branch rows
            for (int l = 0; l < Techs.Lanes.Length; l++)
            {
                float y = Top + EraHead + l * RowH;
                DrawRect(new Rect2(0, y, Size.X, 1), new Color(1, 1, 1, .04f));
                DrawString(plain, new Vector2(18, y + RowH / 2f + 5), Techs.Lanes[l], HorizontalAlignment.Left, LaneW - 24, UiFonts.Small, Pal.Mu);
            }

            var views = g.TechViews();
            var state = new Game.TechState[Techs.Count];
            foreach (var v in views) state[v.Id] = v.State;

            // the Great Fork frame
            float fx0 = float.MaxValue, fy0 = float.MaxValue, fx1 = float.MinValue, fy1 = float.MinValue;
            bool forkSeen = false;
            foreach (var v in views)
            {
                if (v.Def.Fork < 0) continue;
                float x = _s.CardX(v.Def), y = _s.CardY(v.Def);
                fx0 = Math.Min(fx0, x); fy0 = Math.Min(fy0, y); fx1 = Math.Max(fx1, x + CardW); fy1 = Math.Max(fy1, y + CardH);
                forkSeen |= v.State != Game.TechState.Hidden;
            }
            if (fx0 < float.MaxValue)
            {
                var r = new Rect2(fx0 - 12, fy0 - 44, fx1 - fx0 + 24, fy1 - fy0 + 56);
                DrawRect(r, new Color(1, 1, 1, .03f));
                Dashed(r, forkSeen ? Pal.Warn : Pal.Ln2);
                DrawString(font, r.Position + new Vector2(10, 18), forkSeen ? "ВЕЛИКАЯ РАЗВИЛКА" : "???",
                    HorizontalAlignment.Left, r.Size.X - 20, UiFonts.Small, forkSeen ? Pal.Warn : Pal.Mu2);
                if (forkSeen) DrawString(plain, r.Position + new Vector2(10, 34), "один путь из трёх, навсегда",
                    HorizontalAlignment.Left, r.Size.X - 20, UiFonts.Small, Pal.Mu);
            }

            // links: from a prerequisite's right edge to the card's left edge, with one bend
            for (int t = 0; t < Techs.Count; t++)
            {
                var d = Techs.All[t];
                foreach (int rq in Techs.Requires(t))
                {
                    var a = Techs.All[rq];
                    if (state[t] == Game.TechState.Hidden || state[rq] == Game.TechState.Hidden) continue;   // hidden means hidden: no lines give it away
                    var p0 = new Vector2(_s.CardX(a) + CardW, _s.CardY(a) + CardH / 2f);
                    var p1 = new Vector2(_s.CardX(d), _s.CardY(d) + CardH / 2f);
                    Color c = state[t] == Game.TechState.Known ? Pal.Ac
                        : state[rq] == Game.TechState.Known && state[t] is Game.TechState.Open or Game.TechState.Studying ? Pal.Hi
                        : state[t] == Game.TechState.Closed ? Pal.Mu2 : Pal.Ln3;
                    float mid = Mathf.Round(Math.Max(p0.X + 12, p1.X - GapX / 2f));
                    var pts = new[] { p0, new Vector2(mid, p0.Y), new Vector2(mid, p1.Y), p1 };
                    DrawPolyline(pts, new Color(0, 0, 0, .5f), 4);
                    DrawPolyline(pts, c, 2);
                    DrawRect(new Rect2(p1.X - 5, p1.Y - 3, 5, 6), c);   // arrow stub
                }
            }
        }

        void Dashed(Rect2 r, Color c)
        {
            for (float x = r.Position.X; x < r.End.X; x += 8) { DrawRect(new Rect2(x, r.Position.Y, 4, 2), c); DrawRect(new Rect2(x, r.End.Y - 2, 4, 2), c); }
            for (float y = r.Position.Y; y < r.End.Y; y += 8) { DrawRect(new Rect2(r.Position.X, y, 2, 4), c); DrawRect(new Rect2(r.End.X - 2, y, 2, 4), c); }
        }
    }

    // ------------------------------------------------------------------ a card

    sealed partial class Node : Control
    {
        Game.TechView _v;
        readonly TextureRect _icon = new() { MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.KeepCentered };
        readonly Label _name = Ui.Text("", "Semi");
        readonly Label _status = Ui.Text("", "SmallMu");
        bool _hover;

        public Node() { }
        public Node(Game.TechView v)
        {
            _v = v;
            Size = CustomMinimumSize = new Vector2(CardW, CardH);
            MouseFilter = MouseFilterEnum.Stop;
            _icon.Position = new Vector2(10, (CardH - 32) / 2f);
            _icon.Size = new Vector2(32, 32);
            AddChild(_icon);
            _name.Position = new Vector2(50, 9); _name.Size = new Vector2(CardW - 58, 20); _name.ClipText = true;
            _status.Position = new Vector2(50, 31); _status.Size = new Vector2(CardW - 58, 18); _status.ClipText = true;
            AddChild(_name); AddChild(_status);
            MouseEntered += () => { _hover = true; QueueRedraw(); };
            MouseExited += () => { _hover = false; QueueRedraw(); };
            GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && _v.State == Game.TechState.Open)
                {
                    Game.I.Research(_v.Id);
                    AcceptEvent();
                }
            };
            this.Tip(Tip);
            Set(v);
        }

        public void Set(Game.TechView v)
        {
            _v = v;
            bool hidden = v.State == Game.TechState.Hidden;
            MouseDefaultCursorShape = v.State == Game.TechState.Open ? CursorShape.PointingHand : CursorShape.Arrow;
            _icon.Texture = Icons.Get(hidden ? "help-hexagon" : v.Def.Icon, 2);
            _icon.SelfModulate = v.State switch
            {
                Game.TechState.Known => Pal.Mu,
                Game.TechState.Studying or Game.TechState.Open => Pal.Hi,
                _ => Pal.Mu2,
            };
            _name.Text = hidden ? "???" : v.Def.Name;
            _name.AddThemeColorOverride("font_color", v.State switch
            {
                Game.TechState.Open or Game.TechState.Studying => Pal.Hi,
                Game.TechState.Known => Pal.Tx,
                _ => Pal.Mu,
            });
            _status.Text = v.State switch
            {
                Game.TechState.Known => "изучено",
                Game.TechState.Studying => v.SecondsLeft < 0 ? "изучается" : v.SecondsLeft < 60 ? $"изучается · ≈{v.SecondsLeft} с" : $"изучается · ≈{(v.SecondsLeft + 59) / 60} мин",
                Game.TechState.Open => v.Points > 0 ? $"продолжить · {v.Points * 100 / Math.Max(1, v.Cost)}%" : $"изучить · {v.Cost} очков",
                Game.TechState.Closed => "путь закрыт",
                Game.TechState.Hidden => "не открыто",
                _ => v.Def.Era > Game.I.EraIndex ? $"эпоха «{Eras.Name(v.Def.Era)}»" : "нужны знания",
            };
            _status.AddThemeColorOverride("font_color", v.State == Game.TechState.Studying ? Pal.Ok : v.State == Game.TechState.Closed ? Pal.Bad : Pal.Mu);
            QueueRedraw();
        }

        public override void _Draw()
        {
            var r = new Rect2(Vector2.Zero, Size);
            var st = _v.State;
            Color fill = st switch
            {
                Game.TechState.Known => Pal.Hex(0x26262b),
                Game.TechState.Studying => Pal.Hex(0x23252a),
                Game.TechState.Open => _hover ? Pal.SurfaceHover : Pal.Surface,
                _ => Pal.Hex(0x141416),
            };
            Color frame = st switch
            {
                Game.TechState.Studying => Pal.Ok,
                Game.TechState.Open => _hover ? Pal.Hi : Pal.Ac,
                Game.TechState.Known => Pal.Ln3,
                Game.TechState.Closed => Pal.BadFill,
                _ => Pal.Ln,
            };
            DrawRect(new Rect2(4, 4, Size.X, Size.Y), Pal.Shadow);   // hard pixel shadow
            DrawRect(r, fill);
            int bw = st is Game.TechState.Studying or Game.TechState.Open ? 2 : 2;
            DrawRect(new Rect2(0, 0, Size.X, bw), frame); DrawRect(new Rect2(0, Size.Y - bw, Size.X, bw), frame);
            DrawRect(new Rect2(0, 0, bw, Size.Y), frame); DrawRect(new Rect2(Size.X - bw, 0, bw, Size.Y), frame);
            if (st == Game.TechState.Known) DrawRect(new Rect2(0, 0, 4, Size.Y), Pal.Ac);
            if (st == Game.TechState.Hidden)
                for (float y = 4; y < Size.Y - 4; y += 4)
                    for (float x = 4 + (y / 4 % 2) * 2; x < Size.X - 4; x += 4) DrawRect(new Rect2(x, y, 1, 1), new Color(1, 1, 1, .05f));
            if (st is Game.TechState.Studying or Game.TechState.Open && _v.Points > 0)
            {
                float w = (Size.X - 8) * Mathf.Clamp(_v.Points / (float)Math.Max(1, _v.Cost), 0, 1);
                DrawRect(new Rect2(4, Size.Y - 7, Size.X - 8, 3), Pal.Well);
                DrawRect(new Rect2(4, Size.Y - 7, Mathf.Round(w), 3), st == Game.TechState.Studying ? Pal.Ok : Pal.Ac);
            }
        }

        void Tip(TipCard t)
        {
            var v = _v;
            if (v.State == Game.TechState.Hidden)
            {
                t.Title("Неизвестная технология").Mu("Откроется, когда будут изучены технологии, ведущие к ней");
                return;
            }
            var d = v.Def;
            t.Title(d.Name).Line(d.Lore).Kv("Даёт", d.Effect);
            if (d.Fork >= 0) t.Kv("Развилка", "один путь из трёх, навсегда", Pal.Warn);
            var req = Techs.Requires(v.Id);
            if (req.Length > 0)
            {
                var names = new List<string>();
                foreach (int r in req) names.Add(Techs.All[r].Name);
                t.Kv("Нужно", string.Join(", ", names));
            }
            if (v.State != Game.TechState.Known) t.Kv("Цена", $"{v.Cost} очков науки").Kv("Вложено", $"{v.Points} ({v.Points * 100 / Math.Max(1, v.Cost)}%)");
            if (v.State == Game.TechState.Open) t.Mu(Game.I.Researching >= 0 ? "Нажмите, чтобы переключиться: вложенное не пропадёт" : "Нажмите, чтобы начать");
            else if (Game.I.TechProblem(v.Id) is string why) t.Mu(why);
        }
    }
}
