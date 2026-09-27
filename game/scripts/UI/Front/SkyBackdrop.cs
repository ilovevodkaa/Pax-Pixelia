using System.Collections.Generic;
using Godot;

namespace PaxPixelia.UI.Front;

/// <summary>
/// Full-screen «Горизонт эпох» (shaders/front/era_sky.gdshader): night sky, the planet of <see cref="NextWorld"/>
/// and the era skyline. The virtual pixel is ps = max(2, floor(H / 225)) screen px. The planet dissolves into a
/// grey «forming» pattern while a world is generated and dithers back in (8 × 60 ms) when it is ready.
/// </summary>
public partial class SkyBackdrop : ColorRect
{
    public const string SkylinePath = "res://assets/front/skyline_eras.png";
    const int MaxEmitters = 8;
    const float RevealStep = .06f;

    ShaderMaterial _mat;
    int _reveal;         // 0..8
    int _revealTarget;
    double _revealIn;

    public int PixelSize { get; private set; } = 4;
    public ShaderMaterial Mat => _mat;

    /// <summary>Intro: the horizon sits this many vp lower.</summary>
    public float Rise { set => _mat.SetShaderParameter("rise", value); }
    /// <summary>−1 = the whole horizon; 0..10 = only that era, centred (chapter card).</summary>
    public int SoloEra { set => _mat.SetShaderParameter("solo_era", value); }
    public bool Motion { set => _mat.SetShaderParameter("motion", value ? 1f : 0f); }
    public bool ShowPlanet { set => _mat.SetShaderParameter("show_planet", value ? 1f : 0f); }
    /// <summary>Screen px of flat ground under the skyline (rounded up to whole vp); the title's captions sit on it.</summary>
    public int GroundPx { get => _groundPx; set { _groundPx = value; if (_mat != null) OnResized(); } }
    int _groundPx = 40;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/front/era_sky.gdshader") };
        Material = _mat;
        FrontClock.Register(_mat);
        _mat.SetShaderParameter("motion", FrontClock.Reduced ? 0f : 1f);
        LoadSkyline();
        GetViewport().SizeChanged += OnResized;
        OnResized();
        NextWorld.Started += OnWorldStarted;
        NextWorld.Ready += OnWorldReady;
        if (NextWorld.IsReady) { OnWorldReady(); _reveal = _revealTarget; ApplyReveal(); }
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= OnResized;
        NextWorld.Started -= OnWorldStarted;
        NextWorld.Ready -= OnWorldReady;
        FrontClock.Unregister(_mat);
    }

    public override void _Process(double delta)
    {
        if (_reveal == _revealTarget) return;
        _revealIn -= delta;
        if (_revealIn > 0) return;
        _revealIn = RevealStep;
        _reveal += _reveal < _revealTarget ? 1 : -1;
        ApplyReveal();
    }

    void OnWorldStarted() => _revealTarget = 0;

    void OnWorldReady()
    {
        _mat.SetShaderParameter("planet_map", NextWorld.PlanetMap);
        _revealTarget = 8;
        _reveal = 0;   // a new map: dither in from the «forming» pattern
        ApplyReveal();
    }

    void ApplyReveal() => _mat.SetShaderParameter("planet_reveal", _reveal / 8f);

    void OnResized()
    {
        var size = GetViewportRect().Size;
        PixelSize = Mathf.Max(2, (int)(size.Y / 225));
        _mat.SetShaderParameter("screen_size", size);
        _mat.SetShaderParameter("ps", (float)PixelSize);
        _mat.SetShaderParameter("ground_vp", Mathf.Max(3f, Mathf.Ceil(_groundPx / (float)PixelSize)));
    }

    /// <summary>The atlas plus the smoke sources and rocket bounds found in it (encoding: menu_skyline.py).</summary>
    void LoadSkyline()
    {
        var tex = FrontAssets.LoadTexture(SkylinePath);
        var img = FrontAssets.LoadImage(SkylinePath);
        if (tex == null || img == null) return;
        _mat.SetShaderParameter("skyline", tex);
        var emitters = new List<Vector4>();
        float rx0 = 1e9f, rx1 = -1, rf0 = 1e9f, rf1 = -1;
        for (int y = 0; y < img.GetHeight(); y++)
        for (int x = 0; x < img.GetWidth(); x++)
        {
            var c = img.GetPixel(x, y);
            int id = Mathf.RoundToInt(c.B8 / 16f), era = c.G8 / 20, fg = img.GetHeight() - 1 - y;
            if (id is >= 2 and <= 6 && emitters.Count < MaxEmitters) emitters.Add(new Vector4(x, fg, id, era));
            if (id == 9 && c.R8 > 0) { rx0 = Mathf.Min(rx0, x); rx1 = Mathf.Max(rx1, x); rf0 = Mathf.Min(rf0, fg); rf1 = Mathf.Max(rf1, fg); }
        }
        var arr = new Vector4[MaxEmitters];
        emitters.CopyTo(arr);
        _mat.SetShaderParameter("emitters", arr);
        _mat.SetShaderParameter("emitter_count", emitters.Count);
        if (rx1 >= 0) _mat.SetShaderParameter("rocket_box", new Vector4(rx0, rx1, rf0, rf1));
    }
}
