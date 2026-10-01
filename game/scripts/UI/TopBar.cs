using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// The 52px pixel bar across the top: nation plate (framed pixel flag, name, era), resources (26px icons, spaced
/// captions, values, deltas), square screen buttons (trophy = leaderboard), the clock (pause, a two-line date in a
/// fixed-width slot so month names never shift the bar, 5 speed bars) and the real-time session counter.
/// Density tiers: ≤1440 px compact paddings; &lt;1280 px captions, deltas and the session counter fold away.
/// </summary>
public partial class TopBar : PanelContainer
{
    public const int Height = 52;
    public Button Trophy { get; private set; }
    public event Action LeaderboardToggled;
    public event Action TechToggled;
    public event Action PolicyToggled;
    public event Action WondersToggled;
    public Button TechButton => _screenBtns[0];
    public event Action PauseClicked;

    readonly FlagView _flag = new();
    Label _name, _era;
    PanelContainer _nation;
    Box _nationBox, _nationHover;
    readonly Res[] _res = new Res[5];
    HBoxContainer _screens;
    readonly Button[] _screenBtns = new Button[7];
    PanelContainer _clock;
    Box _clockBox;
    Label _month, _year;
    Control _dateSlot;
    Button _pause;
    readonly SpeedPips _pips = new();
    Button _session;
    Control _sessionGap;

    bool _incomeKnown;
    int _lastMinute = -1;

    public TopBar()
    {
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(0, Height);
        AddThemeStyleboxOverride("panel", new Box().Fill(Pal.Bar).Dither(Pal.Band, Pal.Bar, Height - 2)
            .Grain().Border(Pal.Ln2, 0, 0, 0, 2).Shadow(0, 4).Pad(0, 0, 8, 0));

        var row = Ui.HBox(0);
        AddChild(row);
        row.AddChild(BuildNation());
        _res[0] = new Res("coins", "Казна"); _res[1] = new Res("flask", "Наука"); _res[2] = new Res("users", "Население");
        _res[3] = new Res("scale", "Стабильность"); _res[4] = new Res("building-warehouse", "Материалы");
        foreach (var r in _res) row.AddChild(r.Root);
        row.AddChild(Ui.Expand());
        row.AddChild(BuildScreens());
        row.AddChild(Ui.Gap(14, 0));
        row.AddChild(BuildClock());
        _sessionGap = Ui.Gap(6, 0);
        row.AddChild(_sessionGap);
        row.AddChild(BuildSession());
        WireTips();
    }

