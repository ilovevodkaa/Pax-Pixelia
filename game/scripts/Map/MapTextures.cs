using System;
using Godot;
using PaxPixelia.Core;
using PaxPixelia.Sim;
using PaxPixelia.World;

namespace PaxPixelia.Map;

/// <summary>
/// GPU textures of the map shaders. Per-pixel textures (terrain colour, province id) are uploaded once per world;
/// per-province textures (P entries laid out PDataW wide) and the fog distance field are re-uploaded only when
/// Game raises MapModeChanged / ProvincesChanged / FogChanged.
/// </summary>
internal sealed class MapTextures
{
    public const int PDataW = 256;
    /// <summary>Explored-but-unseen land: faded like an old map, not darkened into mud (same numbers in stale_grade).</summary>
    public const float StaleDesat = .45f, StaleDim = .78f;

    public ImageTexture Base, BaseHalf, Prov, Tint, Info, Own, FogDist, Cloud, Water, Climate;
    Image _tintImg, _infoImg, _ownImg, _fogImg, _climateImg;
    byte[] _tint = Array.Empty<byte>(), _info = Array.Empty<byte>(), _own = Array.Empty<byte>(), _climate = Array.Empty<byte>();
    int _pw, _ph;
    /// <summary>Last uploaded per-province bytes (read-only for ProvinceTransitions: the look a capture starts from).</summary>
    internal byte[] TintData => _tint;
    internal byte[] InfoData => _info;
    internal byte[] OwnData => _own;

    public void Build(WorldData w, FogField fog)
    {
        var colour = TerrainPolish.Apply(w);
        Base = ImageTexture.CreateFromImage(Image.CreateFromData(w.W, w.H, false, Image.Format.Rgba8, colour));
        BaseHalf = ImageTexture.CreateFromImage(Image.CreateFromData(w.W / 2, (w.H + 1) / 2, false, Image.Format.Rgba8, HalfSize(w, colour)));
        Water = ImageTexture.CreateFromImage(Image.CreateFromData(w.W, w.H, false, Image.Format.R8, WaterAnim(w)));
        var ids = new byte[w.N * 2];
        for (int i = 0; i < w.N; i++) { int p = w.Prov[i]; ids[i * 2] = (byte)(p & 255); ids[i * 2 + 1] = (byte)(p >> 8); }
        Prov = ImageTexture.CreateFromImage(Image.CreateFromData(w.W, w.H, false, Image.Format.Rg8, ids));

        _pw = PDataW; _ph = Math.Max(1, (w.P + PDataW - 1) / PDataW);
        int n = _pw * _ph * 4;
        _tint = new byte[n]; _info = new byte[n]; _own = new byte[n];
        _tintImg = Image.CreateFromData(_pw, _ph, false, Image.Format.Rgba8, _tint); Tint = ImageTexture.CreateFromImage(_tintImg);
        _infoImg = Image.CreateFromData(_pw, _ph, false, Image.Format.Rgba8, _info); Info = ImageTexture.CreateFromImage(_infoImg);
        _ownImg = Image.CreateFromData(_pw, _ph, false, Image.Format.Rgba8, _own); Own = ImageTexture.CreateFromImage(_ownImg);
        _climate = new byte[n];
        _climateImg = Image.CreateFromData(_pw, _ph, false, Image.Format.Rgba8, _climate); Climate = ImageTexture.CreateFromImage(_climateImg);

        _fogImg = Image.CreateFromData(w.W, w.H, false, Image.Format.R8, fog.Dist);
        FogDist = ImageTexture.CreateFromImage(_fogImg);
        Cloud = ImageTexture.CreateFromImage(Image.CreateFromData(fog.CloudW, fog.CloudH, false, Image.Format.Rg8, fog.Cloud));
    }

    /// <summary>Terrain colour averaged over 2×2 blocks, for the ×½ atlas (nearest sampling of the full texture would shimmer).</summary>
    static byte[] HalfSize(WorldData w, byte[] src)
    {
        int hw = w.W / 2, hh = (w.H + 1) / 2;
        var dst = new byte[hw * hh * 4];
        System.Threading.Tasks.Parallel.For(0, hh, y =>
        {
            int r0 = 2 * y * w.W * 4, r1 = Math.Min(2 * y + 1, w.H - 1) * w.W * 4;
            for (int x = 0; x < hw; x++)
                for (int c = 0, a = x * 8, o = (y * hw + x) * 4; c < 4; c++)
                    dst[o + c] = (byte)((src[r0 + a + c] + src[r0 + a + 4 + c] + src[r1 + a + c] + src[r1 + a + 4 + c] + 2) >> 2);
        });
        return dst;
    }

