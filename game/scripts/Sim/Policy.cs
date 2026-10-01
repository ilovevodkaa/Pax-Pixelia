using System;
using System.Numerics;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

public enum EdictError { None, Unknown, TooEarly, NoSlot, NotOn }

/// <summary>
/// One edict: a standing order of the realm. Most cost a share of the taxes every rules cycle (<see cref="CostPct"/>);
/// two pay with the people's mood instead. Effects: mood target, taxes, research, city influence, materials — in %.
/// </summary>
public sealed record EdictDef(string Id, string Name, string Icon, int MinEra, int CostPct, int Mood, int TaxPct,
                              int ResearchPct, int InfluencePct, int MaterialsPct, string Effect, string Lore,
                              int CharScale, bool CharRight);

/// <summary>
/// How a realm is governed (pure C#, integers, deterministic): the administration limit with its overextension, prices
/// that grow with the era and the size of the realm, and the edicts. Together they are the gold sink: an empire that
/// grows past what it can govern pays for every province beyond the limit, its people grow restless and new land costs
/// more; the edicts turn a surplus into mood, knowledge, influence or materials — or a mood surplus into gold.
/// </summary>
public static class Policy
{
    public static readonly EdictDef[] Edicts =
    {
        new("feasts", "Праздники", "music", 0, 15, 6, 0, 0, 0, 0,
            "Довольство +6 во всех провинциях",
            "Костры до утра, мясо на всех. Старейшины ворчат, что раньше праздновали скромнее, и берут вторую порцию.",
            Character.Commune, false),
        new("sages", "Покровительство мудрецам", "book", 0, 20, 0, 0, 30, 0, 0,
            "Исследования +30%",
            "Тем, кто считает звёзды, носят еду. Звёзды от этого не меняются, а вот счёт идёт быстрее.",
            Character.Tradition, true),
        new("envoys", "Гонцы к племенам", "walk", 0, 15, 0, 0, 0, 35, 0,
            "Города растут быстрее: влияние +35%",
            "Гонцы несут соседям соль и рассказы о сытой жизни. Рассказы действуют сильнее соли.",
            Character.Openness, true),
        new("corvee", "Трудовая повинность", "hammer", 0, 0, -6, 0, 0, 0, 50,
            "Материалы +50%, довольство −6",
            "Каждый мужчина отдаёт роду десять дней в сезон. Камни таскают все, жалуются тоже все.",
            Character.Openness, false),
        new("levy", "Сбор податей", "coins", 0, 0, -8, 25, 0, 0, 0,
            "Налоги +25%, довольство −8",
            "Сборщик приходит с мешком и уходит с полным. Песни про него поют, но не добрые.",
            Character.Agri, true),
        new("quarantine", "Карантин", "ban", 1, 10, -2, 0, 0, 0, 0,
            "Мор переходит в наши земли и из них вчетверо реже и проходит вдвое быстрее; довольство −2",
            "Заставы на дорогах, костры у ворот, чужих не пускают. Своих, кстати, тоже не выпускают.",
            Character.Openness, false),
    };

    public static int Count => Edicts.Length;

    public static int Index(string id)
    {
        for (int e = 0; e < Edicts.Length; e++) if (Edicts[e].Id == id) return e;
        return -1;
    }

    /// <summary>Edicts in force at once: 1, +1 in Древний мир, Средневековье and Индустриальная.</summary>
    public static int Slots(int era) => 1 + (era >= 1 ? 1 : 0) + (era >= 3 ? 1 : 0) + (era >= 6 ? 1 : 0);

    public static bool On(NationState nat, int e) => (nat.Edicts >> e & 1) != 0;
    public static int Active(NationState nat) => BitOperations.PopCount((uint)nat.Edicts);

    public static EdictError CheckSet(NationState nat, int e, bool on)
    {
        if ((uint)e >= (uint)Edicts.Length) return EdictError.Unknown;
        if (!on) return On(nat, e) ? EdictError.None : EdictError.NotOn;
        if (On(nat, e)) return EdictError.None;
        if (nat.Era < Edicts[e].MinEra) return EdictError.TooEarly;
        if (Active(nat) >= Slots(nat.Era)) return EdictError.NoSlot;
        return EdictError.None;
    }

    public static void Set(NationState nat, int e, bool on)
    {
        if (on) nat.Edicts |= 1 << e; else nat.Edicts &= ~(1 << e);
    }

    // summed effects of the edicts in force
    public static int MoodOf(NationState nat) { int v = 0; for (int e = 0; e < Edicts.Length; e++) if (On(nat, e)) v += Edicts[e].Mood; return v; }
    public static int TaxPct(NationState nat) { int v = 0; for (int e = 0; e < Edicts.Length; e++) if (On(nat, e)) v += Edicts[e].TaxPct; return v; }
    public static int ResearchPct(NationState nat) { int v = 0; for (int e = 0; e < Edicts.Length; e++) if (On(nat, e)) v += Edicts[e].ResearchPct; return v; }
    public static int InfluencePct(NationState nat) { int v = 0; for (int e = 0; e < Edicts.Length; e++) if (On(nat, e)) v += Edicts[e].InfluencePct; return v; }
    public static int MaterialsPct(NationState nat) { int v = 0; for (int e = 0; e < Edicts.Length; e++) if (On(nat, e)) v += Edicts[e].MaterialsPct; return v; }
    public static int CostPct(NationState nat) { int v = 0; for (int e = 0; e < Edicts.Length; e++) if (On(nat, e)) v += Edicts[e].CostPct; return v; }

