using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Molten lava, for an animated material (Docs/TerrainTypes.md, Phase 9 notes): a red surface in two reds with ragged
/// orange patches and hotter cores, each patch in its own orange (its hue and brightness moved by up to a set amount,
/// so neighbouring patches differ), and small dark red specks of crust on the cooler red. The surface has no direction
/// and does not move; what moves are bubbles, discs of pixels whose glint phases fall behind with the distance from
/// the bubble's middle, so with every pixel lit for the same duty a bubble swells from a point, opens into a ring as
/// its middle goes out first, and is gone. A bubble of two pixels' radius or more has a dark rim of sparkle phases on
/// the same timing, which needs the material's sparkle period and duty to match its glint's. No cell field; the base
/// colour is the red, the glint colour the bubbles' and the sparkle colour their rims', and the height's per-chip
/// value is 0.5 everywhere.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Recipe/Lava Bubbles", fileName = "Recipe_Lava")]
public sealed class LavaBubblesRecipe : TerrainMaterialRecipe
{
    private static readonly Vector2Int[] Neighbours = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    [Header("Surface")]
    [Tooltip("The red, and the material's base colour.")]
    [SerializeField] private Color32 red = new(196, 40, 22, 255);
    [Tooltip("The darker red, where the slow tint noise is highest.")]
    [SerializeField] private Color32 darkerRed = new(184, 34, 20, 255);
    [Tooltip("Share of the surface in the darker red.")]
    [SerializeField, Range(0f, 1f)] private float darkerShare = 0.45f;
    [Tooltip("Lattice points of the slow tint noise along each side of a cell.")]
    [SerializeField, Range(0.25f, 8f)] private float tintPerCell = 1.33f;
    [Header("Hot patches")]
    [Tooltip("The orange of a patch, before its own shift.")]
    [SerializeField] private Color32 orange = new(226, 92, 28, 255);
    [Tooltip("The orange of a patch's hotter core, before the patch's shift.")]
    [SerializeField] private Color32 core = new(242, 128, 40, 255);
    [Tooltip("Lattice points of the patches' noise along each side of a cell: more gives smaller patches.")]
    [SerializeField, Range(0.5f, 16f)] private float patchesPerCell = 3.2f;
    [Tooltip("Lattice points of the finer noise that roughens the patches' outlines, along each side of a cell.")]
    [SerializeField, Range(1f, 16f)] private float detailPerCell = 8f;
    [Tooltip("How much the finer noise counts against the patches' own.")]
    [SerializeField, Range(0f, 1f)] private float detail = 0.35f;
    [Tooltip("Share of the surface in orange patches, cores included.")]
    [SerializeField, Range(0f, 0.5f)] private float hotShare = 0.16f;
    [Tooltip("Share of the surface in the patches' cores.")]
    [SerializeField, Range(0f, 0.5f)] private float coreShare = 0.07f;
    [Tooltip("How far each patch's hue moves either way at most, in degrees, so neighbouring patches differ in tone.")]
    [SerializeField, Range(0f, 30f)] private float hueRange = 12f;
    [Tooltip("How far each patch's brightness moves either way at most, as a share of it.")]
    [SerializeField, Range(0f, 0.3f)] private float valueRange = 0.08f;
    [Header("Crust specks")]
    [Tooltip("The specks' dark red.")]
    [SerializeField] private Color32 speck = new(132, 26, 18, 255);
    [Tooltip("Lattice points of the specks' noise along each side of a cell.")]
    [SerializeField, Range(1f, 16f)] private float specksPerCell = 6.4f;
    [Tooltip("Share of the cooler half of the surface (away from the patches) under specks.")]
    [SerializeField, Range(0f, 0.5f)] private float speckShare = 0.12f;
    [Header("Bubbles")]
    [Tooltip("Bubbles per cell.")]
    [SerializeField, Range(0f, 2f)] private float bubblesPerCell = 0.35f;
    [Tooltip("Smallest bubble radius, in pixels.")]
    [SerializeField, Range(0.5f, 6f)] private float smallestBubble = 2.2f;
    [Tooltip("Largest bubble radius, in pixels.")]
    [SerializeField, Range(0.5f, 6f)] private float largestBubble = 4.2f;
    [Tooltip("How far behind a bubble's pixel lights for each pixel it lies from the middle, as a share of the glint period: the bubble swells over its radius times this.")]
    [SerializeField, Range(0f, 0.1f)] private float swellDelay = 0.025f;
    [Tooltip("The colour a bubble lights up with, the material's glint colour.")]
    [SerializeField] private Color32 bubble = new(250, 160, 60, 255);
    [Tooltip("The colour a bubble's rim lights up with, the material's sparkle colour.")]
    [SerializeField] private Color32 bubbleRim = new(130, 26, 18, 255);

