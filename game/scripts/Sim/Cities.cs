using System;
using System.Collections.Generic;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

public enum FoundError { None, NotLand, NotYours, IsCity, TooClose, NoGold, NoSettlers, Unexplored }

/// <summary>
/// Cities and their spheres (pure C#, integers, deterministic). Every owned land province belongs to a city — the capital
/// or a town (<see cref="GameState.City"/>). A city grows its borders by itself: each rules cycle it gathers influence from
/// its people, mood and buildings, and when enough is gathered it takes the best free land next to its provinces, but only
/// inside its sphere (<see cref="Radius"/> land steps from the city) and up to its cap (<see cref="Cap"/> provinces).
/// When every sphere is full, the only way to grow further is to found a new city (<see cref="Found"/>).
/// </summary>
public static class Cities
{
    public const int FoundCost = 150;            // gold
    public const int MinCityDistance = 3;        // land steps between cities
    public const int SettlersMin = 1500, SettlersPermille = 200, SettlersKeep = 2000;
    /// <summary>Influence a city needs for one province at «Обычная» pace (scaled by GameSetup.PacePermille).</summary>
    public const int GrowthNeed = 5000;

    /// <summary>Sphere radius in land steps: 3 in the ancient eras, +1 from Средневековье (3), +1 from Индустриальная (6).</summary>
    public static int Radius(int era) => 3 + (era >= 3 ? 1 : 0) + (era >= 6 ? 1 : 0);

    /// <summary>Most provinces one city can hold: 6, the capital +3, +1 for every two eras.</summary>
    public static int Cap(GameState s, int city) => 6 + (s.CapitalOf[city] >= 0 ? 3 : 0) + s.Nat[s.Owner[city]].Era / 2;

    public static bool IsCity(GameState s, int p) => p >= 0 && (s.CapitalOf[p] >= 0 || s.IsTown[p]);

    public static int Need(GameState s) => (int)Math.Max(500, (long)GrowthNeed * Eras.ClampPace(s.Pace) / 1000);

    // ------------------------------------------------------------------ assignment

    /// <summary>Give every owned province to the nearest city of its owner (at the start, after founding a city).</summary>
    public static void Init(WorldData w, GameState s)
    {
        s.City ??= new int[w.P];
        s.Growth ??= new int[w.P];
        s.SphereNoted ??= new bool[w.P];
        for (int n = 0; n < s.Nat.Length; n++) Reassign(w, s, n);
    }

    /// <summary>Nearest-city assignment for nation n: multi-source BFS from its cities over its own land (ties: lower city id).</summary>
    public static void Reassign(WorldData w, GameState s, int n)
    {
        var sc = SimScratch.For(w, s);
        var dist = sc.Dist; var q = sc.Queue;
        int head = 0, tail = 0;
        for (int p = 0; p < w.P; p++)
        {
            if (s.Owner[p] != n) continue;
            dist[p] = -1;
            s.City[p] = -1;
        }
        for (int p = 0; p < w.P; p++)
            if (s.Owner[p] == n && IsCity(s, p)) { s.City[p] = p; dist[p] = 0; q[tail++] = p; }
        while (head < tail)
        {
            int p = q[head++];
            foreach (int r in w.Adj[p])
            {
                if (s.Owner[r] != n || w.PLand[r] != 1) continue;
                if (dist[r] < 0 || (dist[r] == dist[p] + 1 && s.City[p] < s.City[r]))
                {
                    if (dist[r] < 0) q[tail++] = r;
                    dist[r] = dist[p] + 1; s.City[r] = s.City[p];
                }
            }
        }
        // cut off from every city (islands): the nearest city by distance
        for (int p = 0; p < w.P; p++)
        {
            if (s.Owner[p] != n || s.City[p] >= 0) continue;
            int best = -1, bd = int.MaxValue;
            for (int c = 0; c < w.P; c++)
                if (s.Owner[c] == n && IsCity(s, c)) { int d = Simulation.Distance(w, p, c); if (d < bd) { bd = d; best = c; } }
            s.City[p] = best;
        }
    }

