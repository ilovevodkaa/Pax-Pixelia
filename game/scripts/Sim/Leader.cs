using System;
using System.Text;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

/// <summary>A ruler's trait (data/core/leader_traits.json names them): its bonuses as technology effects (cached per
/// nation and added by Techs.Sum) and a few rules of its own: research %, building price %, wonder share %, how slowly
/// risings ripen %, how hard the borders pull %, festivals' extra mood, years of life.</summary>
public sealed record TraitDef(string Id, string Name, string Group, string Effect, (TechFx Fx, int Amount)[] Fx,
                              int ResearchPct = 0, int BuildPct = 0, int WonderPct = 0, int RevoltPct = 0, int PullPct = 0, int FeastMood = 0, int LifeYears = 0);

/// <summary>
/// The ruler (GDD 3: «лидер-персонаж стареет, умирает, есть наследники»). Every nation has one: a name drawn from its
/// seed, an age, one or two traits of character, sometimes an upbringing, and traits acquired over the reign. The
/// calendar runs far too fast for real lifetimes (early ticks are months), so a ruler's year is <see cref="YearCycles"/>
/// rules cycles — a reign lasts about 15–30 minutes of play. Past 55 death grows likely; the heir takes the throne
/// with traits of their own (and maybe one of the parent's), and the realm mourns (mood −5 unless the faith honours
/// the ancestors). Pure C#, integers, deterministic; names are derived from NationState.RulerSeed, not stored.
/// </summary>
public static class Leader
{
    static (TechFx, int)[] F(params (TechFx, int)[] fx) => fx;
    static readonly (TechFx, int)[] None = Array.Empty<(TechFx, int)>();

    public static readonly TraitDef[] Traits =
    {
        // ---- character (one or two at accession) ----
        new("wise", "Мудрый", "character", "Исследования +10%", None, ResearchPct: 10),
        new("generous", "Щедрый", "character", "Довольство +3, налоги −5%", F((TechFx.Mood, 3), (TechFx.TaxPermille, -50))),
        new("stingy", "Скупой", "character", "Налоги +8%, довольство −2", F((TechFx.TaxPermille, 80), (TechFx.Mood, -2))),
        new("ambitious", "Честолюбивый", "character", "Постройки на 10% дешевле, довольство −1", F((TechFx.Mood, -1)), BuildPct: 10),
        new("pious", "Благочестивый", "character", "Святилища: довольство +2, исследования −5%", F((TechFx.ShrineMood, 2)), ResearchPct: -5),
        new("skeptic", "Скептик", "character", "Исследования +8%, святилища: довольство −2", F((TechFx.ShrineMood, -2)), ResearchPct: 8),
        new("gentle", "Мягкий", "character", "Довольство +4, налоги −4%", F((TechFx.Mood, 4), (TechFx.TaxPermille, -40))),
        new("stern", "Суровый", "character", "Довольство −4, мятеж зреет на 25% дольше", F((TechFx.Mood, -4)), RevoltPct: 25),
        new("curious", "Любознательный", "character", "Разведчики видят на 1 дальше, исследования +3%", F((TechFx.ScoutRange, 1)), ResearchPct: 3),
        new("recluse", "Затворник", "character", "Соседи переманивают провинции вдвое медленнее, налоги −3%", F((TechFx.TaxPermille, -30)), PullPct: -50),
        new("eccentric", "Эксцентрик", "character", "Довольство +1, слава +5 за каждые 5 лет правления", F((TechFx.Mood, 1))),
        new("merry", "Весельчак", "character", "Праздники дают ещё +5 к довольству", None, FeastMood: 5),
        // ---- upbringing (sometimes) ----
        new("architect", "Зодчий", "upbringing", "Постройки на 15% дешевле, чудеса +10% вкладов", None, BuildPct: 15, WonderPct: 10),
        new("merchant", "Купец", "upbringing", "Налоги +5%", F((TechFx.TaxPermille, 50))),
        new("stargazer", "Звездочёт", "upbringing", "Исследования +10%", None, ResearchPct: 10),
        new("farmer", "Землепашец", "upbringing", "Предел населения +5%", F((TechFx.CapPermille, 50))),
        new("seafarer", "Мореход", "upbringing", "Разведчики видят на 1 дальше", F((TechFx.ScoutRange, 1))),
        new("lawgiver", "Законник", "upbringing", "Предел управления +4", F((TechFx.AdminLimit, 4))),
        // ---- acquired over the reign ----
        new("cat_lover", "Кошатник", "acquired", "Предел населения +2%: амбары берегут мыши… то есть от мышей", F((TechFx.CapPermille, 20))),
        new("long_lived", "Долгожитель", "acquired", "Проживёт на 15 лет дольше", None, LifeYears: 15),
        new("sickly", "Хворый", "acquired", "Проживёт на 10 лет меньше, постройки на 5% дороже", None, BuildPct: -5, LifeYears: -10),
        new("beloved", "Любимец народа", "acquired", "Довольство +3", F((TechFx.Mood, 3))),
        new("one_eyed", "Одноглазый", "acquired", "Исследования −3%, довольство +1: суров, но жизнь повидал", F((TechFx.Mood, 1)), ResearchPct: -3),
    };

