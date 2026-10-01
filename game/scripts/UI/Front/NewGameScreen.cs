using System;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Core.Flags;
using PaxPixelia.Core.Nations;

namespace PaxPixelia.UI.Front;

/// <summary>
/// «Новая игра» (MAIN_MENU.md §3.2): seed (words or a number, dice, copy), number of nations, fog, game length, the
/// preview of the very world the title screen generated (NextWorld) and the player's nation card. «НАЧАТЬ» hands the
/// setup and the already generated world to Main (FrontShell.StartGame → Session.Pending) — no second generation.
/// </summary>
public partial class NewGameScreen : FrontScreen
{
    public override string Title => "Новый мир";
    public override string Crumb => "Новая игра";
    public override float PanelWidth => 1240;
    public override Control DefaultFocus => _start;
    public override (string key, string text)[] Hints => new[]
        { ("Up Down", "выбор"), ("Left Right", "значение"), ("R", "кости"), ("Tab", "народ"), ("Ctrl+Enter", "начать"), ("Esc", "назад") };

    static readonly string[] PaceNames = { "Быстрая · ≈12 ч", "Обычная · ≈25 ч", "Эпическая · ≈40 ч" };
    static readonly int[] PaceValues = { GameSetup.PaceQuick, GameSetup.PaceNormal, GameSetup.PaceEpic };
    const int MinNations = 2, MaxNations = 16;
    const string Generating = "Мир складывается…";

    LineEdit _seed;
    Label _seedNumber, _hint, _stats, _bots;
    PxOptionRow _nations, _fog, _pace;
    NationDots _dots;
    WorldPreview _preview;
    NationCard _card;
    Button _start;
    Timer _debounce;
    bool _startWhenReady, _starting;
    readonly Random _rng = new();

    int NationCount => _nations.Index + MinNations;

