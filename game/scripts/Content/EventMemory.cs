using System;
using System.Collections.Generic;

namespace PaxPixelia.Content;

/// <summary>
/// The event deck's memory of one nation: cooldowns, one-shots, memory flags, threads, the open choice and pending
/// follow-ups. Plain data owned by GameState (one per nation) so it replays and serialises with the rest of the
/// state; only EventRunner mutates it, the simulation sets its own flags through EventRunner.SetFlag.
/// </summary>
public sealed class EventMemory
{
    public readonly record struct Scheduled(long At, int Event, int Province, int Foreign);

    public int Nation;
    /// <summary>Bots (and the advisor of an absent player) answer choices at once by the AI weights.</summary>
    public bool Bot;
    /// <summary>Tick of the next metronome beat and the earliest tick of the next choice.</summary>
    public long NextDue, NextChoice;
    /// <summary>Per event: tick from which it may come again (long.MaxValue = a fired one-shot).</summary>
    public long[] ReadyAt;
    public int[] FiredCount;
    /// <summary>Per ContentDb.Flags index.</summary>
    public bool[] Flags;
    /// <summary>Per thread: era of its last stage (one stage per era), -1 = none yet.</summary>
    public int[] ThreadEra;
    /// <summary>Bit per thread whose first stage is boosted this game (CONTENT §4: «2 случайные нити»).</summary>
    public int GuaranteedThreads;
    /// <summary>Jokes fired in a row (anti-repeat).</summary>
    public int JokeStreak;
    public int Total;
    /// <summary>A flag changed since the last trigger scan.</summary>
    public bool FlagsDirty;
    /// <summary>The choice window open for this nation (at most one), null when none.</summary>
    public EventRecord? Pending;
    /// <summary>Follow-ups by due tick (ascending).</summary>
    public readonly List<Scheduled> Queue = new();

    public EventMemory(ContentDb db, int nation, bool bot)
    {
        Nation = nation;
        Bot = bot;
        ReadyAt = new long[db.Events.Length];
        FiredCount = new int[db.Events.Length];
        Flags = new bool[db.Flags.Count];
        ThreadEra = new int[db.Threads.Length];
        Array.Fill(ThreadEra, -1);
    }

    public bool Has(int flag) => flag >= 0 && Flags[flag];

    internal void Schedule(in Scheduled s)
    {
        int i = Queue.Count;
        while (i > 0 && Queue[i - 1].At > s.At) i--;   // stable: equal ticks keep insertion order
        Queue.Insert(i, s);
    }
}
