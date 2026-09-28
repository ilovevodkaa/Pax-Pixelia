using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// HOI4-style nation names: widely spaced pixel-font capitals in a brightened nation colour with a hard dark shadow,
/// stepped along the territory's main axis (tilted at most ~28°, gently arched), sized by territory and fitted to its
/// length at the font's pixel-exact sizes (11 / 22 / 33 px, plus 16).
/// Only met nations (all in observer mode); territory = the provinces the player's map shows as theirs.
/// Placement (per zoom level, by <see cref="LabelPlan"/>): the first of a list of candidates — centroid, slid along
/// the axis, stepped aside, then level above / below the capital, then the same at smaller sizes — that hits no city
/// sprite or name nor another nation's name, and lies mostly over the nation's own land. The selected province is a
/// soft obstacle. A name that fits nowhere (or would need less than 11 px) is left out, as HOI4 does.
/// </summary>
internal sealed class NationLabels
{
    /// <summary>A placed name in level px (x in [0, W × zoom)); A/B are the half extents of its (arched) box.</summary>
    public struct Place
    {
        public bool Show;
        public float X, Y, Angle, Bend, Spacing, Tw, A, B;
        public int Fs;
    }

    sealed class Info
    {
        public bool Show;
        public float Cx, Cy, Tot, Angle, Extent, Bend;
        public float Width32 = -1;     // spaced width at 32 px, for fitting
        public SpacedText Text;
    }

    const int MaxFs = 33, MinFs = 11;
    const int CoverSamples = 9;                      // per row: along the top and the bottom of the letters
    const float CoverShare = .7f;                    // share of those points that must lie over the nation's own land
    static readonly int[] PixelSizes = { 33, 22, 16, 11 };   // pixel-exact (×3, ×2, ×1) plus 16 between

    // candidate offsets: along the axis (× label width) and across it (× font size), nearest the centroid first
    static readonly (float along, float across)[] Offsets = BuildOffsets();

    Info[] _n = Array.Empty<Info>();
    int[] _order = Array.Empty<int>();
    Data.Nation[] _roster;
    readonly List<(Vector2 c, Vector2 u, float a, float b)> _placed = new();

    /// <summary>Names of the running game's roster (Game.I.Nations; rebuilt when a new game brings another one).</summary>
    void SyncRoster()
    {
        var roster = Game.I.Nations;
        if (roster == _roster && _n.Length == roster.Length) return;
        _roster = roster;
        _n = new Info[roster.Length];
        _order = new int[roster.Length];
        for (int i = 0; i < _n.Length; i++) { _n[i] = new Info { Text = new SpacedText(roster[i].Name.ToUpperInvariant()) }; _order[i] = i; }
    }

    /// <summary>Letter spacing of a name: short names spread wider (ART_BIBLE §9), whole pixels.</summary>
    float Spacing(int n, int fs) => MathF.Round(fs * (_n[n].Text.Chars.Length >= 6 ? .3f : .5f));

    static (float, float)[] BuildOffsets()
    {
        var list = new List<(float, float)>();
        foreach (float a in new[] { 0f, .16f, -.16f, .32f, -.32f })
            foreach (float c in new[] { 0f, .6f, -.6f, 1.2f, -1.2f, 1.8f, -1.8f, 2.4f, -2.4f })
                list.Add((a, c));
        list.Sort((x, y) => (MathF.Abs(x.Item1) * 2 + MathF.Abs(x.Item2) * .5f).CompareTo(MathF.Abs(y.Item1) * 2 + MathF.Abs(y.Item2) * .5f));
        return list.ToArray();
    }

