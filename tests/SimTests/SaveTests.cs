using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using PaxPixelia.Content;
using PaxPixelia.Core;
using PaxPixelia.Core.Save;
using PaxPixelia.Sim;
using PaxPixelia.World;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>
/// Saves (F-4) without Godot: a scripted game is saved to a whole .pxs file at awkward moments — right after a claim at
/// its tick boundary, a scout mid-step, paused, an event choice open — read back onto the same world and played on:
/// the state, the journal and the hash must match an uninterrupted game bit for bit. Then: every field of the state
/// classes is known to the snapshot (a new field fails here until it is saved), the world hash, broken / old / too new
/// files are refused with SaveException only (byte flips, truncation, a fuzzed snapshot behind valid checksums), and
/// the time budgets (save &lt; 300 ms, load &lt; 1.5 s with the world regenerated).
/// </summary>
public static class SaveTests
{
    const int End = 4800;   // 1200 cycles, 10 min at speed 3

    sealed class Play
    {
        public GameState S;
        public CommandQueue Q = new();
        public int Next;
        public readonly Recorder Sink = new();
    }

    public static void Run(WorldData w)
    {
        Section("saves: save → load → play on = the uninterrupted game");
        var script = Script(w);
        var whole = Start(w);
        Drive(w, whole, script, End);
        var wholeHash = whole.S.Hash();
        var wholeSnap = SaveFile.Snapshot(whole.S);
        Info($"uninterrupted: {End} ticks, {whole.Q.Journal.Count} commands, hash {wholeHash}, snapshot {wholeSnap.Length / 1024} KB");

        int choiceTick = FirstTick(w, script, s => s.Events.Pending(0) != null);
        var points = new (string Name, long Tick, Func<GameState, bool> Moment)[]
        {
            ("right after a claim, at its tick boundary", 40, s => s.Owner.Count(o => o == 0) > Fresh(w).Owner.Count(o => o == 0) && s.Tick == 40),
            ("a scout mid-step", 103, s => s.Scouts.Any(x => x.Nation == 0 && x.Sub > 0 && x.Sub < Scouts.SubSteps)),
            ("paused", 700, s => s.Paused),
            ("an event choice open", choiceTick, s => s.Events.Pending(0) != null && s.Nat.Sum(n => n.EventCount) > 0),
            ("the last tick", End, s => true),
        };
        foreach (var (name, tick, moment) in points)
        {
            if (tick < 0) { Check(false, $"{name}: the moment happens in the script"); continue; }
            var live = Start(w);
            Drive(w, live, script, tick);
            Check(moment(live.S), $"{name}: the moment is there at tick {tick}", quietPass: true);
            var file = Save(w, live);
            var hash = live.S.Hash();
            var snap = SaveFile.Snapshot(live.S);
            var loaded = Load(w, file, live.Next, out var header, out var body);
            Check(loaded.S.Hash().Equals(hash) && header.StateHash == hash.All, $"{name}: loaded hash = saved ({hash})");
            Check(SaveFile.Snapshot(loaded.S).AsSpan().SequenceEqual(snap), $"{name}: snapshot bytes equal ({snap.Length} B)", quietPass: true);
            Check(moment(loaded.S), $"{name}: the moment survives the file", quietPass: true);
            Check(loaded.Q.Journal.SequenceEqual(live.Q.Journal) && loaded.Q.Sequences.SequenceEqual(live.Q.Sequences), $"{name}: journal and sequence counters", quietPass: true);
            Check(SaveFile.Snapshot(Load(w, Save(w, loaded), loaded.Next, out _, out _).S).AsSpan().SequenceEqual(snap), $"{name}: saving the loaded game again gives the same state", quietPass: true);
            Drive(w, loaded, script, End);
            var h = loaded.S.Hash();
            Check(h.Equals(wholeHash), $"{name}: played on to {End} = uninterrupted{(h.Equals(wholeHash) ? "" : " · differs in: " + h.Diff(wholeHash))}");
            Check(SaveFile.Snapshot(loaded.S).AsSpan().SequenceEqual(wholeSnap) && loaded.Q.Journal.SequenceEqual(whole.Q.Journal),
                $"{name}: … bit for bit, journal included", quietPass: true);
        }

        HeaderAndView(w);
        FieldAudit();
        WorldHashes(w);
        Refusals(w);
        OlderLayout(w);
        Timings(w);
    }

