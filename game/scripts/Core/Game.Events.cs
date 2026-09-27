using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Content;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Core;

/// <summary>An open event choice as the UI shows it: texts filled, only the options offered to the player.</summary>
public sealed record ChoiceInfo(int Event, string Icon, string Title, string Text, (int Index, string Text)[] Options, float SecondsLeft);

/// <summary>
/// The event deck (game/data/events, Sim/SimEvents) on the Game side: the content pack is read once, every game
/// gets its own runner, and the player's open choice is exposed to the UI and answered through a command.
/// </summary>
public partial class Game
{
    static readonly Lazy<ContentDb> ContentPack = new(LoadContent);

    /// <summary>The content pack (game/data), read once; null if it failed to load.</summary>
    public static ContentDb Content => ContentPack.Value;

    /// <summary>The player's event choice opened or closed (the deadline took option 1, or the player answered).</summary>
    public event Action ChoiceChanged;

    static ContentDb LoadContent()
    {
        try { return ContentLoader.Load(); }
        catch (Exception e) { GD.PushError($"content: the event pack failed to load, events are off: {e.Message}"); return null; }
    }

    /// <summary>Bind the event deck to a fresh state (runs on the generation thread; the pack loads on first use).</summary>
    static void AttachEvents(WorldData w, GameState s, GameSetup setup)
    {
        if (ContentPack.Value is { } db) s.Events = new SimEvents(db, w, s, setup.JokePercent);
    }

    // ISimSink: SimEvents reports a human nation's choice window
    public void EventChoiceChanged(int nation)
    {
        if (nation == Viewer) ChoiceChanged?.Invoke();
    }

    /// <summary>The player's open choice, or null.</summary>
    public ChoiceInfo PendingChoice()
    {
        if (!IsReady || State.Events is not { } ev || ev.Pending(Viewer) is not { } r) return null;
        var def = ev.Db.Events[r.Event].Def;
        var options = new List<(int, string)>();
        for (int k = 0; k < (def.Options?.Count ?? 0); k++)
            if (ev.IsOffered(Viewer, r.Event, k)) options.Add((k, ev.Format(World, State, def.Options[k].Text, r)));
        return new ChoiceInfo(r.Event, def.Icon ?? "feather", ev.Format(World, State, def.Title ?? "", r),
            ev.Format(World, State, def.Text ?? "", r), options.ToArray(), SecondsLeft(r));
    }

    /// <summary>Real seconds at the current speed until the open choice takes its first option; -1 when none is open.</summary>
    public float ChoiceSecondsLeft() => IsReady && State.Events?.Pending(Viewer) is { } r ? SecondsLeft(r) : -1;

    float SecondsLeft(in EventRecord r) =>
        Math.Max(0, r.Deadline - Clock.CycleOf(State.Tick)) * Clock.CycleTicks / (float)Clock.TicksPerSecond[State.Speed];

    /// <summary>Answer the open choice with option k (a command: journaled, replayable).</summary>
    public void ChooseOption(int k)
    {
        if (!IsReady || State.Events?.Pending(Viewer) == null) return;
        if (Issue(Cmd.Choose(Viewer, k)) != 0) ShowRefusal("Этот ответ сейчас недоступен");
    }
}
