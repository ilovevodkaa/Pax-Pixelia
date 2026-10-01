using System;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// CLI hooks for automated screenshots of UI states (args after "--"):
///   --select=capital|own|foreign|foreigncap|claim|unowned|sea|fog|fogsea|stale|&lt;id&gt;   open the province panel
///   --hover=&lt;same kinds&gt; [--mouse=x,y]   province tooltip at a fixed mouse position
///   --tipui=gold|pop|stab|clock|pause|session|nation|screen|trophy|fog|mode|regen|zoom   force a UI tooltip
///   --lead  --toast[=text] [--toastkind=pick|err]  --loading  --build  --targeting  --notes  --scroll=px
///   --session=minutes  --noselect   (--pause / --speed are handled by the sim, --select=x,y / --hover=x,y by the map)
///   --event[=id]  open an event of the deck now (default: the first choice that fits)
///   --clicks=x,y@sec;x,y@sec  --moves=x,y@sec  --keys=space@sec;5@sec;esc@sec   synthetic input through the real GUI pipeline
/// fog / fogsea / stale pick a province that really is unexplored / stale under the current fog.
/// </summary>
public static class UiDebug
{
    /// <summary>Hooks the debug flags to WorldReady; returns the handler, which the Hud removes when it leaves the tree
    /// (the Game autoload outlives every game scene).</summary>
    public static Action Setup(Hud hud)
    {
        if (Cli.Has("session")) hud.Top.SessionMinuteOffset = Cli.Int("session", 0);
        if (Cli.Has("loading")) hud.KeepLoading = true;
        Action onReady = () => hud.GetTree().CreateTimer(.05).Timeout += () => { if (GodotObject.IsInstanceValid(hud)) Apply(hud); };
        Game.I.WorldReady += onReady;
        return onReady;
    }

    /// <summary>--event[=id]: open an event now (the given one, else the first choice that fits the player).</summary>
    static void ForceEvent(Game g, string id)
    {
        if (g.State.Events is not { } ev) return;
        int me = GameState.LocalPlayer, want = ev.Db.EventIndex(id);
        if (want >= 0) { ev.Force(g.State, me, want, g); return; }
        for (int e = 0; e < ev.Db.Events.Length && ev.Pending(me) == null; e++)
            if (ev.Db.Events[e].IsChoice) ev.Force(g.State, me, e, g);
    }