    /// <summary>A snapshot of the previous layout (Fixtures/state_v4.bin.gz: seed 1337, tick 4007, written by the v4 build)
    /// still loads: it reads, keeps its hash, passes the load check and comes back in the current layout.</summary>
    static void OlderLayout(WorldData w)
    {
        Section("saves: a snapshot of the previous layout (v4) still loads");
        const ulong V4Hash = 0xEBCCB9BAE5ADF10F;
        if (w.Seed != 1337) { Info("skipped: the v4 fixture is of seed 1337"); return; }
        var path = Path.Combine(LintTests.GameDir(), "..", "tests", "SimTests", "Fixtures", "state_v4.bin.gz");
        byte[] old;
        using (var f = File.OpenRead(path))
        using (var z = new System.IO.Compression.GZipStream(f, System.IO.Compression.CompressionMode.Decompress))
        using (var ms = new MemoryStream()) { z.CopyTo(ms); old = ms.ToArray(); }
        Check(BitConverter.ToInt32(old, 0) == 4 && GameState.SnapshotVersion > 4, $"the fixture is a v4 snapshot ({old.Length} B), the build writes v{GameState.SnapshotVersion}");
        var s = SaveFile.Restore(old, w, TestContent.Db, 100);
        Check(s.Tick == 4007 && s.Hash().All == V4Hash, $"it reads with the hash it was saved with ({s.Hash()})");
        var header = new SaveHeader { StateHash = V4Hash, ContentHash = SaveFile.ContentSignature(TestContent.Db), SnapshotVersion = 4 };
        Check(SaveFile.Consistent(header, old, s), "the load check accepts it (the rewritten snapshot is v5, not byte-equal)");
        var now = SaveFile.Snapshot(s);
        Check(!SaveFile.Consistent(new SaveHeader { StateHash = V4Hash ^ 1, ContentHash = header.ContentHash }, now, s), "a current-layout snapshot with a wrong hash is still refused");
        Check(Techs.Known(s.Nat[0], Techs.Root) && Techs.KnownCount(s.Nat[0]) >= 1 && s.Nat[0].TechsDone.Length == Techs.Words, "its technologies come back as words");
        for (int t = 0; t < 400; t++) Simulation.Step(w, s, null);
        var again = SaveFile.Restore(SaveFile.Snapshot(s), w, TestContent.Db, 100);
        Check(again.Hash().Equals(s.Hash()), "played on and saved in the current layout, it round-trips");
    }

    // ------------------------------------------------------------------ the scripted game

    static GameState Fresh(WorldData w)
    {
        var s = NationGen.CreateInitialState(w);
        s.Nat[0].Control = NationControl.Human;
        Simulation.Begin(w, s);
        s.Events = new SimEvents(TestContent.Db, w, s);
        return s;
    }

    static Play Start(WorldData w) => new() { S = Fresh(w) };

    static List<(long at, Func<GameState, Cmd> make)> Script(WorldData w)
    {
        var list = new List<(long, Func<GameState, Cmd>)>
        {
            (0, s => Cmd.ScoutAuto(0)),
            (0, s => Cmd.SetSpeed(0, 5)),
            (40, s => ClaimBest(w, s)),
            (40, s => ClaimBest(w, s)),
            (41, s => Cmd.ScoutAuto(0)),
            (60, s => Cmd.Research(0, Techs.Index("hunting"))),   // a study under way at the save points
            (299, s => Cmd.CheatTech(0, Techs.SurveyTech)),
            (299, s => Cmd.CheatTech(0, 4)),
            (300, s => BuildFirst(w, s)),
            (301, s => Cmd.Survey(0, Array.FindIndex(s.Owner, o => o == 0))),
            (500, s => Cmd.Pause(0)),
            (900, s => Cmd.Unpause(0)),
            (1200, s => ClaimBest(w, s)),
            (1500, s => Cmd.ScoutAuto(0)),
            (2600, s => Cmd.CheatEra(0, 1)),
            (3001, s => BuildFirst(w, s)),
        };
        // the player answers open choices now and then (the rest run into their deadline)
        for (long t = 1000; t < End; t += 450) list.Add((t, s => s.Events.Pending(0) != null ? Cmd.Choose(0, 0) : default));
        list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return list;
    }