    /// <summary>Territory geometry of every nation (on ownership / fog changes).</summary>
    public void Refresh()
    {
        var g = Game.I; var w = g.World; var s = g.State;
        SyncRoster();
        int nN = _n.Length;
        Span<double> sc = stackalloc double[nN], ss = stackalloc double[nN], sy = stackalloc double[nN], tot = stackalloc double[nN];
        bool fog = s.FogEnabled;
        for (int p = 0; p < w.P; p++)
        {
            int o = g.ShownOwner(p);   // a province still filling with its new owner's colour counts for the old one
            if (o < 0 || o >= nN || (fog && s.Fog[p] == 0)) continue;
            double a = w.PCX[p] / (double)w.W * Math.Tau, m = w.PSize[p];
            sc[o] += Math.Cos(a) * m; ss[o] += Math.Sin(a) * m; sy[o] += w.PCY[p] * m; tot[o] += m;
        }
        Span<double> sxx = stackalloc double[nN], syy = stackalloc double[nN], sxy = stackalloc double[nN];
        for (int n = 0; n < nN; n++)
        {
            var info = _n[n];
            info.Show = tot[n] > 0 && (!fog || g.NationMet(n));
            info.Tot = (float)tot[n];
            if (tot[n] <= 0) continue;
            info.Cx = (float)(((Math.Atan2(ss[n], sc[n]) / Math.Tau * w.W) % w.W + w.W) % w.W);
            info.Cy = (float)(sy[n] / tot[n]);
        }
        // second moments around the centroid (x unwrapped); each province also counts as a disc of its own area
        for (int p = 0; p < w.P; p++)
        {
            int o = g.ShownOwner(p);
            if (o < 0 || o >= nN || tot[o] <= 0 || (fog && s.Fog[p] == 0)) continue;
            double dx = w.PCX[p] - _n[o].Cx, dy = w.PCY[p] - _n[o].Cy, m = w.PSize[p];
            dx -= Math.Round(dx / w.W) * w.W;
            double disc = m / (4 * Math.PI);
            sxx[o] += (dx * dx + disc) * m; syy[o] += (dy * dy + disc) * m; sxy[o] += dx * dy * m;
        }
        for (int n = 0; n < nN; n++)
        {
            if (tot[n] <= 0) continue;
            double a = sxx[n] / tot[n], b = syy[n] / tot[n], c = sxy[n] / tot[n];
            double tr = (a + b) / 2, det = Math.Sqrt(Math.Max(0, (a - b) * (a - b) / 4 + c * c));
            double l1 = tr + det, l2 = Math.Max(1e-6, tr - det), elong = Math.Sqrt(l1 / l2);
            double theta = .5 * Math.Atan2(2 * c, a - b);
            // round-ish territories read best level; elongated ones follow their axis, never steeper than ~28°
            double k = Math.Clamp((elong - 1.3) / .8, 0, 1);
            theta = Math.Clamp(theta, -.49, .49) * k;
            double ct = Math.Cos(theta), st = Math.Sin(theta);
            double along = a * ct * ct + 2 * c * ct * st + b * st * st;
            _n[n].Angle = (float)theta;
            _n[n].Extent = (float)Math.Sqrt(12 * along);           // length of a uniform bar with that variance
            _n[n].Bend = (float)(.35 * k);                           // arch strength, scaled by the label length
        }
        // big territories choose their spot first
        Array.Sort(_order, (x, y) => _n[x].Tot != _n[y].Tot ? _n[y].Tot.CompareTo(_n[x].Tot) : x.CompareTo(y));
    }

    // ------------------------------------------------------------------------------------------------ placement

