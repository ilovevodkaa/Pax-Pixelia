using System.Text;

namespace PaxPixelia.Core.Flags;

/// <summary>
/// Human-shareable flag code «F7Q2-M9KD»: the spec's fields packed into 35 bits, 7 Crockford base32 digits
/// + 1 check digit. Crockford's alphabet has no I, L, O, U, and reading maps them to 1/1/0/V, so a code read
/// aloud or retyped from a screenshot still parses.
/// </summary>
public static class FlagCode
{
    const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Encode(FlagSpec f)
    {
        f = f.Sanitized();
        ulong v = f.Division | (ulong)f.T1 << 4 | (ulong)f.T2 << 8 | (ulong)f.T3 << 12 | (ulong)f.Charge << 16
                  | (ulong)f.Pos << 20 | (ulong)f.ChargeTinct << 22 | (ulong)f.Reserved << 26;
        var digits = new int[8];
        for (int i = 6; i >= 0; i--) { digits[i] = (int)(v & 31); v >>= 5; }
        digits[7] = Check(digits);
        var sb = new StringBuilder(9);
        for (int i = 0; i < 8; i++) { if (i == 4) sb.Append('-'); sb.Append(Alphabet[digits[i]]); }
        return sb.ToString();
    }

    public static bool TryDecode(string code, out FlagSpec spec)
    {
        spec = FlagSpec.Default;
        if (code == null) return false;
        var digits = new int[8];
        int n = 0;
        foreach (char raw in code.ToUpperInvariant())
        {
            if (raw is '-' or ' ') continue;
            char c = raw switch { 'I' or 'L' => '1', 'O' => '0', 'U' => 'V', _ => raw };
            int d = Alphabet.IndexOf(c);
            if (d < 0 || n == 8) return false;
            digits[n++] = d;
        }
        if (n != 8 || digits[7] != Check(digits)) return false;
        ulong v = 0;
        for (int i = 0; i < 7; i++) v = v << 5 | (uint)digits[i];
        spec = new FlagSpec((byte)(v & 15), (byte)(v >> 4 & 15), (byte)(v >> 8 & 15), (byte)(v >> 12 & 15), (byte)(v >> 16 & 15),
            (byte)(v >> 20 & 3), (byte)(v >> 22 & 15), (byte)(v >> 26 & 255)).Sanitized();
        return true;
    }

    /// <summary>Position-weighted sum: catches any single wrong digit and most swaps of neighbours.</summary>
    static int Check(int[] d)
    {
        int s = 0;
        for (int i = 0; i < 7; i++) s += d[i] * (i + 3);
        return s % 31;
    }
}
