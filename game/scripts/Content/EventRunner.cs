using System;

namespace PaxPixelia.Content;

/// <summary>
/// The deck of fates (IDEAS C-1), deterministic and engine-free. Call Tick for every nation on every sim tick:
/// between metronome beats it is a few comparisons. On a beat it deals one card by weight from the events whose
/// era, conditions, cooldown and subject fit, applies chronicle cards at once and opens choice cards (a bot answers
/// by AI weights; a human has ChoiceDeadline cycles, then the safe option 0 is taken). Follow-ups and triggers
/// fire outside the metronome. Durations in data are cycles (0.5 s at speed 3); TicksPerMinuteAtSpeed3 converts
/// them to sim ticks, whatever real length the simulation gives its tick.
/// Not thread-safe (shared scratch buffers): one runner per simulation.
/// </summary>
public sealed class EventRunner
{
    const int SaltPick = 1, SaltGap = 2, SaltChoiceGap = 3, SaltSubject = 4, SaltForeign = 5, SaltBranch = 6, SaltAi = 7, SaltThreads = 8;

    readonly ContentDb _db;
    readonly DeckDef _deck;
    readonly int _seed;
    readonly long[] _weight;
    readonly int[] _province, _foreign;

    /// <summary>Sim ticks per real minute at speed 3; 0 = one tick per content cycle (120 a minute).</summary>
    public int TicksPerMinuteAtSpeed3 { get; init; }
    /// <summary>Lobby «Летописец»: joke weight in percent — Серьёзный 0 / Обычный 100 / Балагур 200.</summary>
    public int JokePercent { get; init; } = 100;

    public ContentDb Db => _db;

    public EventRunner(ContentDb db, int seed)
    {
        _db = db;
        _deck = db.Deck;
        _seed = seed;
        _weight = new long[db.Events.Length];
        _province = new int[db.Events.Length];
        _foreign = new int[db.Events.Length];
    }

    public EventMemory NewMemory(int nation, bool bot, long tick = 0)
    {
        var m = new EventMemory(_db, nation, bot);
        m.NextDue = tick + Cycles(_deck.FirstEvent, ContentRng.Hash(_seed, nation, tick, SaltGap));
        m.NextChoice = m.NextDue + Cycles(_deck.ChoiceGap, ContentRng.Hash(_seed, nation, tick, SaltChoiceGap));
        // The same threads are guaranteed for every nation of a game: they become that game's running jokes.
        int threads = _db.Threads.Length;
        for (int k = 0, tries = 0; k < Math.Min(_deck.GuaranteedThreads, Math.Min(threads, 30)) && tries < 64; tries++)
        {
            int t = ContentRng.Below(ContentRng.Hash(_seed, -1, tries, SaltThreads), threads);
            if ((m.GuaranteedThreads & (1 << t)) != 0) continue;
            m.GuaranteedThreads |= 1 << t;
            k++;
        }
        return m;
    }

    /// <summary>Set or clear a memory flag from the simulation (technology learnt, ruler trait, world quirk…).</summary>
    public bool SetFlag(EventMemory m, string id, bool on = true)
    {
        int f = _db.FlagIndex(id);
        if (f < 0 || m.Flags[f] == on) return f >= 0;
        m.Flags[f] = on;
        m.FlagsDirty = true;
        return true;
    }

    public void Tick(EventMemory m, IEventState s, IEventSink sink, long tick)
    {
        if (m.Pending is { } open && tick >= open.Deadline) Resolve(m, s, sink, tick, open, 0);
        while (m.Queue.Count > 0 && m.Queue[0].At <= tick && RunFollowUp(m, s, sink, tick)) { }
        bool beat = tick >= m.NextDue;
        if (m.FlagsDirty || beat)
        {
            m.FlagsDirty = false;
            Triggers(m, s, sink, tick);
        }
        if (!beat) return;

        bool choice = tick >= m.NextChoice && m.Pending == null;
        int e = Deal(m, s, tick, choice);
        if (e < 0 && choice) { e = Deal(m, s, tick, false); choice = false; }
        if (e < 0) { m.NextDue = tick + Ticks(_deck.Retry); return; }

        Fire(m, s, sink, tick, _db.Events[e], _province[e], _foreign[e]);
        m.NextDue = tick + Cycles(_deck.Gap, ContentRng.Hash(_seed, m.Nation, tick, SaltGap));
        if (choice) m.NextChoice = tick + Cycles(_deck.ChoiceGap, ContentRng.Hash(_seed, m.Nation, tick, SaltChoiceGap));
    }

