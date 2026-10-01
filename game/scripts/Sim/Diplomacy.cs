using System;
using System.Collections.Generic;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

public enum DiploError { None, Unknown, Self, NoContact, NoGold, Disliked, Already, NotStronger, Refused, NothingToAnswer }

/// <summary>
/// Peaceful rivalry (no armies yet): the pull of the borders and the diplomacy of opinions, pacts and tribute.
/// <para>
/// The pull: an unhappy border province (mood under 40, not a city) next to a clearly happier neighbour (20 points
/// more on average over the neighbour's adjacent land) leans towards it; after <see cref="PullAt"/> cycles of leaning
/// (a minute) it goes over, people and buildings with it. A pact, tribute paid or the Great Wall protect a border; a
/// hostile neighbour (opinion ≤ −30) agitates and pulls twice as fast. So a happy realm grows by itself at the cost of
/// its neglectful neighbours — and loses land the same way.
/// </para>
/// <para>
/// Opinions (−100…100) drift towards a base: one faith +10 (else −5), a pact +25, paying tribute −15, receiving it +10.
/// Gifts raise them; refusing a demand or breaking a pact lowers them sharply. A pact needs both sides at 30+ and gives
/// each +3% taxes. Tribute is 10% of the payer's taxes, demanded from a nation half as strong: bots answer by the
/// balance of strength, a human gets a minute to pay or refuse.
/// </para>
/// Pure C#, integers, deterministic; the matrices are nN×nN (row = who, column = about whom).
/// </summary>
public static class Diplomacy
{
    public const int PullAt = 120, PullCheck = 8, PullMood = 40, PullGap = 20;
    public const int Hostile = -30, Friendly = 30;
    public const int PactTaxPermille = 30, MaxPacts = 5, TributePct = 10;
    public const int GiftOpinion = 15, RefuseOpinion = -40, BreakOpinion = -30, DemandWait = 120;
    public const int DriftCycles = 32, ContactCycles = 32;

    // ---------------------------------------------------------------- state

    public static void Init(GameState s)
    {
        int nN = s.Nat.Length;
        if (s.Opinion == null || s.Opinion.Length != nN * nN) { s.Opinion = new sbyte[nN * nN]; s.Pact = new bool[nN * nN]; }
        if (s.TributeTo == null || s.TributeTo.Length != nN)
        {
            s.TributeTo = new sbyte[nN]; Array.Fill(s.TributeTo, (sbyte)-1);
            s.DemandFrom = new sbyte[nN]; Array.Fill(s.DemandFrom, (sbyte)-1);
            s.DemandUntil = new int[nN];
        }
        if (s.Pull == null || s.Pull.Length != s.Owner.Length) s.Pull = new byte[s.Owner.Length];
    }

    public static bool IsBlank(GameState s)
    {
        if (s.Opinion == null) return true;
        foreach (sbyte o in s.Opinion) if (o != 0) return false;
        foreach (bool b in s.Pact) if (b) return false;
        foreach (sbyte t in s.TributeTo) if (t >= 0) return false;
        foreach (sbyte t in s.DemandFrom) if (t >= 0) return false;
        foreach (byte b in s.Pull) if (b != 0) return false;
        return true;
    }

    public static int Opinion(GameState s, int who, int about) => s.Opinion[who * s.Nat.Length + about];
    static void SetOpinion(GameState s, int who, int about, int v) => s.Opinion[who * s.Nat.Length + about] = (sbyte)IntMath.Clamp(v, -100, 100);
    public static void AddOpinion(GameState s, int who, int about, int d) => SetOpinion(s, who, about, Opinion(s, who, about) + d);
    public static bool HasPact(GameState s, int a, int b) => s.Pact[a * s.Nat.Length + b];
    static void SetPact(GameState s, int a, int b, bool on) { s.Pact[a * s.Nat.Length + b] = on; s.Pact[b * s.Nat.Length + a] = on; }
    public static int Pacts(GameState s, int n) { int k = 0; for (int m = 0; m < s.Nat.Length; m++) if (m != n && HasPact(s, n, m)) k++; return k; }

    public static string Mood(int opinion) => opinion <= Hostile ? "враждебны" : opinion < -5 ? "холодны" : opinion < Friendly ? "нейтральны" : opinion < 70 ? "дружелюбны" : "сердечны";

