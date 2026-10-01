using System;
using PaxPixelia.Sim;

namespace PaxPixelia.Core;

public enum UnitSel : byte { None, Scout, Tribe }

/// <summary>A unit picked on the map: a scout party (Id = its Scout.Id) or the nation's own tribe (Id = the nation).</summary>
public readonly record struct UnitRef(UnitSel Kind, int Id)
{
    public bool Any => Kind != UnitSel.None;
}

/// <summary>
/// RTS-style orders: a left click on a unit picks it, a right click on a province sends it there. The pick is view state
/// only (never saved or hashed); orders are the same journaled commands as the panel's buttons (ScoutMove, TribeTo). A
/// party that came home or a tribe that settled drops out of the pick by itself.
/// </summary>
public partial class Game
{
    public UnitRef SelectedUnit { get; private set; }
    /// <summary>The pick changed (map ring, panel rows, cursor).</summary>
    public event Action UnitSelected;

    public void SelectUnit(UnitRef u)
    {
        if (!UnitAlive(u)) u = default;
        if (u == SelectedUnit) return;
        SelectedUnit = u;
        _preview = default;
        UnitSelected?.Invoke();
    }

    public void DeselectUnit() => SelectUnit(default);

    public bool UnitAlive(UnitRef u) => IsReady && u.Kind switch
    {
        UnitSel.Scout => Scouts.Find(State, Viewer, u.Id) != null,
        UnitSel.Tribe => u.Id == Viewer && State.Nat[Viewer].Camp >= 0,
        _ => false,
    };

    /// <summary>The picked scout party, or null.</summary>
    public GameState.Scout SelectedScout => SelectedUnit.Kind == UnitSel.Scout && IsReady ? Scouts.Find(State, Viewer, SelectedUnit.Id) : null;

    /// <summary>Called when parties or the tribe change: a pick that is gone is dropped.</summary>
    void CheckUnit()
    {
        if (SelectedUnit.Any && !UnitAlive(SelectedUnit)) SelectUnit(default);
    }

    void AttachUnits()
    {
        ScoutsChanged += CheckUnit;
        TribeChanged += CheckUnit;
        WorldReady += () => { _preview = default; SelectUnit(default); };
    }

    /// <summary>A right click on p with a unit picked: the party turns there, the tribe walks there (or halts when p is
    /// its own camp). Refusals come as toasts; false when nothing was ordered.</summary>
    public bool OrderUnit(int p)
    {
        if (!IsReady || !SelectedUnit.Any || (uint)p >= (uint)World.P) return false;
        if (OrderLock is { } locked) { ShowRefusal(locked); return false; }
        switch (SelectedUnit.Kind)
        {
            case UnitSel.Scout: return RedirectScout(SelectedUnit.Id, p);
            case UnitSel.Tribe:
                if (p == Camp && Me.CampPath != null) { HaltTribe(); return true; }
                if (Me.CampPath != null && Me.CampPath[^1] == p) return true;   // already on its way there
                return MoveTribe(p);
        }
        return false;
    }

    /// <summary>Why no order can be given at all now (watching a replay, the blitz is over), or null.</summary>
    string OrderLock => IsReplay ? "Это повтор чужой партии: приказы идут из файла" : BlitzOver ? "Блиц окончен: время вышло" : null;

    /// <summary>p is under the clouds: a pick there must not tell land from sea.</summary>
    bool Hidden(int p) => State.FogEnabled && State.Fog[p] == 0;

    /// <summary>Turn party id to p; it finishes the hop under way first.</summary>
    public bool RedirectScout(int id, int p)
    {
        if (!IsReady || (uint)p >= (uint)World.P) return false;
        var err = Scouts.CheckRedirect(World, State, Viewer, id, p, out _);
        if (err == ScoutError.None) err = (ScoutError)Issue(Cmd.ScoutMove(Viewer, id, p));
        if (err != ScoutError.None) { ShowRefusal(RedirectText(err, p)); return false; }
        _preview = default;
        ShowToast(State.Fog[p] != 0 ? $"Разведчики повернули к провинции {World.PName[p]}" : "Разведчики повернули в неизведанные земли");
        ScoutsChanged?.Invoke();
        return true;
    }

