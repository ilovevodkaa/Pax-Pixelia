using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI;

/// <summary>
/// Event cards stacked top-left (newest first, at most 3): pixel cards with an icon tile, the chronicle date and a
/// hover accent bar. Laid out manually so moves can be animated:
/// incoming notes wait in a queue and appear one at a time (at most one every <see cref="Spacing"/> s, so a burst at
/// speed 5 does not churn); the stack eases down first, then the new card fades in on top once the card below has
/// settled; a card that leaves (pushed out or clicked away, × on hover) drops behind the others and fades quickly,
/// holding no space. Heights come from the cards' minimum size, which is valid on their first frame.
/// </summary>
public partial class Notifications : Control
{
    public const int Width = 364;
    const int MaxNotes = 3, Gap = 6, MaxQueued = 6;
    const double Spacing = .25, FadeIn = .18, FadeOut = .08;
    readonly List<Note> _notes = new();
    readonly Queue<(string icon, string text, string year)> _queue = new();
    double _cooldown;

    public Notifications()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(12, TopBar.Height + 12);
        Size = new Vector2(Width, 10);
    }

    public void Add(string icon, string text)
    {
        var year = Game.I.IsReady ? Game.I.DateText : "";
        if (_queue.Count >= MaxQueued) _queue.Dequeue();   // a flood: the oldest waiting note is skipped
        _queue.Enqueue((icon, text, year));
    }

    public void Clear()
    {
        foreach (var c in GetChildren()) c.QueueFree();
        _notes.Clear();
        _queue.Clear();
        _cooldown = 0;
    }

    void Show((string icon, string text, string year) n)
    {
        var note = new Note(n.icon, n.text, n.year);
        note.Clicked += () => Dismiss(note);
        AddChild(note);
        note.Position = new Vector2(-8, 0);
        note.Modulate = new Color(1, 1, 1, 0);
        _notes.Insert(0, note);
        for (int i = _notes.Count - 1; i >= MaxNotes; i--) Dismiss(_notes[i]);
    }

    void Dismiss(Note n)
    {
        if (!_notes.Remove(n)) return;
        n.Dying = true;   // keeps swallowing input until freed, so the click can't reach the map
        MoveChild(n, 0);  // behind the live cards while it fades
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta, k = 1 - Mathf.Exp(-dt * 16);
        _cooldown -= delta;
        if (_queue.Count > 0 && _cooldown <= 0) { Show(_queue.Dequeue()); _cooldown = Spacing; }

        bool belowSettled = true;   // evaluated bottom-up below; the top card waits for the one under it
        for (int i = _notes.Count - 1; i >= 0; i--)
        {
            var n = _notes[i];
            float top = 0;
            for (int j = 0; j < i; j++) top += _notes[j].GetCombinedMinimumSize().Y + Gap;
            var target = new Vector2(0, top);
            if (i == 0 && n.Modulate.A < 1 && !belowSettled)
            {
                n.Position = new Vector2(n.Position.X, top);   // wait in place, invisible, until the stack has made room
                continue;
            }
            // whole pixels only: the unantialiased font would shimmer at fractional positions
            var next = n.Position.Lerp(target, k).Round();
            if (next == n.Position) next = n.Position.MoveToward(target, 1).Round();
            n.Position = n.Position.DistanceTo(target) < 1f ? target : next;
            if (n.Modulate.A < 1) n.Modulate = new Color(1, 1, 1, Mathf.MoveToward(n.Modulate.A, 1, dt / (float)FadeIn));
            if (i == 1) belowSettled = MathF.Abs(n.Position.Y - top) <= 2;
        }
        for (int i = GetChildCount() - 1; i >= 0; i--)
        {
            if (GetChild(i) is not Note n || !n.Dying) continue;
            n.Modulate = new Color(1, 1, 1, n.Modulate.A - dt / (float)FadeOut);
            if (n.Modulate.A <= 0) n.QueueFree();
        }
    }

    /// <summary>A note: sunken icon tile, text, date line; × and the left accent bar on hover.</summary>
    sealed partial class Note : PanelContainer
    {
        public bool Dying;
        public event System.Action Clicked;
        readonly TextureRect _x;
        readonly Box _normal = St.Card().Pad(8, 8, 8, 9);
        readonly Box _hover = St.Card().Border(Pal.Ln3).AccentLeft(Pal.Ac, 4).Pad(8, 8, 8, 9);

        public Note() : this("info-circle", "", "") { }
        public Note(string icon, string text, string year)
        {
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            AddThemeStyleboxOverride("panel", _normal);
            var tile = Ui.Panel(St.Tile(), Ui.Icon(icon, 2, Pal.Ac)).MinSize(34, 34);
            tile.SizeFlagsVertical = SizeFlags.ShrinkBegin;
            var body = Ui.Text(text, null, wrap: true).Spacing(3);
            body.CustomMinimumSize = new Vector2(Width - 16 - 34 - 10 - 20, 0);
            body.Size = body.CustomMinimumSize;
            var date = Ui.Text(year, "SmallMu");
            _x = Ui.Icon("x", 1, Pal.Mu);
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
