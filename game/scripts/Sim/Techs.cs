using System;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

/// <summary>What a technology does besides opening a building (summed over the known ones, see <see cref="Techs.Sum"/>).</summary>
public enum TechFx : byte
{
    None,
    RiverCap,         // population cap of river provinces, ‰ of the base
    CapPermille,      // population cap of every province, ‰ of the base
    ShrineMood,       // mood target per shrine in the province
    Mood,             // mood target everywhere
    QuarryMaterials,  // materials per quarry per cycle
    MaterialDiscount, // % off the materials a building takes
    CityInfluence,    // influence a city gathers per cycle
    Science,          // science per cycle
    TaxPermille,      // taxes, ‰ extra
    ScoutRange,       // scouts see this many provinces further
}

/// <summary>One technology: era, tree position (lane = branch row, order = column inside its era), cost in science at
/// «Обычная» (scaled by the pace like the eras), prerequisites (all needed), the fork it belongs to (one choice per
/// fork, forever; -1 none), the building it opens, its effects, a short effect line and the story of how it was found.</summary>
public sealed record TechDef(string Id, string Name, int Era, int Lane, int Order, int Cost, string[] Requires, int Fork,
                             Bld? Unlocks, (TechFx Fx, int Amount)[] Fx, string Icon, string Effect, string Lore);

/// <summary>
/// The tech tree (IDEAS A-2, A-3), era by era. Science still fills the era progress stock that drives the calendar
/// (<see cref="Eras"/>); the same points also go to the one technology a nation studies. A nation leaves an era only
/// with <see cref="Required"/> of its technologies known, so the eras are earned, not waited out. A technology opens
/// when its prerequisites are known and its era has come; of a fork (the Great Fork of Древний мир) only one can ever
/// be learned. Techs are kept as a bit set (NationState.TechsDone) and per-tech points (NationState.TechPts); points
/// made while nothing is chosen wait in NationState.TechPool and go to the next choice, so a slow click loses nothing.
/// The first six ids never move (saves and tests refer to them).
/// </summary>
public static class Techs
{
    /// <summary>Branch rows of the tree screen.</summary>
    public static readonly string[] Lanes = { "Земледелие", "Охота и скот", "Добыча", "Ремесло", "Строительство", "Торговля", "Знание", "Общество", "Вера" };

    static readonly string[] None = Array.Empty<string>();
    static readonly (TechFx, int)[] NoFx = Array.Empty<(TechFx, int)>();