    /// <summary>Play the script to tick `until`; the commands due at `until` run too (a save is taken at the boundary).</summary>
    static void Drive(WorldData w, Play r, List<(long at, Func<GameState, Cmd> make)> script, long until)
    {
        var s = r.S;
        while (true)
        {
            while (r.Next < script.Count && script[r.Next].at <= s.Tick)
            {
                var c = script[r.Next++].make(s);
                if (c.Type != CmdType.None) { r.Q.Submit(s, c); r.Q.Flush(w, s, r.Sink); }
            }
            if (s.Tick >= until) break;
            int n = (int)Math.Min(7, until - s.Tick);
            if (r.Next < script.Count) n = (int)Math.Min(n, Math.Max(1, script[r.Next].at - s.Tick));
            r.Q.Run(w, s, n, r.Sink);
        }
    }

    static int FirstTick(WorldData w, List<(long at, Func<GameState, Cmd> make)> script, Func<GameState, bool> when)
    {
        var r = Start(w);
        for (int t = 0; t <= End; t++)
        {
            Drive(w, r, script, t);
            if (when(r.S)) return t;
        }
        return -1;
    }

    static Cmd ClaimBest(WorldData w, GameState s)
    {
        var fert = WorldFacts.Of(w).FertPm;
        int best = -1;
        for (int p = 0; p < w.P; p++)
            if (Rules.CheckClaim(w, s, p, 0) == ClaimError.None && (best < 0 || fert[p] > fert[best])) best = p;
        return best < 0 ? default : Cmd.Claim(0, best);
    }

    static Cmd BuildFirst(WorldData w, GameState s)
    {
        for (int p = 0; p < w.P; p++)
            if (s.Owner[p] == 0 && Rules.BuildOptions(w, s, p, 0) is { Count: > 0 } o) return Cmd.Build(0, p, o[0]);
        return default;
    }

    // ------------------------------------------------------------------ files

    static SaveHeader Header(WorldData w, GameState s) => new()
    {
        GameVersion = "test", Kind = SaveKind.Manual, Name = "Проверка", SavedUnixMs = 1_790_000_000_000, PlaytimeMs = 7_654_321,
        Seed = w.Seed, SeedText = "слово", NationCount = s.Nat.Length, Fog = true, PacePermille = s.Pace, JokePercent = 100,
        Player = new Core.Nations.NationDesign("id-1", "Мой народ", 10, 20, 30, Core.Flags.FlagSpec.Default, 2, 123),
        DateText = Calendar.Text(s.Date, false), Year = s.Year, Era = s.Nat[0].Era, NationName = s.Nations[0].Name,
        R = s.Nations[0].R, G = s.Nations[0].G, B = s.Nations[0].B, Flag = s.Nations[0].Flag,
        Tick = s.Tick, WorldW = w.W, WorldH = w.H, Provinces = w.P, WorldHash = WorldHash.Of(w), StateHash = s.Hash().All,
        ContentHash = SaveFile.ContentSignature(s.Events?.Db),
    };

    static byte[] Save(WorldData w, Play r)
    {
        var body = new SaveBody
        {
            State = SaveFile.Snapshot(r.S), Journal = r.Q.Journal.ToList(),
            View = new SaveView { HasCamera = true, CamX = 1234.5f, CamY = 678.25f, Zoom = 4, Mode = 2, Selected = 77, MapMemory = new byte[] { 1, 2, 3, 250 } },
        };
        r.Q.Sequences.CopyTo(body.Seq);
        using var ms = new MemoryStream();
        SaveFile.Write(ms, Header(w, r.S), body);
        return ms.ToArray();
    }

    static Play Load(WorldData w, byte[] file, int next, out SaveHeader header, out SaveBody body)
    {
        (header, body) = SaveFile.Read(new MemoryStream(file));
        var r = new Play { S = SaveFile.Restore(body.State, w, TestContent.Db, header.JokePercent), Next = next };
        r.Q.Restore(body.Journal, body.Seq);
        return r;
    }

