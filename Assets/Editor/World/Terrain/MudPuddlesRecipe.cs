using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mud: dark wet earth with slow tint patches and grit, puddles of many sizes, ridges and lumps. Where a smooth noise
/// of many short waves rises above a level the mud is wetter and darker, and above a higher one lies a puddle: a pool
/// of muddy water sunk into the mud, so the bank on its top-left side shades its edge there and its bottom-right edge
/// catches the light, with short streaks of reflected light in it. Slower cluster waves lower both levels and darken
/// the mud in wetter stretches and raise them in drier ones, so the puddles gather in some places and leave others
/// bare. Ridges are short level rows of lit mud over a row of shade a pixel to their right, lumps of drier mud two
/// pixels square lit from the top left, and cracks (none by default) short wandering grooves with a lit lip below and
/// right of them, all on jittered grids off the puddles and clear of each other. With the puddles off it is packed
/// earth (the Dirt material). No cell field; the base colour is the mud colour, and the height's per-chip value is 0.5
/// everywhere.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Mud Puddles", fileName = "Recipe_Mud")]
public sealed class MudPuddlesRecipe : TerrainMaterialRecipe
{
    private const int GritSalt = 11;
    private const int ReflectionSalt = 13;

    [Header("Mud")]
    [Tooltip("The mud, and the material's base colour.")]
    [SerializeField] private Color32 mud = new(104, 80, 56, 255);
    [Tooltip("Slow tint patches, in 0-255 steps.")]
    [SerializeField] private Wave[] tintWaves = { new(1, 0, 3f), new(0, 1, 2.5f), new(2, -1, 2f), new(1, 2, 1.5f) };
    [Tooltip("Per-pixel noise, in 0-255 steps from darkest to lightest.")]
    [SerializeField, Range(0f, 32f)] private float grit = 7f;
    [Header("Puddles")]
    [Tooltip("Waves of the noise whose high ground makes the wet patches and the puddles, normalised into -1 to 1 by their total amplitude. Many short waves, weaker the shorter they are, give puddles of many sizes and shapes; set for a texture of 16 cells.")]
    [SerializeField] private Wave[] puddleWaves =
    {
        new(7, 2, 1f), new(-3, 8, 0.9f), new(11, -5, 0.8f), new(2, 13, 0.7f), new(15, 3, 0.6f), new(-9, 11, 0.55f),
        new(17, -8, 0.45f), new(5, 19, 0.4f), new(21, 1, 0.3f), new(-16, 13, 0.3f), new(9, -23, 0.25f), new(25, 10, 0.2f)
    };
    [Tooltip("Puddles lie where the puddle waves rise above this level: -1 everywhere, 1 nowhere.")]
    [SerializeField, Range(-1f, 1f)] private float puddleLevel = 0.47f;
    [Tooltip("Slower waves, normalised into -1 to 1, that lower the puddle and wet levels where they rise and raise them where they fall, so puddles gather in wetter stretches and leave drier ones bare. Set for a texture of 16 cells.")]
    [SerializeField] private Wave[] clusterWaves =
    {
        new(1, 1, 0.7f), new(2, -1, 0.6f), new(3, 2, 1f), new(-3, 4, 0.8f), new(5, -2, 0.6f), new(2, 6, 0.5f)
    };
    [Tooltip("How far the cluster waves move the puddle and wet levels.")]
    [SerializeField, Range(0f, 1f)] private float clusterAmount = 0.5f;
    [Tooltip("How much darker the mud is where the cluster waves are highest, and lighter where they are lowest, in 0-255 steps.")]
    [SerializeField, Range(0f, 24f)] private float clusterShade = 6f;
    [Tooltip("The mud is wetter and darker where the puddle waves rise above this level, around the puddles.")]
    [SerializeField, Range(-1f, 1f)] private float wetLevel = 0.22f;
    [Tooltip("How much darker the wet mud is, in 0-255 steps.")]
    [SerializeField, Range(0f, 48f)] private float wetShade = 8f;
    [Tooltip("The pool of a puddle: muddy water, a little darker than the mud.")]
    [SerializeField] private Color32 pool = new(88, 66, 46, 255);
    [Tooltip("A puddle's edge under its top-left bank, in the bank's shade.")]
    [SerializeField] private Color32 bankShade = new(66, 48, 34, 255);
    [Tooltip("A puddle's bottom-right edge, catching the light.")]
    [SerializeField] private Color32 wetRim = new(128, 102, 74, 255);
    [Tooltip("A reflection in a puddle.")]
    [SerializeField] private Color32 reflection = new(150, 132, 110, 255);
    [Tooltip("Chance that a pool pixel starts a reflection, two pixels long.")]
    [SerializeField, Range(0f, 0.2f)] private float reflectionChance = 0.02f;
    [Header("Ridges")]
    [Tooltip("Ridge spots along each side of a cell, on a jittered grid.")]
    [SerializeField, Range(0f, 8f)] private float ridgesPerCell = 1.5f;
    [Tooltip("Chance that a spot holds a ridge, off the puddles and clear of other ridges and lumps.")]
    [SerializeField, Range(0f, 1f)] private float ridgeChance = 0.7f;
    [Tooltip("Shortest ridge, in pixels.")]
    [SerializeField, Range(1, 8)] private int minRidgeLength = 2;
    [Tooltip("Longest ridge, in pixels.")]
    [SerializeField, Range(1, 8)] private int maxRidgeLength = 4;
    [Tooltip("A ridge's row, facing the light.")]
    [SerializeField] private Color32 ridgeLit = new(128, 100, 72, 255);
    [Tooltip("The row under a ridge, a pixel to its right, away from the light.")]
    [SerializeField] private Color32 ridgeShade = new(82, 62, 44, 255);
    [Header("Lumps")]
    [Tooltip("Lump spots along each side of a cell, on a jittered grid.")]
    [SerializeField, Range(0f, 8f)] private float lumpsPerCell = 1.5f;
    [Tooltip("How far a ridge or lump spot moves from its grid position, in grid steps.")]
    [SerializeField, Range(0f, 0.5f)] private float jitter = 0.45f;
    [Tooltip("Chance that a spot holds a lump, off the puddles and clear of ridges and other lumps.")]
    [SerializeField, Range(0f, 1f)] private float lumpChance = 0.6f;
    [Tooltip("A lump's top-left pixel, facing the light.")]
    [SerializeField] private Color32 lumpLit = new(134, 106, 78, 255);
    [Tooltip("A lump's top-right and bottom-left pixels.")]
    [SerializeField] private Color32 lump = new(116, 90, 64, 255);
    [Tooltip("A lump's bottom-right pixel, away from the light.")]
    [SerializeField] private Color32 lumpShade = new(80, 60, 42, 255);
    [Header("Cracks")]
    [Tooltip("Crack spots along each side of a cell, on a jittered grid; 0 for none.")]
    [SerializeField, Range(0f, 8f)] private float cracksPerCell;
    [Tooltip("Chance that a spot holds a crack, off the puddles and clear of ridges, lumps and other cracks.")]
    [SerializeField, Range(0f, 1f)] private float crackChance = 0.5f;
    [Tooltip("Fewest pixels in a crack.")]
    [SerializeField, Range(2, 12)] private int minCrackLength = 3;
    [Tooltip("Most pixels in a crack.")]
    [SerializeField, Range(2, 12)] private int maxCrackLength = 6;
    [Tooltip("Chance at each pixel of a crack that it turns by an eighth of a turn, left or right.")]
    [SerializeField, Range(0f, 1f)] private float crackTurnChance = 0.4f;
    [Tooltip("A crack's pixels, in the shade of the groove.")]
    [SerializeField] private Color32 crack = new(70, 52, 36, 255);
    [Tooltip("The pixel below and right of a crack pixel, the groove's far side catching the light.")]
    [SerializeField] private Color32 crackLip = new(128, 100, 72, 255);

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Draws the tint, puddle and cluster waves, the ridge spots and each spot's ridge (kept or not, length), the lump
    /// spots and whether each holds a lump, the crack spots and each spot's crack (kept or not, length, directions) when
    /// there are cracks, then the height waves, and paints the mud, its wet patches, puddles, ridges, lumps and cracks.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        double[] tint = TerrainArt.PeriodicNoise(random, tintWaves, size);
        double[] puddleNoise = TerrainArt.PeriodicNoise(random, puddleWaves, size);
        double puddleTotal = TerrainArt.TotalAmplitude(puddleWaves);
        double[] cluster = TerrainArt.PeriodicNoise(random, clusterWaves, size);
        double clusterTotal = TerrainArt.TotalAmplitude(clusterWaves);
        bool[] puddle = new bool[size * size];
        bool[] wet = new bool[size * size];
        for (int i = 0; i < puddle.Length; i++)
        {
            cluster[i] = clusterTotal > 0.0 ? cluster[i] / clusterTotal : 0.0;
            double shift = clusterAmount * cluster[i];
            puddle[i] = puddleTotal > 0.0 && puddleNoise[i] / puddleTotal + shift > puddleLevel;
            wet[i] = puddleTotal > 0.0 && puddleNoise[i] / puddleTotal + shift > wetLevel;
        }

