using System;

namespace PaxPixelia.Core.Flags;

/// <summary>
/// A flag as 8 bytes (ART_BIBLE §12): field division + up to 3 tinctures + a charge and its position.
/// Tinctures 0..8 are heraldic colours, 9 = the nation colour. Contract: MAIN_MENU.md §2.6.
/// Only the spec travels (store, network); every client renders it the same way (<see cref="FlagRender"/>).
/// </summary>
public readonly record struct FlagSpec(byte Division, byte T1, byte T2, byte T3, byte Charge, byte Pos, byte ChargeTinct, byte Reserved)
{
    public const byte Nation = FlagPatterns.NationTincture;

    /// <summary>The default player flag: a nation-colour field with a silver fess and a golden sun.</summary>
    public static FlagSpec Default => new(12, Nation, 1, 0, 1, 0, 0, 0);

    /// <summary>Same spec with every field clamped into its valid range (data from disk, clipboard or the network).</summary>
    public FlagSpec Sanitized() => new(
        Math.Min(Division, (byte)(FlagPatterns.DivisionCount - 1)),
        Math.Min(T1, Nation), Math.Min(T2, Nation), Math.Min(T3, Nation),
        Math.Min(Charge, (byte)FlagPatterns.ChargeCount), Math.Min(Pos, (byte)2), Math.Min(ChargeTinct, Nation), 0);

    /// <summary>Deterministic flag of bot nation <paramref name="n"/> in a game with <paramref name="seed"/> (no floats,
    /// no System.Random: every client derives the same roster).</summary>
    public static FlagSpec ForBot(int seed, int n)
    {
        uint h = Mix((uint)seed * 0x9E3779B1u ^ (uint)(n + 1) * 0x85EBCA77u);
        byte Next(int count) { h = Mix(h + 0x6D2B79F5u); return (byte)(h % (uint)count); }
        byte div = Next(FlagPatterns.DivisionCount);
        // the nation colour always shows: it is how friends recognise each other on the map
        byte a = Nation, b = Next(2) == 0 ? (byte)Next(2) : Next(9), c = Next(9);
        if (b == a) b = 1;
        if (c == b) c = (byte)((c + 1) % 9);
        byte charge = Next(3) == 0 ? (byte)0 : (byte)(1 + Next(FlagPatterns.ChargeCount));
        byte pos = div == 8 ? (byte)1 : div == 9 || Next(4) == 0 ? (byte)2 : (byte)0;
        byte ct = Next(2) == 0 ? (byte)0 : (byte)1;   // gold or silver reads on any field (rule of tincture)
        return new FlagSpec(div, a, b, c, charge, pos, ct, 0);
    }

    static uint Mix(uint x)
    {
        x ^= x >> 16; x *= 0x7FEB352Du;
        x ^= x >> 15; x *= 0x846CA68Bu;
        return x ^ (x >> 16);
    }

    // ---- 8 bytes as 16 hex chars (storage) ----
    public string ToHex() => $"{Division:X2}{T1:X2}{T2:X2}{T3:X2}{Charge:X2}{Pos:X2}{ChargeTinct:X2}{Reserved:X2}";

    public static bool TryParseHex(string s, out FlagSpec spec)
    {
        spec = Default;
        if (s == null || s.Length != 16) return false;
        var b = new byte[8];
        for (int i = 0; i < 8; i++)
            if (!byte.TryParse(s.AsSpan(i * 2, 2), System.Globalization.NumberStyles.HexNumber, null, out b[i])) return false;
        spec = new FlagSpec(b[0], b[1], b[2], b[3], b[4], b[5], b[6], b[7]).Sanitized();
        return true;
    }
}
