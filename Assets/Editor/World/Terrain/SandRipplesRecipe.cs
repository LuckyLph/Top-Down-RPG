using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sand: pale and grainy, with slow tint patches, wind ripples and a few dark and light specks. A ripple is a shaded
/// trough under a lit crest, so it reads as a ridge lit from above; ripples run across their direction, whole ripples
/// across the texture, wobble with smooth waves and fade out where another smooth wave falls below a level, leaving
/// patches of flat sand. Specks are single pixels on a jittered grid. Two settings, off by default, make snow of it
/// (Docs/TerrainTypes.md, Phase 7 notes): clumps, small rounded lumps on another jittered grid, lit on the side facing
/// the light and shaded on the side away from it, with a shadow under that side, kept apart from each other; and
/// sparkles, pixels off the clumps' shade with a sparkle phase each, which an animated material lights in turn. No cell
/// field; the base colour is the sand colour, the sparkle colour the sparkles' when there are any, and the height's
/// per-chip value is 0.5 everywhere.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Sand Ripples", fileName = "Recipe_Sand")]
public sealed class SandRipplesRecipe : TerrainMaterialRecipe
{
    private const int GritSalt = 5;

    [Header("Sand")]
    [Tooltip("The sand, and the material's base colour.")]
    [SerializeField] private Color32 sand = new(212, 184, 126, 255);
    [Tooltip("Slow tint patches, in 0-255 steps.")]
    [SerializeField] private Wave[] tintWaves = { new(1, 0, 3f), new(0, 1, 2.5f), new(2, 1, 2f), new(-1, 2, 1.5f), new(3, -2, 1f) };
    [Tooltip("Per-pixel noise, in 0-255 steps from darkest to lightest.")]
    [SerializeField, Range(0f, 32f)] private float grit = 7f;
    [Header("Ripples")]
    [Tooltip("Whole ripples across the texture along x; with Ripples Along Y, the ripples run across this direction. Set for a texture of 16 cells.")]
    [SerializeField, Range(-16, 16)] private int ripplesAlongX = 2;
    [Tooltip("Whole ripples across the texture along y. Set for a texture of 16 cells.")]
    [SerializeField, Range(-32, 32)] private int ripplesAlongY = 24;
    [Tooltip("Waves that bend the ripples, their amplitude in ripples. Set for a texture of 16 cells.")]
    [SerializeField] private Wave[] wobbleWaves =
    {
        new(2, 0, 0.2f), new(3, 1, 0.3f), new(1, 3, 0.26f), new(5, -2, 0.19f), new(4, 5, 0.12f), new(-3, 7, 0.1f)
    };
    [Tooltip("Waves that decide where ripples show, normalised into -1 to 1 by their total amplitude. Waves repeating about as often along y as the ripples, but not quite, break each ripple into strokes at other places than its neighbours, so the strokes do not stack into columns; set for a texture of 16 cells.")]
    [SerializeField] private Wave[] coverWaves =
    {
        new(3, 2, 0.6f), new(-2, 5, 0.5f), new(5, 25, 1f), new(-4, 22, 0.9f), new(7, 27, 0.7f), new(-6, 23, 0.6f), new(9, 26, 0.4f)
    };
    [Tooltip("Ripples show where the cover waves rise above this level: -1 everywhere, 1 nowhere.")]
    [SerializeField, Range(-1f, 1f)] private float coverLevel = 0.15f;
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
    [Header("Clumps")]
    [Tooltip("Clump spots along each side of a cell, on a jittered grid; 0 for none. A clump is a small rounded lump, lit from the top left, kept apart from the others.")]
    [SerializeField, Range(0f, 4f)] private float clumpsPerCell;
    [Tooltip("Chance that a spot holds a clump.")]
    [SerializeField, Range(0f, 1f)] private float clumpChance = 0.5f;
    [Tooltip("Smallest and largest radius of a clump, in pixels.")]
    [SerializeField] private Vector2 clumpRadius = new(1.2f, 2.2f);
    [Tooltip("A clump's pixels facing the light.")]
    [SerializeField] private Color32 clumpLit = new(250, 252, 255, 255);
    [Tooltip("A clump's pixels between its lit and shaded sides.")]
    [SerializeField] private Color32 clump = new(238, 242, 250, 255);
    [Tooltip("A clump's pixels facing away from the light, and the shadow under that side.")]
    [SerializeField] private Color32 clumpShade = new(204, 216, 238, 255);
    [Header("Sparkles")]
    [Tooltip("Sparkle spots along each side of a cell, on a jittered grid; 0 for none. A sparkle is one pixel off the clumps' shade, seen only while it twinkles on an animated material.")]
    [SerializeField, Range(0f, 4f)] private float sparklesPerCell;
    [Tooltip("Chance that a spot holds a sparkle.")]
    [SerializeField, Range(0f, 1f)] private float sparkleChance = 0.6f;
    [Tooltip("The sparkle colour, the material's sparkle colour.")]
    [SerializeField] private Color32 sparkle = new(255, 255, 255, 255);

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Draws the tint, wobble and cover waves, the speck spots and each spot's speck (kept or not, light or dark), the
    /// clump spots and each spot's clump (kept or not, radius) when there are clumps, the sparkle spots and each spot's
    /// sparkle (kept or not, phase) when there are sparkles, then the height waves, and paints the sand, its ripples, its
    /// specks, its clumps and its sparkle phases. With no clumps and no sparkles it draws exactly what it drew before
    /// they existed, so the sand keeps its pixels.
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

