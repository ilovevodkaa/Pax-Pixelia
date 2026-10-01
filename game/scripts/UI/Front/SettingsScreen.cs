using System;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI.Front;

/// <summary>
/// «Настройки» (MAIN_MENU.md §3.4, port of «Mr. President» settings_screen.gd): tabs ГРАФИКА · ЗВУК · ИНТЕРФЕЙС.
/// Changes apply at once; СОХРАНИТЬ writes user://settings.cfg; «Назад» with unsaved changes asks; a new window mode
/// or size must be confirmed within 10 s or it rolls back. Lives on the front-end stack and — the same screen — in the
/// pause menu: then <see cref="FrontScreen.Shell"/> is null, dialogs go to <see cref="DialogHost"/> and
/// <see cref="Closed"/> hands control back.
/// </summary>
public partial class SettingsScreen : FrontScreen
{
    public override string Title => "Настройки";
    public override float PanelWidth => 760;
    public override Control DefaultFocus => _first ?? _tabs?.Tabs[_tabs.Current];
    public override (string key, string text)[] Hints => new[] { ("Up Down", "выбор"), ("Left Right", "значение"), ("Q E", "раздел"), ("Esc", "назад") };

    /// <summary>Raised when the screen closes outside the front-end stack (pause menu).</summary>
    public event Action Closed;
    /// <summary>Full-screen parent for dialogs when there is no FrontShell (the pause menu sets itself).</summary>
    public Control DialogHost;

    static readonly string[] Sections = { Settings.Video, Settings.Audio, Settings.Ui };
    static readonly string[] Modes = { "Окно", "Без рамки", "Полный экран" };
    static readonly string[] OnOff = { "Выкл", "Вкл" };
    const string WindowOnly = "Разрешение меняется только в режиме «Окно».";

    PxTabs _tabs;
    VBoxContainer _body;
    ScrollContainer _scroll;
    Label _hint, _status;
    Button _save;
    Control _first;
    bool _asking;

    static Settings S => Settings.I;
    Node Host => (Node)Shell?.UiRoot ?? DialogHost ?? this;

    public override void Build()
    {
        if (S == null) { AddChild(PixelKit.Paragraph("Настройки недоступны: автозагрузка Settings не подключена.", 16)); return; }
        _tabs = new PxTabs("Графика", "Звук", "Интерфейс");
        _tabs.TabChanged += _ => Fill(true);
        AddChild(_tabs);

        _body = SetupUi.Column(8);
        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        _scroll.AddChild(_body);
        AddChild(_scroll);

        AddChild(SetupUi.Rule());
        _hint = SetupUi.HintBox(PanelWidth - 80);
        AddChild(_hint);
        _status = PixelKit.Label("", 13, PixelKit.TextDim);
        _status.CustomMinimumSize = new Vector2(0, 18);
        AddChild(_status);

        var back = SetupUi.WithKey(PixelKit.Button("Назад", "GhostButton"), "Esc");
        back.CustomMinimumSize = new Vector2(160, 46);
        back.Pressed += () => GoBack();
        var reset = PixelKit.Button("Сбросить раздел", "GhostButton");
        reset.CustomMinimumSize = new Vector2(200, 46);
        reset.Pressed += ResetSection;
        _save = PixelKit.Button("СОХРАНИТЬ", "PrimaryButton");
        _save.CustomMinimumSize = new Vector2(220, 46);
        _save.AddThemeFontOverride("font", PixelKit.Spaced(2));
        _save.Pressed += Save;
        AddChild(SetupUi.Row(10, back, reset, SetupUi.Spacer(), _save));

        Fill(false);
        S.Changed += OnChanged;
    }

    public override void _ExitTree() { if (S != null) S.Changed -= OnChanged; }

    /// <summary>--front=settings:video|audio|ui.</summary>
    public override void SelectSection(string section)
    {
        int i = Array.IndexOf(Sections, section);
        if (i >= 0) _tabs?.Select(i);
    }

    void OnChanged(string _) => UpdateStatus();

