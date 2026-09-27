using System;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// #top — 54px ledger across the screen: nation plate (framed pixel flag, name, government · era, 3px nation rule),
/// resources with captions, screen buttons (trophy = leaderboard), clock plate (round pause, date, speed pips) and
/// the real-time session counter with its escalating jokes.
/// </summary>
public partial class TopBar : PanelContainer
{
    public const int Height = 54;
    public Button Trophy { get; private set; }
    public event Action LeaderboardToggled;
    public event Action PauseClicked;

    readonly FlagView _flag = new();
    Label _name, _gov, _era;
    PanelContainer _nation;
    Box _nationBox;
    readonly Res[] _res = new Res[5];
    PanelContainer _screensBox;
    readonly Button[] _screenBtns = new Button[6];
    PanelContainer _clock;
    Box _clockBox;
    Label _date;
    Button _pause;
    readonly SpeedPips _pips = new();
    Button _session;

    bool _incomeKnown;
    int _lastMinute = -1;

    public TopBar()
    {
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(0, Height);
        AddThemeStyleboxOverride("panel", new Box().Fill(Pal.Hd1, Pal.Hd2).Border(Pal.Ln2, 0, 0, 0, 1)
            .Rule(0, 1, Colors.White).Rule(4, 1, Pal.Ln, fromBottom: true).Rule(1, 3, Pal.Hd2, fromBottom: true)
            .Shadow(Pal.Shade(.10f), 12, 5).Shadow(Pal.Shade(.08f), 3, 1).Pad(0, 0, 10, 0));

        var row = Ui.HBox(0);
        AddChild(row);
        row.AddChild(BuildNation());
        var resBar = Ui.HBox(0);
        _res[0] = new Res("coins", "Казна"); _res[1] = new Res("flask", "Наука"); _res[2] = new Res("users", "Население");
        _res[3] = new Res("scale", "Стабильность"); _res[4] = new Res("feather", "Влияние");
        foreach (var r in _res) resBar.AddChild(r.Root);
        row.AddChild(resBar);
        row.AddChild(Ui.Expand());
        row.AddChild(BuildScreens());
        row.AddChild(Ui.Gap(12, 0));
        row.AddChild(BuildClock());
        row.AddChild(Ui.Gap(4, 0));
        row.AddChild(BuildSession());
        WireTips();
    }

