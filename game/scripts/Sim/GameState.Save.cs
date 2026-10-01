using System;
using System.Collections.Generic;
using System.IO;
using PaxPixelia.Content;
using PaxPixelia.Core;
using PaxPixelia.Core.Flags;
using PaxPixelia.World;
using Bld = PaxPixelia.Core.Data.Bld;

namespace PaxPixelia.Sim;

/// <summary>
/// Explicit field-by-field binary snapshot of the game state for saves (Core/Save/SaveFile). Everything the rules
/// read is written — time, the roster, per-nation state and fog, provinces and buildings, routes, scouts and the event
/// deck's memories — so a loaded state goes on bit for bit (SimTests «save → load» compares GameState.Hash with an
/// uninterrupted game). Left out: the render-only Scout.Progress and the scratch buffers (rebuilt on demand).
/// The event memories are keyed by content ids (events, flags, threads), so a save survives a content pack that adds,
/// drops or reorders events. The world is not in here: it is regenerated from its seed and checked by its hash.
/// Adding a field to GameState, NationState, NationFog or Scout? Write and read it here and bump
/// <see cref="SnapshotVersion"/>; the SimTests field audit fails until the field is listed as saved.
/// </summary>
public sealed partial class GameState
{
    /// <summary>Layout version of the snapshot (the save file carries it; older layouts are read field by field).</summary>
    public const int SnapshotVersion = 11;   // 2: materials · 3: technologies · 4: the nomad phase · 5: techs past 64 · 6: character · 7: edicts · 8: world firsts · 9: wonders and glory · 10: eurekas · 11: unrest

    const int MaxNations = 255;

