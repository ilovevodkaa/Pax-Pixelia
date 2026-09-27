using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using static PaxPixelia.Tests.T;

namespace PaxPixelia.Tests;

/// <summary>
/// Determinism lint over scripts/Sim (the rules): no clocks, no System.Random, no CRT maths, no hash-order iteration,
/// no Godot, no «local player» outside the view file, and no floating point unless a line says `// pax-allow`
/// (or a file opts out of the floating-point rules with `// pax-allow-file:` and a reason). Godot glue is skipped.
/// </summary>
public static class LintTests
{
    static readonly string[] GodotGlue = { "GameActions.cs", "SimDriver.cs", "SimDebugOverlay.cs" };

    static readonly (string rule, Regex re)[] Always =
    {
        ("System.Random", new Regex(@"\bRandom\b")),
        ("wall clock", new Regex(@"\bDateTime\b|\bEnvironment\.TickCount|\bStopwatch\b")),
        ("string/object hash codes", new Regex(@"\.GetHashCode\(")),
        ("threads", new Regex(@"\bParallel\.|\bTask\.Run\b|\bThreadPool\b")),
        ("Godot", new Regex(@"\busing Godot\b|\bGodot\.")),
        ("hash-order collections", new Regex(@"\bDictionary<|\bHashSet<")),
    };

    static readonly (string rule, Regex re)[] FloatingPoint =
    {
        ("float/double", new Regex(@"\bfloat\b|\bdouble\b|\bdecimal\b|\bSingle\b|\bDouble\b")),
        ("float literal", new Regex(@"(?<![\w.])\d+\.\d+[fFdDmM]?\b|(?<![\w.])\d+[fFdD]\b")),
        ("CRT maths", new Regex(@"\bMath\.(Sin|Cos|Tan|Asin|Acos|Atan2?|Exp|Log2?|Log10|Pow|Sqrt|Cbrt|Round|Floor|Ceiling)\b|\bMathF\.")),
    };

    public static void Run()
    {
        Section("determinism lint (scripts/Sim)");
        var dir = Path.Combine(GameDir(), "scripts", "Sim");
        if (!Check(Directory.Exists(dir), $"Sim sources found at {dir}")) return;
        var problems = new List<string>();
        int files = 0;
        foreach (var path in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (GodotGlue.Contains(name)) continue;
            files++;
            var lines = File.ReadAllLines(path);
            bool floatFile = lines.Any(l => l.TrimStart().StartsWith("// pax-allow-file:"));
            for (int i = 0; i < lines.Length; i++)
            {
                var raw = lines[i];
                if (raw.Contains("// pax-allow")) continue;
                var code = StripComment(raw);
                if (code.Trim().Length == 0) continue;
                foreach (var (rule, re) in Always)
                    if (re.IsMatch(code)) problems.Add($"{name}:{i + 1} {rule}: {raw.Trim()}");
                if (!floatFile)
                    foreach (var (rule, re) in FloatingPoint)
                        if (re.IsMatch(code)) problems.Add($"{name}:{i + 1} {rule}: {raw.Trim()}");
                if (name != "GameState.Sim.cs" && Regex.IsMatch(code, @"\bLocalPlayer\b")) problems.Add($"{name}:{i + 1} LocalPlayer outside the view: {raw.Trim()}");
            }
        }
        foreach (var p in problems.Take(20)) Console.WriteLine("   lint " + p);
        Check(problems.Count == 0, $"{files} rule files, {problems.Count} violations");
        Check(Always.All(r => r.re.IsMatch("x") == false) && FloatingPoint[0].re.IsMatch("float x") && FloatingPoint[2].re.IsMatch("Math.Pow(a, b)") && Always[0].re.IsMatch("new System.Random(1)"),
            "the lint itself catches float, Math.Pow and System.Random");
    }

    /// <summary>Code without its // comment (string literals in Sim never contain //).</summary>
    static string StripComment(string line)
    {
        int k = line.IndexOf("//", StringComparison.Ordinal);
        return k < 0 ? line : line[..k];
    }

    internal static string GameDir() =>
        typeof(LintTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "GameDir").Value;
}
