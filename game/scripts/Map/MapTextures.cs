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

    public ImageTexture Base, Prov, Tint, Info, Own, FogDist, Cloud;
    Image _tintImg, _infoImg, _ownImg, _fogImg;
    byte[] _tint = Array.Empty<byte>(), _info = Array.Empty<byte>(), _own = Array.Empty<byte>();
    int _pw, _ph;

    public void Build(WorldData w, FogField fog)
    {
        Base = ImageTexture.CreateFromImage(Image.CreateFromData(w.W, w.H, false, Image.Format.Rgba8, w.BaseColor));
        var ids = new byte[w.N * 2];
        for (int i = 0; i < w.N; i++) { int p = w.Prov[i]; ids[i * 2] = (byte)(p & 255); ids[i * 2 + 1] = (byte)(p >> 8); }
        Prov = ImageTexture.CreateFromImage(Image.CreateFromData(w.W, w.H, false, Image.Format.Rg8, ids));

        _pw = PDataW; _ph = Math.Max(1, (w.P + PDataW - 1) / PDataW);
        int n = _pw * _ph * 4;
        _tint = new byte[n]; _info = new byte[n]; _own = new byte[n];
        _tintImg = Image.CreateFromData(_pw, _ph, false, Image.Format.Rgba8, _tint); Tint = ImageTexture.CreateFromImage(_tintImg);
        _infoImg = Image.CreateFromData(_pw, _ph, false, Image.Format.Rgba8, _info); Info = ImageTexture.CreateFromImage(_infoImg);
        _ownImg = Image.CreateFromData(_pw, _ph, false, Image.Format.Rgba8, _own); Own = ImageTexture.CreateFromImage(_ownImg);

        _fogImg = Image.CreateFromData(w.W, w.H, false, Image.Format.R8, fog.Dist);
        FogDist = ImageTexture.CreateFromImage(_fogImg);
        Cloud = ImageTexture.CreateFromImage(Image.CreateFromData(fog.CloudW, fog.CloudH, false, Image.Format.Rg8, fog.Cloud));
    }

    public void UploadFog(WorldData w, FogField fog)
    {
        _fogImg.SetData(w.W, w.H, false, Image.Format.R8, fog.Dist);
        FogDist.Update(_fogImg);
    }

    /// <summary>Recompute every per-province texel for the current mode / ownership / fog and upload (P≈6k: trivial).</summary>
    public void UpdateProvinces(WorldData w, GameState s, MapMode mode)
    {
        bool fogOn = s.FogEnabled;
        var nations = Data.Nations;
        for (int p = 0; p < w.P; p++)
        {
            int k = p * 4, o = s.Owner[p];
            bool land = w.PLand[p] == 1;
            MapPalette.ModeTint(mode, w, s, p, out var tint, out float tintA, out float desat, out float dim);
            byte fog = s.Fog[p];
            if (fogOn && fog == 1) { desat = MathF.Max(desat, land ? .6f : .3f); dim *= land ? .6f : .8f; }
            _tint[k] = tint.R; _tint[k + 1] = tint.G; _tint[k + 2] = tint.B; _tint[k + 3] = ToByte(tintA);
            _info[k] = ToByte(desat); _info[k + 1] = ToByte(dim); _info[k + 2] = (byte)(fog == 0 ? 0 : fog == 1 ? 128 : 255); _info[k + 3] = land ? (byte)255 : (byte)0;
            if (o >= 0 && o < nations.Length)
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