    public void WriteSnapshot(BinaryWriter w)
    {
        w.Write(SnapshotVersion);
        // ---- time and session ----
        w.Write(Tick); w.Write(Day256); w.Write(DateTarget); w.Write(DateStep);
        w.Write(Pace); w.Write(Speed); w.Write(Paused); w.Write(FogEnabled);

        // ---- roster ----
        int nN = Nat.Length;
        w.Write(nN);
        for (int n = 0; n < nN; n++) WriteNation(w, Nations[n]);
        for (int n = 0; n < nN; n++) w.Write(NationCapital[n]);

        // ---- per nation ----
        int P = Owner.Length;
        w.Write(P);
        for (int n = 0; n < nN; n++)
        {
            var x = Nat[n];
            w.Write((byte)x.Control);
            w.Write(x.Treasury); w.Write(x.LastTaxes); w.Write(x.LastUpkeep);
            w.Write(x.Progress); w.Write(x.ScienceRate); w.Write(x.Era);
            w.Write(x.ProjectIndex); w.Write(x.QueuePct); w.Write(x.ProjectsDone); w.Write(x.EventCount);
            w.Write(x.Materials); w.Write(x.LastMaterials);
            w.Write(x.TechsDone.Length);
            foreach (ulong v in x.TechsDone) w.Write(v);
            w.Write(x.Researching); w.Write(x.TechPool);
            w.Write(x.TechPts.Length);
            foreach (long v in x.TechPts) w.Write(v);
            w.Write(x.Camp); w.Write(x.CampStep); w.Write(x.CampSub); w.Write(x.Supplies); w.Write(x.TribePop); w.Write(x.Legends); w.Write(x.Myth);
            w.Write(x.CampPath != null);
            if (x.CampPath != null) WriteInts(w, x.CampPath);
            w.Write(x.Edicts);
            w.Write(x.Fog != null);
            if (x.Fog == null) continue;
            WriteBytes(w, x.Fog.Fog);
            WriteBools(w, x.Fog.Explored);
            WriteShorts(w, x.Fog.KnownOwner);
            WriteBools(w, x.Fog.Met);
        }

        // ---- per province ----
        WriteShorts(w, Owner); WriteShorts(w, Controller);
        for (int p = 0; p < P; p++) w.Write(Pop[p]);
        for (int p = 0; p < P; p++) w.Write(Religion[p]);
        WriteBytes(w, Mood); WriteBytes(w, Slots);
        for (int p = 0; p < P; p++) w.Write(Ore[p]);
        WriteBools(w, OreFound);
        WriteShorts(w, CapitalOf);
        WriteBools(w, IsTown);
        w.Write(City != null);   // city spheres (Cities.Init allocates them in Simulation.Begin)
        if (City != null)
        {
            for (int p = 0; p < P; p++) w.Write(City[p]);
            for (int p = 0; p < P; p++) w.Write(Growth[p]);
            WriteBools(w, SphereNoted);
        }
        for (int p = 0; p < P; p++)
        {
            var list = Buildings[p];
            w.Write((byte)list.Count);
            foreach (var b in list) w.Write((byte)b);
        }

        // ---- routes and scouts ----
        w.Write(Routes.Count);
        foreach (var rt in Routes) WriteInts(w, rt);
        w.Write(ScoutSeq);
        w.Write(Scouts.Count);
        foreach (var sc in Scouts)
        {
            w.Write(sc.Id); w.Write(sc.Nation); w.Write(sc.Step); w.Write(sc.Sub); w.Write(sc.Auto);
            w.Write(sc.Steps); w.Write(sc.MaxSteps); w.Write(sc.Found);
            WriteInts(w, sc.Path);
        }

        // ---- the event deck ----
        WriteEvents(w, Events);

        // ---- the character of each people (6) ----
        w.Write(Character.Count);
        for (int n = 0; n < nN; n++)
        {
            var x = Nat[n];
            if (x.CharA == null) Character.Init(x);
            for (int k = 0; k < Character.Count; k++) { w.Write(x.CharA[k]); w.Write(x.CharB[k]); w.Write(x.CharLevel[k]); w.Write(x.CharHeld[k]); }
            w.Write(x.CharTraits); w.Write(x.FirstTechs);
        }

        // ---- world firsts (8) ----
        Sim.Firsts.Init(this);
        w.Write(FirstHolder.Length);
        for (int f = 0; f < FirstHolder.Length; f++) { w.Write(FirstHolder[f]); w.Write(FirstCycle[f]); }

        // ---- wonders and glory (9) ----
        Sim.Wonders.Init(this);
        foreach (var x in Nat) { w.Write(x.Glory); w.Write(x.Wonder); w.Write(x.WonderGold); w.Write(x.WonderMats); }
        w.Write(WonderOwner.Length);
        for (int k = 0; k < WonderOwner.Length; k++) { w.Write(WonderOwner[k]); w.Write(WonderFlag[k]); }

        // ---- eurekas (10) ----
        foreach (var x in Nat)
        {
            Sim.Eurekas.Init(x);
            w.Write(x.Eurekas.Length);
            foreach (ulong v in x.Eurekas) w.Write(v);
        }

        // ---- unrest (11) ----
        WriteBytes(w, Unrest ?? new byte[P]);
        WriteBytes(w, Plague ?? new byte[P]);
    }

