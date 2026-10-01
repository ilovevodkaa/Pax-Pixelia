using System;
using System.Collections.Generic;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

public enum UnrestStage : byte { Calm, Grumbling, Unrest, Revolt }

public enum ReliefError { None, NotOwned, Calm, NoGold }

/// <summary>
/// Unrest grows in steps (GDD 9.6), from the province's mood: below 40 people grumble (taxes −25%), below 25 they strike
/// (no taxes, no materials, the city stops growing), below 15 they rise. A rising lasting <see cref="RevoltAt"/> cycles
/// (a minute at speed 3) ends in secession: the province leaves the realm — a town takes the restless part of its land
/// with it — and becomes free land again, its buildings standing for whoever claims it next. A capital does not secede:
/// it riots and the treasury is looted. «Раздать хлеб» buys calm for gold. An empty treasury is a crisis of its own: the
/// paid edicts are repealed and the people, unpaid, sulk (mood target −10) while it lasts. The plague is the crisis that
/// comes by itself: it breaks out in a crowded province, creeps over borders, kills and embitters; the «Карантин» edict
/// slows it and heals it twice as fast. Pure C#, integers.
/// </summary>
public static class Unrest
{
    public const int GrumbleBelow = 40, UnrestBelow = 25, RevoltBelow = 15;
    /// <summary>Cycles of rising before a province secedes; the counter falls twice as fast once the mood is back over 15.</summary>
    public const int RevoltAt = 120;
    /// <summary>Taxes of a grumbling province, %.</summary>
    public const int GrumbleTaxPct = 75;
    /// <summary>The mood target of a nation in debt falls by this much.</summary>
    public const int DebtMood = 10;
    /// <summary>A capital in riot loses this share of the treasury, %.</summary>
    public const int RiotLootPct = 25;
    /// <summary>«Раздать хлеб»: mood in the province and in its own neighbours.</summary>
    public const int ReliefMood = 15, ReliefNeighbourMood = 6;

    public static UnrestStage Stage(int mood) => mood >= GrumbleBelow ? UnrestStage.Calm : mood >= UnrestBelow ? UnrestStage.Grumbling
                                               : mood >= RevoltBelow ? UnrestStage.Unrest : UnrestStage.Revolt;

    public static UnrestStage StageOf(GameState s, int p) => s.Owner[p] < 0 ? UnrestStage.Calm : Stage(s.Mood[p]);

    /// <summary>‰ of p's normal taxes it still pays.</summary>
    public static int TaxPermille(GameState s, int p) => StageOf(s, p) switch
    {
        UnrestStage.Calm => 1000,
        UnrestStage.Grumbling => GrumbleTaxPct * 10,
        _ => 0,
    };

    /// <summary>Do p's workshops and mills work (no strike)?</summary>
    public static bool Works(GameState s, int p) => StageOf(s, p) < UnrestStage.Unrest;

    public static bool InDebt(NationState nat) => nat.Treasury < 0;

    public static string StageName(UnrestStage st) => st switch
    {
        UnrestStage.Grumbling => "Ворчание",
        UnrestStage.Unrest => "Волнения",
        UnrestStage.Revolt => "Мятеж",
        _ => "Спокойно",
    };

    public static string StageEffect(UnrestStage st) => st switch
    {
        UnrestStage.Grumbling => $"налоги −{100 - GrumbleTaxPct}%",
        UnrestStage.Unrest => "ни налогов, ни материалов, город не растёт",
        UnrestStage.Revolt => "ни налогов, ни материалов; если не успокоить — провинция отложится",
        _ => "",
    };

    // ---------------------------------------------------------------- relief

    /// <summary>Gold «Раздать хлеб» costs in p: 30 + 1 per thousand people, a quarter dearer every era.</summary>
    public static int ReliefPrice(GameState s, int p) =>
        (int)((30L + s.Pop[p] / 1000) * Policy.EraPermille(s.Owner[p] >= 0 ? s.Nat[s.Owner[p]].Era : 0) / 1000);

