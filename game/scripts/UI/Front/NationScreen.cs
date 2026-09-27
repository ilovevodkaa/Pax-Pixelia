using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Core.Audio;
using PaxPixelia.Core.Nations;

namespace PaxPixelia.UI.Front;

/// <summary>
/// «Народ» (MAIN_MENU.md §3.3): one screen instead of a 5-step wizard — name (dice, lock), 24 colours with the
/// ramp, tabs ФЛАГ · КУЛЬТУРА, the live «как вас увидят» column and «Мои народы» (user://nations). Every change is
/// the new draft at once (NationStore.Current), Ctrl+Z / Ctrl+Y undo up to 32 steps; «НАЧАТЬ» starts the game.
/// </summary>
public partial class NationScreen : FrontScreen
{
    public override string Title => "Ваш народ";
    public override string Crumb => "Новая игра  ›  Народ";
    public override float PanelWidth => 1240;
    public override Control DefaultFocus => _start;
    public override (string key, string text)[] Hints => new[]
        { ("R", "случайный народ"), ("Q E", "вкладки"), ("Ctrl+Z", "отменить"), ("Ctrl+Enter", "начать"), ("Esc", "назад") };

    /// <summary>Starts the game with the options of «Новая игра» (set by it); null → the last setup and the next world.</summary>
    public Action StartGame;

    const int MaxUndo = 32;
    NationDesign _d;
    readonly List<NationDesign> _undo = new(), _redo = new();
    readonly Random _rng = new();
    bool _nameLocked, _nameValid = true, _syncing;

    LineEdit _name;
    Button _lock, _start;
    Label _forms, _status;
    readonly Swatch[] _colours = new Swatch[24];
    readonly ColorRect[] _ramp = new ColorRect[3];
    PxTabs _tabs;
    FlagEditor _flag;
    CultureGrid _culture;
    NationPreview _preview;
    OptionButton _mine;
    List<NationDesign> _saved = new();

