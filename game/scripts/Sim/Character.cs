using System;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

/// <summary>One scale of a nation's character: two opposite poles, each with its adjective, its «народ …» noun and
/// the bonus its leaning (|v| ≥ 35) and essence (|v| ≥ 70) give, as technology effects.</summary>
public sealed record CharPole(string Adj, string People, string Leaning, string Essence,
                              (TechFx Fx, int Amount)[] LeanFx, (TechFx Fx, int Amount)[] EssenceFx);

public sealed record CharScale(string Name, CharPole Left, CharPole Right);

/// <summary>A permanent trait earned by a deed (CONTENT §10.2), checked every few cycles.</summary>
public sealed record CharTrait(string Name, string Condition, string Effect, string Icon, (TechFx Fx, int Amount)[] Fx);

/// <summary>
/// The character of a people (IDEAS N-2, CONTENT §10): no traits at the start, deeds shape it. Every scale has two
/// accumulators, A (left pole) and B (right pole), in deed units × 1000; deeds add to them (<see cref="Deed"/>), and every
/// <see cref="DecayCycles"/> cycles both shrink by 65460/65536 (half-life ≈ 2400 cycles ≈ 20 min at speed 3), so the
/// character follows what the nation does lately. The value v = round(100·(B − A)/(A + B + 30)) runs −100…100;
/// |v| ≥ 35 is a leaning (a small bonus), |v| ≥ 70 the essence (a big one), with a hysteresis of 10. An essence held for
/// <see cref="HardenCycles"/> cycles hardens: its leaning bonus stays for good. Achievement traits are permanent too.
/// All bonuses are technology effects added by <see cref="Techs.Sum"/>, cached per nation in NationState.CharFx.
/// </summary>
public static class Character
{
    public const int Agri = 0, Tradition = 1, Openness = 2, Faith = 3, Commune = 4;
    public const int Lean = 35, EssenceAt = 70, Hysteresis = 10;
    public const int DecayCycles = 4, DecayNum = 65460, DecayDen = 65536;
    public const int HardenCycles = 1200;   // 10 min at speed 3
    public const int TraitCheckCycles = 8;
    /// <summary>Deed units are stored × 1000.</summary>
    public const int Unit = 1000;
    const int MaxAcc = 1_000_000_000;

    static readonly (TechFx, int)[] No = Array.Empty<(TechFx, int)>();

    public static readonly CharScale[] Scales =
    {
        new("Земледельческий ↔ Купеческий",
            new("земледельческий", "пахарей", "Предел населения +5%", "Предел населения +15%",
                new[] { (TechFx.CapPermille, 50) }, new[] { (TechFx.CapPermille, 150) }),
            new("купеческий", "купцов", "Налоги +5%", "Налоги +15%",
                new[] { (TechFx.TaxPermille, 50) }, new[] { (TechFx.TaxPermille, 150) })),
        new("Традиционный ↔ Новаторский",
            new("традиционный", "хранителей обычаев", "Довольство +1", "«Заветы предков»: довольство +4",
                new[] { (TechFx.Mood, 1) }, new[] { (TechFx.Mood, 4) }),
            new("новаторский", "изобретателей", "Изучение +1 в цикл", "Изучение +2 в цикл",
                new[] { (TechFx.Science, 1) }, new[] { (TechFx.Science, 2) })),
        new("Замкнутый ↔ Открытый",
            new("замкнутый", "домоседов", "Стройка берёт на 5% меньше материалов", "Стройка берёт на 15% меньше материалов",
                new[] { (TechFx.MaterialDiscount, 5) }, new[] { (TechFx.MaterialDiscount, 15) }),
            new("открытый", "странников", "Влияние городов +1", "Влияние городов +2, разведчики видят дальше",
                new[] { (TechFx.CityInfluence, 1) }, new[] { (TechFx.CityInfluence, 2), (TechFx.ScoutRange, 1) })),
        new("Набожный ↔ Светский",
            new("набожный", "богомольцев", "Святилища: довольство +1", "Святилища: довольство +3",
                new[] { (TechFx.ShrineMood, 1) }, new[] { (TechFx.ShrineMood, 3) }),
            new("светский", "вольнодумцев", "Изучение +1 в цикл", "Изучение +1 в цикл, налоги +5%",
                new[] { (TechFx.Science, 1) }, new[] { (TechFx.Science, 1), (TechFx.TaxPermille, 50) })),
        new("Общинный ↔ Вольный",
            new("общинный", "общинников", "Предел населения +3%", "Предел населения +8%, довольство +2",
                new[] { (TechFx.CapPermille, 30) }, new[] { (TechFx.CapPermille, 80), (TechFx.Mood, 2) }),
            new("вольный", "вольных людей", "Налоги +3%", "Налоги +10%",
                new[] { (TechFx.TaxPermille, 30) }, new[] { (TechFx.TaxPermille, 100) })),
    };

