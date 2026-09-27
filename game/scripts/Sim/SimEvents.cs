using System;
using System.Collections.Generic;
using PaxPixelia.Content;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

/// <summary>
/// The deck of fates (Content/EventRunner, game/data/events) bound to one game: an EventMemory per nation, dealt on
/// every rules cycle. Content durations are rules cycles (0.5 s at speed 3), so the runner's tick is the cycle number.
/// Effects change the integer state; human nations get the outcome in their chronicle and their open choices through
/// <see cref="ISimSink.EventChoiceChanged"/>. Stats and systems the Sim does not have yet read as neutral values,
/// which keeps the events that need them out of the deck.
/// </summary>
public sealed class SimEvents
{
    /// <summary>Systems of this build that «requires» may name (core/deck.json «systems»).</summary>
    static readonly string[] Systems = { "religion", "trade_routes" };
    /// <summary>core/deck.json «buildings» ids of the buildings the Sim has, in Data.Bld order.</summary>
    static readonly string[] BuildingIds = { "farm", "lumber", "quarry", "fishery", "pasture", "shrine", "market", "granary" };

    public readonly EventRunner Runner;
    public readonly EventMemory[] Mem;
    readonly View[] _views;
    readonly Effects _fx;

    public ContentDb Db => Runner.Db;

    public SimEvents(ContentDb db, WorldData w, GameState s, int jokePercent = 100)
    {
        Runner = new EventRunner(db, w.Seed) { JokePercent = jokePercent };
        Mem = new EventMemory[s.Nat.Length];
        _views = new View[s.Nat.Length];
        for (int n = 0; n < s.Nat.Length; n++)
        {
            long cycle = Clock.CycleOf(s.Tick);
            Mem[n] = Runner.NewMemory(n, bot: !s.Nat[n].Human, tick: cycle);
            _views[n] = new View(w, s, n);
        }
        _fx = new Effects(w, s, this);
    }

    /// <summary>One rules cycle: every nation's metronome, follow-ups, triggers and choice deadlines.</summary>
    public void Cycle(long cycle, ISimSink sink)
    {
        _fx.Sink = sink;
        for (int n = 0; n < Mem.Length; n++) Runner.Tick(Mem[n], _views[n], _fx, cycle);
    }

    /// <summary>Command Choose: nation n answers its open choice with option k. False when not offered.</summary>
    public bool Choose(GameState s, int n, int k, ISimSink sink)
    {
        if ((uint)n >= (uint)Mem.Length) return false;
        _fx.Sink = sink;
        return Runner.Choose(Mem[n], _views[n], _fx, Clock.CycleOf(s.Tick), k);
    }

    /// <summary>Fire event e for nation n now (world systems, the debug console, tests); false when it cannot.</summary>
    public bool Force(GameState s, int n, int e, ISimSink sink)
    {
        _fx.Sink = sink;
        return Runner.Force(Mem[n], _views[n], _fx, Clock.CycleOf(s.Tick), e);
    }

    public EventRecord? Pending(int n) => (uint)n < (uint)Mem.Length ? Mem[n].Pending : null;
    public bool IsOffered(int n, int e, int k) => Runner.IsOffered(Mem[n], _views[n], e, k);
    public bool SetFlag(int n, string id, bool on = true) => Runner.SetFlag(Mem[n], id, on);

    /// <summary>A template of record r with its placeholders filled from the world and the roster.</summary>
    public string Format(WorldData w, GameState s, string template, in EventRecord r)
    {
        var rec = r;
        return EventText.Format(template, (name, form) => Resolve(w, s, rec, name, form));
    }

    static string Resolve(WorldData w, GameState s, in EventRecord r, string name, string form)
    {
        switch (name)
        {
            case "province": return r.Province >= 0 ? w.PName[r.Province] : "окраина";
            case "capital": { int c = s.NationCapital[r.Nation]; return c >= 0 ? w.PName[c] : "стоянка"; }
            case "foreign": return r.Foreign >= 0 ? NationForm(s.Nations[r.Foreign], form) : "соседи";
            case "nation": return NationForm(s.Nations[r.Nation], form);
            case "ruler": return form == "gen" ? "правителя" : "правитель";
            case "year": return Calendar.YearText(r.Year);
            default: return name;
        }
    }

    static string NationForm(Core.Data.Nation n, string form) => form switch
    {
        "gen" => Ru.Genitive(n.Name),
        "adj" => n.CultureAdj,
        _ => n.Name,
    };

    /// <summary>What one nation looks like to the deck.</summary>
    sealed class View : IEventState
    {
        readonly WorldData _w;
        readonly GameState _s;
        readonly int _n;
        readonly List<int> _pick = new();

        public View(WorldData w, GameState s, int n) { _w = w; _s = s; _n = n; }

        public int Era => _s.Nat[_n].Era;
        public int Year => Calendar.DateOf(_s.Day256).Year;
        public int Capital => _s.NationCapital[_n];
        public bool HasSystem(string id) => Array.IndexOf(Systems, id) >= 0;

