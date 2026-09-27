using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// #lead — leaderboard dropping from the trophy button with a notch. Only nations the player has met are listed
/// (GDD 9.2); the rest are summarised in one muted row.
/// </summary>
public partial class Leaderboard : Control
{
    const int CardWidth = 300;
    readonly PanelContainer _card;
    readonly VBoxContainer _rows = Ui.VBox(0);
    readonly ScrollContainer _scroll;
    readonly Label _met = Ui.Text("", "Aside");
    readonly Notch _notch = new();

    public Leaderboard()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        var head = Ui.Panel(St.Header().Pad(14, 11, 14, 9), Ui.HBox(8, Ui.Text("Таблица лидеров", "LeadTitle"), Ui.Expand(), _met));
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(Ui.Margin(_rows, 0, 4, 0, 6).Grow());
        _card = Ui.Panel(St.Card().Pad(1), Ui.VBox(0, head, _scroll), MouseFilterEnum.Stop);
        _card.CustomMinimumSize = new Vector2(CardWidth, 0);
        AddChild(_card);
        AddChild(_notch);
    }

    public bool Toggle()
    {
        Visible = !Visible;
        if (Visible) Refresh();
        return Visible;
    }

    /// <summary>Drop under the trophy: left = min(screen − 310, trophy.left − 140), notch at the trophy centre.</summary>
    public void Place(Rect2 trophy, Vector2 screen)
    {
        float lx = Mathf.Max(8, Mathf.Min(screen.X - CardWidth - 10, trophy.Position.X - 140));
        Position = new Vector2(Mathf.Round(lx), TopBar.Height + 9);
        _notch.Position = new Vector2(Mathf.Round(trophy.Position.X + trophy.Size.X / 2 - lx) - 9, -8);
        float maxRows = screen.Y - 140 - 44;
        float content = _rows.GetParent<Control>().GetCombinedMinimumSize().Y;
        _scroll.CustomMinimumSize = new Vector2(0, Mathf.Min(content, maxRows));
        _card.ResetSize();
    }

    public void Refresh()
    {
        if (!Visible || !Game.I.IsReady) return;
        Ui.Clear(_rows);
        var rows = Game.I.Leaderboard();
        int top = rows.Count > 0 ? System.Math.Max(1, rows[0].score) : 1;
        _met.Text = $"встречено {rows.Count}";
        for (int k = 0; k < rows.Count; k++)
            _rows.AddChild(new Row(k + 1, rows[k].nation, rows[k].score, (float)rows[k].score / top, k == rows.Count - 1 && Game.I.UnmetNations == 0));
        int unk = Game.I.UnmetNations;
        if (unk > 0)
        {
            string text = $"Ещё {unk} {Fmt.Plural(unk, "держава не встречена", "державы не встречены", "держав не встречены")}";
            var r = Ui.Margin(Ui.HBox(7, Ui.Icon("help-hexagon", 15, Pal.Mu), Ui.Text(text, "SmallMu")), 14, 6, 14, 7);
            r.Tip("Неизвестные державы", "Отправьте разведчиков: державы появятся в таблице после встречи.");
            r.MouseFilter = MouseFilterEnum.Pass;
            _rows.AddChild(r);
        }
    }

    /// <summary>.lr — rank · colour · name · score, thin share bar underneath; the player's row is highlighted.</summary>
    sealed partial class Row : PanelContainer
    {
        readonly float _share;
        readonly bool _me;

        public Row() { }
        public Row(int rank, int nation, int score, float share, bool last)
        {
            _share = share;
            _me = nation == GameState.LocalPlayer;
            MouseFilter = MouseFilterEnum.Pass;
            var box = new Box().Border(Pal.Ln, 0, 0, 0, last ? 0 : 1).Pad(14, 5, 14, 7);
            if (_me) box.Fill(Pal.Hex(0xeceef1));
            AddThemeStyleboxOverride("panel", box);
            var rk = Ui.Text(rank.ToString(), "SmallMu").MinSize(22, 0);
            rk.HorizontalAlignment = HorizontalAlignment.Right;
            var n = Data.Nations[nation];
            var name = Ui.HBox(0, Ui.Text(n.Name, _me ? "Semi" : null), _me ? Ui.Text(" · вы", "Mu") : null);
            AddChild(Ui.HBox(8, rk, new Swatch(Pal.Nation(nation), 12), name.Grow(), Ui.Text(score.ToString(), "Semi")));
            this.Tip(t => t.Title(n.Name).Line($"{n.Gov} · {n.CultureAdj} культура").Kv("Очки", score.ToString()));
        }

        public override void _Draw()
        {
            float x0 = 56, x1 = Size.X - 14, y = Size.Y - 4;
            DrawRect(new Rect2(x0, y, x1 - x0, 2), Pal.IbHover);
            DrawRect(new Rect2(x0, y, Mathf.Round((x1 - x0) * _share), 2), _me ? Pal.Ac : Pal.Hex(0xa9b0b7));
            if (_me) DrawRect(new Rect2(0, 0, 3, Size.Y), Pal.G2);
        }
    }

    /// <summary>The rotated-square notch pointing at the trophy (#lead::after).</summary>
    sealed partial class Notch : Control
    {
        readonly Vector2[] _tri = new Vector2[3];
        readonly Color[] _fill = { Pal.Hd1, Pal.Hd1, Pal.Hd1 };
        readonly Vector2[] _edge = new Vector2[3];

        public Notch()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            Size = new Vector2(18, 10);
        }

        public override void _Draw()
        {
            _tri[0] = new Vector2(1, 9); _tri[1] = new Vector2(9, 1); _tri[2] = new Vector2(17, 9);
            DrawPolygon(_tri, _fill);
            _edge[0] = new Vector2(1.5f, 8.5f); _edge[1] = new Vector2(9, 1); _edge[2] = new Vector2(16.5f, 8.5f);
            DrawPolyline(_edge, Pal.Ln2, 1, true);
            DrawRect(new Rect2(2, 8, 14, 2), Pal.Hd1);
        }
    }
}
