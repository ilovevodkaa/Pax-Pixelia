using System;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI;

/// <summary>
/// #modes — map-mode strip docked 6px above the minimap: terrain / political / religion / trade / fertility,
/// a hairline, and the fog-of-war eye (graphite = observer mode, fog off).
/// </summary>
public partial class ModeStrip : PanelContainer
{
    static readonly (MapMode mode, string icon, string title, string hint)[] Modes =
    {
        (MapMode.Terrain, "mountain", "Рельеф", "Высоты, биомы и реки"),
        (MapMode.Political, "flag", "Политическая карта", "Державы и их границы"),
        (MapMode.Religion, "sun", "Религии", "Вера провинций"),
        (MapMode.Trade, "route", "Торговые пути", "Караваны и морские маршруты"),
        (MapMode.Fertility, "plant", "Плодородие", "Сколько еды даёт земля"),
    };

    readonly Button[] _buttons = new Button[Modes.Length];
    readonly Button _fog;
    readonly HBoxContainer _row;
    public event Action FogToggled;

    public ModeStrip()
    {
        MouseFilter = MouseFilterEnum.Stop;
        AddThemeStyleboxOverride("panel", St.Card().Pad(5, 4, 5, 4));
        _row = Ui.HBox(10);
        for (int i = 0; i < Modes.Length; i++)
        {
            var (mode, icon, title, hint) = Modes[i];
            var b = Ui.IconButton(icon, "Ib", 38, 34, 19, () => Game.I.SetMode(mode));
            b.MouseFilter = MouseFilterEnum.Stop;
            b.Tip(title, null, hint);
            _buttons[i] = b;
            _row.AddChild(b);
        }
        _row.AddChild(Ui.Rule(Pal.Ln2, 1, 20));
        _fog = Ui.IconButton("eye-off", "Ib", 38, 34, 19, () => FogToggled?.Invoke());
        _fog.MouseFilter = MouseFilterEnum.Stop;
        _fog.Tip(t =>
        {
            bool on = !Game.I.IsReady || Game.I.State.FogEnabled;
            if (on) t.Title("Туман войны").Line("Видны только разведанные земли").Mu("Нажмите — режим наблюдателя");
            else t.Title("Режим наблюдателя").Line("Туман войны выключен, видна вся карта").Mu("Нажмите, чтобы вернуть туман");
        });
        _row.AddChild(_fog);
        AddChild(_row);
    }

    internal Control DebugTarget(string name) => name switch { "fog" => _fog, "mode" => _buttons[1], _ => null };

    public void Refresh()
    {
        var m = Game.I.Mode;
        for (int i = 0; i < Modes.Length; i++) _buttons[i].ThemeTypeVariation = Modes[i].mode == m ? "IbOn" : "Ib";
        bool fogOn = !Game.I.IsReady || Game.I.State.FogEnabled;
        _fog.ThemeTypeVariation = fogOn ? "Ib" : "IbOn";
        _fog.Icon = Icons.Get(fogOn ? "eye-off" : "eye", 19);
    }

    /// <summary>Matches the minimap card width and spreads the buttons (CSS justify-content: space-between).</summary>
    public void SetWidth(int cardWidth)
    {
        CustomMinimumSize = new Vector2(cardWidth, 0);
        int content = 6 * 38 + 1, inner = cardWidth - 2 - 10;
        _row.AddThemeConstantOverride("separation", Math.Max(2, (inner - content) / 6));
    }
}

/// <summary>
/// Key of the tinted map modes, a small card docked 6px above the mode strip: the religions present on the known map,
/// the 1–5 fertility ramp, or the trade-route line. Hidden in the terrain and political modes, which read without one.
/// </summary>
public partial class ModeLegend : PanelContainer
{
    readonly VBoxContainer _box = Ui.VBox(4);
    MapMode _shown = (MapMode)(-1);
    int _religions = -1;

    public ModeLegend()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;   // a card like the others: the wheel over it does not zoom the map
        SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        AddThemeStyleboxOverride("panel", St.Card().Pad(10, 7, 12, 9));
        AddChild(_box);
    }

    /// <summary>On mode, fog and ownership changes (cheap: rebuilt only when what it shows changed).</summary>
    public void Refresh()
    {
        var g = Game.I;
        var mode = g.Mode;
        Visible = g.IsReady && mode is MapMode.Religion or MapMode.Trade or MapMode.Fertility;
        if (!Visible) { _shown = mode; return; }
        int religions = mode == MapMode.Religion ? KnownReligions() : 0;
        if (mode == _shown && religions == _religions) return;
        _shown = mode; _religions = religions;
        Ui.Clear(_box);
        switch (mode)
        {
            case MapMode.Religion:
                _box.AddChild(Ui.Cap("Религии"));
                for (int r = 0; r < Data.Religions.Length; r++)
                    if ((religions & (1 << r)) != 0) _box.AddChild(Ui.HBox(7, new Swatch(Pal.Religion(r)), Ui.Text(Data.Religions[r].Name).Sized(12)));
                if (religions == 0) _box.AddChild(Ui.Text("На разведанных землях — никакой", "SmallMu"));
                break;
            case MapMode.Fertility:
                _box.AddChild(Ui.Cap("Плодородие"));
                var ramp = Ui.HBox(2);
                for (int k = 1; k <= 5; k++)
                {
                    var n = Ui.Text(k.ToString(), "SmallMu");
                    n.HorizontalAlignment = HorizontalAlignment.Center;
                    ramp.AddChild(Ui.VBox(2, new Swatch(MiniMapView.FertilityColor((k - 1) / 4f)).MinSize(22, 10), n));
                }
                _box.AddChild(ramp);
                break;
            case MapMode.Trade:
                _box.AddChild(Ui.HBox(8, new RouteSwatch(), Ui.Text("Торговый путь").Sized(12)));
                break;
        }
    }

    /// <summary>Bit r set: religion r is the faith of some land the player's map shows.</summary>
    static int KnownReligions()
    {
        var w = Game.I.World; var s = Game.I.State;
        int mask = 0;
        for (int p = 0; p < w.P; p++)
            if (w.PLand[p] == 1 && s.Religion[p] >= 0 && (!s.FogEnabled || s.Fog[p] > 0)) mask |= 1 << s.Religion[p];
        return mask;
    }

    /// <summary>The trade mode's golden dashes on a dark casing, as on the map.</summary>
    sealed partial class RouteSwatch : Control
    {
        static readonly Color Casing = new(22 / 255f, 26 / 255f, 31 / 255f, .6f), Dash = new(.941f, .784f, .376f);
        public RouteSwatch() { CustomMinimumSize = new Vector2(26, 10); MouseFilter = MouseFilterEnum.Ignore; SizeFlagsVertical = SizeFlags.ShrinkCenter; }
        public override void _Draw()
        {
            float y = Size.Y / 2;
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), Casing, 5);
            for (float x = 1; x < Size.X - 1; x += 7) DrawLine(new Vector2(x, y), new Vector2(Math.Min(x + 4, Size.X - 1), y), Dash, 2);
        }
    }
}
