using UnityEngine;

/// <summary>
/// Draws the effect recipes' frames straight onto the pixel grid, rows from the bottom, with no smoothing: the spark
/// star of an <see cref="ImpactEffectRecipe"/> and the death puff of a <see cref="PuffEffectRecipe"/>. Pure: the same
/// recipe always gives the same pixels, and nothing touches an asset.
/// </summary>
public static class EffectPainter
{
    private static readonly int[,] Bayer =
    {
        { 0, 8, 2, 10 },
        { 12, 4, 14, 6 },
        { 3, 11, 1, 9 },
        { 15, 7, 13, 5 }
    };

    /// <summary>
    /// Draws one frame of <paramref name="recipe"/>'s star in <paramref name="palette"/>, its first ray along
    /// <paramref name="direction"/>: the hot core, then each ray from its start to its tip in the frame's shade with its
    /// hot middle over it, then a one-pixel outline round everything in the darkest colour darkened by the recipe's
    /// outline shade.
    /// </summary>
    public static Color32[] Star(ImpactEffectRecipe recipe, ImpactPalette palette, Vector2 direction, ImpactStarFrame frame)
    {
        Canvas canvas = new(recipe.FrameWidth, recipe.FrameHeight, new Vector2(recipe.FrameWidth / 2, recipe.FrameHeight / 2));
        float scale = recipe.Scale;
        Color32 hot = palette.Get(ImpactShade.Hot);
        if (frame.CoreRadius > 0f)
        {
            canvas.Disk(Vector2.zero, frame.CoreRadius * scale, hot);
        }

        Vector2 along = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector2.right;
        Color32 shade = palette.Get(frame.RayShade);
        for (int ray = 0; ray < recipe.Rays; ray++)
        {
            float angle = 2f * Mathf.PI * ray / recipe.Rays;
            Vector2 way = new(along.x * Mathf.Cos(angle) - along.y * Mathf.Sin(angle), along.x * Mathf.Sin(angle) + along.y * Mathf.Cos(angle));
            float tip = (ray % 2 == 0 ? frame.LongRayTip : frame.ShortRayTip) * scale;
            float from = Mathf.Max(frame.RayFrom * scale, tip - frame.RayLength * scale);
            canvas.Line(way * from, way * tip, shade);
            if (frame.HotBack > 0f)
            {
                canvas.Line(way * from, way * (tip - frame.HotBack * scale), hot);
            }
        }

        Color32 dark = palette.Get(ImpactShade.Dark);
        canvas.Outline(new Color32((byte)(dark.r * recipe.OutlineShade), (byte)(dark.g * recipe.OutlineShade), (byte)(dark.b * recipe.OutlineShade), 255));
        return canvas.Pixels;
    }

    /// <summary>
    /// Draws frame <paramref name="frame"/> of <paramref name="recipe"/>'s puff: the white pop on the first frame when
    /// the recipe has one; otherwise every blob that has appeared, swollen, risen, spread and shrunk to the frame's
    /// time and shaded by how its edge faces the light (later blobs over earlier ones), eaten by the ordered dither once
    /// the dissolve starts, and outlined in the darkest colour until the dissolve has gone far enough.
    /// </summary>
    public static Color32[] Puff(PuffEffectRecipe recipe, int frame)
    {
        int width = recipe.FrameWidth;
        int height = recipe.FrameHeight;
        Color32[] pixels = new Color32[width * height];
        Vector2Int feet = recipe.Feet;
        if (recipe.Pop && frame == 0)
        {
            Color32 white = new(255, 255, 255, 255);
            for (int row = 0; row < height; row++)
            {
                for (int x = 0; x < width; x++)
                {
                    float px = (x - feet.x) / recipe.PopRadii.x;
                    float py = (row - feet.y - recipe.PopHeight) / recipe.PopRadii.y;
                    if (px * px + py * py <= 1f)
                    {
                        pixels[row * width + x] = white;
                    }
                }
            }

            return pixels;
        }

        float time = frame * recipe.FrameSeconds;
        float end = recipe.FrameCount * recipe.FrameSeconds;
        float dissolve = end > recipe.DissolveFrom ? Mathf.Max(0f, (time - recipe.DissolveFrom) / (end - recipe.DissolveFrom)) : 0f;
        int[] shades = new int[width * height];
        Vector2 light = recipe.LightDirection.normalized;
        foreach (PuffBlob blob in recipe.Blobs)
        {
            float local = time - blob.Delay;
            if (local < 0f)
            {
                continue;
            }

            float swell = 1f - Mathf.Pow(1f - Mathf.Clamp01(local / recipe.SwellSeconds), 3f);
            float radius = blob.Radius * (recipe.StartSize + (1f - recipe.StartSize) * swell) * (1f + recipe.Growth * local)
                * (1f - recipe.Shrink * Mathf.Min(1f, local / recipe.ShrinkSeconds) * (blob.Position.y / 30f + 0.4f));
            float cx = blob.Position.x * (1f + recipe.Spread * local);
            float cy = blob.Position.y * (1f + recipe.Rise * local) + recipe.Lift * local;
            if (radius <= 0f)
            {
                continue;
            }

            for (int row = 0; row < height; row++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = x - feet.x - cx;
                    float dy = row - feet.y - cy;
                    if (dx * dx + dy * dy > radius * radius)
                    {
                        continue;
                    }

                    float facing = (dx * light.x + dy * light.y) / radius;
                    shades[row * width + x] = facing > recipe.LightFacing ? 3 : facing < recipe.ShadowFacing ? 1 : 2;
                }
            }
        }