    // ---------------- construction ----------------
    Control BuildNation()
    {
        _name = Ui.Text("Ардания", "NName");
        _gov = Ui.Text("Вождество", "SmallMu");
        _era = Ui.Text("Древний мир", "SmallMu");
        var sub = Ui.HBox(6, _gov, new Dot(), _era);
        var text = Ui.VBox(1, _name, sub).Center();
        _nationBox = new Box().Fill(Colors.White, Pal.Plate2).Border(Pal.Ln2, 0, 0, 1, 0).Rule(1, 4, Pal.Plate2, fromBottom: true)
            .Accent(Colors.Transparent).Pad(14, 0, 22, 0);
        _nation = Ui.Panel(_nationBox, Ui.HBox(13, _flag, text), MouseFilterEnum.Stop);
        _nation.MouseDefaultCursorShape = CursorShape.PointingHand;
        _nation.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && Game.I.IsReady)
            {
                int cap = Game.I.State.NationCapital[GameState.LocalPlayer];
                Game.I.JumpCamera(new Vector2(Game.I.World.PCX[cap], Game.I.World.PCY[cap]));
            }
        };
        return _nation;
    }

    Control BuildScreens()
    {
        var h = Ui.HBox(0);
        (string icon, string name)[] screens = { ("atom", "Технологии"), ("building-bank", "Политика и законы"), ("building-store", "Рынок"), ("affiliate", "Дипломатия"), ("sun", "Религия") };
        for (int i = 0; i < screens.Length; i++)
        {
            var (icon, name) = screens[i];
            var b = Ui.IconButton(icon, i == 0 ? "SegStart" : "Seg", 38, 34, 18, () => Game.I.ShowToast($"Экран «{name}» — нарисуем следующим"));
            b.MouseFilter = MouseFilterEnum.Stop;
            b.Tip(name, null, "Экран в разработке");
            if (i > 0) h.AddChild(Ui.Rule(Pal.Ln, 1, 34));
            h.AddChild(b);
            _screenBtns[i] = b;
        }
        h.AddChild(Ui.Rule(Pal.Ln2, 1, 34));
        Trophy = Ui.IconButton("trophy", "SegEnd", 38, 34, 18, () => LeaderboardToggled?.Invoke());
        Trophy.MouseFilter = MouseFilterEnum.Stop;
        Trophy.Tip("Таблица лидеров", null, "Чужие державы появляются в ней после встречи");
        _screenBtns[5] = Trophy;
        h.AddChild(Trophy);
        _screensBox = Ui.Panel(new Box().Fill(Colors.White).Border(Pal.Ln2).Radius(St.R).Shadow(Pal.Shade(.06f), 1, 1).Pad(1), h, MouseFilterEnum.Stop);
        _screensBox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return _screensBox;
    }

    Control BuildClock()
    {
        _pause = Ui.IconButton("player-pause-filled", "Pause", 32, 32, 14, () => PauseClicked?.Invoke());
        _pause.MouseFilter = MouseFilterEnum.Stop;
        _pause.Center();
        _date = Ui.Text("1250 до н. э.", "Date");
        _pips.SpeedPicked += s => Game.I.SetSpeed(s);
        var right = Ui.VBox(4, _date, _pips).Center();
        _clockBox = new Box().Fill(Colors.White, Pal.Plate2).Border(Pal.Ln2, 1, 0, 1, 1).Rule(1, 4, Pal.Plate2, fromBottom: true).Pad(14, 0, 18, 0);
        _clock = Ui.Panel(_clockBox, Ui.HBox(11, _pause, right), MouseFilterEnum.Stop);
        _clock.CustomMinimumSize = new Vector2(184, 0);
        return _clock;
    }

    Control BuildSession()
    {
        _session = Ui.Button("0:00", "hourglass", "Session", OnSessionClick, 17, 32);
        _session.MouseFilter = MouseFilterEnum.Stop;
        _session.Center();
        return _session;
    }

    void WireTips()
    {
        _nation.Tip(t =>
        {
            var n = Data.Nations[GameState.LocalPlayer];
            t.Title(n.Name).Line($"{n.Gov} · Древний мир").Mu("Нажмите, чтобы показать столицу");
        });
        _res[0].Root.Tip(t =>
        {
            t.Title("Казна");
            if (!Game.I.IsReady || !_incomeKnown) { t.Mu("Доход появится после первого года"); return; }
            var s = Game.I.State;
            t.Line($"Налоги {Fmt.Signed(s.LastTaxes)} · Содержание {Fmt.Signed(-s.LastUpkeep)}")
             .Kv("Итого за год", Fmt.Signed(s.LastIncome, 1), s.LastIncome >= 0 ? Pal.Ok : Pal.Bad)
             .Mu("Тратится на присоединение земель, постройки и геологов");
        });
        _res[1].Root.Tip("Наука", "Жрецы +22 · Святилища +10 · Соседи +6", "Экран технологий — следующим");
        _res[2].Root.Tip("Население", "Во всех провинциях державы");
        _res[3].Root.Tip("Стабильность", "Довольство попов · законы · вера", "Среднее довольство провинций державы");
        _res[4].Root.Tip("Влияние", "Тратится на законы и дипломатию");
        _clock.Tip(t => t.Title("Время").Line("Древний мир: 1 день игры ≈ 1 год.").Mu("Пробел — пауза, 1–5 — скорость"));
        _pause.Tip(t => t.Title(Game.I.IsReady && Game.I.State.Paused ? "Продолжить" : "Пауза").Mu("Пробел"));
        _pips.Tip(t => t.Title("Скорость " + (Game.I.IsReady ? Game.I.State.Speed : 2) + " из 5").Mu("Клавиши 1–5"));
        _session.Tip("Сколько ты уже играешь", null, "Нажми — совет придворных");
    }

    // ---------------- refresh ----------------
    public void OnWorldReady()
    {
        var n = Data.Nations[GameState.LocalPlayer];
        var c = Pal.Nation(GameState.LocalPlayer);
        _flag.SetNation(c);
        _nationBox.SetAccent(c);
        _nation.QueueRedraw();
        _name.Text = n.Name;
        _gov.Text = n.Gov;
        _incomeKnown = false;
        RefreshResources();
        RefreshClock();
    }

    public void OnYearTick()
    {
        _incomeKnown = true;
        RefreshResources();
        RefreshDate();
    }

    public void RefreshResources()
    {
        if (!Game.I.IsReady) return;
        var s = Game.I.State;
        double pop = 0, mood = 0;
        for (int p = 0; p < s.Owner.Length; p++)
            if (s.Owner[p] == GameState.LocalPlayer) { pop += s.Pop[p]; mood += s.Mood[p] * s.Pop[p]; }
        double income = s.LastIncome;
        _res[0].Set(Fmt.Int(s.Gold), _incomeKnown ? Fmt.Signed(income, Math.Abs(income) < 10 ? 1 : 0) : null, income >= 0 ? Pal.Ok : Pal.Bad);
        _res[1].Set("38", "+4", Pal.Ok);
        _res[2].Set(Fmt.Pop(pop), null, default);
        _res[3].Set(pop > 0 ? $"{Math.Round(mood / pop)}%" : "—", null, default);
        _res[4].Set("14", null, default);
    }

    public void RefreshClock()
    {
        if (!Game.I.IsReady) return;
        var s = Game.I.State;
        _pause.ThemeTypeVariation = s.Paused ? "PauseRed" : "Pause";
        _pause.Icon = Icons.Get(s.Paused ? "player-play-filled" : "player-pause-filled", 14);
        _pips.SetSpeed(s.Speed);
        RefreshDate();
    }

    void RefreshDate()
    {
        var s = Game.I.State;
        _date.Text = Fmt.Year(s.Year);
        if (s.Paused) _date.Colored(Pal.Bad); else _date.RemoveThemeColorOverride("font_color");
    }

    internal Control DebugTarget(string name) => name switch
    {
        "gold" => _res[0].Root, "sci" => _res[1].Root, "pop" => _res[2].Root, "stab" => _res[3].Root, "infl" => _res[4].Root,
        "clock" => _clock, "pause" => _pause, "pips" => _pips, "session" => _session, "nation" => _nation, "screen" => _screenBtns[0], "trophy" => Trophy,
        _ => null,
    };

    public void SetLeaderboardOpen(bool open) => Trophy.ThemeTypeVariation = open ? "SegEndOn" : "SegEnd";

    /// <summary>≤1440px: tighter paddings; ≤1180px: captions and deltas hidden (CSS media queries).</summary>
    public void SetDensity(bool compact, bool tiny)
    {
        foreach (var r in _res) r.SetDensity(compact, tiny);
        foreach (var b in _screenBtns) b.CustomMinimumSize = new Vector2(compact ? 34 : 38, 34);
        _clock.CustomMinimumSize = new Vector2(compact ? 170 : 184, 0);
        _clockBox.Pad(compact ? 12 : 14, 0, compact ? 14 : 18, 0);
        _nationBox.Pad(14, 0, compact ? 18 : 22, 0);
        _clock.UpdateMinimumSize();
        _nation.UpdateMinimumSize();
    }

    // ---------------- session timer ----------------
    public int SessionMinuteOffset { get; set; }
    public int SessionMinutes => (int)(Time.GetTicksMsec() / 60000) + SessionMinuteOffset;

    public override void _Process(double delta)
    {
        int m = SessionMinutes;
        if (m == _lastMinute) return;
        bool first = _lastMinute < 0;
        _lastMinute = m;
        ((TextButton)_session).Caption = $"{m / 60}:{m % 60:00}";
        if (!first && m > 0 && m % 60 == 0) Game.I.ShowToast(SessionJokes.For(m), 6);
    }

    void OnSessionClick() => Game.I.ShowToast(SessionJokes.For(SessionMinutes), 5);

    /// <summary>One resource cell: icon · CAPTION over value + delta, hairline divider on the right.</summary>
    sealed class Res
    {
        public readonly PanelContainer Root;
        readonly Box _normal, _hover;
        readonly TextureRect _icon;
        readonly Label _cap, _val, _delta;
        readonly HBoxContainer _row;
        readonly string _iconName;

        public Res(string icon, string caption)
        {
            _iconName = icon;
            _normal = new Box().Border(Pal.Ln, 0, 0, 1, 0).Pad(15, 0, 17, 0);
            _hover = new Box().Fill(Pal.White(.85f)).Border(Pal.Ln, 0, 0, 1, 0).Pad(15, 0, 17, 0);
            _icon = Ui.Icon(icon, 21);
            _cap = Ui.Cap(caption);
            _val = Ui.Text("0", "Val");
            _delta = Ui.Text("", "Delta");
            _delta.VerticalAlignment = VerticalAlignment.Bottom;
            _val.VerticalAlignment = VerticalAlignment.Bottom;
            var values = Ui.HBox(5, _val, Ui.Margin(_delta, 0, 0, 0, 1));
            var col = Ui.VBox(0, _cap, values).Center();
            _row = Ui.HBox(10, _icon, col);
            Root = Ui.Panel(_normal, _row, MouseFilterEnum.Stop);
            Root.MouseEntered += () => Root.AddThemeStyleboxOverride("panel", _hover);
            Root.MouseExited += () => Root.AddThemeStyleboxOverride("panel", _normal);
        }

        public void Set(string value, string delta, Color deltaColor)
        {
            _val.Text = value;
            _delta.Visible = delta != null && !_tiny;
            if (delta != null) { _delta.Text = delta; _delta.Colored(deltaColor); }
            _hasDelta = delta != null;
        }

        bool _tiny, _hasDelta;
        public void SetDensity(bool compact, bool tiny)
        {
            _tiny = tiny;
            _cap.Visible = !tiny;
            _delta.Visible = _hasDelta && !tiny;
            int l = compact ? 11 : 15, r = compact ? 12 : 17;
            _normal.Pad(l, 0, r, 0); _hover.Pad(l, 0, r, 0);
            _row.AddThemeConstantOverride("separation", compact ? 8 : 10);
            int size = compact ? 19 : 21;
            _icon.Texture = Icons.Get(_iconName, size);
            _icon.CustomMinimumSize = new Vector2(size, size);
            Root.UpdateMinimumSize();
        }
    }
}