    public override TerrainMaterial.CellMode CellMode => TerrainMaterial.CellMode.None;

    /// <summary>
    /// Draws the tint, patch, detail and speck noises, the height, each patch's tone shift and the bubbles, in that
    /// order, then colours every pixel and gives the bubbles' pixels their phases.
    /// </summary>
    protected override TerrainTextures Generate(System.Random random, int size)
    {
        int cells = Math.Max(1, size / TerrainStyle.PixelsPerCell);
        double[] tint = TerrainArt.PeriodicValueNoise(random, Lattice(tintPerCell, cells), size);
        double[] patches = TerrainArt.PeriodicValueNoise(random, Lattice(patchesPerCell, cells), size);
        double[] fine = TerrainArt.PeriodicValueNoise(random, Lattice(detailPerCell, cells), size);
        double[] specks = TerrainArt.PeriodicValueNoise(random, Lattice(specksPerCell, cells), size);
        double[] height = SmoothHeight(random, size);
        double[] heat = new double[size * size];
        for (int i = 0; i < heat.Length; i++)
        {
            heat[i] = (1.0 - detail) * patches[i] + detail * fine[i];
        }

        double darkLevel = TerrainArt.Level(tint, 1.0 - darkerShare);
        double hotLevel = TerrainArt.Level(heat, 1.0 - hotShare);
        double coreLevel = TerrainArt.Level(heat, 1.0 - coreShare);
        double coolLevel = TerrainArt.Level(heat, 0.5);
        double speckLevel = TerrainArt.Level(specks, 1.0 - speckShare);
        int[] patch = Patches(heat, hotLevel, size, out int patchCount);
        Color32[] oranges = new Color32[patchCount];
        Color32[] cores = new Color32[patchCount];
        for (int p = 0; p < patchCount; p++)
        {
            double hue = TerrainArt.Uniform(random, -hueRange, hueRange) / 360.0;
            double value = TerrainArt.Uniform(random, -valueRange, valueRange);
            oranges[p] = Shifted(orange, hue, value);
            cores[p] = Shifted(core, hue, value);
        }

        Color32[] albedo = new Color32[size * size];
        for (int i = 0; i < albedo.Length; i++)
        {
            albedo[i] = patch[i] >= 0 ? heat[i] >= coreLevel ? cores[patch[i]] : oranges[patch[i]] : heat[i] < coolLevel && specks[i] >= speckLevel ? speck : tint[i] >= darkLevel ? darkerRed : red;
        }

        byte[] glints = Bubbles(random, size, cells);
        return new TerrainTextures(size, red, CellMode, albedo, null, TerrainArt.HeightPixels(height, null, null, glints), bubble, bubbleRim);
    }

