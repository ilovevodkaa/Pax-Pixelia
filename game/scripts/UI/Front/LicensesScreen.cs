using System.Text;
using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>
/// «Лицензии» (MAIN_MENU.md §3.5): the Godot licence, the engine's third-party components, full texts of the OFL
/// font licences and the sound licences, in one scroll. ↑/↓ and the wheel scroll it.
/// </summary>
public partial class LicensesScreen : FrontScreen
{
    ScrollContainer _scroll;
    Button _back;

    public override string Title => "Лицензии";
    public override float PanelWidth => 760;
    public override Control DefaultFocus => _back;
    public override (string key, string text)[] Hints => new[] { ("Up Down", "листать"), ("Esc", "назад") };

    public override void Build()
    {
        float height = Mathf.Clamp((Shell?.UiRoot.Size.Y ?? 720) - 330, 200, 520);
        _scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, height), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(_scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(list);

        Block(list, "GODOT ENGINE", Engine.GetLicenseText());
        Block(list, "КОМПОНЕНТЫ ДВИЖКА", Components());
        foreach (var (path, name) in CreditsScreen.LicenseFiles("res://assets/fonts", "OFL-"))
            Block(list, $"ШРИФТ {name.ToUpperInvariant()} — SIL OFL 1.1", FileAccess.GetFileAsString(path));
        foreach (var (path, name) in CreditsScreen.LicenseFiles("res://assets/audio/sfx", "LICENSE_"))
            Block(list, $"ЗВУКИ {name.ToUpperInvariant()}", FileAccess.GetFileAsString(path));

        _back = MakeButton("Назад", () => GoBack(), "GhostButton", 140);
        Footer(System.Array.Empty<Control>(), new Control[] { _back });
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_scroll == null || !IsVisibleInTree() || PxConfirm.IsOpen) return;
        int dir = e.IsActionPressed("ui_up", true) ? -1 : e.IsActionPressed("ui_down", true) ? 1 : 0;
        if (e is InputEventKey { Pressed: true, Keycode: Key.Pageup }) dir = -8;
        if (e is InputEventKey { Pressed: true, Keycode: Key.Pagedown }) dir = 8;
        if (dir == 0) return;
        _scroll.ScrollVertical += dir * 40;
        GetViewport().SetInputAsHandled();
    }

    static void Block(VBoxContainer list, string kicker, string text)
    {
        if (list.GetChildCount() > 0) list.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        list.AddChild(PixelKit.Kicker(kicker, PixelKit.Secondary, 11));
        var p = PixelKit.Paragraph(text.Trim().Replace("\r", "").Replace("©", CreditsScreen.CopyrightSign), 13, PixelKit.TextDim);
        p.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        list.AddChild(p);
    }

    /// <summary>One line per third-party component: «FreeType — FTL · © 1996-2023 David Turner…».</summary>
    static string Components()
    {
        var sb = new StringBuilder();
        foreach (var d in Engine.GetCopyrightInfo())
        {
            foreach (var part in d["parts"].AsGodotArray())
            {
                var pd = part.AsGodotDictionary();
                var holders = string.Join("; ", pd["copyright"].AsStringArray());
                sb.Append(d["name"].AsString()).Append(" — ").Append(pd["license"].AsString());
                if (holders.Length > 0) sb.Append(" · ").Append(CreditsScreen.CopyrightSign).Append(' ').Append(holders);
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }
}
