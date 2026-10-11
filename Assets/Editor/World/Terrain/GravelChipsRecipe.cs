using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gravel: packed dirt with slow tint patches, scattered with small rounded chips, each a tilted ellipse a few pixels
/// across, lit from the top left and casting a one-pixel shadow on the dirt to its bottom right. Chips sit on a jittered
/// grid of spots, each holding a chip or none; where chips overlap, a pixel goes to the chip it lies deepest in. The
/// cell field (whole chips) points every pixel of a chip and of its shadow at the chip's centre pixel (its pixel
/// nearest its seed) and holds the chip's reach, the distance from that centre to its farthest pixel, shadow included,
/// so the terrain shader keeps or leaves out each chip whole, shadow and all. Its blue channel is the dirt's shade
/// under every pixel, alpha 0 where no chip is; the base colour is the dirt colour.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Gravel Chips", fileName = "Recipe_Gravel")]
public sealed class GravelChipsRecipe : TerrainMaterialRecipe
{
    private const int GritSalt = 7;

    /// <summary>
    /// One chip: its seed, its ellipse's radii along and across its angle, and its colour.
    /// </summary>
    private readonly struct Chip
    {
        public Chip(double x, double y, double radiusAlong, double radiusAcross, double angle, Color32 colour)
        {
            X = x;
            Y = y;
            RadiusAlong = radiusAlong;
            RadiusAcross = radiusAcross;
            Cos = Math.Cos(angle);
            Sin = Math.Sin(angle);
            Colour = colour;
        }

        public double X { get; }
        public double Y { get; }
        public double RadiusAlong { get; }
        public double RadiusAcross { get; }
        public double Cos { get; }
        public double Sin { get; }
        public Color32 Colour { get; }

        /// <summary>
        /// How far a point offset by (<paramref name="dx"/>, <paramref name="dy"/>) from the seed lies inside the
        /// ellipse: 1 on its rim, below 1 inside.
        /// </summary>
        public double Depth(double dx, double dy)
        {
            double u = (dx * Cos + dy * Sin) / RadiusAlong;
            double v = (-dx * Sin + dy * Cos) / RadiusAcross;
            return Math.Sqrt(u * u + v * v);
        }
    }

    /// <summary>
    /// Where each chip lies: per pixel, the chip it belongs to (its own pixel or its shadow, -1 for bare dirt), whether
    /// it is the shadow, its depth in the chip and offset from its seed, and per chip its centre pixel and reach.
    /// </summary>
    private sealed class Placement
    {
        public Placement(int pixels, int chipCount)
        {
            Owner = new int[pixels];
            IsShadow = new bool[pixels];
            Depth = new double[pixels];
            OffsetX = new double[pixels];
            OffsetY = new double[pixels];
            CentreX = new int[chipCount];
            CentreY = new int[chipCount];
            Reach = new double[chipCount];
        }

        public int[] Owner { get; }
        public bool[] IsShadow { get; }
        public double[] Depth { get; }
        public double[] OffsetX { get; }
        public double[] OffsetY { get; }
        public int[] CentreX { get; }
        public int[] CentreY { get; }
        public double[] Reach { get; }
    }

