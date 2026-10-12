using UnityEngine;

/// <summary>
/// Mud: dark wet earth with slow tint patches and grit, puddles and lumps. A puddle lies wherever a smooth noise rises
/// above a level, in a patch of wetter, darker mud where the same noise rises above a lower one: a pool sunk into the
/// mud, so the bank on its top-left side shades its edge there and its bottom-right edge catches the light, with short
/// streaks of reflected sky in it. Lumps of drier mud sit on a jittered grid off the puddles, two pixels square, lit
/// from the top left. No cell field; the base colour is the mud colour, and the
/// height's per-chip value is 0.5 everywhere.
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
    [SerializeField] private Wave[] tintWaves = { new(1, 0, 4f), new(0, 1, 3.5f), new(2, -1, 2.5f), new(1, 2, 2f) };
    [Tooltip("Per-pixel noise, in 0-255 steps from darkest to lightest.")]
    [SerializeField, Range(0f, 32f)] private float grit = 7f;
    [Header("Puddles")]
    [Tooltip("Waves of the noise whose high ground makes the puddles, normalised into -1 to 1 by their total amplitude.")]
    [SerializeField] private Wave[] puddleWaves = { new(1, 1, 1f), new(2, -1, 0.8f), new(-1, 3, 0.5f), new(3, 2, 0.4f) };
    [Tooltip("Puddles lie where the puddle waves rise above this level: -1 everywhere, 1 nowhere.")]
    [SerializeField, Range(-1f, 1f)] private float puddleLevel = 0.55f;
    [Tooltip("The mud is wetter and darker where the puddle waves rise above this level, around the puddles.")]
    [SerializeField, Range(-1f, 1f)] private float wetLevel = 0.2f;
    [Tooltip("How much darker the wet mud is, in 0-255 steps.")]
    [SerializeField, Range(0f, 48f)] private float wetShade = 12f;
    [Tooltip("The pool of a puddle.")]
    [SerializeField] private Color32 pool = new(80, 72, 64, 255);
    [Tooltip("A puddle's edge under its top-left bank, in the bank's shade.")]
    [SerializeField] private Color32 bankShade = new(56, 46, 38, 255);
    [Tooltip("A puddle's bottom-right edge, catching the light.")]
    [SerializeField] private Color32 wetRim = new(132, 114, 92, 255);
    [Tooltip("A reflection in a puddle.")]
    [SerializeField] private Color32 reflection = new(138, 138, 134, 255);
    [Tooltip("Chance that a pool pixel starts a reflection, two pixels long.")]
    [SerializeField, Range(0f, 0.2f)] private float reflectionChance = 0.015f;
    [Header("Lumps")]
    [Tooltip("Lump spots along each side of a cell, on a jittered grid.")]
    [SerializeField, Range(0.5f, 8f)] private float lumpsPerCell = 3f;
    [Tooltip("How far a spot moves from its grid position, in grid steps.")]
    [SerializeField, Range(0f, 0.5f)] private float jitter = 0.45f;
    [Tooltip("Chance that a spot holds a lump, off the puddles.")]
    [SerializeField, Range(0f, 1f)] private float lumpChance = 0.6f;
    [Tooltip("A lump's top-left pixel, facing the light.")]
    [SerializeField] private Color32 lumpLit = new(150, 122, 90, 255);
    [Tooltip("A lump's top-right and bottom-left pixels.")]
    [SerializeField] private Color32 lump = new(126, 98, 70, 255);
    [Tooltip("A lump's bottom-right pixel, away from the light.")]
    [SerializeField] private Color32 lumpShade = new(72, 54, 38, 255);

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Draws the tint and puddle waves, the lump spots and whether each holds a lump, then the height waves, and paints
    /// the mud, its puddles and its lumps.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        double[] tint = TerrainArt.PeriodicNoise(random, tintWaves, size);
        double[] puddleNoise = TerrainArt.PeriodicNoise(random, puddleWaves, size);
        double puddleTotal = TerrainArt.TotalAmplitude(puddleWaves);
        bool[] puddle = new bool[size * size];
        for (int i = 0; i < puddle.Length; i++)
        {
            puddle[i] = puddleTotal > 0.0 && puddleNoise[i] / puddleTotal > puddleLevel;
        }

        (double X, double Y)[] spots = TerrainArt.JitteredSeeds(random, SeedsAcross(lumpsPerCell), jitter, size);
        Color32?[] lumps = new Color32?[size * size];
        foreach ((double spotX, double spotY) in spots)
        {
            if (random.NextDouble() < lumpChance)
            {
                StampLump(lumps, puddle, size, (int)spotX, (int)spotY);
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
                double wet = puddleTotal > 0.0 && puddleNoise[i] / puddleTotal > wetLevel ? wetShade : 0.0;
                albedo[i] = puddle[i] ? PuddleColour(puddle, size, x, y, pixelGrit) : lumps[i] ?? TerrainArt.Shade(mud, tint[i] + pixelGrit - wet);
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
    /// Marks a lump two pixels square with its top-left pixel at (<paramref name="x"/>, <paramref name="y"/>), lit
    /// there and shaded at its bottom right, wrapping across the texture's edges, unless any of it lies on a puddle.
    /// </summary>
    private void StampLump(Color32?[] lumps, bool[] puddle, int size, int x, int y)
    {
        int right = TerrainArt.Wrap(x + 1, size);
        int below = TerrainArt.Wrap(y - 1, size);
        int[] pixels = { y * size + x, y * size + right, below * size + x, below * size + right };
        foreach (int pixel in pixels)
        {
            if (puddle[pixel])
            {
                return;
            }
        }

        lumps[pixels[0]] = lumpLit;
        lumps[pixels[1]] = lump;
        lumps[pixels[2]] = lump;
        lumps[pixels[3]] = lumpShade;
    }
}