    public static ReliefError CheckRelief(GameState s, int p, int n)
    {
        if (p < 0 || p >= s.Owner.Length || s.Owner[p] != n) return ReliefError.NotOwned;
        if (s.Mood[p] >= GrumbleBelow + 10) return ReliefError.Calm;
        if (s.Nat[n].Treasury < ReliefPrice(s, p) * Rules.Cents) return ReliefError.NoGold;
        return ReliefError.None;
    }

    public static void Relief(WorldData w, GameState s, int p, int n)
    {
        s.Nat[n].Treasury -= ReliefPrice(s, p) * Rules.Cents;
        s.Mood[p] = (byte)Math.Min(100, s.Mood[p] + ReliefMood);
        s.Unrest[p] = (byte)Math.Max(0, s.Unrest[p] - RevoltAt / 2);
        foreach (int q in w.Adj[p])
            if (s.Owner[q] == n) s.Mood[q] = (byte)Math.Min(100, s.Mood[q] + ReliefNeighbourMood);
    }

    // ---------------------------------------------------------------- the cycle

    public static void Init(WorldData w, GameState s)
    {
        if (s.Unrest == null || s.Unrest.Length != w.P) s.Unrest = new byte[w.P];
        if (s.Plague == null || s.Plague.Length != w.P) s.Plague = new byte[w.P];
    }

    /// <summary>One rules cycle after the moods: risings count up (and down), a full count secedes; the human hears when a
    /// province starts rising and when it is half-way.</summary>
    public static void Cycle(WorldData w, GameState s, ISimSink sink, ref List<int> changed)
    {
        if (s.Unrest == null) return;
        List<int> seceding = null;
        for (int p = 0; p < w.P; p++)
        {
            int o = s.Owner[p];
            if (o < 0) { s.Unrest[p] = 0; continue; }
            int u = s.Unrest[p];
            if (s.Mood[p] < RevoltBelow)
            {
                u++;
                if (u == 1 && s.Nat[o].Human)
                    sink?.Notify("alert-triangle", $"В провинции {w.PName[p]} мятеж! Если не успокоить людей, через минуту она отложится");
                else if (u == RevoltAt / 2 && s.Nat[o].Human)
                    sink?.Notify("alert-triangle", $"Мятеж в провинции {w.PName[p]} разгорается: полминуты до отделения. Раздайте хлеб или объявите праздники");
                if (u >= RevoltAt) { (seceding ??= new List<int>()).Add(p); u = 0; }
            }
            else u = Math.Max(0, u - 2);
            s.Unrest[p] = (byte)u;
        }
        if (seceding == null) return;
        foreach (int p in seceding)
            if (s.Owner[p] >= 0) Secede(w, s, p, sink, ref changed);
    }

