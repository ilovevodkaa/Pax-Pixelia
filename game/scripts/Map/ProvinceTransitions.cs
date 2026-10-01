using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Map;

/// <summary>
/// Capture fill (GDD «Захват провинции — анимация заливки», ART_BIBLE §13 «Расширение границы»). When the owner shown
/// for a visible land province changes, the new look does not snap: a front runs out from the border the province
/// shares with the new owner's land (from its centre when there is none) in <see cref="Duration"/> real seconds (game
/// speed and pause do not matter). The front is a light crest 1–2 px thick whose edges are 4×4 Bayer thresholds in
/// world pixels: the old look dithers into the crest, the crest into the new colour (Political / Religion: the full
/// new colour; Terrain and other modes whose colour ignores the owner: only the crest passes). Captures of one frame by
/// one owner flood as one wave (a chain of provinces fills from the old border onwards). The country borders keep the
/// old owner until the fill ends and are redrawn then; nation names, city colours (<see cref="Game.ShownOwner"/>) and
/// the minimap (<see cref="Game.CaptureFillsRunning"/>) switch at the end too (<see cref="Finished"/>).
/// CPU, once per capture: Dijkstra (chamfer 5/7, plus a little value noise so the front meanders) over the province
/// pixels gives each pixel its arrival distance, written into a small R8 atlas. A per-province R8 texture points at
/// the transition's slot; a float row per slot keeps the old tint / border / grading, the timing and the atlas box.
/// While fronts move only the clock uniform changes; the shader side is map_capture.gdshaderinc.
/// Provinces the player cannot see now (fog: visible before and after), off-screen ones and anything past the slot /
/// atlas budget simply snap; a map-mode change snaps everything.
/// Debug (after "--"): --capturetest [--capturecount=N] [--capturegap=S] claim N (3) neighbouring provinces S (1) s
/// apart (0: in one frame, so they flood as one wave) · --capturefreeze=T hold every front at progress T (0..1; 1 lets
/// them end, so borders and names are redrawn) · --capturedur=S front duration (0.9 s) · --capturebench
/// [--capturecount=N] N (20) synthetic fronts in view over and over, frame times printed, then quit · --capturelog
/// print every batch of owner changes (started / snapped).
/// </summary>
internal sealed class ProvinceTransitions : ICaptureFills
{
    public const int MaxSlots = 64;                 // simultaneous fronts
    const int MaxPixelsPerFrame = 120_000;         // Dijkstra budget per frame: later captures snap
    const int Atlas = 512, Cell = 16, Cells = Atlas / Cell;
    const int SlotTexels = 5;
    const float NoiseAmp = 2.5f;                    // px of meander added to the arrival distance
    const int NoiseCell = 8;                        // px lattice of that noise

    public static readonly float Duration = Math.Clamp(Cli.Float("capturedur", .9f), .05f, 60f);
    static readonly bool LogOn = Cli.Has("capturelog");
    static readonly float Freeze = Cli.Has("capturefreeze") ? Math.Clamp(Cli.Float("capturefreeze", .5f), 0f, 1f) : -1f;

    static readonly StringName UProv = "cap_prov_tex", USlot = "cap_slot_tex", UDist = "cap_dist_tex",
        UActive = "cap_active", UTime = "cap_time", UFreeze = "cap_freeze";

    struct Slot { public int P, Cx, Cy, Cw, Ch, OldOwn; public double End; public ulong StartUs; }   // OldOwn: owner byte (owner + 1)

    sealed class Job
    {
        public int P, X0, Y0, BW, BH, Slot, Cx, Cy, Cw, Ch;
        public int[] Dist;
        public float[] D;
    }