    public override void Build()
    {
        var last = LastSetup.Load();
        bool compact = GetViewportRect().Size.Y < 900;
        AddThemeConstantOverride("separation", 16);

        // ---- left: options ----
        _seed = new LineEdit { CustomMinimumSize = new Vector2(236, 46), MaxLength = 40, PlaceholderText = "слова или число", SelectAllOnFocus = true };
        _seed.AddThemeFontSizeOverride("font_size", 18);
        _seed.TextChanged += _ => OnSeedEdited();
        _seed.TextSubmitted += _ => _nations.GrabFocus();
        _seed.FocusEntered += () => ShowHint("Одно зерно — один мир: у друзей с тем же зерном будет та же карта. Можно вписать слова: «лиса у реки».");
        var dice = SetupUi.IconButton(PxIcon.Dice, "Случайное зерно (R)");
        dice.Pressed += RollSeed;
        var copy = SetupUi.IconButton(PxIcon.Copy, "Скопировать зерно");
        copy.Pressed += CopySeed;
        var seedCaption = PixelKit.Label("Зерно", 18);
        seedCaption.CustomMinimumSize = new Vector2(96, 0);
        _seedNumber = PixelKit.Label("", 13, PixelKit.TextDim);
        _seedNumber.CustomMinimumSize = new Vector2(0, 16);

        _nations = new PxOptionRow("Всего держав", Numbers(MinNations, MaxNations), Math.Clamp(last.Nations, MinNations, MaxNations) - MinNations)
            { Hint = "Сколько держав в мире, считая вашу. Остальными правят боты." };
        _nations.Changed += _ => OnNationsChanged();
        _dots = new NationDots();
        _bots = PixelKit.Label("", 13, PixelKit.TextDim);
        _fog = new PxOptionRow("Туман войны", new[] { "Включён", "Выключен" }, last.Fog ? 0 : 1)
            { Hint = "Неизведанное скрыто облаками, разведчики открывают карту. Выключен — весь мир виден с первой минуты." };
        _pace = new PxOptionRow("Длина партии", PaceNames, Math.Max(0, Array.IndexOf(PaceValues, last.Pace)))
            { Hint = "Сколько часов займёт путь от кочевий до Будущего на скорости 3. Дни идут так же, дольше длятся эпохи." };
        foreach (var r in new[] { _nations, _fog, _pace }) { var row = r; row.FocusEntered += () => ShowHint(row.Hint); }

        _hint = SetupUi.HintBox(470);
        var left = SetupUi.Column(8,
            SetupUi.Section("Мир"),
            SetupUi.Row(10, seedCaption, _seed, dice, copy),
            SetupUi.Indent(_seedNumber, 106),
            SetupUi.Gap(14),
            SetupUi.Section("Державы"),
            _nations,
            SetupUi.Indent(SetupUi.Row(12, _dots, _bots), 12),
            SetupUi.Gap(14),
            SetupUi.Section("Партия"),
            _fog, _pace,
            SetupUi.Spacer(false),
            SetupUi.Rule(),
            _hint);
        left.CustomMinimumSize = new Vector2(480, 0);

        // ---- right: preview + nation ----
        _preview = new WorldPreview(compact);
        _preview.Analysed += OnAnalysed;
        _stats = PixelKit.Label(" ", 16, PixelKit.Text);
        var sub = PixelKit.Label("Мир 2560 × 1440 · старт на паузе · потяните карту мышью", 13, PixelKit.TextDim);
        _card = new NationCard(_preview.MapW + 4, compact);
        _card.EditPressed += OpenNation;
        var right = SetupUi.Column(8, _preview, SetupUi.Column(2, _stats, sub), SetupUi.Gap(2), _card);
        AddChild(SetupUi.Row(0, left, SetupUi.Spacer(), right));

        // ---- footer ----
        var back = SetupUi.WithKey(PixelKit.Button("Назад", "GhostButton"), "Esc");
        back.CustomMinimumSize = new Vector2(180, 46);
        back.Pressed += () => GoBack();
        _start = SetupUi.WithKey(PixelKit.Button("НАЧАТЬ", "PrimaryButton"), "Ctrl+Enter");
        _start.CustomMinimumSize = new Vector2(320, 50);
        _start.AddThemeFontOverride("font", PixelKit.Spaced(2));
        _start.AddThemeFontSizeOverride("font_size", 22);
        _start.Pressed += Start;
        _start.FocusEntered += () => ShowHint("Два нажатия от титула до карты: мир уже готов, народ — ваш последний.");
        var blitz = PixelKit.Button("Блиц недели", "GhostButton");
        blitz.CustomMinimumSize = new Vector2(200, 46);
        blitz.Pressed += () => Shell.OpenScreen("blitz");
        blitz.FocusEntered += () => ShowHint("Один мир на всю неделю для всех: 40 минут игрового времени, очки и таблица с друзьями.");
        AddChild(SetupUi.Row(12, back, blitz, SetupUi.Spacer(), _start));

        _debounce = new Timer { OneShot = true, WaitTime = .4 };
        _debounce.Timeout += RegenerateFromField;
        AddChild(_debounce);

        _seed.Text = last.SeedText != "" && SeedText.Parse(last.SeedText) == NextWorld.Seed ? last.SeedText : NextWorld.Seed.ToString();
        UpdateSeedNumber();
        OnNationsChanged();
        RefreshNation();
        // opened before the title asked for its world (e.g. right after returning from a game): ask for ours
        if (!NextWorld.IsReady && !NextWorld.IsGenerating) NextWorld.Regenerate(Seed);
        if (NextWorld.IsReady && NextWorld.Seed == Seed) _preview.ShowWorld(NextWorld.World, NationCount);
        else _preview.ShowGenerating(NextWorld.Status is { Length: > 0 } s ? s : Generating);
        Nav.Unify(this);
    }

    // The shell takes this screen out of the tree while «Народ» is on top: the world may get ready meanwhile,
    // and a start pressed in «Народ» waits for it, so the subscription follows the tree, not Build.
    public override void _EnterTree()
    {
        NextWorld.Ready += OnWorldReady;
        NextWorld.Progress += OnWorldProgress;
        if (_preview != null && NextWorld.IsReady) Callable.From(OnWorldReady).CallDeferred();
    }

