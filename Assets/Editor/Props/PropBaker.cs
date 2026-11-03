using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One baked variant of a prop: its pixels in the recipe's frame (rows from the bottom), its glow (each pixel's alpha how
/// much it glows, all 0 for a prop that does not glow, and its colour the one it flickers to, black for a pixel that
/// does not flicker), its normals (each drawn pixel's surface normal, x right, y up the screen and z toward the camera,
/// each from -1 to 1 as 0 to 255, opaque; clear where nothing is drawn and on the outlines around the drawing, Docs/Lighting.md,
/// Phase 11), the same for each frame of a moving flame's loop, the first being those, its footprint and the box
/// around its drawing in frame pixels, measured on the first frame, whether the drawing of any frame reached the frame's
/// edge, and a light prop's light: where it stands on the sprite, in units from the base (on the ground under where the
/// drawing marked it), and how strongly it shines.
/// </summary>
public sealed class PropVariant
{
    /// <summary>
    /// Creates a baked variant of one frame.
    /// </summary>
    public PropVariant(Color32[] pixels, Color32[] glow, Color32[] normals, RectInt footprint, RectInt drawn, bool atEdge, bool hasLight, Vector2 lightOffset, float lightStrength)
        : this(new[] { pixels }, new[] { glow }, new[] { normals }, footprint, drawn, atEdge, hasLight, lightOffset, lightStrength)
    {
    }

    /// <summary>
    /// Creates a baked variant of the frames of <paramref name="framePixels"/>, <paramref name="frameGlow"/> and
    /// <paramref name="frameNormals"/>, the first the one its footprint, drawing and light were measured on.
    /// </summary>
    public PropVariant(IReadOnlyList<Color32[]> framePixels, IReadOnlyList<Color32[]> frameGlow, IReadOnlyList<Color32[]> frameNormals, RectInt footprint, RectInt drawn, bool atEdge, bool hasLight, Vector2 lightOffset, float lightStrength)
    {
        FramePixels = framePixels;
        FrameGlow = frameGlow;
        FrameNormals = frameNormals;
        Footprint = footprint;
        Drawn = drawn;
        AtEdge = atEdge;
        HasLight = hasLight;
        LightOffset = lightOffset;
        LightStrength = lightStrength;
    }

    public Color32[] Pixels => FramePixels[0];
    public Color32[] Glow => FrameGlow[0];
    public Color32[] Normals => FrameNormals[0];
    public IReadOnlyList<Color32[]> FramePixels { get; }
    public IReadOnlyList<Color32[]> FrameGlow { get; }
    public IReadOnlyList<Color32[]> FrameNormals { get; }
    public RectInt Footprint { get; }
    public RectInt Drawn { get; }
    public bool AtEdge { get; }
    public bool HasLight { get; }
    public Vector2 LightOffset { get; }
    public float LightStrength { get; }

