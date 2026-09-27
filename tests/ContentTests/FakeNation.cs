using System;
using System.Collections.Generic;
using PaxPixelia.Content;

namespace PaxPixelia.Tests;

/// <summary>
/// The calendar of the dry run: era lengths in hours at speed 3 for the «Обычная» preset (25 h in total, GDD 27.09)
/// scaled by the pace, with the calendar years each era spans. Content speaks in cycles: 2 per second at speed 3.
/// </summary>
sealed class Timeline
{
    public const int CyclesPerHour = 7200;
    static readonly double[] Hours = { 1.5, 2.5, 3, 3, 2.5, 2.5, 2.5, 2, 2, 2, 1.5 };
    static readonly int[] StartYear = { -3500, -3000, -800, 500, 1450, 1750, 1870, 1945, 1990, 2030, 2100, 2200 };
    readonly long[] _start = new long[12];

    public long Total => _start[11];

    public Timeline(int pacePermille)
    {
        for (int e = 0; e < 11; e++) _start[e + 1] = _start[e] + (long)(Hours[e] * CyclesPerHour * pacePermille / 1000);
    }

    public int EraAt(long tick)
    {
        int e = 0;
        while (e < 10 && tick >= _start[e + 1]) e++;
        return e;
    }

    public long EraStart(int era) => _start[era];
    public long EraLength(int era) => _start[era + 1] - _start[era];

    public int YearAt(long tick)
    {
        int e = EraAt(tick);
        long into = tick - _start[e], len = Math.Max(1, _start[e + 1] - _start[e]);
        int y = StartYear[e] + (int)((StartYear[e + 1] - StartYear[e]) * into / len);
        return y == 0 ? 1 : y;
    }
}

/// <summary>
/// A scripted nation for the dry run: its land, stats and buildings grow with the era, the «simulation» sets tech,
/// ruler and quirk flags on a schedule, and effects from the deck move its numbers. Implements both sides of the
/// deck's contract the way the real GameState adapter will.
/// </summary>
sealed class FakeNation : IEventState, IEventSink
{
    public readonly int Id;
    public readonly string Name;
    readonly Terrain[] _landBits;
    readonly int _eraLag;
    readonly HashSet<string> _systems;
    readonly Dictionary<string, int> _buildings = new();
    readonly int _seed;

    public int Era { get; private set; }
    public int Year { get; private set; }
    public int Capital => Id * 1000;

    public long Gold = 300, Population = 3000;
    public int Mood = 60, Stability = 60, Science, Culture, Faith, Legacy, Food = 100, Provinces = 1, Met;
    public int Unrest, Heirs = 1, FoodPrice = 1000, NeighborMood = 70, Pollution, Peacefulness;
    public int Axes, Modifiers, Traits;

    public readonly List<EventRecord> Log = new();
    public EventRecord? Open;
    public long AnswerAt;

    public FakeNation(int id, string name, Terrain land, int eraLag, HashSet<string> systems, int seed)
    {
        Id = id; Name = name; _eraLag = eraLag; _systems = systems; _seed = seed;
        var bits = new List<Terrain>();
        for (int b = 1; b <= (int)Terrain.Lake; b <<= 1) if (((int)land & b) != 0) bits.Add((Terrain)b);
        _landBits = bits.ToArray();
    }

    /// <summary>Slow «simulation» step (every 10 minutes of play): era, land, buildings, and the flags the Sim owns.</summary>
    public void Advance(long tick, Timeline t, EventRunner runner, EventMemory m)
    {
        int leaderEra = t.EraAt(tick);
        Era = Math.Max(0, leaderEra - _eraLag);
        Year = t.YearAt(tick);
        long intoEra = tick - t.EraStart(leaderEra);
        int step = (int)(tick / 1200);                                   // 10-minute steps
        uint h = ContentRng.Hash(_seed, Id, step, 99);

        Provinces = 1 + Era * 7 + (int)(intoEra * 6 / Math.Max(1, t.EraLength(leaderEra)));
        Met = Math.Min(15, Era == 0 ? (tick > Timeline.CyclesPerHour ? 1 : 0) : Era * 2);
        Population = Math.Max(Population, 3000L * (Era + 1) * (Era + 1) * Provinces / 4);
        Unrest = (int)(h % 7) switch { 0 => 3, 1 or 2 => 2, 3 => 1, _ => 0 };
        Heirs = 1 + (int)(h >> 8) % 3;
        FoodPrice = (h >> 12) % 9 == 0 ? 2100 : 1000;
        NeighborMood = 20 + (int)((h >> 16) % 70);
        Pollution = Era >= 5 ? (Era - 4) * 20 : 0;
        Peacefulness = Id == 0 ? Math.Min(255, Era * 37) : Math.Min(200, Era * 20);
        Mood += 3 * Math.Sign(60 - Mood);                              // the real Sim drifts mood to its target yearly

        SetBuildings();
        void Flag(string f, bool on) => runner.SetFlag(m, f, on);
        Flag("tech.writing", Era >= 2 || (Era == 1 && intoEra > Timeline.CyclesPerHour / 2));
        Flag("tech.bronze", Era >= 1);
        Flag("tech.astronomy", Era >= 3 || (Era == 2 && intoEra > Timeline.CyclesPerHour));
        Flag("tech.printing", Era >= 4);
        Flag("tech.steam", Era >= 5);
        Flag("tech.telegraph", Era >= 6 || (Era == 5 && intoEra > Timeline.CyclesPerHour));
        Flag("tech.medicine", Era >= 6);
        Flag("tech.radio", Era >= 7);
        Flag("tech.computers", Era >= 8);
        Flag("tech.spaceflight", Era >= 9);
        Flag("state.wonder_building", Era >= 1 && Era <= 9 && step % 5 < 2);
        Flag("state.epidemic", Era >= 4 && Era <= 6 && step % 7 == 3);
        Flag("state.neighbor_satellite", Era >= 9);
        Flag("quirk.mammoth_island", (_landBits.Length > 0 && Array.IndexOf(_landBits, Terrain.Tundra) >= 0));
        Flag("quirk.giant_volcano", true);
        if (step % 30 == 29 && Era >= 1) Flag("ruler.died", true);   // a ruler dies every ~5 hours of play
        if (Id == 2) Flag("trait.eccentric", true);
        if (Id == 3) Flag("trait.merry", true);
        if (Id == 0 && Era >= 3) Flag("dogma.star_teaching", true);
    }

