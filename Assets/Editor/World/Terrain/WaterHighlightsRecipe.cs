using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pond and lake water, for an animated material (Docs/TerrainBlending.md, Phase 5 notes): calm water in three flat
/// tones, sparse short highlight strokes and hidden sparkles, the usual look of still water in 16-bit games. The tones
/// are long, mostly level swells cut from a smooth field at shares of its lowest and highest values, with no dithering.
/// A stroke is a short horizontal dash on a jittered grid of spots, kept where a smooth density field lets it so
/// strokes gather in loose bands, sometimes with a shorter companion dash a row above it, bowed up a pixel in its
/// middle when long enough, and kept clear of other strokes; it rests in a faint colour, and its glint phases run from
/// its middle outwards, so each glint appears in the middle, spreads to the ends and fades from the middle out, like a
/// ripple widening. A sparkle is a pixel of plain water with a sparkle phase of its own, seen only while it twinkles.
/// Shallow water shows its bed (off by default, so deep water has none): a bed tone where a smooth bed field is highest,
/// under the strokes, and sparse specks of pebbles on plain water. With cracks (none by default), short wandering lines
/// with a lip below and right of them, clear of the strokes, the sparkles and each other, and its strokes slanted into
/// diagonal glare (level by default), it is a cracked sheet that still glints (ice, Docs/TerrainTypes.md, Phase 7
/// notes); without strokes only its sparkles light up. No cell field; the base colour is the middle tone, the glint colour the
/// strokes' highlight and the sparkle colour the sparkles', and the height's per-chip value is 0.5 everywhere.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Water Highlights", fileName = "Recipe_Water")]
public sealed class WaterHighlightsRecipe : TerrainMaterialRecipe
{
    [Header("Water")]
    [Tooltip("The middle tone, and the material's base colour.")]
    [SerializeField] private Color32 water = new(40, 104, 160, 255);
    [Tooltip("The darker tone, where the tone field is lowest.")]
    [SerializeField] private Color32 darker = new(37, 98, 154, 255);
    [Tooltip("The lighter tone, where the tone field is highest.")]
    [SerializeField] private Color32 lighter = new(44, 110, 166, 255);
    [Tooltip("Waves of the tone field. X and Y are how many times a wave repeats across the texture; amplitudes only count relative to each other. Set for a texture of 8 cells.")]
    [SerializeField] private Wave[] toneWaves = { new(1, 3, 1f), new(2, 5, 0.7f), new(-1, 4, 0.6f), new(3, 7, 0.4f) };
    [Tooltip("Share of the water in the darker tone.")]
    [SerializeField, Range(0f, 1f)] private float darkerShare = 0.3f;
    [Tooltip("Share of the water in the lighter tone.")]
    [SerializeField, Range(0f, 1f)] private float lighterShare = 0.2f;
    [Header("Strokes")]
    [Tooltip("Stroke spots along each side of a world cell, on a jittered grid.")]
    [SerializeField, Range(0.25f, 4f)] private float strokesPerCell = 1.6f;
    [Tooltip("How far a spot moves from its grid position, in grid steps.")]
    [SerializeField, Range(0f, 0.5f)] private float jitter = 0.45f;
    [Tooltip("Chance that a spot holds a stroke, before the density waves; a stroke that would touch another is left out too.")]
    [SerializeField, Range(0f, 1f)] private float strokeChance = 0.6f;
    [Tooltip("Waves added to the stroke chance, normalised into -1 to 1 and scaled by the density amount, so strokes gather in loose bands. Set for a texture of 8 cells.")]
    [SerializeField] private Wave[] densityWaves = { new(1, 3, 1f), new(2, 5, 0.7f), new(-1, 4, 0.5f) };
    [Tooltip("How much the density waves raise or lower the stroke chance.")]
    [SerializeField, Range(0f, 1f)] private float densityAmount = 0.5f;
    [Tooltip("Shortest stroke, in pixels.")]
    [SerializeField, Range(2, 16)] private int minLength = 3;
    [Tooltip("Longest stroke, in pixels.")]
    [SerializeField, Range(2, 16)] private int maxLength = 10;
    [Tooltip("Strokes at least this long bow up a pixel in their middle, like the crest of a ripple; above the longest stroke, none do.")]
    [SerializeField, Range(2, 17)] private int bowLength = 17;
    [Tooltip("How many rows a stroke climbs per pixel along it, rounded: 0 for level dashes (water), 1 for diagonal ones rising to the right, like glare on ice.")]
    [SerializeField, Range(0f, 1f)] private float slant;
    [Tooltip("Chance that a stroke has a companion: a dash half its length a row above it, shifted sideways, glinting a little after it.")]
    [SerializeField, Range(0f, 1f)] private float companionChance = 0.4f;
    [Tooltip("A stroke's colour while it does not glint; the water colour hides it between glints.")]
    [SerializeField] private Color32 resting = new(50, 116, 172, 255);
    [Tooltip("The colour a stroke glints with, the material's glint colour.")]
    [SerializeField] private Color32 highlight = new(140, 196, 228, 255);
    [Tooltip("How much of the glint period a glint takes to spread from a stroke's middle to its ends.")]
    [SerializeField, Range(0f, 0.5f)] private float spread = 0.15f;
    [Header("Sparkles")]
    [Tooltip("Sparkle spots along each side of a world cell, on a jittered grid.")]
    [SerializeField, Range(0.1f, 4f)] private float sparklesPerCell = 0.7f;
    [Tooltip("Chance that a spot holds a sparkle, off the strokes.")]
    [SerializeField, Range(0f, 1f)] private float sparkleChance = 0.6f;
    [Tooltip("The sparkle colour, the material's sparkle colour.")]
    [SerializeField] private Color32 sparkle = new(226, 246, 242, 255);
    [Header("Bed")]
    [Tooltip("Share of the water showing the bed tone, where the bed field is highest: the bottom of shallow water seen through it. 0 for none, as in deep water.")]
    [SerializeField, Range(0f, 1f)] private float bedShare;
    [Tooltip("The bed seen through the water.")]
    [SerializeField] private Color32 bed = new(98, 158, 164, 255);
    [Tooltip("Waves of the bed field. X and Y are how many times a wave repeats across the texture; amplitudes only count relative to each other. Set for a texture of 8 cells.")]
    [SerializeField] private Wave[] bedWaves = { new(2, 3, 1f), new(3, -2, 0.8f), new(-4, 5, 0.5f), new(6, 4, 0.3f) };
    [Tooltip("Pebble spots along each side of a world cell, on a jittered grid; 0 for none. A pebble is one pixel of plain water, off the strokes and sparkles.")]
    [SerializeField, Range(0f, 4f)] private float pebblesPerCell;
    [Tooltip("Chance that a spot holds a pebble.")]
    [SerializeField, Range(0f, 1f)] private float pebbleChance = 0.5f;
    [Tooltip("The pebbles' colour.")]
    [SerializeField] private Color32 pebble = new(54, 116, 140, 255);
    [Header("Cracks")]
    [Tooltip("Crack spots along each side of a world cell, on a jittered grid; 0 for none. A crack is a line that wanders from its spot, off the strokes and sparkles and clear of other cracks.")]
    [SerializeField, Range(0f, 4f)] private float cracksPerCell;
    [Tooltip("Chance that a spot holds a crack.")]
    [SerializeField, Range(0f, 1f)] private float crackChance = 0.5f;
    [Tooltip("Fewest pixels in a crack.")]
    [SerializeField, Range(2, 16)] private int minCrackLength = 4;
    [Tooltip("Most pixels in a crack.")]
    [SerializeField, Range(2, 16)] private int maxCrackLength = 10;
    [Tooltip("Chance at each pixel of a crack that it turns by an eighth of a turn, left or right.")]
    [SerializeField, Range(0f, 1f)] private float crackTurnChance = 0.3f;
    [Tooltip("A crack's pixels.")]
    [SerializeField] private Color32 crack = new(226, 240, 250, 255);
    [Tooltip("The pixel below and right of a crack pixel, the crack's far side.")]
    [SerializeField] private Color32 crackLip = new(120, 168, 204, 255);

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Draws the tone waves, the bed waves when the bed shows, the density waves, the stroke spots and each spot's
    /// stroke (kept or not, length, phase, companion and its side), the sparkle spots and each spot's sparkle (kept or
    /// not, phase), the pebble spots and each spot's pebble (kept or not) when there are pebbles, the crack spots and
    /// each spot's crack (kept or not, its directions) when there are cracks, then the height waves, and paints the
    /// tones, the bed, the strokes, the pebbles, the cracks and the glint and sparkle phases. With no bed, pebbles or
    /// cracks it draws exactly what it drew before they existed, so deep water keeps its pixels.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        double[] toneField = TerrainArt.PeriodicNoise(random, toneWaves, size);
        Color32[] albedo = PaintTones(toneField);