        public long Stat(Stat stat)
        {
            switch (stat)
            {
                case Content.Stat.Provinces: return Count((_, _) => 1);
                case Content.Stat.Population: return Count((s, p) => s.Pop[p]);
                case Content.Stat.Gold: return _s.Nat[_n].Treasury / Rules.Cents;
                case Content.Stat.Mood:
                {
                    long provinces = Count((_, _) => 1);
                    return provinces == 0 ? 50 : Count((s, p) => s.Mood[p]) / provinces;
                }
                case Content.Stat.Science: return _s.Nat[_n].Progress - Eras.Threshold(_s.Nat[_n].Era, _s.Pace);
                case Content.Stat.MetNations: return Met(null);
                case Content.Stat.TradeRoutes: return TradeRoutes();
                case Content.Stat.BiggestCity:
                {
                    long best = 0;
                    for (int p = 0; p < _w.P; p++) if (_s.Owner[p] == _n && _s.Pop[p] > best) best = _s.Pop[p];
                    return best;
                }
                case Content.Stat.Stability: return 50;
                case Content.Stat.FoodPrice: return 1000;
                case Content.Stat.NeighborMoodMin: return 100;
                case Content.Stat.Heirs: return 1;
                case Content.Stat.Peacefulness: return 255;
                case Content.Stat.RulerAge: return 40;
                default: return 0;   // Culture, Caravans, Unrest, Pollution: no such system yet
            }
        }

        long Count(Func<GameState, int, long> f)
        {
            long sum = 0;
            for (int p = 0; p < _w.P; p++) if (_s.Owner[p] == _n) sum += f(_s, p);
            return sum;
        }

        long TradeRoutes()
        {
            int k = 0;
            foreach (var r in _s.Routes)
                if (r.Length > 0 && (_s.Owner[r[0]] == _n || _s.Owner[r[^1]] == _n)) k++;
            return k;
        }

        public int Buildings(string id)
        {
            int b = Array.IndexOf(BuildingIds, id);
            if (b < 0) return 0;
            int k = 0;
            for (int p = 0; p < _w.P; p++)
                if (_s.Owner[p] == _n)
                    foreach (var x in _s.Buildings[p]) if ((int)x == b) k++;
            return k;
        }

        public int PickProvince(Terrain any, uint roll)
        {
            _pick.Clear();
            for (int p = 0; p < _w.P; p++)
                if (_s.Owner[p] == _n && (any == Terrain.None || (TerrainOf(p) & any) != 0)) _pick.Add(p);
            return _pick.Count == 0 ? -1 : _pick[(int)(roll % (uint)_pick.Count)];
        }

        public int PickForeign(uint roll)
        {
            int count = Met(_pick);
            return count == 0 ? -1 : _pick[(int)(roll % (uint)count)];
        }

        /// <summary>Foreign nations this one knows: a human's fog says whom it met; bots know every nation with land.</summary>
        int Met(List<int> into)
        {
            into?.Clear();
            var met = _s.Nat[_n].Fog?.Met;
            int k = 0;
            for (int m = 0; m < _s.Nat.Length; m++)
            {
                if (m == _n || (met != null ? !met[m] : _s.NationCapital[m] < 0)) continue;
                into?.Add(m);
                k++;
            }
            return k;
        }

        Terrain TerrainOf(int p)
        {
            var t = Terrain.None;
            if (_w.PCoast[p] != 0) t |= Terrain.Coast;
            if (_w.PRiver[p] != 0) t |= Terrain.River;
            t |= _w.PBiome[p] switch
            {
                2 or 3 => Terrain.Mountain,
                6 or 11 => Terrain.Steppe,
                13 => Terrain.Savanna,
                9 or 10 => Terrain.Plains,
                14 => Terrain.Desert,
                5 or 8 => Terrain.Forest,
                12 => Terrain.Jungle,
                1 or 4 => Terrain.Tundra,
                7 => Terrain.Swamp,
                _ => Terrain.None,
            };
            return t;
        }
    }

    /// <summary>Applies effects to the integer state and reports human nations' events to the sink.</summary>
    sealed class Effects : IEventSink
    {
        readonly WorldData _w;
        readonly GameState _s;
        readonly SimEvents _owner;
        public ISimSink Sink;

        public Effects(WorldData w, GameState s, SimEvents owner) { _w = w; _s = s; _owner = owner; }

        public void Add(int n, Resource what, int province, int amount)
        {
            var nat = _s.Nat[n];
            switch (what)
            {
                case Resource.Gold:
                    nat.Treasury = Math.Max(0, nat.Treasury + (long)amount * Rules.Cents);
                    break;
                case Resource.Science:
                    nat.Progress = Math.Max(Eras.Threshold(nat.Era, _s.Pace), nat.Progress + amount);   // never back into an older era
                    break;
                case Resource.Mood:
                    ForProvinces(n, province, p => _s.Mood[p] = (byte)IntMath.Clamp(_s.Mood[p] + amount, 0, 100));
                    break;
                case Resource.Pop:
                    ForProvinces(n, province, p => _s.Pop[p] = (int)Math.Clamp(_s.Pop[p] + (long)_s.Pop[p] * amount / 1000, 10, int.MaxValue));
                    break;
            }
            // Stability, Culture, Faith, Legacy, Food: no such system yet
        }

        void ForProvinces(int n, int province, Action<int> f)
        {
            if (province >= 0) { if (_s.Owner[province] == n) f(province); return; }
            for (int p = 0; p < _w.P; p++) if (_s.Owner[p] == n) f(p);
        }

        public void Axis(int nation, string axis, int delta) { }
        public void Modifier(int nation, string id, int permille, int ticks, int province) { }
        public void Trait(int nation, string traitId) { }

        public void Fired(in EventRecord r)
        {
            if (_s.Nat[r.Nation].Human) Sink?.EventChoiceChanged(r.Nation);
        }

        public void Resolved(in EventRecord r)
        {
            var nat = _s.Nat[r.Nation];
            nat.EventCount++;
            if (!nat.Human || Sink == null) return;
            var def = EventText.Def(_owner.Db, r);
            Sink.Notify(def.Icon ?? "feather", _owner.Format(_w, _s, EventText.Result(_owner.Db, r), r));
            if (r.Option >= 0) Sink.EventChoiceChanged(r.Nation);
        }
    }
}