    /// <summary>
    /// Whether any of its pixels glows, in any frame.
    /// </summary>
    public bool Glows
    {
        get
        {
            foreach (Color32[] frame in FrameGlow)
            {
                foreach (Color32 value in frame)
                {
                    if (value.a > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Whether the variant has a footprint: some of its drawing touches the ground.
    /// </summary>
    public bool HasFootprint => Footprint.width > 0 && Footprint.height > 0;
}

/// <summary>
/// Draws a <see cref="PropRecipe"/>'s variants without touching an asset (Docs/PropGeneration.md, How it works). Each
/// variant seeds a <see cref="System.Random"/> from the recipe's seed and its index, is drawn by the recipe at
/// <see cref="PixelsPerCell"/> pixels a cell onto an <see cref="ArtCanvas"/> of the recipe's frame with the base on
/// its base pixel and the ground hiding what lies under it, and is shaded with the recipe's thresholds and cover noise
/// moved by its stream. Its footprint is measured from the pixels of materials touching the ground that lie no higher
/// than the recipe's contact height: the columns they span and the rows from the lowest of them to as far above the
/// base, both widened to lie evenly around the base, so the root snaps onto whole pixels. Its shadow is the sun's, cast
/// from its sprite in the game (Docs/Lighting.md, Phase 4), so none is baked. A glowing material's pixels go into its
/// glow with their flicker colours, and a light prop's light stands on the ground under where its drawing marks it
/// (Phase 5). Each drawn pixel's normal goes into its normals, for relief under the lights (Phase 11). A recipe of more than one frame, a moving flame (Phase 6), draws each frame from the same stream at its
/// share of the loop. The same recipe always gives the same pixels.
/// </summary>
public static class PropBaker
{
    public const int PixelsPerCell = 32;

    private const float NoiseRange = 97f;
    private const int FlickerSalt = 7919;

    /// <summary>
    /// Draws every variant of <paramref name="recipe"/>, which must be able to generate.
    /// </summary>
    public static List<PropVariant> BakeAll(PropRecipe recipe)
    {
        List<PropVariant> variants = new(recipe.VariantCount);
        for (int i = 0; i < recipe.VariantCount; i++)
        {
            variants.Add(Bake(recipe, i));
        }

        return variants;
    }

    /// <summary>
    /// Draws variant <paramref name="index"/> of <paramref name="recipe"/>, each of its frames at its share of the loop.
    /// </summary>
    public static PropVariant Bake(PropRecipe recipe, int index)
    {
        PropVariant first = BakeFrame(recipe, index, 0f);
        int frames = recipe.Frames;
        if (frames <= 1)
        {
            return first;
        }

        Color32[][] pixels = new Color32[frames][];
        Color32[][] glow = new Color32[frames][];
        Color32[][] normals = new Color32[frames][];
        pixels[0] = first.Pixels;
        glow[0] = first.Glow;
        normals[0] = first.Normals;
        bool atEdge = first.AtEdge;
        for (int f = 1; f < frames; f++)
        {
            PropVariant frame = BakeFrame(recipe, index, f / (float)frames);
            pixels[f] = frame.Pixels;
            glow[f] = frame.Glow;
            normals[f] = frame.Normals;
            atEdge |= frame.AtEdge;
        }

        return new PropVariant(pixels, glow, normals, first.Footprint, first.Drawn, atEdge, first.HasLight, first.LightOffset, first.LightStrength);
    }

    /// <summary>
    /// Draws variant <paramref name="index"/> of <paramref name="recipe"/> at <paramref name="phase"/> of a moving
    /// flame's loop.
    /// </summary>
    private static PropVariant BakeFrame(PropRecipe recipe, int index, float phase)
    {
        ArtCanvas canvas = Render(recipe, index, out PropDrawing drawing, phase);
        Vector3 noise = new(drawing.Range(0f, NoiseRange), drawing.Range(0f, NoiseRange), drawing.Range(0f, NoiseRange));
        Color32[] pixels = canvas.Shade(drawing.Materials, recipe.LightThreshold, recipe.ShadowThreshold, recipe.InnerLineGap, noise);
        RectInt drawn = DrawnBounds(pixels, recipe.FrameSize);
        Vector3 light = canvas.Project(new Vector3(drawing.LightAt.x, drawing.LightAt.y, 0f)) / PixelsPerCell;
        return new PropVariant(pixels, GlowOf(canvas, drawing, pixels), NormalsOf(canvas, pixels), Footprint(canvas, drawing, recipe), drawn, TouchesEdge(pixels, recipe.FrameSize),
            drawing.HasLight, new Vector2(light.x, light.y), drawing.LightStrength);
    }

    /// <summary>
    /// Each drawn pixel's glow: its material's glow in its alpha, 0 to 255, and the colour it flickers to, one of its
    /// material's flicker colours picked by a hash of where it lies, or black; outlines around the drawing glow none.
    /// </summary>
    private static Color32[] GlowOf(ArtCanvas canvas, PropDrawing drawing, Color32[] pixels)
    {
        Color32[] glow = new Color32[pixels.Length];
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                int i = y * canvas.Width + x;
                int id = canvas.MaterialIdAt(x, y);
                if (id < 0 || pixels[i].a == 0 || drawing.Materials[id].Glow <= 0f)
                {
                    continue;
                }

                ArtMaterial material = drawing.Materials[id];
                Color32 flicker = new(0, 0, 0, 0);
                if (material.Flicker.Count > 0)
                {
                    flicker = material.Flicker[Mathf.Min((int)(ArtNoise.Hash(x, y, FlickerSalt) * material.Flicker.Count), material.Flicker.Count - 1)];
                }

                glow[i] = new Color32(flicker.r, flicker.g, flicker.b, (byte)Mathf.RoundToInt(material.Glow * 255f));
            }
        }

        return glow;
    }

    /// <summary>
    /// Each drawn pixel's surface normal (<see cref="ArtCanvas.NormalAt"/>, whose z points away from the camera) with x
    /// right, y up the screen and z toward the camera, each from -1 to 1 as 0 to 255, opaque; clear where nothing is
    /// drawn and on the outlines around the drawing, which fill no pixel of the canvas.
    /// </summary>
    public static Color32[] NormalsOf(ArtCanvas canvas, Color32[] pixels)
    {
        Color32[] normals = new Color32[pixels.Length];
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                int i = y * canvas.Width + x;
                if (pixels[i].a == 0 || !canvas.IsFilled(x, y))
                {
                    continue;
                }

                Vector3 normal = canvas.NormalAt(x, y);
                normals[i] = new Color32(NormalByte(normal.x), NormalByte(normal.y), NormalByte(-normal.z), 255);
            }
        }

        return normals;
    }

    /// <summary>
    /// A normal's component from -1 to 1 as 0 to 255.
    /// </summary>
    private static byte NormalByte(float component)
    {
        return (byte)Mathf.Clamp(Mathf.RoundToInt((component * 0.5f + 0.5f) * 255f), 0, 255);
    }

    /// <summary>
    /// Draws variant <paramref name="index"/> of <paramref name="recipe"/> unshaded, at <paramref name="phase"/> of a
    /// moving flame's loop, so tests can read what each pixel is; <paramref name="drawing"/> holds its materials and
    /// stream.
    /// </summary>
    public static ArtCanvas Render(PropRecipe recipe, int index, out PropDrawing drawing, float phase = 0f)
    {
        ArtCanvas canvas = new(recipe.FrameSize.x, recipe.FrameSize.y, recipe.BasePixel, PixelsPerCell) { HidesBelowGround = true };
        drawing = new PropDrawing(canvas, new System.Random(VariantSeed(recipe.Seed, index)), phase);
        recipe.Draw(drawing);
        return canvas;
    }

    /// <summary>
    /// The seed of variant <paramref name="index"/>'s stream.
    /// </summary>
    public static int VariantSeed(int seed, int index)
    {
        return unchecked(seed * 486187739 + index * 16777619 + 1);
    }

    /// <summary>
    /// The footprint in frame pixels: around the base, the columns and rows the ground-touching pixels below the
    /// contact height reach, the rows mirrored above the base; empty when nothing touches the ground.
    /// </summary>
    private static RectInt Footprint(ArtCanvas canvas, PropDrawing drawing, PropRecipe recipe)
    {
        int left = int.MaxValue, right = int.MinValue, bottom = int.MaxValue;
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                if (!canvas.IsFilled(x, y) || !drawing.TouchesGround(canvas.MaterialIdAt(x, y)) || canvas.WorldPositionAt(x, y).z > recipe.ContactHeight)
                {
                    continue;
                }

                left = Mathf.Min(left, x);
                right = Mathf.Max(right, x + 1);
                bottom = Mathf.Min(bottom, y);
            }
        }

