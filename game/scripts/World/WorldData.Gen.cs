using System.Collections.Generic;

namespace PaxPixelia.World;

/// <summary>Extra generator outputs: land masses / water bodies and diagnostics.</summary>
public sealed partial class WorldData
{
    // ---- bodies: connected land masses and water bodies (4-neighbour, x wraps). A province never spans two ----
    public int[] PBody;        // body id per province
    public int[] BodySize;     // pixels per body
    public byte[] BodyLand;    // 1 = land mass, 0 = water body
    public int Ocean;          // body id of the world ocean (the largest water body)

    /// <summary>True when both provinces lie on the same land mass (reachable on foot) or in the same water body.</summary>
    public bool SameBody(int a, int b) => PBody[a] == PBody[b];

    /// <summary>Provinces renamed to keep names unique, with the name the generator first gave them.</summary>
    public List<(int Province, string MockupName)> Renamed = new();

    /// <summary>Per pixel: terrain material (TerrainPalette; 0 = water, 1..14 = biomes, then sand, rock, snow …) and its
    /// ramp step 0..4. BaseColor[i] = TerrainPalette.Ramps[TerrMaterial[i]][TerrStep[i]] — kept for a palette LUT.</summary>
    public byte[] TerrMaterial, TerrStep;

    /// <summary>Wall-clock milliseconds per generation stage, in order (diagnostics / tests).</summary>
    public List<(string Stage, double Ms)> GenTimings = new();
}
