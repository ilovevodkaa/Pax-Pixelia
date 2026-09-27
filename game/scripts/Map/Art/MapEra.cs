using System;
using PaxPixelia.Core;

namespace PaxPixelia.Map;

/// <summary>
/// Era of a nation as the map draws it (0 = Первобытная … 10 = Будущее; ART_BIBLE §4: a province is drawn in its
/// owner's era): the sim's per-nation era (GameState.Nat[n].Era). Screenshot switch: --era=mix puts nation n in
/// era n mod 11 (--era=N is the sim's debug jump, so every nation really is in era N).
/// </summary>
internal static class MapEra
{
    static readonly bool Mix = Cli.Str("era") == "mix";

    public static int Of(int nation)
    {
        if (Mix && nation >= 0) return nation % MapAtlas.Eras;
        var s = Game.I?.State;
        if (s == null || (uint)nation >= (uint)s.Nat.Length) return Math.Clamp(Game.I?.EraIndex ?? 1, 0, MapAtlas.Eras - 1);
        return Math.Min((int)s.Nat[nation].Era, MapAtlas.Eras - 1);
    }

    /// <summary>Changes whenever any nation's era changes (eras only grow, so their sum is a key; cheap once per frame).</summary>
    public static int Key(int nations)
    {
        if (Mix) return -1;
        var s = Game.I?.State;
        if (s == null) return 1;
        int sum = 0;
        foreach (var nat in s.Nat) sum += nat.Era;
        return sum * 64 + nations;
    }
}
