using System;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI.Front;

/// <summary>
/// The player settings the front-end shell follows (Core/Settings autoload): UI scale, reduced motion and the change
/// event. Null / no-op when the autoload is absent (a scene run on its own in the editor).
/// </summary>
public static class SettingsBridge
{
    public static float? UiScale => Settings.I?.UiScale;
    public static bool? ReducedMotion => Settings.I?.ReducedMotion;

    /// <summary>Subscribe to Settings.Changed ("section/key"); returns an unsubscribe action.</summary>
    public static Action OnChanged(Action<string> handler)
    {
        var s = Settings.I;
        if (s == null) return () => { };
        s.Changed += handler;
        return () => { if (GodotObject.IsInstanceValid(s)) s.Changed -= handler; };
    }
}
