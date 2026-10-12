using System;
using UnityEngine;

/// <summary>
/// Sand: pale and grainy, with slow tint patches, wind ripples and a few dark and light specks. A ripple is a shaded
/// trough under a lit crest, so it reads as a ridge lit from above; ripples run across their direction, whole ripples
/// across the texture, wobble with smooth waves and fade out where another smooth wave falls below a level, leaving
/// patches of flat sand. Specks are single pixels on a jittered grid. No cell field; the base colour is the sand
/// colour, and the height's per-chip value is 0.5 everywhere.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Sand Ripples", fileName = "Recipe_Sand")]
public sealed class SandRipplesRecipe : TerrainMaterialRecipe
{
    private const int GritSalt = 5;

    [Header("Sand")]
    [Tooltip("The sand, and the material's base colour.")]
    [SerializeField] private Color32 sand = new(212, 184, 126, 255);
    [Tooltip("Slow tint patches, in 0-255 steps.")]
    [SerializeField] private Wave[] tintWaves = { new(1, 0, 3f), new(0, 1, 2.5f), new(2, 1, 2f), new(-1, 2, 1.5f) };
    [Tooltip("Per-pixel noise, in 0-255 steps from darkest to lightest.")]
    [SerializeField, Range(0f, 32f)] private float grit = 7f;
    [Header("Ripples")]
    [Tooltip("Whole ripples across the texture along x; with Ripples Along Y, the ripples run across this direction.")]
    [SerializeField, Range(-8, 8)] private int ripplesAlongX = 1;
    [Tooltip("Whole ripples across the texture along y.")]
    [SerializeField, Range(-16, 16)] private int ripplesAlongY = 9;
    [Tooltip("Waves that bend the ripples, their amplitude in ripples.")]
    [SerializeField] private Wave[] wobbleWaves = { new(2, 0, 0.3f), new(1, 1, 0.22f), new(3, -1, 0.12f) };
    [Tooltip("Waves that decide where ripples show, normalised into -1 to 1 by their total amplitude.")]
    [SerializeField] private Wave[] coverWaves = { new(1, 1, 1f), new(2, -1, 0.7f), new(-1, 3, 0.4f) };
    [Tooltip("Ripples show where the cover waves rise above this level: -1 everywhere, 1 nowhere.")]
    [SerializeField, Range(-1f, 1f)] private float coverLevel = -0.25f;
    [Tooltip("Colour of a ripple's trough, the shaded side under its crest.")]
    [SerializeField] private Color32 trough = new(188, 158, 102, 255);
    [Tooltip("Colour of a ripple's crest, lit from above.")]
    [SerializeField] private Color32 crest = new(230, 208, 156, 255);
    [Tooltip("Width in pixels of a ripple's trough, then of its crest above it.")]
    [SerializeField, Range(0.5f, 3f)] private float bandWidth = 1f;
    [Header("Specks")]
    [Tooltip("Speck spots along each side of a cell, on a jittered grid.")]
    [SerializeField, Range(0.5f, 8f)] private float specksPerCell = 2.5f;
    [Tooltip("How far a spot moves from its grid position, in grid steps.")]
    [SerializeField, Range(0f, 0.5f)] private float jitter = 0.45f;
    [Tooltip("Chance that a spot holds a speck.")]
    [SerializeField, Range(0f, 1f)] private float speckChance = 0.4f;
    [Tooltip("Chance that a speck is light rather than dark.")]
    [SerializeField, Range(0f, 1f)] private float lightChance = 0.45f;
    [SerializeField] private Color32 darkSpeck = new(150, 122, 82, 255);
    [SerializeField] private Color32 lightSpeck = new(242, 228, 196, 255);

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Draws the tint, wobble and cover waves, the speck spots and each spot's speck (kept or not, light or dark), then
    /// the height waves, and paints the sand, its ripples and its specks.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        double[] tint = TerrainArt.PeriodicNoise(random, tintWaves, size);
        double[] wobble = TerrainArt.PeriodicNoise(random, wobbleWaves, size);
        double[] cover = TerrainArt.PeriodicNoise(random, coverWaves, size);
        double coverTotal = TerrainArt.TotalAmplitude(coverWaves);
        (double X, double Y)[] spots = TerrainArt.JitteredSeeds(random, SeedsAcross(specksPerCell), jitter, size);
        Color32?[] specks = new Color32?[size * size];
        foreach ((double spotX, double spotY) in spots)
        {
            bool keep = random.NextDouble() < speckChance;
            bool light = random.NextDouble() < lightChance;
            if (keep)
            {
                specks[(int)spotY * size + (int)spotX] = light ? lightSpeck : darkSpeck;
            }
        }

        double[] height = SmoothHeight(random, size);
        double count = Math.Sqrt(ripplesAlongX * ripplesAlongX + ripplesAlongY * ripplesAlongY);
        double spacing = count > 0.0 ? size / count : 0.0;
        Color32[] albedo = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                double pixelGrit = (TerrainArt.Hash01(x, y, GritSalt) - 0.5) * grit;
                Color32 colour = TerrainArt.Shade(sand, tint[i] + pixelGrit);
                bool covered = coverTotal <= 0.0 || cover[i] / coverTotal > coverLevel;
                if (spacing > 0.0 && covered)
                {
                    double ripple = (ripplesAlongX * (x + 0.5) + ripplesAlongY * (y + 0.5)) / size + wobble[i];
                    double across = (ripple - Math.Floor(ripple)) * spacing;
                    if (across < bandWidth)
                    {
                        colour = TerrainArt.Shade(trough, pixelGrit * 0.5);
                    }
                    else if (across < 2.0 * bandWidth)
                    {
                        colour = TerrainArt.Shade(crest, pixelGrit * 0.5);
                    }
                }

                albedo[i] = specks[i] ?? colour;
            }
        }

        return new TerrainTextures(size, sand, CellMode, albedo, null, TerrainArt.HeightPixels(height, null, null));
    }
}