    /// <summary>How many provinces each city holds (indexed by city province).</summary>
    public static int[] Counts(WorldData w, GameState s)
    {
        var cnt = SimScratch.For(w, s).CityCount;
        Array.Clear(cnt);
        for (int p = 0; p < w.P; p++) if (s.Owner[p] >= 0 && s.City[p] >= 0) cnt[s.City[p]]++;
        return cnt;
    }

    // ------------------------------------------------------------------ the sphere

    /// <summary>Land steps from city c to every province up to its radius (in scratch Dist; -1 = outside).</summary>
    static int[] Sphere(WorldData w, GameState s, int c)
    {
        var sc = SimScratch.For(w, s);
        var dist = sc.Dist2; var q = sc.Queue;
        int R = Radius(s.Nat[s.Owner[c]].Era);
        for (int k = 0; k < sc.TouchedCount; k++) dist[sc.Touched[k]] = -1;
        sc.TouchedCount = 0;
        int head = 0, tail = 0;
        dist[c] = 0; q[tail++] = c; sc.Touched[sc.TouchedCount++] = c;
        while (head < tail)
        {
            int p = q[head++];
            if (dist[p] >= R) continue;
            foreach (int r in w.Adj[p])
            {
                if (w.PLand[r] != 1 || dist[r] >= 0) continue;
                dist[r] = dist[p] + 1; q[tail++] = r; sc.Touched[sc.TouchedCount++] = r;
            }
        }
        return dist;
    }

    /// <summary>The city of nation n that could absorb free land province q (in its sphere, next to its provinces, under
    /// its cap), or -1. Used by the claim rule so gold can speed growth up but never bypass the limit.</summary>
    public static int Absorber(WorldData w, GameState s, int q, int n)
    {
        if (s.City == null) return -1;
        var cnt = Counts(w, s);
        int best = -1, bestD = int.MaxValue;
        foreach (int p in w.Adj[q])
        {
            if (s.Owner[p] != n) continue;
            int c = s.City[p];
            if (c < 0 || c == best || cnt[c] >= Cap(s, c)) continue;
            int d = Sphere(w, s, c)[q];
            if (d < 0) continue;
            if (d < bestD || (d == bestD && c < best)) { bestD = d; best = c; }
        }
        return best;
    }

    /// <summary>The free land city c wants next, or -1 when its sphere is exhausted.</summary>
    public static int Target(WorldData w, GameState s, int c)
    {
        int n = s.Owner[c];
        var dist = Sphere(w, s, c);
        var fert = WorldFacts.Of(w).FertPm;
        var sc = SimScratch.For(w, s);
        int best = -1; long bs = long.MinValue;
        for (int k = 0; k < sc.TouchedCount; k++)
        {
            int q = sc.Touched[k];
            if (s.Owner[q] >= 0 || w.PLand[q] != 1) continue;
            bool touches = false;
            foreach (int r in w.Adj[q]) if (s.Owner[r] == n && s.City[r] == c) { touches = true; break; }
            if (!touches) continue;
            long score = fert[q] * 6L + (w.PRiver[q] != 0 ? 1500 : 0) + (w.PCoast[q] != 0 ? 800 : 0)
                       + (Radius(s.Nat[n].Era) - dist[q]) * 1200L + SimRng.Permille(w.Seed, 31, q, c);
            if (score > bs || (score == bs && q < best)) { bs = score; best = q; }
        }
        return best;
    }

    // ------------------------------------------------------------------ growth (every rules cycle)

    /// <summary>Influence a city gathers per rules cycle: 10, +1 per 2000 people (≤ 30), mood/10, shrine +8, market +5.</summary>
    public static int Influence(GameState s, int c)
    {
        int v = 10 + Math.Min(30, s.Pop[c] / 2000) + s.Mood[c] / 10;
        foreach (var b in s.Buildings[c]) v += b switch { Bld.Shrine => 8, Bld.Market => 5, _ => 0 };
        return v;
    }