    /// <summary>Do a and b share a border (any adjacent provinces)?</summary>
    public static bool Borders(WorldData w, GameState s, int a, int b)
    {
        for (int p = 0; p < w.P; p++)
        {
            if (s.Owner[p] != a) continue;
            foreach (int q in w.Adj[p]) if (s.Owner[q] == b) return true;
        }
        return false;
    }

    /// <summary>Bit m: nation m shares a border with n (one pass over n's land; up to 64 nations).</summary>
    public static ulong Neighbours(WorldData w, GameState s, int n)
    {
        ulong mask = 0;
        for (int p = 0; p < w.P; p++)
        {
            if (s.Owner[p] != n) continue;
            foreach (int q in w.Adj[p]) { int m = s.Owner[q]; if (m >= 0 && m != n && m < 64) mask |= 1UL << m; }
        }
        return mask;
    }

    /// <summary>May a deal with b: a human knows them (fog), a bot only its neighbours.</summary>
    public static bool InContact(WorldData w, GameState s, int a, int b) =>
        a != b && (s.Nat[a].Human ? Rules.Met(s, a, b) : Borders(w, s, a, b));

    // ---------------------------------------------------------------- money: pacts and tribute in the budget

    /// <summary>Taxes nation n gains from its pacts (‰ of its own taxes).</summary>
    public static long PactBonus(GameState s, int n, long taxes) => taxes * Math.Min(MaxPacts, Pacts(s, n)) * PactTaxPermille / 1000;

    public static long TributeOf(long taxes) => taxes * TributePct / 100;

    // ---------------------------------------------------------------- prices

    /// <summary>Gold a gift to m costs: 25 cycles of m's taxes (at least 50) — rich nations want rich gifts.</summary>
    public static int GiftPrice(GameState s, int m) => (int)Math.Max(50, s.Nat[m].LastTaxes * 25 / Rules.Cents);

    // ---------------------------------------------------------------- actions (through Commands)

    public static DiploError CheckGift(WorldData w, GameState s, int n, int m)
    {
        if ((uint)m >= (uint)s.Nat.Length) return DiploError.Unknown;
        if (m == n) return DiploError.Self;
        if (!InContact(w, s, n, m)) return DiploError.NoContact;
        if (s.Nat[n].Treasury < GiftPrice(s, m) * Rules.Cents) return DiploError.NoGold;
        return DiploError.None;
    }

    public static void Gift(GameState s, int n, int m)
    {
        long g = GiftPrice(s, m) * Rules.Cents;
        s.Nat[n].Treasury -= g; s.Nat[m].Treasury += g;
        AddOpinion(s, m, n, GiftOpinion);
    }

    public static DiploError CheckPact(WorldData w, GameState s, int n, int m)
    {
        if ((uint)m >= (uint)s.Nat.Length) return DiploError.Unknown;
        if (m == n) return DiploError.Self;
        if (HasPact(s, n, m)) return DiploError.Already;
        if (!InContact(w, s, n, m)) return DiploError.NoContact;
        if (Opinion(s, m, n) < Friendly) return DiploError.Disliked;
        if (s.Nat[m].Human) return DiploError.Refused;   // humans propose, they are not proposed to (yet)
        return DiploError.None;
    }

    public static void MakePact(GameState s, int n, int m) => SetPact(s, n, m, true);

    public static void BreakPact(GameState s, int n, int m)
    {
        SetPact(s, n, m, false);
        AddOpinion(s, m, n, BreakOpinion);
    }

    /// <summary>Strength for tribute: the leaderboard score.</summary>
    static bool Stronger(GameState s, int a, int b, int permille) { var sc = Rules.Scores(s); return (long)sc[a] * 1000 >= (long)sc[b] * permille; }

    public static DiploError CheckDemand(WorldData w, GameState s, int n, int m)
    {
        if ((uint)m >= (uint)s.Nat.Length) return DiploError.Unknown;
        if (m == n) return DiploError.Self;
        if (!InContact(w, s, n, m)) return DiploError.NoContact;
        if (s.TributeTo[m] >= 0 || s.DemandFrom[m] >= 0 || s.TributeTo[n] == m) return DiploError.Already;
        if (!Stronger(s, n, m, 2000)) return DiploError.NotStronger;
        return DiploError.None;
    }

