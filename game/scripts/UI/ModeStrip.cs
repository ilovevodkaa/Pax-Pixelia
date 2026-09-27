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
