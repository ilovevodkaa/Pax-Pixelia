using System;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

public enum DogmaError { None, Unknown, Taken, NoSlot, Excluded }

/// <summary>A dogma of the nation's faith (data/core/dogmas.json names them): its slot, what it excludes, its bonuses
/// as technology effects and the rules of its own (forest and river mood, taxes per shrine, research %, conversion,
/// tolerance of other faiths, festivals, mourning), and how it shapes the people's character.</summary>
public sealed record DogmaDef(string Id, string Name, string Slot, string Effect, (TechFx Fx, int Amount)[] Fx, string Excludes = null,
                              int ForestMood = 0, int RiverMood = 0, int ShrineTax = 0, int ResearchPct = 0, int ConvertPct = 0,
                              bool Tolerant = false, int FeastMood = 0, int CharScale = Character.Faith, bool CharRight = false);

/// <summary>
/// The nation's faith (GDD 9.1: «основываешь сам — имя, символ, 3–4 догмата»). The faith itself is the nation's
/// Religion; it grows dogmas as it matures: a teaching once the ancestors' spirits are known («Духи предков»), a rite
/// with the priesthood («Жречество»), a preaching from Античность on. A dogma is chosen for good — choose well. Dogmas
/// shape the people (character: pious, or secular for tolerance and the temple schools). Provinces of another faith in
/// the realm convert, faster with shrines and missionaries; until they do, they sulk (mood −10) unless the faith is
/// tolerant. Pure C#, integers, deterministic.
/// </summary>
public static class Faith
{
    static (TechFx, int)[] F(params (TechFx, int)[] fx) => fx;
    static readonly (TechFx, int)[] None = Array.Empty<(TechFx, int)>();

    public static readonly DogmaDef[] Dogmas =
    {
        // ---- teaching ----
        new("sacred_groves", "Священные рощи", "teaching", "Лесные провинции: довольство +3; лесопилки дают на 1 меньше", None, ForestMood: 3),
        new("ancestor_worship", "Почитание предков", "teaching", "Смерть правителя без траура, довольство +1", F((TechFx.Mood, 1))),
        new("asceticism", "Аскеза", "teaching", "Довольство +3, налоги −5%", F((TechFx.Mood, 3), (TechFx.TaxPermille, -50)), Excludes: "harvest_feast"),
        new("harvest_feast", "Праздник урожая", "teaching", "Предел населения +5%, праздники ещё +2 к довольству", F((TechFx.CapPermille, 50)), Excludes: "asceticism", FeastMood: 2),
        new("cat_veneration", "Почитание кошек", "teaching", "Предел населения +3%: кошки стерегут амбары", F((TechFx.CapPermille, 30))),
        new("sacred_rivers", "Священные реки", "teaching", "Речные провинции: довольство +3 и людей кормят на 10% больше", F((TechFx.RiverCap, 100)), RiverMood: 3),
        // ---- rite ----
        new("fire_temples", "Огненные храмы", "rite", "Святилища: довольство +3", F((TechFx.ShrineMood, 3))),
        new("pilgrimage", "Паломничество", "rite", "Каждое святилище приносит ещё 0,05 золота за цикл", None, ShrineTax: 5),
        new("temple_schools", "Храмовые школы", "rite", "Исследования +6%", None, ResearchPct: 6, CharScale: Character.Faith, CharRight: true),
        // ---- preaching ----
        new("missionaries", "Миссионеры", "preaching", "Иноверцы в державе обращаются втрое быстрее", None, ConvertPct: 200),
        new("merchant_faith", "Вера торговцев", "preaching", "Налоги +4%, влияние городов +1", F((TechFx.TaxPermille, 40), (TechFx.CityInfluence, 1)), CharScale: Character.Agri, CharRight: true),
        new("tolerance", "Терпимость", "preaching", "Иноверцы не ропщут (без −10 к довольству)", None, Tolerant: true, CharScale: Character.Faith, CharRight: true),
    };

    public static int Count => Dogmas.Length;
    public static int Index(string id) { for (int i = 0; i < Dogmas.Length; i++) if (Dogmas[i].Id == id) return i; return -1; }
    public static bool Has(NationState nat, int d) => d >= 0 && (nat.Dogmas >> d & 1) != 0;
    public static readonly string[] Slots = { "teaching", "rite", "preaching" };
    public static string SlotName(string slot) => slot switch { "teaching" => "Учение", "rite" => "Обряд", _ => "Проповедь" };

    /// <summary>Is the slot open to nation nat: the teaching with «Духи предков», the rite with «Жречество», the preaching in Античность.</summary>
    public static bool SlotOpen(NationState nat, string slot) => slot switch
    {
        "teaching" => Techs.Known(nat, Techs.Index("ancestors")),
        "rite" => Techs.Known(nat, Techs.Index("priesthood")),
        _ => nat.Era >= 2,
    };

    public static string SlotNeed(string slot) => slot switch
    {
        "teaching" => "Откроется с технологией «Духи предков»",
        "rite" => "Откроется с технологией «Жречество»",
        _ => "Откроется в Античности",
    };