    static void Apply(Hud hud)
    {
        var g = Game.I;
        if (!g.IsReady) return;
        if (Cli.Has("loading")) { hud.Loading.ShowNow(); hud.Loading.SetStatus("Реки и побережья · зерно " + g.World.Seed); }
        if (Cli.Has("notes"))
        {
            var w = g.World; int cap = g.State.NationCapital[GameState.LocalPlayer];
            g.Notify("scale", $"{g.Nations[1].Name} предлагает обмен: камень на вино");
            g.Notify("shovel", $"Геологи нашли медь в холмах у {w.PName[cap]}");
            g.Notify("bulb", "Эврика! Три фермы ускорили «Ирригацию» на 20%");
        }
        if (Cli.Has("event")) ForceEvent(g, Cli.Str("event"));
        var sel = Cli.Str("select");
        if (sel != null) { int p = Find(sel); if (p >= 0) g.Select(p); }
        if (Cli.Has("riot") && g.Selected >= 0)   // a rising, sick province for the unrest screenshots
        {
            var st = g.State; int rp = g.Selected;
            st.Mood[rp] = 10; st.Unrest[rp] = 50; st.Plague[rp] = 30;
        }
        if (Cli.Has("build")) hud.Panel.DebugOpenBuild();
        if (Cli.Has("targeting")) g.BeginScoutTargeting();
        var hov = Cli.Str("hover");
        if (hov != null)
        {
            int p = Find(hov);
            hud.FakeMouse = ParseVec(Cli.Str("mouse", "760,420"));
            if (p >= 0) g.Hover(p);
        }
        var tip = Cli.Str("tipui");
        if (tip != null) hud.ForcedTip = hud.DebugTarget(tip);
        if (Cli.Has("lead")) hud.DebugToggleLead();
        if (Cli.Has("learn-e0"))   // the first era known: the next era's cards (and their eurekas) show
            for (int t = 0; t < Techs.Count; t++) if (Techs.All[t].Era == 0) g.Issue(Cmd.CheatTech(GameState.LocalPlayer, t));
        if (Cli.Has("techs")) hud.DebugToggleTech();
        if (Cli.Has("policy")) hud.DebugTogglePolicy();
        if (Cli.Has("wonders")) hud.DebugToggleWonders();
        if (Cli.Has("diplo")) hud.DebugToggleDiplomacy();
        if (Cli.Has("faith")) hud.DebugToggleFaith();
        if (Cli.Has("toast"))
        {
            var text = Cli.Str("toast");
            if (text == "1") text = "Экран «Технологии» — нарисуем следующим";
            var kind = Cli.Str("toastkind") switch { "pick" => ToastKind.Pick, "err" => ToastKind.Error, _ => ToastKind.Info };
            hud.ToastView.Display(text, 60, kind);
        }
        if (Cli.Has("icons")) IconSheet(hud);
        if (Cli.Has("fontsheet")) FontSheet(hud);
        if (Cli.Has("scroll")) hud.GetTree().CreateTimer(.2).Timeout += () => hud.Panel.DebugScroll(Cli.Int("scroll", 0));
        foreach (var (arg, at) in Schedule("clicks"))
        {
            var pos = ParseVec(arg);
            hud.GetTree().CreateTimer(at).Timeout += () => Click(pos);
        }
        foreach (var (arg, at) in Schedule("moves"))
        {
            var pos = ParseVec(arg);
            hud.GetTree().CreateTimer(at).Timeout += () =>
            {
                hud.FakeMouse = pos;   // injected motion does not move the OS cursor that GetMousePosition reports
                Input.ParseInputEvent(new InputEventMouseMotion { Position = pos, GlobalPosition = pos });
            };
        }
        foreach (var (arg, at) in Schedule("keys"))
        {
            var key = arg switch { "space" => Key.Space, "esc" => Key.Escape, _ when arg.Length == 1 && char.IsDigit(arg[0]) => Key.Key0 + (arg[0] - '0'), _ => Key.None };
            hud.GetTree().CreateTimer(at).Timeout += () => Press(key);
        }
    }

    static System.Collections.Generic.IEnumerable<(string arg, double at)> Schedule(string flag)
    {
        var v = Cli.Str(flag);
        if (v == null) yield break;
        foreach (var item in v.Split(';'))
        {
            var parts = item.Split('@');
            yield return (parts[0], parts.Length > 1 && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : .3);
        }
    }

    static void Click(Vector2 pos)
    {
        Input.ParseInputEvent(new InputEventMouseMotion { Position = pos, GlobalPosition = pos });
        Input.ParseInputEvent(new InputEventMouseButton { Position = pos, GlobalPosition = pos, ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true });
        Input.ParseInputEvent(new InputEventMouseButton { Position = pos, GlobalPosition = pos, ButtonIndex = MouseButton.Left, Pressed = false });
    }

