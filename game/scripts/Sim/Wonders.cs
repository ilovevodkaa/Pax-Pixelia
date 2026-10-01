using System;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

public enum WonderError { None, Unknown, Taken, TooEarly, Busy, NotBuilding, NoCapital, Nothing }

/// <summary>What is special about a wonder: an ordinary one, the Tsar Bell (may crack), the Potemkin village (fake
/// glory until a neighbour comes close), the Tsar Cannon (glory and nothing else).</summary>
public enum WonderKind : byte { Plain, Bell, Potemkin, Cannon }

/// <summary>A wonder of the world (data/core/wonders.json names them): era it opens in, cost share (‰ of its era's
/// price), the glory it brings and its bonuses as technology effects.</summary>
public sealed record WonderDef(string Id, string Name, int Era, int CostPermille, int Glory, (TechFx Fx, int Amount)[] Fx,
                               string Effect, string Lore, WonderKind Kind = WonderKind.Plain);

/// <summary>
/// The race for the wonders of the world (IDEAS «гонка чудес»): each wonder stands once in the world, the first nation
/// to pay its price owns it, the others get half of what they put in back. A nation builds one wonder at a time in its
/// capital: 30% of every cycle's surplus goes into it by itself, and «Вложить» pours in the treasury and the store at
/// once. Wonders bring glory (NationState.Glory, with the world firsts and the chronicle's «наследие»), which counts in
/// the leaderboard (×5) — the long game has something to win. Pure C#, integers, deterministic.
/// </summary>
public static class Wonders
{
    static (TechFx, int)[] F(params (TechFx, int)[] fx) => fx;