    /// <summary>p rises: a capital riots (the treasury is looted), a town leaves with its restless land, a province alone.</summary>
    static void Secede(WorldData w, GameState s, int p, ISimSink sink, ref List<int> changed)
    {
        int n = s.Owner[p];
        var nat = s.Nat[n];
        if (s.CapitalOf[p] >= 0)
        {
            long loot = Math.Max(0, nat.Treasury) * RiotLootPct / 100;
            nat.Treasury -= loot;
            s.Mood[p] = (byte)Math.Max((int)s.Mood[p], RevoltBelow + 10);   // the riot burns out
            if (nat.Human) sink?.Notify("alert-triangle", $"Бунт в столице {w.PName[p]}: толпа разграбила казну (−{loot / Rules.Cents} золота). Гнев выгорел, но не забыт");
            return;
        }
        var gone = new List<int> { p };
        bool town = Cities.IsCity(s, p);
        if (town)
            for (int q = 0; q < w.P; q++)
                if (q != p && s.Owner[q] == n && s.City != null && s.City[q] == p && s.Mood[q] < GrumbleBelow) gone.Add(q);
        foreach (int q in gone)
        {
            s.Owner[q] = s.Controller[q] = -1;
            if (s.City != null) { s.City[q] = -1; s.Growth[q] = 0; s.SphereNoted[q] = false; }
            s.IsTown[q] = false;
            s.Mood[q] = 55;
            s.Unrest[q] = 0;
        }
        if (s.City != null) Cities.Reassign(w, s, n);   // the loyal land of a lost town goes to the nearest city left
        (changed ??= new List<int>()).AddRange(gone);
        if (sink == null) return;
        if (nat.Human)
            sink.Notify("alert-triangle", town
                ? $"Город {w.PName[p]} отложился от державы и увёл {gone.Count - 1} {Ru.Plural(gone.Count - 1, "провинцию", "провинции", "провинций")}. Их можно вернуть, но люди помнят"
                : $"Провинция {w.PName[p]} отложилась от державы. Её можно присоединить снова");
        else
            for (int h = 0; h < s.Nat.Length; h++)
                if (s.Nat[h].Human && Rules.Met(s, h, n))
                    sink.Notify("alert-triangle", $"У державы {s.Nations[n].Name} отложилась провинция {w.PName[p]}");
    }

    /// <summary>The budget left nation n's treasury empty this cycle: the paid edicts are repealed (the human is told).</summary>
    public static void OnDebt(GameState s, int n, ISimSink sink)
    {
        var nat = s.Nat[n];
        bool repealed = false;
        for (int e = 0; e < Policy.Count; e++)
            if (Policy.On(nat, e) && Policy.Edicts[e].CostPct > 0) { Policy.Set(nat, e, false); repealed = true; }
        if (nat.Human)
            sink?.Notify("coins", "Казна пуста! Жалованье не платят, люди ропщут (довольство −10)" + (repealed ? ". Платные указы отменены" : ""));
    }

    // ---------------------------------------------------------------- the plague (a crisis that comes by itself)

    /// <summary>A province stays sick this many cycles (2 min at speed 3); Plague[p] counts it down in steps of 4 cycles.
    /// After it the province is immune: Plague[p] in ImmuneBase..255 counts down one every <see cref="ImmuneStep"/> cycles
    /// (≈ 25 min), so a plague burns out instead of circling the world.</summary>
    public const int PlagueCycles = 240, PlagueStep = 4, ImmuneBase = 64, ImmuneStep = 16;
    /// <summary>While sick: the mood target falls by this much, nobody is born and ‰ of the people die every cycle (a third over an outbreak).</summary>
    public const int PlagueMood = 20, PlagueDeathPermille = 2;
    /// <summary>Every <see cref="PlagueStep"/> cycles a sick province infects each healthy land neighbour with this chance, ‰
    /// (a quarter under quarantine): three or four new provinces per sick one, under one under quarantine.</summary>
    public const int SpreadPermille = 12;
    /// <summary>An outbreak is rolled every 30 cycles per settled nation of 10+ provinces from Древний мир on: 1 in 160 (≈ once in 40 min).</summary>
    public const int OutbreakEvery = 30, OutbreakOdds = 160, OutbreakMinProvinces = 10;

    public static bool Sick(GameState s, int p) => s.Plague != null && s.Plague[p] > 0 && s.Plague[p] < ImmuneBase;
    public static bool Immune(GameState s, int p) => s.Plague != null && s.Plague[p] >= ImmuneBase;

    static bool Quarantined(GameState s, int p) => s.Owner[p] >= 0 && Policy.On(s.Nat[s.Owner[p]], Policy.Index("quarantine"));