    // ---- rows ----
    void Fill(bool animate)
    {
        foreach (var n in _body.GetChildren()) { _body.RemoveChild(n); n.QueueFree(); }
        _first = null;
        switch (_tabs.Current)
        {
            case 0: FillVideo(); break;
            case 1: FillAudio(); break;
            default: FillUi(); break;
        }
        Callable.From(FitScroll).CallDeferred();
        Nav.Unify(_body);
        _hint.Text = "";
        UpdateStatus();
        if (!animate) return;
        SetupUi.Stagger(_body, 0, .03f);
        Callable.From(() => _first?.GrabFocus()).CallDeferred();
    }

    /// <summary>The list shows every row when it fits; on low windows it scrolls so the panel stays inside.</summary>
    void FitScroll()
    {
        if (!IsInsideTree()) return;
        float maxH = GetViewportRect().Size.Y - 420;
        _scroll.CustomMinimumSize = new Vector2(0, Mathf.Clamp(_body.GetCombinedMinimumSize().Y, 120, Mathf.Max(160, maxH)));
    }

    PxOptionRow Row(string caption, string[] values, int index, string hint, Action<int> apply)
    {
        var r = new PxOptionRow(caption, values, index) { Hint = hint };
        r.FocusEntered += () => _hint.Text = r.Hint;
        r.Changed += i => apply(i);
        _body.AddChild(r);
        _first ??= r;
        return r;
    }

    void FillVideo()
    {
        var res = Settings.AvailableResolutions();
        var names = res.ConvertAll(v => $"{v.X} × {v.Y}").ToArray();
        PxOptionRow resRow = null;
        var mode = Row("Режим окна", Modes, S.Get<int>(Settings.Video, "mode"),
            "Окно — обычное окно. Без рамки — на весь экран, Alt+Tab без чёрного кадра. Полный экран — монопольный режим.",
            i => _ = ConfirmVideo("mode", i, () => resRow.SetLocked(i == 0 ? null : WindowOnly)));
        resRow = Row("Разрешение", names, Math.Max(0, res.IndexOf(S.Resolution)), "Размер окна; окно встанет по центру экрана.",
            i => _ = ConfirmVideo("resolution", Settings.ResolutionText(res[i]), null));
        if (mode.Index != 0) resRow.SetLocked(WindowOnly);
        Row("Вертикальная синхронизация", OnOff, S.Get<bool>(Settings.Video, "vsync") ? 1 : 0,
            "Убирает разрывы кадра. Выключите, если важна каждая миллисекунда отклика.",
            i => S.Set(Settings.Video, "vsync", i == 1));
        var fps = Array.ConvertAll(Settings.FpsSteps, f => f == 0 ? "Без ограничения" : f.ToString());
        Row("Лимит кадров", fps, Math.Max(0, Array.IndexOf(Settings.FpsSteps, S.Get<int>(Settings.Video, "fps"))),
            "Сколько кадров в секунду рисовать. Окно в фоне всегда рисует не больше 15.",
            i => S.Set(Settings.Video, "fps", Settings.FpsSteps[i]));
        if (Settings.CliMode) _body.AddChild(PixelKit.Label("Запуск с отладочными флагами: окно не меняется.", 13, PixelKit.TextMuted));
    }

    void FillAudio()
    {
        Slider("Общая громкость", "master", "Громкость всей игры.");
        Slider("Музыка", "music", "Музыки в этой версии ещё нет — ползунок пригодится с первой темой.");
        Slider("Эффекты", "sfx", "Щелчки кнопок, фишки и стройка на карте, летопись и сигналы событий.");
        Row("Звук в фоне", OnOff, S.Get<bool>(Settings.Audio, "background") ? 1 : 0,
            "Играть ли звуки, когда окно игры не в фокусе.", i => S.Set(Settings.Audio, "background", i == 1));
    }

    void FillUi()
    {
        int scale = Array.IndexOf(Settings.ScaleSteps, S.Get<int>(Settings.Ui, "scale"));
        var names = new[] { $"Авто · {Mathf.RoundToInt(Settings.AutoScale() * 100)}%", "100%", "150%", "200%" };
        Row("Масштаб интерфейса", names, Math.Max(0, scale),
            "Крупнее — для больших мониторов. Ступени 100 / 150 / 200% держат пиксели шрифта ровными; окно должно вмещать 1280 × 720.",
            i => S.Set(Settings.Ui, "scale", Settings.ScaleSteps[i]));
        Row("Меньше анимации", OnOff, S.ReducedMotion ? 1 : 0,
            "Без интро и переливов, шторки короче. Карта и время живут как обычно.",
            i => S.Set(Settings.Ui, "reduced_motion", i == 1));
    }