    /// <summary>
    /// Rebuilds a state from <see cref="WriteSnapshot"/> on its (regenerated) world; the event deck is re-attached from
    /// <paramref name="content"/> (null = no deck, as in tests without content). Throws InvalidDataException when
    /// the data does not fit the world or holds impossible values.
    /// </summary>
    public static GameState ReadSnapshot(BinaryReader r, WorldData world, ContentDb content, int jokePercent = 100)
    {
        int version = r.ReadInt32();
        if (version < 1 || version > SnapshotVersion) throw new InvalidDataException($"snapshot version {version}");
        var s = new GameState();
        s.Tick = r.ReadInt64(); s.Day256 = r.ReadInt64(); s.DateTarget = r.ReadInt64(); s.DateStep = r.ReadInt64();
        s.Pace = r.ReadInt32(); s.Speed = r.ReadInt32(); s.Paused = r.ReadBoolean(); s.FogEnabled = r.ReadBoolean();
        Require(s.Tick >= 0 && s.Pace == Eras.ClampPace(s.Pace) && s.Speed >= Clock.MinSpeed && s.Speed <= Clock.MaxSpeed && s.DateStep >= 0, "time");

        int nN = r.ReadInt32();
        Require(nN >= 1 && nN <= MaxNations, "nation count");
        s.Nations = new Data.Nation[nN];
        for (int n = 0; n < nN; n++) s.Nations[n] = ReadNation(r);
        s.NationCapital = new int[nN];
        int P = world.P;
        for (int n = 0; n < nN; n++) { s.NationCapital[n] = r.ReadInt32(); Require(s.NationCapital[n] >= -1 && s.NationCapital[n] < P, "capital"); }

        Require(r.ReadInt32() == P, "province count differs from the world");
        s.Nat = new NationState[nN];
        for (int n = 0; n < nN; n++)
        {
            var x = s.Nat[n] = new NationState();
            x.Control = (NationControl)r.ReadByte();
            Require(x.Control is NationControl.Bot or NationControl.Human, "control");
            x.Treasury = r.ReadInt64(); x.LastTaxes = r.ReadInt64(); x.LastUpkeep = r.ReadInt64();
            x.Progress = r.ReadInt64(); x.ScienceRate = r.ReadInt32(); x.Era = r.ReadByte();
            x.ProjectIndex = r.ReadInt32(); x.QueuePct = r.ReadInt32(); x.ProjectsDone = r.ReadInt32(); x.EventCount = r.ReadInt32();
            if (version >= 2) { x.Materials = r.ReadInt64(); x.LastMaterials = r.ReadInt32(); }
            else x.Materials = Rules.StartMaterials;   // a v1 save predates materials: start the stock over
            x.TechPts = new long[Techs.Count];
            if (version >= 3)
            {
                x.TechsDone = new ulong[Techs.Words];
                int words = version >= 5 ? r.ReadInt32() : 1;   // before 5: one long
                Require(words >= 0 && words <= 64, "technologies");
                for (int i = 0; i < words; i++) { ulong v = r.ReadUInt64(); if (i < x.TechsDone.Length) x.TechsDone[i] = v; }
                x.Researching = r.ReadInt32(); x.TechPool = r.ReadInt64();
                int k = r.ReadInt32();
                Require(k >= 0 && k <= 4096, "technologies");
                for (int t = 0; t < k; t++) { long v = r.ReadInt64(); if (t < x.TechPts.Length) x.TechPts[t] = v; }   // a longer tree in a newer build: extra ids dropped
                Techs.Trim(x.TechsDone);
                Techs.Set(x, Techs.Root, true);   // «Огонь» is known by everyone (saves from before the root)
                Require(x.Researching >= -1 && x.Researching < Techs.Count && x.TechPool >= 0, "research");
            }
            else { x.TechsDone = Techs.RootOnly(); Techs.GrantBefore(x, x.Era + 1); }   // an older save: everything up to its era counts as known
            if (version >= 4)
            {
                x.Camp = r.ReadInt32(); x.CampStep = r.ReadInt32(); x.CampSub = r.ReadInt32(); x.Supplies = r.ReadInt32();
                x.TribePop = r.ReadInt32(); x.Legends = r.ReadInt32(); x.Myth = r.ReadInt32();
                if (r.ReadBoolean()) x.CampPath = ReadInts(r, P);
                Require(x.Camp >= -1 && x.Camp < P && x.Supplies >= 0 && x.Supplies <= Nomads.StartSupplies && x.TribePop >= 0
                        && x.Myth >= -1 && x.Myth < Nomads.Legends.Length && (x.CampPath == null || x.CampStep >= 0 && x.CampStep < x.CampPath.Length)
                        && x.CampSub >= 0 && x.CampSub < Nomads.StepTicks, "tribe");
            }
            if (version >= 7)
            {
                x.Edicts = r.ReadInt32();
                Require((x.Edicts & ~((1 << Policy.Count) - 1)) == 0, "edicts");
            }
            Require(x.Era <= Eras.Last && x.ProjectIndex >= -1 && x.ProjectIndex < Simulation.Projects.Length, "nation");
            if (!r.ReadBoolean()) continue;
            x.Fog = new NationFog
            {
                Fog = ReadBytes(r, P), Explored = ReadBools(r, P), KnownOwner = ReadShorts(r, P), Met = ReadBools(r, nN),
            };
            foreach (byte v in x.Fog.Fog) Require(v <= 2, "fog");
        }

        s.Owner = ReadShorts(r, P); s.Controller = ReadShorts(r, P);
        s.Pop = new int[P];
        for (int p = 0; p < P; p++) s.Pop[p] = r.ReadInt32();
        s.Religion = new sbyte[P];
        for (int p = 0; p < P; p++) s.Religion[p] = r.ReadSByte();
        s.Mood = ReadBytes(r, P); s.Slots = ReadBytes(r, P);
        s.Ore = new sbyte[P];
        for (int p = 0; p < P; p++) s.Ore[p] = r.ReadSByte();
        s.OreFound = ReadBools(r, P);
        s.CapitalOf = ReadShorts(r, P);
        s.IsTown = ReadBools(r, P);
        if (r.ReadBoolean())
        {
            s.City = new int[P]; s.Growth = new int[P];
            for (int p = 0; p < P; p++) { s.City[p] = r.ReadInt32(); Require(s.City[p] >= -1 && s.City[p] < P, "city"); }
            for (int p = 0; p < P; p++) { s.Growth[p] = r.ReadInt32(); Require(s.Growth[p] >= 0, "city growth"); }
            s.SphereNoted = ReadBools(r, P);
        }
        for (int p = 0; p < P; p++)
            Require(s.Religion[p] >= -1 && s.Religion[p] < Data.Religions.Length && s.Ore[p] >= -1 && s.Ore[p] < Data.Ores.Length
                    && s.Mood[p] <= 100 && s.Pop[p] >= 0, "province values");
        s.Buildings = new List<Bld>[P];
        int bldKinds = Enum.GetValues<Bld>().Length;
        for (int p = 0; p < P; p++)
        {
            int k = r.ReadByte();
            var list = s.Buildings[p] = new List<Bld>(k);
            for (int i = 0; i < k; i++)
            {
                byte b = r.ReadByte();
                Require(b < bldKinds, "building");
                list.Add((Bld)b);
            }
            Require(s.Owner[p] >= -1 && s.Owner[p] < nN && s.Controller[p] >= -1 && s.Controller[p] < nN && s.CapitalOf[p] >= -1 && s.CapitalOf[p] < nN, "province owner");
        }

        int routes = r.ReadInt32();
        Require(routes >= 0 && routes <= 1 << 16, "routes");
        for (int i = 0; i < routes; i++) s.Routes.Add(ReadInts(r, P));
        s.ScoutSeq = r.ReadInt32();
        int scouts = r.ReadInt32();
        Require(scouts >= 0 && scouts <= nN * Sim.Scouts.Max, "scouts");
        for (int i = 0; i < scouts; i++)
        {
            var sc = new Scout
            {
                Id = r.ReadInt32(), Nation = r.ReadInt32(), Step = r.ReadInt32(), Sub = r.ReadInt32(), Auto = r.ReadBoolean(),
                Steps = r.ReadInt32(), MaxSteps = r.ReadInt32(), Found = r.ReadInt32(),
            };
            sc.Path = ReadInts(r, P);
            Require(sc.Nation >= 0 && sc.Nation < nN && sc.Path.Length > 0 && sc.Step >= 0 && sc.Step < sc.Path.Length
                    && sc.Sub >= 0 && sc.Sub <= Sim.Scouts.SubSteps, "scout");
            s.Scouts.Add(sc);
        }

        ReadEvents(r, s, world, content, jokePercent);

        foreach (var x in s.Nat) Character.Init(x);   // before 6: no character yet, deeds start from here
        if (version >= 6)
        {
            int scales = r.ReadInt32();
            Require(scales >= 0 && scales <= 64, "character");
            foreach (var x in s.Nat)
            {
                for (int k = 0; k < scales; k++)
                {
                    int a = r.ReadInt32(), b = r.ReadInt32(); sbyte lvl = r.ReadSByte(); short held = r.ReadInt16();
                    Require(a >= 0 && b >= 0 && lvl >= -2 && lvl <= 2 && held >= 0, "character");
                    if (k >= Character.Count) continue;   // a newer build's extra scale
                    x.CharA[k] = a; x.CharB[k] = b; x.CharLevel[k] = lvl; x.CharHeld[k] = held;
                }
                x.CharTraits = r.ReadInt32(); x.FirstTechs = r.ReadInt32();
                Require(x.FirstTechs >= 0, "character");
                Character.Refresh(x);
            }
        }

        Sim.Firsts.Init(s);
        if (version >= 8)
        {
            int firsts = r.ReadInt32();
            Require(firsts >= 0 && firsts <= 256, "firsts");
            for (int f = 0; f < firsts; f++)
            {
                short h = r.ReadInt16(); int c = r.ReadInt32();
                Require(h >= -1 && h < nN && c >= 0, "firsts");
                if (f < s.FirstHolder.Length) { s.FirstHolder[f] = h; s.FirstCycle[f] = c; }   // a newer build's extra firsts dropped
            }
        }
        Sim.Firsts.Sync(s);

        Sim.Wonders.Init(s);
        if (version >= 9)
        {
            foreach (var x in s.Nat)
            {
                x.Glory = r.ReadInt32(); x.Wonder = r.ReadInt32(); x.WonderGold = r.ReadInt64(); x.WonderMats = r.ReadInt64();
                Require(x.Glory >= 0 && x.Wonder >= -1 && x.WonderGold >= 0 && x.WonderMats >= 0, "wonders");
                if (x.Wonder >= Sim.Wonders.Count) { x.Wonder = -1; x.WonderGold = 0; x.WonderMats = 0; }   // an older build's list: start over
            }
            int wonders = r.ReadInt32();
            Require(wonders >= 0 && wonders <= 1024, "wonders");
            for (int k = 0; k < wonders; k++)
            {
                sbyte o = r.ReadSByte(); byte f = r.ReadByte();
                Require(o >= -1 && o < nN, "wonders");
                if (k < s.WonderOwner.Length) { s.WonderOwner[k] = o; s.WonderFlag[k] = f; }
            }
        }
        Sim.Wonders.Refresh(s);
        foreach (var x in s.Nat) Sim.Eurekas.Init(x);   // before 10: none marked, it loads with its own hash (its land may strike once)
        if (version >= 10)
            foreach (var x in s.Nat)
            {
                int words = r.ReadInt32();
                Require(words >= 0 && words <= 64, "eurekas");
                for (int i = 0; i < words; i++) { ulong v = r.ReadUInt64(); if (i < x.Eurekas.Length) x.Eurekas[i] = v; }
            }

        if (version >= 11)
        {
            s.Unrest = ReadBytes(r, P);
            foreach (byte u in s.Unrest) Require(u <= Sim.Unrest.RevoltAt, "unrest");
            s.Plague = ReadBytes(r, P);
            foreach (byte u in s.Plague) Require(u <= Sim.Unrest.PlagueCycles / Sim.Unrest.PlagueStep + 1 || u >= Sim.Unrest.ImmuneBase, "plague");
        }
        else { s.Unrest = new byte[P]; s.Plague = new byte[P]; }
        return s;
    }

