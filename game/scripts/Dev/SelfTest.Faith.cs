using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.Dev;

/// <summary>«Правитель и вера» through the real UI: the sun button opens the card with the ruler, an open teaching
/// offers its dogmas, «Принять» adopts one through a command, Esc closes the card.</summary>
public partial class SelfTest
{
    async Task FaithFlow()
    {
        var nat = G.State.Nat[GameState.LocalPlayer];
        Hud.Top.FaithButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check("faith: the sun button opens the card with the ruler", Hud.FaithView.Visible && FindText(Hud.FaithView, Leader.Name(nat.RulerSeed)));
        if (nat.Dogmas == 0)
        {
            G.Issue(Cmd.CheatTech(G.Viewer, Techs.Index("ancestors")));   // the teaching slot opens with «Духи предков»
            Hud.FaithView.Refresh(true);
            await Frames(3);
            var adopt = AllButtons(Hud.FaithView).FirstOrDefault(b => Caption(b) == "Принять" && !b.Disabled);
            Check("faith: an open teaching offers its dogmas", adopt != null);
            int journal = G.Journal.Count;
            adopt?.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(4);
            Check("faith: «Принять» adopts a dogma through a command", nat.Dogmas != 0 && G.Journal.Count > journal);
        }
        await Shot("faith");
        PressKey(Key.Escape);
        await Frames(3);
        Check("faith: Esc closes the card", !Hud.FaithView.Visible);
    }
}
