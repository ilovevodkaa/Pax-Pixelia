using System;
using System.Collections.Generic;
using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// #tip — the white card that follows the mouse. Content is composed with a tiny builder
/// (title in Alegreya SC, prose, key/value rows with a hairline, stale chip, scout-pick line).
/// </summary>
public partial class TipCard : PanelContainer
{
    const int MaxInner = 266;   // max-width 290 minus 2×12 padding
    readonly VBoxContainer _v;
    readonly ProvinceTipView _province = new();
    bool _hasBody;

    public TipCard()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        TopLevel = false;
        AddThemeStyleboxOverride("panel", new Box().Fill(Pal.Hex(0xfcfcfd)).Border(Pal.Ln2).Radius(St.R)
            .Shadow(Pal.Shade(.07f), 16, 7).Shadow(Pal.Shade(.10f), 5, 3).Shadow(Pal.Shade(.12f), 1, 1).Pad(12, 8, 12, 10));
        _v = Ui.VBox(0);
        AddChild(_v);
        AddChild(_province);
    }

    public void Build(Action<TipCard> build)
    {
        Ui.Clear(_v);
        _hasBody = false;
        build(this);
        _v.Visible = true;
        _province.Visible = false;
        Size = Vector2.Zero;
    }

    /// <summary>Switch to the persistent province layout (no controls are created).</summary>
    public bool BuildProvince(int p)
    {
        _v.Visible = false;
        _province.Visible = true;
        Size = Vector2.Zero;
        return _province.Set(p);
    }

    public TipCard Title(string text)
    {
        var l = Wrapped(text, "TipTitle", UiFonts.Fd700, 16);
        _v.AddChild(l);
        _v.AddChild(Ui.Gap(0, 2));
        return this;
    }

    public TipCard Line(string text) { _v.AddChild(Wrapped(text, null, UiFonts.Fu400, 13)); _hasBody = true; return this; }
    public TipCard Mu(string text) { _v.AddChild(Wrapped(text, "Mu", UiFonts.Fu400, 13)); _hasBody = true; return this; }

    /// <summary>.tr — key/value row under a hairline: [swatch] left … right (+ muted unit).</summary>
    public TipCard Row(Color? swatch, string left, string right, string unit = null)
    {
        var l = Ui.HBox(0, swatch is { } c ? new Swatch(c) : null, Ui.Gap(swatch != null ? 6 : 0, 0), Ui.Text(left));
        var r = Ui.HBox(4, Ui.Text(right, "Semi"), unit != null ? Ui.Text(unit, "Mu") : null);
        Separated(Ui.HBox(16, l.Grow(), r));
        return this;
    }

    /// <summary>Plain key/value row without swatch (e.g. «Налоги … +18»).</summary>
    public TipCard Kv(string left, string right, Color? rightColor = null)
    {
        var rl = Ui.Text(right, "Semi");
        if (rightColor is { } c) rl.Colored(c);
        Separated(Ui.HBox(16, Ui.Text(left).Grow(), rl));
        return this;
    }

    /// <summary>«Сведения устарели» — the explored-but-not-visible chip.</summary>
    public TipCard Stale()
    {
        var chip = Ui.Panel(St.Stale().Pad(5, 0, 7, 0), Ui.HBox(5, Ui.Icon("history", 13, Pal.Mu), Ui.Cap("Сведения устарели").Sized(10)));
        chip.CustomMinimumSize = new Vector2(0, 20);
        chip.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _v.AddChild(Ui.Gap(0, 8));
        _v.AddChild(chip);
        return this;
    }

    /// <summary>Scout targeting hint under a hairline; red when the target is invalid.</summary>
    public TipCard Pick(string icon, string text, bool bad)
    {
        var c = bad ? Pal.Bad : Pal.Tx;
        Separated(Ui.HBox(6, Ui.Icon(icon, 15, bad ? Pal.Bad : Pal.Tx2), Ui.Text(text, "Strong").Colored(c)), 7);
        return this;
    }

    void Separated(Control row, int top = 6)
    {
        _v.AddChild(Ui.Gap(0, _hasBody ? top : 3));
        _v.AddChild(Ui.Rule(Pal.Ln, 0, 1));
        _v.AddChild(Ui.Gap(0, 6));
        _v.AddChild(row);
        _hasBody = true;
    }

    /// <summary>Label that wraps only when wider than the tooltip's max width (so short tips shrink to fit).</summary>
    static Label Wrapped(string text, string role, Font font, int size)
    {
        var l = Ui.Text(text, role);
        if (font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X > MaxInner)
        {
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.CustomMinimumSize = new Vector2(MaxInner, 0);
            l.Size = new Vector2(MaxInner, 0);
        }
        return l;
    }

    /// <summary>Place next to the mouse (16/18 px offset), flipping to stay on screen.</summary>
    public void ShowAt(Vector2 mouse, Vector2 screen)
    {
        var s = GetCombinedMinimumSize();
        Size = s;
        float x = mouse.X + 16, y = mouse.Y + 18;
        if (x + s.X > screen.X - 8) x = mouse.X - s.X - 12;
        if (y + s.Y > screen.Y - 8) y = mouse.Y - s.Y - 12;
        Position = new Vector2(Mathf.Round(Mathf.Max(4, x)), Mathf.Round(Mathf.Max(4, y)));
        Visible = true;
    }
}

/// <summary>
/// Tooltip registry for UI elements (the CSS data-tip). The HUD looks up the hovered control and its ancestors
/// every frame and rebuilds the card only when the owner changes.
/// </summary>
public static class Tips
{
    static readonly Dictionary<ulong, Action<TipCard>> Map = new();

    public static T Tip<T>(this T c, Action<TipCard> build) where T : Control
    {
        ulong id = c.GetInstanceId();
        if (!Map.ContainsKey(id)) c.TreeExited += () => Map.Remove(id);
        Map[id] = build;
        return c;
    }

    public static T Tip<T>(this T c, string title, string line = null, string mu = null) where T : Control =>
        c.Tip(t => { t.Title(title); if (line != null) t.Line(line); if (mu != null) t.Mu(mu); });

    public static bool TryFind(Control hovered, out Control owner, out Action<TipCard> build)
    {
        for (Node n = hovered; n is Control c; n = n.GetParent())
            if (Map.TryGetValue(c.GetInstanceId(), out build)) { owner = c; return true; }
        owner = null; build = null;
        return false;
    }
}