    // ------------------------------------------------------------------ roster

    static void WriteNation(BinaryWriter w, Data.Nation x)
    {
        w.Write(x.Name ?? ""); w.Write(x.Gov ?? ""); w.Write(x.R); w.Write(x.G); w.Write(x.B);
        w.Write(x.CultureAdj ?? ""); w.Write(x.Religion);
        WriteFlag(w, x.Flag);
        w.Write(x.Culture);
    }

    static Data.Nation ReadNation(BinaryReader r)
    {
        string name = r.ReadString(), gov = r.ReadString();
        byte red = r.ReadByte(), green = r.ReadByte(), blue = r.ReadByte();
        string adj = r.ReadString();
        int religion = r.ReadInt32();
        var flag = ReadFlag(r);
        byte culture = r.ReadByte();
        Require(religion >= 0 && religion < Data.Religions.Length, "religion");
        return new Data.Nation(name, gov, red, green, blue, adj, religion, flag, culture);
    }

    /// <summary>A flag as its 8 bytes (FlagSpec is the network form of a flag too).</summary>
    public static void WriteFlag(BinaryWriter w, FlagSpec f)
    {
        w.Write(f.Division); w.Write(f.T1); w.Write(f.T2); w.Write(f.T3);
        w.Write(f.Charge); w.Write(f.Pos); w.Write(f.ChargeTinct); w.Write(f.Reserved);
    }

