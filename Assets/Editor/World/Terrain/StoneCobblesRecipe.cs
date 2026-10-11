using System;
using UnityEngine;

/// <summary>
/// Stone: cobbles in dark mortar. Each pixel belongs to the stone whose seed is nearest (a jittered grid of seeds);
/// pixels within the mortar width of their stone's cell edge are mortar, and each stone is lit from the top left along
/// its own edges. The cell field (cut stones) points every pixel at its stone's centre pixel (its pixel nearest its
/// seed) and holds the pixel's exact distance to its stone's cell edge, so the terrain shader can leave a stone out or
/// cut it along the rim; its blue channel is the mortar's shade under every pixel. The base colour is the mortar colour.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Stone Cobbles", fileName = "Recipe_Stone")]
public sealed class StoneCobblesRecipe : TerrainMaterialRecipe
{
    private const int GritSalt = 11;

    [Header("Mortar")]
    [Tooltip("The mortar between the stones, and the material's base colour.")]
    [SerializeField] private Color32 mortar = new(98, 88, 76, 255);
    [Tooltip("Pixels this close to their stone's cell edge, in pixels, are mortar.")]
    [SerializeField, Range(0f, 2f)] private float mortarWidth = 0.5f;
    [Tooltip("Per-pixel noise of the mortar and stones, in 0-255 steps from darkest to lightest.")]
    [SerializeField, Range(0f, 32f)] private float grit = 7f;
    [Header("Stones")]
    [Tooltip("Stones along each side of a cell, on a jittered grid.")]
    [SerializeField, Range(1f, 8f)] private float stonesPerCell = 4.5f;
    [Tooltip("How far a stone's seed moves from its grid position, in grid steps.")]
    [SerializeField, Range(0f, 0.45f)] private float jitter = 0.32f;
    [Tooltip("Each stone takes one of these colours at random.")]
    [SerializeField] private Color32[] palette =
    {
        new(152, 148, 138, 255), new(166, 160, 148, 255), new(140, 136, 126, 255), new(176, 170, 158, 255),
        new(130, 126, 118, 255), new(158, 150, 136, 255), new(146, 144, 138, 255)
    };
    [Tooltip("How much the light changes a stone's colour along its edge, in 0-255 steps.")]
    [SerializeField, Range(0f, 64f)] private float bevel = 18f;
    [Tooltip("Width in pixels of the bevel inside the mortar.")]
    [SerializeField, Range(0.25f, 4f)] private float bevelWidth = 1.25f;
    [Tooltip("How much the light changes a stone's colour away from its seed, in 0-255 steps, for a rounded look.")]
    [SerializeField, Range(0f, 32f)] private float radial = 4f;
    [Tooltip("Distance in pixels from the seed at which the rounded look is strongest.")]
    [SerializeField, Range(0.5f, 16f)] private float radialRadius = 3.5f;
    [Tooltip("How much darker the bevel is on a stone's side that faces away from the light, in 0-255 steps.")]
    [SerializeField, Range(0f, 32f)] private float shadedSide = 8f;
    [Tooltip("How far a side must face away from the light to be shaded, from 0 (sideways) to 1 (straight away).")]
    [SerializeField, Range(0f, 1f)] private float shadedSideFrom = 0.35f;

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.CutStones;

    /// <summary>
    /// Draws the seeds, a colour and a value per stone, then the height waves; finds each pixel's stone, offset from its
    /// seed and distance to its cell edge, and paints the mortar, the stones, the cell field and the height.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        int cells = SeedsAcross(stonesPerCell);
        (double X, double Y)[] seeds = TerrainArt.JitteredSeeds(random, cells, jitter, size);
        Color32[] colours = new Color32[seeds.Length];
        for (int s = 0; s < seeds.Length; s++)
        {
            colours[s] = palette.Length > 0 ? palette[random.Next(palette.Length)] : mortar;
        }

        double[] values = new double[seeds.Length];
        for (int s = 0; s < seeds.Length; s++)
        {
            values[s] = random.NextDouble();
        }

        double[] height = SmoothHeight(random, size);
        int[] owner = new int[size * size];
        double[] offsetX = new double[size * size];
        double[] offsetY = new double[size * size];
        double[] border = new double[size * size];
        FindStones(seeds, cells, size, owner, offsetX, offsetY, border);
        int[] centreX = new int[seeds.Length];
        int[] centreY = new int[seeds.Length];
        FindCentres(owner, offsetX, offsetY, size, centreX, centreY);

