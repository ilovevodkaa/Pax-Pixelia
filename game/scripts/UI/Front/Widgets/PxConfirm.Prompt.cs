using System.Threading.Tasks;
using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>
/// PxConfirm with a text field (the pause menu's «Сохранить» asks for the save's name). The same modal panel, title,
/// text and buttons; the field sits under the text, holds the focus and selects its text, Enter accepts it and Esc
/// cancels (the first Esc may only leave the field's editing).
/// </summary>
public partial class PxConfirm
{
    string _typed;

    /// <summary>Ask for a line of text: returns it trimmed (may be empty), or null when cancelled.</summary>
    public static async Task<string> AskText(Node parent, string title, string text, string initial, string ok, int maxLength = 60)
    {
        var d = new PxConfirm(title, text, new[] { ok, "Отмена" }, 0, 0, -1) { _typed = initial ?? "" };
        var edit = new LineEdit
        {
            Name = "Field", Text = initial ?? "", MaxLength = maxLength, SelectAllOnFocus = true,
            CustomMinimumSize = new Vector2(Width - 48, 46), ContextMenuEnabled = false,
        };
        edit.TextChanged += t => d._typed = t;
        edit.TextSubmitted += _ => { if (!d._done) d.Close(0); };
        var box = d.GetChild(1).GetChild(0).GetChild(0);   // shade · centre › panel › column: head, bar, text, gap, buttons
        box.AddChild(edit);
        box.MoveChild(edit, 3);
        d.Ready += () =>
        {
            // after PxConfirm._Ready queued the focus of a button: the field takes it, ↓ / Tab reach the buttons
            edit.FocusNeighborBottom = edit.FocusNext = edit.GetPathTo(d._buttons[0]);
            foreach (var b in d._buttons) b.FocusNeighborTop = b.GetPathTo(edit);
            d._buttons[^1].FocusNext = d._buttons[^1].GetPathTo(edit);
            edit.CallDeferred(Control.MethodName.GrabFocus);
        };
        parent.AddChild(d);
        int answer = await d._result.Task;
        return answer == 0 ? d._typed.Trim() : null;
    }
}
