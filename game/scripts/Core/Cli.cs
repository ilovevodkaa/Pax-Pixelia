using System.Collections.Generic;
using Godot;

namespace PaxPixelia.Core;

/// <summary>
/// Command-line options after "--", e.g.
///   Godot_console.exe --path game -- --seed=1337 --zoom=3 --mode=pol --shot=D:/tmp/a.png --shot-delay=2.5 --quit
/// Used by automated tests/agents to capture screenshots without a human.
/// </summary>
public static class Cli
{
    static Dictionary<string, string> _args;
    static Dictionary<string, string> Args
    {
        get
        {
            if (_args != null) return _args;
            _args = new();
            foreach (var a in OS.GetCmdlineUserArgs())
            {
                var s = a.TrimStart('-'); int eq = s.IndexOf('=');
                if (eq < 0) _args[s] = "1"; else _args[s[..eq]] = s[(eq + 1)..];
            }
            return _args;
        }
    }
    public static bool Has(string k) => Args.ContainsKey(k);
    public static string Str(string k, string def = null) => Args.TryGetValue(k, out var v) ? v : def;
    public static int Int(string k, int def) => Args.TryGetValue(k, out var v) && int.TryParse(v, out var i) ? i : def;
    public static float Float(string k, float def) => Args.TryGetValue(k, out var v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : def;
}