    /// <summary>Command ChooseOption: answer the open choice. False when there is none or the option is not offered.</summary>
    public bool Choose(EventMemory m, IEventState s, IEventSink sink, long tick, int option)
    {
        if (m.Pending is not { } open || !IsOffered(m, s, open.Event, option)) return false;
        Resolve(m, s, sink, tick, open, option);
        return true;
    }

    /// <summary>Whether option k of event e is shown to this nation now (option 0 always is).</summary>
    public bool IsOffered(EventMemory m, IEventState s, int e, int k)
    {
        var opts = _db.Events[e].Options;
        return k >= 0 && k < opts.Length && (k == 0 || Holds(opts[k].When, m, s, _db.Events[e].Subject));
    }

    /// <summary>
    /// Fire event e now whatever the metronome, era and cooldown say (world systems, the debug console); its
    /// conditions and subject must still hold and a one-shot stays spent. False if it cannot fire, also when a
    /// choice window is already open and e is a choice.
    /// </summary>
    public bool Force(EventMemory m, IEventState s, IEventSink sink, long tick, int e)
    {
        var ev = _db.Events[e];
        if ((ev.IsChoice && m.Pending != null) || m.ReadyAt[e] == long.MaxValue) return false;
        if (!Holds(ev.When, m, s, ev.Subject) || !PickSubject(ev, m, s, tick, out int p, out int f)) return false;
        Fire(m, s, sink, tick, ev, p, f);
        return true;
    }

    // ------------------------------------------------------------------ dealing

    int Deal(EventMemory m, IEventState s, long tick, bool choice)
    {
        long total = 0;
        var events = _db.Events;
        for (int i = 0; i < events.Length; i++)
        {
            _weight[i] = 0;
            var e = events[i];
            if (e.FollowUpOnly || e.Trigger || e.Kind == EventKind.World || e.IsChoice != choice) continue;
            if (!Eligible(e, m, s, tick)) continue;
            if (!PickSubject(e, m, s, tick, out _province[i], out _foreign[i])) continue;
            total += _weight[i] = Weight(e, m);
        }
        if (total <= 0) return -1;
        long r = (long)((ulong)ContentRng.Hash(_seed, m.Nation, tick, SaltPick) * (ulong)total >> 32);
        for (int i = 0; i < events.Length; i++)
            if ((r -= _weight[i]) < 0) return i;
        return -1;
    }

    bool Eligible(EventRt e, EventMemory m, IEventState s, long tick)
    {
        int era = s.Era;
        if (era < e.EraMin || era > e.EraMax || m.ReadyAt[e.Index] > tick) return false;
        if (e.Thread >= 0 && m.ThreadEra[e.Thread] == era) return false;   // one stage of a thread per era
        return Holds(e.When, m, s, e.Subject);
    }

    long Weight(EventRt e, EventMemory m)
    {
        long w = e.Weight * 100L;
        if (e.Tone == Tone.Joke)
        {
            w = w * JokePercent / 100;
            if (m.JokeStreak >= _deck.JokeStreakAfter) w = w * _deck.JokeStreakPercent / 100;
        }
        if (e.Thread >= 0)
        {
            if (e.Def.Stage > 1) w = w * _deck.ThreadBoostPercent / 100;
            else if ((m.GuaranteedThreads & (1 << e.Thread)) != 0) w = w * _deck.GuaranteedBoostPercent / 100;
        }
        if (e.BoostFlag != null)
            for (int k = 0; k < e.BoostFlag.Length; k++)
                if (m.Has(e.BoostFlag[k])) w = w * e.BoostPercent[k] / 100;
        return w;
    }