    static void Press(Key k)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = false });
    }

    /// <summary>--icons: contact sheet of every pixel icon at ×1 and ×2, plus the inverted (shadowless) variant.</summary>
    static void IconSheet(Hud hud)
    {
        var grid = new GridContainer { Columns = 6 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 6);
        foreach (var name in PixelIconArt.Art.Keys)
        {
            var inv = Ui.Panel(St.Inverted(), Ui.Icon(name, 1, Pal.OnAc, shadow: false)).MinSize(20, 20);
            grid.AddChild(Ui.HBox(6, Ui.Icon(name, 1, Pal.Ac), Ui.Icon(name, 2, Pal.Ac), inv, Ui.Text(name, "SmallMu").MinSize(110, 0)));
        }
        var card = Ui.Panel(St.Card().Pad(16), grid, Control.MouseFilterEnum.Stop);
        card.Position = new Vector2(60, 70);
        hud.GetChild(0).AddChild(card);
    }

    /// <summary>--fontsheet: the pixel font at every HUD size / weight / hinting mode (to pick crisp combinations).</summary>
    static void FontSheet(Hud hud)
    {
        var col = Ui.VBox(2);
        const string sample = "Отправить разведчиков · Казна 1 278 +13 · март 3200 до н. э. · ВЛИЯНИЕ Святилище";
        foreach (var hint in new[] { TextServer.Hinting.None, TextServer.Hinting.Light, TextServer.Hinting.Normal })
        {
            var file = new FontFile();
            file.LoadDynamicFont(ProjectSettings.GlobalizePath(PixelKit.FontPath));
            file.Antialiasing = TextServer.FontAntialiasing.None;
            file.Hinting = hint;
            file.SubpixelPositioning = TextServer.SubpixelPositioning.Disabled;
            foreach (int w in new[] { 400, 600 })
            foreach (int size in new[] { 11, 13, 14, 15, 16, 18 })
            {
                var v = new FontVariation { BaseFont = file };
                v.VariationOpentype = new Godot.Collections.Dictionary { { "wght", w } };
                var l = Ui.Text($"{hint} {w} {size}: {sample}");
                l.AddThemeFontOverride("font", v);
                l.AddThemeFontSizeOverride("font_size", size);
                col.AddChild(l);
            }
        }
        var card = Ui.Panel(St.Card().Pad(12), col, Control.MouseFilterEnum.Stop);
        card.Position = new Vector2(10, 60);
        hud.GetChild(0).AddChild(card);
    }

    static Vector2 ParseVec(string s)
    {
        var parts = s.Split(',');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return parts.Length == 2 && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out var x) && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out var y) ? new Vector2(x, y) : new Vector2(760, 420);
    }

    /// <summary>Picks a province that shows the requested panel variant (and fakes fog state where needed).</summary>
    static int Find(string kind)
    {
        var w = Game.I.World; var s = Game.I.State;
        const int me = GameState.LocalPlayer;
        int cap = s.NationCapital[me];
        if (int.TryParse(kind, out int id)) return id >= 0 && id < w.P ? id : -1;
        bool NearMe(int p) { foreach (var q in w.Adj[p]) if (s.Owner[q] == me) return true; return false; }
        int First(Func<int, bool> pred) { for (int p = 0; p < w.P; p++) if (pred(p)) return p; return -1; }
        bool Land(int p) => w.PLand[p] == 1;
        int p;
        switch (kind)
        {
            case "capital": return cap;
            case "own": return First(q => s.Owner[q] == me && q != cap);
            case "foreigncap": return s.NationCapital[1];
            case "foreign": p = First(q => s.Owner[q] == 1 && s.CapitalOf[q] < 0); return p >= 0 ? p : s.NationCapital[1];
            case "claim": return First(q => Land(q) && s.Owner[q] < 0 && NearMe(q));
            case "unowned": return First(q => Land(q) && s.Owner[q] < 0 && !NearMe(q));
            case "sea": return First(q => !Land(q) && w.PSize[q] > 300);
            case "fog": return First(q => Land(q) && s.Fog[q] == 0 && w.PSize[q] > 200);
            case "fogsea": return First(q => !Land(q) && s.Fog[q] == 0);
            case "stale": p = First(q => s.Owner[q] > 0 && s.Fog[q] == 1); return p >= 0 ? p : First(q => Land(q) && s.Fog[q] == 1);
            default: return -1;   // coordinates etc. are resolved by the map's own debug hooks
        }
    }
}
