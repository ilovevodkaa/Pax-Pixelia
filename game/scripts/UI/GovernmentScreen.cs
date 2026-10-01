using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// «Правительство» over the whole map (the bank button or P). Tab «Политика»: the policy tree on a vast dark field you
/// drag around with the mouse (it glides on with inertia and springs back at the edges; the wheel scrolls). At the
/// centre «Основы государства»; around it the eight ways of the political compass, «?» until the foundations are laid;
/// beyond them the rings still to come. Tab «Законы»: the outline of the laws of every era, group by group (passing them
/// comes later). The header shows the budget and the realm's place on the compass. Esc or × closes.
/// </summary>
public partial class GovernmentScreen : Control
{
    internal Hud Hud;

    // kept across HUD rebuilds (an era-group change rebuilds the HUD)
    static int _tab;
    static Vector2 _pan;
    static bool _greeted;

    readonly Label _sub = Ui.Text("", "SmallMu");
    readonly Label _budget = Ui.Text("", "Semi");
    readonly Button _tabPolicy, _tabLaws, _drop;
    readonly TreeView _tree;
    readonly ScrollContainer _lawsScroll;
    readonly VBoxContainer _laws = Ui.VBox(10);
    readonly MiniCompass _compass = new();

    public GovernmentScreen()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);

        var backdrop = Ui.Panel(new Box().Fill(Pal.Well).Dither(Pal.Bar, Pal.Well, 120).Grain(), null, MouseFilterEnum.Stop);
        AddChild(backdrop);
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);

        _tree = new TreeView(this);
        AddChild(_tree);
        _tree.SetAnchorsPreset(LayoutPreset.FullRect);
        _tree.OffsetTop = 64;

        _lawsScroll = new ScrollContainer { MouseFilter = MouseFilterEnum.Stop, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(_lawsScroll);
        _lawsScroll.SetAnchorsPreset(LayoutPreset.FullRect);
        _lawsScroll.OffsetTop = 64;
        var lawsPad = Ui.Margin(_laws, 28, 20, 28, 40);
        lawsPad.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _lawsScroll.AddChild(lawsPad);

        var title = Ui.Text("Правительство", "LeadTitle");
        title.Uppercase = true;
        _tabPolicy = Ui.Button("Политика", "compass", null, () => SetTab(0));
        _tabPolicy.Tip("Политика", "Дерево курсов державы: от основ государства к левым и правым, к власти и к свободе.");
        _tabLaws = Ui.Button("Законы", "book", null, () => SetTab(1));
        _tabLaws.Tip("Законы", "Законы всех эпох по группам. Пока только заготовки: принимать их можно будет позже.");
        _drop = Ui.Button("Оставить курс", "x", "Sm", () => Game.I.DropCourse(), 1, 28);
        _drop.Tip("Оставить курс", "Принятие прервётся, начатое пропадёт.");
        var chip = Ui.Panel(St.Tag(), Ui.HBox(6, Ui.Icon("coins", 1, Pal.Ac), _budget), MouseFilterEnum.Pass);
        chip.CustomMinimumSize = new Vector2(0, 30);
        chip.Tip(BudgetTip);
        _compass.Tip(CompassTip);
        var close = Ui.IconButton("x", "Ib", 34, 34, 1, () => Hud.ClosePolicy());
        close.Tip("Закрыть", null, "Esc");
        _sub.ClipText = true;
        _sub.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var titleCol = Ui.VBox(2, title, _sub);
        titleCol.SizeFlagsHorizontal = SizeFlags.ExpandFill;   // the subtitle gives way on a narrow window
        var head = Ui.Panel(St.Header(64).Pad(20, 12, 14, 10),
            Ui.HBox(14, Ui.Icon("building-bank", 2, Pal.Ac), titleCol, Ui.Gap(12, 0), Ui.HBox(6, _tabPolicy, _tabLaws),
                _drop, chip, _compass, close));
        head.SetAnchorsPreset(LayoutPreset.TopWide);
        AddChild(head);
    }

    public bool Toggle()
    {
        Visible = !Visible;
        _tree.Halt();
        if (!Visible) return false;
        _tree.Build();
        if (!_greeted) { _greeted = true; _tree.Greet(); }
        SetTab(_tab);
        return true;
    }

    int _lawsEra = -1;
    GameState _lawsState;

    /// <summary>The laws tab is built when first shown and again only after an era change or another game.</summary>
    void EnsureLaws()
    {
        if (!Game.I.IsReady) return;
        int era = Game.I.State.Nat[Game.I.Viewer].Era;
        if (era == _lawsEra && ReferenceEquals(_lawsState, Game.I.State)) return;
        _lawsEra = era; _lawsState = Game.I.State;
        BuildLaws();
    }

    /// <summary>Open a tab (the --policy=laws screenshot switch).</summary>
    internal void DebugTab(int t) => SetTab(t);

    void SetTab(int t)
    {
        _tab = t;
        _tree.Visible = t == 0;
        _lawsScroll.Visible = t == 1;
        if (t == 1) EnsureLaws();
        _tabPolicy.ThemeTypeVariation = t == 0 ? "On" : "";
        _tabLaws.ThemeTypeVariation = t == 1 ? "On" : "";
        Refresh();
    }

    /// <summary>States and the header, in place (nothing under the cursor is freed).</summary>
    public void Refresh()
    {
        if (!Visible || !Game.I.IsReady) return;
        var g = Game.I;
        int now = g.CourseNow;
        var views = g.CourseViews();
        _sub.Text = now >= 0 ? $"Принимается «{Politics.All[now].Name}» · ≈{views[now].SecondsLeft} с"
            : !g.StateFounded ? (g.IsNomad ? "Сначала основайте столицу: у кочующего рода ещё нет государства" : "Начните с «Основ государства» в центре")
            : _tab == 0 ? "Выберите следующий курс: каждый уводит державу в свою сторону" : "Законы эпох: пока только заготовки";
        _sub.Colored(now >= 0 ? Pal.Ok : Pal.Mu);
        _drop.Visible = now >= 0;
        var b = g.BudgetLines;
        var (prov, limit, over) = g.Admin;
        _budget.Text = $"{Fmt.Signed(b.Net / 100.0, 1)} за цикл · провинций {prov}/{limit}";
        _budget.Colored(b.Net < 0 || over > 0 ? Pal.Bad : Pal.Tx);
        _compass.QueueRedraw();
        _tree.Refresh(views);
    }

    void BudgetTip(TipCard t)
    {
        var g = Game.I;
        if (!g.IsReady) return;
        var b = g.BudgetLines;
        var (prov, limit, over) = g.Admin;
        t.Title("Казна и управление")
         .Kv("Налоги", Fmt.Signed(b.Taxes / 100.0, 1), Pal.Ok)
         .Kv("Постройки", Fmt.Signed(-b.Buildings / 100.0, 1), Pal.Bad)
         .Kv("Управление", Fmt.Signed(-b.Admin / 100.0, 1), over > 0 ? Pal.Bad : Pal.Mu);
        if (b.Pacts > 0) t.Kv("Договоры", Fmt.Signed(b.Pacts / 100.0, 1), Pal.Ok);
        if (b.TributeIn > 0) t.Kv("Дань нам", Fmt.Signed(b.TributeIn / 100.0, 1), Pal.Ok);
        if (b.TributeOut > 0) t.Kv("Наша дань", Fmt.Signed(-b.TributeOut / 100.0, 1), Pal.Bad);
        t.Kv("Итого за цикл", Fmt.Signed(b.Net / 100.0, 1), b.Net >= 0 ? Pal.Ok : Pal.Bad)
         .Kv("Провинции", $"{prov} из {limit}", over > 0 ? Pal.Bad : Pal.Hi)
         .Mu(over > 0 ? $"Перерасширение {over}%: довольство {Policy.OverMood(over)}, новые земли дороже на {over}%"
                      : "Предел управления растят эпохи, города, знания и курсы державы");
    }

    void CompassTip(TipCard t)
    {
        var g = Game.I;
        t.Title("Политический компас");
        if (!g.StateFounded) { t.Line("Куда пойдёт держава, станет ясно, когда будут заложены «Основы государства»."); return; }
        var (x, y) = g.CompassPosition;
        t.Line("Где держава стоит сейчас: сумма её курсов.").Kv("Курс", Laws.Lean(x, y))
         .Mu("Влево — общее, вправо — своё; вверх — власть, вниз — свобода");
    }

    // ------------------------------------------------------------------ laws

    void BuildLaws()
    {
        Ui.Clear(_laws);
        if (!Game.I.IsReady) return;
        var nat = Game.I.State.Nat[Game.I.Viewer];
        _laws.AddChild(Kit.Para($"Законы держав всех эпох: {Laws.Groups.Length} групп, {Laws.VariantCount} вариантов. Пока это заготовки: " +
                                "держава живёт по древнему обычаю, а принимать законы, спорить о них и платить за них можно будет позже.", true));
        foreach (var cat in Laws.Categories)
        {
            var head = Ui.Text(cat, "H4");
            head.Uppercase = true;
            _laws.AddChild(Ui.Margin(Ui.HBox(10, head, Ui.Rule(Pal.Ln, 0, 2).Grow()), 0, 14, 0, 2));
            for (int gi = 0; gi < Laws.Groups.Length; gi++)
            {
                var grp = Laws.Groups[gi];
                if (grp.Category != cat) continue;
                var name = Ui.Text(grp.Name, "Strong");
                var hint = Ui.Text(grp.Hint, "SmallMu", wrap: true);
                var left = Ui.VBox(2, name, hint);
                left.CustomMinimumSize = new Vector2(280, 0);
                var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
                flow.AddThemeConstantOverride("h_separation", 6);
                flow.AddThemeConstantOverride("v_separation", 6);
                int inForce = Laws.InForce(nat, gi);
                for (int v = 0; v < grp.Variants.Length; v++) flow.AddChild(LawChip(grp, grp.Variants[v], v == inForce, grp.Variants[v].Era <= nat.Era));
                var row = Ui.Panel(St.Row().Pad(12, 10, 12, 10), Ui.HBox(18, left, flow), MouseFilterEnum.Pass);
                _laws.AddChild(row);
            }
        }
    }

    static Control LawChip(LawGroup g, LawDef d, bool inForce, bool reached)
    {
        var box = inForce ? new Box().Fill(Pal.SurfaceHover).Border(Pal.Ac).Pad(9, 4, 9, 4)
            : reached ? new Box().Fill(Pal.Surface).Border(Pal.Ln).Pad(9, 4, 9, 4)
            : new Box().Border(Pal.Ln3).Dashed(2).Pad(9, 4, 9, 4);
        var label = Ui.Text(d.Name, inForce ? "Semi" : null);
        label.Colored(inForce ? Pal.Hi : reached ? Pal.Tx : Pal.Mu);
        var era = Ui.Text(Roman(d.Era + 1), "SmallMu");
        var chip = Ui.Panel(box, Ui.HBox(6, label, era), MouseFilterEnum.Pass);
        chip.Tip(t =>
        {
            t.Title(d.Name);
            if (d.Note != null) t.Line(char.ToUpper(d.Note[0]) + d.Note[1..]);
            t.Kv("Группа", g.Name).Kv("Эпоха", Eras.Name(d.Era)).Kv("Курс", Laws.Lean(d.X, d.Y));
            if (inForce) t.Kv("Сейчас", "действует по обычаю", Pal.Ok);
            t.Mu("Заготовка: принимать законы можно будет позже");
        });
        return chip;
    }

    static string Roman(int n) => n switch { 1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", 9 => "IX", 10 => "X", _ => "XI" };

    // ------------------------------------------------------------------ the tree

    /// <summary>Course id → its icon on the card.</summary>
    static string IconOf(string id) => id switch
    {
        "foundations" => "building-bank", "one_rule" => "crown", "noble_kin" => "flag", "family_plot" => "home",
        "free_trade" => "coins", "assembly" => "users", "circle_of_equals" => "scale", "common_land" => "plant", "common_work" => "hammer",
        _ => "help-hexagon",
    };

    static readonly string[] Arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
    static readonly string[] WayNames = { "власть", "власть и знать", "своё", "своё и свобода", "свобода", "свобода и равенство", "общее", "общее и власть" };

    /// <summary>The draggable field: a world of cards at fixed places (the centre at 0,0), links and the compass behind them.</summary>
    sealed partial class TreeView : Control
    {
        const float Rx1 = 440, Ry1 = 300, Rx2 = 860, Ry2 = 580, Rx3 = 1260, Ry3 = 850;
        static readonly Vector2 Extent = new(1500, 1000);   // half the field: how far the drag goes

        readonly GovernmentScreen _owner;
        readonly Control _world = new() { MouseFilter = MouseFilterEnum.Ignore };
        readonly Links _links;
        readonly List<Card> _cards = new();
        readonly List<Teaser> _teasers = new();
        readonly Label _hint = Ui.Text("Тащите поле мышью · колесо — вверх и вниз, Shift + колесо — вбок", "SmallMu");
        bool _press, _moved;
        Vector2 _pressAt, _lastAt, _vel, _goal;
        bool _gliding;
        ulong _lastMove;
        internal bool Moved => _moved;

        public TreeView(GovernmentScreen owner)
        {
            _owner = owner;
            ClipContents = true;
            MouseFilter = MouseFilterEnum.Stop;
            AddChild(_world);
            _links = new Links(this);
            _world.AddChild(_links);
            var home = Ui.Button("В центр", "home", "Sm", () => GlideTo(Vector2.Zero), 1, 28);
            home.Tip("К основам государства", "Вернуть поле к центру");
            var corner = Ui.HBox(12, _hint, home);
            AddChild(corner);
            corner.SetAnchorsPreset(LayoutPreset.BottomRight);
            corner.GrowHorizontal = GrowDirection.Begin; corner.GrowVertical = GrowDirection.Begin;
            corner.OffsetRight = -18; corner.OffsetBottom = -14;
        }

        /// <summary>Where a card of the first rings stands on the field (the centre at 0,0, up is authority).</summary>
        static Vector2 At(int ring, int dir, float spread = 0)
        {
            if (ring == 0) return Vector2.Zero;
            float rx = ring == 1 ? Rx1 : ring == 2 ? Rx2 : Rx3, ry = ring == 1 ? Ry1 : ring == 2 ? Ry2 : Ry3;
            float a = Mathf.DegToRad(90 - 45 * dir + spread);
            return new Vector2(rx * Mathf.Cos(a), -ry * Mathf.Sin(a));
        }

        /// <summary>Stop every motion: a drag, a fling, a glide (the screen opens or closes).</summary>
        internal void Halt()
        {
            _press = _moved = _gliding = false;
            _vel = Vector2.Zero;
            MouseDefaultCursorShape = CursorShape.Arrow;
        }

        public void Build()
        {
            foreach (var c in _cards) c.QueueFree();
            foreach (var c in _teasers) c.QueueFree();
            _cards.Clear(); _teasers.Clear();
            if (!Game.I.IsReady) return;
            foreach (var v in Game.I.CourseViews())
            {
                var card = new Card(this, v);
                _world.AddChild(card);
                card.Position = (At(v.Def.Ring, v.Def.Dir) - card.Size / 2).Round();
                _cards.Add(card);
            }
            // the rings to come: two ways out of every course of the first ring, three further out
            for (int d = 0; d < 8; d++)
            {
                foreach (float sp in new[] { -11f, 11f })
                {
                    var t = new Teaser(2);
                    _world.AddChild(t);
                    t.Center = At(2, d, sp);
                    t.Position = (t.Center - t.Size / 2).Round();
                    t.From = At(1, d);
                    _teasers.Add(t);
                }
                foreach (float sp in new[] { -15f, 0f, 15f })
                {
                    var t = new Teaser(3);
                    _world.AddChild(t);
                    t.Center = At(3, d, sp);
                    t.Position = (t.Center - t.Size / 2).Round();
                    t.From = At(2, d, sp < 0 ? -11 : 11);
                    _teasers.Add(t);
                }
            }
            _links.QueueRedraw();
        }

        public void Refresh(List<Game.CourseView> views)
        {
            foreach (var c in _cards) if (c.Id < views.Count) c.Set(views[c.Id]);
            _links.QueueRedraw();
        }

        /// <summary>The first opening glides in from below, so the field shows it is a field.</summary>
        public void Greet()
        {
            _pan = new Vector2(0, -260);
            GlideTo(Vector2.Zero);
        }

        void GlideTo(Vector2 p)
        {
            _goal = p; _gliding = true; _vel = Vector2.Zero;
            if (Cli.Has("nosmooth")) { _pan = p; _gliding = false; }
        }

        public override void _GuiInput(InputEvent e)
        {
            switch (e)
            {
                case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Middle } mb:
                    if (mb.Pressed) { _press = true; _moved = false; _pressAt = _lastAt = mb.GlobalPosition; _vel = Vector2.Zero; _gliding = false; _lastMove = Time.GetTicksUsec(); }
                    else
                    {
                        _press = false;
                        if (_moved) MouseDefaultCursorShape = CursorShape.Arrow;
                        if (Time.GetTicksUsec() - _lastMove > 80_000) _vel = Vector2.Zero;   // held still before letting go: no throw
                        // _moved stays true until the next press: the card under the release reads it in the same event
                    }
                    AcceptEvent();
                    break;
                case InputEventMouseMotion mm when _press && (mm.ButtonMask & (MouseButtonMask.Left | MouseButtonMask.Middle)) == 0:
                    // no button held: the release went elsewhere (a card under it, a window switch); the drag is over
                    _press = false;
                    _vel = Vector2.Zero;
                    MouseDefaultCursorShape = CursorShape.Arrow;
                    break;
                case InputEventMouseMotion mm when _press:
                    if (!_moved && mm.GlobalPosition.DistanceTo(_pressAt) > 4) { _moved = true; MouseDefaultCursorShape = CursorShape.Drag; }
                    var shift = mm.GlobalPosition - _lastAt;   // from the positions: every event carries them
                    _lastAt = mm.GlobalPosition;
                    if (_moved)
                    {
                        _pan += shift;
                        ulong now = Time.GetTicksUsec();
                        float dt = Math.Max(1e-3f, (now - _lastMove) / 1e6f);
                        _lastMove = now;
                        _vel = dt > .08f ? shift / dt : _vel.Lerp(shift / dt, .35f);   // after a pause it starts afresh
                    }
                    AcceptEvent();
                    break;
                case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } w:
                {
                    float step = (w.ButtonIndex == MouseButton.WheelUp ? 1 : -1) * 90;
                    if (w.ShiftPressed) GlideTo(_pan + new Vector2(step, 0)); else GlideTo(_pan + new Vector2(0, step));
                    AcceptEvent();
                    break;
                }
            }
        }

        public override void _Process(double delta)
        {
            if (!IsVisibleInTree()) return;
            float dt = (float)delta;
            var lim = new Vector2(Math.Max(0, Extent.X - Size.X / 2), Math.Max(0, Extent.Y - Size.Y / 2));
            if (_gliding)
            {
                _goal = _goal.Clamp(-lim, lim);   // a wheel notch past the edge glides to the edge
                _pan = _pan.Lerp(_goal, 1 - Mathf.Exp(-9 * dt));
                if (_pan.DistanceTo(_goal) < .5f) { _pan = _goal; _gliding = false; }
            }
            else if (!_press && _vel.LengthSquared() > 1)
            {
                _pan += _vel * dt;
                _vel *= Mathf.Exp(-4.5f * dt);
            }
            // past the edge of the field it springs back
            var clamped = new Vector2(Mathf.Clamp(_pan.X, -lim.X, lim.X), Mathf.Clamp(_pan.Y, -lim.Y, lim.Y));
            if (!_press && clamped != _pan) { _pan = _pan.Lerp(clamped, 1 - Mathf.Exp(-10 * dt)); _vel *= .5f; }
            _world.Position = (Size / 2 + _pan).Round();
        }

        // ---------------------------------------------------------------- links and the compass, under the cards

        sealed partial class Links : Control
        {
            readonly TreeView _tree;
            public Links(TreeView t) { _tree = t; MouseFilter = MouseFilterEnum.Ignore; }

            public override void _Draw()
            {
                var haze = Pal.Haze;
                // a faint lattice: the field is big
                for (float y = -Extent.Y; y <= Extent.Y; y += 48)
                    for (float x = -Extent.X; x <= Extent.X; x += 48) DrawRect(new Rect2(x, y, 2, 2), new Color(haze, .05f));
                // ring guides
                foreach (var (rx, ry) in new[] { (Rx1, Ry1), (Rx2, Ry2), (Rx3, Ry3) }) Ellipse(rx, ry, new Color(haze, .05f));
                bool based = Game.I.StateFounded;
                // the compass: its arms and, once the foundations are laid, their names
                var arm = new Color(haze, based ? .12f : .06f);
                DrawRect(new Rect2(-Extent.X + 60, -1, 2 * Extent.X - 120, 2), arm);
                DrawRect(new Rect2(-1, -Extent.Y + 60, 2, 2 * Extent.Y - 120), arm);
                var font = UiFonts.Spaced(3);
                var cap = new Color(Pal.Sec, based ? .8f : .25f);
                Caption(font, based ? "↑  ВЛАСТЬ" : "?", new Vector2(0, -Extent.Y + 40), cap);
                Caption(font, based ? "СВОБОДА  ↓" : "?", new Vector2(0, Extent.Y - 28), cap);
                Caption(font, based ? "←  ОБЩЕЕ · ЛЕВЫЕ" : "?", new Vector2(-Extent.X + 170, -10), cap);
                Caption(font, based ? "ПРАВЫЕ · СВОЁ  →" : "?", new Vector2(Extent.X - 170, -10), cap);

                // teasers: dotted ways out
                foreach (var t in _tree._teasers) Dotted(t.From, t.Center, new Color(haze, t.Ring == 2 ? .16f : .09f));
                // the centre to the first ring
                Card root = null;
                foreach (var c in _tree._cards) if (c.Ring == 0) root = c;
                if (root == null) return;
                foreach (var c in _tree._cards)
                {
                    if (c.Ring != 1) continue;
                    var st = c.State;
                    var col = st switch
                    {
                        Game.CourseState.Adopted => Pal.Ac,
                        Game.CourseState.Adopting => Pal.Ok,
                        Game.CourseState.Open => new Color(Pal.Hi, .55f),
                        Game.CourseState.Closed => new Color(Pal.Bad, .35f),
                        _ => new Color(haze, .14f),
                    };
                    Vector2 a = Vector2.Zero, b = c.Center;
                    DrawLine(a + new Vector2(3, 3), b + new Vector2(3, 3), new Color(Pal.Shadow, Pal.Light ? .25f : .5f), 4);
                    DrawLine(a, b, col, st is Game.CourseState.Adopted or Game.CourseState.Adopting ? 4 : 2);
                }
            }

            void Caption(Font f, string s, Vector2 at, Color c) => DrawString(f, at - new Vector2(200, 0), s, HorizontalAlignment.Center, 400, UiFonts.Small, c);

            void Ellipse(float rx, float ry, Color c)
            {
                for (int k = 0; k < 180; k++)
                {
                    float a = Mathf.Tau * k / 180;
                    DrawRect(new Rect2(rx * Mathf.Cos(a), ry * Mathf.Sin(a), 2, 2), c);
                }
            }

            void Dotted(Vector2 a, Vector2 b, Color c)
            {
                float len = a.DistanceTo(b);
                for (float u = 0; u < len; u += 10) { var p = a.Lerp(b, u / len); DrawRect(new Rect2(p.X - 1, p.Y - 1, 3, 3), c); }
            }
        }

        // ---------------------------------------------------------------- a course

        sealed partial class Card : Control
        {
            readonly TreeView _tree;
            Game.CourseView _v;
            readonly TextureRect _icon = new() { MouseFilter = MouseFilterEnum.Ignore, StretchMode = TextureRect.StretchModeEnum.KeepCentered };
            readonly Label _way = Ui.Text("", "Cap");
            readonly Label _name = Ui.Text("", "Strong");
            readonly Label _quote = Ui.Text("", "SmallMu", wrap: true);
            readonly Label _by = Ui.Text("", "SmallMu");
            readonly Label _status = Ui.Text("", "SmallMu");
            bool _hover;

            public int Id => _v.Id;
            public int Ring => _v.Def.Ring;
            public Game.CourseState State => _v.State;
            public Vector2 Center => Position + Size / 2;

            public Card() { }
            public Card(TreeView tree, Game.CourseView v)
            {
                _tree = tree;
                _v = v;
                bool root = v.Def.Ring == 0;
                Size = CustomMinimumSize = root ? new Vector2(330, 156) : new Vector2(270, 136);
                MouseFilter = MouseFilterEnum.Pass;
                _icon.Position = new Vector2(14, 14); _icon.Size = new Vector2(32, 32);
                AddChild(_icon);
                _way.Position = new Vector2(56, 10); _way.Size = new Vector2(Size.X - 70, 16); _way.ClipText = true;
                _name.Position = new Vector2(56, 26); _name.Size = new Vector2(Size.X - 70, 22); _name.ClipText = true;
                _quote.Position = new Vector2(16, 54); _quote.Size = new Vector2(Size.X - 32, root ? 52 : 40);
                _by.Position = new Vector2(16, root ? 104 : 92); _by.Size = new Vector2(Size.X - 32, 16); _by.ClipText = true;
                _status.Position = new Vector2(16, Size.Y - 26); _status.Size = new Vector2(Size.X - 32, 18); _status.ClipText = true;
                AddChild(_way); AddChild(_name); AddChild(_quote); AddChild(_by); AddChild(_status);
                MouseEntered += () => { _hover = true; QueueRedraw(); };
                MouseExited += () => { _hover = false; QueueRedraw(); };
                GuiInput += e =>
                {
                    // a release that did not drag the field is a click
                    // a blocked card answers with the reason (AdoptCourse refuses with it)
                    if (e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } && !_tree.Moved
                        && _v.State is Game.CourseState.Open or Game.CourseState.Locked or Game.CourseState.Closed)
                        Game.I.AdoptCourse(_v.Id);
                };
                this.Tip(Tip);
                Set(v);
            }

            public void Set(Game.CourseView v)
            {
                _v = v;
                var d = v.Def;
                bool hidden = v.State == Game.CourseState.Hidden;
                MouseDefaultCursorShape = v.State == Game.CourseState.Open ? CursorShape.PointingHand : CursorShape.Arrow;
                _icon.Texture = Icons.Get(hidden ? "help-hexagon" : IconOf(d.Id), 2);
                _icon.SelfModulate = v.State switch
                {
                    Game.CourseState.Adopted => Pal.Ac,
                    Game.CourseState.Open or Game.CourseState.Adopting => Pal.Hi,
                    _ => Pal.Mu2,
                };
                _way.Text = d.Ring == 0 ? "начало" : hidden ? "" : $"{Arrows[d.Dir]}  {WayNames[d.Dir]}";
                _name.Text = hidden ? "" : d.Name;
                _name.Colored(v.State switch
                {
                    Game.CourseState.Open or Game.CourseState.Adopting => Pal.Hi,
                    Game.CourseState.Adopted => Pal.Tx,
                    _ => Pal.Mu,
                });
                _quote.Text = hidden ? "" : $"«{d.Quote}»";
                _icon.Visible = !hidden;
                _by.Text = hidden ? "" : "— " + d.QuoteBy;
                _status.Text = v.State switch
                {
                    Game.CourseState.Adopted => "принят · " + d.Effect,
                    Game.CourseState.Adopting => $"принимается · {v.Cycles * 100 / Math.Max(1, v.Total)}% · ≈{v.SecondsLeft} с",
                    Game.CourseState.Open => $"принять · ≈{v.SecondsLeft} с · +{v.Gold} золота",
                    Game.CourseState.Closed => "путь закрыт",
                    Game.CourseState.Hidden => "откроется после основ государства",
                    _ => d.Ring == 0 ? "сначала основайте столицу" : "сначала основы государства",
                };
                _status.Colored(v.State switch
                {
                    Game.CourseState.Adopting => Pal.Ok,
                    Game.CourseState.Closed => Pal.Bad,
                    Game.CourseState.Open => Pal.Ac,
                    _ => Pal.Mu,
                });
                QueueRedraw();
            }

            public override void _Process(double delta)
            {
                if (_v.State == Game.CourseState.Adopting && IsVisibleInTree()) QueueRedraw();   // the frame breathes while it is taken
            }

            public override void _Draw()
            {
                var r = new Rect2(Vector2.Zero, Size);
                var st = _v.State;
                Color fill = st switch
                {
                    Game.CourseState.Adopted => Pal.Surface,
                    Game.CourseState.Adopting => Pal.SurfaceHover,
                    Game.CourseState.Open => _hover ? Pal.SurfaceHover : Pal.Surface,
                    _ => Pal.Well,
                };
                Color frame = st switch
                {
                    Game.CourseState.Adopting => Pal.Ok,
                    Game.CourseState.Open => _hover ? Pal.Hi : Pal.Ac,
                    Game.CourseState.Adopted => Pal.Ln3,
                    Game.CourseState.Closed => Pal.BadFill,
                    _ => Pal.Ln,
                };
                if (st == Game.CourseState.Adopting)
                {
                    float pulse = .55f + .45f * Mathf.Sin((float)(Time.GetTicksMsec() / 260.0));
                    frame = new Color(frame, pulse);
                }
                DrawRect(new Rect2(5, 5, Size.X, Size.Y), Pal.Shadow);   // hard pixel shadow
                DrawRect(r, fill);
                int bw = Ring == 0 ? 3 : 2;
                DrawRect(new Rect2(0, 0, Size.X, bw), frame); DrawRect(new Rect2(0, Size.Y - bw, Size.X, bw), frame);
                DrawRect(new Rect2(0, 0, bw, Size.Y), frame); DrawRect(new Rect2(Size.X - bw, 0, bw, Size.Y), frame);
                if (st == Game.CourseState.Adopted)
                {
                    DrawRect(new Rect2(0, 0, 5, Size.Y), Pal.Ac);
                    // a tick in the top right corner
                    float x = Size.X - 22, y = 12;
                    DrawRect(new Rect2(x, y + 4, 3, 3), Pal.Ac); DrawRect(new Rect2(x + 3, y + 7, 3, 3), Pal.Ac);
                    DrawRect(new Rect2(x + 6, y + 4, 3, 3), Pal.Ac); DrawRect(new Rect2(x + 9, y + 1, 3, 3), Pal.Ac);
                }
                if (st == Game.CourseState.Hidden)
                {
                    for (float y = 4; y < Size.Y - 4; y += 4)
                        for (float x = 4 + (y / 4 % 2) * 2; x < Size.X - 4; x += 4) DrawRect(new Rect2(x, y, 1, 1), new Color(Pal.Haze, .07f));
                    DrawString(UiFonts.Spaced(1), new Vector2(0, Size.Y / 2 + 18), "?", HorizontalAlignment.Center, Size.X, 48, new Color(Pal.Ln2, .9f));
                }
                if (st == Game.CourseState.Adopting)
                {
                    float w = (Size.X - 12) * Mathf.Clamp(_v.Cycles / (float)Math.Max(1, _v.Total), 0, 1);
                    DrawRect(new Rect2(6, Size.Y - 8, Size.X - 12, 4), Pal.Well);
                    DrawRect(new Rect2(6, Size.Y - 8, Mathf.Round(w), 4), Pal.Ok);
                }
            }

            void Tip(TipCard t)
            {
                var v = _v; var d = v.Def;
                if (v.State == Game.CourseState.Hidden)
                {
                    t.Title("Неизвестный путь").Mu("Заложите «Основы государства» в центре: тогда станет видно, куда ведут пути вокруг");
                    return;
                }
                t.Title(d.Name).Line($"«{d.Quote}» — {d.QuoteBy}").Kv("Даёт", d.Effect).Kv("В казну", $"+{v.Gold} золота при принятии");
                if (d.Ring > 0) t.Kv("Курс", Laws.Lean(d.X, d.Y)).Kv("Закрывает", Facing(d));
                t.Kv("Срок", $"≈{v.SecondsLeft} с").Kv("Дальше", d.Opens);
                if (v.State == Game.CourseState.Open) t.Mu(Game.I.CourseNow >= 0 ? "Сейчас принимается другой курс: один за раз" : "Нажмите, чтобы начать принимать");
                else if (v.State == Game.CourseState.Adopting) t.Mu("Принимается. Оставить можно кнопкой в заголовке");
                else if (v.State != Game.CourseState.Adopted && Game.I.CourseProblem(v.Id) is string why) t.Mu(why);
            }

            static string Facing(CourseDef d)
            {
                var names = new List<string>();
                foreach (var o in Politics.All)
                    if (o.Ring == d.Ring && o.Dir >= 0 && ((d.Dir - o.Dir + 8) % 8) is 3 or 4 or 5) names.Add($"«{o.Name}»");
                return string.Join(", ", names);
            }
        }

        // ---------------------------------------------------------------- a course still to come

        sealed partial class Teaser : Control
        {
            public readonly int Ring;
            public Vector2 Center, From;

            public Teaser() { }
            public Teaser(int ring)
            {
                Ring = ring;
                Size = CustomMinimumSize = ring == 2 ? new Vector2(190, 84) : new Vector2(140, 64);
                MouseFilter = MouseFilterEnum.Pass;
                this.Tip("Путь впереди", "Дальше дерево растёт: эти курсы проработаем позже.");
            }

            public override void _Draw()
            {
                float a = Ring == 2 ? .8f : .5f;
                DrawRect(new Rect2(4, 4, Size.X, Size.Y), new Color(Pal.Shadow, Pal.Shadow.A * a));
                DrawRect(new Rect2(Vector2.Zero, Size), new Color(Pal.Well, a));
                var f = new Color(Pal.Ln, a);
                for (float x = 0; x < Size.X; x += 8) { DrawRect(new Rect2(x, 0, 4, 2), f); DrawRect(new Rect2(x, Size.Y - 2, 4, 2), f); }
                for (float y = 0; y < Size.Y; y += 8) { DrawRect(new Rect2(0, y, 2, 4), f); DrawRect(new Rect2(Size.X - 2, y, 2, 4), f); }
                DrawString(UiFonts.Spaced(1), new Vector2(0, Size.Y / 2 + (Ring == 2 ? 12 : 9)), "???", HorizontalAlignment.Center, Size.X, Ring == 2 ? 30 : 22, new Color(Pal.Ln2, a));
            }
        }
    }

    // ------------------------------------------------------------------ the compass in the header

    sealed partial class MiniCompass : Control
    {
        public MiniCompass()
        {
            CustomMinimumSize = new Vector2(44, 44);
            MouseFilter = MouseFilterEnum.Pass;
        }

        public override void _Draw()
        {
            var s = Size;
            DrawRect(new Rect2(Vector2.Zero, s), Pal.Well);
            var ln = Pal.Ln;
            DrawRect(new Rect2(0, 0, s.X, 2), ln); DrawRect(new Rect2(0, s.Y - 2, s.X, 2), ln);
            DrawRect(new Rect2(0, 0, 2, s.Y), ln); DrawRect(new Rect2(s.X - 2, 0, 2, s.Y), ln);
            DrawRect(new Rect2(s.X / 2 - 1, 4, 2, s.Y - 8), new Color(Pal.Haze, .15f));
            DrawRect(new Rect2(4, s.Y / 2 - 1, s.X - 8, 2), new Color(Pal.Haze, .15f));
            var g = Game.I;
            if (!g.IsReady || !g.StateFounded)
            {
                DrawString(UiFonts.Spaced(1), new Vector2(0, s.Y / 2 + 7), "?", HorizontalAlignment.Center, s.X, 18, Pal.Mu);
                return;
            }
            var (x, y) = g.CompassPosition;
            float px = s.X / 2 + Mathf.Clamp(x, -3, 3) * 6, py = s.Y / 2 - Mathf.Clamp(y, -3, 3) * 6;
            DrawRect(new Rect2(px - 3, py - 3, 7, 7), Pal.Shadow);
            DrawRect(new Rect2(px - 3, py - 4, 6, 6), Pal.Ac);
        }
    }
}