        if (left == int.MaxValue)
        {
            return new RectInt(0, 0, 0, 0);
        }

        Vector2Int pivot = recipe.BasePixel;
        int across = Mathf.Max(pivot.x - left, right - pivot.x);
        int below = Mathf.Max(1, pivot.y - bottom);
        return new RectInt(pivot.x - across, pivot.y - below, 2 * across, 2 * below);
    }

    /// <summary>
    /// The box around every drawn pixel, empty when nothing is drawn.
    /// </summary>
    private static RectInt DrawnBounds(Color32[] pixels, Vector2Int size)
    {
        int left = int.MaxValue, right = int.MinValue, bottom = int.MaxValue, top = int.MinValue;
        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                if (pixels[y * size.x + x].a == 0)
                {
                    continue;
                }

                left = Mathf.Min(left, x);
                right = Mathf.Max(right, x + 1);
                bottom = Mathf.Min(bottom, y);
                top = Mathf.Max(top, y + 1);
            }
        }

        return left == int.MaxValue ? new RectInt(0, 0, 0, 0) : new RectInt(left, bottom, right - left, top - bottom);
    }

    /// <summary>
    /// Whether any pixel on the frame's border is drawn.
    /// </summary>
    private static bool TouchesEdge(Color32[] pixels, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
        {
            if (pixels[x].a > 0 || pixels[(size.y - 1) * size.x + x].a > 0)
            {
                return true;
            }
        }

        for (int y = 0; y < size.y; y++)
        {
            if (pixels[y * size.x].a > 0 || pixels[y * size.x + size.x - 1].a > 0)
            {
                return true;
            }
        }

        return false;
    }
}
