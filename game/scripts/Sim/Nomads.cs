using System;
using System.Collections.Generic;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

public enum SettleError { None, Settled, NotLand, Owned, TooClose }
public enum TribeMoveError { None, Settled, Sea, Here, Far, Unexplored, Elders }

/// <summary>A legend the tribe picks up on its way; at the founding one becomes the nation's myth (a lasting bonus).</summary>
public sealed record LegendDef(string Name, string Where, string Myth, string Line);

/// <summary>
/// The nomad phase «Тропа племени» (IDEAS N-1, pure C#, integers). A game started with GameSetup.Nomad begins with
/// every nation as a tribe on the map (NationState.Camp) and no land: the tribe walks (one province in
/// <see cref="StepTicks"/> ticks), lives off its supplies, collects up to <see cref="MaxLegends"/> legends from the
/// land it crosses and founds its capital where the player chooses; one legend becomes the myth. The elders urge
/// settling after <see cref="UrgeTicks"/> and settle by themselves after <see cref="AutoTicks"/>; bots settle on
/// their spot within 1–2 minutes. Founding takes the camp and the land around it and turns the leftover supplies into
/// materials.
/// </summary>
public static class Nomads
{
    /// <summary>Ticks per province walked (3 s at speed 3), per supply check (2 s), the elders' urge (3 min), the
    /// forced founding (5 min).</summary>
    public const int StepTicks = 24, SupplyTicks = 16, UrgeTicks = 3 * 60 * 8, AutoTicks = 5 * 60 * 8;
    public const int StartSupplies = 100, MaxLegends = 3, InitialSight = 4, CampSight = 2, NearRadius = 3;
    /// <summary>With no free site near the camp, the elders and the bots look this many land steps away.</summary>
    public const int FarRadius = 12;
    /// <summary>Bots settle between these ticks (1 and 2 minutes at speed 3).</summary>
    public const int BotSettleMin = 60 * 8, BotSettleMax = 120 * 8;

    public const int River = 0, Coast = 1, Hills = 2, Steppe = 3, Forest = 4, Friends = 5;

    public static readonly LegendDef[] Legends =
    {
        new("Дети реки", "река", "Провинции на реках: предел населения +15%", "Мы пили из реки, и река нас не выдала."),
        new("Народ волн", "берег моря", "Провинции у моря: предел населения +10%", "Море шумело, а мы слушали и учились."),
        new("Горцы", "холмы и горы", "Каменоломни: +1 материал за цикл", "На камне спать жёстко, зато из камня строить."),
        new("Конное племя", "степь", "Разведчики видят на 1 провинцию дальше", "В степи далеко видно. Мы привыкли смотреть далеко."),
        new("Лесные духи", "лес, тайга или джунгли", "Лесопилки: +1 материал за цикл", "Лес кормил нас, пока мы просили вежливо."),
        new("Друзья у костра", "встреча с чужим родом", "Довольство везде +3", "У чужого костра нас накормили. Мы запомнили."),
    };

    public static bool IsNomad(NationState nat) => nat.Camp >= 0;
    public static bool HasLegend(NationState nat, int l) => (nat.Legends & (1 << l)) != 0;
    public static int LegendCount(NationState nat) => System.Numerics.BitOperations.PopCount((uint)nat.Legends);
    public static bool HasMyth(GameState s, int n, int l) => n >= 0 && s.Nat[n].Myth == l;

    // ------------------------------------------------------------------ start

