using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.UI;

/// <summary>
/// «Дипломатия»: a card dropping from the affiliate button. An open demand for tribute (pay or refuse, with the time
/// left), the tribute we pay, then every nation we have met: how they see us, the pact or tribute between us, the land
/// leaning over the common border, and the deeds — gifts, friendship, a demand for tribute.
/// </summary>
public partial class DiplomacyCard : Control
{
    const int CardWidth = 400;
    readonly PanelContainer _card;
    readonly VBoxContainer _body = Ui.VBox(0);
    readonly ScrollContainer _scroll;
    readonly Leaderboard.Notch _notch = new();
    long _shown = -1;

    public DiplomacyCard()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        var title = Ui.Text("Дипломатия", "LeadTitle");
        title.Uppercase = true;
        var head = Ui.Panel(St.Header(36).Pad(14, 11, 14, 10), Ui.HBox(8, Ui.Icon("affiliate", 1, Pal.Ac), title, Ui.Expand()));
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
        if (_body.GetChildCount() > 0 && _body.GetChild(_body.GetChildCount() - 1) is Control last && last.Size.Y > 0)
            _scroll.CustomMinimumSize = new Vector2(0, Mathf.Min(last.Position.Y + last.Size.Y + 8 + 12, screen.Y - 140 - 44));
        _card.ResetSize();
    }

    /// <summary>What the card shows, folded: rebuilt only when an opinion step, a pact, tribute, a demand, the leaning
    /// land, the treasury's ability to pay a gift or the met nations change.</summary>
    static long Signature()
    {
        var g = Game.I;
        var s = g.State;
        long h = 17;
        void Mix(long v) => h = h * 1_000_003 + v;
        Mix(s.DemandFrom[g.Viewer]); Mix((long)g.DemandSeconds); Mix(s.TributeTo[g.Viewer]);
        for (int m = 0; m < s.Nat.Length; m++)
        {
            if (m == g.Viewer || !Rules.Met(s, g.Viewer, m)) continue;
            Mix(m); Mix(g.OpinionOf(m, g.Viewer) / 5); Mix(g.PactWith(m) ? 1 : 0); Mix(s.TributeTo[m]);
            Mix(g.GiftProblem(m) == null ? 1 : 0); Mix(g.DemandProblem(m) == null ? 1 : 0);
            var (a, b) = g.Leaning(m); Mix(a); Mix(b);
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
        Ui.Clear(_body);

        int d = g.DemandFrom;
        if (d >= 0)
        {
            var pay = Ui.Button("Платить", null, "Pri", () => Game.I.AnswerDemand(true), 1, 28);
            var refuse = Ui.Button("Отказать", null, "Ghost", () => Game.I.AnswerDemand(false), 1, 28);
            var box = Ui.VBox(4,
                Ui.HBox(8, Ui.Icon("alert-triangle", 1, Pal.Bad).Center(), Ui.Text($"{g.Nations[d].Name} требует дань", "Semi").Grow()),
                Ui.Text($"{Diplomacy.TributePct}% наших налогов каждый цикл. Откажем — их люди будут вдвое сильнее мутить наши границы. Ответ нужен через {(int)g.DemandSeconds} с", "SmallMu", true),
                Ui.HBox(8, pay.Grow(), refuse.Grow()));
            _body.AddChild(Ui.Panel(new Box().Fill(Pal.Surface).Border(Pal.Ln).AccentLeft(Pal.Bad, 4).Pad(10, 8, 10, 10), box, MouseFilterEnum.Pass));
            _body.AddChild(Ui.Gap(0, 8));
        }
        int payee = g.TributeTo(g.Viewer);
        if (payee >= 0)
        {
            var stop = Ui.Button("Перестать платить", null, "Ghost", () => Game.I.StopTribute(), 1, 28);
            _body.AddChild(Ui.HBox(8, Ui.Text($"Мы платим дань: {g.Nations[payee].Name}", "SmallMu", true).Grow(), stop));
            _body.AddChild(Ui.Gap(0, 8));
        }

        _body.AddChild(Ui.Margin(Ui.Cap("Встреченные народы"), 0, 4, 0, 4));
        var met = new List<int>();
        for (int m = 0; m < s.Nat.Length; m++) if (m != g.Viewer && Rules.Met(s, g.Viewer, m)) met.Add(m);
        if (met.Count == 0) _body.AddChild(Kit.Para("Мы ещё никого не встретили. Отправьте разведчиков — дипломатия начинается со знакомства", true, UiFonts.Small));
        var score = Rules.Scores(s);
        met.Sort((a, b) => score[b].CompareTo(score[a]));
        foreach (int m in met) _body.AddChild(NationRow(m));
        _body.AddChild(Ui.Gap(0, 6));
        _body.AddChild(Kit.Para($"Несчастная пограничная провинция (довольство ниже {Diplomacy.PullMood}) рядом с соседями, у которых живут на {Diplomacy.PullGap}+ лучше, за минуту переходит к ним. Договор, уплаченная дань и Великая стена держат границу", true, UiFonts.Small));
    }

    Control NationRow(int m)
    {
        var g = Game.I;
        var n = g.Nations[m];
        int op = g.OpinionOf(m, g.Viewer);
        string mood = Diplomacy.Mood(op);
        var status = new List<string>();
        if (g.PactWith(m)) status.Add("договор о дружбе");
        if (g.TributeTo(m) == g.Viewer) status.Add("платит нам дань");
        if (g.TributeTo(g.Viewer) == m) status.Add("мы платим им дань");
        var (ours, theirs) = g.Leaning(m);
        if (ours > 0) status.Add($"к ним тянутся {ours} наших");
        if (theirs > 0) status.Add($"к нам тянутся {theirs} их");
        var name = Ui.HBox(8, new Swatch(Pal.Nation(m), 12).Center(), Ui.Text(n.Name, "Semi").Grow(),
            Ui.Text($"{mood} · {op:+0;−0;0}", "SmallMu").Colored(op <= Diplomacy.Hostile ? Pal.Bad : op >= Diplomacy.Friendly ? Pal.Ok : Pal.Mu));
        var col = Ui.VBox(4, name);
        if (status.Count > 0) col.AddChild(Ui.Text(string.Join(" · ", status), "SmallMu", true));

        var gift = Ui.Button($"Дары · {g.GiftPrice(m)}", "coins", null, () => Game.I.Gift(m), 1, 26);
        Ui.Enable(gift, g.GiftProblem(m) == null);
        gift.Tip("Дары", null, $"Золото им, расположение +{Diplomacy.GiftOpinion}. Цена растёт с их богатством");
        Button pact;
        if (g.PactWith(m))
        {
            pact = Ui.Button("Разорвать", null, "Ghost", () => Game.I.BreakPact(m), 1, 26);
            pact.Tip("Разорвать договор", null, $"Торговля прекратится, расположение {Diplomacy.BreakOpinion}");
        }
        else
        {
            pact = Ui.Button("Дружба", null, null, () => Game.I.ProposePact(m), 1, 26);
            string why = g.PactProblem(m);
            Ui.Enable(pact, why == null);
            pact.Tip(t => t.Title("Договор о дружбе").Line($"Налоги +{Diplomacy.PactTaxPermille / 10}% у обоих, провинции не переманивают друг у друга").Mu(why ?? "Они согласны"));
        }
        var demand = Ui.Button("Дань", null, "Ghost", () => Game.I.DemandTribute(m), 1, 26);
        string dwhy = g.DemandProblem(m);
        Ui.Enable(demand, dwhy == null);
        demand.Tip(t => t.Title("Потребовать дань").Line($"{Diplomacy.TributePct}% их налогов каждый цикл. Соглашаются те, кто вдвое слабее").Mu(dwhy ?? "Можно потребовать"));
        col.AddChild(Ui.HBox(6, gift, pact, demand));
        var box = new Box().Border(Pal.Ln, 0, 0, 0, 2).Pad(0, 7, 0, 9);
        var row = Ui.Panel(box, col, MouseFilterEnum.Pass);
        row.Tip(t => t.Title(n.Name).Line($"{n.Gov} · {n.CultureAdj} культура").Kv("К нам", $"{mood} ({op:+0;−0;0})")
            .Kv("Мы к ним", g.OpinionOf(g.Viewer, m).ToString("+0;−0;0")).Kv("Граница", g.Borders(m) ? "общая" : "нет"));
        return row;
    }
}
