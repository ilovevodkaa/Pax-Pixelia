using System.Linq;
using System.Threading.Tasks;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.Dev;

/// <summary>«Правительство» through the real UI: the bank button opens the screen, a real click on «Основы государства»
/// starts it through a command, once laid the ring shows its ways, a mouse drag moves the field and «В центр» brings it
/// back, the «Законы» tab lists the laws, Esc closes the screen.</summary>
public partial class SelfTest
{
    async Task PolicyFlow()
    {
        var me = G.State.Nat[GameState.LocalPlayer];
        Hud.Top.PolicyButton.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(4);
        Check("government: the bank button opens the screen", Hud.Policy.Visible);
        Check("government: the header shows the budget and the realm", FindText(Hud.Policy, "за цикл"));
        var root = LabelOf(Hud.Policy, "Основы государства")?.GetParent<Control>();
        Check("government: «Основы государства» stand in the middle, the ways around are «?»", root != null && !FindText(Hud.Policy, "Единоначалие"));
        if (root == null) return;
        await Seconds(.8);   // the first opening glides in

        // a real click (press and release without a drag) starts the course
        int journal = G.Journal.Count;
        var at = root.GetGlobalRect().GetCenter();
        MouseAt(at); MouseAt(at, MouseButton.Left, true); MouseAt(at, MouseButton.Left, false);
        await Frames(3);
        Check("government: a click on the card starts it through a command", me.CourseNow == Politics.Root && G.Journal.Skip(journal).Any(c => c.Type == CmdType.Course));
        G.RunTicks((Politics.Total(Politics.Root, G.State.Pace) + 1) * Clock.CycleTicks);
        await Frames(3);
        Check("government: laid, the ring shows its ways", Politics.Has(me, Politics.Root) && FindText(Hud.Policy, "Единоначалие") && FindText(Hud.Policy, "Вече"));
        await Shot("government");

        // drag the field by its empty part, then back to the centre
        var view = Hud.Policy.GetViewportRect().Size;
        // the drag starts on an open card: letting go there must not adopt it
        var openCard = LabelOf(Hud.Policy, "Наследный надел")?.GetParent<Control>();   // on the side: the top one may sit under the header of a small window
        Check("government: an open course card to drag from", openCard != null);
        var from = openCard?.GetGlobalRect().GetCenter() ?? new Vector2(view.X * .5f, view.Y * .5f + 120);
        var rootAt = root.GetGlobalRect().Position;
        MouseAt(from); MouseAt(from, MouseButton.Left, true);
        for (int k = 1; k <= 10; k++) { MouseAt(from + new Vector2(k * 18, k * -6), held: true); await Frames(1); }
        var mid = root.GetGlobalRect().Position - rootAt;
        MouseAt(from + new Vector2(180, -60), MouseButton.Left, false);
        await Frames(4);
        var moved = root.GetGlobalRect().Position - rootAt;
        Check("government: a drag moves the field (and it glides on)", mid.X > 120 && mid.Y < -30 && moved.X >= mid.X, $"held {mid}, after {moved}");
        Check("government: a drag is not a click", me.CourseNow < 0);
        var home = AllButtons(Hud.Policy).FirstOrDefault(b => Caption(b) == "В центр");
        home?.EmitSignal(BaseButton.SignalName.Pressed);
        await Seconds(1.2);
        Check("government: «В центр» brings it back", home != null && (root.GetGlobalRect().Position - rootAt).Length() < 6, $"{root.GetGlobalRect().Position - rootAt}");

        var laws = AllButtons(Hud.Policy).FirstOrDefault(b => Caption(b) == "Законы");
        laws?.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(3);
        Check("government: the «Законы» tab lists the laws", FindText(Hud.Policy, "Глава государства") && FindText(Hud.Policy, "Крепостное право"));
        await Shot("government_laws");
        AllButtons(Hud.Policy).FirstOrDefault(b => Caption(b) == "Политика")?.EmitSignal(BaseButton.SignalName.Pressed);
        PressKey(Key.Escape);
        await Frames(3);
        Check("government: Esc closes the screen", !Hud.Policy.Visible);
    }

    static Label LabelOf(Node root, string text)
    {
        foreach (var n in root.FindChildren("*", "Label", true, false))
            if (n is Label l && l.Text == text && l.IsVisibleInTree()) return l;
        return null;
    }

    static void MouseAt(Vector2 at, bool held = false) =>
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, ButtonMask = held ? MouseButtonMask.Left : 0 });

    static void MouseAt(Vector2 at, MouseButton b, bool down) =>
        Input.ParseInputEvent(new InputEventMouseButton { Position = at, GlobalPosition = at, ButtonIndex = b, Pressed = down, ButtonMask = down ? MouseButtonMask.Left : 0 });
}
