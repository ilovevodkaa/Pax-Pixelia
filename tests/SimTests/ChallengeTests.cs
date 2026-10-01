using System.IO;
using System.Linq;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>«Вызов лидеру»: at every window the leader by score is challenged from where it stands; a met challenge
/// gives glory, a missed one only ends; the challenge lives in the save and the hash.</summary>
public static class ChallengeTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run(WorldData w)
    {
        Section("challenges: the leader is challenged");
        var s = Fresh(w);
        Challenges.Cycle(w, s, Challenges.Window - 1, null);
        Check(s.Nat.All(n => !Challenges.Active(n)), "between windows nobody is challenged");
        s.Nat[Me].Glory = 1000;   // the player leads by far
        Challenges.Cycle(w, s, Challenges.Window, null);
        var me = s.Nat[Me];
        Check(Challenges.Active(me) && s.Nat.Count(Challenges.Active) == 1 && me.ChallengeEnd == 2 * Challenges.Window,
            $"the leader gets one: {Challenges.Text(s, Me)}");
        Check(me.ChallengeGoal > Challenges.Measure(s, Me, me.ChallengeKind), "its goal lies ahead of where it stands");

        Section("challenges: met or missed");
        int glory = me.Glory;
        me.ChallengeGoal = Challenges.Measure(s, Me, me.ChallengeKind);   // reached
        Challenges.Cycle(w, s, Challenges.Window + 1, null);
        Check(!Challenges.Active(me) && me.Glory == glory + Challenges.Glory && me.ChallengesWon == 1, $"met: +{Challenges.Glory} glory, one won");
        Challenges.Cycle(w, s, 2 * Challenges.Window, null);
        Check(Challenges.Active(me), "the next window challenges the leader again");
        me.ChallengeGoal = long.MaxValue / 2;
        glory = me.Glory;
        Challenges.Cycle(w, s, me.ChallengeEnd, null);
        Check(me.ChallengeGoal != long.MaxValue / 2 && me.Glory == glory && me.ChallengesWon == 1, "missed: it just ends, no penalty (the new window sets the next one)");

        Section("challenges: in the save");
        Challenges.Cycle(w, s, 3 * Challenges.Window, null);
        var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) s.WriteSnapshot(bw);
        ms.Position = 0;
        GameState back;
        using (var br = new BinaryReader(ms)) back = GameState.ReadSnapshot(br, w, null);
        Check(back.Hash().Equals(s.Hash()) && back.Nat[Me].ChallengeKind == me.ChallengeKind && back.Nat[Me].ChallengesWon == 1, "an open challenge survives a save");

        Section("challenges: a whole game");
        var g = Fresh(w);
        for (long t = 0; t < (Challenges.Window * 3 + 2) * Clock.CycleTicks; t++) Simulation.Step(w, g, null);
        int got = g.Nat.Sum(n => n.ChallengesWon) + g.Nat.Count(Challenges.Active);
        Check(got >= 1, $"in {Challenges.Window * 3} cycles the leaders took {got} challenges ({g.Nat.Sum(n => n.ChallengesWon)} met)");
    }

    static GameState Fresh(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[Me].Control = NationControl.Human;
        Simulation.Begin(w, s);
        return s;
    }
}