    public static readonly TechDef[] All =
    {
        // ---- Первобытная (0): six, four needed; no prerequisites — the tribe learns what its land teaches
        new("wild_grain", "Дикие злаки", 0, 0, 0, 3000, None, -1, Bld.Farm, NoFx, "plant",
            "Открывает ферму",
            "Кто-то рассыпал зёрна у стоянки, а весной там выросла еда. Совпадение? Старейшины решили, что нет."),
        new("stone_axe", "Каменный топор", 0, 3, 0, 3000, None, -1, Bld.Lumber, NoFx, "trees",
            "Открывает лесопилку",
            "Острый камень на крепкой палке. Деревья впервые боятся людей."),
        new("flint", "Кремень", 0, 2, 0, 3000, None, -1, Bld.Quarry, NoFx, "pick",
            "Открывает каменоломню и геологов",
            "Один камень высекает искру из другого. Тот, кто это заметил, три дня ходил гордый."),
        new("harpoon", "Острога и сеть", 0, 1, 0, 3000, None, -1, Bld.Fishery, NoFx, "anchor",
            "Открывает рыбацкую пристань",
            "Рыба долго считала реку своей. Потом кто-то сплёл сеть."),
        new("taming", "Приручение", 0, 1, 1, 3000, None, -1, Bld.Pasture, NoFx, "paw",
            "Открывает пастбище",
            "Волчонок остался у костра и не ушёл. Козы пришли сами — посмотреть на волчонка."),
        new("ancestors", "Духи предков", 0, 8, 0, 3000, None, -1, Bld.Shrine, NoFx, "sun",
            "Открывает святилище: наука и довольство",
            "Шаман сказал, что предки смотрят. С тех пор у костра говорят тише и спорят реже."),

        // ---- Древний мир (1): «осесть и освятить»; seven needed, then the Great Fork
        new("irrigation", "Ирригация", 1, 0, 0, 9000, new[] { "wild_grain" }, -1, null, new[] { (TechFx.RiverCap, 150) }, "droplet-off",
            "Провинции на реках: предел населения +15%",
            "Кто-то прокопал канаву от реки к полю. Сосед сказал, что это глупость, а потом прокопал свою."),
        new("pottery", "Гончарный круг", 1, 3, 0, 9000, new[] { "wild_grain" }, -1, Bld.Granary, NoFx, "home",
            "Открывает амбар: зерно не пропадает до весны",
            "Глина крутилась, крутилась — и вышел горшок. Мыши ушли искать другую деревню."),
        new("bronze", "Бронза", 1, 2, 0, 9000, new[] { "flint" }, -1, null, new[] { (TechFx.QuarryMaterials, 1) }, "pick",
            "Каменоломни: +1 материал за цикл",
            "Медь мягкая, олово мягкое, а вместе — нет. Кузнец не может объяснить почему и берёт за это втрое."),
        new("masonry", "Каменное строительство", 1, 4, 0, 9000, new[] { "flint", "stone_axe" }, -1, null, new[] { (TechFx.MaterialDiscount, 25) }, "building-warehouse",
            "Постройки требуют на 25% меньше материалов",
            "Камень на камень, и стена стоит. Строители третий год спорят, кто придумал раствор."),
        new("wheel", "Колесо", 1, 1, 0, 9000, new[] { "taming" }, -1, null, new[] { (TechFx.CityInfluence, 3) }, "compass",
            "Города растут быстрее: +3 влияния за цикл",
            "Бревно катилось под гору, и все смеялись. Потом смеяться перестали: бревно везло мешок."),
        new("barter", "Обмен", 1, 5, 0, 9000, new[] { "pottery" }, -1, Bld.Market, NoFx, "coins",
            "Открывает рынок",
            "Горшок за рыбу, рыбу за шкуру, шкуру за горшок. К вечеру все остались при своём, но довольные."),
        new("chief_law", "Закон вождя", 1, 7, 0, 9000, new[] { "ancestors" }, -1, null, new[] { (TechFx.TaxPermille, 100) }, "crown",
            "Налоги +10%",
            "Вождь сказал: «Так будет». Так и стало. Некоторым даже понравилось."),
        new("calendar", "Календарь", 1, 6, 0, 9000, new[] { "ancestors" }, -1, null, new[] { (TechFx.Science, 1) }, "hourglass",
            "Наука +1 за цикл",
            "Жрецы сосчитали дни от разлива до разлива. Вышло 365, но один жрец настаивает на 366."),
        new("writing", "Письменность", 1, 6, 1, 9000, new[] { "calendar" }, -1, null, new[] { (TechFx.Science, 2) }, "book",
            "Наука +2 за цикл",
            "Первая запись: «Три козы — долг». Литература началась с бухгалтерии."),
        new("priesthood", "Жречество", 1, 8, 0, 9000, new[] { "ancestors" }, -1, null, new[] { (TechFx.ShrineMood, 4) }, "sun",
            "Святилища: довольство ещё +4",
            "Шаманов стало много, и им понадобился главный. Главный первым делом построил себе крышу."),
        new("first_cities", "Первые города", 1, 7, 1, 9000, new[] { "pottery", "chief_law" }, -1, null, new[] { (TechFx.CityInfluence, 2) }, "building-bank",
            "Города растут быстрее (+2 влияния) и открывают Великую развилку",
            "Люди поставили дома тесно, чтобы было теплее. Оказалось, так ещё и веселее. И шумнее."),
        new("temple_kingdom", "Храмовое царство", 1, 3, 2, 12000, new[] { "first_cities" }, 1, null, new[] { (TechFx.Science, 3), (TechFx.ShrineMood, 3) }, "sun",
            "Великая развилка. Наука +3, святилища: довольство ещё +3",
            "Правит тот, кого слушают боги. Боги, по слухам, слушают того, кто строит им храмы."),
        new("river_realm", "Речная держава", 1, 4, 2, 12000, new[] { "first_cities" }, 1, null, new[] { (TechFx.CapPermille, 100), (TechFx.RiverCap, 100) }, "droplet-off",
            "Великая развилка. Предел населения +10%, у рек ещё +10%",
            "Река кормит, река возит, река решает споры о границах. Кто держит реку, держит всё."),
        new("steppe_union", "Степной союз", 1, 5, 2, 12000, new[] { "first_cities" }, 1, null, new[] { (TechFx.CityInfluence, 5), (TechFx.ScoutRange, 1) }, "paw",
            "Великая развилка. Города растут быстрее (+5 влияния), разведчики видят дальше",
            "Сто родов, один курултай. Спорят три дня, зато потом скачут в одну сторону."),
    };

