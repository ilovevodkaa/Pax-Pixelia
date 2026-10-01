using System;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

/// <summary>What an eureka counts in the nation's land (one pass over the map gives them all).</summary>
public enum EurekaCount : byte
{
    Fertile, Forest, Hills, Steppe, Coast, Provinces, Cities, Buildings,
    Farms, RiverFarms, Pastures, Fisheries, Quarries, Granaries, Markets, Shrines, Mines, Copper, CopperTin,
}

/// <summary>One eureka: the technology it speeds, what triggers it (a count in the nation's land), the trigger as the
/// player reads it, and the anecdote told when it strikes.</summary>
public sealed record EurekaDef(string Tech, EurekaCount What, int Need, string Trigger, string Line);

/// <summary>
/// Озарения (GDD 9.5, CONTENT §11): a deed that suggests a technology puts <see cref="Permille"/> of its price into it at
/// once, like Civ's eurekas, with an anecdote in the voice of the era. Each strikes once per nation, studied or not (the
/// points wait until the technology is chosen), never for a technology already known. Only deeds count: what the land
/// already held at the founding is marked quietly (<see cref="Baseline"/>). Checked every <see cref="CheckCycles"/> rules
/// cycles from the state itself, so bots get theirs by the same rules.
/// </summary>
public static class Eurekas
{
    public const int Permille = 400, CheckCycles = 4;

    public static readonly EurekaDef[] All =
    {
        // ---- Первобытная ----
        new("wild_grain", EurekaCount.Fertile, 3, "три плодородные провинции", "Птицы клевали зёрна у стоянки. Кто-то догадался бросить зерно в землю"),
        new("stone_axe", EurekaCount.Forest, 3, "три лесные провинции", "Камень, привязанный к палке, рубит лучше камня в руке"),
        new("flint", EurekaCount.Hills, 2, "две провинции в холмах", "Один камень раскололся о другой и стал острым, как зуб"),
        new("harpoon", EurekaCount.Coast, 3, "три провинции у воды", "Рыба ускользала из рук, пока кто-то не заточил палку"),
        new("taming", EurekaCount.Steppe, 2, "две провинции в степи", "Волчонок прибился к костру и не ушёл"),
        new("rafts", EurekaCount.Fisheries, 2, "две рыбацкие пристани", "Кто-то упал в реку и не утонул"),
        new("tally", EurekaCount.Buildings, 8, "восемь построек", "Добра стало больше, чем пальцев на руках"),
        new("ancestors", EurekaCount.Provinces, 15, "пятнадцать провинций", "Род разросся, и старики стали путать имена прадедов"),
        // ---- Древний мир ----
        new("irrigation", EurekaCount.RiverFarms, 2, "две фермы у реки", "Река разлилась и напоила поле. Люди решили повторить это сами"),
        new("pottery", EurekaCount.Farms, 3, "три фермы", "Зерна стало больше, чем корзин"),
        new("plough", EurekaCount.Farms, 5, "пять ферм", "Бык тащил бревно и пропахал борозду глубже мотыги"),
        new("crop_rotation", EurekaCount.Farms, 8, "восемь ферм", "Поле, брошенное на год, родило вдвое"),
        new("calendar", EurekaCount.Fertile, 6, "шесть плодородных провинций", "Жрецы заметили: разлив приходит, когда восходит одна и та же звезда"),
        new("wheel", EurekaCount.Pastures, 3, "три пастбища", "Гончар придумал круг. Жрецы спорят, не колдовство ли это"),
        new("weaving", EurekaCount.Pastures, 2, "два пастбища", "Шерсть спуталась на кусте, и вышла нить"),
        new("riding", EurekaCount.Pastures, 5, "пять пастбищ", "Мальчишка залез на коня на спор и не упал"),
        new("copper", EurekaCount.Copper, 1, "разведанная медь", "Зелёный камень в огне потёк, как вода"),
        new("bronze", EurekaCount.CopperTin, 1, "разведанные медь и олово", "Два мягких металла оказались одним твёрдым"),
        new("mining", EurekaCount.Mines, 1, "рудник на жиле", "Жила уходит вглубь, и люди пошли за ней"),
        new("masonry", EurekaCount.Quarries, 3, "три каменоломни", "Из камня можно не только рубить, но и строить"),
        new("brick", EurekaCount.Granaries, 2, "два амбара", "Глина у амбара засохла на солнце и стала камнем"),
        new("sail", EurekaCount.Fisheries, 4, "четыре рыбацкие пристани", "Ветер унёс плот со шкурой быстрее, чем гребли"),
        new("barter", EurekaCount.Cities, 3, "три города", "В двух городах есть то, чего нет в третьем"),
        new("weights", EurekaCount.Markets, 2, "два рынка", "Торговцы подрались из-за горсти зерна"),
        new("writing", EurekaCount.Markets, 3, "три рынка", "Писцы устали запоминать долги"),
        new("mathematics", EurekaCount.Markets, 4, "четыре рынка", "Счёт по зарубкам больше не помещается на палке"),
        new("priesthood", EurekaCount.Shrines, 3, "три святилища", "Кто-то должен следить за огнём во всех святилищах"),
        new("temples", EurekaCount.Shrines, 5, "пять святилищ", "Святилищ стало столько, что духам тесно"),
        new("chief_law", EurekaCount.Provinces, 25, "двадцать пять провинций", "Земель стало так много, что старейшины не успевают судить"),
        new("first_cities", EurekaCount.Cities, 4, "четыре города", "Городам нужен общий закон стен"),
    };