    /// <summary>Why the picked unit cannot go to p (Russian), or null when it can. For the hover tip.</summary>
    public string OrderProblem(int p)
    {
        if (!IsReady || !SelectedUnit.Any || (uint)p >= (uint)World.P) return null;
        if (OrderLock is { } locked) return locked;
        if (SelectedUnit.Kind == UnitSel.Scout)
        {
            var e = Scouts.CheckRedirect(World, State, Viewer, SelectedUnit.Id, p, out _);
            if (Hidden(p) && e is ScoutError.Sea or ScoutError.Far) return null;   // under the clouds every pick looks fine
            return e == ScoutError.None ? null : RedirectText(e, p);
        }
        if (p == Camp && Me.CampPath != null) return null;   // halt
        var m = Nomads.CheckMove(World, State, Viewer, p, out _);
        if (Hidden(p) && m is TribeMoveError.Sea or TribeMoveError.Far) m = TribeMoveError.Unexplored;
        return m == TribeMoveError.None ? null : MoveText(m);
    }

    string RedirectText(ScoutError e, int p) => e switch
    {
        // under the clouds the refusal must not reveal whether it is sea or another continent
        ScoutError.Sea or ScoutError.Far when State.FogEnabled && State.Fog[p] == 0 => "Разведчики не нашли туда пути по суше",
        ScoutError.Sea => "Разведчики ходят только по суше",
        ScoutError.Here => "Разведчики уже здесь",
        ScoutError.NoParty => "Этот отряд уже вернулся",
        _ => ScoutText(e),
    };

    // what the picked unit would do on a right click at a province: one BFS per (unit, province, where it stands)
    (UnitRef u, int p, long key, int[] path, bool ok) _preview;

    /// <summary>The path the picked unit would take to p (null: none) and whether the order would be accepted.</summary>
    public int[] PreviewOrder(int p, out bool ok)
    {
        ok = false;
        if (!IsReady || !SelectedUnit.Any || (uint)p >= (uint)World.P || OrderLock != null) return null;
        long key = UnitKey();
        if (_preview.u == SelectedUnit && _preview.p == p && _preview.key == key) { ok = _preview.ok; return _preview.path; }
        int[] path = null;
        switch (SelectedUnit.Kind)
        {
            case UnitSel.Scout: ok = Scouts.CheckRedirect(World, State, Viewer, SelectedUnit.Id, p, out path) == ScoutError.None; break;
            case UnitSel.Tribe:
                if (p == Camp && Me.CampPath != null) { ok = true; path = null; break; }
                ok = Nomads.CheckMove(World, State, Viewer, p, out path) == TribeMoveError.None;
                break;
        }
        // under the clouds: no route, no flag, no cross — a party may try anywhere, the tribe never goes there
        if (Hidden(p) && !(SelectedUnit.Kind == UnitSel.Tribe && p == Camp)) { path = null; ok = SelectedUnit.Kind == UnitSel.Scout; }
        _preview = (SelectedUnit, p, key, path, ok);
        return path;
    }

    /// <summary>Where the picked unit stands (and what it steps to), plus a slow clock for fog and the elders' deadline.</summary>
    long UnitKey()
    {
        long slow = State.Tick >> 4;
        if (SelectedScout is { } sc)
        {
            int cur = Scouts.Current(sc), next = sc.Sub > 0 && sc.Step + 1 < sc.Path.Length ? sc.Path[sc.Step + 1] : -1;
            return ((long)cur << 40) ^ ((long)(next + 1) << 20) ^ slow;
        }
        var me = Me;
        return ((long)Camp << 40) ^ ((me.CampPath != null ? 1L : 0L) << 39) ^ ((me.CampPath?[^1] ?? -1) + 1L << 20) ^ ((me.CampSub > 0 ? 1L : 0L) << 38) ^ slow;
    }

    /// <summary>Fraction of the next tick already shown (units glide between ticks; 0 while paused).</summary>
    public float TickLead => IsReady && !State.Paused ? _pump.Fraction16 / 65536f : 0;
}
