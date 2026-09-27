using System;
using System.Collections.Generic;
using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>
/// Lockstep properties: the same commands at the same ticks give the same state whatever the tick batching or frame
/// rate, and a fresh state fed the journal reproduces a live game bit for bit (also with two human nations).
/// </summary>
public static class ReplayTests
{
    const int Ticks = 3200;   // 400 cycles, 6.7 min at speed 3

    public static void Run(WorldData w)
    {
        Section("determinism: tick batching and frame rates");
        var byBatch = new[] { 1, 3, 7, 64 }.Select(b => Live(w, humans: 1, batch: b, out _, out _)).ToArray();
        Check(byBatch.Distinct().Count() == 1, $"batches of 1/3/7/64 ticks → one hash ({string.Join(" ", byBatch.Select(h => h.ToString()))})");
        var byFps = new[] { 30, 60, 144 }.Select(fps => LiveFrames(w, fps)).ToArray();
        Check(byFps.Distinct().Count() == 1 && byFps[0].Equals(byBatch[0]), $"30/60/144 fps through the tick pump → the same hash ({string.Join(" ", byFps.Select(h => h.ToString()))})");

        Section("replay: journal → the same game");
        var live = Live(w, humans: 1, batch: 5, out var journal, out long until);
        Info($"{journal.Count} commands over {until} ticks: {string.Join(", ", journal.GroupBy(c => c.Type).Select(g => $"{g.Key}×{g.Count()}"))}");
        Check(journal.Count >= 10 && journal.Any(c => c.IsSession) && journal.Any(c => c.Type == CmdType.ScoutAuto), "the journal holds game and session commands");
        Check(journal.Zip(journal.Skip(1)).All(z => z.First.Tick <= z.Second.Tick), "journal is in tick order");
        var replayed = ReplayOf(w, humans: 1, journal, until);
        Check(replayed.Equals(live), $"replay hash = live hash ({replayed} vs {live}){(replayed.Equals(live) ? "" : " · differs in: " + replayed.Diff(live))}");
        var parsed = journal.Select(c => Cmd.Parse(c.ToLine())).ToList();
        Check(parsed.SequenceEqual(journal), "journal lines parse back to the same commands");
        Check(ReplayOf(w, humans: 1, parsed, until).Equals(live), "replay from the text journal = live");
        var tampered = journal.Where((c, i) => i != journal.FindIndex(x => x.Type == CmdType.Claim)).ToList();
        var other = ReplayOf(w, humans: 1, tampered, until);
        Check(!other.Equals(live), $"dropping one claim changes the hash (sections: {other.Diff(live)})");

        Section("two human nations (multiplayer-shaped state)");
        var two = Live(w, humans: 2, batch: 3, out var j2, out long u2);
        Check(j2.Any(c => c.Nation == 1), "nation 1 issued commands too");
        Check(ReplayOf(w, humans: 2, j2, u2).Equals(two), "replay of a two-human game = live");
        var s2 = Fresh(w, 2);
        Check(s2.Nat[0].Fog != null && s2.Nat[1].Fog != null && !ReferenceEquals(s2.Nat[0].Fog.Fog, s2.Nat[1].Fog.Fog), "each human nation has its own fog map");
        Check(!s2.Nat[0].Fog.Explored.SequenceEqual(s2.Nat[1].Fog.Explored), "their maps differ");
    }

    static GameState Fresh(WorldData w, int humans)
    {
        var s = NationGen.CreateInitialState(w);
        for (int n = 0; n < humans; n++) s.Nat[n].Control = NationControl.Human;
        Simulation.Begin(w, s);
        s.Events = new SimEvents(TestContent.Db, w, s);   // the deck is part of the game: replays must deal the same cards
        return s;
    }

    /// <summary>A scripted live game: commands submitted at fixed ticks, ticks run in batches of `batch`.</summary>
    static StateHash Live(WorldData w, int humans, int batch, out List<Cmd> journal, out long until)
    {
        var s = Fresh(w, humans);
        var q = new CommandQueue();
        var sink = new Recorder();
        var script = Script(w, s, humans);
        int next = 0;
        while (s.Tick < Ticks)
        {
            while (next < script.Count && script[next].at <= s.Tick)
            {
                var c = script[next++].make(s);
                if (c.Type != CmdType.None) { q.Submit(s, c); q.Flush(w, s, sink); }
            }
            int n = (int)Math.Min(batch, Ticks - s.Tick);
            if (next < script.Count) n = (int)Math.Min(n, Math.Max(1, script[next].at - s.Tick));
            q.Run(w, s, n, sink);
        }
        // a last command at the final boundary is part of the game too
        q.Submit(s, Cmd.CheatGold(0, 7)); q.Flush(w, s, sink);
        journal = q.Journal.ToList();
        until = s.Tick;
        return s.Hash();
    }