    static readonly int Kinds = Enum.GetValues<EurekaCount>().Length;

    /// <summary>Technology index of each eureka (resolved once; ids never move).</summary>
    public static readonly int[] TechOf = Resolve();

    static int[] Resolve()
    {
        var t = new int[All.Length];
        for (int e = 0; e < All.Length; e++)
        {
            t[e] = Techs.Index(All[e].Tech);
            if (t[e] < 0) throw new InvalidOperationException($"eureka for an unknown technology «{All[e].Tech}»");
        }
        return t;
    }

    /// <summary>The eureka of technology t, or -1.</summary>
    public static int Of(int t) => Array.IndexOf(TechOf, t);

    public static bool Fired(NationState nat, int e) => nat.Eurekas != null && (nat.Eurekas[e >> 6] & (1UL << (e & 63))) != 0;

    public static void Init(NationState nat) => nat.Eurekas ??= new ulong[(All.Length + 63) / 64];

    public static bool IsBlank(NationState nat)
    {
        if (nat.Eurekas == null) return true;
        foreach (ulong v in nat.Eurekas) if (v != 0) return false;
        return true;
    }

    /// <summary>At the founding (a settled start, or a tribe that settles): whatever the land already satisfies is marked
    /// without points, so eurekas reward what the people do, not where they were born.</summary>
    public static void Baseline(WorldData w, GameState s, int n)
    {
        var nat = s.Nat[n];
        Init(nat);
        if (Nomads.IsNomad(nat) || s.NationCapital[n] < 0) return;
        Span<int> row = stackalloc int[Kinds];
        var facts = WorldFacts.Of(w);
        for (int p = 0; p < w.P; p++) if (s.Owner[p] == n) CountProvince(w, s, facts, p, row);
        row[(int)EurekaCount.CopperTin] = row[(int)EurekaCount.CopperTin] == 3 ? 1 : 0;
        for (int e = 0; e < All.Length; e++)
            if (row[(int)All[e].What] >= All[e].Need) nat.Eurekas[e >> 6] |= 1UL << (e & 63);
    }

    // ------------------------------------------------------------------ the rules cycle

    public static void Cycle(WorldData w, GameState s, int cycle, ISimSink sink)
    {
        if (cycle % CheckCycles != 0) return;
        int nN = s.Nat.Length;
        bool any = false;
        for (int n = 0; n < nN && !any; n++) any = Pending(s.Nat[n]);
        if (!any) return;
        var counts = Count(w, s);
        for (int n = 0; n < nN; n++)
        {
            var nat = s.Nat[n];
            if (!Pending(nat)) continue;
            for (int e = 0; e < All.Length; e++)
            {
                if (Fired(nat, e)) continue;
                int t = TechOf[e];
                if (Techs.Known(nat, t))
                {
                    nat.Eurekas[e >> 6] |= 1UL << (e & 63);   // known before the deed: nothing left to suggest
                    continue;
                }
                if (counts[n * Kinds + (int)All[e].What] < All[e].Need) continue;
                Strike(s, n, e, sink);
            }
        }
    }

