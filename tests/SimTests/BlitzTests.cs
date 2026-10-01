using System;
using System.IO;
using System.Linq;
using PaxPixelia.Core;
using PaxPixelia.Core.Nations;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>«Блиц недели»: one setup per ISO week, a whole blitz played through the command queue as the game does, its
/// result file read back, the replay check accepting it — and refusing a changed score, a cut journal, debug commands,
/// another week's rules and another content pack.</summary>
public static class BlitzTests
{
    const int Me = GameState.LocalPlayer;

    public static void Run()
    {
        Section("blitz: the week");
        Check(Blitz.WeekId(new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)) == "2026-40" && Blitz.WeekId(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)) == "2026-53",
            "ISO weeks: 1 Oct 2026 is week 40, 1 Jan 2027 still belongs to week 53 of 2026");
        var design = new NationDesign("test", "Тестия", 200, 60, 40, default, 0, 0);
        var setup = Blitz.WeekSetup("2026-40", design);
        Check(setup == Blitz.WeekSetup("2026-40", design) && setup.Seed != Blitz.WeekSetup("2026-41", design).Seed, $"one seed per week («{setup.SeedText}» → {setup.Seed}), the next week another");
        Check(setup.IsBlitz && setup.BlitzTicks == Blitz.Ticks && setup.NationCount == Blitz.Nations && setup.Nomad, $"{Blitz.Nations} nations, a nomad start, {Blitz.Ticks} ticks (40 min at speed 3)");

        Section("blitz: a whole game through the command queue");
        var w = WorldGen.Generate(setup.Seed, GameStart.WorldWidth, GameStart.WorldHeight);
        Check(GameStart.Create(w, setup, TestContent.Db).Hash().Equals(GameStart.Create(w, setup, TestContent.Db).Hash()), "the first state is the same every time");
        var s = GameStart.Create(w, setup, TestContent.Db);
        var q = new CommandQueue();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (s.Tick < setup.BlitzTicks)
        {
            var nat = s.Nat[Me];
            if (s.Tick % 64 == 0)
            {
                if (nat.Researching < 0)
                    for (int t = 0; t < Techs.Count; t++) if (Techs.Open(nat, t)) { q.Submit(s, Cmd.Research(Me, t)); break; }
                if (Scouts.Free(s, Me) > 0 && s.Nat[Me].Fog != null) q.Submit(s, Cmd.ScoutAuto(Me));
            }
            if (s.Tick % 256 == 128 && nat.Camp < 0)
                for (int p = 0; p < w.P; p++) if (Rules.CheckClaim(w, s, p, Me) == ClaimError.None) { q.Submit(s, Cmd.Claim(Me, p)); break; }
            q.Run(w, s, 1, null);
        }
        var rec = Blitz.Record("2026-40", "test", setup, s, q.Journal, 1234);
        Info($"played {s.Tick} ticks in {sw.ElapsedMilliseconds} ms, {q.Journal.Count} commands, era {s.Nat[Me].Era}, score {rec.Score.Total} ({rec.Score})");
        Check(rec.Score.Total > 0 && rec.EndTick == Blitz.Ticks && s.Nat[Me].Camp < 0, "the tribe settled, the game scored");

        Section("blitz: the result file");
        var ms = new MemoryStream();
        Blitz.Write(ms, rec);
        ms.Position = 0;
        var back = Blitz.Read(ms);
        Check(back.Week == rec.Week && back.Setup == rec.Setup && back.Score == rec.Score && back.StateHash == rec.StateHash && back.Journal.SequenceEqual(rec.Journal)
              && back.NationName == rec.NationName, $"written and read back ({ms.Length} B)");
        Check(Blitz.FileName(back).StartsWith("2026-40_Тестия_") && Blitz.FileName(back).EndsWith(Blitz.Extension), Blitz.FileName(back));
        var bad = ms.ToArray(); bad[^3] ^= 0x40;
        Check(Throws(() => Blitz.Read(new MemoryStream(bad))), "a flipped byte is refused");

        Section("blitz: the replay check");
        sw.Restart();
        var ok = Blitz.Check(back, TestContent.Db, w);
        Check(ok == null, $"the honest result replays to the same hash and score ({sw.ElapsedMilliseconds} ms){(ok == null ? "" : ": " + ok)}");
        Check(Blitz.Check(Copy(back, r => r.Score = r.Score with { Lands = r.Score.Lands + 10 }), TestContent.Db, w) != null, "a raised score is caught");
        Check(Blitz.Check(Copy(back, r => r.Journal.RemoveAt(r.Journal.FindIndex(c => c.Type == CmdType.Research))), TestContent.Db, w) != null, "a journal with a command cut out does not add up");
        Check(Blitz.Check(Copy(back, r => r.Journal.Add(new Cmd(r.Journal[^1].Tick, Me, 9999, CmdType.CheatGold, 1000))), TestContent.Db, w) == "В журнале отладочные команды", "debug commands are refused");
        Check(Blitz.Check(Copy(back, r => r.Journal.Insert(0, new Cmd(0, 3, 1, CmdType.Research, 0))), TestContent.Db, w) == "В журнале чужие приказы", "commands for a bot are refused");
        Check(Blitz.Check(Copy(back, r => r.Setup = r.Setup with { NationCount = 4 }), TestContent.Db, w) == "Правила не совпадают с правилами недели", "an easier setup is not the week's");
        Check(Blitz.Check(back, null, w) == "Сыграно в другой версии событий", "another content pack cannot replay it");
    }

    static BlitzRecord Copy(BlitzRecord r, Action<BlitzRecord> change)
    {
        var ms = new MemoryStream();
        Blitz.Write(ms, r);
        ms.Position = 0;
        var c = Blitz.Read(ms);
        change(c);
        return c;
    }

    static bool Throws(Action a)
    {
        try { a(); return false; }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException) { return true; }
    }
}
