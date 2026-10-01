using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI;

/// <summary>
/// Hover tooltip content for a map province (tipFor() in the mockup), fog- and scout-targeting-aware.
/// Built once and updated in place — the hovered province can change every frame while the mouse sweeps the map.
/// </summary>
public partial class ProvinceTipView : VBoxContainer
{
    readonly Label _title = Ui.Text("", "TipTitle");
    readonly Label _sub = Ui.Text("", "Mu");
    readonly Control _row;
    readonly Swatch _swatch = new(Colors.Gray);
    readonly Label _owner = Ui.Text("");
    readonly Label _pop = Ui.Text("", "Semi");
    readonly Label _unit = Ui.Text("чел.", "Mu");
    readonly Control _stale;
    readonly Control _pick;
    readonly TextureRect _pickIcon = Ui.Icon("map-pin", 1, Pal.Ac);
    readonly Label _pickText = Ui.Text("", "Strong");

    public ProvinceTipView()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 0);
        AddChild(_title);
        AddChild(Ui.Gap(0, 4));
        AddChild(_sub);
        _row = Ui.VBox(0, Ui.Gap(0, 7), new HairLine(Pal.Ln2), Ui.Gap(0, 7),
            Ui.HBox(16, Ui.HBox(6, _swatch, _owner).Grow(), Ui.HBox(4, _pop, _unit)));
        AddChild(_row);
        var chip = Ui.Panel(St.Stale().Pad(5, 0, 7, 0), Ui.HBox(5, Ui.Icon("history", 1, Pal.Mu), Ui.Cap("Сведения устарели")));
        chip.CustomMinimumSize = new Vector2(0, 22);
        chip.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _stale = Ui.VBox(0, Ui.Gap(0, 8), chip);
        AddChild(_stale);
        _pick = Ui.VBox(0, Ui.Gap(0, 7), new HairLine(Pal.Ln2), Ui.Gap(0, 7), Ui.HBox(6, _pickIcon, _pickText));
        AddChild(_pick);
    }

    /// <returns>false when there is nothing to show for this province.</returns>
    public bool Set(int p)
    {
        var g = Game.I; var w = g.World; var s = g.State;
        if (w == null || s == null || p < 0 || p >= w.P) return false;
        int f = !s.FogEnabled || s.Fog == null ? 2 : s.Fog[p];
        bool land = w.PLand[p] == 1, picking = g.IsTargeting;

        _stale.Visible = f == 1;
        _pick.Visible = picking;
        if (picking)
        {
            // under the clouds even land and sea are unknown: every unexplored province looks like a valid pick
            bool ok = land || f == 0;
            _pickIcon.Texture = Icons.Get(ok ? "map-pin" : "ban");
            _pickIcon.SelfModulate = ok ? Pal.Ac : Pal.Bad;
            _pickText.Text = ok ? "Отправить разведчиков сюда" : "Разведчики ходят только по суше";
            _pickText.Colored(ok ? Pal.Hi : Pal.Bad);
        }

        if (f == 0)
        {
            var rumor = g.RumorAt(p);
            _title.Text = rumor != null ? "Слухи" : "Неизведанные земли";
            _sub.Text = rumor is { } r ? g.RumorText(r) + (picking ? "" : ". Проверьте: отправьте разведчиков")
                : picking ? "Что там — узнают разведчики" : "Отправьте туда разведчиков из столицы";
            _row.Visible = false;
            return true;
        }
        _title.Text = w.PName[p];
        if (!land)
        {
            _sub.Text = "Морская зона";
            _row.Visible = false;
            return true;
        }
        _sub.Text = $"{w.TerrainName(p)} · климат {Data.Climate(w.PBiome[p])}";
        int o = s.VisibleOwner(p);
        _row.Visible = true;
        _swatch.Color = o >= 0 ? Pal.Nation(o) : Pal.Unowned;
        _owner.Text = o >= 0 ? g.Nations[o].Name : "Ничья земля";
        bool stale = f == 1;
        _pop.Text = stale ? "население неизвестно" : "≈" + Fmt.Int(s.Pop[p]);
        _pop.ThemeTypeVariation = stale ? "Mu" : "Semi";
        _unit.Visible = !stale;
        return true;
    }
}
