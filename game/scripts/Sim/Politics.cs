using System;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

public enum CourseError { None, Unknown, Done, Busy, Locked, Closed, NoCapital, NotAdopting }

/// <summary>A course of the policy tree: where it sits on the political compass (X: left −, right +; Y: liberty −,
/// authority +), its ring (0 the centre, 1 the first ring…), what it takes (cycles at «Обычная»), what it gives once
/// adopted (a little gold now and a lasting bonus as technology effects), and how it shapes the people's character.</summary>
public sealed record CourseDef(string Id, string Name, string Quote, string QuoteBy, int Ring, int Dir, int X, int Y,
                               int Cycles, int Gold, string Effect, (TechFx Fx, int Amount)[] Fx, string Opens);

/// <summary>
/// The policy tree (the «Правительство» screen, tab «Политика»): national courses in the manner of national focuses.
/// One course is adopted at a time and takes its cycles; when done it pays a little gold and adds its bonus for good.
/// Everything starts from «Основы государства» (a capital is needed: a wandering tribe has no state yet). Around it the
/// first ring points the eight ways of the political compass — up authority, down liberty, left the commune, right
/// property — mildly at first («не сразу»): a course shuts the three courses facing it, so a nation leans one way. The
/// nation's position on the compass is the sum of its courses. Ids never move: new courses are appended. Pure C#,
/// integers, deterministic.
/// </summary>
public static class Politics
{
    static (TechFx, int)[] F(params (TechFx, int)[] fx) => fx;

    /// <summary>Compass ways of the first ring, clockwise from the top: ↑ ↗ → ↘ ↓ ↙ ← ↖.</summary>
    public static readonly (int X, int Y)[] Ways = { (0, 1), (1, 1), (1, 0), (1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1) };

    public static readonly CourseDef[] All =
    {
        new("foundations", "Основы государства", "Человек по природе своей есть существо политическое", "Аристотель, «Политика»",
            0, -1, 0, 0, 40, 50, "Предел управления +2", F((TechFx.AdminLimit, 2)), "Открывает восемь путей вокруг"),
        new("one_rule", "Единоначалие", "Нет в многовластии блага; да будет единый властитель", "Гомер, «Илиада»",
            1, 0, 0, 1, 240, 80, "Предел управления +4: наместники вождя", F((TechFx.AdminLimit, 4)), "Дальше — путь власти"),
        new("noble_kin", "Знать и род", "Каков род, таков и плод", "пословица",
            1, 1, 1, 1, 240, 80, "Налоги +4%: знатные роды собирают подать", F((TechFx.TaxPermille, 40)), "Дальше — путь знати"),
        new("family_plot", "Наследный надел", "Мой дом — моя крепость", "английская пословица",
            1, 2, 1, 0, 240, 80, "Предел населения +3%: свою землю берегут", F((TechFx.CapPermille, 30)), "Дальше — путь собственности"),
        new("free_trade", "Вольный торг", "Торговать — не мешки ворочать", "пословица",
            1, 3, 1, -1, 240, 80, "Влияние городов +1: торг тянет людей", F((TechFx.CityInfluence, 1)), "Дальше — путь торга"),
        new("assembly", "Вече", "Глас народа — глас божий", "латинская пословица",
            1, 4, 0, -1, 240, 80, "Довольство +3: решают все вместе", F((TechFx.Mood, 3)), "Дальше — путь свободы"),
        new("circle_of_equals", "Круг равных", "Все за одного, один за всех", "девиз",
            1, 5, -1, -1, 240, 80, "Исследования +1: слово каждого в круге", F((TechFx.Science, 1)), "Дальше — путь равенства"),
        new("common_land", "Общая земля", "Земля — общая мать, всех кормит", "поговорка старейшин",
            1, 6, -1, 0, 240, 80, "Предел населения +4%: поля делят по едокам", F((TechFx.CapPermille, 40)), "Дальше — путь общины"),
        new("common_work", "Общий труд", "Один в поле не воин", "пословица",
            1, 7, -1, 1, 240, 80, "Материалы построек −10%: строят всем миром", F((TechFx.MaterialDiscount, 10)), "Дальше — путь общего дела"),
    };

    public static int Count => All.Length;
    public static int Words => (All.Length + 63) / 64;
    public static int Root => 0;

    public static int Index(string id)
    {
        for (int c = 0; c < All.Length; c++) if (All[c].Id == id) return c;
        return -1;
    }

    public static void Init(NationState nat)
    {
        if (nat.Courses == null || nat.Courses.Length != Words) Array.Resize(ref nat.Courses, Words);
    }

    public static bool Has(NationState nat, int c) => (uint)c < (uint)All.Length && nat.Courses != null && (nat.Courses[c >> 6] >> (c & 63) & 1) != 0;

    static void Set(NationState nat, int c)
    {
        Init(nat);
        nat.Courses[c >> 6] |= 1UL << (c & 63);
    }

    /// <summary>Nothing adopted and nothing under way: the snapshot block and the hash are as before the tree.</summary>
    public static bool IsBlank(NationState nat)
    {
        if (nat.CourseNow >= 0 || nat.CourseCycles != 0) return false;
        if (nat.Courses != null) foreach (ulong v in nat.Courses) if (v != 0) return false;
        return true;
    }