    void SetBuildings()
    {
        int e = Era;
        _buildings["farm"] = e == 0 ? 0 : 2 + e * 3;
        _buildings["pasture"] = 1 + e;
        _buildings["shrine"] = e >= 1 ? 1 + e : 0;
        _buildings["market"] = e >= 1 ? e : 0;
        _buildings["granary"] = e >= 1 ? e : 0;
        _buildings["fishery"] = Has(Terrain.Coast) ? 1 + e : 0;
        _buildings["mine"] = e >= 1 && (Has(Terrain.Mountain) || Has(Terrain.Hills)) ? e : 0;
        _buildings["library"] = e >= 2 ? e - 1 : 0;
        _buildings["factory"] = e >= 5 ? (e - 4) * 2 : 0;
        _buildings["power_plant"] = e >= 7 ? (e - 6) * 2 : 0;
    }

    bool Has(Terrain t) => Array.IndexOf(_landBits, t) >= 0;

    Terrain ProvinceTerrain(int i)
    {
        if (i == 0) return _landBits[0] | (Has(Terrain.River) ? Terrain.River : 0);   // the capital sits on the best land
        uint h = ContentRng.Hash(_seed, Id, i, 77);
        return _landBits[ContentRng.Below(h, _landBits.Length)] | _landBits[ContentRng.Below(h >> 7 | h << 25, _landBits.Length)];
    }

    // ------------------------------------------------------------------ IEventState

    public long Stat(Stat s) => s switch
    {
        Content.Stat.Provinces => Provinces,
        Content.Stat.Population => Population,
        Content.Stat.Gold => Gold,
        Content.Stat.Mood => Mood,
        Content.Stat.Stability => Stability,
        Content.Stat.Science => Science,
        Content.Stat.Culture => Culture,
        Content.Stat.MetNations => Met,
        Content.Stat.TradeRoutes => Era >= 2 ? Era * 2 : 0,
        Content.Stat.Caravans => Era >= 2 ? 4 + Era * 3 : 0,
        Content.Stat.BiggestCity => (long)(5000 * Math.Pow(1.8, Era)),
        Content.Stat.Unrest => Unrest,
        Content.Stat.Heirs => Heirs,
        Content.Stat.FoodPrice => FoodPrice,
        Content.Stat.NeighborMoodMin => Met == 0 ? 100 : NeighborMood,
        Content.Stat.Pollution => Pollution,
        Content.Stat.Peacefulness => Peacefulness,
        Content.Stat.RulerAge => 40,
        _ => 0,
    };

    public int Buildings(string id) => _buildings.TryGetValue(id, out int n) ? n : 0;
    public bool HasSystem(string id) => _systems.Contains(id);

    public int PickProvince(Terrain any, uint roll)
    {
        int count = 0;
        for (int i = 0; i < Provinces; i++) if (any == Terrain.None || (ProvinceTerrain(i) & any) != 0) count++;
        if (count == 0) return -1;
        int k = ContentRng.Below(roll, count);
        for (int i = 0; i < Provinces; i++)
            if ((any == Terrain.None || (ProvinceTerrain(i) & any) != 0) && k-- == 0) return Id * 1000 + i;
        return -1;
    }

    public int PickForeign(uint roll) => Met == 0 ? -1 : (Id + 1 + ContentRng.Below(roll, Met)) % 16;

    // ------------------------------------------------------------------ IEventSink

    public void Add(int nation, Resource what, int province, int amount)
    {
        switch (what)
        {
            case Resource.Gold: Gold += amount; break;
            case Resource.Mood: Mood = Math.Clamp(Mood + (province < 0 ? amount : amount / 3), 0, 100); break;
            case Resource.Pop: Population = Math.Max(100, Population + Population * amount / 1000 / (province < 0 ? 1 : Math.Max(1, Provinces))); break;
            case Resource.Stability: Stability = Math.Clamp(Stability + amount, 0, 100); break;
            case Resource.Science: Science += amount; break;
            case Resource.Culture: Culture += amount; break;
            case Resource.Faith: Faith += amount; break;
            case Resource.Legacy: Legacy += amount; break;
            case Resource.Food: Food += amount; break;
        }
    }

    public void Axis(int nation, string axis, int delta) => Axes++;
    public void Modifier(int nation, string id, int permille, int ticks, int province) => Modifiers++;
    public void Trait(int nation, string traitId) => Traits++;
    public void Fired(in EventRecord r) => Open = r;

    public void Resolved(in EventRecord r)
    {
        Log.Add(r);
        if (Open is { } o && o.Event == r.Event && o.Tick <= r.Tick) Open = null;
    }
}
