using System;
using System.Runtime.CompilerServices;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

public enum ExcavateError { None, NotRuin, NotOwned, Dug, NeedEra, NoGold }

/// <summary>
/// Ruins and archaeology. Some land (about one province in 170, picked by the world seed) holds the ruins of people who
/// lived there before history began. They wait under the grass until a nation that owns the place reaches
/// <see cref="Era"/> (Возрождение) and pays <see cref="Cost"/> gold for the dig (<see cref="Cmd.Excavate"/>): the find
/// brings <see cref="Glory"/> glory, <see cref="Knowledge"/> research points towards the next study and a story.
/// Bots dig too. Who left the ruins is told by the UI only (any name, even one of the player's former nations): the
/// rules know just where the ruins are and which are dug (GameState.RuinsDug, a bit per ruin).
/// </summary>
public static class Archaeology
{
    public const int Era = 4, Cost = 150, Glory = 10, Knowledge = 400;
    /// <summary>Chance per land province of ruins, ‰.</summary>
    public const int RuinPermille = 6;

    static readonly ConditionalWeakTable<WorldData, int[]> Cache = new();

    /// <summary>The provinces with ruins, in id order (the index is the ruin's bit in GameState.RuinsDug).</summary>
    public static int[] Ruins(WorldData w) => Cache.GetValue(w, Find);

    static int[] Find(WorldData w)
    {
        var list = new System.Collections.Generic.List<int>();
        for (int p = 0; p < w.P; p++)
            if (w.PLand[p] == 1 && w.PBiome[p] is not (1 or 2) && SimRng.Permille(w.Seed, 191, p, 0) < RuinPermille) list.Add(p);
        return list.ToArray();
    }

    /// <summary>Ruin index of province p, or -1.</summary>
    public static int IndexOf(WorldData w, int p) { int i = Array.BinarySearch(Ruins(w), p); return i >= 0 ? i : -1; }

    public static bool HasRuins(WorldData w, int p) => IndexOf(w, p) >= 0;

    public static bool Dug(GameState s, int ruin) => s.RuinsDug != null && ruin >= 0 && (s.RuinsDug[ruin >> 6] & (1UL << (ruin & 63))) != 0;

    public static bool Dug(WorldData w, GameState s, int p) => Dug(s, IndexOf(w, p));

    public static ExcavateError Check(WorldData w, GameState s, int p, int n)
    {
        int i = (uint)p < (uint)w.P ? IndexOf(w, p) : -1;
        if (i < 0) return ExcavateError.NotRuin;
        if (s.Owner[p] != n) return ExcavateError.NotOwned;
        if (Dug(s, i)) return ExcavateError.Dug;
        if (s.Nat[n].Era < Era) return ExcavateError.NeedEra;
        if (s.Nat[n].Treasury < Cost * Rules.Cents) return ExcavateError.NoGold;
        return ExcavateError.None;
    }

    /// <summary>Dig the ruins of p for nation n (validated by Check): the cost, the mark, glory and knowledge.</summary>
    internal static void Dig(WorldData w, GameState s, int p, int n)
    {
        int i = IndexOf(w, p);
        s.RuinsDug ??= new ulong[(Ruins(w).Length + 63) / 64 + 1];
        if ((i >> 6) >= s.RuinsDug.Length) Array.Resize(ref s.RuinsDug, (i >> 6) + 1);
        s.RuinsDug[i >> 6] |= 1UL << (i & 63);
        var nat = s.Nat[n];
        nat.Treasury -= Cost * Rules.Cents;
        nat.Glory += Glory;
        nat.TechPool += Knowledge;
    }

    /// <summary>What the dig at p found (the same every time).</summary>
    public static string FindAt(WorldData w, int p) => Finds[SimRng.Pick(w.Seed, 192, p, 0, Finds.Length)];

    public static readonly string[] Finds =
    {
        "глиняные таблички с первым счётом: зарубки, зарубки и одна обида",
        "бронзовый шлем без хозяина и без единой вмятины",
        "очаг, выложенный камнем так ровно, как сейчас не умеют",
        "кости собаки, похороненной с почестями, как вождь",
        "бусы из ракушек за сотню дней пути от моря",
        "стену с рисунками: охотники, звери и кто-то очень высокий",
        "зерно в запечатанном горшке — оно ещё всходит",
        "печать с именем царя, которого нет ни в одной летописи",
        "детскую игрушку — глиняную лошадку на колёсах",
        "серп из обсидиана, острый до сих пор",
    };

    /// <summary>Bots dig what they own once they can afford it twice over (a salted chance every 50 cycles).</summary>
    internal static void Cycle(WorldData w, GameState s, int cycle, ISimSink sink)
    {
        if (cycle % 50 != 0) return;
        var ruins = Ruins(w);
        foreach (int p in ruins)
        {
            int n = s.Owner[p];
            if (n < 0 || s.Nat[n].Control != NationControl.Bot || s.Nat[n].Treasury < 2 * Cost * Rules.Cents) continue;
            if (Check(w, s, p, n) != ExcavateError.None || !SimRng.Chance(w.Seed, 193, p, cycle, 1, 3)) continue;
            Commands.Apply(w, s, Cmd.Excavate(n, p), sink);
        }
    }
}
