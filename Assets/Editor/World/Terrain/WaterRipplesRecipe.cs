using System;
using UnityEngine;

/// <summary>
/// Water, for an animated material: deep water in a few flat tones, dithered where they meet, crossed by rows of wave
/// crests and dotted with hidden sparkles. The tones come from smooth waves cut into bands, with a 2 x 2 ordered dither
/// at each band's edge. The crests lie along lines of constant phase, whole rows across the texture, bent by smooth
/// waves and broken into dashes where another smooth wave falls below a level: each dash is dim at its ends and bright
/// in its core. Glints (Docs/TerrainBlending.md, Phase 5) light the cores and the sparkles: a core's glint phase, in the
/// height's blue channel, starts at its row's own offset and runs along x, so a glint travels along each row of crests;
/// a sparkle is two pixels of plain water with a phase of their own, seen only while it glints. No cell field; the base
/// colour is the water colour, and the height's per-chip value is 0.5 everywhere.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Water Ripples", fileName = "Recipe_Water")]
public sealed class WaterRipplesRecipe : TerrainMaterialRecipe
{
    private const int GritSalt = 19;

    private static readonly double[] Dither = { 0.125, 0.625, 0.875, 0.375 };

    [Header("Water")]
    [Tooltip("The middle tone of the water, and the material's base colour.")]
    [SerializeField] private Color32 water = new(44, 96, 150, 255);
    [Tooltip("Waves of the water's depth, normalised into -1 to 1 by their total amplitude and cut into the tones.")]
    [SerializeField] private Wave[] depthWaves = { new(1, 0, 1f), new(0, 1, 0.9f), new(1, -1, 0.7f), new(2, 1, 0.45f), new(-1, 3, 0.3f) };
    [Tooltip("How many flat tones the water has, from dark to light around the water colour.")]
    [SerializeField, Range(1, 5)] private int tones = 3;
    [Tooltip("Step between two tones, in 0-255 steps.")]
    [SerializeField, Range(0f, 24f)] private float toneStep = 9f;
    [Tooltip("How much of the way from one tone to the next is dithered: 0 for a hard edge, 1 for a dither across the whole band.")]
    [SerializeField, Range(0f, 1f)] private float ditherWidth = 0.3f;
    [Tooltip("Per-pixel noise, in 0-255 steps from darkest to lightest.")]
    [SerializeField, Range(0f, 16f)] private float grit = 1f;
    [Header("Wave crests")]
    [Tooltip("Whole rows of crests across the texture along x; with Rows Along Y, the rows run across this direction.")]
    [SerializeField, Range(-8, 8)] private int rowsAlongX;
    [Tooltip("Whole rows of crests across the texture along y.")]
    [SerializeField, Range(-24, 24)] private int rowsAlongY = 12;
    [Tooltip("Waves that bend the rows, their amplitude in rows.")]
    [SerializeField] private Wave[] wobbleWaves = { new(1, 0, 0.3f), new(2, 1, 0.18f), new(3, -1, 0.1f) };
    [Tooltip("Waves along the rows that break them into dashes, normalised into -1 to 1 by their total amplitude.")]
    [SerializeField] private Wave[] dashWaves = { new(2, 3, 1f), new(3, -2, 0.8f), new(5, 4, 0.5f), new(-4, 5, 0.4f) };
    [Tooltip("A crest shows where the dash waves rise above this level, dim near it.")]
    [SerializeField, Range(-1f, 1f)] private float dashLevel = 0.05f;
    [Tooltip("A crest is bright, and glints, where the dash waves rise above this level.")]
    [SerializeField, Range(-1f, 1f)] private float coreLevel = 0.35f;
    [Tooltip("A dash's dim ends.")]
    [SerializeField] private Color32 crestDim = new(64, 122, 176, 255);
    [Tooltip("A dash's bright core, which glints.")]
    [SerializeField] private Color32 crestBright = new(90, 148, 198, 255);
    [Tooltip("How many times a glint's phase runs through its period across the texture along x, so a glint travels along its row; 0 lights a whole row at once.")]
    [SerializeField, Range(0, 8)] private int glintTrips = 3;
    [Header("Sparkles")]
    [Tooltip("Sparkle spots along each side of a cell, on a jittered grid.")]
    [SerializeField, Range(0.5f, 8f)] private float sparklesPerCell = 1.5f;
    [Tooltip("How far a spot moves from its grid position, in grid steps.")]
    [SerializeField, Range(0f, 0.5f)] private float jitter = 0.45f;
    [Tooltip("Chance that a spot holds a sparkle, off the crests.")]
    [SerializeField, Range(0f, 1f)] private float sparkleChance = 0.5f;

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Draws the depth, wobble and dash waves, a glint offset per row of crests, the sparkle spots and each spot's
    /// sparkle (kept or not, phase), then the height waves, and paints the water's tones, the crests and the glint
    /// phases of the crests' cores and the sparkles.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        double[] depth = TerrainArt.PeriodicNoise(random, depthWaves, size);
        double[] wobble = TerrainArt.PeriodicNoise(random, wobbleWaves, size);
        double[] dashes = TerrainArt.PeriodicNoise(random, dashWaves, size);
        double depthTotal = TerrainArt.TotalAmplitude(depthWaves);
        double dashTotal = TerrainArt.TotalAmplitude(dashWaves);
        int rowCycle = RowCycle();
        double[] rowOffsets = new double[Math.Max(rowCycle, 1)];
        for (int r = 0; r < rowOffsets.Length; r++)
        {
            rowOffsets[r] = random.NextDouble();
        }

