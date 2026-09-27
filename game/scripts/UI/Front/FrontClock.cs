using System.Collections.Generic;
using Godot;
using PaxPixelia.Core;

namespace PaxPixelia.UI.Front;

/// <summary>
/// Background clock of the front-end (MAIN_MENU.md §2.4). Front shaders read the uniform <c>t</c> from here instead
/// of TIME, so a screenshot with <c>--front-t=SEC</c> is deterministic and «меньше анимации» is one flag.
/// FrontShell (and later the chapter card) calls <see cref="Tick"/> once per frame.
/// </summary>
public static class FrontClock
{
    public static double T;
    public static bool Frozen;
    public static bool Reduced;

    static readonly List<ShaderMaterial> _materials = new();
    static readonly StringName TParam = "t";   // cached: SetShaderParameter runs for every material every frame

    /// <summary>--front-t=SEC freezes the clock at SEC; --no-motion turns on «меньше анимации».</summary>
    public static void ApplyCli()
    {
        if (Cli.Has("front-t")) { T = Cli.Float("front-t", 0); Frozen = true; }
        if (Cli.Has("no-motion")) Reduced = true;
    }

    public static void Register(ShaderMaterial m)
    {
        if (!_materials.Contains(m)) _materials.Add(m);
        m.SetShaderParameter(TParam, (float)T);
    }

    public static void Unregister(ShaderMaterial m) => _materials.Remove(m);

    public static void Tick(double delta)
    {
        if (!Frozen) T += delta;
        float t = (float)T;
        for (int i = _materials.Count - 1; i >= 0; i--)
        {
            if (GodotObject.IsInstanceValid(_materials[i])) _materials[i].SetShaderParameter(TParam, t);
            else _materials.RemoveAt(i);
        }
    }
}