    public static readonly WonderDef[] All =
    {
        // ---- Первобытная ----
        new("stone_circle", "Каменный круг", 0, 1000, 30, F((TechFx.Mood, 2), (TechFx.Science, 1)),
            "Довольство +2, изучение +1", "Двадцать камней поставили так, что солнце в день разлива встаёт ровно в просвет. Зачем — объяснят потом."),
        new("ancestor_mound", "Курган предков", 0, 1000, 30, F((TechFx.Mood, 2), (TechFx.CityInfluence, 1)),
            "Довольство +2, влияние городов +1", "Сюда приходят говорить с дедами. Деды молчат, но слушают — так считается."),
        new("painted_cave", "Расписная пещера", 0, 1000, 30, F((TechFx.Science, 1), (TechFx.ShrineMood, 2)),
            "Изучение +1, святилища: довольство +2", "Охра, уголь и сажа. Бизоны на стене бегут уже тысячу лет и всё никак не убегут."),
        // ---- Древний мир ----
        new("great_pyramid", "Великая пирамида", 1, 1500, 60, F((TechFx.AdminLimit, 4), (TechFx.Mood, 1)),
            "Предел управления +4, довольство +1 · в полтора раза дороже, много славы", "Каждый камень весит как дом. Зачем столько — знает только фараон, а он уже не скажет."),
        new("great_granary", "Великий амбар", 1, 1000, 40, F((TechFx.CapPermille, 60), (TechFx.Mood, 1)),
            "Предел населения +6%, довольство +1", "Зерна на семь тощих лет. Мыши тоже в курсе."),
        new("hanging_gardens", "Висячие сады", 1, 1000, 40, F((TechFx.Mood, 4), (TechFx.CapPermille, 50)),
            "Довольство +4, предел населения +5%", "Деревья на крыше. Поливать их носят воду по лестнице, и ни один водонос не жалуется вслух."),
        new("great_ziggurat", "Великий зиккурат", 1, 1000, 40, F((TechFx.ShrineMood, 3), (TechFx.CityInfluence, 2)),
            "Святилища: довольство +3, влияние городов +2", "Ступени до неба. Жрецы говорят, что наверху ближе к богам; рабочие говорят, что наверху ветрено."),
        new("great_lighthouse", "Великий маяк", 1, 1000, 40, F((TechFx.ScoutRange, 1), (TechFx.TaxPermille, 50)),
            "Разведчики видят на 1 провинцию дальше, налоги +5%", "Огонь на башне виден за день пути. Корабли идут на него, купцы идут за кораблями."),
        // ---- Античность ----
        new("great_library", "Великая библиотека", 2, 1000, 50, F((TechFx.Science, 3), (TechFx.AdminLimit, 6)),
            "Изучение +3, предел управления +6", "Каждый корабль в порту обязан отдать свитки переписчикам. Корабли уходят легче, библиотека тяжелеет."),
        new("great_road", "Великая дорога", 2, 1000, 50, F((TechFx.CityInfluence, 4), (TechFx.AdminLimit, 4)),
            "Влияние городов +4, предел управления +4", "Мощёная, прямая, с верстовыми камнями. По ней вести идут быстрее, чем по реке."),
        new("amphitheatre", "Амфитеатр", 2, 1000, 50, F((TechFx.Mood, 4)),
            "Довольство +4", "Хлеба хватает не всегда. Зрелищ теперь хватает всегда."),
        new("colossus", "Колосс", 2, 1000, 50, F((TechFx.TaxPermille, 100)),
            "Налоги +10%", "Медный великан у гавани. Купцы клянутся, что он им подмигивает."),
        // ---- Средневековье ----
        new("first_university", "Первый университет", 3, 1000, 60, F((TechFx.Science, 4), (TechFx.TaxPermille, 50)),
            "Изучение +4, налоги +5%", "Студенты спорят до утра, профессора до обеда. Город доволен: студенты много едят."),
        new("slow_cathedral", "Собор, который строят 300 лет", 3, 2000, 120, F((TechFx.ShrineMood, 4), (TechFx.Mood, 3)),
            "Святилища: довольство +4, довольство +3 · вдвое дороже", "Заложил прадед, достраивает правнук. Архитектор каждые сорок лет новый, чертёж всё тот же."),
        new("great_wall", "Великая стена", 3, 1000, 60, F((TechFx.AdminLimit, 8), (TechFx.Mood, 2)),
            "Предел управления +8, довольство +2", "Стена видна с любой горы. За ней спокойно — по крайней мере, так кажется изнутри."),
        new("tsar_bell", "Царь-колокол", 3, 1000, 60, F((TechFx.ShrineMood, 3)),
            "Святилища: довольство +3 — если не треснет (40%). Треснувший даёт полторы славы", "Отлили самый большой колокол в мире. Звонить в него пока не пробовали.", WonderKind.Bell),
        // ---- Возрождение ----
        new("observatory", "Обсерватория", 4, 1000, 60, F((TechFx.Science, 4), (TechFx.TaxPermille, 50)),
            "Изучение +4, налоги +5%", "Звёзд оказалось больше, чем думали. Астрологи в панике, мореходы в восторге."),
        new("botanical_garden", "Ботанический сад", 4, 1000, 60, F((TechFx.CapPermille, 100)),
            "Предел населения +10%", "Заморские клубни прижились. Теперь их едят все, хотя сначала боялись."),
        new("potemkin_village", "Потёмкинская деревня", 4, 200, 60, F(),
            "Слава как у настоящего чуда за пятую часть цены — пока к границам не подойдёт сосед. Тогда фасады падают: слава пропадает, довольство −5", "Фасады свежевыкрашены, жители улыбаются по команде.", WonderKind.Potemkin),
        new("tsar_cannon", "Царь-пушка", 4, 1000, 90, F(),
            "Слава, и больше ничего: из неё ни разу не выстрелили", "Ядра к ней тоже отлили. Каждое — больше ствола.", WonderKind.Cannon),
        // ---- Эпоха пара ----
        new("great_harbour", "Великая гавань", 5, 1000, 70, F((TechFx.TaxPermille, 100), (TechFx.CityInfluence, 2)),
            "Налоги +10%, влияние городов +2", "Причалы на сотню кораблей. Таможня считает и днём, и ночью."),
        new("beacon_statue", "Статуя-маяк", 5, 1000, 70, F((TechFx.CapPermille, 80), (TechFx.CityInfluence, 3)),
            "Предел населения +8%, влияние городов +3", "Факел в поднятой руке. К ней плывут те, кому дома тесно."),
        new("grand_canal", "Большой канал", 5, 1000, 70, F((TechFx.TaxPermille, 100), (TechFx.AdminLimit, 6)),
            "Налоги +10%, предел управления +6", "Два моря стали одним путём. Карты перерисовывают всем миром."),
        new("crystal_palace", "Хрустальный дворец", 5, 1000, 70, F((TechFx.TaxPermille, 80), (TechFx.Mood, 2)),
            "Налоги +8%, довольство +2 · хозяин Всемирной выставки", "Стекло и чугун. Внутри — всё лучшее, что умеет мир; снаружи — очередь на полдня."),
        // ---- Индустриальная ----
        new("transcontinental", "Трансконтинентальная магистраль", 6, 1000, 80, F((TechFx.CityInfluence, 5), (TechFx.AdminLimit, 10)),
            "Влияние городов +5, предел управления +10", "От океана до океана за неделю. Часы по всей стране пришлось поставить одинаково."),
        new("giant_port", "Порт-гигант", 6, 1000, 80, F((TechFx.TaxPermille, 150)),
            "Налоги +15%", "Краны выше соборов. Корабли разгружают быстрее, чем их успевают нагрузить."),
        new("iron_tower", "Железная башня", 6, 1000, 120, F((TechFx.Mood, 2)),
            "Довольство +2, двойная слава", "Её строили на двадцать лет. Сносить так никто и не решился."),
        // ---- Атомная ----
        new("olympic_stadium", "Олимпийский стадион", 7, 1000, 100, F((TechFx.Mood, 5)),
            "Довольство +5", "Мир собирается бегать, прыгать и спорить о судьях. Хозяин игр собирает славу."),
        new("great_dam", "Великая плотина", 7, 1000, 90, F((TechFx.CapPermille, 100), (TechFx.QuarryMaterials, 1)),
            "Предел населения +10%, каменоломни +1", "Река встала стеной. Ниже по течению впервые за век не боятся разлива."),
        new("first_npp", "Первая АЭС", 7, 1000, 90, F((TechFx.TaxPermille, 100), (TechFx.MineMaterials, 2)),
            "Налоги +10%, рудники +2", "Энергия из ничего. Ну, почти из ничего."),
        // ---- Информационная ----
        new("world_network", "Всемирная сеть", 8, 1000, 100, F((TechFx.Science, 5), (TechFx.AdminLimit, 12)),
            "Изучение +5, предел управления +12", "Каждый может спросить что угодно. Ответы пока бывают разные."),
        new("seed_vault", "Хранилище семян", 8, 1000, 100, F((TechFx.CapPermille, 150)),
            "Предел населения +15%", "Во льдах лежат семена всего, что когда-либо росло. На всякий случай."),
        new("cyber_arena", "Киберарена", 8, 1000, 100, F((TechFx.Mood, 6)),
            "Довольство +6", "Зрелища без границ: болеют за тех, кого никогда не видели вживую."),
        // ---- Космическая ----
        new("radio_telescope", "Радиотелескоп", 9, 1000, 110, F((TechFx.ScoutRange, 3)),
            "Разведчики видят на 3 провинции дальше", "Слушает небо. Небо пока молчит, но телескоп терпелив."),
        new("orbital_station", "Орбитальная станция", 9, 1000, 130, F((TechFx.AdminLimit, 15), (TechFx.Science, 4)),
            "Предел управления +15, изучение +4", "Постоянный дом на орбите. Вид из окна — вся держава сразу."),
        // ---- Будущее ----
        new("hyperloop", "Гиперпетля", 10, 1000, 130, F((TechFx.CityInfluence, 8), (TechFx.AdminLimit, 15)),
            "Влияние городов +8, предел управления +15", "Из столицы в любой город — за время одной чашки чая."),
        new("digital_immortality", "Цифровое бессмертие", 10, 1000, 140, F((TechFx.Mood, 5)),
            "Довольство +5 · правитель больше не умирает", "Портрет правителя теперь голограмма. Голограмма улыбается чаще."),
        new("generation_ark", "Ковчег поколений", 10, 1500, 250, F(),
            "Корабль к другим звёздам: величайшая слава мира", "Тысяча человек, семена, книги и одна кошка. Путь — двести лет."),
    };

