using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;

namespace PaxPixelia.Map;

/// <summary>
/// Construction sites (ART_BIBLE §10.2), from ×5 like the building icons: on the plot where the building will stand
/// (<see cref="BuildingPlacement"/>, placed after the standing ones) its icon rises from the ground row by row with the
/// job's progress, under the scaffold of the era the job was ordered in (SpriteData.Sites: branches and hides, mud brick,
/// a treadwheel crane … a tower crane, a hologram), 4 frames at 220 ms, still on pause. The capital's own project shows
/// the same way while it is a building. When a building is done a puff of dust hangs over it for a moment. Immediate
/// mode: the retained chunks are not redrawn for an animation. Only what the player sees now (fog 2) is drawn.
/// </summary>
internal partial class SiteOverlay : MapOverlay
{
    const double FrameMs = 220, PuffMs = 640;

    readonly List<Data.Bld> _kinds = new();
    readonly List<(int Era, int Permille)> _jobs = new();
    readonly List<int> _provinces = new();
    readonly List<int> _mine = new();
    readonly HashSet<int> _seenP = new();
    // where each job stood last frame (province, building) → plot, to puff when it is gone and the building stands
    Dictionary<long, Vector2> _was = new(), _now = new();
    readonly List<(Vector2 At, int Province, double Start)> _puffs = new();

    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    public override void _Process(double delta)
    {
        if (!Game.I.IsReady || Map == null || Map.View.Level < 5) return;
        if (Game.I.State.Builds.Count > 0 || _puffs.Count > 0 || _was.Count > 0 || AnyProject(Game.I.State)) QueueRedraw();
    }

    static bool AnyProject(GameState s)
    {
        for (int n = 0; n < s.Nat.Length; n++)
            if (s.Nat[n].ProjectIndex >= 0 && s.NationCapital[n] >= 0 && Simulation.Projects[s.Nat[n].ProjectIndex].Building != null) return true;
        return false;
    }

    static long Key(int p, Data.Bld b) => (long)p << 8 | (byte)b;

    public override void _Draw()
    {
        (_was, _now) = (_now, _was);
        _now.Clear();
        if (Map == null || !Map.HasWorld || !Game.I.IsReady) { _was.Clear(); _puffs.Clear(); return; }
        var v = Map.View;
        if (v.Level < 5) { _was.Clear(); _puffs.Clear(); return; }
        var s = Game.I.State;
        double ms = Time.GetTicksMsec();
        bool still = s.Paused || Settings.I?.ReducedMotion == true;
        int bs = Lod.BuildingScale(v.Level);
        var plan = Map.Labels.Get(v.Level);

        // the provinces with something going up: jobs in order, then capitals busy with a building project
        _provinces.Clear(); _seenP.Clear();
        foreach (var j in s.Builds) if (_seenP.Add(j.Province)) _provinces.Add(j.Province);
        for (int n = 0; n < s.Nat.Length; n++)
        {
            int cap = s.NationCapital[n];
            if (cap >= 0 && s.Nat[n].ProjectIndex >= 0 && Simulation.Projects[s.Nat[n].ProjectIndex].Building != null && _seenP.Add(cap)) _provinces.Add(cap);
        }

        Span<Vector2> at = stackalloc Vector2[32];
        foreach (int p in _provinces)
        {
            if (FogOn && s.Fog[p] != 2) continue;
            var w = Game.I.World;
            float sy = v.ScreenY(w.PCY[p] + .5f);
            if (sy < -400 || sy > v.Screen.Y + 400) continue;   // far off screen: no plot of it can reach
            var standing = Map.Memory.Buildings(p);
            _kinds.Clear(); _jobs.Clear();
            foreach (var b in standing) _kinds.Add(b);
            // the jobs of a province share their builders, so they finish in the order of the cycles they still need
            // (ties in list order, as Construction.Cycle adds them): listed that way the next to stand is always first,
            // and joining the standing ones it keeps its plot
            _mine.Clear();
            for (int i = 0; i < s.Builds.Count; i++) if (s.Builds[i].Province == p) _mine.Add(i);
            int rate = Construction.RateOf(_mine.Count);
            _mine.Sort((a, b) =>
            {
                int ca = (s.Builds[a].Total - s.Builds[a].Work + rate - 1) / rate, cb = (s.Builds[b].Total - s.Builds[b].Work + rate - 1) / rate;
                return ca != cb ? ca.CompareTo(cb) : a.CompareTo(b);   // jobs finishing in one cycle stand in list order
            });
            foreach (int i in _mine) { var j = s.Builds[i]; _kinds.Add(j.Building); _jobs.Add((j.Era, Construction.PermilleDone(j))); }
            int owner = s.Owner[p];
            if (owner >= 0 && s.NationCapital[owner] == p && s.Nat[owner].ProjectIndex >= 0
                && Simulation.Projects[s.Nat[owner].ProjectIndex].Building is Data.Bld pb && !_kinds.Contains(pb))
            {
                _kinds.Add(pb); _jobs.Add((s.Nat[owner].Era, s.Nat[owner].QueuePct * 10));
            }
            bool city = BuildingPlacement.CityShown(Map.Memory, plan, v.Level, p, out bool cap);
            int placed = BuildingPlacement.Place(Map, p, cap, city, plan, _kinds, at);
            int nation = Map.Memory.Colours(p);
            for (int k = standing.Count; k < placed; k++)
            {
                var (era, pm) = _jobs[k - standing.Count];
                _now[Key(p, _kinds[k])] = at[k];
                float y = at[k].Y * v.Zoom + v.Origin.Y;
                if (y < -16 * bs || y > v.Screen.Y + 16 * bs) continue;   // the scaffold reaches 9 sprite px above and below its plot
                int frame = still ? 0 : (int)(ms / FrameMs) + p * 3 + k;
                int icon = MapAtlas.Building((int)_kinds[k]), site = MapAtlas.Site(era, frame);
                int rows = Math.Clamp(pm * MapAtlas.Size(icon).Y / 1000, 0, MapAtlas.Size(icon).Y - 1);
                for (float sx = v.FirstX(at[k].X, 40); sx < v.Screen.X + 40; sx += v.WZ)
                {
                    MapAtlas.DrawBottom(this, icon, nation, sx, y, bs, rows);
                    // the scaffold is 4 sprite px taller than the icon and stands on the same ground row
                    DrawSprite(site, nation, sx, y - 2 * bs, bs);
                }
            }
        }

        // a job gone from the list whose building now stands: dust over it for a moment
        foreach (var (key, pos) in _was)
        {
            if (_now.ContainsKey(key)) continue;
            int p = (int)(key >> 8);
            var b = (Data.Bld)(byte)(key & 0xFF);
            if ((uint)p < (uint)s.Buildings.Length && s.Buildings[p].Contains(b) && Settings.I?.ReducedMotion != true) _puffs.Add((pos, p, ms));
        }
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            var (pos, p, start) = _puffs[i];
            double age = ms - start;
            if (age > PuffMs || (FogOn && s.Fog[p] != 2)) { _puffs.RemoveAt(i); continue; }
            int id = MapAtlas.Puff(age < PuffMs / 2 ? 0 : 1);
            for (float sx = v.FirstX(pos.X, 40); sx < v.Screen.X + 40; sx += v.WZ)
                MapAtlas.DrawSprite(this, id, Map.Memory.Colours(p), sx, pos.Y * v.Zoom + v.Origin.Y - 2 * bs, bs);
        }
    }
}