    [Header("Dirt")]
    [Tooltip("The packed dirt, and the material's base colour.")]
    [SerializeField] private Color32 dirt = new(146, 122, 88, 255);
    [Tooltip("Slow tint patches of the dirt, in 0-255 steps.")]
    [SerializeField] private Wave[] tintWaves = { new(1, 0, 4f), new(0, 1, 3f), new(2, 2, 3f), new(3, -1, 2f) };
    [Tooltip("Per-pixel noise of the dirt, in 0-255 steps from darkest to lightest.")]
    [SerializeField, Range(0f, 32f)] private float dirtGrit = 7f;
    [Header("Chips")]
    [Tooltip("Chip spots along each side of a cell, on a jittered grid.")]
    [SerializeField, Range(1f, 12f)] private float spotsPerCell = 6.75f;
    [Tooltip("How far a spot moves from its grid position, in grid steps.")]
    [SerializeField, Range(0f, 0.5f)] private float jitter = 0.4f;
    [Tooltip("Chance that a spot holds a chip, before the density waves.")]
    [SerializeField, Range(0f, 1f)] private float chipChance = 0.72f;
    [Tooltip("Waves added to the chip chance, so chips cluster here and there.")]
    [SerializeField] private Wave[] densityWaves = { new(3, 2, 0.08f), new(-2, 3, 0.08f), new(4, -1, 0.06f) };
    [Tooltip("Most chips take a colour between this one and the grey one.")]
    [SerializeField] private Color32 lightChip = new(180, 164, 132, 255);
    [Tooltip("Most chips take a colour between the light one and this one.")]
    [SerializeField] private Color32 greyChip = new(160, 152, 138, 255);
    [Tooltip("The colour of the other chips.")]
    [SerializeField] private Color32 darkChip = new(126, 110, 88, 255);
    [Tooltip("Chance that a chip is dark.")]
    [SerializeField, Range(0f, 1f)] private float darkChance = 0.16f;
    [Tooltip("Each chip's colour is shaded by up to this much either way, in 0-255 steps.")]
    [SerializeField, Range(0f, 32f)] private float colourJitter = 10f;
    [Tooltip("Smallest mean radius of a chip, in pixels.")]
    [SerializeField, Range(0.5f, 6f)] private float minRadius = 1.15f;
    [Tooltip("Largest mean radius of a chip, in pixels.")]
    [SerializeField, Range(0.5f, 6f)] private float maxRadius = 2.1f;
    [Tooltip("Smallest stretch of a chip: its radius along its angle is the mean radius times the stretch, across it the mean radius over the stretch.")]
    [SerializeField, Range(0.5f, 2f)] private float minStretch = 0.8f;
    [Tooltip("Largest stretch of a chip.")]
    [SerializeField, Range(0.5f, 2f)] private float maxStretch = 1.25f;
    [Tooltip("Per-pixel noise of the chips, in 0-255 steps from darkest to lightest.")]
    [SerializeField, Range(0f, 32f)] private float chipGrit = 5f;
    [Tooltip("How much the light changes a chip's colour at its rim, in 0-255 steps.")]
    [SerializeField, Range(0f, 64f)] private float bevel = 22f;
    [Tooltip("Where the bevel starts, as a fraction of the way from a chip's centre to its rim.")]
    [SerializeField, Range(0f, 0.95f)] private float bevelStart = 0.35f;
    [Tooltip("How much darker the dirt is in a chip's shadow, one pixel to its bottom right, in 0-255 steps.")]
    [SerializeField, Range(0f, 64f)] private float shadow = 16f;

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.WholeChips;

    /// <summary>
    /// Draws the tint and density waves, the chip spots, each spot's chip (kept or not, colour, size, stretch, angle and
    /// shade), a value per kept chip, then the height waves; places the chips and their shadows and paints the dirt,
    /// the chips, the cell field and the height.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        double[] tint = TerrainArt.PeriodicNoise(random, tintWaves, size);
        double[] density = TerrainArt.PeriodicNoise(random, densityWaves, size);
        (double X, double Y)[] spots = TerrainArt.JitteredSeeds(random, SeedsAcross(spotsPerCell), jitter, size);
        List<Chip> chips = new();
        foreach ((double x, double y) in spots)
        {
            bool keep = random.NextDouble() < chipChance + density[(int)y * size + (int)x];
            bool dark = random.NextDouble() < darkChance;
            Color32 colour = TerrainArt.Lerp(lightChip, greyChip, random.NextDouble());
            double radius = TerrainArt.Uniform(random, minRadius, maxRadius);
            double stretch = TerrainArt.Uniform(random, minStretch, maxStretch);
            double angle = TerrainArt.Uniform(random, 0.0, Math.PI);
            double shade = TerrainArt.Uniform(random, -colourJitter, colourJitter);
            if (keep)
            {
                chips.Add(new Chip(x, y, radius * stretch, radius / stretch, angle, TerrainArt.Shade(dark ? darkChip : colour, shade)));
            }
        }