    /// <summary>n demands tribute from m. A bot answers at once (yes if n is more than twice as strong and it does not
    /// hate n); a human gets <see cref="DemandWait"/> cycles. Returns true if tribute is now paid.</summary>
    public static bool Demand(GameState s, int n, int m, int cycle, ISimSink sink)
    {
        if (s.Nat[m].Human)
        {
            s.DemandFrom[m] = (sbyte)n; s.DemandUntil[m] = cycle + DemandWait;
            sink?.Notify("affiliate", $"{s.Nations[n].Name} требует дань: {TributePct}% наших налогов. Откажем — их люди начнут мутить наши границы. Ответ — в «Дипломатии»");
            return false;
        }
        bool yes = Stronger(s, n, m, 2000) && Opinion(s, m, n) > -50;
        if (yes) s.TributeTo[m] = (sbyte)n; else AddOpinion(s, n, m, RefuseOpinion / 2);
        if (s.Nat[n].Human) sink?.Notify("affiliate", yes ? $"{s.Nations[m].Name} согласилась платить нам дань: {TributePct}% своих налогов" : $"{s.Nations[m].Name} отказалась платить дань");
        return yes;
    }

    /// <summary>The human answers a demand: pay, or refuse (the demander turns hostile and agitates the border).</summary>
    public static DiploError Answer(GameState s, int n, bool pay)
    {
        int d = s.DemandFrom[n];
        if (d < 0) return DiploError.NothingToAnswer;
        s.DemandFrom[n] = -1; s.DemandUntil[n] = 0;
        if (pay) s.TributeTo[n] = (sbyte)d;
        else SetOpinion(s, d, n, Math.Min(Opinion(s, d, n), 0) + RefuseOpinion);
        return DiploError.None;
    }

    /// <summary>The payer stops paying: the receiver takes it badly.</summary>
    public static DiploError StopTribute(GameState s, int n)
    {
        int r = s.TributeTo[n];
        if (r < 0) return DiploError.NothingToAnswer;
        s.TributeTo[n] = -1;
        AddOpinion(s, r, n, RefuseOpinion);
        return DiploError.None;
    }

    // ---------------------------------------------------------------- the cycle

    public static void Cycle(WorldData w, GameState s, int cycle, ISimSink sink, ref List<int> changed)
    {
        if (s.Opinion == null) return;
        int nN = s.Nat.Length;
        // a human who did not answer in time has refused
        for (int n = 0; n < nN; n++)
            if (s.DemandFrom[n] >= 0 && cycle >= s.DemandUntil[n])
            {
                int d = s.DemandFrom[n];
                Answer(s, n, false);
                if (s.Nat[n].Human) sink?.Notify("affiliate", $"Мы не ответили {s.Nations[d].Name}: это сочли отказом. Их люди мутят наши границы");
            }
        if (cycle % DriftCycles == 0) Drift(s);
        if (cycle % PullCheck == 0) Pull(w, s, sink, ref changed);
    }

    static int Base(GameState s, int a, int b)
    {
        int v = s.Nations[a].Religion == s.Nations[b].Religion ? 10 : -5;
        if (HasPact(s, a, b)) v += 25;
        if (s.TributeTo[a] == b) v -= 15;
        if (s.TributeTo[b] == a) v += 10;
        return v;
    }

    static void Drift(GameState s)
    {
        int nN = s.Nat.Length;
        for (int a = 0; a < nN; a++)
            for (int b = 0; b < nN; b++)
            {
                if (a == b) continue;
                int o = Opinion(s, a, b), t = Base(s, a, b);
                if (o != t) SetOpinion(s, a, b, o + Math.Sign(t - o));
            }
    }

    /// <summary>Whose border protects p's owner o against nation m: a pact, tribute paid to m, or the Great Wall.</summary>
    static bool Shielded(GameState s, int o, int m)
    {
        if (HasPact(s, o, m) || s.TributeTo[o] == m) return true;
        int wall = Wonders.Index("great_wall");
        return wall >= 0 && s.WonderOwner != null && s.WonderOwner[wall] == o;
    }