    internal static void Grow(WorldData w, GameState s, ISimSink sink, ref List<int> changed)
    {
        if (s.City == null) return;
        int need = Need(s);
        var cnt = Counts(w, s);
        for (int c = 0; c < w.P; c++)
        {
            if (!IsCity(s, c) || s.Owner[c] < 0) continue;
            int n = s.Owner[c];
            if (cnt[c] >= Cap(s, c)) { Stalled(w, s, c, n, sink, full: true); continue; }
            s.Growth[c] = Math.Min(need, s.Growth[c] + Influence(s, c));
            if (s.Growth[c] < need) continue;
            int q = Target(w, s, c);
            if (q < 0) { Stalled(w, s, c, n, sink, full: false); continue; }
            s.Growth[c] = 0;
            s.SphereNoted[c] = false;
            Join(s, q, n, c);
            cnt[c]++;
            (changed ??= new List<int>()).Add(q);
            if (s.IsHuman(n))
                sink?.Notify("flag", s.CapitalOf[c] >= 0
                    ? $"Столица {w.PName[c]} присоединила земли {w.PName[q]}"
                    : $"Город {w.PName[c]} присоединил земли {w.PName[q]}");
        }
    }

    static void Stalled(WorldData w, GameState s, int c, int n, ISimSink sink, bool full)
    {
        if (s.SphereNoted[c]) return;
        s.SphereNoted[c] = true;
        if (s.IsHuman(n))
            sink?.Notify("map-pin", full
                ? $"{w.PName[c]} достиг предела: {Cap(s, c)} провинций. Чтобы расти дальше, основайте новый город"
                : $"Земли вокруг {w.PName[c]} освоены — основайте новый город");
    }

    /// <summary>Tribes of free land q join nation n under city c (no gold: culture, not purchase).</summary>
    static void Join(GameState s, int q, int n, int c)
    {
        s.Owner[q] = s.Controller[q] = (short)n;
        s.Religion[q] = (sbyte)s.Nations[n].Religion;
        s.Pop[q] = (int)Math.Min(int.MaxValue, (long)s.Pop[q] * Rules.ClaimPopBoostPermille / 1000);
        s.Mood[q] = Math.Min(s.Mood[q], (byte)58);
        s.City[q] = c;
    }

    /// <summary>Estimated rules cycles until city c takes its next province (-1: stalled).</summary>
    public static int CyclesToNext(WorldData w, GameState s, int c)
    {
        if (s.City == null || !IsCity(s, c)) return -1;
        if (Counts(w, s)[c] >= Cap(s, c) || Target(w, s, c) < 0) return -1;
        int left = Need(s) - s.Growth[c];
        return left <= 0 ? 0 : (left + Influence(s, c) - 1) / Influence(s, c);
    }

    // ------------------------------------------------------------------ founding a city

    /// <summary>The own city settlers would come from for a new city at p: the nearest one that can spare people.</summary>
    public static int SettlerSource(WorldData w, GameState s, int p, int n)
    {
        int best = -1, bd = int.MaxValue;
        for (int c = 0; c < w.P; c++)
        {
            if (s.Owner[c] != n || !IsCity(s, c) || s.Pop[c] < SettlersKeep + SettlersMin) continue;
            int d = Simulation.Distance(w, p, c);
            if (d < bd || (d == bd && c < best)) { bd = d; best = c; }
        }
        return best;
    }

    public static int Settlers(GameState s, int source) =>
        Math.Min(s.Pop[source] - SettlersKeep, Math.Max(SettlersMin, (int)((long)s.Pop[source] * SettlersPermille / 1000)));

    /// <summary>Is any city (of any nation) closer than <see cref="MinCityDistance"/> land steps to p?</summary>
    static bool CityNear(WorldData w, GameState s, int p)
    {
        var sc = SimScratch.For(w, s);
        var dist = sc.Dist2; var q = sc.Queue;
        for (int k = 0; k < sc.TouchedCount; k++) dist[sc.Touched[k]] = -1;
        sc.TouchedCount = 0;
        int head = 0, tail = 0;
        dist[p] = 0; q[tail++] = p; sc.Touched[sc.TouchedCount++] = p;
        while (head < tail)
        {
            int x = q[head++];
            if (IsCity(s, x)) return true;
            if (dist[x] >= MinCityDistance - 1) continue;
            foreach (int r in w.Adj[x])
            {
                if (w.PLand[r] != 1 || dist[r] >= 0) continue;
                dist[r] = dist[x] + 1; q[tail++] = r; sc.Touched[sc.TouchedCount++] = r;
            }
        }
        return false;
    }