    public static int Count => All.Length;

    /// <summary>Allocate the world's wonder record (all free).</summary>
    public static void Init(GameState s)
    {
        if (s.WonderOwner != null && s.WonderOwner.Length == All.Length) return;
        s.WonderOwner = new sbyte[All.Length];
        s.WonderFlag = new byte[All.Length];
        Array.Fill(s.WonderOwner, (sbyte)-1);
    }

    /// <summary>No glory, nothing built or under way (hashes as the layouts before wonders did).</summary>
    public static bool IsBlank(GameState s)
    {
        foreach (var x in s.Nat) if (x.Glory != 0 || x.Wonder >= 0 || x.WonderGold != 0 || x.WonderMats != 0) return false;
        if (s.WonderOwner != null) foreach (sbyte o in s.WonderOwner) if (o >= 0) return false;
        return true;
    }

    public static int Index(string id)
    {
        for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return i;
        return -1;
    }

    // ---------------------------------------------------------------- prices

    /// <summary>Gold a wonder costs: 2500 × (era + 1) × (era + 4) / 4, times its share (the cathedral twice, the village a fifth).</summary>
    public static long GoldCost(int w) => 2500L * (All[w].Era + 1) * (All[w].Era + 4) / 4 * All[w].CostPermille / 1000;

    /// <summary>Materials it takes: 2000 × (era + 1)², times its share.</summary>
    public static long MatCost(int w) => 2000L * (All[w].Era + 1) * (All[w].Era + 1) * All[w].CostPermille / 1000;