    public static int Count => Scales.Length;

    public const int RiverKeepers = 0, Highlanders = 1, Ploughmen = 2, Bookmen = 3, Seafarers = 4, Gatherers = 5;

    public static readonly CharTrait[] Traits =
    {
        new("Хранители рек", "15 провинций на реках", "Предел населения у рек +5%", "droplet-off", new[] { (TechFx.RiverCap, 50) }),
        new("Горцы", "10 рудников", "Рудники: +1 материал", "pick", new[] { (TechFx.MineMaterials, 1) }),
        new("Пахари мира", "40 ферм", "Предел населения +5%", "plant", new[] { (TechFx.CapPermille, 50) }),
        new("Книжники", "первыми в мире открыли 10 технологий", "Изучение +1 в цикл", "book", new[] { (TechFx.Science, 1) }),
        new("Мореходы", "20 рыбацких пристаней", "Налоги +5%", "anchor", new[] { (TechFx.TaxPermille, 50) }),
        new("Собиратели земель", "60 провинций", "Влияние городов +1", "flag", new[] { (TechFx.CityInfluence, 1) }),
    };

    /// <summary>Bits of NationState.CharTraits above the achievements: pole k (scale·2 + side) hardened for good.</summary>
    public const int HardenedBit = 16;

    // ------------------------------------------------------------------ state

    public static void Init(NationState nat)
    {
        nat.CharA ??= new int[Count];
        nat.CharB ??= new int[Count];
        nat.CharLevel ??= new sbyte[Count];
        nat.CharHeld ??= new short[Count];
        Refresh(nat);
    }

    /// <summary>No deeds, levels, traits or firsts yet (a fresh nation, or one loaded from a save before characters).</summary>
    public static bool IsBlank(NationState nat)
    {
        if (nat.CharTraits != 0 || nat.FirstTechs != 0) return false;
        if (nat.CharA == null) return true;
        for (int k = 0; k < nat.CharA.Length; k++)
            if (nat.CharA[k] != 0 || nat.CharB[k] != 0 || nat.CharLevel[k] != 0 || nat.CharHeld[k] != 0) return false;
        return true;
    }

    /// <summary>The scale's value, −100 (left pole) … 100 (right pole).</summary>
    public static int Value(NationState nat, int scale)
    {
        if (nat.CharA == null) return 0;
        long a = nat.CharA[scale], b = nat.CharB[scale];
        long num = 100 * (b - a), den = a + b + 30L * Unit;
        return (int)((num >= 0 ? num + den / 2 : num - den / 2) / den);
    }

    /// <summary>−2 left essence, −1 left leaning, 0 none, 1 right leaning, 2 right essence.</summary>
    public static int Level(NationState nat, int scale) => nat.CharLevel == null ? 0 : nat.CharLevel[scale];

    public static CharPole Pole(int scale, int level) => level < 0 ? Scales[scale].Left : Scales[scale].Right;

    public static bool Has(NationState nat, int trait) => (nat.CharTraits & (1 << trait)) != 0;
    public static bool Hardened(NationState nat, int scale, bool right) => (nat.CharTraits & (1 << (HardenedBit + scale * 2 + (right ? 1 : 0)))) != 0;

    // ------------------------------------------------------------------ deeds

    /// <summary>A deed of nation n pushes a scale towards a pole by `units` (whole deed units; ‰ precision via <see cref="DeedPermille"/>).</summary>
    public static void Deed(GameState s, int n, int scale, bool right, int units) => DeedPermille(s, n, scale, right, units * Unit);

