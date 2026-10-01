using System;
using System.Collections.Generic;
using System.Globalization;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

public enum CmdType : byte
{
    None,
    // game commands: change the simulation
    Claim, Build, Survey, ScoutTo, ScoutAuto,
    // session commands: time control (journaled so a replay and the evening summary see them)
    Pause, Unpause, SetSpeed,
    // debug / console: mark a game as tampered with once saves exist
    CheatGold, CheatEra,
    // answer the nation's open event choice (SimEvents): A = option
    Choose,
    // found a town at A (settlers from the nearest city)
    FoundCity,
    // study technology A (Techs.All index); debug: learn technology A (-1 = every one, one path of each fork)
    Research, CheatTech,
    // the tribe: walk to A (-1 = stop), found the capital on the camp with legend A as the myth (-1 = none)
    TribeTo, Settle,
    // put edict A in force (B = 1) or repeal it (B = 0)
    Edict,
}

/// <summary>
/// A serialisable order: at tick boundary Tick, nation Nation's Seq-th command. A, B, C are the arguments
/// (province, building, target, speed, amount…). ~20 bytes; the whole journal of an evening is a few hundred KB.
/// </summary>
public readonly record struct Cmd(int Tick, byte Nation, ushort Seq, CmdType Type, int A = 0, int B = 0, int C = 0)
{
    public static Cmd Claim(int n, int province) => new(0, (byte)n, 0, CmdType.Claim, province);
    public static Cmd Build(int n, int province, Bld b) => new(0, (byte)n, 0, CmdType.Build, province, (int)b);
    public static Cmd Survey(int n, int province) => new(0, (byte)n, 0, CmdType.Survey, province);
    public static Cmd ScoutTo(int n, int province) => new(0, (byte)n, 0, CmdType.ScoutTo, province);
    public static Cmd ScoutAuto(int n) => new(0, (byte)n, 0, CmdType.ScoutAuto);
    public static Cmd Pause(int n) => new(0, (byte)n, 0, CmdType.Pause);
    public static Cmd Unpause(int n) => new(0, (byte)n, 0, CmdType.Unpause);
    public static Cmd SetSpeed(int n, int speed) => new(0, (byte)n, 0, CmdType.SetSpeed, speed);
    public static Cmd CheatGold(int n, int gold) => new(0, (byte)n, 0, CmdType.CheatGold, gold);
    public static Cmd CheatEra(int n, int era) => new(0, (byte)n, 0, CmdType.CheatEra, era);
    public static Cmd Choose(int n, int option) => new(0, (byte)n, 0, CmdType.Choose, option);
    public static Cmd FoundCity(int n, int province) => new(0, (byte)n, 0, CmdType.FoundCity, province);
    public static Cmd Research(int n, int tech) => new(0, (byte)n, 0, CmdType.Research, tech);
    public static Cmd CheatTech(int n, int tech) => new(0, (byte)n, 0, CmdType.CheatTech, tech);
    public static Cmd TribeTo(int n, int province) => new(0, (byte)n, 0, CmdType.TribeTo, province);
    public static Cmd Settle(int n, int myth) => new(0, (byte)n, 0, CmdType.Settle, myth);
    public static Cmd Edict(int n, int edict, bool on) => new(0, (byte)n, 0, CmdType.Edict, edict, on ? 1 : 0);

    public bool IsSession => Type is CmdType.Pause or CmdType.Unpause or CmdType.SetSpeed;

    /// <summary>One journal line: «tick nation seq Type a b c».</summary>
    public string ToLine() => string.Create(CultureInfo.InvariantCulture, $"{Tick} {Nation} {Seq} {Type} {A} {B} {C}");

    public static Cmd Parse(string line)
    {
        var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var ci = CultureInfo.InvariantCulture;
        return new Cmd(int.Parse(f[0], ci), byte.Parse(f[1], ci), ushort.Parse(f[2], ci), Enum.Parse<CmdType>(f[3]),
                       int.Parse(f[4], ci), int.Parse(f[5], ci), int.Parse(f[6], ci));
    }
}

/// <summary>
/// The one place where commands change the state. Validation runs again at execution (the state may have moved on
/// since the UI checked). Returns 0 on success, otherwise the refusal code of the matching rule
/// (ClaimError, BuildError, SurveyError or ScoutError as int).
/// </summary>
public static class Commands
{
    public const int BadCommand = 99;

