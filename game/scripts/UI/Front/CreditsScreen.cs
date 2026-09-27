using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>
/// «Авторы» (MAIN_MENU.md §3.5): the people from assets/front/credits.json in a card, then an auto-scrolling list
/// of the engine, fonts and sounds. Fonts and sounds are not written by hand: every res://assets/fonts/OFL-*.txt
/// and res://assets/audio/sfx/LICENSE_*.txt adds a line, so a removed asset drops out of the credits too.
/// </summary>
public partial class CreditsScreen : FrontScreen
{
    public const string CreditsPath = "res://assets/front/credits.json";
    const float ScrollSpeed = 30, ScrollStep = 2, EndPause = 2.5f;

    ScrollContainer _scroll;
    Button _back;
    double _acc, _pause = EndPause;   // read the top first, then roll

    /// <summary>The pixel font's «©» is a tiny superscript that reads as «°»; spell it the ASCII way.</summary>
    public const string CopyrightSign = "(c)";

    public override string Title => "Авторы";
    public override float PanelWidth => 640;
    public override Control DefaultFocus => _back;
    public override (string key, string text)[] Hints => new[] { ("Up Down", "листать"), ("Enter", "принять"), ("Esc", "назад") };

    public override void Build()
    {
        var card = Card();
        foreach (var (kicker, lines) in LoadPeople())
        {
            if (lines.Count == 0) continue;   // «Играли первыми» stays hidden until the list has names
            if (card.GetChildCount() > 0) card.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
            var k = PixelKit.Kicker(kicker, PixelKit.Secondary, 11);
            k.HorizontalAlignment = HorizontalAlignment.Center;
            card.AddChild(k);
            bool lead = card.GetChildCount() == 1;
            foreach (var line in lines)
            {
                var l = PixelKit.Label(line, lead ? 33 : 22, lead ? PixelKit.AccentLight : PixelKit.Text, HorizontalAlignment.Center);
                l.AddThemeFontOverride("font", PixelKit.Spaced(lead ? 2 : 1));
                if (lead) PixelKit.TextShadow(l);
                card.AddChild(l);
            }
        }

        _scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 190), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FocusMode = FocusModeEnum.None };
        AddChild(_scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 10);
        _scroll.AddChild(list);
        var engine = Engine.GetVersionInfo();
        list.AddChild(Row("ДВИЖОК", new[] { $"Godot Engine {engine["major"]}.{engine["minor"]} (MIT) · .NET {System.Environment.Version.Major} (MIT)" }));
        list.AddChild(Row("ШРИФТЫ", FontLines()));
        var sounds = SoundLines();
        if (sounds.Count > 0) list.AddChild(Row("ЗВУКИ", sounds));
        var thanks = PixelKit.Label("Спасибо, что играете в Pax Pixelia!", 18, PixelKit.AccentLight, HorizontalAlignment.Center);
        list.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        list.AddChild(thanks);
        list.AddChild(new Control { CustomMinimumSize = new Vector2(0, 120) });

        _back = MakeButton("Назад", () => GoBack(), "GhostButton", 140);
        Footer(new Control[] { MakeButton("Лицензии", () => Shell.Push(new LicensesScreen()), "", 160) }, new Control[] { _back });
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_scroll == null || !IsVisibleInTree() || PxConfirm.IsOpen) return;
        int dir = e.IsActionPressed("ui_up", true) ? -1 : e.IsActionPressed("ui_down", true) ? 1 : 0;
        if (dir == 0) return;
        _scroll.ScrollVertical += dir * 40;
        _pause = EndPause;
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        if (_scroll == null || !IsVisibleInTree()) return;
        if (_scroll.GetGlobalRect().HasPoint(_scroll.GetGlobalMousePosition())) { _pause = 1; return; }   // hover holds the list
        if (_pause > 0) { _pause -= delta; return; }
        _acc += delta * ScrollSpeed;
        if (_acc < ScrollStep) return;
        int step = (int)(_acc / ScrollStep) * (int)ScrollStep;
        _acc -= step;
        var bar = _scroll.GetVScrollBar();
        if (_scroll.ScrollVertical >= bar.MaxValue - bar.Page - 1) { _scroll.ScrollVertical = 0; _pause = EndPause; return; }
        _scroll.ScrollVertical += step;
    }

    static Control Row(string kicker, IEnumerable<string> lines)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        var k = PixelKit.Kicker(kicker, PixelKit.TextDim, 11);
        k.CustomMinimumSize = new Vector2(96, 0);
        k.VerticalAlignment = VerticalAlignment.Top;
        k.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        row.AddChild(k);
        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 6);
        foreach (var line in lines) col.AddChild(PixelKit.Paragraph(line, 16, PixelKit.Text));
        row.AddChild(col);
        return row;
    }

    static List<(string kicker, List<string> lines)> LoadPeople()
    {
        var result = new List<(string, List<string>)>();
        if (!FileAccess.FileExists(CreditsPath)) return result;
        var json = Json.ParseString(FileAccess.GetFileAsString(CreditsPath));
        if (json.VariantType != Variant.Type.Dictionary) return result;
        foreach (var s in json.AsGodotDictionary()["sections"].AsGodotArray())
        {
            var d = s.AsGodotDictionary();
            var lines = new List<string>();
            foreach (var l in d["lines"].AsGodotArray()) lines.Add(l.AsString());
            result.Add((d["kicker"].AsString(), lines));
        }
        return result;
    }

    /// <summary>«Pixelify Sans — OFL 1.1, © 2021 The Pixelify Sans Project Authors» from each OFL-*.txt.</summary>
    static List<string> FontLines()
    {
        var lines = new List<string>();
        foreach (var (file, name) in LicenseFiles("res://assets/fonts", "OFL-"))
        {
            var holder = CopyrightHolder(file);
            var line = $"{name} — OFL 1.1{(holder.Length > 0 ? $", {CopyrightSign} {holder}" : "")}";
            if (name == "Pixelify Sans") line += "; кириллица доработана (PixelifySansMrP)";
            lines.Add(line);
        }
        return lines;
    }

    static List<string> SoundLines()
    {
        var lines = new List<string>();
        foreach (var (file, name) in LicenseFiles("res://assets/audio/sfx", "LICENSE_"))
        {
            var text = FileAccess.GetFileAsString(file);
            lines.Add(text.Contains("CC0") || text.Contains("Creative Commons Zero") ? $"{name} — Interface Sounds (CC0)" : name);
        }
        return lines;
    }

    /// <summary>(path, display name) of «prefix*.txt» files: OFL-PixelifySans.txt → «Pixelify Sans».</summary>
    public static IEnumerable<(string path, string name)> LicenseFiles(string dir, string prefix)
    {
        using var d = DirAccess.Open(dir);
        if (d == null) yield break;
        var files = new List<string>(d.GetFiles());
        files.Sort(StringComparer.Ordinal);
        foreach (var f in files)
        {
            if (!f.StartsWith(prefix) || !f.EndsWith(".txt")) continue;
            var stem = f[prefix.Length..^4];
            yield return ($"{dir}/{f}", Regex.Replace(stem, "(?<=[a-z])(?=[A-Z])", " "));
        }
    }

    static string CopyrightHolder(string path)
    {
        foreach (var raw in FileAccess.GetFileAsString(path).Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("Copyright", StringComparison.OrdinalIgnoreCase)) continue;
            line = Regex.Replace(line, @"^Copyright\s*(\(c\)|©)?\s*", "", RegexOptions.IgnoreCase);
            line = Regex.Replace(line, @"\s*\(https?://[^)]*\)", "");
            return line.Replace(", The ", " The ").Trim().TrimEnd('.');
        }
        return "";
    }
}