    ShaderMaterial _mat;
    WorldData _w;
    int _pw, _ph;
    byte[] _oldTint = Array.Empty<byte>(), _oldInfo = Array.Empty<byte>(), _oldOwn = Array.Empty<byte>();
    byte[] _prov = Array.Empty<byte>(), _atlas = new byte[Atlas * Atlas];
    readonly float[] _slotF = new float[MaxSlots * SlotTexels * 4];
    readonly byte[] _slotB = new byte[MaxSlots * SlotTexels * 16];
    Image _provImg, _slotImg, _atlasImg;
    ImageTexture _provTex, _slotTex, _atlasTex;
    readonly Slot[] _slots = new Slot[MaxSlots];
    readonly bool[] _cellUsed = new bool[Cells * Cells];
    int _active;
    bool _warming;
    double _time;
    bool _dirtyProv, _dirtySlot, _dirtyAtlas, _dirtyActive;
    int[] _jobOf = Array.Empty<int>(), _stamp = Array.Empty<int>();
    int _stampGen;
    readonly List<Job> _jobs = new();
    readonly List<int>[] _bucket = { new(), new(), new(), new(), new(), new(), new(), new() };   // Dial's queue: d & 7
    int _queued;
    readonly List<int> _finished = new();

    public int Active => _active;
    public bool Running => _active > 0;

    /// <summary>Provinces whose fill ended (or snapped) since the last <see cref="ClearFinished"/>: their labels, city
    /// colours and borders show the new owner now.</summary>
    public List<int> Finished => _finished;
    public void ClearFinished() => _finished.Clear();

    public int HeldOwner(int p)
    {
        if (_active == 0 || (uint)p >= (uint)_prov.Length || _w != Game.I.World) return int.MinValue;
        int i = _prov[p] - 1;
        return i >= 0 && _slots[i].P == p ? _slots[i].OldOwn - 1 : int.MinValue;
    }


    /// <summary>New world: textures sized for it, bound to the map material, the current look taken as «old».</summary>
    public void Reset(WorldData w, MapTextures tex, ShaderMaterial mat)
    {
        _w = w; _mat = mat;
        _pw = MapTextures.PDataW; _ph = Math.Max(1, (w.P + _pw - 1) / _pw);
        _prov = new byte[_pw * _ph];
        _provImg = Image.CreateFromData(_pw, _ph, false, Image.Format.R8, _prov);
        _provTex = ImageTexture.CreateFromImage(_provImg);
        _slotImg = Image.CreateFromData(SlotTexels, MaxSlots, false, Image.Format.Rgbaf, _slotB);
        _slotTex = ImageTexture.CreateFromImage(_slotImg);
        _atlasImg = Image.CreateFromData(Atlas, Atlas, false, Image.Format.R8, _atlas);
        _atlasTex = ImageTexture.CreateFromImage(_atlasImg);
        for (int i = 0; i < MaxSlots; i++) _slots[i].P = -1;
        Array.Clear(_cellUsed);
        _active = 0; _time = 0;
        _jobOf = new int[w.P]; _stamp = new int[w.P]; _stampGen = 0;
        mat.SetShaderParameter(UProv, _provTex);
        mat.SetShaderParameter(USlot, _slotTex);
        mat.SetShaderParameter(UDist, _atlasTex);
        mat.SetShaderParameter(UActive, 0);
        mat.SetShaderParameter(UFreeze, Freeze);
        Remember(tex);
        WarmUp(w, tex);
        _finished.Clear();
        Flush();
        _debug.Reset();
    }

    /// <summary>One throwaway front at load: JIT-compiles the per-pixel paths now (~3 ms once) instead of on the
    /// first capture of the game.</summary>
    void WarmUp(WorldData w, MapTextures tex)
    {
        int p = 0;
        while (p < w.P && w.PLand[p] != 1) p++;
        if (p >= w.P) return;
        _jobs.Clear();
        _jobs.Add(new Job { P = p });
        _warming = true;
        Start(w, tex, _jobs, _oldTint, _oldInfo, _oldOwn, onScreenOnly: false);
        CancelAll();
        _warming = false;
        _jobs.Clear();
    }

