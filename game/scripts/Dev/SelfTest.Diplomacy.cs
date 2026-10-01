using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.Dev;

/// <summary>«Дипломатия» through the real UI: the affiliate button opens the card with the nations we met, «Дары» sends
/// gold through a command and warms them, Esc closes the card.</summary>
public partial class SelfTest
{
    async Task DiplomacyFlow()
    {
        var s = G.State;
        Hud.Top.DiplomacyButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check("diplomacy: the affiliate button opens the card", Hud.DiplomacyView.Visible);
        int m = Enumerable.Range(1, s.Nat.Length - 1).FirstOrDefault(k => Rules.Met(s, G.Viewer, k), -1);
        Check("diplomacy: the nations we met are listed", m > 0 && FindText(Hud.DiplomacyView, G.Nations[m].Name), m > 0 ? G.Nations[m].Name : "nobody met");
        if (m <= 0) { PressKey(Key.Escape); await Frames(2); return; }
        G.Issue(Cmd.CheatGold(G.Viewer, 1000));
        Hud.DiplomacyView.Refresh(true);
        await Frames(3);
        int Warmth() { int t = 0; for (int k = 0; k < s.Nat.Length; k++) if (k != G.Viewer) t += Diplomacy.Opinion(s, k, G.Viewer); return t; }
        int before = Warmth(), journal = G.Journal.Count;
        var gift = AllButtons(Hud.DiplomacyView).FirstOrDefault(b => Caption(b).StartsWith("Дары") && !b.Disabled);
        gift?.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check("diplomacy: «Дары» warm a nation through a command", gift != null && G.Journal.Count > journal && Warmth() >= before + Diplomacy.GiftOpinion - 2,
              $"their opinions of us {before} → {Warmth()}");
        await Shot("diplomacy");
        PressKey(Key.Escape);
        await Frames(3);
        Check("diplomacy: Esc closes the card", !Hud.DiplomacyView.Visible);
    }
}
