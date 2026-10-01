using System;
using System.Collections.Generic;
using PaxPixelia.World;

namespace PaxPixelia.Sim;

/// <summary>One kind of challenge: its name, how it is measured, how far the goal is from where the nation stands.</summary>
public sealed record ChallengeDef(string Name, string Goal, string Done);

/// <summary>
/// «Вызов лидеру» (IDEAS principle 8: the leader gets a choice and glory, never a penalty). Every <see cref="Window"/>
/// rules cycles the world's leader by score, if it has no challenge yet, is set a hard goal measured from where it stands
/// — grow a seventh more people, take four more provinces, learn three technologies, found a town, keep the people
/// content — to meet before the next window. Met: <see cref="Glory"/> glory and a line in every chronicle that has met
/// it. Missed: nothing but silence. Bots take challenges too, so the leaderboard race is open to everybody.
/// State per nation: ChallengeKind (-1 none), ChallengeGoal, ChallengeEnd (cycle), ChallengesWon.
/// </summary>
public static class Challenges
{
    /// <summary>Rules cycles between challenges and the time to meet one: 600 cycles, 5 min at speed 3.</summary>
    public const int Window = 600;
    public const int Glory = 20;

    public static readonly ChallengeDef[] All =
    {
        new("Великое множество", "людей {0}", "Народ стал многолюднее, чем мечтали старейшины"),
        new("Земли за горизонтом", "провинций {0}", "Границы раздвинулись, как и было обещано"),
        new("Свет знаний", "изучено технологий {0}", "Мудрецы исполнили обещанное: знания умножились"),
        new("Новый город", "городов {0}", "На новом месте встал город, и дым его очагов виден издалека"),
        new("Сытые и довольные", "довольство в среднем {0}%", "Народ доволен — летописцы записали это без лести"),
    };

    public const int People = 0, Lands = 1, Knowledge = 2, Towns = 3, Content = 4;

    /// <summary>Where nation n stands on a challenge's measure now.</summary>
    public static long Measure(GameState s, int n, int kind)
    {
        switch (kind)
        {
            case People: { long k = 0; for (int p = 0; p < s.Owner.Length; p++) if (s.Owner[p] == n) k += s.Pop[p]; return k; }
            case Lands: { long k = 0; for (int p = 0; p < s.Owner.Length; p++) if (s.Owner[p] == n) k++; return k; }
            case Knowledge: return Techs.KnownCount(s.Nat[n]);
            case Towns: { long k = 0; for (int p = 0; p < s.Owner.Length; p++) if (s.Owner[p] == n && Cities.IsCity(s, p)) k++; return k; }
            case Content:
            {
                long sum = 0, k = 0;
                for (int p = 0; p < s.Owner.Length; p++) if (s.Owner[p] == n) { sum += s.Mood[p]; k++; }
                return k == 0 ? 0 : sum / k;
            }
            default: return 0;
        }
    }

    /// <summary>The goal of a fresh challenge from where the nation stands.</summary>
    static long GoalFrom(GameState s, int n, int kind)
    {
        long now = Measure(s, n, kind);
        return kind switch
        {
            People => now + now / 7 + 100,
            Lands => now + 4,
            Knowledge => now + 3,
            Towns => now + 1,
            Content => Math.Max(70, Math.Min(90, now + 8)),
            _ => now,
        };
    }

    /// <summary>Can kind be set for nation n now (it must be possible to meet)?</summary>
    static bool Fits(GameState s, int n, int kind) => kind switch
    {
        Knowledge => Techs.HasOpen(s.Nat[n]),
        Towns => s.NationCapital[n] >= 0,
        _ => true,
    };

    public static bool Active(NationState nat) => nat.ChallengeKind >= 0;

    /// <summary>«Великое множество: людей 120 000 (сейчас 104 300)».</summary>
    public static string Text(GameState s, int n)
    {
        var nat = s.Nat[n];
        if (!Active(nat)) return null;
        var d = All[nat.ChallengeKind];
        return $"{d.Name}: {string.Format(d.Goal, Num(nat.ChallengeGoal))} (сейчас {Num(Measure(s, n, nat.ChallengeKind))})";
    }

    static string Num(long v) => v.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');

    /// <summary>Cycles left until the challenge ends.</summary>
    public static int CyclesLeft(GameState s, int n, int cycle) => Active(s.Nat[n]) ? Math.Max(0, s.Nat[n].ChallengeEnd - cycle) : 0;

    // ------------------------------------------------------------------ every rules cycle

    internal static void Cycle(WorldData w, GameState s, int cycle, ISimSink sink)
    {
        // what stands: met or out of time
        for (int n = 0; n < s.Nat.Length; n++)
        {
            var nat = s.Nat[n];
            if (!Active(nat)) continue;
            int kind = nat.ChallengeKind;
            if (Measure(s, n, kind) >= nat.ChallengeGoal)
            {
                nat.Glory += Glory;
                nat.ChallengesWon++;
                Clear(nat);
                if (sink == null) continue;
                if (nat.Human) sink.Notify("trophy", $"Вызов исполнен: «{All[kind].Name}». {All[kind].Done}. Слава +{Glory}");
                else if (Simulation.MetByHumanPublic(s, n)) sink.Notify("trophy", $"{s.Nations[n].Name} исполняет вызов «{All[kind].Name}» и получает славу");
            }
            else if (cycle >= nat.ChallengeEnd)
            {
                Clear(nat);
                if (nat.Human) sink?.Notify("hourglass", $"Вызов «{All[kind].Name}» не исполнен. Летописцы промолчат — до следующего раза");
            }
        }
        // a new window: the leader by score is challenged
        if (cycle == 0 || cycle % Window != 0) return;
        var scores = Rules.Scores(s);
        int leader = -1;
        for (int n = 0; n < s.Nat.Length; n++)
            if (Home(s, n) && (leader < 0 || scores[n] > scores[leader])) leader = n;
        if (leader < 0 || Active(s.Nat[leader])) return;
        var open = new List<int>(All.Length);
        for (int k = 0; k < All.Length; k++) if (Fits(s, leader, k)) open.Add(k);
        if (open.Count == 0) return;
        int pick = open[SimRng.Pick(w.Seed, 181, leader, cycle / Window, open.Count)];
        var lead = s.Nat[leader];
        lead.ChallengeKind = pick;
        lead.ChallengeGoal = GoalFrom(s, leader, pick);
        lead.ChallengeEnd = cycle + Window;
        if (sink == null) return;
        if (lead.Human) sink.Notify("crown", $"Вы первая держава мира, и летописцы ждут большего. Вызов: {Text(s, leader)}. Срок — до следующего вызова");
        else if (Simulation.MetByHumanPublic(s, leader)) sink.Notify("crown", $"{s.Nations[leader].Name} — первая держава мира и принимает вызов «{All[pick].Name}»");
    }

    static bool Home(GameState s, int n) => s.NationCapital[n] >= 0;

    static void Clear(NationState nat) { nat.ChallengeKind = -1; nat.ChallengeGoal = 0; nat.ChallengeEnd = 0; }
}