    public static FoundError Check(WorldData w, GameState s, int p, int n)
    {
        if (p < 0 || p >= w.P || w.PLand[p] != 1) return FoundError.NotLand;
        if (IsCity(s, p)) return FoundError.IsCity;
        int o = s.Owner[p];
        if (o >= 0 && o != n) return FoundError.NotYours;
        if (o < 0)
        {
            if (s.Nat[n].Fog is { } f && !f.Explored[p]) return FoundError.Unexplored;
            if (!Rules.Borders(w, s, p, n)) return FoundError.NotYours;
        }
        if (CityNear(w, s, p)) return FoundError.TooClose;
        if (s.Nat[n].Treasury < FoundCost * Rules.Cents) return FoundError.NoGold;
        if (SettlerSource(w, s, p, n) < 0) return FoundError.NoSettlers;
        return FoundError.None;
    }

    /// <summary>Settlers from the nearest city found a town at p. Caller validated with Check.</summary>
    public static void Found(WorldData w, GameState s, int p, int n)
    {
        int src = SettlerSource(w, s, p, n);
        int people = Settlers(s, src);
        s.Nat[n].Treasury -= FoundCost * Rules.Cents;
        s.Pop[src] -= people;
        if (s.Owner[p] < 0)
        {
            s.Owner[p] = s.Controller[p] = (short)n;
            s.Religion[p] = (sbyte)s.Nations[n].Religion;
            s.Mood[p] = Math.Min(s.Mood[p], (byte)60);
        }
        s.Pop[p] = (int)Math.Min(int.MaxValue, (long)s.Pop[p] + people);
        s.IsTown[p] = true;
        s.Growth[p] = 0;
        s.SphereNoted[p] = false;
        s.City[p] = p;
        TakeNearby(w, s, p, n);   // the nearest land moves to the new town, freeing room in the old cities
        foreach (int c in AllCities(w, s, n)) s.SphereNoted[c] = false;
    }

    /// <summary>Own provinces within 2 land steps of new town t move to it (nearest first, up to its cap). Old cities only
    /// lose provinces, so no city is ever pushed past its cap.</summary>
    static void TakeNearby(WorldData w, GameState s, int t, int n)
    {
        var sc = SimScratch.For(w, s);
        var dist = sc.Dist2; var q = sc.Queue;
        for (int k = 0; k < sc.TouchedCount; k++) dist[sc.Touched[k]] = -1;
        sc.TouchedCount = 0;
        int head = 0, tail = 0, taken = 1, cap = Cap(s, t);
        dist[t] = 0; q[tail++] = t; sc.Touched[sc.TouchedCount++] = t;
        while (head < tail && taken < cap)
        {
            int p = q[head++];
            if (dist[p] >= 2) continue;
            foreach (int r in w.Adj[p])
            {
                if (dist[r] >= 0 || s.Owner[r] != n || w.PLand[r] != 1) continue;
                dist[r] = dist[p] + 1; q[tail++] = r; sc.Touched[sc.TouchedCount++] = r;
                if (IsCity(s, r) || taken >= cap) continue;
                s.City[r] = t; taken++;
            }
        }
    }

    static IEnumerable<int> AllCities(WorldData w, GameState s, int n)
    {
        for (int c = 0; c < w.P; c++) if (s.Owner[c] == n && IsCity(s, c)) yield return c;
    }

    /// <summary>Bots: where a bot would found its next city — own land without a city, far enough from cities,
    /// fertile and far from the capital's crowd; -1 if nowhere.</summary>
    internal static int BotFoundSite(WorldData w, GameState s, int n)
    {
        var fert = WorldFacts.Of(w).FertPm;
        int best = -1; long bs = long.MinValue;
        for (int p = 0; p < w.P; p++)
        {
            if (w.PLand[p] != 1 || IsCity(s, p)) continue;
            int o = s.Owner[p];
            if (o != n && !(o < 0 && Rules.Borders(w, s, p, n))) continue;
            long score = fert[p] * 4L + (w.PRiver[p] != 0 ? 800 : 0) + (w.PCoast[p] != 0 ? 400 : 0) + SimRng.Permille(w.Seed, 32, p, n);
            if (score <= bs) continue;
            if (CityNear(w, s, p)) continue;
            bs = score; best = p;
        }
        return best;
    }
}