    /// <summary>Label · slider · «70%» (port of _slider); the whole row lights up like an option row when focused.</summary>
    void Slider(string caption, string key, string hint)
    {
        var label = PixelKit.Label(caption, 18);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var slider = new HSlider { MinValue = 0, MaxValue = 100, Step = 1, Value = S.Get<int>(Settings.Audio, key), CustomMinimumSize = new Vector2(250, 24) };
        slider.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        var value = PixelKit.Label($"{(int)slider.Value}%", 18, PixelKit.AccentLight, HorizontalAlignment.Right);
        value.CustomMinimumSize = new Vector2(64, 0);
        var row = new PanelContainer { CustomMinimumSize = new Vector2(0, 40) };
        var idle = PixelKit.Padded(PixelKit.Box(Colors.Transparent), 12, 6);
        var hot = PixelKit.Padded(PixelKit.Box(PixelKit.SurfaceHover), 12, 6);
        row.AddThemeStyleboxOverride("panel", idle);
        row.AddChild(SetupUi.Row(12, label, slider, value));
        slider.ValueChanged += v =>
        {
            value.Text = $"{(int)v}%";
            S.Set(Settings.Audio, key, (int)v);
            PixelKit.Sfx?.Invoke("tick", .9f + (float)v / 500f);
        };
        slider.FocusEntered += () => { _hint.Text = hint; row.AddThemeStyleboxOverride("panel", hot); label.AddThemeColorOverride("font_color", PixelKit.AccentLight); };
        slider.FocusExited += () => { row.AddThemeStyleboxOverride("panel", idle); label.AddThemeColorOverride("font_color", PixelKit.Text); };
        row.MouseEntered += () => slider.GrabFocus();
        _body.AddChild(row);
        _first ??= slider;
    }

    /// <summary>A new window mode / size must be confirmed within 10 s, otherwise it rolls back (a broken mode on a
    /// friend's monitor must never lock them out).</summary>
    async Task ConfirmVideo<T>(string key, T value, Action after)
    {
        var old = S.Get<T>(Settings.Video, key);
        S.Set(Settings.Video, key, value);
        after?.Invoke();
        if (Settings.CliMode || _asking) return;
        _asking = true;
        int answer = await PxConfirm.Ask(Host, "Оставить этот режим?", "Вернём прежний через {0} с.", new[] { "Оставить", "Вернуть" },
            countdown: 10, focus: 1, danger: -1);
        _asking = false;
        if (answer == 0 || !IsInsideTree()) return;
        S.Set(Settings.Video, key, old);
        Fill(false);
    }

    // ---- footer ----
    void UpdateStatus()
    {
        if (_status == null) return;
        bool dirty = S.IsDirty;
        _save.Disabled = !dirty;
        _status.Text = dirty ? "Есть несохранённые изменения" : "Все изменения сохранены";
        _status.AddThemeColorOverride("font_color", dirty ? PixelKit.Accent : PixelKit.TextDim);
    }

    void Save()
    {
        S.Save();
        PixelKit.Sfx?.Invoke("confirm", 1f);
        UpdateStatus();
        _status.Text = "Сохранено";
        _status.AddThemeColorOverride("font_color", PixelKit.Good);
        PixelKit.PopIn(_status, 0, .8f, .3f);
        DefaultFocus?.GrabFocus();
    }

    void ResetSection()
    {
        S.ResetSection(Sections[_tabs.Current]);
        PixelKit.Sfx?.Invoke("close", 1f);
        Fill(true);
    }

    public override bool GoBack()
    {
        if (_asking) return false;
        if (S != null && S.IsDirty) { _ = AskSave(); return false; }
        Close();
        return true;
    }

    async Task AskSave()
    {
        _asking = true;
        int a = await PxConfirm.Ask(Host, "Сохранить изменения?", "Без сохранения настройки вернутся к прежним.",
            new[] { "Сохранить", "Не сохранять", "Отмена" }, focus: 0, danger: 1);
        _asking = false;
        if (a == 0) S.Save();
        else if (a == 1) S.Revert();
        else return;
        Close();
    }

    void Close()
    {
        if (Shell != null) Shell.Pop();
        else Closed?.Invoke();
    }
}