    /// <param name="batch">When given, changed provinces are collected there and the caller raises the events and
    /// refreshes the fog once (the rules cycle); otherwise they are raised here.</param>
    public static int Apply(WorldData w, GameState s, in Cmd c, ISimSink sink, List<int> batch = null)
    {
        int n = c.Nation;
        if (n >= s.Nat.Length) return BadCommand;
        switch (c.Type)
        {
            case CmdType.Claim:
            {
                var e = Rules.CheckClaim(w, s, c.A, n);
                if (e != ClaimError.None) return (int)e;
                Rules.Claim(w, s, c.A, n);
                Character.OnClaim(w, s, c.A, n);
                Changed(w, s, c.A, sink, batch, fog: true);
                return 0;
            }
            case CmdType.Build:
            {
                var e = Rules.CheckBuild(w, s, c.A, (Bld)c.B, n);
                if (e != BuildError.None) return (int)e;
                Rules.Build(s, c.A, (Bld)c.B, n);
                Character.OnBuild(s, n, (Bld)c.B);
                Simulation.SyncQueue(s, n);   // building the capital's current project by hand moves its queue on
                Changed(w, s, c.A, sink, batch, fog: false);
                return 0;
            }
            case CmdType.Survey:
            {
                var e = Rules.CheckSurvey(s, c.A, n);
                if (e != SurveyError.None) return (int)e;
                Rules.Survey(s, c.A, n);
                Character.OnSurvey(s, n);
                Changed(w, s, c.A, sink, batch, fog: false);
                return 0;
            }
            case CmdType.ScoutTo:
                if (c.A < 0 || c.A >= w.P) return (int)ScoutError.Sea;
                return (int)Scouts.Send(w, s, n, c.A, sink, out _);
            case CmdType.ScoutAuto:
                return (int)Scouts.Send(w, s, n, -1, sink, out _);
            case CmdType.Pause: s.Paused = true; return 0;
            case CmdType.Unpause: s.Paused = false; return 0;
            case CmdType.SetSpeed: s.Speed = IntMath.Clamp(c.A, Clock.MinSpeed, Clock.MaxSpeed); return 0;
            case CmdType.CheatGold: s.Nat[n].Treasury += (long)c.A * Rules.Cents; return 0;
            case CmdType.CheatEra: Simulation.JumpToEra(s, c.A); return 0;
            case CmdType.FoundCity:
            {
                var e = Cities.Check(w, s, c.A, n);
                if (e != FoundError.None) return (int)e;
                Cities.Found(w, s, c.A, n);
                Character.OnFound(s, n);
                Changed(w, s, c.A, sink, batch, fog: true);
                return 0;
            }
            case CmdType.Choose: return s.Events != null && s.Events.Choose(s, n, c.A, sink) ? 0 : BadCommand;
            case CmdType.Research: return Techs.Choose(s.Nat[n], c.A) ? 0 : BadCommand;
            case CmdType.TribeTo:
                if (c.A < 0)
                {
                    if (s.Nat[n].Camp < 0) return (int)TribeMoveError.Settled;
                    Nomads.Halt(s.Nat[n]);
                    return 0;
                }
                return (int)Nomads.MoveTo(w, s, n, c.A);
            case CmdType.Settle:
            {
                var e = Nomads.CheckSettle(w, s, n, s.Nat[n].Camp);
                if (e != SettleError.None) return (int)e;
                List<int> got = null;
                Nomads.Found(w, s, n, c.A, sink, ref got);
                if (batch != null) { batch.AddRange(got); return 0; }
                sink?.RaiseProvincesChanged(got.ToArray());
                FogOfWar.Refresh(w, s, sink);
                return 0;
            }
            case CmdType.CheatTech:
            {
                var nat = s.Nat[n];
                if (c.A >= 0 && c.A < Techs.Count) Techs.Learn(nat, c.A);
                else if (c.A < 0) { for (int t = 0; t < Techs.Count; t++) if (!Techs.ForkClosed(nat, t)) Techs.Learn(nat, t); }   // of a fork its first open path
                else return BadCommand;
                return 0;
            }
            case CmdType.Edict:
            {
                var e = Policy.CheckSet(s.Nat[n], c.A, c.B != 0);
                if (e != EdictError.None) return (int)e;
                Policy.Set(s.Nat[n], c.A, c.B != 0);
                return 0;
            }
            default: return BadCommand;
        }
    }