    public static void DeedPermille(GameState s, int n, int scale, bool right, int amount)
    {
        if ((uint)n >= (uint)s.Nat.Length) return;
        var nat = s.Nat[n];
        if (nat.CharA == null) Init(nat);
        var acc = right ? nat.CharB : nat.CharA;
        acc[scale] = (int)Math.Min(MaxAcc, (long)acc[scale] + amount);
    }

    /// <summary>Building b (CONTENT §10.1): fields and herds make tillers, markets traders, granaries a commune,
    /// shrines the faithful, mills and quarries a people that makes do with its own, piers a people facing the sea.</summary>
    public static void OnBuild(GameState s, int n, Bld b)
    {
        switch (b)
        {
            case Bld.Farm: case Bld.Pasture: Deed(s, n, Agri, false, 2); break;
            case Bld.Fishery: Deed(s, n, Agri, false, 1); Deed(s, n, Openness, true, 1); break;
            case Bld.Granary: Deed(s, n, Agri, false, 1); Deed(s, n, Commune, false, 2); break;
            case Bld.Market: Deed(s, n, Agri, true, 2); Deed(s, n, Commune, true, 1); break;
            case Bld.Shrine: Deed(s, n, Faith, false, 1); break;
            case Bld.Lumber: case Bld.Quarry: Deed(s, n, Openness, false, 1); break;
        }
    }

    /// <summary>A province joined nation n: land next to another nation's is a meeting with strangers.</summary>
    public static void OnClaim(WorldData w, GameState s, int p, int n)
    {
        foreach (int q in w.Adj[p])
            if (s.Owner[q] >= 0 && s.Owner[q] != n) { Deed(s, n, Openness, true, 2); return; }
    }

    /// <summary>Technology t learned: every discovery leans to the worldly, first in the world is innovation; knowledge,
    /// faith and trade techs push their scales further.</summary>
    public static void OnLearn(GameState s, int n, int t)
    {
        bool first = true;
        for (int m = 0; m < s.Nat.Length && first; m++) if (m != n && Techs.Known(s.Nat[m], t)) first = false;
        var nat = s.Nat[n];
        if (Techs.All[t].Cost <= 0) return;   // the root everyone knows
        Deed(s, n, Faith, true, 1);
        if (first)
        {
            Deed(s, n, Tradition, true, 3);
            nat.FirstTechs++;
        }
        foreach (var (fx, _) in Techs.All[t].Fx)
            if (fx == TechFx.Science) Deed(s, n, Faith, true, 1);
            else if (fx == TechFx.ShrineMood) Deed(s, n, Faith, false, 2);
        if (Techs.All[t].Unlocks is Bld.Market) Deed(s, n, Agri, true, 2);
        if (Techs.All[t].Unlocks is Bld.Shrine) Deed(s, n, Faith, false, 2);
    }

    /// <summary>The capital finished a project: monuments and old ways, and the building it brings counts as built.</summary>
    public static void OnProject(GameState s, int n, Bld? b)
    {
        Deed(s, n, Tradition, false, 3);
        if (b is Bld x) OnBuild(s, n, x);
    }

    /// <summary>Custom keeps by itself: each rules cycle adds this (‰ of a deed) to the traditional pole, so a people that
    /// stops discovering drifts back to its old ways (≈ 17 units at rest against the decay).</summary>
    public const int CustomPermille = 5;

    /// <summary>A scout party came home with maps.</summary>
    public static void OnScoutsBack(GameState s, int n) => Deed(s, n, Openness, true, 2);

    /// <summary>Geologists went out: curiosity.</summary>
    public static void OnSurvey(GameState s, int n) => Deed(s, n, Tradition, true, 1);

    /// <summary>A new town: settlers who go their own way, out into new lands.</summary>
    public static void OnFound(GameState s, int n)
    {
        Deed(s, n, Commune, true, 1);
        Deed(s, n, Openness, true, 2);
    }

    /// <summary>The founding myth is the first big push (CONTENT §10.1: «Дети реки» +15 …).</summary>
    public static void OnMyth(GameState s, int n, int myth)
    {
        switch (myth)
        {
            case Nomads.River: Deed(s, n, Agri, false, 15); break;
            case Nomads.Coast: Deed(s, n, Agri, true, 15); break;
            case Nomads.Hills: Deed(s, n, Tradition, false, 15); break;
            case Nomads.Steppe: Deed(s, n, Commune, true, 15); break;
            case Nomads.Forest: Deed(s, n, Faith, false, 15); break;
            case Nomads.Friends: Deed(s, n, Openness, true, 15); break;
        }
    }