    /// <summary>The share of every cycle's surplus (gold and materials) that goes into the wonder under construction, %.</summary>
    public const int AutoPct = 30;
    /// <summary>A wonder lost to a rival gives back this share of what was put in, %.</summary>
    public const int RefundPct = 50;
    /// <summary>Glory counts this many points in the leaderboard.</summary>
    public const int GloryScore = 5;

    // ---------------------------------------------------------------- state helpers

    public static bool Available(GameState s, int n, int w) => (uint)w < (uint)All.Length && s.WonderOwner[w] < 0 && s.Nat[n].Era >= All[w].Era;

    public static WonderError CheckStart(GameState s, int n, int w)
    {
        if ((uint)w >= (uint)All.Length) return WonderError.Unknown;
        if (s.WonderOwner[w] >= 0) return WonderError.Taken;
        if (s.Nat[n].Era < All[w].Era) return WonderError.TooEarly;
        if (s.NationCapital[n] < 0) return WonderError.NoCapital;
        if (s.Nat[n].Wonder >= 0) return WonderError.Busy;
        return WonderError.None;
    }

    public static void Start(GameState s, int n, int w)
    {
        var nat = s.Nat[n];
        nat.Wonder = w; nat.WonderGold = 0; nat.WonderMats = 0;
    }

    /// <summary>Give the construction up: half of what went in comes back.</summary>
    public static void Stop(GameState s, int n)
    {
        var nat = s.Nat[n];
        if (nat.Wonder < 0) return;
        nat.Treasury += nat.WonderGold * RefundPct / 100;
        nat.Materials += nat.WonderMats * RefundPct / 100;
        nat.Wonder = -1; nat.WonderGold = 0; nat.WonderMats = 0;
    }

    /// <summary>Pour gold (whole units) and materials into the wonder, up to what the nation has and the wonder still
    /// needs; -1 = all there is. Returns false when there is nothing to give.</summary>
    public static bool Invest(GameState s, int n, int gold = -1, int mats = -1)
    {
        var nat = s.Nat[n];
        if (nat.Wonder < 0) return false;
        long g = Math.Min(Math.Max(0, nat.Treasury), GoldCost(nat.Wonder) * Rules.Cents - nat.WonderGold);
        long m = Math.Min(Math.Max(0, nat.Materials), MatCost(nat.Wonder) - nat.WonderMats);
        if (gold >= 0) g = Math.Min(g, (long)gold * Rules.Cents);
        if (mats >= 0) m = Math.Min(m, mats);
        if (g <= 0 && m <= 0) return false;
        nat.Treasury -= g; nat.WonderGold += g;
        nat.Materials -= m; nat.WonderMats += m;
        return true;
    }

    /// <summary>‰ of the wonder under construction paid (the lesser of gold and materials).</summary>
    public static int ProgressPermille(NationState nat)
    {
        if (nat.Wonder < 0) return 0;
        long g = nat.WonderGold * 1000 / Math.Max(1, GoldCost(nat.Wonder) * Rules.Cents);
        long m = nat.WonderMats * 1000 / Math.Max(1, MatCost(nat.Wonder));
        return (int)Math.Min(1000, Math.Min(g, m));
    }

