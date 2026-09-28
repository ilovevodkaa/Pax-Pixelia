using System;
using System.Collections.Generic;
using PaxPixelia.Core;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Map;

/// <summary>
/// What the local player last saw in each province (towns, capital, buildings, owner's era), so a stale province
/// («Сведения устарели») keeps showing old news instead of live state — the map-side twin of the sim's KnownOwner
/// (which <see cref="GameState.VisibleOwner"/> already applies to ownership). Captured while a province is visible
/// and at the moment it goes stale; visible provinces and observer mode read the live state.
/// </summary>
internal sealed class MapMemory
{
    static readonly Data.Bld[] NoBuildings = Array.Empty<Data.Bld>();

    bool[] _town = Array.Empty<bool>();
    short[] _cap = Array.Empty<short>();
    byte[] _era = Array.Empty<byte>();
    Data.Bld[][] _bld = Array.Empty<Data.Bld[]>();
    GameState _s;

    public void Reset(WorldData w, GameState s)
    {
        _s = s;
        _town = new bool[w.P]; _cap = new short[w.P]; _era = new byte[w.P]; _bld = new Data.Bld[w.P][];
        Array.Fill(_cap, (short)-1);
        Array.Fill(_bld, NoBuildings);
        // the starting map (home and whatever the tribe knows) is known as it is now
        for (int p = 0; p < w.P; p++) if (!s.FogEnabled || s.Fog == null || s.Fog[p] > 0) Capture(p);
    }

    /// <summary>After ProvincesChanged / FogChanged / an era change: refresh what the player can see now (null = all).
    /// Provinces that just went stale are listed by FogChanged too; their state is still the one last seen.</summary>
    public void Capture(IReadOnlyList<int> ps)
    {
        if (_s == null) return;
        if (ps == null) { for (int p = 0; p < _cap.Length; p++) if (Live(p)) Capture(p); return; }
        foreach (int p in ps) if ((uint)p < (uint)_cap.Length && (Live(p) || _s.Fog[p] == 1)) Capture(p);
    }

    void Capture(int p)
    {
        var s = _s;
        _town[p] = s.IsTown[p];
        _cap[p] = s.CapitalOf[p];
        int o = s.Owner[p];
        _era[p] = (byte)MapEra.Of(o >= 0 ? o : Math.Max((int)s.CapitalOf[p], 0));
        var live = s.Buildings[p];
        var old = _bld[p];
        if (live == null || live.Count == 0) { _bld[p] = NoBuildings; return; }
        bool same = old.Length == live.Count;
        for (int i = 0; same && i < old.Length; i++) same = old[i] == live[i];
        if (!same) _bld[p] = live.ToArray();
    }

    bool Live(int p) => !_s.FogEnabled || _s.Fog == null || _s.Fog[p] == 2;

    public bool IsTown(int p) => Live(p) ? _s.IsTown[p] : _town[p];
    public int CapitalOf(int p) => Live(p) ? _s.CapitalOf[p] : _cap[p];
    public bool IsCity(int p) => IsTown(p) || CapitalOf(p) >= 0;

    public IReadOnlyList<Data.Bld> Buildings(int p)
    {
        if (!Live(p)) return _bld[p];
        return (IReadOnlyList<Data.Bld>)_s.Buildings[p] ?? NoBuildings;
    }

    /// <summary>Nation whose colours a city or building in p wears: the owner the map shows (the old one while a capture
    /// fill runs), else the capital's.</summary>
    public int Colours(int p)
    {
        int o = Game.I.ShownOwner(p);
        return o >= 0 ? o : Math.Max(CapitalOf(p), 0);
    }

    public int Era(int p) => Live(p) ? MapEra.Of(Colours(p)) : _era[p];
}