    /// <summary>Deed units (‰) an edict in force adds to its character scale every cycle: a people governed by festivals
    /// for ten minutes grows communal, by sages inventive, by envoys open, by the corvée self-reliant, by the levy mercantile.</summary>
    public const int EdictDeedPermille = 20;

    /// <summary>Every rules cycle, before the character is weighed: each edict in force is a small deed (Character).</summary>
    public static void Deeds(GameState s)
    {
        for (int n = 0; n < s.Nat.Length; n++)
        {
            int bits = s.Nat[n].Edicts;
            for (int e = 0; bits != 0 && e < Edicts.Length; e++, bits >>= 1)
                if ((bits & 1) != 0) Character.DeedPermille(s, n, Edicts[e].CharScale, Edicts[e].CharRight, EdictDeedPermille);
        }
    }

    /// <summary>What the edicts in force take from these taxes per cycle (hundredths).</summary>
    public static long EdictCost(NationState nat, long taxes) => taxes * CostPct(nat) / 100;

    // ---------------------------------------------------------------- administration

    /// <summary>Upkeep of governing one province per cycle, and of one beyond the limit on top of that (hundredths).</summary>
    public const long AdminPerProvince = 2, AdminPerOver = 10;
    /// <summary>The mood target falls by overextension % / 5, at most this much.</summary>
    public const int OverMoodMax = 15;

    /// <summary>
    /// Provinces a realm can govern without strain: 16, +4 per era, +4 per city (the capital counts), plus what its
    /// technologies add (Совет старейшин, Закон вождя, Письменность, Первые города).
    /// </summary>
    public static int AdminLimit(NationState nat, int cities) => 16 + 4 * nat.Era + 4 * Math.Max(1, cities) + Techs.Sum(nat, TechFx.AdminLimit);

    /// <summary>Provinces beyond the limit, in % of the limit (0 when within it).</summary>
    public static int OverPct(int provinces, int limit) => provinces <= limit ? 0 : (provinces - limit) * 100 / Math.Max(1, limit);

    /// <summary>Administration upkeep per cycle (hundredths): every province, and five times as much beyond the limit.</summary>
    public static long AdminUpkeep(int provinces, int limit) => provinces * AdminPerProvince + Math.Max(0, provinces - limit) * AdminPerOver;

    public static int OverMood(int overPct) => -Math.Min(OverMoodMax, overPct / 5);

    /// <summary>Price multiplier of an era, ‰: every era makes things a quarter dearer (×1 → ×3.75 by Будущее).</summary>
    public static int EraPermille(int era) => 1000 + 250 * era;

    // ---------------------------------------------------------------- the realm in numbers

    /// <summary>Provinces and cities of nation n (one pass over the map; the rules cycle keeps its own tallies).</summary>
    public static (int provinces, int cities) Size(GameState s, int n)
    {
        int prov = 0, cities = 0;
        for (int p = 0; p < s.Owner.Length; p++)
        {
            if (s.Owner[p] != n) continue;
            prov++;
            if (Cities.IsCity(s, p)) cities++;
        }
        return (prov, cities);
    }

    /// <summary>The administration of nation n now: provinces, limit, overextension %.</summary>
    public static (int provinces, int limit, int overPct) Admin(GameState s, int n)
    {
        var (prov, cities) = Size(s, n);
        int limit = AdminLimit(s.Nat[n], cities);
        return (prov, limit, OverPct(prov, limit));
    }

    /// <summary>The budget of nation n per cycle, line by line (hundredths): what the treasury tooltip and the policy card show.</summary>
    public readonly record struct Budget(long Taxes, long Buildings, long Admin, long Edicts, long Pacts = 0, long TributeIn = 0, long TributeOut = 0)
    {
        public long Upkeep => Buildings + Admin + Edicts + TributeOut;
        public long Net => Taxes + Pacts + TributeIn - Upkeep;
    }

    public static Budget BudgetOf(GameState s, int n)
    {
        long taxes = 0, bld = 0;
        int prov = 0, cities = 0;
        for (int p = 0; p < s.Owner.Length; p++)
        {
            if (s.Owner[p] != n) continue;
            taxes += Rules.ProvinceTax(s, p);
            bld += Rules.ProvinceUpkeep(s, p);
            prov++;
            if (Cities.IsCity(s, p)) cities++;
        }
        var nat = s.Nat[n];
        long pacts = s.Pact != null ? Diplomacy.PactBonus(s, n, taxes) : 0;
        long tOut = s.TributeTo != null && s.TributeTo[n] >= 0 ? Diplomacy.TributeOf(taxes + pacts) : 0, tIn = 0;
        if (s.TributeTo != null)
            for (int m = 0; m < s.Nat.Length; m++)
            {
                if (s.TributeTo[m] != n) continue;
                long tm = 0;
                for (int p = 0; p < s.Owner.Length; p++) if (s.Owner[p] == m) tm += Rules.ProvinceTax(s, p);
                tIn += Diplomacy.TributeOf(tm + Diplomacy.PactBonus(s, m, tm));
            }
        return new Budget(taxes, bld, AdminUpkeep(prov, AdminLimit(nat, cities)), EdictCost(nat, taxes + pacts), pacts, tIn, tOut);
    }
}
