using System;
using Godot;
using PaxPixelia.Core.Save;
using PaxPixelia.Sim;

namespace PaxPixelia.Core;

/// <summary>
/// «Блиц недели» on the Game side (Core/Blitz.cs): the clock stops at the blitz's last tick, orders are refused from
/// then on, the result is scored and written to the blitz folder (BlitzStore) and <see cref="BlitzEnded"/> tells the
/// HUD. A loaded blitz that had already ended shows its result again (the same file is rewritten).
/// </summary>
public partial class Game
{
    /// <summary>The running blitz reached its end (the result card opens).</summary>
    public event Action BlitzEnded;

    public bool IsBlitz => IsReady && Setup is { IsBlitz: true };

    /// <summary>The blitz time is up: the clock stands, only pause and speed are taken.</summary>
    public bool BlitzOver => IsBlitz && State.Tick >= Setup.BlitzTicks;

    public long BlitzTicksLeft => IsBlitz ? Math.Max(0, Setup.BlitzTicks - State.Tick) : 0;

    /// <summary>The week of the running blitz («2026-40»), from its seed text.</summary>
    public string BlitzWeek => IsBlitz && Setup.SeedText is { } t && t.StartsWith(Blitz.SeedTextOf("")) ? t[Blitz.SeedTextOf("").Length..] : "";

    /// <summary>The finished blitz's record and file (null before the end).</summary>
    public BlitzRecord BlitzResult { get; private set; }
    public string BlitzResultPath { get; private set; }

    /// <summary>The result is this game's (the running blitz has ended and been scored).</summary>
    public bool BlitzResultCurrent => BlitzOver && BlitzResult != null && ReferenceEquals(_blitzFor, State);

    GameState _blitzFor;

    /// <summary>At most this many ticks may run now (the blitz stops exactly on its last tick).</summary>
    int BlitzCap(int ticks) => IsBlitz ? (int)Math.Min(ticks, BlitzTicksLeft) : ticks;

    /// <summary>Called after ticks ran and on WorldReady: score and write a blitz that has just ended (once per game).</summary>
    void CheckBlitzEnd()
    {
        if (!BlitzOver || ReferenceEquals(_blitzFor, State)) return;
        _blitzFor = State;
        BlitzResult = Blitz.Record(BlitzWeek, ProjectSettings.GetSetting("application/config/version", "0.1").AsString(), Setup, State, Journal,
                                   DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        try { BlitzResultPath = BlitzStore.Write(BlitzResult); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            BlitzResultPath = null;
            GD.PushError($"blitz: the result was not written: {e.Message}");
        }
        GD.Print($"blitz: over at tick {State.Tick}, score {BlitzResult.Score.Total} → {BlitzResultPath}");
        BlitzEnded?.Invoke();
    }

    /// <summary>A fresh blitz: the rules in one chronicle line.</summary>
    void AnnounceBlitz()
    {
        if (!IsBlitz || State.Tick > 0) return;
        Notify("hourglass", $"Блиц недели: {Blitz.Ticks / 480} минут игрового времени на скорости 3. Очки дают люди, земли, эпохи, знания, города и встречи");
    }
}
