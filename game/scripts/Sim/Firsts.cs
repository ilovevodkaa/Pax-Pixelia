using System;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

/// <summary>A world first: its title, the deed in the present tense («… первой в мире основывает новый город»), the
/// permanent bonus it gives (technology effects) and the push it gives the holder's character.</summary>
public sealed record FirstDef(string Name, string Deed, string Effect, string Icon, (TechFx Fx, int Amount)[] Fx,
                              int Scale = -1, bool Right = false, int Push = 0);

/// <summary>
/// Мировые первенства (CONTENT §10.3): one prize per deed for the whole world, taken by the first nation to do it and
/// held for good. Event deeds (a settled hearth, a new town, gold, a technology) are awarded the moment they happen;
/// standing deeds (a mine, three peoples met, a great city, a big realm) are checked every <see cref="CheckCycles"/>
/// rules cycles, ties going to the bigger number, then the lower nation index. Every human hears of it — by name if
/// they have met the holder, otherwise as «некий далёкий народ». Bonuses join the character's (NationState.CharFx).
/// </summary>
public static class Firsts
{
    public const int Hearth = 0, NewTown = 1, Mine = 2, Gold = 3, ThreePeoples = 4, Writing = 5, GreatFork = 6, GreatCity = 7, Realm = 8, FirstWonder = 9;
    /// <summary>Glory every world first brings besides its bonus (Wonders: glory counts in the leaderboard).</summary>
    public const int FirstGlory = 15;
    public const int CheckCycles = 4;
    public const int GreatCityPeople = 250_000, RealmProvinces = 50, Peoples = 3;

    public static readonly FirstDef[] All =
    {
        new("Первый очаг", "оседает у постоянного очага", "Довольство +2", "home", new[] { (TechFx.Mood, 2) },
            Character.Tradition, false, 5),
        new("Дочерний город", "основывает новый город", "Влияние городов +1", "map-pin", new[] { (TechFx.CityInfluence, 1) },
            Character.Openness, true, 5),
        new("Первый рудник", "добывает металл из рудника", "Рудники: +1 материал", "pick", new[] { (TechFx.MineMaterials, 1) },
            Character.Tradition, true, 3),
        new("Золотая жила", "находит золото", "Налоги +5%", "coins", new[] { (TechFx.TaxPermille, 50) },
            Character.Agri, true, 5),
        new("Три костра", "знакомится с тремя народами", "Налоги +3%", "users", new[] { (TechFx.TaxPermille, 30) },
            Character.Openness, true, 10),
        new("Первое слово", "записывает слово", "Изучение +1 в цикл", "book", new[] { (TechFx.Science, 1) },
            Character.Faith, true, 5),
        new("Великий путь", "выбирает свой путь на Великой развилке", "Довольство +2", "compass", new[] { (TechFx.Mood, 2) },
            Character.Tradition, true, 5),
        new("Великий город", "растит город в 250 тысяч жителей", "Предел населения +5%", "building-bank", new[] { (TechFx.CapPermille, 50) },
            Character.Agri, false, 5),
        new("Держава пятидесяти земель", "собирает 50 провинций", "Налоги +3%", "flag", new[] { (TechFx.TaxPermille, 30) },
            Character.Commune, false, 5),
        new("Чудо света", "возводит чудо света", "Довольство +2", "diamond", new[] { (TechFx.Mood, 2) },
            Character.Faith, false, 5),
    };

    public static int Count => All.Length;

    /// <summary>Allocate the record of who took what (all open).</summary>
    public static void Init(GameState s)
    {
        if (s.FirstHolder != null && s.FirstHolder.Length == Count) return;
        s.FirstHolder = new short[Count];
        s.FirstCycle = new int[Count];
        Array.Fill(s.FirstHolder, (short)-1);
    }

    /// <summary>Holder of first f, or -1 while it is open.</summary>
    public static int Holder(GameState s, int f) => s.FirstHolder == null ? -1 : s.FirstHolder[f];

    public static bool Has(NationState nat, int f) => (nat.Firsts & (1 << f)) != 0;

    /// <summary>No first taken yet (hashes as the layouts before firsts did).</summary>
    public static bool IsBlank(GameState s)
    {
        if (s.FirstHolder == null) return true;
        foreach (short h in s.FirstHolder) if (h >= 0) return false;
        return true;
    }

    /// <summary>Rebuild every nation's NationState.Firsts bits from the record (after a load).</summary>
    public static void Sync(GameState s)
    {
        foreach (var nat in s.Nat) nat.Firsts = 0;
        if (s.FirstHolder == null) return;
        for (int f = 0; f < Count; f++)
        {
            int h = s.FirstHolder[f];
            if ((uint)h < (uint)s.Nat.Length) s.Nat[h].Firsts |= 1 << f;
        }
        foreach (var nat in s.Nat) Character.Refresh(nat);
    }

    // ------------------------------------------------------------------ deeds of the moment

    /// <summary>A tribe settled: the first hearth, if another tribe still wanders (a game started settled has no race).</summary>
    public static void OnSettle(WorldData w, GameState s, int n, ISimSink sink)
    {
        if (Holder(s, Hearth) >= 0) return;
        for (int m = 0; m < s.Nat.Length; m++)
            if (m != n && Nomads.IsNomad(s.Nat[m])) { Award(w, s, Hearth, n, sink); return; }
    }

    public static void OnFound(WorldData w, GameState s, int n, ISimSink sink)
    {
        if (Holder(s, NewTown) < 0) Award(w, s, NewTown, n, sink);
    }

