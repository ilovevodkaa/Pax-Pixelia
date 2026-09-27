using System;
using PaxPixelia.Core.Flags;

namespace PaxPixelia.Core.Nations;

/// <summary>
/// The nations of one game (MAIN_MENU §2.6, hook 5), built before the simulation starts and handed to it as data
/// (the host will send the finished roster, so the float colour maths here is fine). Slot 0 is the player's design;
/// bots are Data.Nations[1..count-1]. A bot whose colour is too close to the player's (ΔE OKLab &lt; 0.10) takes the
/// first free colour of the 24-colour palette (NationPalette); a bot with the player's name takes the unused default name.
/// Bot flags are derived from the seed (FlagSpec.ForBot), so every client and the chapter card draw the same ones.
/// </summary>
public static class NationRoster
{
    public const double MinColorDistance = 0.10;

    public static Data.Nation[] Build(GameSetup s)
    {
        int count = Math.Clamp(s.NationCount, 2, Data.Nations.Length);
        var roster = new Data.Nation[count];
        var def = Data.Nations[0];
        roster[0] = s.Player is { } d
            ? new Data.Nation(d.Name, def.Gov, d.R, d.G, d.B, NationNames.Adjective(d.Name), def.Religion, d.Flag, d.Culture)
            : def with { Flag = FlagSpec.ForBot(s.Seed, 0) };
        var me = roster[0];
        for (int n = 1; n < count; n++)
        {
            var bot = Data.Nations[n] with { Flag = FlagSpec.ForBot(s.Seed, n) };
            if (string.Equals(bot.Name, me.Name, StringComparison.OrdinalIgnoreCase) && s.Player != null)
                bot = bot with { Name = def.Name, CultureAdj = def.CultureAdj };
            roster[n] = bot;
        }
        if (s.Player != null) ResolveColors(roster);
        return roster;
    }

    /// <summary>Bots too close to the player's colour move to the first palette colour nobody uses and the player can tell apart.</summary>
    static void ResolveColors(Data.Nation[] roster)
    {
        var me = roster[0];
        for (int n = 1; n < roster.Length; n++)
        {
            if (DeltaE(roster[n], me.R, me.G, me.B) >= MinColorDistance) continue;
            foreach (var (r, g, b) in NationPalette.Colors)
            {
                if (DeltaE(me, r, g, b) < MinColorDistance || Used(roster, r, g, b)) continue;
                roster[n] = roster[n] with { R = r, G = g, B = b };
                break;
            }
        }
    }

    static bool Used(Data.Nation[] roster, byte r, byte g, byte b)
    {
        foreach (var x in roster) if (x.R == r && x.G == g && x.B == b) return true;
        return false;
    }

    public static double DeltaE(Data.Nation a, byte r, byte g, byte b) => ColorMath.DeltaEOklab((a.R, a.G, a.B), (r, g, b));
}