    /// <summary>Cycles course c takes in a game of this pace.</summary>
    public static int Total(int c, int pace) => Math.Max(1, All[c].Cycles * Eras.ClampPace(pace) / 1000);

    /// <summary>The gold course c pays nation nat when adopted (whole gold; dearer eras pay more).</summary>
    public static int GoldFor(int c, NationState nat) => (int)((long)All[c].Gold * Policy.EraPermille(nat.Era) / 1000);

    /// <summary>Courses of the first ring facing way d: the opposite one and its two neighbours.</summary>
    static bool Facing(int d, int e) => d >= 0 && e >= 0 && ((d - e + 8) % 8) is 3 or 4 or 5;

    /// <summary>Is c shut for good because the nation took (or is taking) a course facing it?</summary>
    public static bool Shut(NationState nat, int c)
    {
        int d = All[c].Dir;
        if (d < 0) return false;
        for (int o = 0; o < All.Length; o++)
            if ((Has(nat, o) || nat.CourseNow == o) && o != c && All[o].Ring == All[c].Ring && Facing(d, All[o].Dir)) return true;
        return false;
    }

    /// <summary>The course needed before c: the centre for the first ring.</summary>
    public static int Parent(int c) => All[c].Ring == 0 ? -1 : Root;

    public static CourseError CheckStart(GameState s, int n, int c)
    {
        var nat = s.Nat[n];
        if ((uint)c >= (uint)All.Length) return CourseError.Unknown;
        if (Has(nat, c)) return CourseError.Done;
        if (nat.CourseNow >= 0) return CourseError.Busy;
        if (s.NationCapital[n] < 0 || Nomads.IsNomad(nat)) return CourseError.NoCapital;
        if (Parent(c) is int p and >= 0 && !Has(nat, p)) return CourseError.Locked;
        if (Shut(nat, c)) return CourseError.Closed;
        return CourseError.None;
    }

    internal static void Start(NationState nat, int c) { nat.CourseNow = c; nat.CourseCycles = 0; }

    internal static void Stop(NationState nat) { nat.CourseNow = -1; nat.CourseCycles = 0; }

    /// <summary>Where the nation stands on the compass: the sum of its courses (X right, Y authority).</summary>
    public static (int X, int Y) Position(NationState nat)
    {
        int x = 0, y = 0;
        for (int c = 0; c < All.Length; c++) if (Has(nat, c)) { x += All[c].X; y += All[c].Y; }
        return (x, y);
    }

    public static int Fx(NationState nat, TechFx fx) => nat.CourseFx == null ? 0 : nat.CourseFx[(int)fx];

    public static void Refresh(NationState nat)
    {
        int nFx = Enum.GetValues<TechFx>().Length;
        if (nat.CourseFx == null || nat.CourseFx.Length != nFx) nat.CourseFx = new int[nFx];
        else Array.Clear(nat.CourseFx);
        for (int c = 0; c < All.Length; c++)
            if (Has(nat, c)) foreach (var (f, a) in All[c].Fx) nat.CourseFx[(int)f] += a;
    }

    /// <summary>Every rules cycle: the course under way moves on; done, it pays its gold, adds its bonus and shapes the people.</summary>
    public static void Cycle(WorldData w, GameState s, int cycle, ISimSink sink)
    {
        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            int c = nat.CourseNow;
            if (c < 0) continue;
            if (s.NationCapital[n] < 0) { Stop(nat); continue; }   // the capital was lost: the course with it
            if (++nat.CourseCycles < Total(c, s.Pace)) continue;
            Stop(nat);
            Set(nat, c);
            Refresh(nat);
            int gold = GoldFor(c, nat);
            nat.Treasury += gold * Rules.Cents;
            var d = All[c];
            // the compass shapes the people: the commune or free hands, tradition or openness
            if (d.X != 0) Character.Deed(s, n, Character.Commune, d.X > 0, 10);
            if (d.Y > 0) Character.Deed(s, n, Character.Tradition, false, 10);
            if (d.Y < 0) Character.Deed(s, n, Character.Openness, true, 10);
            Character.Refresh(nat);
            if (nat.Human) sink?.Notify("building-bank", $"Принят курс «{d.Name}»: {d.Effect}. В казну +{gold} золота");
        }
    }

    /// <summary>A bot takes the next course now and then: the centre first, then a way of its own (salted, so bots differ).</summary>
    public static void BotChoose(WorldData w, GameState s, int n, int cycle)
    {
        var nat = s.Nat[n];
        if (nat.CourseNow >= 0 || !SimRng.Chance(w.Seed, 201, n, cycle, 1, 60)) return;
        int best = -1; uint bs = 0;
        for (int c = 0; c < All.Length; c++)
        {
            if (CheckStart(s, n, c) != CourseError.None) continue;
            uint sc = c == Root ? uint.MaxValue : SimRng.Hash(w.Seed, 202, n, c) | 1;
            if (sc > bs) { bs = sc; best = c; }
        }
        if (best >= 0) Commands.Apply(w, s, Cmd.Course(n, best, true), null);
    }
}