    static void HeaderAndView(WorldData w)
    {
        Section("saves: header and view");
        var r = Start(w);
        Drive(w, r, Script(w), 64);
        var file = Save(w, r);
        var h = SaveFile.ReadHeader(new MemoryStream(file));
        var want = Header(w, r.S);
        Check(h.Name == want.Name && h.Kind == SaveKind.Manual && h.Seed == w.Seed && h.SeedText == "слово" && h.PlaytimeMs == want.PlaytimeMs
              && h.SavedUnixMs == want.SavedUnixMs && h.NationName == want.NationName && h.DateText == want.DateText && h.Era == want.Era
              && h.Tick == 64 && h.Provinces == w.P && h.WorldHash == want.WorldHash && h.Player == want.Player && h.Flag == want.Flag,
            $"header round trip: «{h.Name}» {h.NationName} · {h.DateText} · зерно «{h.SeedText}» · design «{h.Player?.Name}»");
        var setup = h.ToSetup();
        Check(setup.Seed == w.Seed && setup.NationCount == r.S.Nat.Length && setup.StartPaused && setup.Player == want.Player && setup.PacePermille == r.S.Pace,
            "the header gives the setup (a loaded game starts paused)");
        var (_, body) = SaveFile.Read(new MemoryStream(file));
        var v = body.View;
        Check(v.HasCamera && v.CamX == 1234.5f && v.CamY == 678.25f && v.Zoom == 4 && v.Mode == 2 && v.Selected == 77 && v.MapMemory.SequenceEqual(new byte[] { 1, 2, 3, 250 }),
            "view round trip: camera, zoom, map mode, selection, the map's memory");
        Check(file.Length < 256 * 1024, $"file size {file.Length / 1024} KB (deflate)");
    }

    // ------------------------------------------------------------------ the state classes are all known to the snapshot

    static void FieldAudit()
    {
        Section("saves: field audit (a new state field must be saved: GameState.Save.cs)");
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var known = new (Type Type, string[] Saved, string[] Derived)[]
        {
            (typeof(GameState), new[] { "Tick", "Day256", "DateTarget", "DateStep", "Pace", "Speed", "Paused", "Nations", "Nat", "NationCapital",
                "Owner", "Controller", "Pop", "Religion", "Mood", "Slots", "Buildings", "Ore", "OreFound", "CapitalOf", "IsTown", "Unrest", "Plague", "Pull", "City", "Growth", "SphereNoted", "Routes",
                "FogEnabled", "Scouts", "ScoutSeq", "Events", "WonderOwner", "WonderFlag", "Opinion", "Pact", "TributeTo", "DemandFrom", "DemandUntil", "FirstHolder", "FirstCycle" }, new[] { "Scratch" }),
            (typeof(NationState), new[] { "Control", "Treasury", "LastTaxes", "LastUpkeep", "Progress", "ScienceRate", "Era", "ProjectIndex",
                "QueuePct", "ProjectsDone", "EventCount", "Materials", "LastMaterials", "TechsDone", "Researching", "TechPts", "TechPool", "Camp", "CampPath", "CampStep", "CampSub", "Supplies", "TribePop", "Legends", "Myth", "Edicts", "Fog",
                "CharA", "CharB", "CharLevel", "CharHeld", "CharTraits", "FirstTechs", "Glory", "Wonder", "WonderGold", "WonderMats", "Eurekas", "ChallengeKind", "ChallengeGoal", "ChallengeEnd", "ChallengesWon" }, new[] { "CharFx", "WonderFx", "Firsts" }),
            (typeof(NationFog), new[] { "Fog", "Explored", "KnownOwner", "Met" }, Array.Empty<string>()),
            (typeof(GameState.Scout), new[] { "Id", "Nation", "Path", "Step", "Sub", "Auto", "Steps", "MaxSteps", "Found" }, new[] { "Progress" }),
            (typeof(EventMemory), new[] { "Nation", "Bot", "NextDue", "NextChoice", "ReadyAt", "FiredCount", "Flags", "ThreadEra", "GuaranteedThreads",
                "JokeStreak", "Total", "FlagsDirty", "Pending", "Queue" }, Array.Empty<string>()),
            // the deck itself: rebuilt from the content pack, the world seed and the setup's «Летописец»
            (typeof(SimEvents), Array.Empty<string>(), new[] { "Runner", "Mem", "_views", "_fx" }),
            (typeof(EventRunner), Array.Empty<string>(), new[] { "_db", "_deck", "_seed", "_weight", "_province", "_foreign",
                "<TicksPerMinuteAtSpeed3>k__BackingField", "<JokePercent>k__BackingField" }),
        };
        foreach (var (type, saved, derived) in known)
        {
            var fields = type.GetFields(all).Select(f => f.Name).ToList();
            var unknown = fields.Except(saved).Except(derived).ToList();
            var gone = saved.Concat(derived).Except(fields).ToList();
            Check(unknown.Count == 0 && gone.Count == 0, $"{type.Name}: {fields.Count} fields, all saved or derived" +
                (unknown.Count > 0 ? $" · NEW: {string.Join(", ", unknown)}" : "") + (gone.Count > 0 ? $" · gone: {string.Join(", ", gone)}" : ""));
        }
        Check(GameState.SnapshotVersion >= 1 && SaveFile.FormatVersion >= SaveFile.MinFormat, $"versions: file {SaveFile.FormatVersion} (reads {SaveFile.MinFormat}..), snapshot {GameState.SnapshotVersion}");
    }

