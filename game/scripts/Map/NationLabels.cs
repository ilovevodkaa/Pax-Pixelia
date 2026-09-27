using System;
using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// HOI4-style nation names: widely spaced Alegreya SC capitals in a brightened nation colour with a dark halo,
/// laid along the territory's main axis (tilted at most ~28°, gently arched), sized by territory and fitted to its
/// length. Only met nations (all in observer mode); territory = owned provinces the player knows. A label that
/// would cover its capital slides along its axis, steps aside or, failing that, sits level above the capital.
/// Territory geometry is recomputed only on data change.
/// </summary>
internal sealed class NationLabels
{
    sealed class Info
    {
        public bool Show;
        public float Cx, Cy, Tot, Angle, Extent, Bend;
        public float Width32 = -1;     // spaced width at 32 px, for fitting
        public SpacedText Text;
    }

    readonly Info[] _n;

    public NationLabels()
    {
        _n = new Info[Data.Nations.Length];
        for (int i = 0; i < _n.Length; i++) _n[i] = new Info { Text = new SpacedText(Data.Nations[i].Name.ToUpperInvariant()) };
    }

    public void Refresh()
    {
        var g = Game.I; var w = g.World; var s = g.State;
        int nN = _n.Length;
        Span<double> sc = stackalloc double[nN], ss = stackalloc double[nN], sy = stackalloc double[nN], tot = stackalloc double[nN];
        bool fog = s.FogEnabled;
        for (int p = 0; p < w.P; p++)
        {
            int o = s.Owner[p];
            if (o < 0 || o >= nN || (fog && s.Fog[p] == 0)) continue;
            double a = w.PCX[p] / (double)w.W * Math.Tau, m = w.PSize[p];
            sc[o] += Math.Cos(a) * m; ss[o] += Math.Sin(a) * m; sy[o] += w.PCY[p] * m; tot[o] += m;
        }
        Span<double> sxx = stackalloc double[nN], syy = stackalloc double[nN], sxy = stackalloc double[nN];
        for (int n = 0; n < nN; n++)
        {
            var info = _n[n];
            info.Show = tot[n] > 0 && (!fog || g.NationMet(n));
            if (tot[n] <= 0) continue;
            info.Cx = (float)(((Math.Atan2(ss[n], sc[n]) / Math.Tau * w.W) % w.W + w.W) % w.W);
            info.Cy = (float)(sy[n] / tot[n]);
            info.Tot = (float)tot[n];
        }
        // second moments around the centroid (x unwrapped); each province also counts as a disc of its own area
        for (int p = 0; p < w.P; p++)
        {
            int o = s.Owner[p];
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
            _n[n].Bend = (float)(.35 * k);                           // arch strength, scaled per frame by the label length
        }
    }

    /// <summary>Draw labels (zoom ≤ 4) and add their screen boxes to <paramref name="boxes"/> (sea names keep clear).</summary>
    public void Draw(CanvasItem ci, in MapViewport v, List<Rect2> boxes)
    {
        var g = Game.I; var w = g.World; var s = g.State;
        float z = v.Zoom;
        int ps = PixelSprites.CityScale(v.Level);
        var font = MapFonts.Display700;
        for (int n = 0; n < _n.Length; n++)
        {
            var info = _n[n];
            if (!info.Show) continue;
            float fsRule = Math.Clamp(MathF.Sqrt(info.Tot) * z * .16f, 13, 38);
            // fit the spaced text into ~90% of the territory length
            if (info.Width32 < 0) info.Width32 = new SpacedText(Data.Nations[n].Name.ToUpperInvariant()).Width(font, 32, 32 * .22f);
            float fitFs = info.Extent * z * .9f / info.Width32 * 32;
            int fs = (int)MathF.Round(Math.Clamp(Math.Min(fsRule, fitFs), 13, 38));
            float spacing = fs * .22f;
            float tw = info.Text.Width(font, fs, spacing);
            float angle = info.Angle;
            // arch: sag of ~6% of the label length at its ends
            float bend = tw > 0 ? info.Bend * .24f / (tw * .5f) : 0;
            if (fs <= 14) { angle *= .5f; bend = 0; }

            // label centre relative to the territory centroid's screen position (same for every wrapped copy)
            var off = Vector2.Zero;
            int cap = s.NationCapital != null && n < s.NationCapital.Length ? s.NationCapital[n] : -1;
            if (z >= 2 && cap >= 0 && (!s.FogEnabled || s.Fog[cap] > 0))
                AvoidCapital(v, w, info, cap, fs, tw, ps, ref off, ref angle, ref bend);
            float yy = v.ScreenY(info.Cy) + off.Y;
            if (yy < -60 || yy > v.Screen.Y + 60) continue;
            var col = MapPalette.LabelColor(n);
            int halo = Math.Max(3, (int)MathF.Round(fs / 6f));
            for (float sx = v.FirstX(info.Cx, tw) + off.X; sx < v.Screen.X + tw; sx += v.WZ)
            {
                float hw = tw * .5f * MathF.Abs(MathF.Cos(angle)) + fs * .5f * MathF.Abs(MathF.Sin(angle));
                float hh = tw * .5f * MathF.Abs(MathF.Sin(angle)) + fs * .6f;
                boxes.Add(new Rect2(sx - hw, yy - hh, hw * 2, hh * 2));
                info.Text.Draw(ci, font, fs, spacing, sx, yy, angle, bend, col, halo, MapPalette.Halo);
            }
        }
    }