    public override void Build()
    {
        _d = NationStore.Current;
        bool compact = GetViewportRect().Size.Y < 900;
        AddThemeConstantOverride("separation", compact ? 10 : 14);
        float rightW = compact ? 360 : 420;

        // ---- name ----
        _name = new LineEdit { MaxLength = NationNames.MaxLength, CustomMinimumSize = new Vector2(compact ? 300 : 340, 46), PlaceholderText = "Имя народа" };
        _name.AddThemeFontSizeOverride("font_size", 22);
        _name.AddThemeFontOverride("font", PixelKit.Spaced(2));
        _name.TextChanged += OnNameEdited;
        _name.TextSubmitted += _ => _colours[0].GrabFocus();
        _name.FocusEntered += () => Say("2–20 букв, пробел и дефис. Кости придумают имя, замок сбережёт его от «Случайного народа».");
        var dice = SetupUi.IconButton(PxIcon.Dice, "Случайное имя");
        dice.Pressed += () => { if (!_nameLocked) Commit(_d with { Name = NationNames.Random(_rng) }); else Refuse(_lock); };
        _lock = SetupUi.IconButton(PxIcon.Lock, "Замок: «Случайный народ» не трогает имя");
        _lock.ToggleMode = true;
        var lockIcon = (Control)_lock.GetChild(0).GetChild(0);
        lockIcon.Modulate = PixelKit.TextMuted;
        _lock.Toggled += on => { _nameLocked = on; lockIcon.Modulate = on ? PixelKit.AccentLight : PixelKit.TextMuted; Sfx.Try("tick"); };
        _forms = PixelKit.Label("", 13, PixelKit.TextDim);
        _forms.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _forms.ClipText = true;
        _forms.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _forms.TooltipText = "Родительный падеж · прилагательное · жители";
        var nameRow = SetupUi.Row(10, Caption("Имя"), _name, dice, _lock, _forms);

        // ---- colours ----
        var grid = new GridContainer { Columns = compact ? 24 : 12 };
        grid.AddThemeConstantOverride("h_separation", 1);
        grid.AddThemeConstantOverride("v_separation", 1);
        for (int i = 0; i < _colours.Length; i++)
        {
            int k = i;
            var s = new Swatch(compact ? 26 : 30, $"Цвет {i + 1}");
            s.Pressed += () => Commit(_d with { R = NationPalette.Colors[k].R, G = NationPalette.Colors[k].G, B = NationPalette.Colors[k].B });
            s.FocusEntered += () => Say("Цвет державы: на карте, во флаге (квадрат с ромбом) и на городах.");
            _colours[i] = s;
            grid.AddChild(s);
        }
        var ramp = SetupUi.Row(2);
        for (int i = 0; i < 3; i++) { _ramp[i] = new ColorRect { CustomMinimumSize = new Vector2(14, 14) }; ramp.AddChild(_ramp[i]); }
        ramp.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var colourRow = SetupUi.Row(10, Caption("Цвет"), grid, ramp);

        // ---- tabs ----
        _tabs = new PxTabs("Флаг", "Культура");
        _tabs.TabChanged += ShowTab;
        _flag = new FlagEditor(compact);
        _flag.Changed += f => Commit(_d with { Flag = f }, refreshFlag: false);
        _culture = new CultureGrid(compact);
        _culture.Picked += c => Commit(_d with { Culture = (byte)c });

        var left = SetupUi.Column(compact ? 8 : 12, nameRow, colourRow, _tabs, _flag, _culture);
        left.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        // ---- right: my nations + preview ----
        _mine = new OptionButton { CustomMinimumSize = new Vector2(rightW - 46 * 2 - 20, 46), FitToLongestItem = false, ClipText = true };
        _mine.ItemSelected += OnMinePicked;
        var add = SquareButton("+", "Новый народ");
        add.Pressed += () => Commit(NationStore.RandomDesign(_rng) with { Name = _nameLocked ? _d.Name : NationNames.Random(_rng) });
        var del = SquareButton("×", "Удалить из «Моих народов»");
        del.Pressed += () => _ = DeleteSaved();
        _preview = new NationPreview(compact, rightW);
        var right = SetupUi.Column(8, SetupUi.Section("Мои народы"), SetupUi.Row(10, _mine, add, del), SetupUi.Gap(2), _preview);
        right.CustomMinimumSize = new Vector2(rightW, 0);

        AddChild(SetupUi.Row(24, left, right));

        // ---- footer ----
        _status = PixelKit.Label("", 13, PixelKit.TextDim, HorizontalAlignment.Center);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(120, 0);
        var back = SetupUi.WithKey(PixelKit.Button("Назад", "GhostButton"), "Esc");
        back.CustomMinimumSize = new Vector2(160, 46);
        back.Pressed += () => GoBack();
        var random = SetupUi.WithKey(PixelKit.Button("Случайный народ", ""), "R");
        random.CustomMinimumSize = new Vector2(250, 46);
        random.Pressed += RandomNation;
        var save = PixelKit.Button("Сохранить", "");
        save.CustomMinimumSize = new Vector2(160, 46);
        save.Pressed += Save;
        _start = SetupUi.WithKey(PixelKit.Button("НАЧАТЬ", "PrimaryButton"), "Ctrl+Enter");
        _start.CustomMinimumSize = new Vector2(300, 50);
        _start.AddThemeFontOverride("font", PixelKit.Spaced(2));
        _start.AddThemeFontSizeOverride("font_size", 22);
        _start.Pressed += Start;
        AddChild(SetupUi.Row(12, back, random, save, _status, _start));

        ShowTab(_tabs.Current);
        FillMine();
        Apply(refreshName: true);
        Nav.Unify(this);
        if (WorldPreview.CachedWorld == null) { if (NextWorld.IsReady) _ = PrepareChunk(); else NextWorld.Ready += OnWorldReady; }
    }

    public override void _ExitTree() => NextWorld.Ready -= OnWorldReady;

    void OnWorldReady() { if (WorldPreview.CachedWorld == null) _ = PrepareChunk(); }