        Color32?[] clumps = new Color32?[size * size];
        if (clumpsPerCell > 0f)
        {
            float smallest = Mathf.Min(clumpRadius.x, clumpRadius.y);
            float largest = Mathf.Max(clumpRadius.x, clumpRadius.y);
            foreach ((double spotX, double spotY) in TerrainArt.JitteredSeeds(random, SeedsAcross(clumpsPerCell), jitter, size))
            {
                bool keep = random.NextDouble() < clumpChance;
                double radius = TerrainArt.Uniform(random, smallest, largest);
                if (keep)
                {
                    StampClump(clumps, size, spotX, spotY, radius);
                }
            }
        }

        byte[] glints = null;
        if (sparklesPerCell > 0f)
        {
            glints = new byte[size * size];
            foreach ((double spotX, double spotY) in TerrainArt.JitteredSeeds(random, SeedsAcross(sparklesPerCell), jitter, size))
            {
                bool keep = random.NextDouble() < sparkleChance;
                double phase = random.NextDouble();
                int pixel = (int)spotY * size + (int)spotX;
                if (keep && !(clumps[pixel].HasValue && SameColour(clumps[pixel].Value, clumpShade)))
                {
                    glints[pixel] = TerrainArt.SparkleByte(phase);
                }
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

                albedo[i] = clumps[i] ?? specks[i] ?? colour;
            }
        }

        return new TerrainTextures(size, sand, CellMode, albedo, null, TerrainArt.HeightPixels(height, null, null, glints), null, glints != null ? sparkle : (Color32?)null);
    }

    /// <summary>
    /// Marks a clump of <paramref name="radius"/> pixels around (<paramref name="x"/>, <paramref name="y"/>): each pixel
    /// whose centre lies within the radius in the lit, middle or shaded colour by how far it faces the light
    /// (<see cref="TerrainArt.LightX"/>, <see cref="TerrainArt.LightY"/>), and the pixels just outside it on the side away
    /// from the light in the shaded colour, its shadow, wrapping across the texture's edges; unless any of them lies on
    /// or beside another clump.
    /// </summary>
    private void StampClump(Color32?[] clumps, int size, double x, double y, double radius)
    {
        int reach = (int)Math.Ceiling(radius) + 1;
        List<(int Pixel, Color32 Colour)> pixels = new();
        for (int dy = -reach; dy <= reach; dy++)
        {
            for (int dx = -reach; dx <= reach; dx++)
            {
                int px = (int)Math.Floor(x) + dx;
                int py = (int)Math.Floor(y) + dy;
                double ox = px + 0.5 - x;
                double oy = py + 0.5 - y;
                double distance = Math.Sqrt(ox * ox + oy * oy);
                double facing = distance > 0.0 ? (ox * TerrainArt.LightX + oy * TerrainArt.LightY) / distance : 1.0;
                int pixel = TerrainArt.Wrap(py, size) * size + TerrainArt.Wrap(px, size);
                if (distance <= radius)
                {
                    pixels.Add((pixel, facing > 0.35 ? clumpLit : facing < -0.35 ? clumpShade : clump));
                }
                else if (distance <= radius + 1.0 && facing < -0.5)
                {
                    pixels.Add((pixel, clumpShade));
                }
            }
        }

        foreach ((int pixel, Color32 _) in pixels)
        {
            int px = pixel % size;
            int py = pixel / size;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (clumps[TerrainArt.Wrap(py + dy, size) * size + TerrainArt.Wrap(px + dx, size)].HasValue)
                    {
                        return;
                    }
                }
            }
        }

        foreach ((int pixel, Color32 colour) in pixels)
        {
            clumps[pixel] = colour;
        }
    }

    /// <summary>
    /// Whether <paramref name="a"/> and <paramref name="b"/> are the same colour.
    /// </summary>
    private static bool SameColour(Color32 a, Color32 b)
    {
        return a.r == b.r && a.g == b.g && a.b == b.b;
    }
}