    /// <summary>
    /// Per water pixel (ART_BIBLE §5): bits 0–3 phase of its 3×3 cluster (hash), bits 4–5 distance to the shore (1, 2;
    /// 0 = open water), bit 6 a sparkle pixel. The shader cycles foam, shallow shimmer and sparse glints from it.
    /// </summary>
    static byte[] WaterAnim(WorldData w)
    {
        var d = new byte[w.N];
        System.Threading.Tasks.Parallel.For(0, w.H, y =>
        {
            for (int x = 0; x < w.W; x++)
            {
                int i = y * w.W + x;
                if (w.Land[i] != 0) continue;
                int shore = 0;
                for (int r = 1; r <= 2 && shore == 0; r++)
                    if (IsLand(w, x - r, y) || IsLand(w, x + r, y) || IsLand(w, x, y - r) || IsLand(w, x, y + r)) shore = r;
                int cy = y / 3, cx = (x + (cy & 1) * 2) / 3;
                uint h = Hash(cx, cy, w.Seed), hp = Hash(x, y, w.Seed + 3);
                d[i] = (byte)((h & 15) | (uint)(shore << 4) | ((hp % 11 == 0) ? 64u : 0u));
            }
        });
        return d;
    }

    static bool IsLand(WorldData w, int x, int y)
    {
        if (y < 0 || y >= w.H) return false;
        if (x < 0) x += w.W; else if (x >= w.W) x -= w.W;
        return w.Land[y * w.W + x] != 0;
    }

    static uint Hash(int x, int y, int seed)
    {
        uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 1442695041u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    public void UploadFog(WorldData w, FogField fog)
    {
        _fogImg.SetData(w.W, w.H, false, Image.Format.R8, fog.Dist);
        FogDist.Update(_fogImg);
    }

    /// <summary>The climate of every province (Sim/Climate.Look: drying, green desert, frost, thaw) for the terrain shader.</summary>
    public void UpdateClimate(WorldData w, GameState s)
    {
        for (int p = 0; p < w.P; p++)
        {
            var (dry, green, frost, thaw) = Sim.Climate.Look(w, s, p);
            int k = p * 4;
            _climate[k] = dry; _climate[k + 1] = green; _climate[k + 2] = frost; _climate[k + 3] = thaw;
        }
        Upload(_climateImg, _climate, Climate);
    }

    /// <summary>Recompute every per-province texel for the current mode / ownership / fog and upload (P≈6k: trivial).</summary>
    public void UpdateProvinces(WorldData w, GameState s, MapMode mode)
    {
        bool fogOn = s.FogEnabled;
        int nations = Game.I.Nations.Length;
        for (int p = 0; p < w.P; p++)
        {
            int k = p * 4, o = s.VisibleOwner(p);
            bool land = w.PLand[p] == 1;
            MapPalette.ModeTint(mode, w, s, p, out var tint, out float tintA, out float desat, out float dim);
            byte fog = s.Fog[p];
            if (fogOn && fog == 1) { desat = MathF.Max(desat, land ? StaleDesat : .3f); dim *= land ? StaleDim : .85f; }
            _tint[k] = tint.R; _tint[k + 1] = tint.G; _tint[k + 2] = tint.B; _tint[k + 3] = ToByte(tintA);
            _info[k] = ToByte(desat); _info[k + 1] = ToByte(dim); _info[k + 2] = (byte)(fog == 0 ? 0 : fog == 1 ? 128 : 255); _info[k + 3] = land ? (byte)255 : (byte)0;
            if (o >= 0 && o < nations)
            {
                var b = MapPalette.BorderColor(o);
                _own[k] = b.R; _own[k + 1] = b.G; _own[k + 2] = b.B; _own[k + 3] = (byte)(o + 1);
            }
            else { _own[k] = _own[k + 1] = _own[k + 2] = 0; _own[k + 3] = 0; }
        }
        Upload(_tintImg, _tint, Tint); Upload(_infoImg, _info, Info); Upload(_ownImg, _own, Own);
    }

    void Upload(Image img, byte[] data, ImageTexture tex)
    {
        img.SetData(_pw, _ph, false, Image.Format.Rgba8, data);
        tex.Update(img);
    }

    static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);

    public void Bind(ShaderMaterial m, WorldData w)
    {
        m.SetShaderParameter("prov_tex", Prov);
        m.SetShaderParameter("pinfo_tex", Info);
        m.SetShaderParameter("fogd_tex", FogDist);
        m.SetShaderParameter("cloud_tex", Cloud);
        m.SetShaderParameter("world_size", new Vector2(w.W, w.H));
        m.SetShaderParameter("pdata_w", PDataW);
    }
}