    /// <summary>Turn a freshly generated state into a nomad start: every nation becomes a tribe on its capital site,
    /// the generated borders, buildings, towns and routes are dropped (the land is free tribes' land again).</summary>
    public static void Start(WorldData w, GameState s)
    {
        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            int camp = s.NationCapital[n];
            nat.Camp = camp;
            nat.Supplies = StartSupplies;
            nat.TribePop = Math.Max(1500, s.Pop[camp] / 2);
            s.Pop[camp] = Math.Max(10, s.Pop[camp] - nat.TribePop);
            nat.Legends = 0; nat.Myth = -1;
            s.NationCapital[n] = -1;
        }
        for (int p = 0; p < w.P; p++)
        {
            if (s.Owner[p] < 0) continue;
            s.Owner[p] = s.Controller[p] = -1;
            s.CapitalOf[p] = -1;
            s.IsTown[p] = false;
            s.Buildings[p].Clear();
            s.OreFound[p] = false;
        }
        s.Routes.Clear();
        for (int n = 0; n < s.Nat.Length; n++) Collect(w, s, n, null);   // the land the tribe starts on
    }

    // ------------------------------------------------------------------ every tick

    /// <summary>Walk, eat, remember. Returns the provinces whose ownership changed (bots or the elders founding).</summary>
    internal static void Tick(WorldData w, GameState s, ISimSink sink, ref List<int> changed, ref bool moved)
    {
        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            if (nat.Camp < 0) continue;

            if (nat.CampPath != null && (++nat.CampSub) >= StepTicks)
            {
                nat.CampSub = 0;
                nat.CampStep++;
                nat.Camp = nat.CampPath[nat.CampStep];
                moved = true;
                if (nat.CampStep >= nat.CampPath.Length - 1) { nat.CampPath = null; nat.CampStep = 0; }
                Collect(w, s, n, sink);
                if (nat.Fog != null) FogOfWar.RefreshNation(w, s, n, sink);
            }

            if (s.Tick % SupplyTicks == 0) Eat(w, s, nat);

            if (nat.Control == NationControl.Bot)
            {
                if (s.Tick >= BotSettleTick(w.Seed, n) && CheckSettle(w, s, n, nat.Camp) == SettleError.None)
                    Found(w, s, n, BestMyth(nat), sink, ref changed);
                else if (s.Tick >= BotSettleMax && nat.CampPath == null)
                {
                    // the spot got taken (a neighbour settled next door): the best free site nearby, or walk there
                    int site = FallbackSite(w, s, n);
                    if (site == nat.Camp) Found(w, s, n, BestMyth(nat), sink, ref changed);
                    else if (site >= 0) MoveTo(w, s, n, site);
                }
                continue;
            }

            if (s.Tick == UrgeTicks && nat.Human)
                sink?.Notify("alert-triangle", "Старейшины требуют осесть: род устал идти. Через две минуты они выберут место сами");
            if (s.Tick >= AutoTicks && nat.CampPath == null)
            {
                int site = CheckSettle(w, s, n, nat.Camp) == SettleError.None ? nat.Camp : FallbackSite(w, s, n);
                if (site == nat.Camp)
                {
                    if (nat.Human) sink?.Notify("alert-triangle", "Старейшины не стали ждать и развели очаг на месте стоянки");
                    Found(w, s, n, BestMyth(nat), sink, ref changed);
                }
                else if (site >= 0) MoveTo(w, s, n, site, elders: true);   // walk to the nearest good site and settle there
            }
        }
    }

    public static int BotSettleTick(int seed, int n) => BotSettleMin + SimRng.Permille(seed, 51, n, 0) * (BotSettleMax - BotSettleMin) / 1000;

    /// <summary>Every <see cref="SupplyTicks"/>: rich land (fertile or on a river) feeds the tribe, barren land starves it;
    /// with nothing left people leave (2% a check).</summary>
    static void Eat(WorldData w, GameState s, NationState nat)
    {
        int p = nat.Camp, fert = WorldFacts.Of(w).FertPm[p];
        int d = fert >= 600 || w.PRiver[p] != 0 ? 1 : fert <= 150 ? -1 : 0;
        if (nat.CampPath != null && d == 0) d = -1;          // walking through middling land costs food
        nat.Supplies = IntMath.Clamp(nat.Supplies + d, 0, StartSupplies);
        if (nat.Supplies == 0) nat.TribePop = Math.Max(300, nat.TribePop - nat.TribePop / 50);
    }

    /// <summary>Legends of the province the tribe stands on (up to <see cref="MaxLegends"/>).</summary>
    static void Collect(WorldData w, GameState s, int n, ISimSink sink)
    {
        var nat = s.Nat[n];
        int p = nat.Camp;
        int b = w.PBiome[p];
        Try(River, w.PRiver[p] != 0);
        Try(Coast, w.PCoast[p] != 0);
        Try(Hills, WorldFacts.Of(w).Hills[p] || b is 2 or 3);
        Try(Steppe, b is 6 or 11);
        Try(Forest, b is 5 or 8 or 12);
        bool friends = false;
        for (int m = 0; m < s.Nat.Length && !friends; m++)
        {
            if (m == n) continue;
            int c = s.Nat[m].Camp;
            if (c == p || (c >= 0 && Array.IndexOf(w.Adj[p], c) >= 0)) friends = true;
            else foreach (int q in w.Adj[p]) if (s.Owner[q] == m) { friends = true; break; }
        }
        Try(Friends, friends);

        void Try(int l, bool ok)
        {
            if (!ok || HasLegend(nat, l) || LegendCount(nat) >= MaxLegends) return;
            nat.Legends |= 1 << l;
            if (nat.Human) sink?.Notify("book", $"Новое предание: «{Legends[l].Name}». {Legends[l].Line}");
        }
    }

    // ------------------------------------------------------------------ moving

    /// <param name="elders">The elders lead the walk (after <see cref="AutoTicks"/> the player no longer can).</param>
    public static TribeMoveError CheckMove(WorldData w, GameState s, int n, int target, out int[] path, bool elders = false)
    {
        path = null;
        var nat = s.Nat[n];
        if (nat.Camp < 0) return TribeMoveError.Settled;
        if (nat.Human && !elders && s.Tick >= AutoTicks) return TribeMoveError.Elders;   // no walking away from the founding
        if (target < 0 || target >= w.P || w.PLand[target] != 1) return TribeMoveError.Sea;
        if (nat.Fog is { } f && !f.Explored[target]) return TribeMoveError.Unexplored;
        if (target == nat.Camp) return TribeMoveError.Here;
        var bfs = SimScratch.For(w, s).A;
        bfs.RunLand(w, stackalloc int[] { nat.Camp });
        path = bfs.Trace(target);
        return path == null ? TribeMoveError.Far : TribeMoveError.None;
    }

    public static TribeMoveError MoveTo(WorldData w, GameState s, int n, int target, bool elders = false)
    {
        var e = CheckMove(w, s, n, target, out var path, elders);
        if (e != TribeMoveError.None) return e;
        var nat = s.Nat[n];
        nat.CampPath = path; nat.CampStep = 0; nat.CampSub = 0;
        return TribeMoveError.None;
    }

    /// <summary>Stop where the tribe stands now (the step under way is dropped).</summary>
    public static void Halt(NationState nat) { nat.CampPath = null; nat.CampStep = 0; nat.CampSub = 0; }

    // ------------------------------------------------------------------ the site

    /// <summary>How good p is for a capital, 0..100: fertility 40, river 15, sea 10, varied land around 20, hills 10,
    /// nobody near 5 (IDEAS N-1).</summary>
    public static int SiteScore(WorldData w, GameState s, int n, int p) => Site(w, s, n, p).Total;

    public readonly record struct SiteParts(int Fertility, int River, int Coast, int Variety, int Stone, int Room)
    {
        public int Total => Fertility + River + Coast + Variety + Stone + Room;
    }

    public static SiteParts Site(WorldData w, GameState s, int n, int p)
    {
        if (p < 0 || p >= w.P || w.PLand[p] != 1) return default;
        var facts = WorldFacts.Of(w);
        int fert = facts.FertPm[p] * 40 / 1000;
        int biomes = 1 << w.PBiome[p], hills = facts.Hills[p] ? 1 : 0;
        foreach (int q in w.Adj[p])
        {
            if (w.PLand[q] != 1) continue;
            biomes |= 1 << w.PBiome[q];
            if (facts.Hills[q]) hills = 1;
        }
        int variety = Math.Min(20, (System.Numerics.BitOperations.PopCount((uint)biomes) - 1) * 7);
        bool room = true;
        for (int m = 0; m < s.Nat.Length && room; m++)
        {
            if (m == n) continue;
            int c = s.NationCapital[m] >= 0 ? s.NationCapital[m] : s.Nat[m].Camp;
            if (c >= 0 && Simulation.Distance(w, p, c) < 160) room = false;
        }
        return new SiteParts(fert, w.PRiver[p] != 0 ? 15 : 0, w.PCoast[p] != 0 ? 10 : 0, variety, hills * 10, room ? 5 : 0);
    }

    /// <summary>Where the elders (or a bot whose spot was taken) go: the best site near the camp, else the best one up to
    /// <see cref="FarRadius"/> steps away; -1 when the land around is all taken.</summary>
    public static int FallbackSite(WorldData w, GameState s, int n) =>
        BestSites(w, s, n, 1) is { Count: > 0 } near ? near[0] : BestSites(w, s, n, 1, FarRadius) is { Count: > 0 } far ? far[0] : -1;

    /// <summary>The best sites to settle within <paramref name="radius"/> steps of the camp (best first, ties by id).</summary>
    public static List<int> BestSites(WorldData w, GameState s, int n, int count, int radius = NearRadius)
    {
        var list = new List<int>();
        var nat = s.Nat[n];
        if (nat.Camp < 0) return list;
        var bfs = SimScratch.For(w, s).B;
        bfs.RunLand(w, stackalloc int[] { nat.Camp });
        var cand = new List<(int score, int p)>();
        for (int k = 0; k < bfs.Count; k++)
        {
            int p = bfs.Queue[k];
            if (bfs.Dist[p] > radius) break;
            if (nat.Fog is { } f && !f.Explored[p]) continue;
            if (CheckSettle(w, s, n, p) != SettleError.None) continue;
            cand.Add((SiteScore(w, s, n, p), p));
        }
        cand.Sort((a, b) => a.score != b.score ? b.score.CompareTo(a.score) : a.p.CompareTo(b.p));
        for (int i = 0; i < cand.Count && i < count; i++) list.Add(cand[i].p);
        return list;
    }

    // ------------------------------------------------------------------ founding

    public static SettleError CheckSettle(WorldData w, GameState s, int n, int p)
    {
        if (s.Nat[n].Camp < 0) return SettleError.Settled;
        if (p < 0 || p >= w.P || w.PLand[p] != 1) return SettleError.NotLand;
        if (s.Owner[p] >= 0) return SettleError.Owned;
        if (Cities.CityNearAny(w, s, p)) return SettleError.TooClose;
        return SettleError.None;
    }

    /// <summary>The legend a bot (or the elders) makes the myth: the first one it found.</summary>
    public static int BestMyth(NationState nat)
    {
        for (int l = 0; l < Legends.Length; l++) if (HasLegend(nat, l)) return l;
        return -1;
    }

    /// <summary>The tribe settles on its camp: the capital, the land around it, the myth. Caller validated with CheckSettle.</summary>
    internal static void Found(WorldData w, GameState s, int n, int myth, ISimSink sink, ref List<int> changed)
    {
        var nat = s.Nat[n];
        int p = nat.Camp;
        var nation = s.Nations[n];
        var got = changed ??= new List<int>();
        Take(p);
        s.CapitalOf[p] = (short)n;
        s.NationCapital[n] = p;
        s.Pop[p] = (int)Math.Min(int.MaxValue, (long)s.Pop[p] + nat.TribePop);
        s.Mood[p] = 65;
        foreach (int q in w.Adj[p]) if (w.PLand[q] == 1 && s.Owner[q] < 0 && !Cities.IsCity(s, q)) Take(q);
        nat.Materials += nat.Supplies / 2;
        nat.Myth = myth >= 0 && HasLegend(nat, myth) ? myth : -1;
        if (nat.Myth >= 0) Character.OnMyth(s, n, nat.Myth);
        nat.Camp = -1; nat.CampPath = null; nat.CampStep = 0; nat.CampSub = 0;
        nat.TribePop = 0;
        if (s.City != null) Cities.Reassign(w, s, n);
        Simulation.SyncQueue(s, n);

        if (sink == null) return;
        if (nat.Human)
        {
            sink.Notify("home", $"Род осел у очага. Так основана столица {w.PName[p]}");
            if (nat.Myth >= 0) sink.Notify("book", $"Миф «{Legends[nat.Myth].Name}»: {Legends[nat.Myth].Line} {Legends[nat.Myth].Myth}");
        }
        else if (nat.Fog == null && Simulation.MetByHumanPublic(s, n))
            sink.Notify("home", $"{nation.Name} осели и основали столицу {w.PName[p]}");

        void Take(int q)
        {
            s.Owner[q] = s.Controller[q] = (short)n;
            s.Religion[q] = (sbyte)nation.Religion;
            if (s.City != null) s.City[q] = p;
            got.Add(q);
        }
    }

    // ------------------------------------------------------------------ myths in the rules

    /// <summary>Population cap bonus of the myth for province p of nation o, in the capacity's ‰ units.</summary>
    internal static int MythCapacity(WorldData w, GameState s, int o, int p) =>
        HasMyth(s, o, River) && w.PRiver[p] != 0 ? 240 : HasMyth(s, o, Coast) && w.PCoast[p] != 0 ? 160 : 0;

    internal static int MythMaterials(GameState s, int o, int p)
    {
        int m = 0;
        if (HasMyth(s, o, Hills) && s.Buildings[p].Contains(Bld.Quarry)) m++;
        if (HasMyth(s, o, Forest) && s.Buildings[p].Contains(Bld.Lumber)) m++;
        return m;
    }

    internal static int MythMood(GameState s, int o) => HasMyth(s, o, Friends) ? 3 : 0;
    internal static int MythScoutRange(GameState s, int n) => HasMyth(s, n, Steppe) ? 1 : 0;
}