    /// <summary>The same script driven by the real-time pump at a frame rate (speed 3; pauses stop the pump like Game does).</summary>
    static StateHash LiveFrames(WorldData w, int fps)
    {
        var s = Fresh(w, 1);
        var q = new CommandQueue();
        var sink = new Recorder();
        var pump = new TickPump();
        var script = Script(w, s, 1);
        int next = 0;
        long frame = TickPump.MicrosPerSecond / fps;
        while (s.Tick < Ticks)
        {
            while (next < script.Count && script[next].at <= s.Tick)
            {
                var c = script[next++].make(s);
                if (c.Type != CmdType.None) { q.Submit(s, c); q.Flush(w, s, sink); }
            }
            if (s.Paused) { q.Submit(s, Cmd.Unpause(0)); q.Flush(w, s, sink); }   // the scripted player resumes at once
            int n = pump.Advance(frame, Clock.TicksPerSecond[s.Speed]);
            if (next < script.Count) n = (int)Math.Min(n, Math.Max(0, script[next].at - s.Tick));
            n = (int)Math.Min(n, Ticks - s.Tick);
            q.Run(w, s, n, sink);
        }
        q.Submit(s, Cmd.CheatGold(0, 7)); q.Flush(w, s, sink);
        return s.Hash();
    }

    static StateHash ReplayOf(WorldData w, int humans, IReadOnlyList<Cmd> journal, long until)
    {
        var s = Fresh(w, humans);
        Replay.Run(w, s, journal, until, new Recorder());
        return s.Hash();
    }

    /// <summary>Commands at fixed ticks; each picks its target from the state at that moment (like a player looking at the map).</summary>
    static List<(long at, Func<GameState, Cmd> make)> Script(WorldData w, GameState s0, int humans)
    {
        var list = new List<(long, Func<GameState, Cmd>)>
        {
            (0, s => Cmd.ScoutAuto(0)),
            (0, s => Cmd.SetSpeed(0, 5)),
            (40, s => Cmd.Pause(0)),
            (40, s => ClaimBest(w, s, 0)),
            (40, s => ClaimBest(w, s, 0)),
            (40, s => Cmd.Unpause(0)),
            (41, s => Cmd.ScoutAuto(0)),
            (300, s => BuildFirst(w, s, 0)),
            (301, s => Cmd.Survey(0, FirstOwned(s, 0))),
            (777, s => ClaimBest(w, s, 0)),
            (1500, s => Cmd.ScoutAuto(0)),
            (1501, s => Cmd.SetSpeed(0, 2)),
            (2222, s => ClaimBest(w, s, 0)),
            (2600, s => Cmd.CheatEra(0, 1)),
            (2601, s => BuildFirst(w, s, 0)),
            (2700, s => s.Events.Pending(0) != null ? Cmd.Choose(0, 0) : Cmd.CheatGold(0, 1)),
        };
        if (humans > 1)
        {
            list.Add((0, s => Cmd.ScoutAuto(1)));
            list.Add((40, s => ClaimBest(w, s, 1)));
            list.Add((900, s => BuildFirst(w, s, 1)));
            list.Add((1500, s => ClaimBest(w, s, 1)));
        }
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return list;
    }

    static Cmd ClaimBest(WorldData w, GameState s, int n)
    {
        var fert = WorldFacts.Of(w).FertPm;
        int best = -1;
        for (int p = 0; p < w.P; p++)
            if (Rules.CheckClaim(w, s, p, n) == ClaimError.None && (best < 0 || fert[p] > fert[best])) best = p;
        return best < 0 ? default : Cmd.Claim(n, best);
    }

    static Cmd BuildFirst(WorldData w, GameState s, int n)
    {
        for (int p = 0; p < w.P; p++)
            if (s.Owner[p] == n && Rules.BuildOptions(w, s, p, n) is { Count: > 0 } o) return Cmd.Build(n, p, o[0]);
        return default;
    }

    static int FirstOwned(GameState s, int n) => Array.FindIndex(s.Owner, o => o == n);
}
