using System;
using UnityEngine;

/// <summary>
/// Grass: dark green with slow tint patches and per-pixel grit, and small tufts of blades, dark strokes with lighter
/// tips like the marks in the old grass edge, clustering here and there. Tufts sit on a jittered grid of spots; each
/// spot holds one of the tuft glyphs, centred on it and pointing up, or none. No cell field; the base colour is the
/// grass colour, and the height's per-chip value is 0.5 everywhere.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Grass Tufts", fileName = "Recipe_Grass")]
public sealed class GrassTuftsRecipe : TerrainMaterialRecipe
{
    private const byte NoMark = 0;
    private const byte BladeMark = 1;
    private const byte TipMark = 2;
    private const int GritSalt = 3;

    [Header("Grass")]
    [Tooltip("The grass, and the material's base colour.")]
    [SerializeField] private Color32 grass = new(35, 114, 40, 255);
    [Tooltip("Slow tint patches, in 0-255 steps.")]
    [SerializeField] private Wave[] tintWaves = { new(1, 0, 1.4f), new(0, 1, 1.1f), new(2, 1, 0.9f), new(1, -2, 0.9f), new(3, 2, 0.5f) };
    [Tooltip("Per-pixel noise, in 0-255 steps from darkest to lightest.")]
    [SerializeField, Range(0f, 16f)] private float grit = 3f;
    [Header("Tufts")]
    [Tooltip("Colour of a blade ('#' in a glyph).")]
    [SerializeField] private Color32 blade = new(24, 88, 30, 255);
    [Tooltip("Colour of a blade's tip ('+' in a glyph).")]
    [SerializeField] private Color32 tip = new(62, 146, 60, 255);
    [Tooltip("Tuft spots along each side of a cell, on a jittered grid.")]
    [SerializeField, Range(0.5f, 8f)] private float spotsPerCell = 3f;
    [Tooltip("How far a spot moves from its grid position, in grid steps.")]
    [SerializeField, Range(0f, 0.5f)] private float jitter = 0.42f;
    [Tooltip("Chance that a spot holds a tuft, before the density waves.")]
    [SerializeField, Range(0f, 1f)] private float tuftChance = 0.62f;
    [Tooltip("Waves added to the tuft chance, so tufts cluster here and there.")]
    [SerializeField] private Wave[] densityWaves = { new(2, 1, 0.14f), new(-1, 2, 0.12f), new(3, -2, 0.08f) };
    [Tooltip("Tuft shapes, top row first: '#' a blade, '+' a tip, anything else leaves the grass. A spot with a tuft takes one at random, centred on it.")]
    [SerializeField, TextArea(3, 6)] private string[] glyphs =
    {
        "+.+\n#.#\n.#.",
        "+.+.+\n#.#.#\n.#.#.",
        ".+\n+#\n#.",
        "+...+\n#.+.#\n.#.#.",
        "+.\n#+\n.#",
        ".+.\n+#+\n#.#"
    };

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Draws the tint and density waves, the tuft spots, whether each holds a tuft and which, then the height waves,
    /// and paints the grass with its tufts.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        double[] tint = TerrainArt.PeriodicNoise(random, tintWaves, size);
        double[] density = TerrainArt.PeriodicNoise(random, densityWaves, size);
        (double X, double Y)[] spots = TerrainArt.JitteredSeeds(random, SeedsAcross(spotsPerCell), jitter, size);
        string[][] shapes = ParseGlyphs();
        byte[] marks = new byte[size * size];
        foreach ((double spotX, double spotY) in spots)
        {
            int x = (int)spotX;
            int y = (int)spotY;
            bool keep = random.NextDouble() < tuftChance + density[y * size + x];
            int shape = shapes.Length > 0 ? random.Next(shapes.Length) : -1;
            if (keep && shape >= 0)
            {
                Stamp(marks, size, shapes[shape], x, y);
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
                albedo[i] = marks[i] switch
                {
                    BladeMark => TerrainArt.Shade(blade, pixelGrit),
                    TipMark => TerrainArt.Shade(tip, pixelGrit),
                    _ => TerrainArt.Shade(grass, tint[i] + pixelGrit)
                };
            }
        }

        return new TerrainTextures(size, grass, CellMode, albedo, null, TerrainArt.HeightPixels(height, null, null));
    }

    /// <summary>
    /// The glyphs as rows, top first, without blank lines at either end.
    /// </summary>
    private string[][] ParseGlyphs()
    {
        string[][] shapes = new string[glyphs.Length][];
        for (int i = 0; i < glyphs.Length; i++)
        {
            string[] rows = (glyphs[i] ?? string.Empty).Trim('\r', '\n').Split('\n');
            for (int row = 0; row < rows.Length; row++)
            {
                rows[row] = rows[row].TrimEnd('\r');
            }

            shapes[i] = rows;
        }

        return shapes;
    }

    /// <summary>
    /// Marks the blades and tips of a glyph centred on pixel (<paramref name="x"/>, <paramref name="y"/>), its top row
    /// highest, wrapping across the texture's edges and over earlier tufts.
    /// </summary>
    private static void Stamp(byte[] marks, int size, string[] rows, int x, int y)
    {
        int width = 0;
        foreach (string row in rows)
        {
            width = Math.Max(width, row.Length);
        }

        int left = x - width / 2;
        int top = y + rows.Length / 2;
        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < rows[r].Length; c++)
            {
                byte mark = rows[r][c] switch
                {
                    '#' => BladeMark,
                    '+' => TipMark,
                    _ => NoMark
                };
                if (mark != NoMark)
                {
                    marks[TerrainArt.Wrap(top - r, size) * size + TerrainArt.Wrap(left + c, size)] = mark;
                }
            }
        }
    }
}