    public static int Owned(GameState s, int n)
    {
        int k = 0;
        for (int w = 0; w < All.Length; w++) if (s.WonderOwner[w] == n && !Fallen(s, w)) k++;
        return k;
    }

    /// <summary>Flags per wonder: the bell cracked, the village's facades fell.</summary>
    public const byte Cracked = 1, FacadesFell = 2;
    public static bool Fallen(GameState s, int w) => (s.WonderFlag[w] & FacadesFell) != 0;

    // ---------------------------------------------------------------- bonuses (cached per nation, added by Techs.Sum)

    public static int Fx(NationState nat, TechFx fx) => nat.WonderFx == null ? 0 : nat.WonderFx[(int)fx];

    /// <summary>Rebuild every nation's wonder bonuses from the owners (after a wonder is finished, falls, or a save is loaded).</summary>
    public static void Refresh(GameState s)
    {
        int nFx = Enum.GetValues<TechFx>().Length;
        foreach (var nat in s.Nat)
        {
            if (nat.WonderFx == null || nat.WonderFx.Length != nFx) nat.WonderFx = new int[nFx];
            else Array.Clear(nat.WonderFx);
        }
        if (s.WonderOwner == null) return;
        for (int w = 0; w < All.Length; w++)
        {
            int o = s.WonderOwner[w];
            if (o < 0 || o >= s.Nat.Length || Fallen(s, w)) continue;
            if (All[w].Kind == WonderKind.Bell && (s.WonderFlag[w] & Cracked) != 0) continue;   // a cracked bell gives glory, not peace
            foreach (var (f, a) in All[w].Fx) s.Nat[o].WonderFx[(int)f] += a;
        }
    }

    // ---------------------------------------------------------------- the cycle

    /// <summary>The Potemkin village's facades are checked every this many cycles (4 s at speed 3).</summary>
    public const int FacadeCycles = 8;