    bool Holds(CondRt c, EventMemory m, IEventState s, Subject subject)
    {
        if (c == CondRt.Always) return true;
        int y = s.Year;
        if (y < c.YearFrom || y > c.YearTo) return false;
        foreach (int f in c.Flags) if (!m.Has(f)) return false;
        foreach (int f in c.NotFlags) if (m.Has(f)) return false;
        if (c.AnyFlags.Length > 0 && !AnyOf(c.AnyFlags, m)) return false;
        for (int k = 0; k < c.MinStat.Length; k++) if (s.Stat(c.MinStat[k]) < c.MinValue[k]) return false;
        for (int k = 0; k < c.MaxStat.Length; k++) if (s.Stat(c.MaxStat[k]) > c.MaxValue[k]) return false;
        if (c.Buildings != null)
            for (int k = 0; k < c.Buildings.Length; k++) if (s.Buildings(c.Buildings[k]) < c.BuildingMin[k]) return false;
        if (c.Requires != null)
            foreach (var sys in c.Requires) if (!s.HasSystem(sys)) return false;
        // With a province subject the terrain picks the province itself (PickSubject); otherwise it is a land test.
        return c.Terrain == Terrain.None || subject == Subject.Province || s.PickProvince(c.Terrain, 0) >= 0;
    }

    static bool AnyOf(int[] flags, EventMemory m)
    {
        foreach (int f in flags) if (m.Has(f)) return true;
        return false;
    }

    bool PickSubject(EventRt e, EventMemory m, IEventState s, long tick, out int province, out int foreign)
    {
        province = -1; foreign = -1;
        switch (e.Subject)
        {
            case Subject.Province:
                province = s.PickProvince(e.When.Terrain, ContentRng.Hash(_seed, m.Nation, tick, SaltSubject + e.Index * 16));
                return province >= 0;
            case Subject.Capital:
                province = s.Capital;
                return province >= 0;
            case Subject.Foreign:
                foreign = s.PickForeign(ContentRng.Hash(_seed, m.Nation, tick, SaltForeign + e.Index * 16));
                return foreign >= 0;
            default:
                return true;
        }
    }

    void Triggers(EventMemory m, IEventState s, IEventSink sink, long tick)
    {
        var events = _db.Events;
        for (int i = 0; i < events.Length; i++)
        {
            var e = events[i];
            if (!e.Trigger || m.ReadyAt[i] > tick || (e.IsChoice && m.Pending != null)) continue;
            if (s.Era < e.EraMin || s.Era > e.EraMax || !Holds(e.When, m, s, e.Subject)) continue;
            if (PickSubject(e, m, s, tick, out int p, out int f)) Fire(m, s, sink, tick, e, p, f);
        }
    }

    /// <summary>Runs the head of the follow-up queue. False when it has to wait (a choice window is open).</summary>
    bool RunFollowUp(EventMemory m, IEventState s, IEventSink sink, long tick)
    {
        var q = m.Queue[0];
        var e = _db.Events[q.Event];
        if (e.IsChoice && m.Pending != null) return false;
        m.Queue.RemoveAt(0);
        if (Holds(e.When, m, s, e.Subject)) Fire(m, s, sink, tick, e, q.Province, q.Foreign);   // a lapsed chain just ends
        return true;
    }

    // ------------------------------------------------------------------ firing and effects

    void Fire(EventMemory m, IEventState s, IEventSink sink, long tick, EventRt e, int province, int foreign)
    {
        m.ReadyAt[e.Index] = e.Once ? long.MaxValue : tick + Ticks(e.Cooldown);
        m.FiredCount[e.Index]++;
        m.Total++;
        m.JokeStreak = e.Tone == Tone.Joke ? m.JokeStreak + 1 : 0;
        if (e.Thread >= 0)
        {
            m.ThreadEra[e.Thread] = s.Era;
            m.GuaranteedThreads &= ~(1 << e.Thread);
        }
        var r = new EventRecord(m.Nation, tick, e.Index, -1, -1, province, foreign, s.Era, s.Year, 0);
        if (!e.IsChoice)
        {
            Apply(m, s, sink, e.Effects, r, tick);
            sink.Resolved(r);
            return;
        }
        if (m.Bot)
        {
            Resolve(m, s, sink, tick, r, AiOption(m, s, r));
            return;
        }
        r = r with { Deadline = tick + Ticks(_deck.ChoiceDeadline) };
        m.Pending = r;
        sink.Fired(r);
    }

