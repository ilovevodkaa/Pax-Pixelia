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
    Science,          // research points per cycle (technologies only: the era stock and the calendar keep their pace)
    TaxPermille,      // taxes, ‰ extra
    ScoutRange,       // scouts see this many provinces further
    MineMaterials,    // materials per mine (a quarry on a surveyed metal vein)
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
/// be learned. Techs are kept as a bit set of 64-bit words (NationState.TechsDone, any number of technologies) and
/// per-tech points (NationState.TechPts); points
/// made while nothing is chosen wait in NationState.TechPool and go to the next choice, so a slow click loses nothing.
/// Ids never move (saves keep the bit set; new technologies are appended): the tree's shape is Lane/Order. The root
/// «Огонь» is known by everyone from the start.
/// </summary>
public static class Techs
{
    /// <summary>Branch rows of the tree screen.</summary>
    public static readonly string[] Lanes = { "Земледелие", "Охота и скот", "Добыча", "Ремесло", "Строительство", "Торговля", "Знание", "Общество", "Вера" };

    static readonly string[] None = Array.Empty<string>();
    static readonly (TechFx, int)[] NoFx = Array.Empty<(TechFx, int)>();

    public static readonly TechDef[] All =
    {
        // ids 0..19 never move (saves keep the bit set); the tree's shape is Lane/Order, not the id
        // ---- Первобытная (0): from the fire the tribe's knowledge branches out
        new("wild_grain", "Дикие злаки", 0, 0, 2, 800, new[] { "gathering" }, -1, Bld.Farm, NoFx, "plant",
            "Открывает ферму",
            "Кто-то рассыпал зёрна у стоянки, а весной там выросла еда. Совпадение? Старейшины решили, что нет."),
        new("stone_axe", "Каменный топор", 0, 3, 2, 800, new[] { "stone_tools" }, -1, Bld.Lumber, NoFx, "trees",
            "Открывает лесопилку",
            "Острый камень на крепкой палке. Деревья впервые боятся людей."),
        new("flint", "Кремень", 0, 2, 2, 800, new[] { "stone_tools" }, -1, Bld.Quarry, NoFx, "pick",
            "Открывает каменоломню и геологов",
            "Один камень высекает искру из другого. Тот, кто это заметил, три дня ходил гордый."),
        new("harpoon", "Острога и сеть", 0, 1, 2, 800, new[] { "hunting" }, -1, Bld.Fishery, NoFx, "anchor",
            "Открывает рыбацкую пристань",
            "Рыба долго считала реку своей. Потом кто-то сплёл сеть."),
        new("taming", "Приручение", 0, 1, 3, 800, new[] { "hunting" }, -1, Bld.Pasture, NoFx, "paw",
            "Открывает пастбище",
            "Волчонок остался у костра и не ушёл. Козы пришли сами — посмотреть на волчонка."),
        new("ancestors", "Духи предков", 0, 8, 2, 800, new[] { "speech" }, -1, Bld.Shrine, NoFx, "sun",
            "Открывает святилище: наука и довольство",
            "Шаман сказал, что предки смотрят. С тех пор у костра говорят тише и спорят реже."),

        // ---- Древний мир (1): «осесть и освятить»
        new("irrigation", "Ирригация", 1, 0, 0, 2000, new[] { "wild_grain" }, -1, null, new[] { (TechFx.RiverCap, 150) }, "droplet-off",
            "Провинции на реках: предел населения +15%",
            "Кто-то прокопал канаву от реки к полю. Сосед сказал, что это глупость, а потом прокопал свою."),
        new("pottery", "Гончарный круг", 1, 3, 0, 2000, new[] { "wild_grain" }, -1, Bld.Granary, NoFx, "home",
            "Открывает амбар: зерно не пропадает до весны",
            "Глина крутилась, крутилась — и вышел горшок. Мыши ушли искать другую деревню."),
        new("bronze", "Бронза", 1, 2, 1, 2000, new[] { "copper" }, -1, null, new[] { (TechFx.QuarryMaterials, 1) }, "pick",
            "Каменоломни: ещё +1 материал за цикл",
            "Медь мягкая, олово мягкое, а вместе — нет. Кузнец не может объяснить почему и берёт за это втрое."),
        new("masonry", "Каменное строительство", 1, 4, 0, 2000, new[] { "flint", "stone_axe" }, -1, null, new[] { (TechFx.MaterialDiscount, 20) }, "building-warehouse",
            "Постройки требуют на 20% меньше материалов",
            "Камень на камень, и стена стоит. Строители третий год спорят, кто придумал раствор."),
        new("wheel", "Колесо", 1, 1, 0, 2000, new[] { "taming" }, -1, null, new[] { (TechFx.CityInfluence, 3) }, "compass",
            "Города растут быстрее: +3 влияния за цикл",
            "Бревно катилось под гору, и все смеялись. Потом смеяться перестали: бревно везло мешок."),
        new("barter", "Обмен", 1, 5, 1, 2000, new[] { "pottery" }, -1, Bld.Market, NoFx, "coins",
            "Открывает рынок",
            "Горшок за рыбу, рыбу за шкуру, шкуру за горшок. К вечеру все остались при своём, но довольные."),
        new("chief_law", "Закон вождя", 1, 7, 0, 2000, new[] { "elders" }, -1, null, new[] { (TechFx.TaxPermille, 100) }, "crown",
            "Налоги +10%",
            "Вождь сказал: «Так будет». Так и стало. Некоторым даже понравилось."),
        new("calendar", "Календарь", 1, 6, 0, 2000, new[] { "tally" }, -1, null, new[] { (TechFx.Science, 1) }, "hourglass",
            "Исследования +1 очко за цикл",
            "Жрецы сосчитали дни от разлива до разлива. Вышло 365, но один жрец настаивает на 366."),
        new("writing", "Письменность", 1, 6, 1, 2000, new[] { "calendar" }, -1, null, new[] { (TechFx.Science, 1) }, "book",
            "Исследования +1 очко за цикл",
            "Первая запись: «Три козы — долг». Литература началась с бухгалтерии."),
        new("priesthood", "Жречество", 1, 8, 0, 2000, new[] { "ancestors" }, -1, null, new[] { (TechFx.ShrineMood, 4) }, "sun",
            "Святилища: довольство ещё +4",
            "Шаманов стало много, и им понадобился главный. Главный первым делом построил себе крышу."),
        new("first_cities", "Первые города", 1, 7, 3, 2000, new[] { "brick", "chief_law" }, -1, null, new[] { (TechFx.CityInfluence, 2) }, "building-bank",
            "Города растут быстрее (+2 влияния) и открывают Великую развилку",
            "Люди поставили дома тесно, чтобы было теплее. Оказалось, так ещё и веселее. И шумнее."),
        new("temple_kingdom", "Храмовое царство", 1, 3, 4, 3000, new[] { "first_cities" }, 1, null, new[] { (TechFx.Science, 2), (TechFx.ShrineMood, 3) }, "sun",
            "Великая развилка. Исследования +2 очка за цикл, святилища: довольство ещё +3",
            "Правит тот, кого слушают боги. Боги, по слухам, слушают того, кто строит им храмы."),
        new("river_realm", "Речная держава", 1, 4, 4, 3000, new[] { "first_cities" }, 1, null, new[] { (TechFx.CapPermille, 100), (TechFx.RiverCap, 100) }, "droplet-off",
            "Великая развилка. Предел населения +10%, у рек ещё +10%",
            "Река кормит, река возит, река решает споры о границах. Кто держит реку, держит всё."),
        new("steppe_union", "Степной союз", 1, 5, 4, 3000, new[] { "first_cities" }, 1, null, new[] { (TechFx.CityInfluence, 5), (TechFx.ScoutRange, 1) }, "paw",
            "Великая развилка. Города растут быстрее (+5 влияния), разведчики видят дальше",
            "Сто родов, один курултай. Спорят три дня, зато потом скачут в одну сторону."),

        // ---- Первобытная: the root and the first steps (ids 20..28)
        new("fire", "Огонь", 0, 4, 0, 0, None, -1, null, NoFx, "torch",
            "С него всё начинается: знают все роды",
            "Кто-то не испугался молнии и унёс горящую ветку. С тех пор у людей есть вечер."),
        new("gathering", "Собирательство", 0, 0, 1, 800, new[] { "fire" }, -1, null, new[] { (TechFx.CapPermille, 30) }, "plant",
            "Предел населения +3%",
            "Эти ягоды можно, эти нельзя. Знание стоило роду двух дядюшек."),
        new("hunting", "Охота", 0, 1, 1, 800, new[] { "fire" }, -1, null, new[] { (TechFx.CapPermille, 20) }, "paw",
            "Предел населения +2%",
            "Загонная охота: двадцать человек кричат, один бросает копьё. Мамонт против."),
        new("stone_tools", "Каменные орудия", 0, 3, 1, 800, new[] { "fire" }, -1, null, new[] { (TechFx.MaterialDiscount, 10) }, "pick",
            "Постройки требуют на 10% меньше материалов",
            "Скол, ещё скол — и камень режет. Первые инструменты лежали в руке лучше, чем у нас мышка."),
        new("speech", "Речь и предания", 0, 6, 1, 800, new[] { "fire" }, -1, null, new[] { (TechFx.Science, 1) }, "book",
            "Исследования +1 очко за цикл",
            "Старики рассказывают у огня, дети запоминают. Так знание пережило своих хозяев."),
        new("hides", "Шкуры и иглы", 0, 3, 3, 800, new[] { "hunting" }, -1, null, new[] { (TechFx.Mood, 2) }, "user",
            "Довольство везде +2: зимой больше не холодно",
            "Костяная игла и жила. Одежда по размеру — первая роскошь человечества."),
        new("tally", "Счёт по зарубкам", 0, 6, 2, 800, new[] { "speech" }, -1, null, new[] { (TechFx.TaxPermille, 30) }, "hourglass",
            "Налоги +3%: зерно и шкуры теперь считают",
            "Зарубка на кости — одна луна. Через год кость кончилась, пришлось взять вторую."),
        new("rafts", "Плоты", 0, 5, 3, 800, new[] { "stone_axe" }, -1, null, new[] { (TechFx.ScoutRange, 1) }, "anchor",
            "Разведчики видят на 1 провинцию дальше",
            "Три бревна, верёвка — и река уже не стена, а дорога."),
        new("elders", "Совет старейшин", 0, 7, 2, 800, new[] { "speech" }, -1, null, new[] { (TechFx.TaxPermille, 50) }, "users",
            "Налоги +5%",
            "Самые старые садятся в круг и решают. Самые молодые ворчат, но слушаются."),

        // ---- Древний мир: the rest of the branches (ids 29..39)
        new("plough", "Плуг", 1, 0, 1, 2000, new[] { "irrigation", "taming" }, -1, null, new[] { (TechFx.CapPermille, 80) }, "plant",
            "Предел населения +8%",
            "Бык тянет, человек держит. Поле стало втрое больше, а спина болит так же."),
        new("crop_rotation", "Севооборот", 1, 0, 2, 2000, new[] { "plough" }, -1, null, new[] { (TechFx.CapPermille, 70) }, "plant",
            "Предел населения +7%",
            "Год пшеница, год бобы, год отдых. Земля сказала спасибо урожаем."),
        new("riding", "Верховая езда", 1, 1, 1, 2000, new[] { "wheel" }, -1, null, new[] { (TechFx.ScoutRange, 1), (TechFx.CityInfluence, 1) }, "compass",
            "Разведчики видят дальше, города растут быстрее (+1)",
            "Сначала конь возил телегу. Потом кто-то сел сверху — и мир стал меньше."),
        new("copper", "Медь", 1, 2, 0, 2000, new[] { "flint" }, -1, null, new[] { (TechFx.QuarryMaterials, 1) }, "pick",
            "Каменоломни: +1 материал за цикл",
            "Зелёный камень потёк в костре. Первый металл был мягким, но блестел."),
        new("mining", "Рудники", 1, 2, 2, 2000, new[] { "bronze" }, -1, null, new[] { (TechFx.MineMaterials, 2) }, "pick",
            "Рудники на жилах: ещё +2 материала за цикл",
            "Копали вглубь, пока не нашли жилу. Потом копали, пока не нашли воду. Потом думали."),
        new("weaving", "Ткачество", 1, 3, 1, 2000, new[] { "hides" }, -1, null, new[] { (TechFx.Mood, 2) }, "user",
            "Довольство везде +2",
            "Нить через нить, и вышла ткань. Шкуры остались охотникам и упрямцам."),
        new("brick", "Кирпич", 1, 4, 1, 2000, new[] { "pottery", "masonry" }, -1, null, new[] { (TechFx.CityInfluence, 2) }, "building-warehouse",
            "Города растут быстрее: +2 влияния",
            "Глина, солома, солнце. Дом, который не уносит дождём, сделал из деревни город."),
        new("sail", "Парус", 1, 5, 0, 2000, new[] { "rafts" }, -1, null, new[] { (TechFx.TaxPermille, 40) }, "anchor",
            "Налоги +4%: товары идут по воде",
            "Ветер дул и раньше, но теперь он работает на нас. Гребцы впервые отдохнули."),
        new("weights", "Меры и весы", 1, 5, 2, 2000, new[] { "barter", "tally" }, -1, null, new[] { (TechFx.TaxPermille, 50) }, "scale",
            "Налоги +5%",
            "Мешок мешку рознь, а гиря гире — нет. Купцы приуныли, казна обрадовалась."),
        new("mathematics", "Математика", 1, 6, 3, 2000, new[] { "writing", "weights" }, -1, null, new[] { (TechFx.Science, 1) }, "atom",
            "Исследования +1 очко за цикл",
            "Писцы считали зерно и вдруг стали считать просто так. Так родилась наука."),
        new("temples", "Храмы", 1, 8, 1, 2000, new[] { "priesthood", "masonry" }, -1, null, new[] { (TechFx.ShrineMood, 3) }, "building-bank",
            "Святилища: довольство ещё +3",
            "Богам — каменный дом. Жрецам — комнаты при нём. Писцам — угол, где можно писать."),
    };

