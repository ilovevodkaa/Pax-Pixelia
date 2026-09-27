using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI;

/// <summary>
/// #notes — event cards stacked top-left (newest first, at most 3). Cards slide in from the left, the stack eases into
/// place, a click dismisses (× appears on hover). Laid out manually so moves can be animated.
/// </summary>
public partial class Notifications : Control
{
    public const int Width = 364;
    const int MaxNotes = 3, Gap = 6;
    readonly List<Note> _notes = new();

    public Notifications()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(12, TopBar.Height + 12);
        Size = new Vector2(Width, 10);
    }

    public void Add(string icon, string text)
    {
        var year = Game.I.IsReady ? Fmt.Year(Game.I.State.Year) : "";
        var note = new Note(icon, text, year);
        note.Clicked += () => Dismiss(note);
        AddChild(note);
        note.Position = new Vector2(-10, 0);
        note.Modulate = new Color(1, 1, 1, 0);
        _notes.Insert(0, note);
        for (int i = _notes.Count - 1; i >= MaxNotes; i--) Dismiss(_notes[i]);
    }

    public void Clear()
    {
        foreach (var n in _notes) n.QueueFree();
        _notes.Clear();
    }

    void Dismiss(Note n)
    {
        if (!_notes.Remove(n)) return;
        n.Dying = true;   // keeps swallowing input until freed, so the click can't reach the map
    }

    public override void _Process(double delta)
    {
        float k = 1 - Mathf.Exp(-(float)delta * 16), y = 0;
        foreach (var n in _notes)
        {
            var target = new Vector2(0, y);
            n.Position = n.Position.DistanceTo(target) < .5f ? target : n.Position.Lerp(target, k);
            n.Modulate = new Color(1, 1, 1, Mathf.MoveToward(n.Modulate.A, 1, (float)delta * 5));
            y += n.Size.Y + Gap;
        }
        for (int i = GetChildCount() - 1; i >= 0; i--)
        {
            if (GetChild(i) is not Note n || !n.Dying) continue;
            n.Modulate = new Color(1, 1, 1, n.Modulate.A - (float)delta * 7);
            n.Position += new Vector2(-(float)delta * 40, 0);
            if (n.Modulate.A <= 0) n.QueueFree();
        }
    }

    /// <summary>.note — sunken icon tile, text, year line; × on hover.</summary>
    sealed partial class Note : PanelContainer
    {
        public bool Dying;
        public event System.Action Clicked;
        readonly TextureRect _x;
        readonly Box _normal = St.Card(St.R).Pad(8, 8, 8, 9);
        readonly Box _hover = St.Card(St.R).Pad(8, 8, 8, 9);

        public Note() : this("info-circle", "", "") { }
        public Note(string icon, string text, string year)
        {
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            _hover.Border(Pal.Ln3);
            AddThemeStyleboxOverride("panel", _normal);
            var tile = Ui.Panel(St.Tile(false, St.R), Ui.Icon(icon, 16)).MinSize(28, 28);
            tile.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            var body = Ui.Text(text, null, wrap: true);
            body.CustomMinimumSize = new Vector2(Width - 16 - 28 - 10 - 20, 0);
            body.Size = body.CustomMinimumSize;
            var date = Ui.Text(year, "SmallMu");
            _x = Ui.Icon("x", 13, Pal.Mu2);
            _x.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            _x.Modulate = new Color(1, 1, 1, 0);
            AddChild(Ui.HBox(10, tile, Ui.VBox(2, body, date).Grow(), _x));
            Size = new Vector2(Width, 0);
            CustomMinimumSize = new Vector2(Width, 0);
            MouseEntered += () => { AddThemeStyleboxOverride("panel", _hover); _x.Modulate = Colors.White; };
            MouseExited += () => { AddThemeStyleboxOverride("panel", _normal); _x.Modulate = new Color(1, 1, 1, 0); };
        }

        public override void _GuiInput(InputEvent e)
        {
            if (e is not InputEventMouseButton { ButtonIndex: MouseButton.Left } mb) return;
            AcceptEvent();
            if (!mb.Pressed && !Dying) Clicked?.Invoke();
        }
    }
}