    public override void _ExitTree()
    {
        NextWorld.Ready -= OnWorldReady;
        NextWorld.Progress -= OnWorldProgress;
    }

    public override void OnShown()
    {
        RefreshNation();   // back from «Народ»
        base.OnShown();
    }

    static string[] Numbers(int from, int to)
    {
        var a = new string[to - from + 1];
        for (int i = 0; i < a.Length; i++) a[i] = (from + i).ToString();
        return a;
    }

    void ShowHint(string text) => _hint.Text = text ?? "";

    // ---- seed ----
    int Seed => SeedText.Normalize(_seed.Text).Length == 0 ? NextWorld.Seed : SeedText.Parse(_seed.Text);

    void OnSeedEdited()
    {
        UpdateSeedNumber();
        _debounce.Start();
    }

    void UpdateSeedNumber() =>
        _seedNumber.Text = SeedText.IsWords(_seed.Text) ? "= " + Fmt.Int(SeedText.Parse(_seed.Text)) : "";

    void RollSeed()
    {
        _seed.Text = SeedText.Random5(_rng).ToString();
        UpdateSeedNumber();
        PixelKit.Bump(_seed, 1.04f);
        RegenerateFromField();
    }

    void CopySeed()
    {
        DisplayServer.ClipboardSet(_seed.Text.Trim().Length > 0 ? _seed.Text.Trim() : NextWorld.Seed.ToString());
        _seedNumber.Text = "Зерно скопировано";
        PixelKit.PopIn(_seedNumber, 0, .8f, .3f);
    }

    void RegenerateFromField()
    {
        _debounce.Stop();
        int seed = Seed;
        if (seed == NextWorld.Seed && (NextWorld.IsReady || NextWorld.IsGenerating)) return;   // that world is shown or on its way
        NextWorld.Regenerate(seed);
        _preview.ShowGenerating(Generating);
    }

    void OnWorldProgress(string status) { if (!NextWorld.IsReady) _preview?.ShowGenerating(status); }

    void OnWorldReady()
    {
        if (_preview == null || !IsInsideTree() || !NextWorld.IsReady || NextWorld.Seed != Seed) return;   // stale: the field asks for another
        _preview.ShowWorld(NextWorld.World, NationCount);
    }

    void OnAnalysed(WorldStats s) =>
        _stats.Text = s.Provinces == 0 ? " " : $"Суша {s.LandPercent}% · провинций {Fmt.Int(s.Provinces)} · держав {s.Nations}";

    // ---- nations ----
    void OnNationsChanged()
    {
        int n = NationCount;
        _dots.Set(n);
        _bots.Text = $"вы и {n - 1} {Fmt.Plural(n - 1, "бот", "бота", "ботов")}";
        _preview?.SetNationCount(n);
    }

    void RefreshNation()
    {
        var d = NationStore.Current;
        _card.Show(d);
        _preview.SetPlayerColour(d.Rgb);
    }

    void OpenNation()
    {
        if (!_starting) Shell.Push(new NationScreen { StartGame = Start });
    }

    // ---- start ----
    void Start()
    {
        if (_starting) return;
        if (!NextWorld.IsReady || NextWorld.Seed != Seed)
        {
            RegenerateFromField();
            if (!_startWhenReady) NextWorld.Ready += StartWhenReady;   // also while «Народ» covers this screen
            _startWhenReady = true;
            _start.Text = "Мир почти готов…";
            return;
        }
        _starting = true;
        var text = _seed.Text.Trim();
        var setup = new GameSetup(Seed, text.Length > 0 ? text : Seed.ToString(), NationCount,
            _fog.Index == 0, PaceValues[_pace.Index], true, NationStore.TouchCurrent());
        LastSetup.Save(setup);
        Shell.StartGame(setup, NextWorld.World);
    }