    /// <summary>
    /// Places the bubbles, each at a random spot and radius where it keeps a pixel clear of every bubble before it
    /// (a few tries per bubble, wrapping at the texture's edges), and gives each pixel within its radius a glint phase
    /// that falls behind with the distance from its middle, or a sparkle phase on the same timing on the outermost
    /// pixel of a bubble of two pixels' radius or more.
    /// </summary>
    private byte[] Bubbles(System.Random random, int size, int cells)
    {
        byte[] glints = new byte[size * size];
        bool[] taken = new bool[size * size];
        int wanted = TerrainArt.Round(bubblesPerCell * cells * cells);
        double smallest = Math.Min(smallestBubble, largestBubble);
        double largest = Math.Max(smallestBubble, largestBubble);
        for (int b = 0; b < wanted; b++)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                double radius = TerrainArt.Uniform(random, smallest, largest);
                double cx = TerrainArt.Uniform(random, 0.0, size);
                double cy = TerrainArt.Uniform(random, 0.0, size);
                double phase = random.NextDouble();
                int reach = (int)Math.Ceiling(radius) + 1;
                if (Overlaps(taken, cx, cy, reach, size))
                {
                    continue;
                }

                for (int dy = -reach; dy <= reach; dy++)
                {
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        int x = TerrainArt.Wrap((int)Math.Floor(cx) + dx, size);
                        int y = TerrainArt.Wrap((int)Math.Floor(cy) + dy, size);
                        double distance = Math.Sqrt(Square(Math.Floor(cx) + dx + 0.5 - cx) + Square(Math.Floor(cy) + dy + 0.5 - cy));
                        if (distance > radius)
                        {
                            continue;
                        }

                        int i = y * size + x;
                        taken[i] = true;
                        double pixelPhase = phase - swellDelay * distance;
                        glints[i] = radius >= 2.0 && distance > radius - 1.0 ? TerrainArt.SparkleByte(pixelPhase) : TerrainArt.GlintByte(pixelPhase);
                    }
                }

                break;
            }
        }

        return glints;
    }

    /// <summary>
    /// Whether any pixel within <paramref name="reach"/> of (<paramref name="cx"/>, <paramref name="cy"/>), wrapping,
    /// already belongs to a bubble.
    /// </summary>
    private static bool Overlaps(bool[] taken, double cx, double cy, int reach, int size)
    {
        for (int dy = -reach - 1; dy <= reach + 1; dy++)
        {
            for (int dx = -reach - 1; dx <= reach + 1; dx++)
            {
                if (taken[TerrainArt.Wrap((int)Math.Floor(cy) + dy, size) * size + TerrainArt.Wrap((int)Math.Floor(cx) + dx, size)])
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The connected patches (four neighbours, wrapping) of pixels whose heat is at or above <paramref name="level"/>:
    /// each pixel's patch index, or -1 off the patches, numbered in the order a scan from the first pixel meets them.
    /// </summary>
    private static int[] Patches(double[] heat, double level, int size, out int count)
    {
        int[] patch = new int[heat.Length];
        for (int i = 0; i < patch.Length; i++)
        {
            patch[i] = -1;
        }

        count = 0;
        Stack<int> open = new();
        for (int start = 0; start < heat.Length; start++)
        {
            if (patch[start] >= 0 || heat[start] < level)
            {
                continue;
            }

            patch[start] = count;
            open.Push(start);
            while (open.Count > 0)
            {
                int i = open.Pop();
                int x = i % size;
                int y = i / size;
                foreach (Vector2Int step in Neighbours)
                {
                    int n = TerrainArt.Wrap(y + step.y, size) * size + TerrainArt.Wrap(x + step.x, size);
                    if (patch[n] < 0 && heat[n] >= level)
                    {
                        patch[n] = count;
                        open.Push(n);
                    }
                }
            }

            count++;
        }

        return patch;
    }

    /// <summary>
    /// <paramref name="colour"/> with its hue turned by <paramref name="hue"/> of a full turn and its brightness moved
    /// by <paramref name="value"/> of itself, opaque.
    /// </summary>
    private static Color32 Shifted(Color32 colour, double hue, double value)
    {
        Color.RGBToHSV(colour, out float h, out float s, out float v);
        Color shifted = Color.HSVToRGB(Mathf.Repeat(h + (float)hue, 1f), s, Mathf.Clamp01(v * (1f + (float)value)));
        return new Color32(TerrainArt.ToByte(shifted.r * 255.0), TerrainArt.ToByte(shifted.g * 255.0), TerrainArt.ToByte(shifted.b * 255.0), 255);
    }

    /// <summary>
    /// Lattice points across a texture of <paramref name="cells"/> cells for <paramref name="perCell"/> per cell.
    /// </summary>
    private static int Lattice(float perCell, int cells)
    {
        return Math.Max(1, TerrainArt.Round(perCell * cells));
    }

    /// <summary>
    /// <paramref name="value"/> times itself.
    /// </summary>
    private static double Square(double value)
    {
        return value * value;
    }
}