    static bool Pending(NationState nat)
    {
        if (Nomads.IsNomad(nat)) return false;
        Init(nat);
        for (int e = 0; e < All.Length; e++) if (!Fired(nat, e)) return true;
        return false;
    }

    static void Strike(GameState s, int n, int e, ISimSink sink)
    {
        var nat = s.Nat[n];
        int t = TechOf[e];
        nat.Eurekas[e >> 6] |= 1UL << (e & 63);
        nat.TechPts[t] += (long)Techs.Cost(t, s.Pace) * Permille / 1000;
        Character.Deed(s, n, Character.Tradition, true, 2);
        if (nat.Human) sink?.Notify("atom", $"Озарение: «{Techs.All[t].Name}». {All[e].Line}. Изучение продвинулось на {Permille / 10}%");
    }

    /// <summary>Per nation × EurekaCount: what its land holds now (Kinds counts per nation, in one array).</summary>
    static int[] Count(WorldData w, GameState s)
    {
        int nN = s.Nat.Length;
        var sc = SimScratch.For(w, s);
        if (sc.EurekaCounts.Length != nN * Kinds) sc.EurekaCounts = new int[nN * Kinds];
        var c = sc.EurekaCounts;
        Array.Clear(c);
        var facts = WorldFacts.Of(w);
        for (int p = 0; p < w.P; p++)
        {
            int o = s.Owner[p];
            if (o < 0) continue;
            CountProvince(w, s, facts, p, c.AsSpan(o * Kinds, Kinds));
        }
        for (int n = 0; n < nN; n++)
        {
            var row = c.AsSpan(n * Kinds, Kinds);
            row[(int)EurekaCount.CopperTin] = row[(int)EurekaCount.CopperTin] == 3 ? 1 : 0;   // bit 1 copper, bit 2 tin
        }
        return c;
    }

    static void CountProvince(WorldData w, GameState s, WorldFacts facts, int p, Span<int> row)
    {
        int b = w.PBiome[p];
        row[(int)EurekaCount.Provinces]++;
        if (facts.FertPm[p] >= 600) row[(int)EurekaCount.Fertile]++;
        if (b is 5 or 8 or 12) row[(int)EurekaCount.Forest]++;
        if (facts.Hills[p] || b is 2 or 3) row[(int)EurekaCount.Hills]++;
        if (b is 6 or 11) row[(int)EurekaCount.Steppe]++;
        if (w.PCoast[p] != 0) row[(int)EurekaCount.Coast]++;
        if (Cities.IsCity(s, p)) row[(int)EurekaCount.Cities]++;
        if (Rules.IsMine(s, p)) row[(int)EurekaCount.Mines]++;
        int ore = Rules.KnownOre(s, p);
        if (ore == Rules.OreCopper) { row[(int)EurekaCount.Copper]++; row[(int)EurekaCount.CopperTin] |= 1; }
        else if (ore == Rules.OreTin) row[(int)EurekaCount.CopperTin] |= 2;
        foreach (var x in s.Buildings[p])
        {
            row[(int)EurekaCount.Buildings]++;
            switch (x)
            {
                case Bld.Farm: row[(int)EurekaCount.Farms]++; if (w.PRiver[p] != 0) row[(int)EurekaCount.RiverFarms]++; break;
                case Bld.Pasture: row[(int)EurekaCount.Pastures]++; break;
                case Bld.Fishery: row[(int)EurekaCount.Fisheries]++; break;
                case Bld.Quarry: row[(int)EurekaCount.Quarries]++; break;
                case Bld.Granary: row[(int)EurekaCount.Granaries]++; break;
                case Bld.Market: row[(int)EurekaCount.Markets]++; break;
                case Bld.Shrine: row[(int)EurekaCount.Shrines]++; break;
            }
        }
    }

    /// <summary>How far nation n is towards eureka e (for the technology screen): the count now, capped at the need.</summary>
    public static int Progress(WorldData w, GameState s, int n, int e)
    {
        Span<int> row = stackalloc int[Kinds];
        var facts = WorldFacts.Of(w);
        for (int p = 0; p < w.P; p++) if (s.Owner[p] == n) CountProvince(w, s, facts, p, row);
        row[(int)EurekaCount.CopperTin] = row[(int)EurekaCount.CopperTin] == 3 ? 1 : 0;
        return Math.Min(All[e].Need, row[(int)All[e].What]);
    }
}
