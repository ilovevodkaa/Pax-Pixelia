using System.Collections.Generic;

namespace PaxPixelia.Sim;

/// <summary>
/// Mutable game state on top of a WorldData (pure C#, no Godot types). Created by NationGen.CreateInitialState(world).
/// Everything that changes during play lives here so it can later be serialised and synchronised (lockstep).
/// </summary>
public sealed partial class GameState
{
    public const int LocalPlayer = 0;       // nation index controlled by this client (for now)

    // ---- time ----
    public int Year = -1250;                // negative = до н. э.; there is no year 0
    public int Speed = 2;                   // 1..5
    public bool Paused;
    public double Gold = 1240;

    // ---- nations ----
    public int[] NationCapital;             // capital province per nation (Data.Nations index)

    // ---- per province (length P) ----
    public short[] Owner;                   // nation index or -1 (unclaimed tribes)
    public float[] Pop;
    public sbyte[] Religion;                // Data.Religions index or -1
    public byte[] Mood;                     // 0..100
    public byte[] Slots;                    // building slots
    public List<Core.Data.Bld>[] Buildings;
    public sbyte[] Ore;                     // Data.Ores index or -1
    public bool[] OreFound;
    public short[] CapitalOf;               // nation index if capital else -1
    public bool[] IsTown;

    // ---- trade routes: province paths ----
    public List<int[]> Routes = new();

    // ---- fog of war (owned by Fog module): 0 unexplored, 1 explored (stale), 2 visible ----
    public byte[] Fog;
    public bool[] Explored;
    public bool FogEnabled = true;          // false = observer mode

    // ---- scouts (max 2 active). Moved by Sim, drawn by Map ----
    public List<Scout> Scouts = new();
    public sealed class Scout
    {
        public int Id;
        public int[] Path;          // land provinces from start to target
        public int Step;            // index into Path of the province the scout is leaving
        public float Progress;      // 0..1 towards Path[Step+1] (= Sub / Scouts.SubSteps)
        public bool Auto;
        public int Sub;             // integer progress towards Path[Step+1], 0..Scouts.SubSteps (lockstep-safe)
        public int Steps;           // provinces walked so far
        public int MaxSteps;        // auto parties: the walk to the first frontier plus Scouts.AutoSteps of exploring
        public int Found;           // provinces this party has added to the map
    }

    // ---- construction queue of the local capital (demo) ----
    public string QueueName = "Амбар";
    public int QueuePct = 64;

    public static string YearText(int y) => y < 0 ? $"{-y} до н. э." : $"{y} н. э.";
}