    void StartWhenReady()
    {
        if (NextWorld.Seed != Seed) return;   // an older world finished first
        NextWorld.Ready -= StartWhenReady;
        Callable.From(Start).CallDeferred();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete && _startWhenReady) NextWorld.Ready -= StartWhenReady;   // closed while waiting
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k || !IsVisibleInTree() || PxConfirm.IsOpen) return;
        if (k.CtrlPressed && k.Keycode is Key.Enter or Key.KpEnter) { Start(); GetViewport().SetInputAsHandled(); }
        else if (k.PhysicalKeycode == Key.R && !k.CtrlPressed && GetViewport().GuiGetFocusOwner() is not LineEdit)
        {
            RollSeed();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Input(InputEvent e)
    {
        // Tab opens «Народ» (MAIN_MENU.md §3.2) instead of moving focus — but not while the seed is being typed
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Tab, ShiftPressed: false } && IsVisibleInTree()
            && !PxConfirm.IsOpen && GetViewport().GuiGetFocusOwner() is not LineEdit)
        {
            GetViewport().SetInputAsHandled();
            OpenNation();
        }
    }
}

/// <summary>Row of 16 small squares: taken slots, the player's one (framed) and free ones.</summary>
public partial class NationDots : Control
{
    int _n = 16;
    const int Cell = 10, Step = 12;

    public NationDots() { CustomMinimumSize = new Vector2(16 * Step, Cell + 4); MouseFilter = MouseFilterEnum.Ignore; }

    public void Set(int n) { _n = n; QueueRedraw(); }

    public override void _Draw()
    {
        for (int i = 0; i < 16; i++)
        {
            var r = new Rect2(i * Step, 2, Cell, Cell);
            if (i == 0) { DrawRect(r.Grow(1), PixelKit.AccentLight); DrawRect(r.Grow(-1), PixelKit.Ink); DrawRect(r.Grow(-2), PixelKit.AccentLight); }
            else DrawRect(r, i < _n ? PixelKit.Accent : PixelKit.Surface);
        }
    }
}

/// <summary>«ВАШ НАРОД» card: flag ×3, name, government and culture, «Изменить народ».</summary>
public partial class NationCard : PanelContainer
{
    readonly TextureRect _flag;
    readonly Label _name, _line;
    public event Action EditPressed;

    public NationCard(float width, bool compact)
    {
        CustomMinimumSize = new Vector2(width, 0);
        AddThemeStyleboxOverride("panel", PixelKit.Padded(PixelKit.Framed(PixelKit.Surface, PixelKit.PanelBorder), 16, 14));
        _flag = SetupUi.Pixel(new Vector2(60, 42));
        _flag.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _name = PixelKit.Label("", 22, PixelKit.AccentLight);
        _name.AddThemeFontOverride("font", PixelKit.Spaced(2));
        _line = PixelKit.Label("", compact ? 13 : 16, PixelKit.TextDim);
        var text = SetupUi.Column(2, PixelKit.Kicker("ВАШ НАРОД", PixelKit.Secondary, 11), _name, _line);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var edit = SetupUi.WithKey(PixelKit.Button(compact ? "Изменить" : "Изменить народ", ""), "Tab");
        edit.CustomMinimumSize = new Vector2(compact ? 170 : 236, 46);
        edit.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        edit.Pressed += () => EditPressed?.Invoke();
        AddChild(SetupUi.Row(16, _flag, text, edit));
    }

    public void Show(NationDesign d)
    {
        _flag.Texture = FlagTextures.Get(d.Flag, d.Rgb);
        _name.Text = d.Name.ToUpper();
        _line.Text = $"Вождество · {NationNames.Cultures[d.Culture % NationNames.Cultures.Length].Adjective} культура";
    }
}