        Color32[] albedo = new Color32[size * size];
        byte[] glints = new byte[size * size];
        bool[] crest = new bool[size * size];
        double count = Math.Sqrt(rowsAlongX * rowsAlongX + rowsAlongY * rowsAlongY);
        double spacing = count > 0.0 ? size / count : 0.0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                double pixelGrit = (TerrainArt.Hash01(x, y, GritSalt) - 0.5) * grit;
                albedo[i] = TerrainArt.Shade(water, Tone(depthTotal > 0.0 ? depth[i] / depthTotal : 0.0, x, y) + pixelGrit);
                double dash = dashTotal > 0.0 ? dashes[i] / dashTotal : 1.0;
                if (spacing <= 0.0 || dash <= dashLevel)
                {
                    continue;
                }

                double row = (rowsAlongX * (x + 0.5) + rowsAlongY * (y + 0.5)) / size + wobble[i];
                if ((row - Math.Floor(row)) * spacing >= 1.0)
                {
                    continue;
                }

                crest[i] = true;
                bool core = dash > coreLevel;
                albedo[i] = TerrainArt.Shade(core ? crestBright : crestDim, pixelGrit);
                if (core)
                {
                    int rowIndex = TerrainArt.Wrap((int)Math.Floor(row), rowOffsets.Length);
                    glints[i] = TerrainArt.GlintByte(rowOffsets[rowIndex] + glintTrips * (x + 0.5) / size);
                }
            }
        }

        (double X, double Y)[] spots = TerrainArt.JitteredSeeds(random, SeedsAcross(sparklesPerCell), jitter, size);
        foreach ((double spotX, double spotY) in spots)
        {
            bool keep = random.NextDouble() < sparkleChance;
            double phase = random.NextDouble();
            int left = (int)spotY * size + (int)spotX;
            int right = (int)spotY * size + TerrainArt.Wrap((int)spotX + 1, size);
            if (keep && !crest[left] && !crest[right])
            {
                glints[left] = TerrainArt.GlintByte(phase);
                glints[right] = TerrainArt.GlintByte(phase);
            }
        }

        double[] height = SmoothHeight(random, size);
        return new TerrainTextures(size, water, CellMode, albedo, null, TerrainArt.HeightPixels(height, null, null, glints));
    }

    /// <summary>
    /// The shade, in 0-255 steps, of the tone a normalised depth (-1 to 1) falls in at pixel (<paramref name="x"/>,
    /// <paramref name="y"/>): the depth cut into <see cref="tones"/> bands, with a 2 x 2 ordered dither over the
    /// dither width across each band's edge, the middle band at the water colour.
    /// </summary>
    private double Tone(double normalisedDepth, int x, int y)
    {
        if (tones <= 1)
        {
            return 0.0;
        }

        double band = (normalisedDepth + 1.0) * 0.5 * (tones - 1);
        double dither = 0.5 + (Dither[(y & 1) * 2 + (x & 1)] - 0.5) * ditherWidth;
        int level = Mathf.Clamp((int)Math.Floor(band + dither), 0, tones - 1);
        return (level - (tones - 1) * 0.5) * toneStep;
    }

    /// <summary>
    /// How many rows of crests the glint offsets repeat over: the greatest common divisor of the rows along x and y, so
    /// a row keeps its offset across the texture's edges, where its index moves on by the rows along each axis.
    /// </summary>
    private int RowCycle()
    {
        int a = Math.Abs(rowsAlongX);
        int b = Math.Abs(rowsAlongY);
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
