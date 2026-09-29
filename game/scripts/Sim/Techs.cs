using System;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

/// <summary>One technology: its era, cost in science points at «Обычная» (scaled by the pace like the eras), the
/// building it opens (if any), a short effect line and the tribe's own story of how it was found.</summary>
public sealed record TechDef(string Id, string Name, int Era, int Cost, Bld? Unlocks, string Icon, string Effect, string Lore);

/// <summary>
/// The tech tree (IDEAS A-2), era by era. Science still fills the era progress stock that drives the calendar
/// (<see cref="Eras"/>); the same points also go to the one technology a nation studies. A nation leaves an era only
/// with <see cref="Required"/> of its technologies known (Первобытная: 4 of 6), so the eras are earned, not waited out.
/// Techs are kept as a bit set (NationState.TechsDone) and per-tech points (NationState.TechPts); points made while
/// nothing is chosen wait in NationState.TechPool and go to the next choice, so a slow click loses nothing.
/// </summary>
public static class Techs
{
    public static readonly TechDef[] All =
    {
        new("wild_grain", "Дикие злаки", 0, 3000, Bld.Farm, "plant",
            "Открывает ферму",
            "Кто-то рассыпал зёрна у стоянки, а весной там выросла еда. Совпадение? Старейшины решили, что нет."),
        new("stone_axe", "Каменный топор", 0, 3000, Bld.Lumber, "trees",
            "Открывает лесопилку",
            "Острый камень на крепкой палке. Деревья впервые боятся людей."),
        new("flint", "Кремень", 0, 3000, Bld.Quarry, "pick",
            "Открывает каменоломню и геологов",
            "Один камень высекает искру из другого. Тот, кто это заметил, три дня ходил гордый."),
        new("harpoon", "Острога и сеть", 0, 3000, Bld.Fishery, "anchor",
            "Открывает рыбацкую пристань",
            "Рыба долго считала реку своей. Потом кто-то сплёл сеть."),
        new("taming", "Приручение", 0, 3000, Bld.Pasture, "paw",
            "Открывает пастбище",
            "Волчонок остался у костра и не ушёл. Козы пришли сами — посмотреть на волчонка."),
        new("ancestors", "Духи предков", 0, 3000, Bld.Shrine, "sun",
            "Открывает святилище: наука и довольство",
            "Шаман сказал, что предки смотрят. С тех пор у костра говорят тише и спорят реже."),
    };

    /// <summary>Technologies of an era a nation needs to leave it (only eras that already have a tree are gated).</summary>
    public static int Required(int era) => era == 0 ? 4 : 0;

    /// <summary>Buildings with no technology yet (Амбар, Рынок) open with the era that brings them.</summary>
    public static int BuildingEra(Bld b) => b is Bld.Granary or Bld.Market ? 1 : 0;

    /// <summary>The technology that opens geologists (Кремень).</summary>
    public const int SurveyTech = 2;

    public static int Count => All.Length;
    public static long AllMask => (1L << All.Length) - 1;

    public static int Cost(int t, int pace) => (int)Math.Max(1, (long)All[t].Cost * Eras.ClampPace(pace) / 1000);

    public static bool Known(NationState nat, int t) => (uint)t < (uint)All.Length && (nat.TechsDone & (1L << t)) != 0;

    /// <summary>The technology that opens building b, or -1 when b needs none.</summary>
    public static int For(Bld b)
    {
        for (int t = 0; t < All.Length; t++) if (All[t].Unlocks == b) return t;
        return -1;
    }

    /// <summary>Can nation n build b as far as knowledge goes (its technology, or the era that brings it)?</summary>
    public static bool Allows(NationState nat, Bld b)
    {
        int t = For(b);
        return (t < 0 || Known(nat, t)) && nat.Era >= BuildingEra(b);
    }

    public static int KnownIn(NationState nat, int era)
    {
        int k = 0;
        for (int t = 0; t < All.Length; t++) if (All[t].Era == era && Known(nat, t)) k++;
        return k;
    }

    public static int CountIn(int era)
    {
        int k = 0;
        foreach (var d in All) if (d.Era == era) k++;
        return k;
    }

    /// <summary>The highest era nation n's knowledge lets it enter: it may leave era e only with Required(e) of its techs.</summary>
    public static int EraCap(NationState nat)
    {
        for (int e = 0; e < Eras.Last; e++) if (KnownIn(nat, e) < Required(e)) return e;
        return Eras.Last;
    }

    /// <summary>Can nation n start studying t now: not known yet and of its era or earlier.</summary>
    public static bool Open(NationState nat, int t) => (uint)t < (uint)All.Length && !Known(nat, t) && All[t].Era <= nat.Era;

    /// <summary>Start (or switch to) t: points already put into t stay, the waiting pool joins it.</summary>
    public static bool Choose(NationState nat, int t)
    {
        if (!Open(nat, t)) return false;
        nat.Researching = t;
        nat.TechPts[t] += nat.TechPool;
        nat.TechPool = 0;
        return true;
    }

    /// <summary>Everything of the eras before `era` becomes known (debug era jumps; the pool and choice are kept).</summary>
    public static void GrantBefore(NationState nat, int era)
    {
        for (int t = 0; t < All.Length; t++) if (All[t].Era < era) Learn(nat, t);
    }

    internal static void Learn(NationState nat, int t)
    {
        nat.TechsDone |= 1L << t;
        nat.TechPts[t] = 0;
        if (nat.Researching == t) nat.Researching = -1;
    }

    /// <summary>A bot's next study: the open technology its own salted order likes best (so bots differ), or -1.</summary>
    public static int BotPick(int seed, int n, NationState nat)
    {
        int best = -1, bs = -1;
        for (int t = 0; t < All.Length; t++)
        {
            if (!Open(nat, t)) continue;
            int sc = SimRng.Permille(seed, 41, n, t);
            if (sc > bs) { bs = sc; best = t; }
        }
        return best;
    }

    /// <summary>
    /// Add a cycle's science to nation n's study. Returns the technology it finished this cycle, or -1. Bots choose by
    /// themselves; a human with nothing chosen banks the points in the pool.
    /// </summary>
    internal static int Advance(int seed, int n, NationState nat, int points, int pace)
    {
        if (nat.Researching < 0 && nat.Control == NationControl.Bot)
        {
            int pick = BotPick(seed, n, nat);
            if (pick >= 0) Choose(nat, pick);
        }
        int r = nat.Researching;
        if (r < 0)
        {
            if (HasOpen(nat)) nat.TechPool += points;   // nothing chosen yet: the points wait
            return -1;
        }
        nat.TechPts[r] += points;
        if (nat.TechPts[r] < Cost(r, pace)) return -1;
        long spare = nat.TechPts[r] - Cost(r, pace);
        Learn(nat, r);
        nat.TechPool += spare;                          // the overflow goes on to the next choice
        return r;
    }

    public static bool HasOpen(NationState nat)
    {
        for (int t = 0; t < All.Length; t++) if (Open(nat, t)) return true;
        return false;
    }
}