/// <summary>user://last_setup.cfg: the options of the last started game come back next time.</summary>
public readonly record struct LastSetup(string SeedText, int Nations, bool Fog, int Pace)
{
    const string FilePath = "user://last_setup.cfg", Section = "setup";

    public static LastSetup Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(FilePath) != Error.Ok) return new LastSetup("", 16, true, GameSetup.PaceNormal);
        return new LastSetup(cfg.GetValue(Section, "seed_text", "").AsString(), cfg.GetValue(Section, "nations", 16).AsInt32(),
            cfg.GetValue(Section, "fog", true).AsBool(), cfg.GetValue(Section, "pace", GameSetup.PaceNormal).AsInt32());
    }

    public static void Save(GameSetup s)
    {
        var cfg = new ConfigFile();
        cfg.SetValue(Section, "seed_text", s.SeedText);
        cfg.SetValue(Section, "nations", s.NationCount);
        cfg.SetValue(Section, "fog", s.Fog);
        cfg.SetValue(Section, "pace", s.PacePermille);
        cfg.Save(FilePath);
    }
}

/// <summary>Layout helpers shared by the setup screens («Новая игра», «Народ», «Настройки», пауза, карточка главы).</summary>
public static class SetupUi
{
    /// <summary>Section caption: 11 px spaced capitals.</summary>
    public static Label Section(string text) => PixelKit.Kicker(text.ToUpper(), PixelKit.Secondary, 11);

    /// <summary>Two-line hint under a column (16 px TextDim), fixed height so the layout never jumps.</summary>
    public static Label HintBox(float width)
    {
        var l = PixelKit.Paragraph("", 16, PixelKit.TextDim);
        l.CustomMinimumSize = new Vector2(width, 44);
        l.VerticalAlignment = VerticalAlignment.Top;
        return l;
    }

    /// <summary>Square button with a 16 px pixel icon at ×2.</summary>
    public static Button IconButton(PxIcon icon, string tip, int size = 46)
    {
        var b = PixelKit.Button("", "");
        b.CustomMinimumSize = new Vector2(size, size);
        b.TooltipText = tip;
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        center.AddChild(PxIcons.Make(icon, size >= 40 ? 2 : 1, PixelKit.AccentLight));
        b.AddChild(center);
        return b;
    }

    /// <summary>A keycap at the right edge inside a button: «НАЧАТЬ  [CTRL+ENTER]» (text moves left).</summary>
    public static Button WithKey(Button b, string key)
    {
        var box = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = Control.MouseFilterEnum.Ignore };
        box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        box.OffsetRight = -12;
        box.AddChild(new KeyHint(key));
        b.AddChild(box);
        b.Alignment = HorizontalAlignment.Left;
        return b;
    }

    public static TextureRect Pixel(Vector2 size) => new()
    {
        CustomMinimumSize = size, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest, MouseFilter = Control.MouseFilterEnum.Ignore,
    };

    public static PanelContainer Card(Control content, float padX = 16, float padY = 12, Color? fill = null)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", PixelKit.Padded(PixelKit.Framed(fill ?? PixelKit.Surface, PixelKit.PanelBorder), padX, padY));
        p.AddChild(content);
        return p;
    }

    public static HBoxContainer Row(int gap, params Control[] items)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", gap);
        foreach (var c in items) h.AddChild(c);
        return h;
    }

    public static VBoxContainer Column(int gap, params Control[] items)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", gap);
        foreach (var c in items) v.AddChild(c);
        return v;
    }

    public static Control Indent(Control c, int px)
    {
        var m = new MarginContainer();
        m.AddThemeConstantOverride("margin_left", px);
        m.AddChild(c);
        return m;
    }

    public static Control Spacer(bool horizontal = true)
    {
        var c = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        if (horizontal) c.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; else c.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        return c;
    }

    public static Control Gap(float h) => new() { CustomMinimumSize = new Vector2(0, h), MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>2 px PanelBorder line across the container.</summary>
    public static ColorRect Rule() => new() { Color = PixelKit.PanelBorder, CustomMinimumSize = new Vector2(0, 2), MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>Staggered appearance of a container's children (as the panel items in Mr. President).</summary>
    public static void Stagger(Control container, float delay = .08f, float step = .04f)
    {
        if (FrontClock.Reduced) return;
        foreach (var n in container.GetChildren())
            if (n is Control c && c.Visible) { PixelKit.PopIn(c, delay, .94f, .32f); delay += step; }
    }
}