    static void Changed(WorldData w, GameState s, int p, ISimSink sink, List<int> batch, bool fog)
    {
        if (batch != null) { batch.Add(p); return; }
        sink?.RaiseProvincesChanged(new[] { p });
        if (fog) FogOfWar.Refresh(w, s, sink);
    }
}

/// <summary>
/// The command pipeline: every state-changing order of a human (or the console) is submitted here, stamped with the
/// next tick boundary and a per-nation sequence number, executed at that boundary in (tick, nation, seq) order and
/// written to the journal. Single player is a network of one: its boundary is «now», so a command runs at once —
/// also while paused. Replaying the journal on the same world reproduces the same state (<see cref="Replay"/>).
/// </summary>
public sealed class CommandQueue
{
    readonly List<Cmd> _pending = new();
    readonly List<Cmd> _journal = new();
    readonly ushort[] _seq = new ushort[256];

    public IReadOnlyList<Cmd> Journal => _journal;
    public int PendingCount => _pending.Count;

    /// <summary>Called for every executed command with its result (0 = done).</summary>
    public Action<Cmd, int> Executed;

    public void Clear() { _pending.Clear(); _journal.Clear(); Array.Clear(_seq); }

    /// <summary>Per-nation sequence counters (a save keeps them with the journal).</summary>
    public ReadOnlySpan<ushort> Sequences => _seq;

    /// <summary>A loaded game: its journal and sequence counters come back, nothing is pending.</summary>
    public void Restore(IEnumerable<Cmd> journal, ReadOnlySpan<ushort> seq)
    {
        Clear();
        _journal.AddRange(journal);
        seq[..Math.Min(seq.Length, _seq.Length)].CopyTo(_seq);
    }

    /// <summary>Stamp and queue a command for the next tick boundary.</summary>
    public Cmd Submit(GameState s, Cmd c)
    {
        c = c with { Tick = (int)s.Tick, Seq = ++_seq[c.Nation] };
        _pending.Add(c);
        return c;
    }

    /// <summary>Execute the commands due at the current boundary. Returns the result of the last one (0 if none).</summary>
    public int Flush(WorldData w, GameState s, ISimSink sink)
    {
        if (_pending.Count == 0) return 0;
        if (_pending.Count > 1) _pending.Sort(Order);
        int last = 0, k = 0;
        for (; k < _pending.Count && _pending[k].Tick <= s.Tick; k++)
        {
            var c = _pending[k] with { Tick = (int)s.Tick };   // a late command runs now and is journaled when it ran
            last = Commands.Apply(w, s, c, sink);
            _journal.Add(c);
            Executed?.Invoke(c, last);
        }
        _pending.RemoveRange(0, k);
        return last;
    }

    /// <summary>Run ticks: at every boundary the due commands first, then the tick itself.</summary>
    public TickReport Run(WorldData w, GameState s, int ticks, ISimSink sink)
    {
        var r = new TickReport();
        for (int t = 0; t < ticks; t++)
        {
            Flush(w, s, sink);
            r.Add(Simulation.Step(w, s, sink));
        }
        return r;
    }

    static int Order(Cmd a, Cmd b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) : a.Nation != b.Nation ? a.Nation.CompareTo(b.Nation) : a.Seq.CompareTo(b.Seq);
}

/// <summary>Re-runs a game from its initial state and journal: the base of replays, desync hunts and rejoin.</summary>
public static class Replay
{
    /// <summary>Advance s to tick `until`, applying the journal's commands at their boundaries (and those due at `until`).</summary>
    public static TickReport Run(WorldData w, GameState s, IReadOnlyList<Cmd> journal, long until, ISimSink sink)
    {
        var r = new TickReport();
        int k = 0;
        while (k < journal.Count && journal[k].Tick < s.Tick) k++;
        while (true)
        {
            while (k < journal.Count && journal[k].Tick == s.Tick) Commands.Apply(w, s, journal[k++], sink);
            if (s.Tick >= until) break;
            r.Add(Simulation.Step(w, s, sink));
        }
        return r;
    }
}
