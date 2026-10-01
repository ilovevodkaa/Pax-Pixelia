using System;

namespace PaxPixelia.Sim;

/// <summary>
/// Sectioned hash of the game state for desync checks: when two clients disagree, the differing section names the
/// system that drifted («Провинции», «Туман»…). Render-only data (Scout.Progress) and session settings (speed, pause,
/// observer view) are left out: they may legitimately differ between clients.
/// </summary>
public readonly record struct StateHash(ulong Time, ulong Nations, ulong Provinces, ulong Buildings, ulong Fog, ulong Scouts)
{
    public ulong All
    {
        get
        {
            var h = new Fnv();
            h.Add(Time); h.Add(Nations); h.Add(Provinces); h.Add(Buildings); h.Add(Fog); h.Add(Scouts);
            return h.Value;
        }
    }

    /// <summary>Names of the sections that differ from another hash (empty when in sync).</summary>
    public string Diff(in StateHash o)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (Time != o.Time) parts.Add("время");
        if (Nations != o.Nations) parts.Add("державы");
        if (Provinces != o.Provinces) parts.Add("провинции");
        if (Buildings != o.Buildings) parts.Add("постройки");
        if (Fog != o.Fog) parts.Add("туман");
        if (Scouts != o.Scouts) parts.Add("разведчики");
        return string.Join(", ", parts);
    }

    public override string ToString() => $"{All:X16}";
}

/// <summary>FNV-1a over 64-bit words: tiny, allocation-free and the same on every machine.</summary>
public struct Fnv
{
    ulong _h;
    public Fnv() { _h = 14695981039346656037UL; }
    public void Add(long v) { _h = (_h ^ (ulong)v) * 1099511628211UL; }
    public void Add(ulong v) { _h = (_h ^ v) * 1099511628211UL; }
    public void Add(bool v) => Add(v ? 1L : 0L);
    public void Add(ReadOnlySpan<byte> v) { foreach (var b in v) Add((long)b); }
    public readonly ulong Value => _h;
}

