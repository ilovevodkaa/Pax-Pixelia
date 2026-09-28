using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Core.Flags;
using PaxPixelia.Core.Save;

namespace PaxPixelia.UI.Front;

/// <summary>
/// «Загрузить» (F-4): the saves, newest first, in a scrolling list on the left — thumbnail, name, nation and date,
/// the kind tag (АВТО / РУЧНОЕ), playtime and when it was saved — and the chosen one on the right: the 320×180
/// thumbnail with the player's fog, flag and nation, era, date, playtime, the world. Enter or a click loads,
/// «Удалить» (or Delete) asks through PxConfirm. Saves this build cannot read are listed dimmed with the reason and
/// can only be deleted. Opened from the title (FrontShell) and from the pause menu (ScreenHost: Shell is null, dialogs
/// go to <see cref="DialogHost"/>, <see cref="LoadRequested"/> loads and <see cref="Closed"/> hands control back).
/// </summary>
public partial class LoadScreen : FrontScreen
{
    public override string Title => "Загрузить";
    public override float PanelWidth => 1020;
    public override Control DefaultFocus => _rows.Count > 0 ? _rows[0].Button : _back;
    public override (string key, string text)[] Hints => _rows.Count > 0
        ? new[] { ("Up Down", "выбор"), ("Enter", "загрузить"), ("Del", "удалить"), ("Esc", "назад") }
        : new[] { ("Esc", "назад") };

    /// <summary>Pause-menu host: back hands control to it.</summary>
    public event Action Closed;
    /// <summary>Full-screen parent for dialogs when there is no FrontShell.</summary>
    public Control DialogHost;
    /// <summary>Pause-menu host: load this save (the title's shell loads through Shell.LoadSave).</summary>
    public Action<SaveEntry> LoadRequested;
    /// <summary>A save was deleted while the screen was open (the title refreshes «ПРОДОЛЖИТЬ»).</summary>
    public bool Changed { get; private set; }

    sealed record Row(SaveEntry Entry, Button Button);

    /// <summary>The listed saves and their row buttons, in order (tests).</summary>
    internal IReadOnlyList<(SaveEntry Entry, Button Button)> Rows => _rows.ConvertAll(r => (r.Entry, r.Button));

    readonly List<Row> _rows = new();
    SaveEntry _selected;
    bool _compact;
    VBoxContainer _list;
    ScrollContainer _scroll;
    TextureRect _thumb, _flag;
    Label _name, _nation, _lines, _error;
    float _listW;
    Button _load, _delete, _back;
    Control _detail;

    Node Host => (Node)Shell?.UiRoot ?? DialogHost ?? this;