    /// <summary>Every frame: advance the clock, retire finished fronts, run the debug switches.</summary>
    public void Tick(double delta)
    {
        if (_w == null) return;
        _debug.Tick(this, delta);
        if (_active == 0) return;
        _time += delta;
        if (Freeze < 0 || Freeze >= 1)
            for (int i = 0; i < MaxSlots; i++)
                if (_slots[i].P >= 0 && _time >= _slots[i].End) Free(i);
        Flush();
    }

    /// <summary>
    /// Right after MapTextures.UpdateProvinces: provinces of <paramref name="changed"/> (null = everything, no fronts)
    /// whose shown owner changed start a front from the look the textures had before. A mode change snaps all.
    /// </summary>
    public void OnUploaded(WorldData w, GameState s, MapTextures tex, List<int> changed, bool modeChanged)
    {
        if (_w == null) return;
        if (modeChanged) CancelAll();
        else if (changed != null && changed.Count > 0) Detect(w, s, tex, changed);
        Remember(tex);
        Flush();
    }

    void Remember(MapTextures tex)
    {
        Copy(tex.TintData, ref _oldTint); Copy(tex.InfoData, ref _oldInfo); Copy(tex.OwnData, ref _oldOwn);
        static void Copy(byte[] src, ref byte[] dst)
        {
            if (dst.Length != src.Length) dst = new byte[src.Length];
            Buffer.BlockCopy(src, 0, dst, 0, src.Length);
        }
    }

    // ---------------------------------------------------------------- starting fronts

    void Detect(WorldData w, GameState s, MapTextures tex, List<int> changed)
    {
        var own = tex.OwnData; var info = tex.InfoData;
        bool fogOn = s.FogEnabled, snap = Settings.I?.ReducedMotion == true;   // «меньше анимации»: captures snap
        if (++_stampGen == int.MaxValue) { Array.Clear(_stamp); _stampGen = 1; }
        _jobs.Clear();
        int fogged = 0;
        foreach (int p in changed)
        {
            if ((uint)p >= (uint)w.P || _stamp[p] == _stampGen) continue;
            _stamp[p] = _stampGen;
            int k = p * 4;
            if (w.PLand[p] != 1 || own[k + 3] == _oldOwn[k + 3]) continue;
            Cancel(p);
            // fog rules: only what the player sees now, and saw before, moves; the rest snaps like any fog update
            if (fogOn && (info[k + 2] != 255 || _oldInfo[k + 2] != 255)) { fogged++; continue; }
            if (snap) continue;
            _jobs.Add(new Job { P = p });
        }
        int asked = _jobs.Count;
        if (asked > 0) Start(w, tex, _jobs, _oldTint, _oldInfo, _oldOwn, onScreenOnly: true);
        if (LogOn && asked + fogged > 0)
            GD.Print($"capture: {_jobs.Count} fronts started, {asked - _jobs.Count} snapped (off screen / budget), {fogged} snapped (fog); active {_active}");
    }