    static void Pull(WorldData w, GameState s, ISimSink sink, ref List<int> changed)
    {
        int nN = s.Nat.Length;
        Span<int> sum = stackalloc int[nN];
        Span<int> cnt = stackalloc int[nN];
        for (int p = 0; p < w.P; p++)
        {
            int o = s.Owner[p];
            if (o < 0 || s.Pull[p] == 0 && s.Mood[p] >= PullMood) continue;
            if (Cities.IsCity(s, p) || s.Mood[p] >= PullMood)
            {
                s.Pull[p] = (byte)Math.Max(0, s.Pull[p] - 2 * PullCheck);
                continue;
            }
            sum.Clear(); cnt.Clear();
            foreach (int q in w.Adj[p])
            {
                int m = s.Owner[q];
                if (m >= 0 && m != o) { sum[m] += s.Mood[q]; cnt[m]++; }
            }
            int best = -1, bm = 0;
            for (int m = 0; m < nN; m++)
            {
                if (cnt[m] == 0 || Shielded(s, o, m)) continue;
                int avg = sum[m] / cnt[m];
                if (avg - s.Mood[p] >= PullGap && avg > bm) { bm = avg; best = m; }
            }
            if (best < 0) { s.Pull[p] = (byte)Math.Max(0, s.Pull[p] - 2 * PullCheck); continue; }
            int step = Opinion(s, best, o) <= Hostile ? 2 * PullCheck : PullCheck;
            int was = s.Pull[p], now = Math.Min(PullAt, was + step);
            s.Pull[p] = (byte)now;
            if (was == 0 && s.Nat[o].Human) sink?.Notify("affiliate", $"Провинция {w.PName[p]} засматривается на соседей ({s.Nations[best].Name}): у них живут лучше. Поднимите довольство");
            if (now < PullAt) continue;
            GoOver(w, s, p, o, best, sink, ref changed);
        }
    }

    static void GoOver(WorldData w, GameState s, int p, int from, int to, ISimSink sink, ref List<int> changed)
    {
        s.Owner[p] = s.Controller[p] = (short)to;
        s.Pull[p] = 0;
        if (s.Unrest != null) s.Unrest[p] = 0;
        s.Mood[p] = (byte)Math.Max((int)s.Mood[p], PullMood);
        if (s.City != null)
        {
            s.City[p] = -1;
            Cities.Reassign(w, s, from);
            Cities.Reassign(w, s, to);
        }
        AddOpinion(s, from, to, -25);
        (changed ??= new List<int>()).Add(p);
        if (sink == null) return;
        if (s.Nat[from].Human) sink.Notify("affiliate", $"Провинция {w.PName[p]} ушла под руку державы {s.Nations[to].Name}: там лучше жилось");
        if (s.Nat[to].Human) sink.Notify("affiliate", $"Провинция {w.PName[p]} сама перешла к нам от {Ru.Genitive(s.Nations[from].Name)}: у нас лучше живётся");
    }

    // ---------------------------------------------------------------- bots

    /// <summary>A bot weighs its neighbours about every 30 s: pacts with friends, gifts to almost-friends, tribute from
    /// the much weaker, and it stops paying a nation it has outgrown.</summary>
    public static void BotAct(WorldData w, GameState s, int n, int cycle, ISimSink sink)
    {
        if (!SimRng.Chance(w.Seed, 101, n, cycle, 1, 60)) return;
        var nat = s.Nat[n];
        int payee = s.TributeTo[n];
        if (payee >= 0 && Stronger(s, n, payee, 1000)) { Commands.Apply(w, s, Cmd.StopTribute(n), sink); }
        ulong near = Neighbours(w, s, n);
        for (int m = 0; m < s.Nat.Length && m < 64; m++)
        {
            if (m == n || (near >> m & 1) == 0) continue;
            if (!HasPact(s, n, m) && !s.Nat[m].Human && Opinion(s, n, m) >= Friendly && Opinion(s, m, n) >= Friendly)
            {
                Commands.Apply(w, s, Cmd.Pact(n, m, true), sink);
                continue;
            }
            int o = Opinion(s, m, n);
            if (o >= 10 && o < Friendly && Opinion(s, n, m) >= 0 && nat.Treasury > 5L * GiftPrice(s, m) * Rules.Cents
                && SimRng.Chance(w.Seed, 102, n * 64 + m, cycle, 1, 3))
            {
                Commands.Apply(w, s, Cmd.Gift(n, m), sink);
                continue;
            }
            if (Opinion(s, n, m) < 20 && CheckDemand(w, s, n, m) == DiploError.None && SimRng.Chance(w.Seed, 103, n * 64 + m, cycle, 1, 8))
                Commands.Apply(w, s, Cmd.DemandTribute(n, m), sink);
        }
    }
}