    /// <summary>Sickness: outbreaks, spreading, deaths, healing. Called every rules cycle before the risings are counted.</summary>
    public static void Plague(WorldData w, GameState s, int cycle, ISimSink sink)
    {
        if (s.Plague == null) return;
        int nN = s.Nat.Length;
        if (cycle % OutbreakEvery == 0)
            for (int n = 0; n < nN; n++)
            {
                var nat = s.Nat[n];
                if (nat.Era < 1 || Nomads.IsNomad(nat) || !SimRng.Chance(w.Seed, 95, n, cycle, 1, OutbreakOdds)) continue;
                int best = -1, bp = 0, prov = 0;
                for (int q = 0; q < w.P; q++)
                {
                    if (s.Owner[q] != n) continue;
                    prov++;
                    if (s.Pop[q] > bp && s.Plague[q] == 0) { bp = s.Pop[q]; best = q; }   // crowded places catch it first (not the immune)
                }
                if (best < 0 || prov < OutbreakMinProvinces) continue;
                s.Plague[best] = PlagueCycles / PlagueStep;
                Tell(w, s, best, sink, $"Мор в провинции {w.PName[best]}! Люди умирают и злятся. Карантин остановит заразу");
            }
        bool step = cycle % PlagueStep == 0, immuneStep = cycle % ImmuneStep == 0;
        for (int q = 0; q < w.P; q++)
        {
            byte v = s.Plague[q];
            if (v == 0) continue;
            if (v >= ImmuneBase)
            {
                if (immuneStep) s.Plague[q] = v == ImmuneBase ? (byte)0 : (byte)(v - 1);
                continue;
            }
            s.Pop[q] = (int)Math.Max(10, s.Pop[q] - (long)s.Pop[q] * PlagueDeathPermille / 1000);
            if (!step) continue;
            bool qq = Quarantined(s, q);
            foreach (int r in w.Adj[q])
            {
                if (w.PLand[r] != 1 || s.Plague[r] != 0) continue;
                int odds = SpreadPermille / (qq || Quarantined(s, r) ? 4 : 1);
                if (SimRng.Permille(w.Seed, 96, r, cycle) >= odds) continue;
                s.Plague[r] = PlagueCycles / PlagueStep + 1;   // +1: not counted down in this very step
                if (s.Owner[r] >= 0 && s.Owner[q] != s.Owner[r]) Tell(w, s, r, sink, $"Мор перешёл границу: заболела провинция {w.PName[r]}");
            }
        }
        if (!step) return;
        for (int q = 0; q < w.P; q++)
        {
            if (!Sick(s, q)) continue;
            int heal = Quarantined(s, q) ? 2 : 1;
            int left = s.Plague[q] - heal;
            if (left > 0) { s.Plague[q] = (byte)left; continue; }
            s.Plague[q] = 255;   // healed: immune for a long while
            if (s.Owner[q] >= 0 && s.Nat[s.Owner[q]].Human) sink?.Notify("sun", $"Мор в провинции {w.PName[q]} отступил");
        }
    }

    static void Tell(WorldData w, GameState s, int p, ISimSink sink, string text)
    {
        if (sink == null || s.Owner[p] < 0) return;
        if (s.Nat[s.Owner[p]].Human) { sink.Notify("alert-triangle", text); return; }
        for (int h = 0; h < s.Nat.Length; h++)   // a plague next door is news too
            if (s.Nat[h].Human)
                foreach (int q in w.Adj[p])
                    if (s.Owner[q] == h) { sink.Notify("alert-triangle", $"У соседей ({s.Nations[s.Owner[p]].Name}) мор: провинция {w.PName[p]} у самой нашей границы"); return; }
    }

    // ---------------------------------------------------------------- bots

    /// <summary>A bot looks after its restless provinces about every 10 s: bread where a rising is half-way to secession.</summary>
    public static void BotCare(WorldData w, GameState s, int n, int cycle)
    {
        if (!SimRng.Chance(w.Seed, 91, n, cycle, 1, 20)) return;
        for (int p = 0; p < w.P; p++)
        {
            if (s.Owner[p] != n || s.Unrest[p] < RevoltAt / 3) continue;
            if (CheckRelief(s, p, n) == ReliefError.None) Commands.Apply(w, s, Cmd.Relief(n, p), null);
        }
    }
}