    /// <summary>
    /// Start fronts for <paramref name="jobs"/>, their old look taken from the given per-province bytes (MapTextures
    /// layout). Jobs grouped by new owner flood together.
    /// </summary>
    void Start(WorldData w, MapTextures tex, List<Job> jobs, byte[] oldTint, byte[] oldInfo, byte[] oldOwn, bool onScreenOnly)
    {
        var own = tex.OwnData; var tint = tex.TintData;
        var cam = Game.I.CameraRect.Grow(48);
        int budget = MaxPixelsPerFrame;
        // allocate: bbox, on screen, atlas cells, slot; whatever does not fit snaps
        for (int i = jobs.Count - 1; i >= 0; i--)
        {
            var j = jobs[i];
            Bbox(w, j);
            int px = w.PixOffset[j.P + 1] - w.PixOffset[j.P];
            if ((onScreenOnly && !OnScreen(w, j, cam)) || px > budget || !AllocCells(j) || (j.Slot = FreeSlot()) < 0)
            {
                if (j.Cw > 0) FreeCells(j.Cx, j.Cy, j.Cw, j.Ch);
                jobs.RemoveAt(i);
                continue;
            }
            budget -= px;
            _slots[j.Slot].P = j.P;   // reserved
        }
        if (jobs.Count == 0) return;
        // group by the new owner (own byte), one Dijkstra per group
        jobs.Sort((a, b) => own[a.P * 4 + 3].CompareTo(own[b.P * 4 + 3]));
        for (int g0 = 0; g0 < jobs.Count;)
        {
            int ob = own[jobs[g0].P * 4 + 3], g1 = g0;
            while (g1 < jobs.Count && own[jobs[g1].P * 4 + 3] == ob) g1++;
            float reach = Flood(w, own, jobs, g0, g1, ob);
            float step = MathF.Max(reach, 1f) / 254f;
            for (int i = g0; i < g1; i++) Commit(w, jobs[i], tint, oldTint, oldInfo, oldOwn, reach, step);
            g0 = g1;
        }
        _dirtyProv = _dirtySlot = _dirtyAtlas = _dirtyActive = true;
    }

    /// <summary>Arrival distance (px) of every pixel of jobs[g0..g1), seeded at the pixels touching owner byte ob's
    /// other land; returns the largest.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]   // no slow tier-0 JIT on the first capture of a game
    float Flood(WorldData w, byte[] own, List<Job> jobs, int g0, int g1, int ob)
    {
        if (++_stampGen == int.MaxValue) { Array.Clear(_stamp); _stampGen = 1; }
        for (int i = g0; i < g1; i++) { _stamp[jobs[i].P] = _stampGen; _jobOf[jobs[i].P] = i; }
        int W = w.W, H = w.H;
        foreach (var b in _bucket) b.Clear();
        _queued = 0;
        for (int i = g0; i < g1; i++)
        {
            var j = jobs[i];
            j.Dist = new int[j.BW * j.BH];
            Array.Fill(j.Dist, int.MaxValue);
            for (int k = w.PixOffset[j.P], e = w.PixOffset[j.P + 1]; k < e; k++)
            {
                int gi = w.PixList[k], x = gi % W, y = gi / W;
                if (Touches(x - 1, y) || Touches(x + 1, y) || Touches(x, y - 1) || Touches(x, y + 1))
                    Push(i, Local(j, x, y), 0);
            }
        }
        Run();
        // provinces (or parts) the owner's land does not touch: from the centre, then from whatever is left
        for (int i = g0; i < g1; i++)
        {
            var j = jobs[i];
            int c = Local(j, w.PCX[j.P], w.PCY[j.P]);
            if (c >= 0 && w.Prov[w.PCY[j.P] * W + w.PCX[j.P]] == j.P && j.Dist[c] == int.MaxValue) { Push(i, c, 0); Run(); }
            for (int k = w.PixOffset[j.P], e = w.PixOffset[j.P + 1]; k < e; k++)
            {
                int gi = w.PixList[k], l = Local(j, gi % W, gi / W);
                if (j.Dist[l] == int.MaxValue) { Push(i, l, 0); Run(); }
            }
        }
        float reach = 0;
        for (int i = g0; i < g1; i++)
        {
            var j = jobs[i];
            j.D = new float[j.Dist.Length];
            for (int k = w.PixOffset[j.P], e = w.PixOffset[j.P + 1]; k < e; k++)
            {
                int gi = w.PixList[k], x = gi % W, y = gi / W, l = Local(j, x, y);
                float d = MathF.Max(0f, j.Dist[l] / 5f + NoiseAmp * (Noise(x, y, W) * 2f - 1f));
                j.D[l] = d;
                if (d > reach) reach = d;
            }
            j.Dist = null;
        }
        return reach;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        bool Touches(int x, int y)
        {
            if (y < 0 || y >= H) return false;
            if (x < 0) x += W; else if (x >= W) x -= W;
            int q = w.Prov[y * W + x];
            return _stamp[q] != _stampGen && w.PLand[q] == 1 && own[q * 4 + 3] == ob;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        void Push(int i, int l, int d) { jobs[i].Dist[l] = d; _bucket[d & 7].Add((i << 21) | l); _queued++; }

        // Dial's algorithm: steps cost 5 or 7, so everything queued lies within 8 of the current distance
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        void Run()
        {
            for (int d = 0; _queued > 0; d++)
            {
                var bucket = _bucket[d & 7];
                for (int n = 0; n < bucket.Count; n++) Settle(bucket[n], d);
                _queued -= bucket.Count;
                bucket.Clear();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        void Settle(int node, int d)
        {
            int i = node >> 21, l = node & ((1 << 21) - 1);
            var j = jobs[i];
            if (j.Dist[l] != d) return;   // improved since it was queued
            int x = j.X0 + l % j.BW, y = j.Y0 + l / j.BW;
            if (x >= W) x -= W;
            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= H) continue;
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx;
                    if (nx < 0) nx += W; else if (nx >= W) nx -= W;
                    int q = w.Prov[ny * W + nx];
                    if (_stamp[q] != _stampGen) continue;
                    int iq = _jobOf[q];
                    var jq = jobs[iq];
                    int lq = Local(jq, nx, ny), nd = d + (dx != 0 && dy != 0 ? 7 : 5);
                    if (lq >= 0 && nd < jq.Dist[lq]) Push(iq, lq, nd);
                }
            }
        }
    }