        if (bedShare > 0f)
        {
            double[] bedField = TerrainArt.PeriodicNoise(random, bedWaves, size);
            double bedAbove = Quantile(bedField, 1.0 - bedShare);
            for (int i = 0; i < albedo.Length; i++)
            {
                if (bedField[i] > bedAbove)
                {
                    albedo[i] = bed;
                }
            }
        }

        double[] density = TerrainArt.PeriodicNoise(random, densityWaves, size);
        double densityTotal = TerrainArt.TotalAmplitude(densityWaves);
        byte[] glints = new byte[size * size];
        bool[] taken = new bool[size * size];
        int shortest = Math.Min(minLength, maxLength);
        int longest = Math.Max(minLength, maxLength);
        foreach ((double spotX, double spotY) in TerrainArt.JitteredSeeds(random, SeedsAcross(strokesPerCell), jitter, size))
        {
            int x = (int)spotX;
            int y = (int)spotY;
            double chance = strokeChance + (densityTotal > 0.0 ? densityAmount * density[y * size + x] / densityTotal : 0.0);
            bool keep = random.NextDouble() < chance;
            int length = random.Next(shortest, longest + 1);
            double phase = random.NextDouble();
            bool companion = random.NextDouble() < companionChance;
            int side = random.Next(2) == 0 ? -1 : 1;
            if (keep && StampStroke(albedo, glints, taken, size, x, y, length, phase) && companion)
            {
                int companionLength = Math.Max(2, length / 2);
                StampStroke(albedo, glints, taken, size, x + side * (length / 3), y + 1, companionLength, phase - 0.05, true);
            }
        }

