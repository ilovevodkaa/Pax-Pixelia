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
            nat.Add(NationCapital[n]);
            if (Events?.Mem[n] is { } m) { nat.Add(m.Total); nat.Add(m.NextDue); nat.Add(m.NextChoice); nat.Add(m.Pending?.Event ?? -1); }
        }

        var prov = new Fnv(); var bld = new Fnv();
        for (int p = 0; p < Owner.Length; p++)
        {
            prov.Add(Owner[p]); prov.Add(Controller[p]); prov.Add(Pop[p]); prov.Add(Religion[p]); prov.Add(Mood[p]);
            prov.Add(CapitalOf[p]); prov.Add(IsTown[p]);
            bld.Add(Slots[p]); bld.Add(Ore[p]); bld.Add(OreFound[p]); bld.Add(Buildings[p].Count);
            foreach (var b in Buildings[p]) bld.Add((long)b);
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