        Color32?[] marks = new Color32?[size * size];
        int shortest = Math.Min(minRidgeLength, maxRidgeLength);
        int longest = Math.Max(minRidgeLength, maxRidgeLength);
        if (ridgesPerCell > 0f)
        {
            foreach ((double spotX, double spotY) in TerrainArt.JitteredSeeds(random, SeedsAcross(ridgesPerCell), jitter, size))
            {
                bool keep = random.NextDouble() < ridgeChance;
                int length = random.Next(shortest, longest + 1);
                if (keep)
                {
                    StampRidge(marks, puddle, size, (int)spotX, (int)spotY, length);
                }
            }
        }

        if (lumpsPerCell > 0f)
        {
            foreach ((double spotX, double spotY) in TerrainArt.JitteredSeeds(random, SeedsAcross(lumpsPerCell), jitter, size))
            {
                if (random.NextDouble() < lumpChance)
                {
                    StampLump(marks, puddle, size, (int)spotX, (int)spotY);
                }
            }
        }

        if (cracksPerCell > 0f)
        {
            int fewest = Math.Min(minCrackLength, maxCrackLength);
            int most = Math.Max(minCrackLength, maxCrackLength);
            foreach ((double spotX, double spotY) in TerrainArt.JitteredSeeds(random, SeedsAcross(cracksPerCell), jitter, size))
            {
                bool keep = random.NextDouble() < crackChance;
                int[] directions = TerrainArt.CrackDirections(random, fewest, most, crackTurnChance);
                if (keep)
                {
                    StampCrack(marks, puddle, size, (int)spotX, (int)spotY, directions);
                }
            }
        }

