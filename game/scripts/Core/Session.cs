namespace PaxPixelia.Core;

/// <summary>A game prepared by the front-end: the setup plus the world already generated for the planet preview (may be null).</summary>
public sealed record PendingGame(GameSetup Setup, World.WorldData World);

/// <summary>Hand-over between the front-end scene (Front.tscn) and the game scene (Main.tscn). Contract: MAIN_MENU.md §2.6.</summary>
public static class Session
{
    public static PendingGame Pending;       // written by the front-end, taken by Main
    public static bool ReturnedFromGame;     // short intro on the title screen
    public static bool LaunchedFromMenu;     // game started from the menu (chapter card waits, start paused)

    public static PendingGame Take() { var p = Pending; Pending = null; LaunchedFromMenu = p != null; return p; }
}