    /// <summary>Place every nation name for a level (see the class summary). Names are shown up to ×4.</summary>
    public void Layout(LabelPlan plan, LabelPlan.Tier t, List<Rect2> cities, Rect2? soft, Func<int, bool> visible)
    {
        _placed.Clear();
        for (int n = 0; n < t.Nations.Length; n++) t.Nations[n].Show = false;
        if (t.Level > 4) return;
        var g = Game.I; var w = g.World; var s = g.State;
        SyncRoster();
        float z = t.Z, wrap = w.W * z;
        var font = MapFonts.Pixel;
        foreach (int n in _order)
        {
            if (n >= t.Nations.Length) continue;
            var info = _n[n];
            if (!info.Show) continue;
            if (info.Width32 < 0) info.Width32 = info.Text.Width(font, 33, Spacing(n, 33)) / 33 * 32;
            float fsRule = Math.Clamp(MathF.Sqrt(info.Tot) * z * .16f, MinFs, MaxFs);
            float fitFs = info.Extent * z * .9f / info.Width32 * 32;      // the spaced text over ~90% of the territory
            if (fitFs < MinFs * .8f) continue;                           // far too small a land for its name
            float fs0 = Math.Clamp(Math.Min(fsRule, fitFs), MinFs, MaxFs);
            int cap = s.NationCapital != null && n < s.NationCapital.Length ? s.NationCapital[n] : -1;
            bool capShown = cap >= 0 && t.Level >= 2 && visible(cap);

            Place best = default; bool found = false, perfect = false;
            int tries = 0;
            foreach (int fs in PixelSizes)
            {
                if (perfect || fs > fs0 + 2 || tries++ >= 3) continue;
                float spacing = Spacing(n, fs), tw = info.Text.Width(font, fs, spacing);
                float angle = info.Angle, bend = tw > 0 ? info.Bend * .24f / (tw * .5f) : 0;
                if (fs <= 16) { angle *= .5f; bend = 0; }
                var c0 = new Vector2(info.Cx * z, info.Cy * z);
                var u = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var nn = new Vector2(-u.Y, u.X);
                foreach (var (al, ac) in Offsets)
                    if (Try(n, c0 + u * (al * tw) + nn * (ac * fs), angle, bend, fs, spacing, tw)) break;
                if (perfect || !capShown) continue;
                // level, just above or below the capital's sprite and name
                var sr = plan.SpriteRect(w, cap, true, t.Level, z);
                float top = sr.Position.Y, bottom = t.Level >= 3 ? sr.End.Y + 3 + LabelPlan.CapSize : sr.End.Y;
                float cx = (w.PCX[cap] + .5f) * z;
                if (!Try(n, new Vector2(cx, top - fs * .6f - 3), 0, 0, fs, spacing, tw))
                    Try(n, new Vector2(cx, bottom + fs * .6f + 3), 0, 0, fs, spacing, tw);
            }
            if (!found) continue;
            best.X = ((best.X % wrap) + wrap) % wrap;
            t.Nations[n] = best;
            _placed.Add((new Vector2(best.X, best.Y), new Vector2(MathF.Cos(best.Angle), MathF.Sin(best.Angle)), best.A, best.B));

            // candidate check: hard obstacles and land cover must pass; the selection is only avoided when possible
            bool Try(int nation, Vector2 c, float angle, float bend, int fs, float spacing, float tw)
            {
                float sag = MathF.Abs(bend) * tw * tw / 8;
                float a = tw / 2 + 3, b = fs * .55f + sag / 2;
                var u = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var nn = new Vector2(-u.Y, u.X);
                foreach (var r in cities) if (Overlap(c, u, nn, a, b, r, wrap)) return false;
                foreach (var (pc, pu, pa, pb) in _placed) if (Overlap(c, u, nn, a, b, Box(pc, pu, pa, pb), wrap)) return false;
                if (!Covers(nation, c, u, nn, bend, tw, fs, z, w, s)) return false;
                bool clean = soft is not Rect2 sel || !Overlap(c, u, nn, a, b, sel, wrap);
                if (found && !clean) return false;
                best = new Place { Show = true, X = c.X, Y = c.Y, Angle = angle, Bend = bend, Spacing = spacing, Tw = tw, A = a, B = b, Fs = fs };
                found = true;
                perfect = clean;
                return clean;
            }
        }
    }

    /// <summary>Most of the label (the top and bottom of its letters along the arch) lies over land the map shows as
    /// the nation's, so a name neither spills over its neighbours nor straddles its own border.</summary>
    static bool Covers(int n, Vector2 c, Vector2 u, Vector2 nn, float bend, float tw, int fs, float z, World.WorldData w, Sim.GameState s)
    {
        int total = CoverSamples * 2, need = (int)MathF.Ceiling(total * CoverShare), hits = 0, seen = 0;
        for (int row = -1; row <= 1; row += 2)
            for (int i = 0; i < CoverSamples; i++, seen++)
            {
                float m = -tw / 2 + tw * i / (CoverSamples - 1);
                var q = (c + u * m + nn * (bend * m * m * .5f + row * fs * .32f)) / z;
                int y = (int)MathF.Floor(q.Y);
                if (y >= 0 && y < w.H)
                {
                    int x = ((int)MathF.Floor(q.X) % w.W + w.W) % w.W, p = w.Prov[y * w.W + x];
                    if (Game.I.ShownOwner(p) == n && (!s.FogEnabled || s.Fog[p] > 0)) hits++;
                }
                if (hits >= need) return true;
                if (hits + (total - 1 - seen) < need) return false;
            }
        return hits >= need;
    }