    /// <summary>One rules cycle: every builder puts its share in; a paid wonder is finished (the rivals get half back);
    /// every few cycles the village's facades are checked.</summary>
    public static void Cycle(WorldData w, GameState s, int cycle, ISimSink sink)
    {
        if (s.WonderOwner == null) return;
        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            if (nat.Wonder < 0) continue;
            long net = nat.LastTaxes - nat.LastUpkeep;
            long g = Math.Min(Math.Max(0, nat.Treasury), Math.Max(0, net) * AutoPct / 100);
            long m = Math.Min(Math.Max(0, nat.Materials), Math.Max(0, (long)nat.LastMaterials) * AutoPct / 100);
            g = Math.Min(g, GoldCost(nat.Wonder) * Rules.Cents - nat.WonderGold);
            m = Math.Min(m, MatCost(nat.Wonder) - nat.WonderMats);
            nat.Treasury -= Math.Max(0, g); nat.WonderGold += Math.Max(0, g);
            nat.Materials -= Math.Max(0, m); nat.WonderMats += Math.Max(0, m);
        }
        // finished wonders, the lowest nation index first among those done this very cycle
        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            int wd = nat.Wonder;
            if (wd < 0 || nat.WonderGold < GoldCost(wd) * Rules.Cents || nat.WonderMats < MatCost(wd)) continue;
            Finish(w, s, n, wd, cycle, sink);
        }
        if (cycle % FacadeCycles == 0) Facades(w, s, sink);
    }

    static void Finish(WorldData w, GameState s, int n, int wd, int cycle, ISimSink sink)
    {
        var def = All[wd];
        var nat = s.Nat[n];
        s.WonderOwner[wd] = (sbyte)n;
        nat.Wonder = -1; nat.WonderGold = 0; nat.WonderMats = 0;
        int glory = def.Glory;
        if (def.Kind == WonderKind.Bell && SimRng.Chance(w.Seed, 81, wd, cycle, 2, 5))
        {
            s.WonderFlag[wd] |= Cracked;
            glory = glory * 3 / 2;
        }
        nat.Glory += glory;
        Firsts.OnWonder(w, s, n, sink);
        for (int r = 0; r < s.Nat.Length; r++)   // the rivals: half of their stone and gold comes back
        {
            if (r == n || s.Nat[r].Wonder != wd) continue;
            Stop(s, r);
            if (s.Nat[r].Human && sink != null)
                sink.Notify("diamond", $"{(Met(s, r, n) ? s.Nations[n].Name : "Неизвестная держава")} опередила нас: «{def.Name}» достроено. Половина вложенного вернулась в казну");
        }
        Refresh(s);
        if (sink == null) return;
        if (nat.Human)
        {
            bool cracked = (s.WonderFlag[wd] & Cracked) != 0;
            sink.Notify("diamond", cracked ? $"«{def.Name}» отлит — и треснул. Зато какой! Слава +{glory}" : $"Чудо света «{def.Name}» достроено! {def.Effect}. Слава +{glory}");
        }
        else
            for (int h = 0; h < s.Nat.Length; h++)
                if (s.Nat[h].Human && s.Nat[h].Wonder != wd && Met(s, h, n))
                    sink.Notify("diamond", $"{s.Nations[n].Name} достраивает чудо света «{def.Name}»");
    }

    /// <summary>A Potemkin village stands while no foreign land touches its owner's realm.</summary>
    static void Facades(WorldData w, GameState s, ISimSink sink)
    {
        int wd = Index("potemkin_village");
        if (wd < 0 || s.WonderOwner[wd] < 0 || Fallen(s, wd)) return;
        int n = s.WonderOwner[wd];
        bool seen = false;
        for (int p = 0; p < w.P && !seen; p++)
        {
            if (s.Owner[p] != n) continue;
            foreach (int q in w.Adj[p]) if (s.Owner[q] >= 0 && s.Owner[q] != n) { seen = true; break; }
        }
        if (!seen) return;
        s.WonderFlag[wd] |= FacadesFell;
        s.Nat[n].Glory = Math.Max(0, s.Nat[n].Glory - All[wd].Glory);
        for (int p = 0; p < w.P; p++) if (s.Owner[p] == n) s.Mood[p] = (byte)Math.Max(0, s.Mood[p] - 5);
        Refresh(s);
        if (s.Nat[n].Human) sink?.Notify("diamond", "Соседи подошли к границам и увидели «Потёмкинскую деревню» сбоку. Фасады упали: слава пропала, народ смущён");
    }

    static bool Met(GameState s, int viewer, int n) => Rules.Met(s, viewer, n);

    // ---------------------------------------------------------------- bots

    /// <summary>A bot without a wonder looks for one about once a minute when its treasury holds three claims' worth; it
    /// takes the newest it may build, by taste.</summary>
    public static void BotChoose(WorldData w, GameState s, int n, int cycle)
    {
        var nat = s.Nat[n];
        if (nat.Wonder >= 0 || s.NationCapital[n] < 0 || !SimRng.Chance(w.Seed, 82, n, cycle, 1, 120)) return;
        if (nat.Treasury < 3L * Rules.ClaimPrice(s, n) * Rules.Cents) return;   // land and buildings first: a wonder needs a cushion
        int best = -1; long bs = long.MinValue;
        for (int wd = 0; wd < All.Length; wd++)
        {
            if (CheckStart(s, n, wd) != WonderError.None || All[wd].Kind == WonderKind.Potemkin) continue;
            long sc = All[wd].Era * 1000L + (SimRng.Hash(w.Seed, 83, n, wd) & 1023);   // the newest wonders first, then taste
            if (sc > bs) { bs = sc; best = wd; }
        }
        if (best >= 0) Commands.Apply(w, s, Cmd.WonderStart(n, best), null);
    }

    /// <summary>Bots also put a tenth of a fat treasury in now and then (every ~minute), keeping a claim's price in reserve.</summary>
    public static void BotInvest(WorldData w, GameState s, int n, int cycle)
    {
        var nat = s.Nat[n];
        if (nat.Wonder < 0 || !SimRng.Chance(w.Seed, 84, n, cycle, 1, 120)) return;
        long reserve = (long)Rules.ClaimPrice(s, n) * Rules.Cents * 2;
        long spare = nat.Treasury - reserve;
        if (spare <= 0) return;
        int gold = (int)Math.Min(int.MaxValue, spare / 10 / Rules.Cents), mats = (int)Math.Min(int.MaxValue, Math.Max(0, nat.Materials - 600) / 10);
        if (gold > 0 || mats > 0) Commands.Apply(w, s, Cmd.WonderInvest(n, gold, mats), null);
    }
}