        double[] height = SmoothHeight(random, size);
        Color32[] albedo = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                double pixelGrit = (TerrainArt.Hash01(x, y, GritSalt) - 0.5) * grit;
                albedo[i] = puddle[i] ? PuddleColour(puddle, size, x, y, pixelGrit) : marks[i] ?? TerrainArt.Shade(mud, tint[i] + pixelGrit - clusterShade * cluster[i] - (wet[i] ? wetShade : 0.0));
            }
        }

        return new TerrainTextures(size, mud, CellMode, albedo, null, TerrainArt.HeightPixels(height, null, null));
    }

    /// <summary>
    /// The colour of puddle pixel (<paramref name="x"/>, <paramref name="y"/>): in the bank's shade where the mud lies
    /// above or to its left, catching the light where it lies below or to its right, else the pool or a reflection,
    /// which starts at a pixel picked by a hash and runs one pixel to its right.
    /// </summary>
    private Color32 PuddleColour(bool[] puddle, int size, int x, int y, double pixelGrit)
    {
        bool dryAbove = !puddle[TerrainArt.Wrap(y + 1, size) * size + x];
        bool dryLeft = !puddle[y * size + TerrainArt.Wrap(x - 1, size)];
        bool dryBelow = !puddle[TerrainArt.Wrap(y - 1, size) * size + x];
        bool dryRight = !puddle[y * size + TerrainArt.Wrap(x + 1, size)];
        if (dryAbove || dryLeft)
        {
            return TerrainArt.Shade(bankShade, pixelGrit * 0.5);
        }

        if (dryBelow || dryRight)
        {
            return TerrainArt.Shade(wetRim, pixelGrit * 0.5);
        }

        bool reflects = TerrainArt.Hash01(x, y, ReflectionSalt) < reflectionChance || TerrainArt.Hash01(TerrainArt.Wrap(x - 1, size), y, ReflectionSalt) < reflectionChance;
        return reflects ? reflection : TerrainArt.Shade(pool, pixelGrit * 0.3);
    }

    /// <summary>
    /// Marks a ridge of <paramref name="length"/> lit pixels from (<paramref name="x"/>, <paramref name="y"/>)
    /// rightwards, over as many shaded pixels a row below and a pixel to the right, wrapping across the texture's edges,
    /// unless any of it lies on a puddle or touches another mark.
    /// </summary>
    private void StampRidge(Color32?[] marks, bool[] puddle, int size, int x, int y, int length)
    {
        int below = TerrainArt.Wrap(y - 1, size);
        int[] lit = new int[length];
        int[] shade = new int[length];
        for (int k = 0; k < length; k++)
        {
            lit[k] = y * size + TerrainArt.Wrap(x + k, size);
            shade[k] = below * size + TerrainArt.Wrap(x + k + 1, size);
        }

        if (!IsClear(marks, puddle, size, lit) || !IsClear(marks, puddle, size, shade))
        {
            return;
        }

        foreach (int pixel in lit)
        {
            marks[pixel] = ridgeLit;
        }

        foreach (int pixel in shade)
        {
            marks[pixel] = ridgeShade;
        }
    }

    /// <summary>
    /// Marks a lump two pixels square with its top-left pixel at (<paramref name="x"/>, <paramref name="y"/>), lit
    /// there and shaded at its bottom right, wrapping across the texture's edges, unless any of it lies on a puddle or
    /// touches another mark.
    /// </summary>
    private void StampLump(Color32?[] marks, bool[] puddle, int size, int x, int y)
    {
        int right = TerrainArt.Wrap(x + 1, size);
        int below = TerrainArt.Wrap(y - 1, size);
        int[] pixels = { y * size + x, y * size + right, below * size + x, below * size + right };
        if (!IsClear(marks, puddle, size, pixels))
        {
            return;
        }

        marks[pixels[0]] = lumpLit;
        marks[pixels[1]] = lump;
        marks[pixels[2]] = lump;
        marks[pixels[3]] = lumpShade;
    }

    /// <summary>
    /// Marks a crack from (<paramref name="x"/>, <paramref name="y"/>) along <paramref name="directions"/>
    /// (<see cref="TerrainArt.CrackPixels"/>), its groove in the crack colour and a lit lip below and right of it,
    /// unless any of it lies on a puddle or touches another mark.
    /// </summary>
    private void StampCrack(Color32?[] marks, bool[] puddle, int size, int x, int y, int[] directions)
    {
        (List<int> groove, List<int> lip) = TerrainArt.CrackPixels(x, y, directions, size);
        if (!IsClear(marks, puddle, size, groove.ToArray()) || !IsClear(marks, puddle, size, lip.ToArray()))
        {
            return;
        }

        foreach (int pixel in groove)
        {
            marks[pixel] = crack;
        }

        foreach (int pixel in lip)
        {
            marks[pixel] = crackLip;
        }
    }

    /// <summary>
    /// Whether none of <paramref name="pixels"/> lies on a puddle or has a marked pixel within one pixel of it.
    /// </summary>
    private static bool IsClear(Color32?[] marks, bool[] puddle, int size, int[] pixels)
    {
        foreach (int pixel in pixels)
        {
            if (puddle[pixel])
            {
                return false;
            }

            int px = pixel % size;
            int py = pixel / size;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (marks[TerrainArt.Wrap(py + dy, size) * size + TerrainArt.Wrap(px + dx, size)].HasValue)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