    public static int InSlot(NationState nat, string slot)
    {
        for (int d = 0; d < Dogmas.Length; d++) if (Dogmas[d].Slot == slot && Has(nat, d)) return d;
        return -1;
    }

    public static DogmaError CheckAdopt(NationState nat, int d)
    {
        if ((uint)d >= (uint)Dogmas.Length) return DogmaError.Unknown;
        if (Has(nat, d)) return DogmaError.Taken;
        var def = Dogmas[d];
        if (!SlotOpen(nat, def.Slot) || InSlot(nat, def.Slot) >= 0) return DogmaError.NoSlot;
        if (def.Excludes != null && Has(nat, Index(def.Excludes))) return DogmaError.Excluded;
        return DogmaError.None;
    }

    /// <summary>Adopt dogma d for good: its bonuses, and a push to the people's character (10 deed units).</summary>
    public static void Adopt(GameState s, int n, int d)
    {
        var nat = s.Nat[n];
        nat.Dogmas |= 1 << d;
        Refresh(nat);
        var def = Dogmas[d];
        Character.Deed(s, n, def.CharScale, def.CharRight, 10);
        Character.Refresh(nat);
    }

    // summed rules
    static int Sum(NationState nat, Func<DogmaDef, int> f) { int v = 0; for (int d = 0; d < Dogmas.Length; d++) if (Has(nat, d)) v += f(Dogmas[d]); return v; }
    public static int ForestMood(NationState nat) => Sum(nat, d => d.ForestMood);
    public static int RiverMood(NationState nat) => Sum(nat, d => d.RiverMood);
    public static int ShrineTax(NationState nat) => Sum(nat, d => d.ShrineTax);
    public static int ResearchPct(NationState nat) => Sum(nat, d => d.ResearchPct);
    public static int ConvertPct(NationState nat) => Sum(nat, d => d.ConvertPct);
    public static int FeastMood(NationState nat) => Sum(nat, d => d.FeastMood);
    public static bool Tolerant(NationState nat) => Has(nat, Index("tolerance"));

    public static int Fx(NationState nat, TechFx fx) => nat.FaithFx == null ? 0 : nat.FaithFx[(int)fx];

    public static void Refresh(NationState nat)
    {
        int nFx = Enum.GetValues<TechFx>().Length;
        if (nat.FaithFx == null || nat.FaithFx.Length != nFx) nat.FaithFx = new int[nFx];
        else Array.Clear(nat.FaithFx);
        for (int d = 0; d < Dogmas.Length; d++)
            if (Has(nat, d)) foreach (var (f, a) in Dogmas[d].Fx) nat.FaithFx[(int)f] += a;
    }

    /// <summary>Mood the faith adds to province p of nation o (forests, rivers; the foreign-faith penalty is in Moods).</summary>
    public static int MoodIn(WorldData w, GameState s, int o, int p)
    {
        var nat = s.Nat[o];
        if (nat.Dogmas == 0) return 0;
        int v = 0;
        if (w.PRiver[p] != 0) v += RiverMood(nat);
        if (ForestMood(nat) != 0 && WorldFacts.Of(w).Allows(p, Bld.Lumber)) v += ForestMood(nat);
        return v;
    }

    // ---------------------------------------------------------------- conversion

    /// <summary>Every 16 cycles a province of another faith in the realm may take the realm's faith: 4%, +2% per shrine in
    /// it, ×(1 + missionaries) — a minute or two for a province with a shrine.</summary>
    public const int ConvertCycles = 16, ConvertBasePermille = 40, ConvertShrinePermille = 20;

    public static void Cycle(WorldData w, GameState s, int cycle, ISimSink sink)
    {
        if (cycle % ConvertCycles != 0) return;
        for (int p = 0; p < w.P; p++)
        {
            int o = s.Owner[p];
            if (o < 0 || s.Religion[p] < 0) continue;
            int faith = s.Nations[o].Religion;
            if (s.Religion[p] == faith) continue;
            int odds = ConvertBasePermille;
            foreach (var b in s.Buildings[p]) if (b == Bld.Shrine) odds += ConvertShrinePermille;
            odds += odds * ConvertPct(s.Nat[o]) / 100;
            if (SimRng.Permille(w.Seed, 131, p, cycle) >= odds) continue;
            s.Religion[p] = (sbyte)faith;
            if (s.Nat[o].Human) sink?.Notify("sun", $"Провинция {w.PName[p]} приняла нашу веру — {Core.Data.Religions[faith].Name}");
        }
    }

    // ---------------------------------------------------------------- bots

    /// <summary>A bot fills an open slot with a dogma of its taste (seeded), now and then.</summary>
    public static void BotChoose(WorldData w, GameState s, int n, int cycle)
    {
        if (!SimRng.Chance(w.Seed, 132, n, cycle, 1, 90)) return;
        var nat = s.Nat[n];
        int best = -1; uint bs = 0;
        for (int d = 0; d < Dogmas.Length; d++)
        {
            if (CheckAdopt(nat, d) != DogmaError.None) continue;
            uint sc = SimRng.Hash(w.Seed, 133, n, d) | 1;
            if (sc > bs) { bs = sc; best = d; }
        }
        if (best >= 0) Commands.Apply(w, s, Cmd.Dogma(n, best), null);
    }
}
