using PaxPixelia.Core.Nations;

namespace PaxPixelia.Core;

/// <summary>
/// Everything chosen before a game starts (front-end «Новая игра» / «Народ», or CLI). Built by the front-end,
/// consumed by Game.I.NewGame. Contract from docs/design/MAIN_MENU.md §2.6.
/// </summary>
public sealed record GameSetup(
    int Seed, string SeedText,        // SeedText — as the player typed it (for display and last_setup)
    int NationCount,                  // 2..16 including the player
    bool Fog,                         // false = the whole map is open (the old --nofog)
    int PacePermille,                 // game length preset relative to «Обычная» (25 h at speed 3) = 1000; Быстрая ≈480, Эпическая ≈1600
    bool StartPaused,                 // true when started from the menu, CLI: Cli.Has("pause")
    NationDesign Player)              // null = the default player nation (Data.Nations[0])
{
    public const int PaceQuick = 480, PaceNormal = 1000, PaceEpic = 1600;
    public static GameSetup Default(int seed) => new(seed, seed.ToString(), 16, true, PaceNormal, false, null);
}