    // ---------------- construction ----------------
    Control BuildNation()
    {
        _name = Ui.Text("Ардания", "NName");
        _name.Uppercase = true;
        _era = Ui.Text("Древний мир", "Kick");
        _era.Uppercase = true;
        var text = Ui.VBox(1, _name, _era).Center();
        _nationBox = NationBox(Pal.Bar, false);
        _nationHover = NationBox(Pal.SurfaceHover, true);
        _nation = Ui.Panel(_nationBox, Ui.HBox(12, _flag, text), MouseFilterEnum.Stop);
        _nation.MouseDefaultCursorShape = CursorShape.PointingHand;
        _nation.MouseEntered += () => _nation.AddThemeStyleboxOverride("panel", _nationHover);
        _nation.MouseExited += () => _nation.AddThemeStyleboxOverride("panel", _nationBox);
        _nation.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && Game.I.IsReady)
            {
                int cap = Scouts.Capital(Game.I.State, GameState.LocalPlayer);   // the camp while nomadic
                if (cap < 0) return;
                Game.I.JumpCamera(new Vector2(Game.I.World.PCX[cap], Game.I.World.PCY[cap]));
            }
        };
        return _nation;
    }

    /// <summary>Nation plate: a slightly darker slab with the 2px divider; hover lifts it and adds the left accent.</summary>
    static Box NationBox(Color fill, bool hover)
    {
        var b = new Box().Fill(fill).Border(Pal.Ln2, 0, 0, 2, 0).Pad(12, 0, 20, 0);
        if (hover) b.AccentLeft(Pal.Ac, 4);
        return b;
    }

    Control BuildScreens()
    {
        _screens = Ui.HBox(2);
        (string icon, string name)[] screens = { ("atom", "Технологии"), ("building-bank", "Политика и законы"), ("building-store", "Рынок"), ("affiliate", "Дипломатия"), ("sun", "Религия") };
        for (int i = 0; i < screens.Length; i++)
        {
            var (icon, name) = screens[i];
            var b = i switch
            {
                0 => Ui.IconButton(icon, "Ib", 36, 34, 2, () => TechToggled?.Invoke()),
                1 => Ui.IconButton(icon, "Ib", 36, 34, 2, () => PolicyToggled?.Invoke()),
                _ => Ui.IconButton(icon, "Ib", 36, 34, 2, () => Game.I.ShowToast($"Экран «{name}» — нарисуем следующим")),
            };
            b.MouseFilter = MouseFilterEnum.Stop;
            if (i == 0) b.Tip(t =>
            {
                t.Title("Технологии").Mu("Клавиша T — дерево технологий");
                var g = Game.I;
                if (!g.IsReady) return;
                var (known, needed, _) = g.EraKnowledge;
                int r = g.Researching;
                t.Line(r >= 0 ? $"Изучается: «{Techs.All[r].Name}»" : g.ResearchIdle ? "Ничего не изучается — выберите технологию" : "Всё доступное изучено");
                if (needed > 0) t.Kv($"До эпохи «{g.NextEraName}»", $"{known} из {needed}", known >= needed ? Pal.Ok : Pal.Hi);
            });
            else if (i == 1) b.Tip(t =>
            {
                t.Title("Политика").Mu("Бюджет, предел управления и указы");
                var g = Game.I;
                if (!g.IsReady) return;
                var (prov, limit, over) = g.Admin;
                t.Kv("Провинции", $"{prov} из {limit}", over > 0 ? Pal.Bad : Pal.Hi);
                if (over > 0) t.Kv("Перерасширение", $"{over}%", Pal.Bad);
                t.Kv("Указы", $"{g.EdictsActive} из {g.EdictSlots}", g.EdictsActive < g.EdictSlots ? Pal.Warn : Pal.Hi);
            });
            else b.Tip(name, null, "Экран в разработке");
            _screens.AddChild(b);
            _screenBtns[i] = b;
        }
        _screens.AddChild(Ui.Margin(Ui.Rule(Pal.Ln2, 2, 22), 6, 0, 6, 0));
        Trophy = Ui.IconButton("trophy", "Ib", 36, 34, 2, () => LeaderboardToggled?.Invoke());
        Trophy.MouseFilter = MouseFilterEnum.Stop;
        Trophy.Tip("Таблица лидеров", null, "Чужие державы появляются в ней после встречи");
        WondersButton = Ui.IconButton("diamond", "Ib", 36, 34, 2, () => WondersToggled?.Invoke());
        WondersButton.MouseFilter = MouseFilterEnum.Stop;
        WondersButton.Tip(t =>
        {
            t.Title("Чудеса света").Mu("Каждое чудо стоит в мире одно: кто достроил первым, тот и владеет");
            var g = Game.I;
            if (!g.IsReady) return;
            t.Kv("Слава", g.Glory.ToString(), Pal.Hi);
            int wd = g.BuildingWonder;
            t.Kv("Строится", wd >= 0 ? $"{Wonders.All[wd].Name} · {g.WonderProgressPermille / 10}%" : "ничего", wd >= 0 ? Pal.Ok : Pal.Warn);
        });
        _screenBtns[5] = WondersButton;
        _screens.AddChild(WondersButton);
        _screenBtns[6] = Trophy;
        _screens.AddChild(Trophy);
        _screens.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return _screens;
    }

    Control BuildClock()
    {
        _pause = Ui.IconButton("player-pause-filled", "Pause", 34, 32, 1, () => PauseClicked?.Invoke());
        _pause.MouseFilter = MouseFilterEnum.Stop;
        _pause.Center();
        _month = Ui.Text("", "SmallMu");
        _year = Ui.Text("", "Date");
        _dateSlot = Ui.VBox(0, _month, _year).Center();
        _pips.SpeedPicked += s => Game.I.SetSpeed(s);
        _pips.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        var pipsWrap = Ui.Margin(_pips, 0, 0, 0, 12);
        _clockBox = new Box().Fill(Pal.Well).Border(Pal.Ln2, 2, 0, 2, 0).Pad(12, 0, 14, 0);
        _clock = Ui.Panel(_clockBox, Ui.HBox(12, _pause, _dateSlot, pipsWrap), MouseFilterEnum.Stop);
        SizeDateSlot();
        return _clock;
    }

    Control BuildSession()
    {
        _session = Ui.Button("0:00", "hourglass", "Session", OnSessionClick, 1, 32);
        _session.MouseFilter = MouseFilterEnum.Stop;
        _session.Center();
        return _session;
    }

    void WireTips()
    {
        _nation.Tip(t =>
        {
            var n = Game.I.Nations[GameState.LocalPlayer];
            t.Title(n.Name).Line($"{n.Gov} · {Game.I.EraName}");
            if (Game.I.IsReady) CharacterTip.Fill(t, Game.I.State.Nat[GameState.LocalPlayer]);
            t.Mu("Нажмите, чтобы показать столицу");
        });
        _res[0].Root.Tip(t =>
        {
            t.Title("Казна");
            if (!Game.I.IsReady || !_incomeKnown) { t.Mu("Доход появится после первого цикла"); return; }
            var s = Game.I.State;
            var b = Game.I.BudgetLines;
            t.Line($"Налоги {Fmt.Signed(b.Taxes / 100.0, 1)} · Постройки {Fmt.Signed(-b.Buildings / 100.0, 1)}")
             .Line($"Управление {Fmt.Signed(-b.Admin / 100.0, 1)} · Указы {Fmt.Signed(-b.Edicts / 100.0, 1)}")
             .Kv("Итого за цикл", Fmt.Signed(s.LastIncome, 1), s.LastIncome >= 0 ? Pal.Ok : Pal.Bad)
             .Kv("В минуту", Fmt.Signed(s.LastIncome * Game.CyclesPerMinute(s.Speed)), s.LastIncome >= 0 ? Pal.Ok : Pal.Bad)
             .Mu("Цикл — полсекунды при скорости 3. Золото тратится на земли, города, постройки, геологов и указы. Подробно — в «Политике»");
        });
        _res[1].Root.Tip(t =>
        {
            var g = Game.I;
            var sp = g.ScienceParts;
            t.Title("Наука").Line($"Мудрецы +{sp.Sages} · Земли +{sp.Lands} · Святилища +{sp.Shrines}" + (sp.Knowledge > 0 ? $" · Знания +{sp.Knowledge} к исследованиям" : "") + (sp.CatchUp > 0 ? $" · Догоняем +{sp.CatchUp}" : ""))
             .Kv("За цикл", "+" + g.ScienceRate, Pal.Ok)
             .Kv("Исследования", $"+{g.ResearchRate} за цикл", Pal.Ok);
            if (g.NextEraName != "") t.Kv($"До эпохи «{g.NextEraName}»", $"{g.EraProgressPermille / 10}%", Pal.Hi);
            int r = g.Researching;
            t.Kv("Изучается", r >= 0 ? Techs.All[r].Name : "ничего", r >= 0 ? Pal.Ok : Pal.Bad);
            t.Mu("Эпоха наступает, когда наука наберёт свою цену");
        });
        _res[2].Root.Tip("Население", "Во всех провинциях державы");
        _res[3].Root.Tip(t =>
        {
            t.Title("Стабильность").Line("Среднее довольство провинций державы");
            var g = Game.I;
            if (!g.IsReady) return;
            var (gr, un, rv, sick) = g.UnrestCounts();
            if (gr + un + rv == 0) t.Kv("Беспорядки", "нет", Pal.Ok);
            else
            {
                if (gr > 0) t.Kv("Ворчание", $"{gr} · налоги −{100 - Unrest.GrumbleTaxPct}%", Pal.Warn);
                if (un > 0) t.Kv("Волнения", $"{un} · ни налогов, ни материалов", Pal.Bad);
                if (rv > 0) t.Kv("Мятеж", $"{rv} · вот-вот отложатся", Pal.Bad);
            }
            if (sick > 0) t.Kv("Мор", $"{sick} {Fmt.Plural(sick, "провинция", "провинции", "провинций")}", Pal.Bad);
            if (g.InDebt) t.Kv("Казна", "пуста: довольство −10", Pal.Bad);
            t.Mu($"Ниже {Unrest.GrumbleBelow} ворчат, ниже {Unrest.UnrestBelow} бастуют, ниже {Unrest.RevoltBelow} — мятеж и отделение через минуту");
        });
        _res[4].Root.Tip(t =>
        {
            t.Title("Материалы");
            if (!Game.I.IsReady) return;
            var (l, q, m, c) = Game.I.MaterialSources();
            var parts = new List<string>();
            if (l > 0) parts.Add($"Лесопилки +{l * Rules.LumberMaterials}");
            if (q > 0) parts.Add($"Каменоломни +{q * Rules.QuarryMaterials}");
            if (m > 0) parts.Add($"Рудники +{m * Rules.MineMaterials}");
            if (c > 0) parts.Add($"Столица +{c * Rules.CapitalMaterials}");
            t.Line(string.Join(" · ", parts)).Kv("За цикл", "+" + Game.I.State.LastMaterials, Pal.Ok)
             .Mu("Дерево и камень: нужны для построек и новых городов. Лесопилки и каменоломни строятся без них");
        });
        _clock.Tip(t =>
        {
            t.Title(Game.I.DateText).Line($"Эпоха: {Game.I.EraName}");
            if (!_session.Visible) t.Line($"Вы играете {Fmt.Duration(SessionMinutes)}");
            t.Mu("Пробел — пауза, 1–5 — скорость");
        });
        _pause.Tip(t => t.Title(Game.I.IsReady && Game.I.State.Paused ? "Продолжить" : "Пауза").Mu("Пробел"));
        _pips.Tip(t =>
        {
            t.Title("Скорость " + (Game.I.IsReady ? Game.I.State.Speed : 2) + " из 5");
            if (Game.I.Hurrying) t.Line("Умное время: в мире тихо, время бежит на скорости 5");
            t.Mu("Клавиши 1–5");
        });
        _session.Tip(t =>
        {
            if (Game.I.IsBlitz)
            {
                t.Title("Блиц недели").Line(Game.I.BlitzOver ? "Время вышло: партия подсчитана" : "Сколько игрового времени осталось на скорости 3");
                t.Mu("На скорости 5 время идёт впятеро быстрее");
                return;
            }
            t.Title("Сколько ты уже играешь").Mu("Нажми — совет придворных");
        });
    }

    // ---------------- refresh ----------------
    public void OnWorldReady()
    {
        var n = Game.I.Nations[GameState.LocalPlayer];
        _flag.SetFlag(n.Flag, (n.R, n.G, n.B));
        _name.Text = n.Name;
        _era.Text = Game.I.EraName;
        _incomeKnown = false;
        RefreshResources();
        RefreshClock();
    }

    /// <summary>A rules cycle ran: the budget is known (the HUD refreshes the readouts, at most 5× a second).</summary>
    public void OnCycleTick() => _incomeKnown = true;

    public void OnDateChanged()
    {
        RefreshDate();
        if (_era.Text != Game.I.EraName) _era.Text = Game.I.EraName;
    }

    public void RefreshResources()
    {
        if (!Game.I.IsReady) return;
        var s = Game.I.State;
        double pop = 0, mood = 0;
        for (int p = 0; p < s.Owner.Length; p++)
            if (s.Owner[p] == GameState.LocalPlayer) { pop += s.Pop[p]; mood += s.Mood[p] * s.Pop[p]; }
        if (s.Nat[GameState.LocalPlayer].Camp >= 0) pop += s.Nat[GameState.LocalPlayer].TribePop;   // the tribe on the move
        double income = s.LastIncome;
        _res[0].Set(Fmt.Int(s.Gold), _incomeKnown ? Fmt.Signed(income, Math.Abs(income) < 10 ? 1 : 0) : null, income >= 0 ? Pal.Ok : Pal.Bad);
        _res[1].Set($"{Game.I.EraProgressPermille / 10}%", "+" + Game.I.ScienceRate, Pal.Ok);
        _res[2].Set(Fmt.Pop(pop), null, default);
        _res[3].Set(pop > 0 ? $"{Math.Round(mood / pop)}%" : "—", null, default);
        _res[4].Set(Fmt.Int(s.Materials), _incomeKnown ? "+" + s.LastMaterials : null, Pal.Ok);
    }

    public void RefreshClock()
    {
        if (!Game.I.IsReady) return;
        var s = Game.I.State;
        _pause.ThemeTypeVariation = s.Paused ? "PauseRed" : "Pause";
        _pause.Icon = Icons.Get(s.Paused ? "player-play-filled" : "player-pause-filled");
        _pips.SetSpeed(s.Speed);
        RefreshDate();
    }

    string _shownDate;
    bool _shownPaused;

    void RefreshDate()
    {
        if (!Game.I.IsReady) return;
        string date = Game.I.DateText;
        if (date == _shownDate && Game.I.State.Paused == _shownPaused) return;
        _shownDate = date;
        _shownPaused = Game.I.State.Paused;
        var (month, year) = SplitDate(date);
        _month.Text = month;
        _month.Visible = month.Length > 0;
        _year.Text = year;
        if (Game.I.State.Paused) _year.Colored(Pal.Bad); else _year.RemoveThemeColorOverride("font_color");
        float need = Mathf.Max(UiFonts.Width(UiFonts.Regular, month, UiFonts.Small), UiFonts.Width(UiFonts.Semi, year, UiFonts.Value));
        if (need > _dateSlot.CustomMinimumSize.X) _dateSlot.CustomMinimumSize = new Vector2(Mathf.Ceil(need), 0);   // grows only
    }

    /// <summary>«март 3200 до н. э.» → («март», «3200 до н. э.»); «12 марта 1893» → («12 марта», «1893»). The year is the
    /// last run of digits with whatever follows it (era suffix); a text without digits stays on the second line.</summary>
    public static (string month, string year) SplitDate(string text)
    {
        if (string.IsNullOrEmpty(text)) return ("", "");
        int end = text.Length - 1;
        while (end >= 0 && !char.IsDigit(text[end])) end--;
        if (end < 0) return ("", text);
        int start = end;
        while (start > 0 && char.IsDigit(text[start - 1])) start--;
        return (text[..start].TrimEnd(' ', ','), text[start..]);
    }

    /// <summary>Wide enough for the widest month line and year line the calendar can produce, so the bar never jumps.</summary>
    void SizeDateSlot()
    {
        float w = 0;
        foreach (var m in new[] { "сентябрь", "28 сентября", "28 февраля" }) w = Mathf.Max(w, UiFonts.Width(UiFonts.Regular, m, UiFonts.Small));
        foreach (var y in new[] { "8888 до н. э.", "2888 н. э." }) w = Mathf.Max(w, UiFonts.Width(UiFonts.Semi, y, UiFonts.Value));
        _dateSlot.CustomMinimumSize = new Vector2(Mathf.Ceil(w), 0);
    }

    internal Control DebugTarget(string name) => name switch
    {
        "gold" => _res[0].Root, "sci" => _res[1].Root, "pop" => _res[2].Root, "stab" => _res[3].Root, "infl" or "mat" => _res[4].Root,
        "clock" => _clock, "pause" => _pause, "pips" => _pips, "session" => _session, "nation" => _nation, "screen" => _screenBtns[0], "trophy" => Trophy,
        _ => null,
    };

    public void SetTechOpen(bool open)
    {
        TechButton.ThemeTypeVariation = open ? "IbOn" : "Ib";
        TechButton.Icon = Icons.Get("atom", 2, !open);
    }

    /// <summary>Nothing studied while something could be: the atom glows until the player chooses.</summary>
    public void SetResearchIdle(bool idle)
    {
        if (TechButton.ThemeTypeVariation == "IbOn") return;
        TechButton.SelfModulate = idle ? new Color(1.6f, 1.35f, .7f) : Colors.White;
    }

    public Button PolicyButton => _screenBtns[1];
    public Button WondersButton { get; private set; }

    public void SetWondersOpen(bool open)
    {
        WondersButton.ThemeTypeVariation = open ? "IbOn" : "Ib";
        WondersButton.Icon = Icons.Get("diamond", 2, !open);
    }

    public void SetPolicyOpen(bool open)
    {
        PolicyButton.ThemeTypeVariation = open ? "IbOn" : "Ib";
        PolicyButton.Icon = Icons.Get("building-bank", 2, !open);
    }

    public void SetLeaderboardOpen(bool open)
    {
        Trophy.ThemeTypeVariation = open ? "IbOn" : "Ib";
        Trophy.Icon = Icons.Get("trophy", 2, !open);
    }

    /// <summary>
    /// Fits the bar to the window width: ≤1440 tighter paddings; ≤1366 the session counter moves into the clock's
    /// tooltip; &lt;1280 captions, deltas and the era line fold away and the resource icons drop to 1×.
    /// </summary>
    public void SetWidth(float width)
    {
        bool compact = width <= 1440, narrow = width <= 1366, tiny = width < 1280;
        foreach (var r in _res) r.SetDensity(compact, tiny);
        foreach (var b in _screenBtns) b.CustomMinimumSize = new Vector2(tiny ? 30 : narrow ? 32 : compact ? 34 : 36, 34);
        _clockBox.Pad(compact ? 10 : 12, 0, compact ? 10 : 14, 0);
        _nationBox.Pad(compact ? 10 : 12, 0, tiny ? 10 : compact ? 14 : 20, 0);
        _nationHover.Pad(compact ? 10 : 12, 0, tiny ? 10 : compact ? 14 : 20, 0);
        _name.Sized(tiny ? UiFonts.Value : UiFonts.Title);
        _era.Visible = !tiny;
        _session.Visible = _sessionGap.Visible = !narrow;
        _clock.UpdateMinimumSize();
        _nation.UpdateMinimumSize();
    }

    // ---------------- session timer ----------------
    public int SessionMinuteOffset { get; set; }
    public int SessionMinutes => (int)(Time.GetTicksMsec() / 60000) + SessionMinuteOffset;

    long _blitzShown = -1;

    public override void _Process(double delta)
    {
        _pips.Hurry = Game.I.IsReady && Game.I.Hurrying;
        if (Game.I.IsBlitz)
        {
            // the blitz counts down its game time (at speed 3) instead of the session's real time
            long left = (Game.I.BlitzTicksLeft + 7) / Clock.TicksPerSecond[Clock.ReferenceSpeed];
            if (left != _blitzShown) { _blitzShown = left; ((TextButton)_session).Caption = $"{left / 60}:{left % 60:00}"; }
            return;
        }
        int m = SessionMinutes;
        if (m == _lastMinute) return;
        bool first = _lastMinute < 0;
        _lastMinute = m;
        ((TextButton)_session).Caption = $"{m / 60}:{m % 60:00}";
        if (!first && m > 0 && m % 60 == 0) Game.I.ShowToast(SessionJokes.For(m), 6);
    }

    void OnSessionClick()
    {
        if (Game.I.IsBlitz) Game.I.ShowToast(Game.I.BlitzOver ? "Блиц окончен" : $"Блиц: осталось {Fmt.Duration((int)(Game.I.BlitzTicksLeft / 480))} игрового времени", 4);
        else Game.I.ShowToast(SessionJokes.For(SessionMinutes), 5);
    }

    /// <summary>One resource cell: 26px pixel icon · spaced CAPTION over value + delta, 2px divider on the right.</summary>
    sealed class Res
    {
        public readonly PanelContainer Root;
        readonly Box _normal, _hover;
        readonly TextureRect _icon;
        readonly Label _cap, _val, _delta;
        readonly HBoxContainer _row;
        readonly string _iconName;
        bool _tiny, _hasDelta;

        public Res(string icon, string caption)
        {
            _iconName = icon;
            _normal = Cell(false);
            _hover = Cell(true);
            _icon = Ui.Icon(icon, 2, Pal.Ac);
            _cap = Ui.Cap(caption);
            _val = Ui.Text("0", "Val");
            _delta = Ui.Text("", "Delta");
            var values = Ui.HBox(6, _val, _delta);
            var col = Ui.VBox(1, _cap, values).Center();
            _row = Ui.HBox(9, _icon, col);
            Root = Ui.Panel(_normal, _row, MouseFilterEnum.Stop);
            Root.MouseEntered += () => { Root.AddThemeStyleboxOverride("panel", _hover); _icon.SelfModulate = Pal.Hi; };
            Root.MouseExited += () => { Root.AddThemeStyleboxOverride("panel", _normal); _icon.SelfModulate = Pal.Ac; };
        }

        static Box Cell(bool hover)
        {
            var b = new Box().Border(Pal.Ln, 0, 0, 2, 0).Pad(14, 0, 16, 0);
            if (hover) b.Fill(Pal.A(Pal.SurfaceHover, .7f));
            return b;
        }

        public void Set(string value, string delta, Color deltaColor)
        {
            _val.Text = value;
            _hasDelta = delta != null;
            _delta.Visible = _hasDelta && !_tiny;
            if (delta != null) { _delta.Text = delta; _delta.Colored(deltaColor); }
        }

        public void SetDensity(bool compact, bool tiny)
        {
            _tiny = tiny;
            _cap.Visible = !tiny;
            _delta.Visible = _hasDelta && !tiny;
            int l = tiny ? 8 : compact ? 10 : 14, r = tiny ? 10 : compact ? 12 : 16;
            _normal.Pad(l, 0, r, 0); _hover.Pad(l, 0, r, 0);
            _row.AddThemeConstantOverride("separation", tiny ? 5 : compact ? 7 : 9);
            int scale = tiny ? 1 : 2;
            _icon.Texture = Icons.Get(_iconName, scale);
            _icon.CustomMinimumSize = new Vector2(Icons.Size(scale), Icons.Size(scale));
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