    /// <summary>Does a (province name) rect hit a placed nation name of this level?</summary>
    public bool Hits(LabelPlan.Tier t, Rect2 r)
    {
        float wrap = Game.I.World.W * t.Z;
        foreach (var p in t.Nations)
        {
            if (!p.Show) continue;
            var u = new Vector2(MathF.Cos(p.Angle), MathF.Sin(p.Angle));
            if (Overlap(new Vector2(p.X, p.Y), u, new Vector2(-u.Y, u.X), p.A, p.B, r, wrap)) return true;
        }
        return false;
    }

    static Rect2 Box(Vector2 c, Vector2 u, float a, float b)
    {
        var h = new Vector2(a * MathF.Abs(u.X) + b * MathF.Abs(u.Y), a * MathF.Abs(u.Y) + b * MathF.Abs(u.X));
        return new Rect2(c - h, h * 2);
    }

    /// <summary>Oriented box (centre c, axes u/n, half extents a/b) vs a rect, separating axes, x on the cylinder.</summary>
    static bool Overlap(Vector2 c, Vector2 u, Vector2 n, float a, float b, Rect2 r, float wrap)
    {
        var hb = r.Size / 2;
        var d = r.Position + hb - c;
        if (wrap > 0) d.X -= MathF.Round(d.X / wrap) * wrap;
        if (MathF.Abs(d.X) > a * MathF.Abs(u.X) + b * MathF.Abs(n.X) + hb.X) return false;
        if (MathF.Abs(d.Y) > a * MathF.Abs(u.Y) + b * MathF.Abs(n.Y) + hb.Y) return false;
        if (MathF.Abs(d.Dot(u)) > a + hb.X * MathF.Abs(u.X) + hb.Y * MathF.Abs(u.Y)) return false;
        if (MathF.Abs(d.Dot(n)) > b + hb.X * MathF.Abs(n.X) + hb.Y * MathF.Abs(n.Y)) return false;
        return true;
    }

    // ------------------------------------------------------------------------------------------------ drawing

    /// <summary>Draw a level's names at the current view zoom and add their screen boxes to <paramref name="boxes"/>
    /// (sea names keep clear). During a zoom glide the text keeps its pixel size; only its position follows the map.</summary>
    public void Draw(CanvasItem ci, in MapViewport v, LabelPlan.Tier t, List<Rect2> boxes)
    {
        var font = MapFonts.Pixel;
        for (int n = 0; n < t.Nations.Length && n < _n.Length; n++)
        {
            var p = t.Nations[n];
            if (!p.Show) continue;
            float yy = v.ScreenY(p.Y / t.Z);
            if (yy < -80 || yy > v.Screen.Y + 80) continue;
            var col = MapPalette.LabelColor(n);
            int depth = p.Fs >= 22 ? 2 : 1;
            float hw = p.A * MathF.Abs(MathF.Cos(p.Angle)) + p.B * MathF.Abs(MathF.Sin(p.Angle));
            float hh = p.A * MathF.Abs(MathF.Sin(p.Angle)) + p.B * MathF.Abs(MathF.Cos(p.Angle));
            for (float sx = v.FirstX(p.X / t.Z, hw); sx < v.Screen.X + hw; sx += v.WZ)
            {
                boxes.Add(new Rect2(sx - hw, yy - hh, hw * 2, hh * 2));
                _n[n].Text.Draw(ci, font, p.Fs, p.Spacing, sx, yy, p.Angle, p.Bend, col, TextFx.Shadow, depth);
            }
        }
    }
}