public sealed partial class GameState
{
    public StateHash Hash()
    {
        var time = new Fnv();
        time.Add(Tick); time.Add(Day256); time.Add(DateTarget); time.Add(DateStep); time.Add(Pace);

        var nat = new Fnv();
        nat.Add(Nat.Length);
        for (int n = 0; n < Nat.Length; n++)
        {
            var x = Nat[n];
            nat.Add((long)x.Control); nat.Add(x.Treasury); nat.Add(x.LastTaxes); nat.Add(x.LastUpkeep);
            nat.Add(x.Progress); nat.Add(x.ScienceRate); nat.Add(x.Era);
            nat.Add(x.ProjectIndex); nat.Add(x.QueuePct); nat.Add(x.ProjectsDone); nat.Add(x.EventCount);
            nat.Add(x.Materials); nat.Add(x.LastMaterials);
            foreach (ulong v in x.TechsDone) nat.Add(v);   // one word per 64 technologies (the hash of a one-word set is the old long's)
            nat.Add(x.Researching); nat.Add(x.TechPool);
            foreach (long v in x.TechPts) nat.Add(v);
            nat.Add(x.Camp); nat.Add(x.CampStep); nat.Add(x.CampSub); nat.Add(x.Supplies); nat.Add(x.TribePop); nat.Add(x.Legends); nat.Add(x.Myth);
            if (!Character.IsBlank(x))   // a blank character hashes as the layouts before it did
            {
                for (int k = 0; k < x.CharA.Length; k++) { nat.Add(x.CharA[k]); nat.Add(x.CharB[k]); nat.Add(x.CharLevel[k]); nat.Add(x.CharHeld[k]); }
                nat.Add(x.CharTraits); nat.Add(x.FirstTechs);
            }
            if (x.Edicts != 0) nat.Add(x.Edicts);   // no edicts hashes as before them (older saves keep their hash)
            nat.Add(x.CampPath?.Length ?? -1);
            if (x.CampPath != null) foreach (int p in x.CampPath) nat.Add(p);
            nat.Add(NationCapital[n]);
            if (Events?.Mem[n] is { } m) { nat.Add(m.Total); nat.Add(m.NextDue); nat.Add(m.NextChoice); nat.Add(m.Pending?.Event ?? -1); }
        }
        foreach (var x in Nat)
            if (!Eurekas.IsBlank(x)) foreach (ulong v in x.Eurekas) nat.Add(v);   // none struck yet hashes as before
        if (!Firsts.IsBlank(this))   // no first taken yet hashes as the layouts before them did
            for (int f = 0; f < FirstHolder.Length; f++) { nat.Add(FirstHolder[f]); nat.Add(FirstCycle[f]); }
        if (!Diplomacy.IsBlank(this))   // no opinion, pact, tribute or pull yet: hashes as before diplomacy
        {
            foreach (sbyte o in Opinion) nat.Add(o);
            foreach (bool b in Pact) nat.Add(b);
            for (int k = 0; k < TributeTo.Length; k++) { nat.Add(TributeTo[k]); nat.Add(DemandFrom[k]); nat.Add(DemandUntil[k]); }
            foreach (byte b in Pull) nat.Add(b);
        }
        foreach (var x in Nat)   // a nation without a ruler or dogmas yet (an older save) hashes as before them
            if (x.Rulers != 0 || x.Dogmas != 0)
            {
                nat.Add(x.Rulers); nat.Add(x.RulerSeed); nat.Add(x.RulerNumeral); nat.Add(x.RulerStart);
                nat.Add(x.RulerAge0); nat.Add(x.RulerLife); nat.Add(x.RulerTraits); nat.Add(x.Dogmas);
            }
        if (!Wonders.IsBlank(this))  // no glory, no wonder: hashes as before wonders
        {
            foreach (var x in Nat) { nat.Add(x.Glory); nat.Add(x.Wonder); nat.Add(x.WonderGold); nat.Add(x.WonderMats); }
            for (int k = 0; k < WonderOwner.Length; k++) { nat.Add(WonderOwner[k]); nat.Add(WonderFlag[k]); }
        }
        if (RuinsDug != null && Array.Exists(RuinsDug, v => v != 0)) { nat.Add(-14L); foreach (ulong v in RuinsDug) nat.Add(v); }   // nothing dug hashes as before
        foreach (var x in Nat)   // no challenge yet hashes as before challenges
            if (x.ChallengeKind >= 0 || x.ChallengesWon > 0) { nat.Add(-12L); nat.Add(x.ChallengeKind); nat.Add(x.ChallengeGoal); nat.Add(x.ChallengeEnd); nat.Add(x.ChallengesWon); }

        var prov = new Fnv(); var bld = new Fnv();
        for (int p = 0; p < Owner.Length; p++)
        {
            prov.Add(Owner[p]); prov.Add(Controller[p]); prov.Add(Pop[p]); prov.Add(Religion[p]); prov.Add(Mood[p]);
            prov.Add(CapitalOf[p]); prov.Add(IsTown[p]);
            if (Unrest != null && Unrest[p] != 0) { prov.Add(-7L); prov.Add(Unrest[p]); }   // no rising hashes as before unrest
            if (Plague != null && Plague[p] != 0) { prov.Add(-8L); prov.Add(Plague[p]); }
            if (City != null) { prov.Add(City[p]); prov.Add(Growth[p]); prov.Add(SphereNoted[p]); }
            bld.Add(Slots[p]); bld.Add(Ore[p]); bld.Add(OreFound[p]); bld.Add(Buildings[p].Count);
            foreach (var b in Buildings[p]) bld.Add((long)b);
        }
        if (Builds.Count > 0)   // nothing going up hashes as before construction took time
        {
            bld.Add(-16L);
            foreach (var j in Builds)
            {
                bld.Add(j.Province); bld.Add((long)j.Building); bld.Add(j.Nation); bld.Add(j.Era);
                bld.Add(j.Work); bld.Add(j.Total); bld.Add(j.Gold); bld.Add(j.Materials);
            }
        }

        var fog = new Fnv();
        for (int n = 0; n < Nat.Length; n++)
        {
            var f = Nat[n].Fog;
            if (f == null) { fog.Add(-1L); continue; }
            fog.Add(f.Fog);
            for (int p = 0; p < f.Explored.Length; p++) { fog.Add(f.Explored[p]); fog.Add(f.KnownOwner[p]); }
            foreach (var m in f.Met) fog.Add(m);
        }

        var sc = new Fnv();
        sc.Add(ScoutSeq);
        foreach (var x in Scouts)
        {
            sc.Add(x.Id); sc.Add(x.Nation); sc.Add(x.Step); sc.Add(x.Sub); sc.Add(x.Auto); sc.Add(x.Steps); sc.Add(x.MaxSteps); sc.Add(x.Found);
            foreach (int p in x.Path) sc.Add(p);
        }
        foreach (var rt in Routes) { sc.Add(rt.Length); foreach (int p in rt) sc.Add(p); }

        return new StateHash(time.Value, nat.Value, prov.Value, bld.Value, fog.Value, sc.Value);
    }
}