        Color32[] albedo = new Color32[size * size];
        Color32[] field = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                double pixelGrit = (TerrainArt.Hash01(x, y, GritSalt) - 0.5) * grit;
                int amount = Mathf.Clamp(TerrainArt.Round(pixelGrit), -128, 127);
                int stone = owner[i];
                albedo[i] = border[i] < mortarWidth ? TerrainArt.Shade(mortar, amount) : StonePixel(colours[stone], offsetX[i], offsetY[i], border[i], pixelGrit);
                int ox = TerrainArt.WrapDelta(centreX[stone] - x, size);
                int oy = TerrainArt.WrapDelta(centreY[stone] - y, size);
                byte distance = (byte)Mathf.Clamp(TerrainArt.Round(border[i] * 16.0), 1, 255);
                field[i] = new Color32((byte)(128 + ox), (byte)(128 + oy), (byte)(128 + amount), distance);
            }
        }

        return new TerrainTextures(size, mortar, CellMode, albedo, field, TerrainArt.HeightPixels(height, owner, values));
    }

    /// <summary>
    /// For every pixel, the stone whose seed lies nearest its centre (the lowest seed index on a tie), the offset from
    /// that seed and the exact distance to the stone's cell edge: the nearest of the lines halfway between its seed and
    /// any other seed. Seeds are searched ring by ring of the seed grid around the pixel's grid cell, and the search
    /// stops once no farther ring can hold a nearer seed or a nearer edge, so the cost per pixel does not grow with the
    /// number of seeds.
    /// </summary>
    private void FindStones((double X, double Y)[] seeds, int cells, int size, int[] owner, double[] offsetX, double[] offsetY, double[] border)
    {
        double cellSize = (double)size / cells;
        double overhang = Math.Max(0.0, jitter - 0.5) * cellSize;
        int maxRing = cells / 2;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double px = x + 0.5;
                double py = y + 0.5;
                int column = Mathf.Clamp((int)(px / cellSize), 0, cells - 1);
                int row = Mathf.Clamp((int)(py / cellSize), 0, cells - 1);
                int nearest = -1;
                double nearestSquare = double.MaxValue;
                for (int ring = 0; ring <= maxRing; ring++)
                {
                    double ringDistance = (ring - 1) * cellSize - overhang;
                    if (nearest >= 0 && ringDistance > 0.0 && ringDistance * ringDistance > nearestSquare)
                    {
                        break;
                    }

                    for (int dy = -ring; dy <= ring; dy++)
                    {
                        for (int dx = -ring; dx <= ring; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring)
                            {
                                continue;
                            }

                            int s = TerrainArt.Wrap(row + dy, cells) * cells + TerrainArt.Wrap(column + dx, cells);
                            double sx = TerrainArt.WrapDelta(px - seeds[s].X, size);
                            double sy = TerrainArt.WrapDelta(py - seeds[s].Y, size);
                            double square = sx * sx + sy * sy;
                            if (square < nearestSquare || (square == nearestSquare && s < nearest))
                            {
                                nearest = s;
                                nearestSquare = square;
                            }
                        }
                    }
                }

                double nearestX = TerrainArt.WrapDelta(px - seeds[nearest].X, size);
                double nearestY = TerrainArt.WrapDelta(py - seeds[nearest].Y, size);
                double nearestDistance = Math.Sqrt(nearestSquare);
                double edge = double.MaxValue;
                for (int ring = 0; ring <= maxRing; ring++)
                {
                    if (((ring - 1) * cellSize - overhang - nearestDistance) / 2.0 >= edge)
                    {
                        break;
                    }

                    for (int dy = -ring; dy <= ring; dy++)
                    {
                        for (int dx = -ring; dx <= ring; dx++)
                        {
                            int s = TerrainArt.Wrap(row + dy, cells) * cells + TerrainArt.Wrap(column + dx, cells);
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring || s == nearest)
                            {
                                continue;
                            }

                            double sx = TerrainArt.WrapDelta(px - seeds[s].X, size);
                            double sy = TerrainArt.WrapDelta(py - seeds[s].Y, size);
                            double gapX = sx - nearestX;
                            double gapY = sy - nearestY;
                            double gap = Math.Sqrt(gapX * gapX + gapY * gapY);
                            if (gap > 1e-6)
                            {
                                edge = Math.Min(edge, (sx * sx + sy * sy - nearestSquare) / (2.0 * gap));
                            }
                        }
                    }
                }

                int i = y * size + x;
                owner[i] = nearest;
                offsetX[i] = nearestX;
                offsetY[i] = nearestY;
                border[i] = edge;
            }
        }
    }

    /// <summary>
    /// Each stone's centre pixel: its pixel nearest its seed, the first in row order on a tie.
    /// </summary>
    private static void FindCentres(int[] owner, double[] offsetX, double[] offsetY, int size, int[] centreX, int[] centreY)
    {
        double[] nearest = new double[centreX.Length];
        for (int s = 0; s < nearest.Length; s++)
        {
            nearest[s] = double.MaxValue;
        }

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                double distance = offsetX[i] * offsetX[i] + offsetY[i] * offsetY[i];
                if (distance < nearest[owner[i]])
                {
                    nearest[owner[i]] = distance;
                    centreX[owner[i]] = x;
                    centreY[owner[i]] = y;
                }
            }
        }
    }

    /// <summary>
    /// A stone pixel offset by (<paramref name="dx"/>, <paramref name="dy"/>) from its seed and
    /// <paramref name="border"/> pixels from its cell edge: its colour lit from the top left, strongest in the bevel
    /// along its edge, a little away from its seed, and darker on the bevel's side facing away from the light.
    /// </summary>
    private Color32 StonePixel(Color32 colour, double dx, double dy, double border, double pixelGrit)
    {
        double lit = TerrainArt.BevelLight(dx, dy);
        double bevelAmount = TerrainArt.Clamp01(1.0 - (border - mortarWidth) / bevelWidth);
        double radialAmount = TerrainArt.Clamp01(Math.Sqrt(dx * dx + dy * dy) / radialRadius);
        double amount = lit * (bevel * bevelAmount + radial * radialAmount) + pixelGrit;
        if (bevelAmount > 0.0 && lit < -shadedSideFrom)
        {
            amount -= shadedSide * bevelAmount;
        }

        return TerrainArt.Shade(colour, amount);
    }
}
