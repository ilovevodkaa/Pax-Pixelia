namespace PaxPixelia.Core.Flags;

/// <summary>
/// A flag as 8 bytes (ART_BIBLE §12): field division + up to 3 tinctures + a charge and its position.
/// Tinctures 0..8 are heraldic colours, 9 = the nation colour. Contract: MAIN_MENU.md §2.6.
/// </summary>
public readonly record struct FlagSpec(byte Division, byte T1, byte T2, byte T3, byte Charge, byte Pos, byte ChargeTinct, byte Reserved);