    public static int Count => Traits.Length;
    public static int Index(string id) { for (int i = 0; i < Traits.Length; i++) if (Traits[i].Id == id) return i; return -1; }
    public static bool Has(NationState nat, int t) => (nat.RulerTraits >> t & 1) != 0;

    /// <summary>Rules cycles per ruler's year (24 s at speed 3).</summary>
    public const int YearCycles = 48;
    public const int MournMood = 5;

    public static int Age(GameState s, NationState nat) => nat.RulerAge0 + Math.Max(0, Clock.CycleOf(s.Tick) - nat.RulerStart) / YearCycles;
    public static int ReignYears(GameState s, NationState nat) => Math.Max(0, Clock.CycleOf(s.Tick) - nat.RulerStart) / YearCycles;
    public static int Life(NationState nat)
    {
        int life = nat.RulerLife;
        for (int t = 0; t < Traits.Length; t++) if (Has(nat, t)) life += Traits[t].LifeYears;
        return life;
    }

    // summed rules of the traits
    static int Sum(NationState nat, Func<TraitDef, int> f) { int v = 0; for (int t = 0; t < Traits.Length; t++) if (Has(nat, t)) v += f(Traits[t]); return v; }
    public static int ResearchPct(NationState nat) => Sum(nat, d => d.ResearchPct);
    public static int BuildPct(NationState nat) => Sum(nat, d => d.BuildPct);
    public static int WonderPct(NationState nat) => Sum(nat, d => d.WonderPct);
    public static int RevoltPct(NationState nat) => Sum(nat, d => d.RevoltPct);
    public static int PullPct(NationState nat) => Sum(nat, d => d.PullPct);
    public static int FeastMood(NationState nat) => Sum(nat, d => d.FeastMood);

    // ---------------------------------------------------------------- bonuses as technology effects

    public static int Fx(NationState nat, TechFx fx) => nat.LeaderFx == null ? 0 : nat.LeaderFx[(int)fx];

    public static void Refresh(NationState nat)
    {
        int nFx = Enum.GetValues<TechFx>().Length;
        if (nat.LeaderFx == null || nat.LeaderFx.Length != nFx) nat.LeaderFx = new int[nFx];
        else Array.Clear(nat.LeaderFx);
        for (int t = 0; t < Traits.Length; t++)
            if (Has(nat, t)) foreach (var (f, a) in Traits[t].Fx) nat.LeaderFx[(int)f] += a;
    }

    // ---------------------------------------------------------------- names

