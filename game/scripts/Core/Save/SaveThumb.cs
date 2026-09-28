using System;
using System.Threading.Tasks;
using Godot;

namespace PaxPixelia.Core.Save;

/// <summary>
/// The 320×180 save thumbnail: the map as the player sees it (terrain, borders, fog, cities, names), without the
/// interface. A throw-away SubViewport shares the root viewport's World2D, so it draws the map canvas but none of the
/// CanvasLayers (HUD, pause menu, dialogs, the dither curtain); it renders with the next frame and is read back after
/// FramePostDraw. Where nothing can be rendered (headless runs) or no frame may be waited for (the synchronous save at
/// Alt+F4), <see cref="CpuPicture"/> paints the camera's view from the world colours, owner tint and the player's fog.
/// </summary>
public static class SaveThumb
{
    public const int Width = 320, Height = 180;

    /// <summary>The last capture fell back to the CPU picture (tests, logs).</summary>
    public static bool LastWasCpu { get; private set; }

    /// <summary>Render the map without UI (one frame later); the CPU picture when rendering is impossible.</summary>
    public static async Task<Image> CaptureAsync(Node host)
    {
        Image img = null;
        try { img = await RenderMap(host); }
        catch (Exception e) { GD.PushWarning($"save: thumbnail render failed ({e.Message}), drawing it on the CPU"); }
        LastWasCpu = img == null;
        return img ?? CpuPicture();
    }

    /// <summary>The synchronous thumbnail (no frame to wait for): the CPU picture.</summary>
    public static Image CaptureNow()
    {
        LastWasCpu = true;
        return CpuPicture();
    }

    static async Task<Image> RenderMap(Node host)
    {
        if (DisplayServer.GetName() == "headless" || host == null || !host.IsInsideTree()) return null;
        var root = host.GetTree().Root;
        var size = (Vector2I)root.GetVisibleRect().Size;
        if (size.X < 16 || size.Y < 16) return null;
        var vp = new SubViewport
        {
            Name = "SaveThumbViewport",
            Size = size,
            World2D = root.World2D,
            Disable3D = true,
            TransparentBg = false,
            HandleInputLocally = false,
            GuiDisableInput = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
            CanvasItemDefaultTextureFilter = root.CanvasItemDefaultTextureFilter,
            CanvasItemDefaultTextureRepeat = root.CanvasItemDefaultTextureRepeat,
            Snap2DTransformsToPixel = root.Snap2DTransformsToPixel,
            Snap2DVerticesToPixel = root.Snap2DVerticesToPixel,
        };
        host.AddChild(vp);
        vp.CanvasTransform = root.CanvasTransform;   // once in the tree: the world's canvas is attached to it then
        try
        {
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (!GodotObject.IsInstanceValid(vp)) return null;
            var frame = vp.GetTexture()?.GetImage();
            if (frame == null || frame.IsEmpty()) return null;
            return Fit(frame);
        }
        finally
        {
            if (GodotObject.IsInstanceValid(vp)) vp.QueueFree();
        }
    }

    /// <summary>Centre crop to 16:9, then down to 320×180.</summary>
    static Image Fit(Image frame)
    {
        if (frame.IsCompressed()) return null;
        if (frame.GetFormat() != Image.Format.Rgba8 && frame.GetFormat() != Image.Format.Rgb8) frame.Convert(Image.Format.Rgba8);
        int w = frame.GetWidth(), h = frame.GetHeight();
        int cw = w, ch = w * 9 / 16;
        if (ch > h) { ch = h; cw = h * 16 / 9; }
        var crop = frame.GetRegion(new Rect2I((w - cw) / 2, (h - ch) / 2, cw, ch));
        crop.Resize(Width, Height, Image.Interpolation.Lanczos);
        crop.Convert(Image.Format.Rgb8);
        return crop;
    }

    // ------------------------------------------------------------------ CPU picture

    static readonly Color Cloud = Color.FromHtml("#1c1c20");

    /// <summary>The camera's view (or the whole world) sampled from the world colours: owner tint, fog and stale land.</summary>
    public static Image CpuPicture()
    {
        var g = Game.I;
        var w = g?.World; var s = g?.State;
        var img = Image.CreateEmpty(Width, Height, false, Image.Format.Rgb8);
        if (w == null || s == null) { img.Fill(Cloud); return img; }
        var r = g.CameraRect;
        if (r.Size.X <= 0 || r.Size.Y <= 0) r = new Rect2(0, 0, w.W, w.H);
        // keep 16:9 around the camera centre
        var c = r.Position + r.Size / 2;
        float vw = Math.Max(r.Size.X, r.Size.Y * 16 / 9f), vh = vw * 9 / 16f;
        var fog = s.Fog;
        bool fogOn = s.FogEnabled && fog != null;
        var data = new byte[Width * Height * 3];
        var bc = w.BaseColor;
        var nations = g.Nations;
        for (int y = 0; y < Height; y++)
        {
            int wy = Math.Clamp((int)(c.Y - vh / 2 + (y + .5f) * vh / Height), 0, w.H - 1);
            for (int x = 0; x < Width; x++)
            {
                int wx = Mathf.PosMod((int)(c.X - vw / 2 + (x + .5f) * vw / Width), w.W);
                int i = wy * w.W + wx, p = w.Prov[i], o = (y * Width + x) * 3;
                float cr, cg, cb;
                if (fogOn && fog[p] == 0) { cr = Cloud.R * 255; cg = Cloud.G * 255; cb = Cloud.B * 255; }
                else
                {
                    cr = bc[i * 4]; cg = bc[i * 4 + 1]; cb = bc[i * 4 + 2];
                    int owner = s.VisibleOwner(p);
                    if (w.PLand[p] == 1 && owner >= 0 && owner < nations.Length)
                    {
                        var n = nations[owner];
                        cr += (n.R - cr) * .5f; cg += (n.G - cg) * .5f; cb += (n.B - cb) * .5f;
                    }
                    if (fogOn && fog[p] == 1)
                    {
                        float m = (cr + cg + cb) / 3;
                        cr = (cr + (m - cr) * .45f) * .78f; cg = (cg + (m - cg) * .45f) * .78f; cb = (cb + (m - cb) * .45f) * .78f;
                    }
                }
                data[o] = (byte)Math.Clamp(cr, 0, 255); data[o + 1] = (byte)Math.Clamp(cg, 0, 255); data[o + 2] = (byte)Math.Clamp(cb, 0, 255);
            }
        }
        img.SetData(Width, Height, false, Image.Format.Rgb8, data);
        return img;
    }
}
