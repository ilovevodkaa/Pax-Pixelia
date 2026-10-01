using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// Leaderboard card dropping from the trophy button with a stepped pixel notch. Only nations the player has met are
/// listed (GDD 9.2); the rest are summarised in one muted row.
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
        var title = Ui.Text("Таблица лидеров", "LeadTitle");
        title.Uppercase = true;
        var head = Ui.Panel(St.Header(36).Pad(14, 11, 14, 10), Ui.HBox(8, Ui.Icon("trophy", 1, Pal.Ac), title, Ui.Expand(), _met));
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
        _rows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(Ui.Margin(_rows, 0, 4, 0, 6).Grow());
        _card = Ui.Panel(St.Card().Pad(2), Ui.VBox(0, head, _scroll), MouseFilterEnum.Stop);
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
        Position = new Vector2(Mathf.Round(lx), TopBar.Height + 12);
        _notch.Position = new Vector2(Mathf.Round(trophy.Position.X + trophy.Size.X / 2 - lx) - Notch.W / 2, -Notch.H + 2);
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
            var r = Ui.Margin(Ui.HBox(8, Ui.Icon("help-hexagon", 1, Pal.Mu), Ui.Text(text, "SmallMu")), 14, 8, 14, 8);
            r.Tip("Неизвестные державы", "Отправьте разведчиков: державы появятся в таблице после встречи.");
            r.MouseFilter = MouseFilterEnum.Pass;
            _rows.AddChild(r);
        }
    }

    /// <summary>A row: rank · colour · name · score, a 2px share bar underneath; the player's row is lifted with the accent bar.</summary>
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
            var box = new Box().Border(Pal.Ln, 0, 0, 0, last ? 0 : 2).Pad(14, 6, 14, 9);
            if (_me) box.Fill(Pal.Surface).AccentLeft(Pal.Ac, 4);
            AddThemeStyleboxOverride("panel", box);
            var rk = Ui.Text(rank.ToString(), "SmallMu").MinSize(22, 0);
            rk.HorizontalAlignment = HorizontalAlignment.Right;
            var n = Game.I.Nations[nation];
            var name = Ui.HBox(0, Ui.Text(n.Name, _me ? "Semi" : null), _me ? Ui.Text(" · вы", "Mu") : null);
            AddChild(Ui.HBox(8, rk, new Swatch(Pal.Nation(nation), 12), name.Grow(), Ui.Text(score.ToString(), "Semi")));
            this.Tip(t => t.Title(n.Name).Line($"{n.Gov} · {n.CultureAdj} культура").Kv("Очки", score.ToString()));
        }

        public override void _Draw()
        {
            float x0 = 58, x1 = Size.X - 14, y = Size.Y - 6;
            DrawRect(new Rect2(x0, y, x1 - x0, 2), Pal.Ln);
            DrawRect(new Rect2(x0, y, Mathf.Round((x1 - x0) * _share), 2), _me ? Pal.Hi : Pal.Ln3);
        }
    }

    /// <summary>Stepped pixel notch pointing at the trophy: 2px stairs of frame colour around the card fill.</summary>
    internal sealed partial class Notch : Control
    {
        public const int W = 20, H = 12;

        public Notch()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            Size = new Vector2(W, H);
        }

        public override void _Draw()
        {
            // rows of 2px from the tip down; each row is frame | fill | frame, the last row merges into the card's top frame
            for (int i = 0; i < H / 2; i++)
            {
                float half = 2 + i * 2, y = i * 2;
                DrawRect(new Rect2(W / 2f - half, y, half * 2, 2), Pal.Ln2);
                if (half > 2) DrawRect(new Rect2(W / 2f - half + 2, y, half * 2 - 4, 2), Pal.Card);
            }
        }
    }
}
