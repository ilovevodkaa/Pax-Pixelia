using System;
using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>
/// A screen shown in the front-end's overlay panel (port of Mr. President's screen_base.gd, MAIN_MENU.md §2.3).
/// FrontShell sets <see cref="Shell"/>, puts the screen under the panel header (crumbs, title, divider, subtitle),
/// calls <see cref="Build"/>, animates the panel in and then calls <see cref="OnShown"/>. Screens are kept alive
/// while another screen is pushed on top, so their state survives «Назад».
/// </summary>
public abstract partial class FrontScreen : VBoxContainer
{
    public static readonly (string key, string text)[] DefaultHints = { ("Up Down", "выбор"), ("Enter", "принять"), ("Esc", "назад") };

    /// <summary>Set by FrontShell before <see cref="Build"/>.</summary>
    public FrontShell Shell;

    public virtual string Title => "";
    /// <summary>Breadcrumb for «PAX PIXELIA › НОВАЯ ИГРА › НАРОД».</summary>
    public virtual string Crumb => Title;
    public virtual string Subtitle => "";
    /// <summary>Panel width; the shell clamps it to the window width − 48.</summary>
    public virtual float PanelWidth => 540;
    /// <summary>Focused after the appear animation.</summary>
    public virtual Control DefaultFocus => null;
    /// <summary>The hint bar at the bottom of the screen (key names: see KeyHint).</summary>
    public virtual (string key, string text)[] Hints => DefaultHints;

    protected FrontScreen() => AddThemeConstantOverride("separation", 12);

    public abstract void Build();

    public virtual void OnShown() => DefaultFocus?.GrabFocus();

    /// <summary>Back button and Esc. Return false when the screen handled Esc itself (e.g. closed its own popup).</summary>
    public virtual bool GoBack() { Shell.Pop(); return true; }

    /// <summary>Open a sub-section (tab) by name — used by <c>--front=settings:audio</c>, <c>nation:flag</c>.</summary>
    public virtual void SelectSection(string section) { }

    // ---- helpers shared by the screens ----

    protected Button AddButton(string text, Action pressed, string variation = "")
    {
        var b = PixelKit.Button(text, variation);
        b.Pressed += pressed;
        AddChild(b);
        return b;
    }

    protected Button AddBackButton(string text = "Назад") => AddButton(text, () => GoBack(), "GhostButton");

    protected void AddGap(float height) => AddChild(new Control { CustomMinimumSize = new Vector2(0, height), MouseFilter = MouseFilterEnum.Ignore });

    /// <summary>A framed card (Surface fill) with a vertical content box.</summary>
    protected VBoxContainer Card(Color? fill = null, Color? border = null)
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", PixelKit.Padded(PixelKit.Framed(fill ?? PixelKit.Surface, border ?? PixelKit.PanelBorder), 20, 16));
        AddChild(panel);
        var content = new VBoxContainer { Alignment = AlignmentMode.Center };
        content.AddThemeConstantOverride("separation", 4);
        panel.AddChild(content);
        return content;
    }

    /// <summary>A footer row: buttons at the left, a flexible gap, buttons at the right.</summary>
    protected HBoxContainer Footer(Control[] left, Control[] right)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        foreach (var c in left) row.AddChild(c);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        foreach (var c in right) row.AddChild(c);
        AddChild(row);
        return row;
    }

    protected static Button MakeButton(string text, Action pressed, string variation = "", float minWidth = 0)
    {
        var b = PixelKit.Button(text, variation);
        b.CustomMinimumSize = new Vector2(minWidth, 46);
        b.Pressed += pressed;
        return b;
    }
}
