using System.Collections.Generic;

namespace PaxPixelia.World;

/// <summary>
/// Immutable result of world generation (pure C#, no Godot types). Produced by WorldGen.Generate(seed).
/// Pixel arrays are row-major, index i = y*W + x. The world wraps horizontally (x = W is x = 0).
/// Mirrors the mockup globals in docs/mockups/js (land, hgt, biome, baseCol, river, riverPaths, prov, p* arrays).
/// </summary>
public sealed partial class WorldData
{
    public int Seed, W, H, N;

    // ---- per pixel ----
    public byte[] Land;        // 1 = land, 0 = water
    public float[] Height;     // land: 0..~1.2 (hills > .34, mountains > .6, peaks > .82); water: negative depth -1..0
    public byte[] Biome;       // Data.BiomeName index (0 = water)
    public byte[] BaseColor;   // RGBA8, 4 bytes per pixel: shaded pixel-art terrain colour (rivers NOT baked in)
    public byte[] River;       // 1 = river pixel
    public int[] Prov;         // province id per pixel, 0..P-1

    // ---- rivers as smoothed polylines in world pixel coords (x may be unwrapped beyond [0,W)) ----
    // Flow per point: 0 at a river's head … 1 for a big river (drives the drawn width). A tributary ends on the
    // river it joins; main rivers end one pixel into the sea or lake.
    public sealed class RiverPath { public float[] Xs, Ys, Flow; public float MinY, MaxY; }
    public List<RiverPath> Rivers = new();

    // ---- per province ----
    public int P;
    public int[] PSize;        // pixel count
    public byte[] PLand;       // 1 land / 0 sea
    public int[] PCX, PCY;     // a pixel of the province closest to its (wrapped) centroid — use for labels/sprites
    public byte[] PBiome;      // dominant biome
    public float[] PH;         // mean height
    public byte[] PRiver;      // has a river pixel
    public byte[] PCoast;      // land province adjacent to sea
    public float[] PFert;      // 0..1 fertility
    public string[] PName;     // generated Russian name ("Море …", "Озеро …", "Океан …", "Залив …" for water)
    public int[][] Adj;        // symmetric adjacency (land+sea)
    public int[] PixOffset;    // counting-sort index: pixels of province p are PixList[PixOffset[p] .. PixOffset[p+1])
    public int[] PixList;

    public string TerrainName(int p) => PH[p] > .6f ? "Горы" : PH[p] > .34f && PBiome[p] > 3 ? "Холмы" : Core.Data.BiomeName[PBiome[p]];
}
