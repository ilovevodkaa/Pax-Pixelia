using Godot;

namespace PaxPixelia.Core.Audio;

/// <summary>
/// Hover and click for buttons that do not get the kit's press motion (PixelKit.AddPressMotion already sounds):
/// the HUD's icon, menu and small buttons (UI.Ui.Button). A toggle button clicks «on» / «off» instead.
/// What the press does may sound louder in the same frame (a mode tab, a book, a pause latch) — Sfx keeps that one.
/// </summary>
public static class UiSounds
{
    public static void Attach(BaseButton b)
    {
        b.MouseEntered += () => { if (!b.Disabled) Sfx.I?.Play("hover", (float)GD.RandRange(.97, 1.03), 0f, "ui.hover"); };
        if (b.ToggleMode) b.Toggled += on => Sfx.I?.Play(on ? "toggle_on" : "toggle_off", 1f, 0f, "ui.toggle");
        else b.Pressed += () => Sfx.I?.Play("click", (float)GD.RandRange(.96, 1.04), 0f, "ui.click");
    }
}