    static readonly string[] Roots = { "Аш", "Ур", "Тар", "Нам", "Эн", "Хам", "Сар", "Бел", "Кир", "Ар", "Мал", "Зор", "Ил", "Дан", "Рам", "Тиг", "Сен", "Ор", "Мер", "Наб" };
    static readonly string[] Mids = { "", "", "а", "и", "у", "е" };
    static readonly string[] HeEnds = { "ур", "ан", "им", "от", "ал", "ор", "ес", "ад", "ук", "ин" };
    static readonly string[] SheEnds = { "а", "ия", "эла", "ина", "ея", "ата" };

    public static bool Female(int seed) => (seed & 7) == 3;   // a queen now and then

    /// <summary>The ruler's personal name from its seed: a root, maybe a link vowel, an ending.</summary>
    public static string Name(int seed)
    {
        uint h = (uint)seed * 2654435761u;
        var sb = new StringBuilder();
        sb.Append(Roots[h % (uint)Roots.Length]); h /= (uint)Roots.Length;
        sb.Append(Mids[h % (uint)Mids.Length]); h /= (uint)Mids.Length;
        var ends = Female(seed) ? SheEnds : HeEnds;
        string end = ends[h % (uint)ends.Length];
        if (sb[^1] is 'а' or 'и' or 'у' or 'е' && end[0] is 'а' or 'е' or 'и' or 'у' or 'о' or 'э') end = end[1..].Length > 0 ? end[1..] : end;
        sb.Append(end);
        return sb.ToString();
    }

    static readonly string[] Roman = { "", "", " II", " III", " IV", " V", " VI", " VII", " VIII", " IX", " X" };

    /// <summary>«Ашур II»: the name, and a numeral when a ruler of the same name reigned before.</summary>
    public static string Title(NationState nat) => Name(nat.RulerSeed) + (nat.RulerNumeral < Roman.Length ? Roman[nat.RulerNumeral] : $" {nat.RulerNumeral}");

    public static string TraitList(NationState nat)
    {
        var sb = new StringBuilder();
        for (int t = 0; t < Traits.Length; t++)
            if (Has(nat, t)) { if (sb.Length > 0) sb.Append(", "); sb.Append(Traits[t].Name); }
        return sb.Length == 0 ? "без особых черт" : sb.ToString();
    }

    // ---------------------------------------------------------------- accession, the cycle, death

    static int Pick(int seed, int salt, int n, int k, string group)
    {
        int count = 0;
        foreach (var t in Traits) if (t.Group == group) count++;
        int r = (int)(SimRng.Hash(seed, salt, n, k) % (uint)count);
        for (int t = 0; t < Traits.Length; t++)
            if (Traits[t].Group == group && r-- == 0) return t;
        return 0;
    }

    /// <summary>A new ruler of nation n (the first, or an heir): name, age, life, traits.</summary>
    public static void Crown(WorldData w, GameState s, int n, int heirOf = -1)
    {
        var nat = s.Nat[n];
        int k = nat.Rulers++;
        int oldSeed = nat.RulerSeed, oldTraits = nat.RulerTraits, oldNumeral = nat.RulerNumeral;
        nat.RulerSeed = (int)(SimRng.Hash(w.Seed, 111, n, k) & 0x7fffffff);
        if (heirOf >= 0 && SimRng.Chance(w.Seed, 110, n, k, 1, 3)) nat.RulerSeed = oldSeed;   // named after the parent
        nat.RulerNumeral = heirOf >= 0 && Name(oldSeed) == Name(nat.RulerSeed) ? Math.Max(2, oldNumeral + 1) : 1;
        nat.RulerStart = Clock.CycleOf(s.Tick);
        nat.RulerAge0 = (byte)(k == 0 ? 30 + SimRng.Hash(w.Seed, 112, n, k) % 8 : 18 + SimRng.Hash(w.Seed, 112, n, k) % 18);
        nat.RulerLife = (byte)(58 + SimRng.Hash(w.Seed, 113, n, k) % 22);
        int traits = 1 << Pick(w.Seed, 114, n, k, "character");
        if (SimRng.Chance(w.Seed, 115, n, k, 1, 3)) traits |= 1 << Pick(w.Seed, 116, n, k, "character");
        if (SimRng.Chance(w.Seed, 117, n, k, 1, 3)) traits |= 1 << Pick(w.Seed, 118, n, k, "upbringing");
        if (heirOf >= 0 && SimRng.Chance(w.Seed, 119, n, k, 1, 2))   // like father, like son: one trait of character carries over
            for (int t = 0; t < Traits.Length; t++)
                if ((oldTraits >> t & 1) != 0 && Traits[t].Group == "character") { traits |= 1 << t; break; }
        nat.RulerTraits = traits;
        Refresh(nat);
    }