    /// <summary>The option a bot or the advisor takes: weighted by «ai» among the offered ones.</summary>
    public int AiOption(EventMemory m, IEventState s, in EventRecord r)
    {
        var opts = _db.Events[r.Event].Options;
        long total = 0;
        for (int k = 0; k < opts.Length; k++) if (IsOffered(m, s, r.Event, k)) total += opts[k].Ai;
        if (total <= 0) return 0;
        long x = (long)((ulong)ContentRng.Hash(_seed, m.Nation, r.Tick, SaltAi) * (ulong)total >> 32);
        for (int k = 0; k < opts.Length; k++)
            if (IsOffered(m, s, r.Event, k) && (x -= opts[k].Ai) < 0) return k;
        return 0;
    }

    void Resolve(EventMemory m, IEventState s, IEventSink sink, long tick, EventRecord r, int option)
    {
        if (m.Pending is { } open && open.Event == r.Event && open.Tick == r.Tick) m.Pending = null;
        var o = _db.Events[r.Event].Options[option];
        Apply(m, s, sink, o.Effects, r, tick);
        int branch = -1;
        if (o.RollPermille != null)
        {
            int x = ContentRng.Below(ContentRng.Hash(_seed, m.Nation, r.Tick, SaltBranch), 1000);
            branch = o.RollPermille.Length - 1;
            for (int k = 0; k < o.RollPermille.Length; k++)
                if ((x -= o.RollPermille[k]) < 0) { branch = k; break; }
            Apply(m, s, sink, o.RollEffects[branch], r, tick);
        }
        sink.Resolved(r with { Tick = tick, Option = option, Branch = branch });
    }

    void Apply(EventMemory m, IEventState s, IEventSink sink, EffectsRt fx, in EventRecord r, long tick)
    {
        if (!fx.Any) return;
        int n = m.Nation;
        int local = r.Province >= 0 ? r.Province : s.Capital;
        Add(sink, n, Resource.Gold, -1, fx.Gold);
        Add(sink, n, Resource.Mood, -1, fx.Mood);
        Add(sink, n, Resource.Mood, local, fx.MoodLocal);
        Add(sink, n, Resource.Mood, s.Capital, fx.MoodCapital);
        Add(sink, n, Resource.Pop, -1, fx.Pop);
        Add(sink, n, Resource.Pop, local, fx.PopLocal);
        Add(sink, n, Resource.Stability, -1, fx.Stability);
        Add(sink, n, Resource.Science, -1, fx.Science);
        Add(sink, n, Resource.Culture, -1, fx.Culture);
        Add(sink, n, Resource.Faith, -1, fx.Faith);
        Add(sink, n, Resource.Legacy, -1, fx.Legacy);
        Add(sink, n, Resource.Food, -1, fx.Food);
        foreach (int f in fx.SetFlags) if (!m.Flags[f]) { m.Flags[f] = true; m.FlagsDirty = true; }
        foreach (int f in fx.ClearFlags) if (m.Flags[f]) { m.Flags[f] = false; m.FlagsDirty = true; }
        if (fx.Axes != null)
            for (int k = 0; k < fx.Axes.Length; k++) sink.Axis(n, fx.Axes[k], fx.AxisDelta[k]);
        if (fx.Modifiers != null)
            foreach (var mod in fx.Modifiers)
                sink.Modifier(n, mod.Id, mod.Permille, (int)Math.Min(int.MaxValue, Ticks(mod.Cycles)), mod.Local ? local : -1);
        if (fx.Trait != null) sink.Trait(n, fx.Trait);
        if (fx.FollowUp >= 0)
            m.Schedule(new EventMemory.Scheduled(tick + Ticks(Math.Max(1, fx.FollowUpAfter)), fx.FollowUp, r.Province, r.Foreign));
    }

    static void Add(IEventSink sink, int n, Resource what, int province, int amount)
    {
        if (amount != 0) sink.Add(n, what, province, amount);
    }

    long Cycles(int[] range, uint roll) => Ticks(ContentRng.Between(roll, range[0], range[1]));

    /// <summary>Content cycles → sim ticks; a positive duration never rounds down to zero.</summary>
    public long Ticks(long cycles)
    {
        int perMinute = TicksPerMinuteAtSpeed3 > 0 ? TicksPerMinuteAtSpeed3 : _deck.CyclesPerMinuteAtSpeed3;
        long t = cycles * perMinute / _deck.CyclesPerMinuteAtSpeed3;
        return cycles > 0 ? Math.Max(1, t) : t;
    }
}