        bool[] filled = new bool[width * height];
        ColourRamp colours = recipe.Colours;
        for (int row = 0; row < height; row++)
        {
            int top = height - 1 - row;
            for (int x = 0; x < width; x++)
            {
                int i = row * width + x;
                if (shades[i] == 0 || (dissolve > 0f && Bayer[top % 4, x % 4] / 16f < dissolve * 1.05f))
                {
                    continue;
                }

                filled[i] = true;
                pixels[i] = shades[i] == 3 ? colours.Light : shades[i] == 1 ? colours.Shadow : colours.Middle;
            }
        }

        if (dissolve < recipe.OutlineUntil)
        {
            for (int row = 0; row < height; row++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = row * width + x;
                    bool beside = (x > 0 && filled[i - 1]) || (x < width - 1 && filled[i + 1]) || (row > 0 && filled[i - width]) || (row < height - 1 && filled[i + width]);
                    if (!filled[i] && beside)
                    {
                        pixels[i] = colours.Outline;
                    }
                }
            }
        }

        return pixels;
    }

    /// <summary>
    /// A frame being drawn, rows from the bottom, with a middle point the drawing's coordinates (x right, y up, in
    /// pixels) are measured from: a point lands on the pixel its coordinates round to.
    /// </summary>
    private sealed class Canvas
    {
        private readonly int width;
        private readonly int height;
        private readonly Vector2 middle;

        /// <summary>
        /// Creates an empty frame of <paramref name="frameWidth"/> by <paramref name="frameHeight"/> pixels measured
        /// from <paramref name="origin"/>.
        /// </summary>
        public Canvas(int frameWidth, int frameHeight, Vector2 origin)
        {
            width = frameWidth;
            height = frameHeight;
            middle = origin;
            Pixels = new Color32[width * height];
        }

        public Color32[] Pixels { get; }

        /// <summary>
        /// Colours the pixel <paramref name="point"/> lands on, when it lies in the frame.
        /// </summary>
        public void Put(Vector2 point, Color32 colour)
        {
            int x = Mathf.FloorToInt(middle.x + point.x + 0.5f);
            int top = Mathf.FloorToInt(middle.y - point.y + 0.5f);
            int row = height - 1 - top;
            if (x >= 0 && x < width && row >= 0 && row < height)
            {
                Pixels[row * width + x] = colour;
            }
        }

        /// <summary>
        /// Colours every whole-pixel offset from <paramref name="centre"/> within <paramref name="radius"/>, a little
        /// generously so small disks stay round.
        /// </summary>
        public void Disk(Vector2 centre, float radius, Color32 colour)
        {
            int reach = (int)radius + 1;
            for (int y = -reach; y <= reach; y++)
            {
                for (int x = -reach; x <= reach; x++)
                {
                    if (x * x + y * y <= radius * radius + 0.3f)
                    {
                        Put(centre + new Vector2(x, y), colour);
                    }
                }
            }
        }

        /// <summary>
        /// Colours a line from <paramref name="from"/> to <paramref name="to"/>, sampled twice per pixel of its longer
        /// side; a line of no length colours one pixel.
        /// </summary>
        public void Line(Vector2 from, Vector2 to, Color32 colour)
        {
            int steps = (int)(Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y)) * 2f) + 1;
            for (int i = 0; i <= steps; i++)
            {
                Put(Vector2.Lerp(from, to, (float)i / steps), colour);
            }
        }

        /// <summary>
        /// Colours every empty pixel beside a drawn one, along a side, in <paramref name="colour"/>.
        /// </summary>
        public void Outline(Color32 colour)
        {
            bool[] drawn = new bool[Pixels.Length];
            for (int i = 0; i < Pixels.Length; i++)
            {
                drawn[i] = Pixels[i].a > 0;
            }

            for (int row = 0; row < height; row++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = row * width + x;
                    bool beside = (x > 0 && drawn[i - 1]) || (x < width - 1 && drawn[i + 1]) || (row > 0 && drawn[i - width]) || (row < height - 1 && drawn[i + width]);
                    if (!drawn[i] && beside)
                    {
                        Pixels[i] = colour;
                    }
                }
            }
        }
    }
}