    public static void OnSurvey(WorldData w, GameState s, int n, int p, ISimSink sink)
    {
        if (Holder(s, Gold) < 0 && s.Ore[p] == Rules.OreGold) Award(w, s, Gold, n, sink);
    }

    public static void OnLearn(WorldData w, GameState s, int n, int t, ISimSink sink)
    {
        if (Holder(s, Writing) < 0 && Techs.All[t].Id == "writing") Award(w, s, Writing, n, sink);
        if (Holder(s, GreatFork) < 0 && Techs.All[t].Fork >= 0) Award(w, s, GreatFork, n, sink);
    }

    public static void OnWonder(WorldData w, GameState s, int n, ISimSink sink)
    {
        if (Holder(s, FirstWonder) < 0) Award(w, s, FirstWonder, n, sink);
    }

    // ------------------------------------------------------------------ standing deeds

    /// <summary>Every few cycles: mines, peoples met, the biggest city and the biggest realm, in one pass over the map.</summary>
    public static void Cycle(WorldData w, GameState s, int cycle, ISimSink sink)
    {
        if (cycle % CheckCycles != 0 || s.FirstHolder == null) return;
        bool mine = Holder(s, Mine) < 0, peoples = Holder(s, ThreePeoples) < 0, city = Holder(s, GreatCity) < 0 && s.City != null,
             realm = Holder(s, Realm) < 0;
        if (!mine && !peoples && !city && !realm) return;
        int nN = s.Nat.Length;
        if (nN > 64) peoples = false;   // contact masks are one word
        var sc = SimScratch.For(w, s);
        if (sc.FirstProvinces.Length != nN)
        {
            sc.FirstProvinces = new int[nN]; sc.FirstMines = new int[nN]; sc.FirstPeoples = new int[nN]; sc.Contact = new ulong[nN];
        }
        var provinces = sc.FirstProvinces; var mines = sc.FirstMines; var met = sc.FirstPeoples; var contact = sc.Contact;
        var cityPop = sc.CityPop;
        Array.Clear(provinces); Array.Clear(mines); Array.Clear(contact);
        if (city) Array.Clear(cityPop);
        for (int p = 0; p < w.P; p++)
        {
            int o = s.Owner[p];
            if (o < 0) continue;
            provinces[o]++;
            if (mine && Rules.IsMine(s, p)) mines[o]++;
            if (city && s.City[p] >= 0) cityPop[s.City[p]] += s.Pop[p];
            if (peoples)
                foreach (int q in w.Adj[p])
                {
                    int m = s.Owner[q];
                    if (m >= 0 && m != o) contact[o] |= 1UL << m;
                }
        }
        if (peoples)
            for (int n = 0; n < nN; n++)
            {
                if (s.Nat[n].Fog is { } f)
                    for (int m = 0; m < nN && m < f.Met.Length; m++) if (m != n && f.Met[m]) contact[n] |= 1UL << m;
                met[n] = System.Numerics.BitOperations.PopCount(contact[n]);
            }

        if (mine) Best(w, s, Mine, mines, 1, sink);
        if (peoples) Best(w, s, ThreePeoples, met, Peoples, sink);
        if (realm) Best(w, s, Realm, provinces, RealmProvinces, sink);
        if (city)
        {
            long best = GreatCityPeople - 1; int who = -1;
            for (int c = 0; c < w.P; c++)
                if (cityPop[c] > best && s.Owner[c] >= 0) { best = cityPop[c]; who = s.Owner[c]; }
            if (who >= 0) Award(w, s, GreatCity, who, sink);
        }
    }

    /// <summary>The nation with the biggest value of at least `need` takes first f (ties: the lower index).</summary>
    static void Best(WorldData w, GameState s, int f, int[] value, int need, ISimSink sink)
    {
        int who = -1, top = need - 1;
        for (int n = 0; n < value.Length; n++) if (value[n] > top) { top = value[n]; who = n; }
        if (who >= 0) Award(w, s, f, who, sink);
    }

    // ------------------------------------------------------------------ the prize

    static void Award(WorldData w, GameState s, int f, int n, ISimSink sink)
    {
        var d = All[f];
        s.FirstHolder[f] = (short)n;
        s.FirstCycle[f] = Clock.CycleOf(s.Tick);
        s.Nat[n].Firsts |= 1 << f;
        if (d.Push > 0) Character.Deed(s, n, d.Scale, d.Right, d.Push);
        s.Nat[n].Glory += FirstGlory;
        Character.Refresh(s.Nat[n]);
        if (sink == null) return;
        for (int h = 0; h < s.Nat.Length; h++)
        {
            if (!s.Nat[h].Human) continue;
            if (h == n) sink.Notify("trophy", $"{s.Nations[n].Name} первой в мире {d.Deed}! Первенство «{d.Name}» наше навсегда. {d.Effect}");
            else if (Rules.Met(s, h, n)) sink.Notify("trophy", $"{s.Nations[n].Name} первой в мире {d.Deed}. Первенство «{d.Name}» уходит к ним");
            else sink.Notify("trophy", $"Некий далёкий народ первым в мире {d.Deed}. Первенство «{d.Name}» уже не наше");
        }
    }

    /// <summary>Who holds first f as viewer h may know it: the name, «некий далёкий народ», or null while it is open.</summary>
    public static string HolderText(GameState s, int f, int viewer)
    {
        int n = Holder(s, f);
        if (n < 0) return null;
        return Rules.Met(s, viewer, n) ? s.Nations[n].Name : "некий далёкий народ";
    }
}
