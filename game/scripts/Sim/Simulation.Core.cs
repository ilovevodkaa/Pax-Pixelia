using System;
using System.Collections.Generic;
using PaxPixelia.Core;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

/// <summary>
/// Yearly rules (pure C#, deterministic): calendar, budget, population, mood, the capital's construction queue,
/// the chronicle of events and the bots' slow expansion. Game calls Year() through Simulation.YearTick.
/// </summary>
public static partial class Simulation
{
    public const int EventEveryYears = 11;
    public const int QueuePctPerYear = 3;

    public sealed record Project(string Name, string DoneText, Bld? Building);

    /// <summary>The capital builds these in turn, each once: building projects already standing in the capital are skipped,
    /// the others (walls, road) are one-off. When everything is done the queue stands idle (ProjectIndex = -1).</summary>
    public static readonly Project[] Projects =
    {
        new("Амбар", "В столице построен амбар", Bld.Granary),
        new("Каменные стены", "Столицу обнесли каменными стенами", null),
        new("Рынок", "В столице открылся рынок", Bld.Market),
        new("Святилище", "В столице освятили новое святилище", Bld.Shrine),
        new("Мощёная дорога", "Главную улицу столицы вымостили камнем", null),
    };

    /// <summary>Prepare a freshly generated state for play: fog, vision and met nations; the capital's first project.</summary>
    public static void Begin(WorldData w, GameState s)
    {
        FogOfWar.Init(w, s);
        int cap = Scouts.Capital(s);
        s.ProjectsDone = 0;
        s.ProjectIndex = NextProject(s, Projects.Length - 1, cap);   // skip what the capital already has
        s.QueueName = s.ProjectIndex >= 0 ? Projects[s.ProjectIndex].Name : null;
    }

    public static void Year(WorldData w, GameState s, ISimSink sink)
    {
        s.Year++;
        if (s.Year == 0) s.Year = 1;          // no year zero: 1 до н. э. → 1 н. э.

        Budget(s);
        Grow(w, s);
        Moods(w, s);
        List<int> changed = null;
        Queue(s, sink, ref changed);
        Bots.Expand(w, s, sink, ref changed);
        if (s.Year % EventEveryYears == 0) Chronicle.Fire(w, s, sink);

        if (changed != null)
        {
            sink?.RaiseProvincesChanged(changed.ToArray());
            FogOfWar.Refresh(w, s, sink);     // new owners may reveal nations we already see
        }
    }

    static void Budget(GameState s)
    {
        var (taxes, upkeep) = Rules.Budget(s);
        s.LastTaxes = taxes; s.LastUpkeep = upkeep;
        s.Gold += taxes - upkeep;
    }

    /// <summary>Population cap of p: fertile land feeds more, farms and granaries raise it, the capital draws people.</summary>
    public static float Capacity(WorldData w, GameState s, int p)
    {
        float baseCap = w.PSize[p] * Math.Max(.05f, w.PFert[p]) * 55f;
        if (s.Owner[p] < 0) return baseCap * .3f;    // nomad tribes
        float m = 1.6f;
        foreach (var b in s.Buildings[p])
            m += b switch { Bld.Farm => .3f, Bld.Granary => .2f, Bld.Fishery => .15f, Bld.Pasture => .1f, _ => 0f };
        if (s.CapitalOf[p] >= 0) m *= 2.5f;
        return baseCap * m;
    }

    /// <summary>Logistic growth: ~0.3–1.5 % a year by fertility and mood, slowing towards Capacity.</summary>
    static void Grow(WorldData w, GameState s)
    {
        for (int p = 0; p < w.P; p++)
        {
            if (w.PLand[p] != 1) continue;
            float pop = s.Pop[p];
            if (pop <= 0) continue;
            float cap = Capacity(w, s, p);
            float r = .003f + .012f * w.PFert[p];
            float mood = Math.Clamp((s.Mood[p] - 30) / 50f, -.5f, 1.2f);
            float room = 1f - pop / cap;
            pop += pop * r * (room >= 0 ? mood * room : room);   // over capacity it shrinks whatever the mood
            s.Pop[p] = Math.Max(10f, pop);
        }
    }

    /// <summary>Mood drifts one point a year towards what the province has: shrines, markets, granaries, the right faith, room to live.</summary>
    static void Moods(WorldData w, GameState s)
    {
        for (int p = 0; p < w.P; p++)
        {
            int o = s.Owner[p];
            int target = 60;
            if (o >= 0)
            {
                foreach (var b in s.Buildings[p])
                    target += b switch { Bld.Shrine => 8, Bld.Market => 3, Bld.Granary => 4, _ => 0 };
                if (s.CapitalOf[p] >= 0) target += 5;
                if (s.Religion[p] >= 0 && s.Religion[p] != Data.Nations[o].Religion) target -= 10;
            }
            target = Math.Clamp(target, 5, 95);
            int m = s.Mood[p];
            m += Math.Sign(target - m);
            double j = SimRng.U(w.Seed, 5, p, s.Year);   // rare ±1 so equal provinces do not all move in unison
            if (j < .04) m--; else if (j > .96) m++;
            s.Mood[p] = (byte)Math.Clamp(m, 0, 100);
        }
    }

    static void Queue(GameState s, ISimSink sink, ref List<int> changed)
    {
        SyncQueue(s);
        if (s.ProjectIndex < 0) return;
        s.QueuePct += QueuePctPerYear;
        if (s.QueuePct < 100) return;
        var pr = Projects[s.ProjectIndex];
        int cap = Scouts.Capital(s);
        if (pr.Building is Bld b)
        {
            if (cap >= 0)
            {
                if (s.Buildings[cap].Count >= s.Slots[cap]) s.Slots[cap]++;   // the project brings its own plot
                s.Buildings[cap].Add(b);
                (changed ??= new List<int>()).Add(cap);
            }
        }
        else s.ProjectsDone |= 1 << s.ProjectIndex;
        sink?.Notify("hammer", pr.DoneText);
        s.QueuePct = 0;
        Advance(s, cap);
    }

    /// <summary>
    /// If the current project's building already stands in the capital (the player built it by hand), move on to the
    /// next project quietly, keeping the progress made so far.
    /// </summary>
    public static void SyncQueue(GameState s)
    {
        int cap = Scouts.Capital(s);
        if (s.ProjectIndex >= 0 && cap >= 0 && Projects[s.ProjectIndex].Building is Bld b && s.Buildings[cap].Contains(b)) Advance(s, cap);
    }

    static void Advance(GameState s, int cap)
    {
        s.ProjectIndex = NextProject(s, s.ProjectIndex, cap);
        s.QueueName = s.ProjectIndex >= 0 ? Projects[s.ProjectIndex].Name : null;
        if (s.ProjectIndex < 0) s.QueuePct = 0;
    }

    /// <summary>The next project after `from` that is still to do, or -1 when the capital has everything.</summary>
    static int NextProject(GameState s, int from, int cap)
    {
        for (int k = 1; k <= Projects.Length; k++)
        {
            int i = ((from < 0 ? Projects.Length - 1 : from) + k) % Projects.Length;
            bool done = Projects[i].Building is Bld b ? cap >= 0 && s.Buildings[cap].Contains(b) : (s.ProjectsDone & (1 << i)) != 0;
            if (!done) return i;
        }
        return -1;
    }

    /// <summary>World-wrapped distance between province anchors in pixels.</summary>
    public static float Distance(WorldData w, int a, int b)
    {
        float dx = Math.Abs(w.PCX[a] - w.PCX[b]);
        if (dx > w.W / 2f) dx = w.W - dx;
        float dy = w.PCY[a] - w.PCY[b];
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