/// <summary>GDD §9.8: the longer you play, the more insistent the court's advice to take a break.</summary>
public static class SessionJokes
{
    static readonly string[][] Tiers =
    {
        new[] { "Всего {0} на троне. Империи не строятся за один вечер — но попробовать можно.", "{0} у власти. Советники довольны, чай ещё горячий." },
        new[] { "Правитель, ты на троне уже {0}. Народ просит тебя попить чаю.", "{0} без перерыва. Даже жрецы Солнца спят по ночам.", "{0}. Самое время размять ноги — держава подождёт на паузе." },
        new[] { "{0}. «Ещё один год» — так говорил каждый павший император.", "{0}. Летописцы устали записывать. Может, отдохнёшь?", "{0}. Твой конь-советник настоятельно рекомендует прогулку." },
        new[] { "{0}! Сделай отдых, пожалуйста. Мы серьёзно.", "{0}. Ещё один ход, говоришь? Мы это уже слышали. Трижды.", "{0}. Солнце уже взошло и зашло. Держава простоит без тебя до завтра." },
    };

    public static string For(int minutes)
    {
        int tier = minutes < 30 ? 0 : minutes < 90 ? 1 : minutes < 180 ? 2 : 3;
        var list = Tiers[tier];
        return string.Format(list[(minutes / 7) % list.Length], Fmt.Duration(minutes));
    }
}
