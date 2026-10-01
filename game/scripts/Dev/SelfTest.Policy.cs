using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.Dev;

/// <summary>«Политика» through the real UI: the bank button opens the card, «Издать» issues an edict through a command,
/// the full slots disable the rest, «Отменить» repeals it, Esc closes the card.</summary>
public partial class SelfTest
{
    async Task PolicyFlow()
    {
        var me = G.State.Nat[GameState.LocalPlayer];
        int before = me.Edicts;
        for (int e = 0; e < Policy.Count; e++) if (Policy.On(me, e)) G.SetEdict(e, false);   // a clean start
        await Frames(2);
        Hud.Top.PolicyButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check("policy: the bank button opens the card", Hud.Policy.Visible);
        Check("policy: the card shows the budget and the limit", FindText(Hud.Policy, "Бюджет за цикл") && FindText(Hud.Policy, "Провинции"));
        var issue = AllButtons(Hud.Policy).Where(b => Caption(b) == "Издать").ToList();
        Check("policy: every edict has its button", issue.Count == Policy.Count, $"{issue.Count} of {Policy.Count}");
        if (issue.Count == 0) return;
        int notes = _notes.Count, journal = G.Journal.Count;
        issue[0].EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check("policy: «Издать» puts the edict in force through a command", Policy.On(me, 0) && G.Journal.Count > journal);
        Check("policy: the chronicle notes it", _notes.Skip(notes).Any(n => n.text.StartsWith("Издан указ")));
        Hud.Policy.Refresh();
        await Frames(2);
        var after = AllButtons(Hud.Policy).ToList();
        bool full = me.Era == 0 || Policy.Active(me) >= Policy.Slots(me.Era);
        Check("policy: with the slots full the other edicts wait", !full || after.Where(b => Caption(b) == "Издать").All(b => b.Disabled));
        await Shot("policy");
        var repeal = after.FirstOrDefault(b => Caption(b) == "Отменить");
        Check("policy: the edict in force can be repealed", repeal != null);
        repeal?.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check("policy: «Отменить» repeals it", !Policy.On(me, 0));
        PressKey(Key.Escape);
        await Frames(3);
        Check("policy: Esc closes the card", !Hud.Policy.Visible);
        for (int e = 0; e < Policy.Count; e++) if ((before >> e & 1) != 0) G.SetEdict(e, true);
    }
}