    void Say(string text, Color? color = null)
    {
        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color ?? PixelKit.TextDim);
    }

    static Button SquareButton(string glyph, string tip)
    {
        var b = PixelKit.Button(glyph, "");
        b.CustomMinimumSize = new Vector2(46, 46);
        b.TooltipText = tip;
        b.AddThemeFontSizeOverride("font_size", 22);
        return b;
    }

    static Label Caption(string text)
    {
        var l = PixelKit.Label(text, 18);
        l.CustomMinimumSize = new Vector2(64, 0);
        return l;
    }

    /// <summary>Opened directly (--front=nation): find the capital of the next world for the «у будущей столицы» piece.</summary>
    async Task PrepareChunk()
    {
        var w = NextWorld.World;
        var caps = await Task.Run(() => Sim.NationGen.PlaceCapitals(w, LastSetup.Load().Nations));
        if (!IsInsideTree()) return;
        WorldPreview.Remember(w, caps);
        _preview.RefreshChunk();
    }

    /// <summary>--front=nation:flag|culture.</summary>
    public override void SelectSection(string section)
    {
        int i = section switch { "flag" => 0, "culture" => 1, _ => -1 };
        if (i >= 0) _tabs.Select(i);
    }

    void ShowTab(int i)
    {
        _flag.Visible = i == 0;
        _culture.Visible = i == 1;
        SetupUi.Stagger(i == 0 ? _flag : _culture, 0, .03f);
    }

    // ---- state ----
    void Commit(NationDesign next, bool refreshFlag = true)
    {
        if (next == _d || _syncing) return;
        _undo.Add(_d);
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
        _d = next;
        NationStore.Current = next;
        Apply(refreshName: next.Name != _name.Text, refreshFlag);
    }

    void Apply(bool refreshName, bool refreshFlag = true)
    {
        _syncing = true;
        if (refreshName) { _name.Text = _d.Name; _nameValid = true; }
        _forms.Text = $"{Sim.Ru.Genitive(_d.Name)} · {NationNames.AdjectiveMasculine(_d.Name)} · {NationNames.People(_d.Name)}";
        int chosen = Array.IndexOf(NationPalette.Colors, _d.Rgb);
        for (int i = 0; i < _colours.Length; i++)
        {
            var c = NationPalette.Colors[i];
            _colours[i].Set(Color.Color8(c.R, c.G, c.B), i == chosen, false, true);
        }
        var (light, main, dark) = NationRamp.From(_d.Rgb);
        _ramp[0].Color = Color.Color8(light.R, light.G, light.B);
        _ramp[1].Color = Color.Color8(main.R, main.G, main.B);
        _ramp[2].Color = Color.Color8(dark.R, dark.G, dark.B);
        if (refreshFlag) _flag.SetFlag(_d.Flag, _d.Rgb); else _flag.SetNation(_d.Rgb);
        _culture.Select(_d.Culture);
        _preview.Show(_d);
        _start.Disabled = !_nameValid;
        SyncMine();
        _syncing = false;
    }

    void OnNameEdited(string text)
    {
        if (_syncing) return;
        var clean = NationNames.Clean(text);
        _nameValid = clean != null;
        _start.Disabled = !_nameValid;
        if (!_nameValid) { Say("Имя — от 2 до 20 букв, можно пробел и дефис.", PixelKit.Warn); return; }
        Say("");
        Commit(_d with { Name = clean });
    }

    void Undo(bool redo)
    {
        var from = redo ? _redo : _undo;
        var to = redo ? _undo : _redo;
        if (from.Count == 0) { Sfx.Try("tick", .8f); return; }
        to.Add(_d);
        _d = from[^1];
        from.RemoveAt(from.Count - 1);
        NationStore.Current = _d;
        Sfx.Try("remove");
        Apply(refreshName: true);
    }

    void RandomNation()
    {
        var r = NationStore.RandomDesign(_rng);
        Commit(_d with { Name = _nameLocked ? _d.Name : r.Name, R = r.R, G = r.G, B = r.B, Flag = r.Flag, Culture = r.Culture });
        Sfx.Try("pop");
        PixelKit.Bump(_preview, 1.02f);
    }

    static void Refuse(Control c) { Sfx.Try("error"); PixelKit.Shake(c, 6); }

    // ---- «Мои народы» ----
    void FillMine()
    {
        _saved = NationStore.List();
        SyncMine();
    }

    void SyncMine()
    {
        _mine.Clear();
        int selected = -1;
        if (!_saved.Exists(s => s.Id == _d.Id)) { _mine.AddItem($"Черновик · {_d.Name}"); selected = 0; }
        foreach (var s in _saved)
        {
            if (s.Id == _d.Id) selected = _mine.ItemCount;
            _mine.AddItem(s.Id == _d.Id ? _d.Name : s.Name);
        }
        _mine.Select(selected);
    }

    void OnMinePicked(long index)
    {
        int offset = _saved.Exists(s => s.Id == _d.Id) ? 0 : 1;
        int i = (int)index - offset;
        if (i < 0 || i >= _saved.Count) return;
        Commit(_saved[i]);
        Sfx.Try("pop");
    }

    void Save()
    {
        NationStore.Save(_d);
        FillMine();
        Sfx.Try("ready");
        Say($"«{_d.Name}» — в «Моих народах»", PixelKit.Good);
        PixelKit.PopIn(_status, 0, .8f, .3f);
    }

    async Task DeleteSaved()
    {
        if (!NationStore.IsSaved(_d.Id)) { Refuse(_mine); return; }
        int a = await Shell.Confirm("Удалить народ?", $"«{_d.Name}» пропадёт из «Моих народов». Черновик останется на экране.",
            new[] { "Удалить", "Отмена" }, focus: 1, danger: 0);
        if (a != 0) return;
        NationStore.Delete(_d.Id);
        Commit(_d with { Id = NationStore.NewId() });
        FillMine();
        Sfx.Try("remove");
    }

    // ---- start / back / keys ----
    void Start()
    {
        if (!_nameValid) { Refuse(_name); return; }
        if (StartGame != null) { StartGame(); return; }
        var last = LastSetup.Load();
        int seed = NextWorld.Seed;
        var setup = new GameSetup(seed, seed.ToString(), last.Nations, last.Fog, last.Pace, true, NationStore.TouchCurrent());
        LastSetup.Save(setup);
        Shell.StartGame(setup);
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k || !IsVisibleInTree() || PxConfirm.IsOpen) return;
        bool typing = GetViewport().GuiGetFocusOwner() is LineEdit;
        bool handled = true;
        if (k.CtrlPressed && k.Keycode is Key.Enter or Key.KpEnter) Start();
        else if (k.CtrlPressed && k.PhysicalKeycode == Key.Z) Undo(k.ShiftPressed);
        else if (k.CtrlPressed && k.PhysicalKeycode == Key.Y) Undo(true);
        else if (typing || k.Echo || k.CtrlPressed) handled = false;
        else if (k.PhysicalKeycode == Key.R) RandomNation();
        else handled = false;
        if (handled) GetViewport().SetInputAsHandled();
    }
}

