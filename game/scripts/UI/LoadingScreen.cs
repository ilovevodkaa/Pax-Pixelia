using Godot;

namespace PaxPixelia.UI;

/// <summary>
/// #loading — drafting-paper backdrop (radial light + 16/80px grid) with a framed plate: swaying compass rose,
/// «Глобальная стратегия», «Pax Pixelia», ornament, generation status and an indeterminate bar.
/// Shown while Game.NewWorld runs; fades out on WorldReady.
/// </summary>
public partial class LoadingScreen : Control
{
    readonly Label _status;
    readonly Rose _rose = new();
    readonly IndeterminateBar _bar = new();
    GradientTexture2D _light;
    Tween _fade;

    public LoadingScreen()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsPreset(LayoutPreset.FullRect);
        _light = new GradientTexture2D
        {
            Width = 256, Height = 256, Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(.5f, .46f), FillTo = new Vector2(.5f + .7f, .46f),
            Gradient = new Gradient { Offsets = new[] { 0f, .52f, 1f }, Colors = new[] { Pal.Hex(0xfcfcfd), Pal.Hex(0xeef0f2), Pal.Hex(0xd6dade) } },
        };

        var kick = Ui.Text("Глобальная стратегия", "Kick"); kick.Uppercase = true;
        var big = Ui.Text("Pax Pixelia", "Big");
        _status = Ui.Text("Генерация мира…", "Mu");
        _status.HorizontalAlignment = HorizontalAlignment.Center;
        _status.CustomMinimumSize = new Vector2(0, 18);
        foreach (var l in new[] { kick, big }) l.HorizontalAlignment = HorizontalAlignment.Center;

        var col = Ui.VBox(0,
            Center(_rose), Ui.Gap(0, 22),
            kick, Ui.Gap(0, 10),
            big,
            Ui.Gap(0, 18), Center(new Ornament()), Ui.Gap(0, 15),
            _status, Ui.Gap(0, 15),
            Center(_bar));
        var plate = Ui.Panel(new Box().Fill(new Color(251 / 255f, 251 / 255f, 252 / 255f, .9f)).Border(Pal.Ln2).Radius(St.RCard)
            .Inner(6, Pal.Ln).Shadow(Pal.Shade(.10f), 36, 16).Shadow(Pal.Shade(.08f), 3, 1).Pad(84, 40, 84, 38), col);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        center.AddChild(plate);
        AddChild(center);
    }

    static Control Center(Control c) { c.SizeFlagsHorizontal = SizeFlags.ShrinkCenter; return c; }

    public void SetStatus(string text) => _status.Text = text;

    public void ShowNow()
    {
        _fade?.Kill();
        Modulate = Colors.White;
        Visible = true;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public void FadeOut()
    {
        if (!Visible) return;
        MouseFilter = MouseFilterEnum.Ignore;
        _fade?.Kill();
        _fade = CreateTween();
        _fade.TweenProperty(this, "modulate:a", 0f, .35).SetEase(Tween.EaseType.In);
        _fade.TweenCallback(Callable.From(() => Visible = false));
    }

    public override void _Draw()
    {
        DrawTextureRect(_light, new Rect2(Vector2.Zero, Size), false);
        var minor = new Color(40 / 255f, 48 / 255f, 58 / 255f, .022f);
        var major = new Color(40 / 255f, 48 / 255f, 58 / 255f, .05f);
        for (float x = 0; x < Size.X; x += 16) DrawRect(new Rect2(x, 0, 1, Size.Y), (int)x % 80 == 0 ? major : minor);
        for (float y = 0; y < Size.Y; y += 16) DrawRect(new Rect2(0, y, Size.X, 1), (int)y % 80 == 0 ? major : minor);
    }

    /// <summary>The compass rose: a static ring and a needle swaying −8°…+10° (6 s, eased).</summary>
    sealed partial class Rose : Control
    {
        readonly Texture2D _ring = Icons.Art("rose_ring.svg", 2f), _needle = Icons.Art("rose_needle.svg", 2f);
        double _t;

        public Rose()
        {
            CustomMinimumSize = new Vector2(96, 96);
            MouseFilter = MouseFilterEnum.Ignore;
            TextureFilter = TextureFilterEnum.LinearWithMipmaps;
        }

        public override void _Process(double delta) { if (IsVisibleInTree()) { _t += delta; QueueRedraw(); } }

        public override void _Draw()
        {
            DrawTextureRect(_ring, new Rect2(0, 0, 96, 96), false);
            float ph = (float)(_t % 6.0 / 6.0);
            float u = ph < .5f ? ph * 2 : 2 - ph * 2;
            float e = u * u * (3 - 2 * u);
            float ang = Mathf.DegToRad(Mathf.Lerp(-8, 10, e));
            DrawSetTransform(new Vector2(48, 48), ang);
            DrawTextureRect(_needle, new Rect2(-48, -48, 96, 96), false);
            DrawSetTransform(Vector2.Zero);
        }
    }

    /// <summary>Two double hairlines around a small graphite diamond.</summary>
    sealed partial class Ornament : Control
    {
        public Ornament() { CustomMinimumSize = new Vector2(110 * 2 + 20 + 8, 8); MouseFilter = MouseFilterEnum.Ignore; }

        public override void _Draw()
        {
            float cy = 4;
            foreach (float x in new[] { 0f, 110 + 20 + 8 })
            {
                DrawRect(new Rect2(x, cy - 1, 110, 1), Pal.Ln3);
                DrawRect(new Rect2(x, cy + 1, 110, 1), Pal.Ln2);
            }
            var c = new Vector2(110 + 14, cy + .5f);
            DrawColoredPolygon(new[] { c + new Vector2(0, -4), c + new Vector2(4, 0), c + new Vector2(0, 4), c + new Vector2(-4, 0) }, Pal.Tx);
        }
    }

    /// <summary>3px track with a 36% graphite segment sweeping across (1.1 s, ease-in-out).</summary>
    sealed partial class IndeterminateBar : Control
    {
        double _t;
        public IndeterminateBar() { CustomMinimumSize = new Vector2(250, 3); MouseFilter = MouseFilterEnum.Ignore; }
        public override void _Process(double delta) { if (IsVisibleInTree()) { _t += delta; QueueRedraw(); } }

        public override void _Draw()
        {
            DrawRect(new Rect2(0, 0, 250, 3), Pal.Track);
            float ph = (float)(_t % 1.1 / 1.1), e = ph < .5f ? 2 * ph * ph : 1 - 2 * (1 - ph) * (1 - ph);
            float seg = 250 * .36f, x = Mathf.Lerp(-seg, 250, e);
            float a = Mathf.Max(0, x), b = Mathf.Min(250, x + seg);
            if (b > a) DrawRect(new Rect2(a, 0, b - a, 3), Pal.G2);
        }
    }
}