    public static FlagSpec ReadFlag(BinaryReader r)
    {
        var b = r.ReadBytes(8);
        Require(b.Length == 8, "flag");
        // default (all zero) marks a roster entry without a flag of its own: keep it as it was
        var f = new FlagSpec(b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7]);
        return f == default ? f : f.Sanitized();
    }

    // ------------------------------------------------------------------ event deck

    /// <summary>The deck's memories with id tables in front: indexes are mapped back through the ids on load.</summary>
    static void WriteEvents(BinaryWriter w, SimEvents ev)
    {
        w.Write(ev != null);
        if (ev == null) return;
        var db = ev.Db;
        w.Write(db.Events.Length);
        foreach (var e in db.Events) w.Write(e.Def?.Id ?? "");
        w.Write(db.Flags.Count);
        foreach (var f in db.Flags) w.Write(f ?? "");
        w.Write(db.Threads.Length);
        foreach (var t in db.Threads) w.Write(t ?? "");
        w.Write(ev.Mem.Length);
        foreach (var m in ev.Mem)
        {
            w.Write(m.Nation); w.Write(m.Bot); w.Write(m.NextDue); w.Write(m.NextChoice);
            foreach (long v in m.ReadyAt) w.Write(v);
            foreach (int v in m.FiredCount) w.Write(v);
            WriteBools(w, m.Flags);
            foreach (int v in m.ThreadEra) w.Write(v);
            w.Write(m.GuaranteedThreads);
            w.Write(m.JokeStreak); w.Write(m.Total); w.Write(m.FlagsDirty);
            w.Write(m.Pending.HasValue);
            if (m.Pending is { } p) WriteRecord(w, p);
            w.Write(m.Queue.Count);
            foreach (var q in m.Queue) { w.Write(q.At); w.Write(q.Event); w.Write(q.Province); w.Write(q.Foreign); }
        }
    }

    static void ReadEvents(BinaryReader r, GameState s, WorldData world, ContentDb db, int jokePercent)
    {
        if (!r.ReadBoolean()) { s.Events = null; if (db != null) s.Events = new SimEvents(db, world, s, jokePercent); return; }
        var eventIds = ReadStrings(r);
        var flagIds = ReadStrings(r);
        var threadIds = ReadStrings(r);
        int mems = r.ReadInt32();
        Require(mems == s.Nat.Length, "event memories");
        // the saved pack's indexes → this pack's (−1 = gone)
        int[] ev = null, fl = null, th = null;
        if (db != null)
        {
            ev = new int[eventIds.Length]; fl = new int[flagIds.Length]; th = new int[threadIds.Length];
            for (int i = 0; i < ev.Length; i++) ev[i] = db.EventIndex(eventIds[i]);
            for (int i = 0; i < fl.Length; i++) fl[i] = db.FlagIndex(flagIds[i]);
            for (int i = 0; i < th.Length; i++) th[i] = db.ThreadIndex(threadIds[i]);
            s.Events = new SimEvents(db, world, s, jokePercent);
        }
        for (int n = 0; n < mems; n++)
        {
            int nation = r.ReadInt32(); bool bot = r.ReadBoolean();
            long nextDue = r.ReadInt64(), nextChoice = r.ReadInt64();
            var readyAt = new long[eventIds.Length];
            for (int i = 0; i < readyAt.Length; i++) readyAt[i] = r.ReadInt64();
            var fired = new int[eventIds.Length];
            for (int i = 0; i < fired.Length; i++) fired[i] = r.ReadInt32();
            var flags = ReadBools(r, flagIds.Length);
            var threadEra = new int[threadIds.Length];
            for (int i = 0; i < threadEra.Length; i++) threadEra[i] = r.ReadInt32();
            int guaranteed = r.ReadInt32();
            int jokeStreak = r.ReadInt32(), total = r.ReadInt32();
            bool dirty = r.ReadBoolean();
            EventRecord? pending = r.ReadBoolean() ? ReadRecord(r) : null;
            int queued = r.ReadInt32();
            Require(queued >= 0 && queued <= 1 << 16, "event queue");
            var queue = new EventMemory.Scheduled[queued];
            for (int i = 0; i < queued; i++) queue[i] = new EventMemory.Scheduled(r.ReadInt64(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
            if (s.Events == null) continue;

            var m = s.Events.Mem[n];
            Require(nation == m.Nation, "event memory order");
            m.Bot = bot; m.NextDue = nextDue; m.NextChoice = nextChoice;
            Array.Clear(m.ReadyAt); Array.Clear(m.FiredCount); Array.Clear(m.Flags); Array.Fill(m.ThreadEra, -1);
            for (int i = 0; i < ev.Length; i++)
                if (ev[i] >= 0) { m.ReadyAt[ev[i]] = readyAt[i]; m.FiredCount[ev[i]] = fired[i]; }
            for (int i = 0; i < fl.Length; i++) if (fl[i] >= 0) m.Flags[fl[i]] = flags[i];
            m.GuaranteedThreads = 0;
            for (int i = 0; i < th.Length; i++)
            {
                if (th[i] < 0) continue;
                m.ThreadEra[th[i]] = threadEra[i];
                if (i < 31 && (guaranteed & (1 << i)) != 0 && th[i] < 31) m.GuaranteedThreads |= 1 << th[i];
            }
            m.JokeStreak = jokeStreak; m.Total = total; m.FlagsDirty = dirty;
            m.Pending = pending is { } pr && Map(ev, pr.Event) is int pe and >= 0 ? pr with { Event = pe } : null;
            m.Queue.Clear();
            foreach (var q in queue)
                if (Map(ev, q.Event) is int qe and >= 0) m.Queue.Add(q with { Event = qe });
        }
    }

    static int Map(int[] table, int i) => (uint)i < (uint)table.Length ? table[i] : -1;

    static void WriteRecord(BinaryWriter w, in EventRecord x)
    {
        w.Write(x.Nation); w.Write(x.Tick); w.Write(x.Event); w.Write(x.Option); w.Write(x.Branch);
        w.Write(x.Province); w.Write(x.Foreign); w.Write(x.Era); w.Write(x.Year); w.Write(x.Deadline);
    }

    static EventRecord ReadRecord(BinaryReader r) => new(r.ReadInt32(), r.ReadInt64(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
        r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt64());

    // ------------------------------------------------------------------ arrays

    static void Require(bool ok, string what)
    {
        if (!ok) throw new InvalidDataException("bad save data: " + what);
    }

    static void WriteBytes(BinaryWriter w, byte[] a) { w.Write(a.Length); w.Write(a); }

    static byte[] ReadBytes(BinaryReader r, int expect)
    {
        int n = r.ReadInt32();
        Require(n == expect, "array length");
        var a = r.ReadBytes(n);
        Require(a.Length == n, "truncated");
        return a;
    }

    /// <summary>Bools packed 8 to a byte.</summary>
    static void WriteBools(BinaryWriter w, bool[] a)
    {
        w.Write(a.Length);
        for (int i = 0; i < a.Length; i += 8)
        {
            int b = 0;
            for (int k = 0; k < 8 && i + k < a.Length; k++) if (a[i + k]) b |= 1 << k;
            w.Write((byte)b);
        }
    }

    static bool[] ReadBools(BinaryReader r, int expect)
    {
        int n = r.ReadInt32();
        Require(n == expect, "array length");
        var a = new bool[n];
        for (int i = 0; i < n; i += 8)
        {
            int b = r.ReadByte();
            for (int k = 0; k < 8 && i + k < n; k++) a[i + k] = (b & (1 << k)) != 0;
        }
        return a;
    }

    static void WriteShorts(BinaryWriter w, short[] a) { w.Write(a.Length); foreach (short v in a) w.Write(v); }

    static short[] ReadShorts(BinaryReader r, int expect)
    {
        int n = r.ReadInt32();
        Require(n == expect, "array length");
        var a = new short[n];
        for (int i = 0; i < n; i++) a[i] = r.ReadInt16();
        return a;
    }

    static void WriteInts(BinaryWriter w, int[] a) { w.Write(a.Length); foreach (int v in a) w.Write(v); }

    /// <summary>A province list (route, scout path): every entry must be a province of the world.</summary>
    static int[] ReadInts(BinaryReader r, int provinces)
    {
        int n = r.ReadInt32();
        Require(n >= 0 && n <= provinces, "list length");
        var a = new int[n];
        for (int i = 0; i < n; i++) { a[i] = r.ReadInt32(); Require((uint)a[i] < (uint)provinces, "province id"); }
        return a;
    }

    static string[] ReadStrings(BinaryReader r)
    {
        int n = r.ReadInt32();
        Require(n >= 0 && n <= 1 << 16, "id table");
        var a = new string[n];
        for (int i = 0; i < n; i++) a[i] = r.ReadString();
        return a;
    }
}