    // ------------------------------------------------------------------ the world

    static void WorldHashes(WorldData w)
    {
        Section("saves: the world is regenerated from its seed and checked by its hash");
        var sw = Stopwatch.StartNew();
        ulong a = WorldHash.Of(w);
        long hashMs = sw.ElapsedMilliseconds;
        var again = WorldGen.Generate(w.Seed, w.W, w.H);
        var other = WorldGen.Generate(w.Seed + 1, w.W, w.H);
        Check(WorldHash.Of(again) == a, $"same seed → same world hash ({a:X16}, {hashMs} ms)");
        Check(WorldHash.Of(other) != a, "another seed → another hash");
    }

    // ------------------------------------------------------------------ bad files

    static void Refusals(WorldData w)
    {
        Section("saves: broken, old and too new files are refused (SaveException only)");
        var r = Start(w);
        Drive(w, r, Script(w), 1500);
        var file = Save(w, r);
        var (goodHeader, goodBody) = SaveFile.Read(new MemoryStream(file));

        SaveError? Try(byte[] bytes, bool restore = false)
        {
            try
            {
                var (h, b) = SaveFile.Read(new MemoryStream(bytes));
                if (restore) SaveFile.Restore(b.State, w, TestContent.Db, h.JokePercent);
                return null;
            }
            catch (SaveException e) { return e.Code; }
        }

        // single byte flips over the whole file (the reserved u16 after the format may change)
        int flips = 0, refused = 0; var other = new List<string>();
        for (int i = 0; i < file.Length; i += Math.Max(1, file.Length / 600))
        {
            if (i is 6 or 7) continue;
            var b = (byte[])file.Clone();
            b[i] ^= 0xA5;
            flips++;
            try { if (Try(b) != null) refused++; else other.Add($"@{i}/{file.Length} read"); }
            catch (Exception e) { other.Add($"@{i} {e.GetType().Name}"); }
        }
        Check(refused == flips, $"{flips} byte flips → {refused} SaveException{(other.Count > 0 ? " · " + string.Join(", ", other.Take(5)) : "")}");

        int cuts = 0, cutRefused = 0; other.Clear();
        for (int len = 0; len < file.Length; len += Math.Max(1, file.Length / 300))
        {
            cuts++;
            try { if (Try(file[..len]) != null) cutRefused++; else other.Add($"{len} read"); }
            catch (Exception e) { other.Add($"{len} {e.GetType().Name}"); }
        }
        Check(cutRefused == cuts, $"{cuts} truncations → {cutRefused} SaveException{(other.Count > 0 ? " · " + string.Join(", ", other.Take(5)) : "")}");

        var magic = (byte[])file.Clone(); magic[0] = (byte)'Z';
        Check(Try(magic) == SaveError.NotASave, "wrong magic → NotASave");
        Check(Try(System.Text.Encoding.UTF8.GetBytes("hello")) == SaveError.NotASave && Try(Array.Empty<byte>()) == SaveError.NotASave, "text / empty file → NotASave");
        var newer = (byte[])file.Clone(); newer[4] = (byte)(SaveFile.FormatVersion + 1); newer[5] = 0;
        Check(Try(newer) == SaveError.TooNew, "a newer file format → TooNew");
        var older = (byte[])file.Clone(); older[4] = (byte)(SaveFile.MinFormat - 1); older[5] = 0;
        Check(Try(older) == SaveError.TooOld, "an older file format → TooOld");
        goodHeader.SnapshotVersion = GameState.SnapshotVersion + 1;
        Check(Try(Rewrap(goodHeader, goodBody)) == SaveError.TooNew, "a newer state layout → TooNew");
        goodHeader.SnapshotVersion = GameState.SnapshotVersion;

        // a damaged snapshot behind valid checksums (an edited file): Restore refuses it, or the game's check after
        // restoring (the state hash, then the snapshot written again) does, or the flip changed nothing
        int fuzz = 0, fuzzRefused = 0, hashCaught = 0, same = 0, silent = 0; other.Clear();
        var state = goodBody.State;
        for (int i = 0; i < state.Length; i += Math.Max(1, state.Length / 400))
        {
            fuzz++;
            var body = new SaveBody { State = (byte[])state.Clone(), Journal = goodBody.Journal, Seq = goodBody.Seq, View = goodBody.View };
            body.State[i] ^= 0x5A;
            try
            {
                var s = SaveFile.Restore(body.State, w, TestContent.Db, 100);
                bool sameBytes = SaveFile.Snapshot(s).AsSpan().SequenceEqual(state);
                if (s.Hash().All != goodHeader.StateHash) hashCaught++;
                else if (!sameBytes) silent++;   // e.g. a deck cooldown: only the snapshot written again tells
                else same++;                     // e.g. a bool byte that was already true
            }
            catch (SaveException) { fuzzRefused++; }
            catch (Exception e) { other.Add($"@{i} {e.GetType().Name}: {e.Message}"); }
        }
        Check(other.Count == 0, $"{fuzz} snapshot flips: {fuzzRefused} refused by the reader, {hashCaught} by the state hash, {silent} by the snapshot check, {same} change nothing" +
            (other.Count > 0 ? " · " + string.Join(" | ", other.Take(4)) : ""));
        Check(Try(Rewrap(goodHeader, new SaveBody { State = new byte[] { 1, 2, 3 } }), restore: true) == SaveError.Corrupt, "a state of garbage → Corrupt");
        Check(Try(Rewrap(goodHeader, new SaveBody { State = Array.Empty<byte>() }), restore: true) == SaveError.Corrupt, "an empty state → Corrupt");
        var small = WorldGen.Generate(7, 640, 360);
        Check(RestoreOn(small, state) == SaveError.Corrupt, "a state on another world → Corrupt (province count)");
        Check(RestoreWithout(w, state), "a state read without the content pack: no deck, provinces intact");
    }

