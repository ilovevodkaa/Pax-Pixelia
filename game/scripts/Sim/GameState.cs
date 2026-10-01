using System.Collections.Generic;
using PaxPixelia.Core;

namespace PaxPixelia.Sim;

/// <summary>Who gives a nation its commands. Bots act inside the rules cycle; humans only through the command queue.</summary>
public enum NationControl : byte { Bot, Human }

/// <summary>
/// What one nation knows of the map (only nations that need it — humans — carry one; bots are not limited by fog).
/// Fog: 0 unexplored, 1 explored but not seen now (stale), 2 visible. Explored and Met never revert.
/// KnownOwner: the owner this nation last saw (-1 = tribes or never seen); stale land keeps showing it.
/// </summary>
public sealed class NationFog
{
    public byte[] Fog;
    public bool[] Explored;
    public short[] KnownOwner;
    public bool[] Met;          // per nation: seen at least one province while that nation owned it
}

/// <summary>Per-nation state: every player of a 16-player game has its own treasury, research, projects and fog.</summary>
public sealed class NationState
{
    public NationControl Control;
    public long Treasury;               // gold in hundredths
    public long LastTaxes, LastUpkeep;  // hundredths, last rules cycle
    public long Progress;               // research stock that drives the eras (the future tech tree feeds the same)
    public int ScienceRate;             // points gained in the last cycle
    public byte Era;
    public int ProjectIndex = -1;       // current capital project (Simulation.Projects), -1 once everything is built
    public int QueuePct;
    public int ProjectsDone;            // bit i: the one-off project i (no building attached) is finished
    public int EventCount;              // chronicle cards dealt to this nation so far
    public long Materials;              // wood and stone for buildings and new cities (whole units)
    public int LastMaterials;           // materials gained in the last cycle
    public ulong[] TechsDone;           // bit t of word t / 64: Techs.All[t] is known (Techs.Known, Techs.Words)
    public int Researching = -1;        // the technology being studied, -1 = none chosen
    public long[] TechPts;              // science put into each technology so far (Techs.Count)
    public long TechPool;               // science made while nothing was chosen: goes to the next choice
    public int Edicts;                  // bit e: Policy.Edicts[e] is in force
    // ---- the nomad phase (Nomads.cs): the tribe before its capital ----
    public int Camp = -1;               // province the tribe stands on, -1 once settled (or never nomadic)
    public int[] CampPath;              // the walk under way (null = standing)
    public int CampStep, CampSub;       // index into CampPath of the province it is leaving; ticks towards the next
    public int Supplies;                // 0..Nomads.StartSupplies
    public int TribePop;                // people of the tribe, they found the capital
    public int Legends;                 // bit l: Nomads.Legends[l] collected
    public int Myth = -1;               // the legend that became the nation's myth at the founding
    // ---- the character of the people (Character.cs) ----
    public int[] CharA, CharB;          // per scale: deeds towards the left / right pole (units × 1000, decaying)
    public sbyte[] CharLevel;           // per scale: −2 left essence … 2 right essence
    public short[] CharHeld;            // per scale: cycles the essence has held (hardens at Character.HardenCycles)
    public int CharTraits;              // bit t: Character.Traits[t] earned; from bit Character.HardenedBit: hardened poles
    public int FirstTechs;              // technologies this nation learned first in the world
    public int[] CharFx;                // derived: the bonuses of the above per TechFx (Character.Refresh), never saved
    public NationFog Fog;

    public bool Human => Control == NationControl.Human;
}

/// <summary>
/// Mutable game state on top of a WorldData (pure C#, integers only, no Godot types). Created by
/// NationGen.CreateInitialState(world, roster); changed only by Simulation.Step and Commands.Apply, so it can be hashed,
/// replayed from a command journal and kept in lockstep.
/// </summary>
public sealed partial class GameState
{
    // ---- time (see Clock and Calendar) ----
    public long Tick;                       // ticks run since the start
    public long Day256;                     // calendar: days × 256 since 1 January 4000 до н. э.
    public long DateTarget, DateStep;       // the date glides towards DateTarget by DateStep per tick
    public int Pace = 1000;                 // GameSetup.PacePermille (1000 = «Обычная»): scales every era's cost
    public int Speed = 2;                   // 1..5 (session setting, changed by commands)
    public bool Paused;

    // ---- nations ----
    public Data.Nation[] Nations;           // the roster of this game (player's design at [0])
    public NationState[] Nat;
    public int[] NationCapital;             // capital province per nation

    // ---- per province (length P) ----
    public short[] Owner;                   // nation index or -1 (unclaimed tribes)
    public short[] Controller;              // who holds it now: equals Owner in the peaceful game (seam for occupation)
    public int[] Pop;
    public sbyte[] Religion;                // Data.Religions index or -1
    public byte[] Mood;                     // 0..100
    public byte[] Slots;                    // building slots
    public List<Data.Bld>[] Buildings;
    public sbyte[] Ore;                     // Data.Ores index or -1
    public bool[] OreFound;
    public short[] CapitalOf;               // nation index if capital else -1
    public bool[] IsTown;

    // ---- trade routes: province paths ----
    public List<int[]> Routes = new();

    /// <summary>false = observer mode: a view switch only, the fog is still computed underneath.</summary>
    public bool FogEnabled = true;

    // ---- scouts (max Scouts.Max per nation). Moved by Sim, drawn by Map ----
    public List<Scout> Scouts = new();
    public int ScoutSeq;                    // last Scout.Id handed out (stable ids)

    public sealed class Scout
    {
        public int Id;
        public int Nation;
        public int[] Path;          // land provinces from start to target
        public int Step;            // index into Path of the province the scout is leaving
        public int Sub;             // integer progress towards Path[Step+1], 0..Scouts.SubSteps
        public bool Auto;
        public int Steps;           // provinces walked so far
        public int MaxSteps;        // auto parties: the walk to the first frontier plus Scouts.AutoSteps of exploring
        public int Found;           // provinces this party has added to the map
        /// <summary>Render-only position 0..1 towards Path[Step+1], interpolated between ticks by the Game; never read by rules.</summary>
        public float Progress;      // pax-allow: view data
    }

    /// <summary>The event deck of this game (null in tests without content): dealt every rules cycle.</summary>
    public SimEvents Events;

    /// <summary>Reusable buffers for BFS passes (not game state: never hashed, rebuilt on demand).</summary>
    internal SimScratch Scratch;

    public int NationCount => Nat.Length;
    public bool IsHuman(int n) => (uint)n < (uint)Nat.Length && Nat[n].Human;
}