        foreach ((double spotX, double spotY) in TerrainArt.JitteredSeeds(random, SeedsAcross(sparklesPerCell), jitter, size))
        {
            bool keep = random.NextDouble() < sparkleChance;
            double phase = random.NextDouble();
            int pixel = (int)spotY * size + (int)spotX;
            if (keep && !taken[pixel])
            {
                glints[pixel] = TerrainArt.SparkleByte(phase);
            }
        }

        if (pebblesPerCell > 0f)
        {
            foreach ((double spotX, double spotY) in TerrainArt.JitteredSeeds(random, SeedsAcross(pebblesPerCell), jitter, size))
            {
                bool keep = random.NextDouble() < pebbleChance;
                int pixel = (int)spotY * size + (int)spotX;
                if (keep && !taken[pixel] && glints[pixel] == 0)
                {
                    albedo[pixel] = pebble;
                }
            }
        }

        if (cracksPerCell > 0f)
        {
            int fewest = Math.Min(minCrackLength, maxCrackLength);
            int most = Math.Max(minCrackLength, maxCrackLength);
            bool[] cracked = new bool[size * size];
            foreach ((double spotX, double spotY) in TerrainArt.JitteredSeeds(random, SeedsAcross(cracksPerCell), jitter, size))
            {
                bool keep = random.NextDouble() < crackChance;
                int[] directions = TerrainArt.CrackDirections(random, fewest, most, crackTurnChance);
                if (keep)
                {
                    StampCrack(albedo, glints, taken, cracked, size, (int)spotX, (int)spotY, directions);
                }
            }
        }