/// <summary>The «КУЛЬТУРА» tab: 5 cards (3 + 2); the chosen one is inverted like the PrimaryButton.</summary>
public partial class CultureGrid : VBoxContainer
{
    readonly Button[] _cards = new Button[NationNames.Cultures.Length];
    readonly List<Label>[] _labels = new List<Label>[NationNames.Cultures.Length];
    public event Action<int> Picked;

    public CultureGrid(bool compact)
    {
        AddThemeConstantOverride("separation", 12);
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 12);
        for (int i = 0; i < _cards.Length; i++)
        {
            int k = i;
            var c = NationNames.Cultures[i];
            var b = PixelKit.Button("", "");
            b.CustomMinimumSize = new Vector2(compact ? 236 : 226, 118);
            b.Pressed += () => Picked?.Invoke(k);
            var name = PixelKit.Label(c.Name, 22, PixelKit.AccentLight);
            name.AddThemeFontOverride("font", PixelKit.Spaced(1));
            var cities = PixelKit.Label(string.Join(" · ", c.Cities), 13, PixelKit.TextDim);
            cities.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            var people = PixelKit.Label(string.Join(" · ", c.People), 13, PixelKit.TextDim);
            var col = SetupUi.Column(4, name, cities, people);
            col.MouseFilter = MouseFilterEnum.Ignore;
            var pad = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
            pad.SetAnchorsPreset(LayoutPreset.FullRect);
            foreach (var side in new[] { "left", "right", "top", "bottom" }) pad.AddThemeConstantOverride("margin_" + side, side is "left" or "right" ? 16 : 12);
            pad.AddChild(col);
            foreach (var l in new[] { name, cities, people }) l.MouseFilter = MouseFilterEnum.Ignore;
            b.AddChild(pad);
            _labels[i] = new List<Label> { name, cities, people };
            _cards[i] = b;
            grid.AddChild(b);
        }
        AddChild(grid);
        AddChild(PixelKit.Paragraph("Культура уже даёт народу прилагательное; имена городов и людей по ней появятся вместе с генератором имён.", 13, PixelKit.TextMuted));
    }

    public void Select(int culture)
    {
        for (int i = 0; i < _cards.Length; i++)
        {
            bool on = i == culture;
            _cards[i].ThemeTypeVariation = on ? "PrimaryButton" : "";
            _labels[i][0].AddThemeColorOverride("font_color", on ? PixelKit.Ink : PixelKit.AccentLight);
            _labels[i][1].AddThemeColorOverride("font_color", on ? PixelKit.Panel : PixelKit.TextDim);
            _labels[i][2].AddThemeColorOverride("font_color", on ? PixelKit.Panel : PixelKit.TextDim);
        }
    }
}