    /// <summary>Technologies of an era a nation needs to leave it, the root included (Первобытная 9 of 15, Древний мир
    /// 14 of 25; later eras have no tree yet).</summary>
    public static int Required(int era) => era switch { 0 => 9, 1 => 14, _ => 0 };

    /// <summary>The root of the tree («Огонь»): every nation knows it from the start.</summary>
    public static readonly int Root = Array.FindIndex(All, d => d.Id == "fire");

    /// <summary>The technology that opens geologists (Кремень).</summary>
    public const int SurveyTech = 2;

    public static int Count => All.Length;

    // ---------------------------------------------------------------- the bit set (NationState.TechsDone)

    /// <summary>64-bit words of a nation's bit set: bit t of word t / 64 is Techs.All[t].</summary>
    public static int Words => (All.Length + 63) / 64;

    /// <summary>The bit set of a new nation: only the root is known.</summary>
    public static ulong[] RootOnly()
    {
        var set = new ulong[Words];
        set[Root >> 6] |= 1UL << (Root & 63);
        return set;
    }

    /// <summary>Set or clear bit t (debug, saves and tests; the rules learn through <see cref="Learn"/>).</summary>
    internal static void Set(NationState nat, int t, bool on)
    {
        if (on) nat.TechsDone[t >> 6] |= 1UL << (t & 63);
        else nat.TechsDone[t >> 6] &= ~(1UL << (t & 63));
    }