        double[] values = new double[chips.Count];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = random.NextDouble();
        }

        double[] height = SmoothHeight(random, size);
        Placement placement = Place(chips, size);
        return Paint(chips, placement, tint, size, TerrainArt.HeightPixels(height, placement.Owner, values));
    }

    /// <summary>
    /// Gives each pixel inside a chip's ellipse to the chip it lies deepest in (the first one on a tie), each bare
    /// pixel whose top left neighbour belongs to a chip (not its shadow) to that chip as shadow, takes each chip's
    /// pixel nearest its seed as its centre and its farthest pixel as its reach.
    /// </summary>
    private static Placement Place(List<Chip> chips, int size)
    {
        Placement placement = new(size * size, chips.Count);
        for (int i = 0; i < placement.Owner.Length; i++)
        {
            placement.Owner[i] = -1;
            placement.Depth[i] = double.MaxValue;
        }

        for (int c = 0; c < chips.Count; c++)
        {
            Chip chip = chips[c];
            int reach = (int)Math.Ceiling(Math.Max(chip.RadiusAlong, chip.RadiusAcross)) + 1;
            int seedX = (int)Math.Floor(chip.X);
            int seedY = (int)Math.Floor(chip.Y);
            for (int oy = -reach; oy <= reach; oy++)
            {
                for (int ox = -reach; ox <= reach; ox++)
                {
                    int x = TerrainArt.Wrap(seedX + ox, size);
                    int y = TerrainArt.Wrap(seedY + oy, size);
                    double dx = TerrainArt.WrapDelta(x + 0.5 - chip.X, size);
                    double dy = TerrainArt.WrapDelta(y + 0.5 - chip.Y, size);
                    double depth = chip.Depth(dx, dy);
                    int i = y * size + x;
                    if (depth <= 1.0 && depth < placement.Depth[i])
                    {
                        placement.Owner[i] = c;
                        placement.Depth[i] = depth;
                        placement.OffsetX[i] = dx;
                        placement.OffsetY[i] = dy;
                    }
                }
            }
        }

        double[] nearest = new double[chips.Count];
        for (int c = 0; c < nearest.Length; c++)
        {
            nearest[c] = double.MaxValue;
        }

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                int owner = placement.Owner[i];
                if (owner < 0)
                {
                    continue;
                }

                double distance = placement.OffsetX[i] * placement.OffsetX[i] + placement.OffsetY[i] * placement.OffsetY[i];
                if (distance < nearest[owner])
                {
                    nearest[owner] = distance;
                    placement.CentreX[owner] = x;
                    placement.CentreY[owner] = y;
                }
            }
        }

        int[] chipOwner = (int[])placement.Owner.Clone();
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                int topLeft = chipOwner[TerrainArt.Wrap(y + 1, size) * size + TerrainArt.Wrap(x - 1, size)];
                if (chipOwner[i] < 0 && topLeft >= 0)
                {
                    placement.Owner[i] = topLeft;
                    placement.IsShadow[i] = true;
                }
            }
        }

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int owner = placement.Owner[y * size + x];
                if (owner >= 0)
                {
                    int ox = TerrainArt.WrapDelta(placement.CentreX[owner] - x, size);
                    int oy = TerrainArt.WrapDelta(placement.CentreY[owner] - y, size);
                    placement.Reach[owner] = Math.Max(placement.Reach[owner], Math.Sqrt(ox * ox + oy * oy));
                }
            }
        }

        return placement;
    }

    /// <summary>
    /// Paints the dirt, the chips lit from the top left and their shadows, and the cell field: offset to the chip's
    /// centre pixel plus 128, the dirt's shade plus 128, and the chip's reach times 16 (alpha 0 for bare dirt).
    /// </summary>
    private TerrainTextures Paint(List<Chip> chips, Placement placement, double[] tint, int size, Color32[] height)
    {
        Color32[] albedo = new Color32[size * size];
        Color32[] field = new Color32[size * size];
        double bevelSpan = Math.Max(1e-6, 1.0 - bevelStart);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                double hash = TerrainArt.Hash01(x, y, GritSalt) - 0.5;
                int amount = Mathf.Clamp(TerrainArt.Round(tint[i] + hash * dirtGrit), -128, 127);
                Color32 ground = TerrainArt.Shade(dirt, amount);
                int owner = placement.Owner[i];
                if (owner < 0)
                {
                    albedo[i] = ground;
                    field[i] = new Color32(128, 128, (byte)(128 + amount), 0);
                    continue;
                }

                if (placement.IsShadow[i])
                {
                    albedo[i] = TerrainArt.Shade(ground, -shadow);
                }
                else
                {
                    double dx = placement.OffsetX[i];
                    double dy = placement.OffsetY[i];
                    double edge = TerrainArt.Clamp01((placement.Depth[i] - bevelStart) / bevelSpan);
                    albedo[i] = TerrainArt.Shade(chips[owner].Colour, TerrainArt.BevelLight(dx, dy) * bevel * edge + hash * chipGrit);
                }

                int ox = TerrainArt.WrapDelta(placement.CentreX[owner] - x, size);
                int oy = TerrainArt.WrapDelta(placement.CentreY[owner] - y, size);
                byte reach = (byte)Mathf.Clamp((int)Math.Ceiling(placement.Reach[owner] * 16.0), 1, 255);
                field[i] = new Color32((byte)(128 + ox), (byte)(128 + oy), (byte)(128 + amount), reach);
            }
        }

        return new TerrainTextures(size, dirt, CellMode, albedo, field, height);
    }
}