    /// <summary>Technologies of an era a nation needs to leave it (Первобытная 4 of 6, Древний мир 7; later eras have
    /// no tree yet).</summary>
    public static int Required(int era) => era switch { 0 => 4, 1 => 7, _ => 0 };

    /// <summary>The technology that opens geologists (Кремень).</summary>
    public const int SurveyTech = 2;

    public static int Count => All.Length;
    public static long AllMask => (1L << All.Length) - 1;

    static readonly int[][] Prereq = BuildPrereq();

    static int[][] BuildPrereq()
    {
        var r = new int[All.Length][];
        for (int t = 0; t < All.Length; t++)
        {
            r[t] = new int[All[t].Requires.Length];
            for (int k = 0; k < r[t].Length; k++) r[t][k] = Array.FindIndex(All, d => d.Id == All[t].Requires[k]);
        }
        return r;
    }

    /// <summary>Indices of t's prerequisites.</summary>
    public static int[] Requires(int t) => Prereq[t];

    public static int Index(string id) => Array.FindIndex(All, d => d.Id == id);

    public static int Cost(int t, int pace) => (int)Math.Max(1, (long)All[t].Cost * Eras.ClampPace(pace) / 1000);

    public static bool Known(NationState nat, int t) => (uint)t < (uint)All.Length && (nat.TechsDone & (1L << t)) != 0;

    /// <summary>The technology that opens building b, or -1 when b needs none.</summary>
    public static int For(Bld b)
    {
        for (int t = 0; t < All.Length; t++) if (All[t].Unlocks == b) return t;
        return -1;
    }

    /// <summary>Can the nation build b as far as knowledge goes?</summary>
    public static bool Allows(NationState nat, Bld b)
    {
        int t = For(b);
        return t < 0 || Known(nat, t);
    }

    /// <summary>Sum of one effect over the nation's known technologies.</summary>
    public static int Sum(NationState nat, TechFx fx)
    {
        if (nat.TechsDone == 0) return 0;
        int s = 0;
        for (int t = 0; t < All.Length; t++)
        {
            if ((nat.TechsDone & (1L << t)) == 0) continue;
            foreach (var (f, a) in All[t].Fx) if (f == fx) s += a;
        }
        return s;
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

    /// <summary>Is another path of t's fork already taken (then t is closed forever)?</summary>
    public static bool ForkClosed(NationState nat, int t)
    {
        int f = All[t].Fork;
        if (f < 0) return false;
        for (int o = 0; o < All.Length; o++) if (o != t && All[o].Fork == f && Known(nat, o)) return true;
        return false;
    }

    public static bool PrereqsKnown(NationState nat, int t)
    {
        foreach (int r in Prereq[t]) if (!Known(nat, r)) return false;
        return true;
    }

    /// <summary>Can the nation start studying t now: not known, its era has come, its prerequisites known, its fork open.</summary>
    public static bool Open(NationState nat, int t) =>
        (uint)t < (uint)All.Length && !Known(nat, t) && All[t].Era <= nat.Era && PrereqsKnown(nat, t) && !ForkClosed(nat, t);

    /// <summary>Start (or switch to) t: points already put into t stay, the waiting pool joins it.</summary>
    public static bool Choose(NationState nat, int t)
    {
        if (!Open(nat, t)) return false;
        nat.Researching = t;
        nat.TechPts[t] += nat.TechPool;
        nat.TechPool = 0;
        return true;
    }

    /// <summary>Everything of the eras before `era` becomes known (debug era jumps; the pool and choice are kept); of a
    /// fork only its first path.</summary>
    public static void GrantBefore(NationState nat, int era)
    {
        for (int t = 0; t < All.Length; t++) if (All[t].Era < era && !ForkClosed(nat, t)) Learn(nat, t);
    }

    internal static void Learn(NationState nat, int t)
    {
        nat.TechsDone |= 1L << t;
        nat.TechPts[t] = 0;
        if (nat.Researching == t) nat.Researching = -1;
        // the other paths of a fork close: their points are lost with them
        int f = All[t].Fork;
        if (f < 0) return;
        for (int o = 0; o < All.Length; o++)
        {
            if (o == t || All[o].Fork != f) continue;
            nat.TechPts[o] = 0;
            if (nat.Researching == o) nat.Researching = -1;
        }
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
        if (nat.Researching >= 0 && !Open(nat, nat.Researching)) nat.Researching = -1;   // its fork closed meanwhile
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