    static SaveError? RestoreOn(WorldData w, byte[] state)
    {
        try { SaveFile.Restore(state, w, TestContent.Db, 100); return null; }
        catch (SaveException e) { return e.Code; }
    }

    static bool RestoreWithout(WorldData w, byte[] state)
    {
        var withPack = SaveFile.Restore(state, w, TestContent.Db, 100);
        var without = SaveFile.Restore(state, w, null, 100);
        var a = withPack.Hash(); var b = without.Hash();
        return without.Events == null && a.Provinces == b.Provinces && a.Fog == b.Fog && a.Scouts == b.Scouts && a.Time == b.Time;
    }

    static byte[] Rewrap(SaveHeader h, SaveBody b)
    {
        using var ms = new MemoryStream();
        SaveFile.Write(ms, h, b);
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ time

    static void Timings(WorldData w)
    {
        Section("saves: time budgets");
        var r = Start(w);
        Drive(w, r, Script(w), End);
        for (int i = 0; i < 3; i++) Save(w, r);   // warm up the JIT
        var sw = Stopwatch.StartNew();
        const int N = 20;
        byte[] file = null;
        for (int i = 0; i < N; i++) file = Save(w, r);
        double saveMs = sw.Elapsed.TotalMilliseconds / N;
        sw.Restart();
        for (int i = 0; i < N; i++) Load(w, file, 0, out _, out _);
        double loadMs = sw.Elapsed.TotalMilliseconds / N;
        sw.Restart();
        var world = WorldGen.Generate(w.Seed, w.W, w.H);
        bool same = WorldHash.Of(world) == WorldHash.Of(w);
        double genMs = sw.Elapsed.TotalMilliseconds;
        Check(saveMs < 300, $"save (snapshot + journal + deflate + checksums): {saveMs:F1} ms, {file.Length / 1024} KB");
        Check(loadMs + genMs < 1500 && same, $"load: world {genMs:F0} ms + hash check + file {loadMs:F1} ms = {genMs + loadMs:F0} ms");
    }
}
