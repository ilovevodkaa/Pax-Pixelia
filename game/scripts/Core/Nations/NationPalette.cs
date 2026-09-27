namespace PaxPixelia.Core.Nations;

/// <summary>
/// The 24 nation colours offered in «Народ»: the 16 of Data.Nations + 8 extra picked offline for the largest minimal
/// ΔE_OKLab to all others within the palette's own range (never gray: gray is the UI's). NationRoster gives bots a
/// free colour from it when the player took theirs.
/// </summary>
public static class NationPalette
{
    static readonly (byte R, byte G, byte B)[] Extra =
    {
        (186, 150, 246), (30, 102, 42), (246, 126, 150), (66, 186, 246), (90, 66, 162), (138, 126, 222), (114, 102, 30), (30, 90, 138),
    };

    public static readonly (byte R, byte G, byte B)[] Colors = Build();

    static (byte R, byte G, byte B)[] Build()
    {
        var all = new (byte, byte, byte)[Data.Nations.Length + Extra.Length];
        for (int i = 0; i < Data.Nations.Length; i++) all[i] = (Data.Nations[i].R, Data.Nations[i].G, Data.Nations[i].B);
        Extra.CopyTo(all, Data.Nations.Length);
        return all;
    }

    /// <summary>Index of the palette colour closest to <paramref name="c"/> (for designs saved before a palette change).</summary>
    public static int Nearest((byte R, byte G, byte B) c)
    {
        int best = 0; double bd = double.MaxValue;
        for (int i = 0; i < Colors.Length; i++)
        {
            double d = ColorMath.DeltaEOklab(c, Colors[i]);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }
}
