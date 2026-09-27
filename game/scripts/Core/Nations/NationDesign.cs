using PaxPixelia.Core.Flags;

namespace PaxPixelia.Core.Nations;

/// <summary>The player's nation as designed in the «Народ» screen (stored in «Мои народы»). Contract: MAIN_MENU.md §2.6.</summary>
public sealed record NationDesign(string Id, string Name, byte R, byte G, byte B, FlagSpec Flag, byte Culture, long LastUsedUnix)
{
    public (byte R, byte G, byte B) Rgb => (R, G, B);
}