        double[] height = SmoothHeight(random, size);
        return new TerrainTextures(size, water, CellMode, albedo, null, TerrainArt.HeightPixels(height, null, null, glints), highlight, sparkle);
    }

    /// <summary>
    /// The three tones cut from <paramref name="toneField"/> at its lowest darker share and highest lighter share.
    /// </summary>
    private Color32[] PaintTones(double[] toneField)
    {
        double darkerBelow = Quantile(toneField, darkerShare);
        double lighterAbove = Quantile(toneField, 1.0 - lighterShare);
        Color32[] albedo = new Color32[toneField.Length];
        for (int i = 0; i < albedo.Length; i++)
        {
            albedo[i] = darkerShare > 0f && toneField[i] < darkerBelow ? darker : lighterShare > 0f && toneField[i] > lighterAbove ? lighter : water;
        }

        return albedo;
    }

    /// <summary>
    /// Paints a crack from (<paramref name="x"/>, <paramref name="y"/>) along <paramref name="directions"/>
    /// (<see cref="TerrainArt.CrackPixels"/>), its groove in the crack colour and its lip in the lip colour, and marks
    /// them cracked, unless any of its pixels lies on a stroke or its surroundings, on a sparkle, or within a pixel of
    /// another crack.
    /// </summary>
    private void StampCrack(Color32[] albedo, byte[] glints, bool[] taken, bool[] cracked, int size, int x, int y, int[] directions)
    {
        (List<int> groove, List<int> lip) = TerrainArt.CrackPixels(x, y, directions, size);
        List<int> pixels = new(groove);
        pixels.AddRange(lip);
        foreach (int pixel in pixels)
        {
            if (taken[pixel] || glints[pixel] != 0)
            {
                return;
            }

            int px = pixel % size;
            int py = pixel / size;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (cracked[TerrainArt.Wrap(py + dy, size) * size + TerrainArt.Wrap(px + dx, size)])
                    {
                        return;
                    }
                }
            }
        }

        foreach (int pixel in groove)
        {
            albedo[pixel] = crack;
            cracked[pixel] = true;
        }

        foreach (int pixel in lip)
        {
            albedo[pixel] = crackLip;
            cracked[pixel] = true;
        }
    }

    /// <summary>
    /// The value of <paramref name="field"/> below which <paramref name="share"/> of its pixels lie.
    /// </summary>
    private static double Quantile(double[] field, double share)
    {
        double[] sorted = (double[])field.Clone();
        Array.Sort(sorted);
        int index = Mathf.Clamp((int)Math.Floor(share * sorted.Length), 0, sorted.Length - 1);
        return sorted[index];
    }

    /// <summary>
    /// Paints a stroke of <paramref name="length"/> pixels centred on (<paramref name="x"/>, <paramref name="y"/>),
    /// climbing by the slant along it, bowed up a pixel in its middle when it is at least <see cref="bowLength"/> long,
    /// in the resting colour, with
    /// glint phases from <paramref name="phase"/> at its middle to the spread later at its ends, wrapping across the
    /// texture's edges. Leaves it out when any of its pixels or their neighbours belong to another stroke, and marks
    /// its pixels and their neighbours taken, except, for a <paramref name="companion"/> dash, the pixels its own stroke
    /// took. Returns whether it was painted.
    /// </summary>
    private bool StampStroke(Color32[] albedo, byte[] glints, bool[] taken, int size, int x, int y, int length, double phase, bool companion = false)
    {
        int left = x - length / 2;
        double half = Math.Max((length - 1) * 0.5, 0.5);
        int[] pixels = new int[length];
        for (int k = 0; k < length; k++)
        {
            double fromMiddle = Math.Abs(k - (length - 1) * 0.5) / half;
            bool bowed = length >= bowLength && fromMiddle < 0.5;
            int row = (bowed ? y + 1 : y) + (int)Math.Floor(k * slant + 0.5);
            pixels[k] = TerrainArt.Wrap(row, size) * size + TerrainArt.Wrap(left + k, size);
        }

        foreach (int pixel in pixels)
        {
            if (companion ? glints[pixel] != 0 || taken[TerrainArt.Wrap(pixel / size + 1, size) * size + pixel % size] : taken[pixel])
            {
                return false;
            }
        }

        for (int k = 0; k < length; k++)
        {
            double fromMiddle = Math.Abs(k - (length - 1) * 0.5) / half;
            albedo[pixels[k]] = resting;
            glints[pixels[k]] = TerrainArt.GlintByte(phase - spread * fromMiddle);
        }

        foreach (int pixel in pixels)
        {
            int px = pixel % size;
            int py = pixel / size;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    taken[TerrainArt.Wrap(py + dy, size) * size + TerrainArt.Wrap(px + dx, size)] = true;
                }
            }
        }

        return true;
    }
}
