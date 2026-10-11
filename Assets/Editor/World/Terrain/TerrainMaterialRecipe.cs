using System;
using UnityEngine;

/// <summary>
/// An editor-only recipe for one <see cref="TerrainMaterial"/>'s textures (Docs/TerrainBlending.md, Phase 1b): the
/// material it fills, a seed, the repeat in cells and the waves of the smooth height, plus each kind's own parameters.
/// <see cref="Generate"/> draws the textures from one <see cref="System.Random"/> seeded with the given seed, so the same
/// seed always gives the same pixels; <see cref="TerrainMaterialWriter"/> writes them as the material's textures and
/// its inspector previews them. Textures repeat every <see cref="RepeatCells"/> cells of 32 px; waves count whole
/// periods across the texture, so they repeat with it.
/// </summary>
public abstract class TerrainMaterialRecipe : ScriptableObject
{
    private const double MaxHeightSlope = 0.1;

    /// <summary>
    /// One sine wave of periodic noise: whole periods across the texture along x and y, and its amplitude.
    /// </summary>
    [Serializable]
    public struct Wave
    {
        [Tooltip("Whole periods across the texture along x.")]
        [SerializeField] private int x;
        [Tooltip("Whole periods across the texture along y.")]
        [SerializeField] private int y;
        [SerializeField] private float amplitude;

        /// <summary>
        /// A wave of <paramref name="x"/> and <paramref name="y"/> periods across the texture.
        /// </summary>
        public Wave(int x, int y, float amplitude)
        {
            this.x = x;
            this.y = y;
            this.amplitude = amplitude;
        }

        public int X => x;
        public int Y => y;
        public float Amplitude => amplitude;
    }

    [Tooltip("The terrain material Generate fills: its textures, base colour, repeat and cell mode.")]
    [SerializeField] private TerrainMaterial material;
    [Tooltip("The same seed always gives the same textures.")]
    [SerializeField] private int seed = 1;
    [Tooltip("Cells the textures cover before they repeat, 32 px each.")]
    [SerializeField, Range(1, 8)] private int repeatCells = 4;
    [Tooltip("Waves of the smooth height the soft look reads (Phase 2), in the height texture's red channel. Normalised into 0-1 by their total amplitude, and scaled down further where the red channel's slope could reach 0.1 per pixel (the cell field contract allows 0.2).")]
    [SerializeField] private Wave[] heightWaves =
    {
        new(1, 0, 1f), new(0, 1, 0.9f), new(1, 1, 0.7f), new(2, -1, 0.5f), new(-1, 2, 0.45f), new(3, 1, 0.25f)
    };

    public TerrainMaterial Material => material;
    public int Seed => seed;
    public int RepeatCells => repeatCells;

    /// <summary>
    /// What the cell field this recipe writes holds, which the material must be set to read.
    /// </summary>
    public abstract TerrainMaterial.CellMode CellMode { get; }

    /// <summary>
    /// Generates the material's textures from <paramref name="textureSeed"/>, the same seed always giving the same
    /// pixels.
    /// </summary>
    public TerrainTextures Generate(int textureSeed)
    {
        return Generate(new System.Random(textureSeed), repeatCells * TerrainStyle.PixelsPerCell);
    }

    /// <summary>
    /// Generates textures of <paramref name="size"/> pixels square, drawing every random value from
    /// <paramref name="random"/>.
    /// </summary>
    protected abstract TerrainTextures Generate(System.Random random, int size);

    /// <summary>
    /// How many seeds a grid of <paramref name="perCell"/> seeds along each side of a cell puts along each side of the
    /// texture, at least one.
    /// </summary>
    protected int SeedsAcross(float perCell)
    {
        return Math.Max(1, TerrainArt.Round(perCell * repeatCells));
    }

    /// <summary>
    /// The smooth height in [0, 1] of every pixel, from the height waves with phases drawn from
    /// <paramref name="random"/>: 0.5 plus half the waves' sum over their total amplitude, or over more where the
    /// height's slope could otherwise reach 0.1 per pixel.
    /// </summary>
    protected double[] SmoothHeight(System.Random random, int size)
    {
        double total = 0.0;
        foreach (Wave wave in heightWaves)
        {
            total += Math.Abs(wave.Amplitude);
        }

        double scale = Math.Max(total, 0.5 * TerrainArt.MaxSlope(heightWaves, size) / MaxHeightSlope);
        double[] height = TerrainArt.PeriodicNoise(random, heightWaves, size);
        for (int i = 0; i < height.Length; i++)
        {
            height[i] = scale > 0.0 ? 0.5 + 0.5 * height[i] / scale : 0.5;
        }

        return height;
    }
}