    /// <summary>Once a ruler's year: age, maybe a new trait, maybe death and an heir. The human hears of it.</summary>
    public static void Cycle(WorldData w, GameState s, int cycle, ISimSink sink)
    {
        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            if (nat.Rulers == 0) { Crown(w, s, n); continue; }
            int reign = cycle - nat.RulerStart;
            if (reign <= 0 || reign % YearCycles != 0) continue;
            int age = Age(s, nat), years = reign / YearCycles;
            // acquired over the reign
            int acquired = -1;
            if (age >= 70 && !Has(nat, Index("long_lived")) && SimRng.Chance(w.Seed, 121, n, cycle, 1, 5)) acquired = Index("long_lived");
            else if (SimRng.Chance(w.Seed, 122, n, cycle, 1, 60)) acquired = Pick(w.Seed, 123, n, cycle, "acquired");
            if (acquired == Index("beloved") && AverageMood(s, n) < 70) acquired = -1;
            if (acquired >= 0 && !Has(nat, acquired))
            {
                nat.RulerTraits |= 1 << acquired;
                Refresh(nat);
                if (nat.Human) sink?.Notify("crown", $"{Title(nat)} приобретает черту «{Traits[acquired].Name}»: {Traits[acquired].Effect.ToLowerInvariant()}");
            }
            if (Has(nat, Index("eccentric")) && years % 5 == 0) nat.Glory += 5;
            // death
            int life = Life(nat);
            bool dies = age >= life || age >= 55 && SimRng.Chance(w.Seed, 124, n, cycle, Math.Max(1, age - 52), 100);
            if (!dies) continue;
            string old = Title(nat);
            int reigned = ReignYears(s, nat);
            Crown(w, s, n, heirOf: n);
            bool honoured = Faith.Has(nat, Faith.Index("ancestor_worship"));
            if (!honoured)
                for (int p = 0; p < w.P; p++) if (s.Owner[p] == n) s.Mood[p] = (byte)Math.Max(0, s.Mood[p] - MournMood);
            if (sink == null) continue;
            if (nat.Human)
                sink.Notify("crown", $"Умер {old} ({age} лет, правил {reigned} {Ru.Plural(reigned, "год", "года", "лет")}). На престол {(Female(nat.RulerSeed) ? "взошла" : "взошёл")} {Title(nat)}, {nat.RulerAge0} лет: {TraitList(nat).ToLowerInvariant()}"
                                    + (honoured ? ". Предков чтут — траур недолог" : $". Траур: довольство −{MournMood}"));
            else
                for (int h = 0; h < s.Nat.Length; h++)
                    if (s.Nat[h].Human && Rules.Met(s, h, n)) sink.Notify("crown", $"В державе {s.Nations[n].Name} умер {old}. Правит {Title(nat)}");
        }
    }

    static int AverageMood(GameState s, int n)
    {
        long m = 0, k = 0;
        for (int p = 0; p < s.Owner.Length; p++) if (s.Owner[p] == n) { m += s.Mood[p]; k++; }
        return k == 0 ? 0 : (int)(m / k);
    }
}