    /// <summary>Push of the land under a capital that was never a camp (a game started settled, without the nomad phase):
    /// the same places the legends come from shape the people a little, as a myth would.</summary>
    public const int LandPush = 8;

    public static void SeedFromLand(WorldData w, GameState s, int n)
    {
        int p = s.NationCapital[n];
        if (p < 0 || Nomads.IsNomad(s.Nat[n]) || s.Nat[n].Myth >= 0) return;
        int b = w.PBiome[p];
        if (w.PRiver[p] != 0) Deed(s, n, Agri, false, LandPush);
        if (w.PCoast[p] != 0) Deed(s, n, Agri, true, LandPush);
        if (WorldFacts.Of(w).Hills[p] || b is 2 or 3) Deed(s, n, Tradition, false, LandPush);
        if (b is 6 or 11) Deed(s, n, Commune, true, LandPush);
        if (b is 5 or 8 or 12) Deed(s, n, Faith, false, LandPush);
    }

    // ------------------------------------------------------------------ the rules cycle

    /// <summary>Decay, levels with hysteresis, hardening and achievements; tells a human what changed.</summary>
    public static void Cycle(WorldData w, GameState s, int cycle, ISimSink sink)
    {
        bool decay = cycle % DecayCycles == 0, traits = cycle % TraitCheckCycles == 0;
        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            if (nat.CharA == null) Init(nat);
            if (Nomads.IsNomad(nat)) continue;
            DeedPermille(s, n, Tradition, false, CustomPermille);
            bool dirty = false;
            for (int k = 0; k < Count; k++)
            {
                if (decay)
                {
                    nat.CharA[k] = (int)((long)nat.CharA[k] * DecayNum / DecayDen);
                    nat.CharB[k] = (int)((long)nat.CharB[k] * DecayNum / DecayDen);
                }
                int v = Value(nat, k), old = nat.CharLevel[k], lvl = NewLevel(old, v);
                if (lvl != old)
                {
                    nat.CharLevel[k] = (sbyte)lvl;
                    dirty = true;
                    bool news = lvl != 0 && (Math.Abs(lvl) > Math.Abs(old) || Math.Sign(lvl) != Math.Sign(old));
                    if (news && nat.Human) sink?.Notify("users", LevelText(s, n, k, lvl));
                }
                if (Math.Abs(lvl) == 2)
                {
                    if (nat.CharHeld[k] < short.MaxValue) nat.CharHeld[k]++;
                    if (nat.CharHeld[k] == HardenCycles && !Hardened(nat, k, lvl > 0))
                    {
                        nat.CharTraits |= 1 << (HardenedBit + k * 2 + (lvl > 0 ? 1 : 0));
                        dirty = true;
                        if (nat.Human) sink?.Notify("history", $"Это уже не привычка, а суть: народ навсегда {Pole(k, lvl).Adj}. {Pole(k, lvl).Leaning} останется при нём");
                    }
                }
                else nat.CharHeld[k] = 0;
            }
            if (traits && CheckTraits(w, s, n, sink)) dirty = true;
            if (dirty) Refresh(nat);
        }
    }

    static int NewLevel(int old, int v)
    {
        int a = Math.Abs(v), sign = Math.Sign(v);
        int want = a >= EssenceAt ? 2 : a >= Lean ? 1 : 0;
        if (old == 0 || Math.Sign(old) != sign) return sign * want;
        int keep = Math.Abs(old);
        // the same side: step up at once, step down only past the hysteresis
        if (want >= keep) return sign * want;
        int floor = keep == 2 ? EssenceAt - Hysteresis : Lean - Hysteresis;
        if (a >= floor) return old;
        return sign * (a >= Lean - Hysteresis && keep == 2 ? 1 : want);
    }

    static string LevelText(GameState s, int n, int k, int lvl)
    {
        var pole = Pole(k, lvl);
        string name = s.Nations[n].Name;
        return Math.Abs(lvl) == 2
            ? $"Суть народа: {name} — {pole.Adj} народ. {pole.Essence}"
            : $"{name} склоняется к тому, чтобы стать народом {pole.People}. {pole.Leaning}";
    }

    static bool CheckTraits(WorldData w, GameState s, int n, ISimSink sink)
    {
        var nat = s.Nat[n];
        int rivers = 0, mines = 0, farms = 0, fisheries = 0, provinces = 0;
        for (int p = 0; p < w.P; p++)
        {
            if (s.Owner[p] != n) continue;
            provinces++;
            if (w.PRiver[p] != 0) rivers++;
            if (Rules.IsMine(s, p)) mines++;
            foreach (var b in s.Buildings[p])
                if (b == Bld.Farm) farms++;
                else if (b == Bld.Fishery) fisheries++;
        }
        bool any = false;
        Earn(RiverKeepers, rivers >= 15);
        Earn(Highlanders, mines >= 10);
        Earn(Ploughmen, farms >= 40);
        Earn(Bookmen, nat.FirstTechs >= 10);
        Earn(Seafarers, fisheries >= 20);
        Earn(Gatherers, provinces >= 60);
        return any;

        void Earn(int t, bool ok)
        {
            if (!ok || Has(nat, t)) return;
            nat.CharTraits |= 1 << t;
            any = true;
            if (nat.Human) sink?.Notify(Traits[t].Icon, $"Народ заслужил черту «{Traits[t].Name}»: {Traits[t].Condition}. {Traits[t].Effect}");
            else if (sink != null && Simulation.MetByHumanPublic(s, n)) sink.Notify(Traits[t].Icon, $"{s.Nations[n].Name}: народ заслужил черту «{Traits[t].Name}»");
        }
    }

    // ------------------------------------------------------------------ bonuses

    /// <summary>Rebuild the cached effects (NationState.CharFx, indexed by TechFx) from levels, hardened poles and traits.</summary>
    public static void Refresh(NationState nat)
    {
        int nFx = Enum.GetValues<TechFx>().Length;
        if (nat.CharFx == null || nat.CharFx.Length != nFx) nat.CharFx = new int[nFx];
        else Array.Clear(nat.CharFx);
        if (nat.CharLevel == null) return;
        for (int k = 0; k < Count; k++)
        {
            int lvl = nat.CharLevel[k];
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1;
                var pole = right ? Scales[k].Right : Scales[k].Left;
                bool active = lvl != 0 && (lvl > 0) == right;
                if (active) Add(nat, Math.Abs(lvl) == 2 ? pole.EssenceFx : pole.LeanFx);
                else if (Hardened(nat, k, right)) Add(nat, pole.LeanFx);   // a hardened pole keeps its leaning
            }
        }
        for (int t = 0; t < Traits.Length; t++) if (Has(nat, t)) Add(nat, Traits[t].Fx);
    }

    static void Add(NationState nat, (TechFx Fx, int Amount)[] fx)
    {
        foreach (var (f, a) in fx) nat.CharFx[(int)f] += a;
    }

    /// <summary>What the character adds to one technology effect (Techs.Sum adds it).</summary>
    public static int Fx(NationState nat, TechFx fx) => nat.CharFx == null ? 0 : nat.CharFx[(int)fx];

    // ------------------------------------------------------------------ words

    /// <summary>«Портрет народа»: one line from the two strongest scales, e.g. «набожный народ пахарей».</summary>
    public static string Portrait(NationState nat)
    {
        int a = -1, b = -1;
        for (int k = 0; k < Count; k++)
        {
            if (Level(nat, k) == 0) continue;
            if (a < 0 || Strength(nat, k) > Strength(nat, a)) { b = a; a = k; }
            else if (b < 0 || Strength(nat, k) > Strength(nat, b)) b = k;
        }
        if (a < 0) return "Характер народа ещё не сложился";
        var pa = Pole(a, Level(nat, a));
        if (b < 0) return Cap($"{pa.Adj} народ");
        var pb = Pole(b, Level(nat, b));
        return Cap($"{pa.Adj} народ {pb.People}");
    }

    static int Strength(NationState nat, int k) => Math.Abs(Level(nat, k)) * 1000 + Math.Abs(Value(nat, k));

    static string Cap(string t) => t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..];
}