    public override void Build()
    {
        var view = GetViewportRect().Size / Mathf.Max(1f, Shell == null ? 1f : FrontShell.UiScale(GetViewportRect().Size));
        _compact = view.Y < 800;
        float width = Mathf.Min(PanelWidth, view.X - 48) - 68;
        float detailW = _compact ? 256 : 320;
        float listW = width - detailW - 24;
        float listH = _compact ? Mathf.Clamp(view.Y - 330, 180, 330) : Mathf.Clamp(view.Y - 360, 300, 470);

        _list = SetupUi.Column(6);
        _list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll = new ScrollContainer { CustomMinimumSize = new Vector2(listW, listH), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _scroll.AddChild(_list);
        _listW = listW;
        var left = SetupUi.Column(8, SetupUi.Section("Сохранения"), _scroll);

        _thumb = SetupUi.Pixel(new Vector2(detailW, detailW * 9 / 16));
        _thumb.TextureFilter = TextureFilterEnum.Linear;
        var frame = SetupUi.Card(_thumb, 0, 0, PixelKit.Ink);
        _name = PixelKit.Label("", _compact ? 18 : 22, PixelKit.AccentLight);
        _name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _name.CustomMinimumSize = new Vector2(detailW, 0);
        _flag = SetupUi.Pixel(new Vector2(30, 21));
        _flag.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _nation = PixelKit.Label("", 16, PixelKit.Text);
        _lines = PixelKit.Label("", _compact ? 13 : 15, PixelKit.TextDim);
        _lines.CustomMinimumSize = new Vector2(detailW, 0);
        _lines.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _error = PixelKit.Paragraph("", 15, PixelKit.Bad);
        _error.CustomMinimumSize = new Vector2(detailW, 0);
        _load = SetupUi.WithKey(MakeButton("ЗАГРУЗИТЬ", () => Load(_selected), "PrimaryButton", detailW), "Enter");
        _load.AddThemeFontOverride("font", PixelKit.Spaced(2));
        _delete = SetupUi.WithKey(MakeButton("Удалить", () => _ = Delete(_selected), "GhostButton", detailW), "Del");
        _detail = SetupUi.Column(_compact ? 6 : 10, SetupUi.Section("Выбрано"), frame, _name, SetupUi.Row(10, _flag, _nation), _lines, _error,
            SetupUi.Gap(_compact ? 2 : 6), _load, _delete);
        _detail.CustomMinimumSize = new Vector2(detailW, 0);

        AddChild(SetupUi.Row(24, left, _detail));
        _back = MakeButton("Назад", () => GoBack(), "GhostButton", 180);
        Footer(new Control[] { _back }, Array.Empty<Control>());
        Fill();
    }

    public override void OnShown()
    {
        base.OnShown();
        if (_rows.Count > 0) Select(_rows[0].Entry);
    }

    public override bool GoBack()
    {
        SaveStore.ReleaseThumbs();
        if (Shell != null) Shell.Pop();
        else Closed?.Invoke();
        return true;
    }

    // ---- the list ----

    void Fill()
    {
        foreach (var c in _list.GetChildren()) c.QueueFree();
        _rows.Clear();
        var entries = SaveStore.List();
        foreach (var e in entries)
        {
            var b = MakeRow(e);
            _list.AddChild(b);
            _rows.Add(new Row(e, b));
        }
        if (_rows.Count == 0)
        {
            var empty = PixelKit.Paragraph("Сохранений пока нет. Игра сохраняется сама — раз в десять игровых лет и когда вы выходите в меню.", 16, PixelKit.TextDim);
            empty.CustomMinimumSize = new Vector2(_listW - 20, 0);
            _list.AddChild(empty);
        }
        for (int i = 0; i < _rows.Count; i++)
        {
            var b = _rows[i].Button;
            b.FocusNeighborLeft = b.FocusNeighborRight = ".";
            if (i == _rows.Count - 1) b.FocusNeighborBottom = b.GetPathTo(_back);
        }
        Nav.Unify(_list);
        _selected = null;
        ShowDetail(_rows.Count > 0 ? _rows[0].Entry : null);
    }

    Button MakeRow(SaveEntry e)
    {
        var b = PixelKit.Button("", "");
        b.CustomMinimumSize = new Vector2(0, _compact ? 62 : 76);
        b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        b.ClipContents = true;
        float tw = _compact ? 88 : 112, th = tw * 9 / 16;
        var thumb = SetupUi.Pixel(new Vector2(tw, th));
        thumb.TextureFilter = TextureFilterEnum.Linear;
        thumb.Texture = SaveStore.Thumb(e);
        var thumbBox = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        thumbBox.AddThemeStyleboxOverride("panel", PixelKit.Box(PixelKit.Ink, PixelKit.PanelBorder));
        thumbBox.AddChild(thumb);

        var h = e.Header;
        var title = PixelKit.Label(h == null ? e.FileName : SaveText.Title(h), _compact ? 15 : 16, e.Ok ? PixelKit.Text : PixelKit.TextDim);
        var sub = PixelKit.Label(h == null ? e.Error : $"{h.NationName} · {h.DateText}", 13, e.Ok ? PixelKit.TextDim : PixelKit.Bad);
        var tag = PixelKit.Label(h == null ? "" : $"{SaveText.Kind(h.Kind)} · в игре {SaveText.Playtime(h.PlaytimeMs)} · {SaveText.When(h.SavedUnixMs)}", 13, PixelKit.TextMuted);
        foreach (var l in new[] { title, sub, tag })
        {
            l.ClipText = true;
            l.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            l.MouseFilter = MouseFilterEnum.Ignore;
        }
        var text = SetupUi.Column(2, title, sub, tag);
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        text.Alignment = BoxContainer.AlignmentMode.Center;
        text.MouseFilter = MouseFilterEnum.Ignore;
        var row = SetupUi.Row(12, thumbBox, text);
        row.MouseFilter = MouseFilterEnum.Ignore;
        var pad = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        foreach (var (side, px) in new[] { ("left", 8), ("right", 10), ("top", 6), ("bottom", 6) }) pad.AddThemeConstantOverride("margin_" + side, px);
        pad.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pad.AddChild(row);
        b.AddChild(pad);
        if (!e.Ok) b.Modulate = new Color(1, 1, 1, .6f);
        b.FocusEntered += () => { Select(e); _scroll.EnsureControlVisible(b); };
        b.Pressed += () => Load(e);
        b.TooltipText = e.Ok ? "" : e.Error;
        return b;
    }

    void Select(SaveEntry e)
    {
        if (e == null || ReferenceEquals(e, _selected)) return;
        ShowDetail(e);
    }

    /// <summary>The chosen row keeps the hover look (focus rings only show while steering with the keyboard).</summary>
    void MarkSelected()
    {
        foreach (var r in _rows)
        {
            if (ReferenceEquals(r.Entry, _selected)) r.Button.AddThemeStyleboxOverride("normal", r.Button.GetThemeStylebox("hover"));
            else r.Button.RemoveThemeStyleboxOverride("normal");
        }
    }

    void ShowDetail(SaveEntry e)
    {
        _selected = e;
        MarkSelected();
        _detail.Visible = e != null;
        if (e == null) return;
        var h = e.Header;
        _thumb.Texture = SaveStore.Thumb(e);
        _name.Text = h == null ? e.FileName : SaveText.Title(h);
        var error = SaveStore.Check(e);   // the whole file: a damaged body is caught before the scene changes
        _error.Visible = error != null;
        _error.Text = error ?? "";
        _load.Disabled = error != null;
        if (h == null)
        {
            _flag.Visible = false;
            _nation.Text = "";
            _lines.Text = $"Файл: {e.FileName}\nИзменён: {SaveText.When(e.ModifiedUnixMs)}";
            return;
        }
        _flag.Visible = true;
        var rgb = (h.R, h.G, h.B);
        _flag.Texture = FlagTextures.Get(h.Flag == default ? FlagSpec.ForBot(h.Seed, 0) : h.Flag, rgb);
        _nation.Text = h.NationName;
        _nation.AddThemeColorOverride("font_color", PixelKit.Text);
        var era = Sim.Eras.Name(Math.Clamp(h.Era, 0, Sim.Eras.Last));
        _lines.Text = _compact
            ? $"{era} · {h.DateText}\nВ игре {SaveText.Playtime(h.PlaytimeMs)} · {SaveText.When(h.SavedUnixMs)}\nЗерно {h.SeedText} · держав {h.NationCount} · {SaveText.Pace(h.PacePermille).ToLower()}"
            : $"Эпоха: {era}\nДата: {h.DateText}\nВ игре: {SaveText.Playtime(h.PlaytimeMs)}\nСохранено: {SaveText.When(h.SavedUnixMs)} · {SaveText.Kind(h.Kind).ToLower()}\n" +
              $"Мир: зерно {h.SeedText} · держав {h.NationCount} · {SaveText.Pace(h.PacePermille).ToLower()}";
    }

    // ---- actions ----

    void Load(SaveEntry e)
    {
        if (e == null) return;
        if (SaveStore.Check(e) != null) { ShowDetail(e); PixelKit.Shake(_load, 6); PixelKit.Sfx?.Invoke("error", 1f); return; }
        if (LoadRequested != null) LoadRequested(e);
        else Shell?.LoadSave(e);
    }

    async Task Delete(SaveEntry e)
    {
        if (e == null || PxConfirm.IsOpen) return;
        int a = await PxConfirm.Ask(Host, "Удалить сохранение?", $"«{(e.Header == null ? e.FileName : SaveText.Title(e.Header))}» пропадёт навсегда.",
            new[] { "Удалить", "Отмена" }, focus: 1, danger: 0);
        if (a != 0 || !IsInsideTree()) return;
        int index = _rows.FindIndex(r => ReferenceEquals(r.Entry, e));
        if (!SaveStore.Delete(e)) { PixelKit.Sfx?.Invoke("error", 1f); return; }
        Changed = true;
        Fill();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (_rows.Count == 0) { _back.GrabFocus(); return; }
        var next = _rows[Math.Clamp(index, 0, _rows.Count - 1)];
        ShowDetail(next.Entry);
        next.Button.GrabFocus();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Delete } || PxConfirm.IsOpen || !IsVisibleInTree()) return;
        if (GetViewport().GuiGetFocusOwner() is Button b && _rows.Find(r => r.Button == b) is { } row)
        {
            GetViewport().SetInputAsHandled();
            _ = Delete(row.Entry);
        }
    }
}
