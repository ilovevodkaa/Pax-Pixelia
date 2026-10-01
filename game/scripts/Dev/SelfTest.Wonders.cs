using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.Dev;

/// <summary>«Чудеса света» through the real UI: the diamond button opens the card, «Заложить» lays a wonder, «Вложить
/// казну» pours the treasury in, «Бросить» gives half back, Esc closes the card.</summary>
public partial class SelfTest
{
    async Task WondersFlow()
    {
        var me = G.State.Nat[GameState.LocalPlayer];
        if (me.Wonder >= 0) G.StopWonder();
        await Frames(2);
        Hud.Top.WondersButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check("wonders: the diamond button opens the card", Hud.WondersView.Visible);
        var lay = AllButtons(Hud.WondersView).Where(b => Caption(b) == "Заложить").ToList();
        Check("wonders: the era's free wonders can be laid", lay.Count > 0, $"{lay.Count} buttons");
        if (lay.Count == 0) return;
        int journal = G.Journal.Count;
        lay[0].EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check("wonders: «Заложить» lays one through a command", me.Wonder >= 0 && G.Journal.Count > journal, me.Wonder >= 0 ? Wonders.All[me.Wonder].Name : "none");
        Hud.WondersView.Refresh(true);
        await Frames(3);
        Check("wonders: the card shows it under way", FindText(Hud.WondersView, "Строится") && FindText(Hud.WondersView, Wonders.All[System.Math.Max(0, me.Wonder)].Name));
        G.Issue(Cmd.CheatGold(G.Viewer, 500));
        double gold = G.State.Gold;
        var invest = AllButtons(Hud.WondersView).FirstOrDefault(b => Caption(b) == "Вложить казну");
        invest?.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check("wonders: «Вложить казну» pours the treasury in", invest != null && G.State.Gold < gold && me.WonderGold > 0, $"{gold:F0} → {G.State.Gold:F0}");
        await Shot("wonders");
        Hud.WondersView.Refresh(true);
        await Frames(3);
        var stop = AllButtons(Hud.WondersView).FirstOrDefault(b => Caption(b) == "Бросить");
        stop?.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check("wonders: «Бросить» gives the construction up", stop != null && me.Wonder < 0);
        PressKey(Key.Escape);
        await Frames(3);
        Check("wonders: Esc closes the card", !Hud.WondersView.Visible);
    }
}