    /// <summary>
    /// Keep the label off the capital sprite and its name: slide it along its own axis, else sideways, else (as in
    /// the mockup) lay it level just above the capital. Overlap is tested label-box vs capital-box (separating axes).
    /// </summary>
    static void AvoidCapital(in MapViewport v, World.WorldData w, Info info, int cap, int fs, float tw, int ps, ref Vector2 off, ref float angle, ref float bend)
    {
        float z = v.Zoom;
        float dxw = w.PCX[cap] + .5f - info.Cx; dxw -= MathF.Round(dxw / w.W) * w.W;
        // capital block: sprite (8 sprite px tall) plus, from ×3, its name underneath
        float nameHalf = v.Level >= 3 ? MapFonts.Display700.GetStringSize(w.PName[cap], HorizontalAlignment.Left, -1, 14).X / 2 + 4 : 0;
        float top = -ps * 6 - 3, bottom = v.Level >= 3 ? ps * 6 + 9 + 10 : ps * 7;
        var c = new Vector2(dxw * z, (w.PCY[cap] + .5f - info.Cy) * z + (top + bottom) / 2);
        var hb = new Vector2(MathF.Max(ps * 7.5f, nameHalf), (bottom - top) / 2);
        float a = tw / 2 + 4, b = fs * .55f + (bend * tw * tw / 8) / 2;

        var u = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var nn = new Vector2(-u.Y, u.X);
        var d = c - off;
        if (!Overlap(d, u, nn, a, b, hb)) return;
        float ru = hb.X * MathF.Abs(u.X) + hb.Y * MathF.Abs(u.Y) + a + 3;
        float du = d.Dot(u), tu = du >= 0 ? du - ru : du + ru;             // slide along the axis, away from the capital
        if (MathF.Abs(tu) <= tw * .3f) { off += u * tu; return; }
        float rn = hb.X * MathF.Abs(nn.X) + hb.Y * MathF.Abs(nn.Y) + b + 3;
        float dn = d.Dot(nn), tn = dn >= 0 ? dn - rn : dn + rn;            // or step sideways
        if (MathF.Abs(tn) <= fs * 1.4f + ps * 4) { off += nn * tn; return; }
        angle = 0; bend = 0;
        off = new Vector2(0, c.Y - hb.Y - fs * .6f - 2);                    // level, right above the capital
    }

    static bool Overlap(Vector2 d, Vector2 u, Vector2 n, float a, float b, Vector2 hb)
    {
        if (MathF.Abs(d.X) > a * MathF.Abs(u.X) + b * MathF.Abs(n.X) + hb.X) return false;
        if (MathF.Abs(d.Y) > a * MathF.Abs(u.Y) + b * MathF.Abs(n.Y) + hb.Y) return false;
        if (MathF.Abs(d.Dot(u)) > a + hb.X * MathF.Abs(u.X) + hb.Y * MathF.Abs(u.Y)) return false;
        if (MathF.Abs(d.Dot(n)) > b + hb.X * MathF.Abs(n.X) + hb.Y * MathF.Abs(n.Y)) return false;
        return true;
    }
}
