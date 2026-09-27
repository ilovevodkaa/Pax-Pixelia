using System.Collections.Generic;
using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>Keyboard/mouse focus helpers shared by the front-end screens (MAIN_MENU.md §2.5).</summary>
public static class Nav
{
    /// <summary>
    /// One highlight: hovering a focusable control focuses it, so hover and keyboard focus never show two
    /// different buttons. Text fields are skipped (hover must not steal typing focus). Safe to call repeatedly.
    /// </summary>
    public static void Unify(Node root)
    {
        foreach (var c in Interactive(root))
        {
            if (c is LineEdit or TextEdit || c.HasMeta("nav_unified")) continue;
            c.SetMeta("nav_unified", true);
            c.MouseEntered += () =>
            {
                if (c.FocusMode != Control.FocusModeEnum.None && c.IsVisibleInTree() && !(c is BaseButton { Disabled: true }))
                    c.GrabFocus();
            };
        }
    }

    /// <summary>
    /// Keyboard focus shows the button's hover look (for BigButton: the 8 px accent bar) instead of the frame ring,
    /// so the title column always has exactly one highlighted item, whichever device moved it.
    /// </summary>
    public static void FocusLooksLikeHover(Button b)
    {
        b.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        b.FocusEntered += () =>
        {
            b.AddThemeStyleboxOverride("normal", b.GetThemeStylebox("hover"));
            b.AddThemeColorOverride("font_color", b.GetThemeColor("font_hover_color"));
            b.AddThemeColorOverride("font_focus_color", b.GetThemeColor("font_hover_color"));
        };
        b.FocusExited += () =>
        {
            b.RemoveThemeStyleboxOverride("normal");
            b.RemoveThemeColorOverride("font_color");
            b.RemoveThemeColorOverride("font_focus_color");
        };
    }

    /// <summary>Focusable, visible controls below root in tree order.</summary>
    public static List<Control> Interactive(Node root)
    {
        var list = new List<Control>();
        Collect(root, list);
        return list;
    }

    static void Collect(Node n, List<Control> list)
    {
        foreach (var child in n.GetChildren())
        {
            if (child is not Control c || !c.Visible) continue;
            if (c.FocusMode == Control.FocusModeEnum.All) list.Add(c);
            Collect(c, list);
        }
    }

    public static Control FirstFocusable(Node root)
    {
        foreach (var c in Interactive(root))
            if (!(c is BaseButton { Disabled: true })) return c;
        return null;
    }
}