    /// <summary>Write the job's distances into its atlas cells and its slot row, point the province at the slot.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    void Commit(WorldData w, Job j, byte[] tint, byte[] oldTint, byte[] oldInfo, byte[] oldOwn, float reach, float step)
    {
        int W = w.W, ax = j.Cx * Cell, ay = j.Cy * Cell;
        for (int k = w.PixOffset[j.P], e = w.PixOffset[j.P + 1]; k < e; k++)
        {
            int gi = w.PixList[k], x = gi % W, y = gi / W;
            int lx = x - j.X0; if (lx < 0) lx += W;
            int ly = y - j.Y0;
            _atlas[(ay + ly) * Atlas + ax + lx] = (byte)Math.Min(254, (int)MathF.Round(j.D[ly * j.BW + lx] / step));
        }
        j.D = null;
        int b = j.P * 4, o = j.Slot * SlotTexels * 4;
        for (int c = 0; c < 4; c++) { _slotF[o + c] = oldTint[b + c] / 255f; _slotF[o + 4 + c] = oldOwn[b + c] / 255f; }
        _slotF[o + 8] = oldInfo[b] / 255f; _slotF[o + 9] = oldInfo[b + 1] / 255f;
        _slotF[o + 10] = tint[b + 3] > 0 || oldTint[b + 3] > 0 ? 1f : 1.25f;   // crest: the only sign of a capture where no tint changes colour (Terrain)
        _slotF[o + 11] = 0;
        _slotF[o + 12] = j.X0; _slotF[o + 13] = j.Y0; _slotF[o + 14] = ax; _slotF[o + 15] = ay;
        _slotF[o + 16] = (float)_time; _slotF[o + 17] = Duration; _slotF[o + 18] = reach; _slotF[o + 19] = step;
        _slots[j.Slot] = new Slot { P = j.P, Cx = j.Cx, Cy = j.Cy, Cw = j.Cw, Ch = j.Ch, OldOwn = oldOwn[b + 3], End = _time + Duration + .02, StartUs = Time.GetTicksUsec() };
        _prov[j.P] = (byte)(j.Slot + 1);
        _active++;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static void Bbox(WorldData w, Job j)
    {
        int W = w.W, cx = w.PCX[j.P], x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;
        for (int k = w.PixOffset[j.P], e = w.PixOffset[j.P + 1]; k < e; k++)
        {
            int gi = w.PixList[k], y = gi / W, dx = gi % W - cx;
            if (dx > W / 2) dx -= W; else if (dx < -W / 2) dx += W;
            if (dx < x0) x0 = dx; if (dx > x1) x1 = dx;
            if (y < y0) y0 = y; if (y > y1) y1 = y;
        }
        j.X0 = Mathf.PosMod(cx + x0, W); j.Y0 = y0; j.BW = x1 - x0 + 1; j.BH = y1 - y0 + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    int Local(Job j, int x, int y)
    {
        int lx = x - j.X0; if (lx < 0) lx += _w.W;
        int ly = y - j.Y0;
        return lx < j.BW && (uint)ly < (uint)j.BH ? ly * j.BW + lx : -1;
    }

    static bool OnScreen(WorldData w, Job j, Rect2 cam)
    {
        if (cam.Size.X <= 0) return true;
        if (j.Y0 + j.BH < cam.Position.Y || j.Y0 > cam.End.Y) return false;
        for (int s = -1; s <= 1; s++)
        {
            float x0 = j.X0 + s * w.W;
            if (x0 + j.BW >= cam.Position.X && x0 <= cam.End.X) return true;
        }
        return false;
    }

    /// <summary>Smooth value noise in [0, 1] on a <see cref="NoiseCell"/> px lattice, periodic in x with the world.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    static float Noise(int x, int y, int W)
    {
        float fx = (x + .5f) / NoiseCell, fy = (y + .5f) / NoiseCell;
        int cx = (int)MathF.Floor(fx), cy = (int)MathF.Floor(fy), cw = Math.Max(1, W / NoiseCell);
        float tx = fx - cx, ty = fy - cy;
        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
        float a = L(cx, cy), b = L(cx + 1, cy), c = L(cx, cy + 1), d = L(cx + 1, cy + 1);
        return (a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty;

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        float L(int ix, int iy)
        {
            ix = ((ix % cw) + cw) % cw;
            uint h = (uint)ix * 374761393u + (uint)iy * 668265263u + 0x9E3779B9u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 1023u) / 1023f;
        }
    }

    // ---------------------------------------------------------------- slots and atlas cells

    int FreeSlot()
    {
        for (int i = 0; i < MaxSlots; i++) if (_slots[i].P < 0) return i;
        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    bool AllocCells(Job j)
    {
        j.Cw = (j.BW + Cell - 1) / Cell; j.Ch = (j.BH + Cell - 1) / Cell;
        if (j.Cw > Cells || j.Ch > Cells) { j.Cw = j.Ch = 0; return false; }
        for (int cy = 0; cy + j.Ch <= Cells; cy++)
            for (int cx = 0; cx + j.Cw <= Cells; cx++)
            {
                bool free = true;
                for (int y = 0; y < j.Ch && free; y++)
                    for (int x = 0; x < j.Cw; x++)
                        if (_cellUsed[(cy + y) * Cells + cx + x]) { free = false; break; }
                if (!free) continue;
                for (int y = 0; y < j.Ch; y++)
                    for (int x = 0; x < j.Cw; x++) _cellUsed[(cy + y) * Cells + cx + x] = true;
                j.Cx = cx; j.Cy = cy;
                return true;
            }
        j.Cw = j.Ch = 0;
        return false;
    }

    void FreeCells(int cx, int cy, int cw, int ch)
    {
        for (int y = 0; y < ch; y++)
            for (int x = 0; x < cw; x++) _cellUsed[(cy + y) * Cells + cx + x] = false;
    }

    void Free(int i)
    {
        ref var sl = ref _slots[i];
        if (sl.P < 0) return;
        if (_prov[sl.P] == i + 1) _prov[sl.P] = 0;
        _finished.Add(sl.P);
        if (LogOn && !_warming) GD.Print($"capture: fill of {sl.P} ended after {(Time.GetTicksUsec() - sl.StartUs) / 1e6:F3} s real time");
        FreeCells(sl.Cx, sl.Cy, sl.Cw, sl.Ch);
        sl.P = -1;
        _active--;
        _dirtyProv = _dirtyActive = true;
        if (_active == 0) _time = 0;
    }

    /// <summary>Snap p to its new look if a front is crossing it.</summary>
    void Cancel(int p)
    {
        int i = _prov[p] - 1;
        if (i >= 0 && _slots[i].P == p) Free(i);
    }

    void CancelAll()
    {
        for (int i = 0; i < MaxSlots; i++) Free(i);
    }

    void Flush()
    {
        if (_dirtyAtlas) { _atlasImg.SetData(Atlas, Atlas, false, Image.Format.R8, _atlas); _atlasTex.Update(_atlasImg); }
        if (_dirtySlot)
        {
            Buffer.BlockCopy(_slotF, 0, _slotB, 0, _slotB.Length);
            _slotImg.SetData(SlotTexels, MaxSlots, false, Image.Format.Rgbaf, _slotB);
            _slotTex.Update(_slotImg);
        }
        if (_dirtyProv) { _provImg.SetData(_pw, _ph, false, Image.Format.R8, _prov); _provTex.Update(_provImg); }
        if (_dirtyActive) _mat.SetShaderParameter(UActive, _active);
        if (_active > 0) _mat.SetShaderParameter(UTime, (float)_time);
        _dirtyAtlas = _dirtySlot = _dirtyProv = _dirtyActive = false;
    }

    // ---------------------------------------------------------------- debug switches

    readonly DebugRun _debug = new();

    /// <summary>--capturetest / --capturebench (see the class summary).</summary>
    sealed class DebugRun
    {
        static readonly int TestCount = Cli.Has("capturetest") ? Math.Max(1, Cli.Int("capturecount", 3)) : 0;
        static readonly float Gap = MathF.Max(0f, Cli.Float("capturegap", 1f));
        static readonly int BenchCount = Cli.Has("capturebench") ? Math.Clamp(Cli.Int("capturecount", 20), 1, MaxSlots) : 0;
        static bool _testDone, _benchDone;

        double _t, _next;
        int _claimed, _last = -1;
        // bench
        int _phase;
        readonly List<double> _frames = new(8192);
        readonly List<double> _startMs = new();
        List<double> _idle;

        public void Reset() { _t = 0; _next = 1.0; _claimed = 0; _last = -1; _phase = 0; }

        public void Tick(ProvinceTransitions tr, double delta)
        {
            if (TestCount == 0 && BenchCount == 0) return;
            _t += delta;
            if (TestCount > 0 && !_testDone && _t >= _next)
                do ClaimNext(); while (Gap <= 0 && !_testDone);   // --capturegap=0: all in one frame, one wave
            if (BenchCount > 0 && !_benchDone) Bench(tr, delta);
        }

        void ClaimNext()
        {
            var g = Game.I; var w = g.World; var s = g.State;
            int cap = s.NationCapital[g.Viewer];
            int best = -1; float bestD = float.MaxValue;
            for (int pass = 0; pass < 2 && best < 0; pass++)
                for (int p = 0; p < w.P; p++)
                {
                    var e = Rules.CheckClaim(w, s, p, g.Viewer);
                    if (e != ClaimError.None && e != ClaimError.NoGold) continue;
                    if (pass == 0 && (_last < 0 || Array.IndexOf(w.Adj[p], _last) < 0)) continue;   // a chain if possible
                    float dx = MathF.Abs(w.PCX[p] - w.PCX[cap]); dx = MathF.Min(dx, w.W - dx);
                    float d = dx * dx + (w.PCY[p] - w.PCY[cap]) * (float)(w.PCY[p] - w.PCY[cap]);
                    if (d < bestD) { bestD = d; best = p; }
                }
            if (best < 0) { GD.Print("capturetest: nothing left to claim"); _testDone = true; return; }
            g.Issue(Cmd.CheatGold(g.Viewer, g.ClaimPrice));
            g.Claim(best);
            GD.Print($"capturetest: claimed {best} {w.PName[best]} centre ({w.PCX[best]},{w.PCY[best]}) at t={_t:F2}s");
            _last = best;
            if (++_claimed >= TestCount) _testDone = true;
            _next = _t + Gap;
        }

        // phase 0: settle 2 s · 1: 4 s without fronts · 2: 6 s with BenchCount fronts restarted every Duration + .1 s
        void Bench(ProvinceTransitions tr, double delta)
        {
            if (_phase == 0)
            {
                if (_t < 2) return;
                DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
                _phase = 1; _t = 0; _frames.Clear();
                return;
            }
            _frames.Add(delta * 1000);
            if (_phase == 1)
            {
                if (_t < 4) return;
                _idle = new List<double>(_frames);
                _phase = 2; _t = 0; _next = 0; _frames.Clear();
                return;
            }
            if (_t >= _next)
            {
                _next = _t + Duration + .1;
                int n = tr.StartSynthetic(BenchCount, out double ms);
                _startMs.Add(ms);
                if (_startMs.Count == 1) GD.Print($"capturebench: {n} fronts per batch, active {tr.Active}");
            }
            if (_t < 6) return;
            _benchDone = true;
            Report("idle (no fronts)", _idle);
            Report($"{BenchCount} fronts, restarted every {Duration + .1:F2}s", _frames);
            _startMs.Sort();
            GD.Print($"capturebench: batch start (Dijkstra + atlas + uploads) avg {Avg(_startMs):F3} ms, worst {_startMs[^1]:F3} ms over {_startMs.Count} batches, zoom ×{Game.I.ZoomLevel}");
            (Engine.GetMainLoop() as SceneTree)?.Quit();
        }

        static double Avg(List<double> v) { double s = 0; foreach (var x in v) s += x; return s / Math.Max(1, v.Count); }

        static void Report(string name, List<double> f)
        {
            if (f.Count < 3) return;
            var a = f.GetRange(1, f.Count - 1); a.Sort();
            double P(double q) => a[Math.Min(a.Count - 1, (int)(q * a.Count))];
            GD.Print($"capturebench: {name,-40} frames={a.Count,6} avg={Avg(a):F3} ms p99={P(.99):F3} worst={a[^1]:F3} ms");
        }
    }

    /// <summary>--capturebench: fronts on the <paramref name="n"/> visible land provinces nearest the view centre, from
    /// the look of unowned land to their current look (nothing in the game changes).</summary>
    int StartSynthetic(int n, out double ms)
    {
        var g = Game.I; var w = g.World; var s = g.State;
        var tex = MapView.Current.Tex;
        var info = tex.InfoData;
        var c = g.CameraRect.GetCenter();
        var cand = new List<(float d, int p)>();
        for (int p = 0; p < w.P; p++)
        {
            if (w.PLand[p] != 1 || (s.FogEnabled && info[p * 4 + 2] != 255) || _prov[p] != 0) continue;
            float dx = MathF.Abs(w.PCX[p] - Mathf.PosMod(c.X, w.W)); dx = MathF.Min(dx, w.W - dx);
            float dy = w.PCY[p] - c.Y;
            cand.Add((dx * dx + dy * dy, p));
        }
        cand.Sort((a, b) => a.d.CompareTo(b.d));
        var old = new byte[tex.TintData.Length];
        var oldInfo = (byte[])info.Clone();
        var oldOwn = new byte[old.Length];
        _jobs.Clear();
        for (int i = 0; i < cand.Count && _jobs.Count < n; i++)
        {
            int p = cand[i].p;
            oldInfo[p * 4] = 46; oldInfo[p * 4 + 1] = 240;   // unowned land in the political mode
            _jobs.Add(new Job { P = p });
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Start(w, tex, _jobs, old, oldInfo, oldOwn, onScreenOnly: false);
        Flush();
        ms = sw.Elapsed.TotalMilliseconds;
        return _jobs.Count;
    }
}
