using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// «Чудеса света»: a card dropping from the diamond button. The glory of the nation, the wonder under construction (what
/// is paid, who else builds it, «Вложить» and «Бросить»), the wonders it may lay now, those already standing and whose
/// they are, and how many the next era opens.
/// </summary>
public partial class WondersCard : Control
{
    const int CardWidth = 380;
    readonly PanelContainer _card;
    readonly VBoxContainer _body = Ui.VBox(0);
    readonly ScrollContainer _scroll;
    readonly Label _glory = Ui.Text("", "Aside");
    readonly Leaderboard.Notch _notch = new();

    public WondersCard()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        var title = Ui.Text("Чудеса света", "LeadTitle");
        title.Uppercase = true;
        var head = Ui.Panel(St.Header(36).Pad(14, 11, 14, 10), Ui.HBox(8, Ui.Icon("diamond", 1, Pal.Ac), title, Ui.Expand(), _glory));
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, MouseFilter = MouseFilterEnum.Pass };
        _body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(Ui.Margin(_body, 14, 8, 14, 12).Grow());
        _card = Ui.Panel(St.Card().Pad(2), Ui.VBox(0, head, _scroll), MouseFilterEnum.Stop);
        _card.CustomMinimumSize = new Vector2(CardWidth, 0);
        AddChild(_card);
        AddChild(_notch);
    }

    public bool Toggle()
    {
        Visible = !Visible;
        if (Visible) Refresh(true);
        return Visible;
    }

    public void Place(Rect2 button, Vector2 screen)
    {
        float lx = Mathf.Max(8, Mathf.Min(screen.X - CardWidth - 10, button.Position.X - 200));
        Position = new Vector2(Mathf.Round(lx), TopBar.Height + 12);
        _notch.Position = new Vector2(Mathf.Round(button.Position.X + button.Size.X / 2 - lx) - Leaderboard.Notch.W / 2, -Leaderboard.Notch.H + 2);
        // the laid-out bottom of the last row (wrapped labels report a tall minimum before they know their width);
        // right after a rebuild the new rows have no size yet: keep the old height for that frame
        if (_body.GetChildCount() > 0 && _body.GetChild(_body.GetChildCount() - 1) is Control last && last.Size.Y > 0)
            _scroll.CustomMinimumSize = new Vector2(0, Mathf.Min(last.Position.Y + last.Size.Y + 8 + 12, screen.Y - 140 - 44));
        _card.ResetSize();
    }

    long _shown = -1;

    /// <summary>What the card shows, folded into one number: it is rebuilt only when this changes (every 1% of progress,
    /// a new owner, a rival, the era, the glory, whether a wonder may be laid).</summary>
    static long Signature()
    {
        var g = Game.I;
        var s = g.State;
        var nat = s.Nat[g.Viewer];
        long h = 17;
        void Mix(long v) => h = h * 1_000_003 + v;
        Mix(nat.Era); Mix(nat.Wonder); Mix(nat.Glory); Mix(g.WonderProgressPermille / 10);
        for (int w = 0; w < Wonders.Count; w++)
        {
            Mix(s.WonderOwner[w]); Mix(s.WonderFlag[w]); Mix(g.WonderRivals(w).count);
            Mix(g.WonderStartProblem(w) == null ? 1 : 0);
        }
        return h;
    }

    public void Refresh(bool force = false)
    {
        if (!Visible || !Game.I.IsReady) return;
        long sig = Signature();
        if (!force && sig == _shown) return;
        _shown = sig;
        var g = Game.I;
        var s = g.State;
        var nat = s.Nat[g.Viewer];
        Ui.Clear(_body);
        _glory.Text = $"слава {g.Glory}";

        int wd = g.BuildingWonder;
        _body.AddChild(Caption("Строится"));
        if (wd >= 0) _body.AddChild(UnderWay(wd));
        else _body.AddChild(Kit.Para("Столица свободна: заложите чудо, пока его не забрали соседи", true, UiFonts.Small));

        _body.AddChild(Ui.Gap(0, 10));
        _body.AddChild(Caption("Можно заложить"));
        int open = 0;
        for (int w = 0; w < Wonders.Count; w++)
        {
            if (!Wonders.Available(s, g.Viewer, w) || w == wd) continue;
            _body.AddChild(OpenRow(w));
            open++;
        }
        if (open == 0) _body.AddChild(Kit.Para("Все чудеса этой эпохи уже стоят или строятся вами", true, UiFonts.Small));

        int next = 0;
        for (int w = 0; w < Wonders.Count; w++) if (Wonders.All[w].Era == nat.Era + 1) next++;
        if (next > 0 && nat.Era < Eras.Last)
            _body.AddChild(Kit.Para($"В эпоху «{Eras.Name(nat.Era + 1)}» откроются ещё {next} {Fmt.Plural(next, "чудо", "чуда", "чудес")}", true, UiFonts.Small));

        _body.AddChild(Ui.Gap(0, 10));
        _body.AddChild(Caption("Возведены"));
        int built = 0;
        for (int w = 0; w < Wonders.Count; w++)
        {
            string owner = g.WonderOwnerText(w);
            if (owner == null) continue;
            built++;
            var d = Wonders.All[w];
            bool mine = owner == "вы";
            string note = Wonders.Fallen(s, w) ? " · фасады упали" : (s.WonderFlag[w] & Wonders.Cracked) != 0 ? " · треснул" : "";
            var row = Ui.Margin(Ui.HBox(8, Ui.Icon("diamond", 1, mine ? Pal.Ac : Pal.Mu).Center(), Ui.Text(d.Name, mine ? "Semi" : null).Grow(),
                Ui.Text(owner + note, "SmallMu")), 0, 2, 0, 2);
            row.MouseFilter = MouseFilterEnum.Pass;
            row.Tip(t => t.Title(d.Name).Line(d.Effect).Kv("Владелец", owner).Mu(d.Lore));
            _body.AddChild(row);
        }
        if (built == 0) _body.AddChild(Kit.Para("Ни одного чуда в мире ещё нет", true, UiFonts.Small));
        _body.AddChild(Ui.Gap(0, 6));
        _body.AddChild(Kit.Para($"Слава идёт в очки таблицы лидеров ×{Wonders.GloryScore}. Её дают чудеса, мировые первенства и летопись", true, UiFonts.Small));
    }

    static Control Caption(string text) => Ui.Margin(Ui.Cap(text), 0, 4, 0, 4);

    static string Cost(int w) => $"{Fmt.Int(Wonders.GoldCost(w))} золота · {Fmt.Int(Wonders.MatCost(w))} материалов";

    Control UnderWay(int w)
    {
        var g = Game.I;
        var d = Wonders.All[w];
        var (gold, mats) = g.WonderPaid;
        var box = Ui.VBox(4);
        box.AddChild(Ui.HBox(8, Ui.Icon("diamond", 1, Pal.Ac).Center(), Ui.Text(d.Name, "Semi").Grow(), Ui.Text($"{g.WonderProgressPermille / 10}%", "Semi").Colored(Pal.Hi)));
        box.AddChild(Ui.Text(d.Effect, "SmallMu", true));
        box.AddChild(Line("Золото", $"{Fmt.Int(gold)} / {Fmt.Int(Wonders.GoldCost(w))}"));
        box.AddChild(new Bar((float)gold / System.Math.Max(1, Wonders.GoldCost(w))));
        box.AddChild(Line("Материалы", $"{Fmt.Int(mats)} / {Fmt.Int(Wonders.MatCost(w))}"));
        box.AddChild(new Bar((float)mats / System.Math.Max(1, Wonders.MatCost(w))));
        var (count, known) = g.WonderRivals(w);
        if (count > 0)
            box.AddChild(Ui.Text(known.Count > 0 ? $"Строят и соперники: {string.Join(", ", known)}" + (count > known.Count ? $" и ещё {count - known.Count}" : "")
                                                 : $"Строят и соперники: {count} {Fmt.Plural(count, "далёкий народ", "далёких народа", "далёких народов")}", "SmallMu", true).Colored(Pal.Warn));
        box.AddChild(Ui.Text($"Сюда сами идут {Wonders.AutoPct}% дохода и материалов", "SmallMu", true));
        var invest = Ui.Button("Вложить казну", "coins", "Pri", () => Game.I.InvestWonder(), 1, 28);
        invest.Tip("Вложить казну", null, "Всё золото и все материалы, сколько нужно стройке");
        var stop = Ui.Button("Бросить", null, "Ghost", () => Game.I.StopWonder(), 1, 28);
        stop.Tip("Бросить стройку", null, $"Вернётся {Wonders.RefundPct}% вложенного");
        box.AddChild(Ui.HBox(8, invest.Grow(), stop));
        var panel = Ui.Panel(new Box().Fill(Pal.Surface).Border(Pal.Ln).AccentLeft(Pal.Ac, 4).Pad(10, 8, 10, 10), box, MouseFilterEnum.Pass);
        panel.Tip(t => t.Title(d.Name).Line(d.Effect).Kv("Слава", $"+{d.Glory}").Mu(d.Lore));
        return panel;
    }

    Control OpenRow(int w)
    {
        var g = Game.I;
        var d = Wonders.All[w];
        var text = Ui.VBox(1, Ui.Text(d.Name), Ui.Text(d.Effect, "SmallMu", true), Ui.Text($"{Cost(w)} · слава +{d.Glory}", "SmallMu", true));
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var btn = Ui.Button("Заложить", null, "Pri", () => Game.I.StartWonder(w), 1, 28);
        btn.CustomMinimumSize = new Vector2(96, 28);
        string why = g.WonderStartProblem(w);
        Ui.Enable(btn, why == null);
        var (count, known) = g.WonderRivals(w);
        var row = Ui.Panel(new Box().Border(Pal.Ln, 0, 0, 0, 2).Pad(0, 7, 0, 8), Ui.HBox(10, text, btn.Center()), MouseFilterEnum.Pass);
        row.Tip(t =>
        {
            t.Title(d.Name).Line(d.Effect).Kv("Цена", Cost(w)).Kv("Слава", $"+{d.Glory}");
            if (count > 0) t.Kv("Уже строят", known.Count > 0 ? string.Join(", ", known) + (count > known.Count ? $" и ещё {count - known.Count}" : "") : count.ToString(), Pal.Warn);
            if (why != null) t.Line(why);
            t.Mu(d.Lore);
        });
        return row;
    }

    static Control Line(string name, string value) => Ui.HBox(8, Ui.Text(name, "SmallMu").Grow(), Ui.Text(value, "SmallMu"));

    sealed partial class Bar : Control
    {
        readonly float _share;
        public Bar() { }
        public Bar(float share) { _share = Mathf.Clamp(share, 0, 1); CustomMinimumSize = new Vector2(0, 6); MouseFilter = MouseFilterEnum.Ignore; }

        public override void _Draw()
        {
            DrawRect(new Rect2(0, 1, Size.X, 4), Pal.Ln);
            DrawRect(new Rect2(0, 1, Mathf.Round(Size.X * _share), 4), Pal.Hi);
        }
    }
}