    /// <summary>Drops bits past the last technology (a save from a build with a longer tree).</summary>
    internal static void Trim(ulong[] set)
    {
        int tail = All.Length & 63;
        if (tail != 0) set[^1] &= (1UL << tail) - 1;
    }

    /// <summary>Does the nation know nothing but the root?</summary>
    public static bool OnlyRoot(NationState nat)
    {
        for (int t = 0; t < All.Length; t++) if (Known(nat, t) != (t == Root)) return false;
        return true;
    }

    /// <summary>How many technologies the nation knows.</summary>
    public static int KnownCount(NationState nat)
    {
        int k = 0;
        foreach (ulong v in nat.TechsDone) k += System.Numerics.BitOperations.PopCount(v);
        return k;
    }

    /// <summary>A number that changes whenever the bit set does (the UI's «research changed» check).</summary>
    public static ulong Signature(NationState nat)
    {
        ulong h = 14695981039346656037UL;
        foreach (ulong v in nat.TechsDone) h = (h ^ v) * 1099511628211UL;
        return h;
    }

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

    public static bool Known(NationState nat, int t) => (uint)t < (uint)All.Length && (nat.TechsDone[t >> 6] & (1UL << (t & 63))) != 0;

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

    /// <summary>Research points a cycle: the nation's science plus what its knowledge adds (Речь, Календарь, Письменность…).</summary>
    public static int ResearchRate(NationState nat) => nat.ScienceRate + (nat.ScienceRate > 0 ? Sum(nat, TechFx.Science) : 0);

    /// <summary>Sum of one effect over the nation's known technologies.</summary>
    public static int Sum(NationState nat, TechFx fx)
    {
        int s = 0;
        for (int t = 0; t < All.Length; t++)
        {
            if (!Known(nat, t)) continue;
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
        Set(nat, t, true);
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
