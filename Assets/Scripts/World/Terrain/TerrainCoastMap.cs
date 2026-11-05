using System;
using UnityEngine;

/// <summary>
/// How far each spot of a renderer's area lies from the coast of its layer with waves, as a texture for the terrain
/// blending shader: a signed distance in pixels, positive in that layer and negative outside it, over the control map's
/// cells, <see cref="TexelsPerCell"/> texels per cell side, smoothed by a box blur so the waves that follow it bend
/// gently around the coast's corners: a short blur near the coast, blending from <see cref="FarFrom"/> pixels out over
/// <see cref="FarBlend"/> into a long one, so far out at sea the waves forget the coast's steps. A cell counts as the layer's when that layer or one above it that is no bridge is
/// filled, or when it is empty, so the map's edge is no coast. Stored in a bilinear R16 texture as
/// (distance - <see cref="MinDistance"/>) / <see cref="DistanceRange"/>; 1 x 1 without a layer with waves. Built again
/// whenever the control map changes, which only works the distances out again when the coast moved, and then blurs and
/// encodes again only the texels whose distance changed, widened by the blur's reach (<see cref="Reach"/>), the others
/// keeping what a whole build gives them (Docs/KnownGaps.md, Phase 6); owns its texture and buffers,
/// <see cref="Dispose"/> destroys the texture.
/// </summary>
public sealed class TerrainCoastMap : IDisposable
{
    public const int TexelsPerCell = 8;
    public const float PixelsPerTexel = (float)TerrainStyle.PixelsPerCell / TexelsPerCell;
    public const float MinDistance = -64f;
    public const float DistanceRange = 1024f;
    public const int NearBlurRadius = 3;
    public const int FarBlurRadius = 8;
    public const int BlurPasses = 3;
    public const float FarFrom = 8f;
    public const float FarBlend = 50f;
    public const int Reach = BlurPasses * FarBlurRadius;
    private const float Far = 1e6f;
    private const float Diagonal = 1.41421356f;

    private readonly string textureName;
    private Texture2D texture;
    private bool[] sea = new bool[0];
    private float[] toLand = new float[0];
    private float[] toSea = new float[0];
    private float[] signed = new float[0];
    private float[] near = new float[0];
    private float[] far = new float[0];
    private ushort[] encoded = new ushort[0];
    private bool built;
    private int builtWidth;
    private RectInt rebuilt;

    /// <summary>
    /// Creates an empty map whose texture will be named <paramref name="textureName"/>.
    /// </summary>
    public TerrainCoastMap(string textureName)
    {
        this.textureName = textureName;
    }

    public Texture2D Texture => texture;

    /// <summary>
    /// The texels the last build blurred and encoded again: the whole map for a first build or one of a new size, none
    /// when nothing changed, else the bounds of the texels whose distance changed widened by <see cref="Reach"/>.
    /// </summary>
    internal RectInt LastRebuilt => rebuilt;

    /// <summary>
    /// Builds the map over <paramref name="controlMap"/>'s bounds for the layer at <paramref name="waveLayer"/> (-1 for
    /// none), <paramref name="bridges"/> holding a bit for each bridge layer, and uploads it; keeps the texture as it is
    /// when no cell changed sides since the last build, and after a build of the same size blurs and encodes again only
    /// around the texels whose distance changed (<see cref="LastRebuilt"/>), which gives the texels a whole build gives.
    /// </summary>
    public void Build(TerrainControlMap controlMap, int waveLayer, uint bridges)
    {
        BoundsInt bounds = controlMap.Bounds;
        bool used = waveLayer >= 0 && !controlMap.IsEmpty;
        int width = used ? bounds.size.x * TexelsPerCell : 1;
        int height = used ? bounds.size.y * TexelsPerCell : 1;
        Prepare(width, height);
        rebuilt = new RectInt(0, 0, 0, 0);
        if (!used)
        {
            built = false;
            encoded[0] = 0;
            Upload();
            return;
        }

        bool wasBuilt = built;
        uint counted = ~(bridges & ~(1u << waveLayer));
        bool changed = !built;
        for (int cy = 0; cy < bounds.size.y; cy++)
        {
            for (int cx = 0; cx < bounds.size.x; cx++)
            {
                uint bits = controlMap.GetCell(new Vector3Int(bounds.xMin + cx, bounds.yMin + cy, 0));
                bool isSea = bits == 0 || ((bits & counted) >> waveLayer) != 0;
                for (int ty = 0; ty < TexelsPerCell; ty++)
                {
                    int row = (cy * TexelsPerCell + ty) * width + cx * TexelsPerCell;
                    for (int tx = 0; tx < TexelsPerCell; tx++)
                    {
                        changed |= sea[row + tx] != isSea;
                        sea[row + tx] = isSea;
                    }
                }
            }
        }

        if (!changed)
        {
            return;
        }

        if (wasBuilt)
        {
            Update(width, height);
        }
        else
        {
            Compute(width, height);
        }

        Upload();
    }

    /// <summary>
    /// Works the map's distances out over a window of <paramref name="cellsWide"/> x <paramref name="cellsHigh"/> cells
    /// from <paramref name="seaCells"/>, a flag per cell, row by row from the bottom, telling whether the cell is the
    /// sea's (as <see cref="Build"/> decides it from a control map), without a texture: <see cref="EncodedAt"/> reads
    /// them. It touches no Unity object, so it runs on any thread: the editor's coast bake works out many chunks' maps at
    /// once this way, from the cells of the whole world around each.
    /// </summary>
    public void ComputeFromCells(bool[] seaCells, int cellsWide, int cellsHigh)
    {
        if (seaCells == null || seaCells.Length != cellsWide * cellsHigh || cellsWide <= 0 || cellsHigh <= 0)
        {
            throw new ArgumentException("The sea flags must hold one flag per cell of the window.", nameof(seaCells));
        }

        int width = cellsWide * TexelsPerCell;
        int height = cellsHigh * TexelsPerCell;
        PrepareBuffers(width, height);
        for (int cy = 0; cy < cellsHigh; cy++)
        {
            for (int cx = 0; cx < cellsWide; cx++)
            {
                bool isSea = seaCells[cy * cellsWide + cx];
                for (int ty = 0; ty < TexelsPerCell; ty++)
                {
                    int row = (cy * TexelsPerCell + ty) * width + cx * TexelsPerCell;
                    for (int tx = 0; tx < TexelsPerCell; tx++)
                    {
                        sea[row + tx] = isSea;
                    }
                }
            }
        }

        Compute(width, height);
    }

    /// <summary>
    /// The encoded distance of texel (<paramref name="x"/>, <paramref name="y"/>) of the last build, the texel at the
    /// map's edge for one past it, as a clamped texture sampler reads it.
    /// </summary>
    public ushort EncodedAt(int x, int y)
    {
        int width = Mathf.Max(1, builtWidth);
        int height = encoded.Length / width;
        x = x < 0 ? 0 : x >= width ? width - 1 : x;
        y = y < 0 ? 0 : y >= height ? height - 1 : y;
        return encoded[y * width + x];
    }

    /// <summary>
    /// Works the distances out from the sea flags of <paramref name="width"/> x <paramref name="height"/> texels and
    /// encodes them.
    /// </summary>
    private void Compute(int width, int height)
    {
        built = true;
        Chamfer(width, height, true, toLand);
        Chamfer(width, height, false, toSea);
        for (int i = 0; i < width * height; i++)
        {
            signed[i] = SignedAt(i);
        }

        rebuilt = new RectInt(0, 0, width, height);
        Blur(width, rebuilt, rebuilt);
    }

    /// <summary>
    /// Works the distances out again from the sea flags of <paramref name="width"/> x <paramref name="height"/> texels,
    /// finds the texels whose distance changed since the last build, and blurs and encodes again only those widened by
    /// <see cref="Reach"/>, from a window widened by twice that: the blur's errors at the window's edges inside the map
    /// spread no further than <see cref="Reach"/> texels, so every texel it writes gets what a whole build gives it, and
    /// every other texel already has it.
    /// </summary>
    private void Update(int width, int height)
    {
        Chamfer(width, height, true, toLand);
        Chamfer(width, height, false, toSea);
        int xMin = width;
        int yMin = height;
        int xMax = -1;
        int yMax = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                float value = SignedAt(i);
                if (value != signed[i])
                {
                    signed[i] = value;
                    xMin = x < xMin ? x : xMin;
                    yMin = y < yMin ? y : yMin;
                    xMax = x > xMax ? x : xMax;
                    yMax = y > yMax ? y : yMax;
                }
            }
        }

        if (xMax < 0)
        {
            return;
        }

        RectInt changed = new(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
        rebuilt = Widen(changed, Reach, width, height);
        Blur(width, Widen(changed, 2 * Reach, width, height), rebuilt);
    }

    /// <summary>
    /// The signed distance of texel <paramref name="i"/> in pixels, from the last chamfers: to land for a sea texel,
    /// negative to the sea for a land texel, clamped to <see cref="DistanceRange"/> texels and taken to the texel's edge.
    /// </summary>
    private float SignedAt(int i)
    {
        float texels = sea[i] ? toLand[i] : -toSea[i];
        texels = texels > DistanceRange ? DistanceRange : texels < -DistanceRange ? -DistanceRange : texels;
        return (texels - (sea[i] ? 0.5f : -0.5f)) * PixelsPerTexel;
    }

    /// <summary>
    /// Blurs the signed distances of <paramref name="window"/> near and far (<see cref="BoxBlur"/>, which takes the
    /// window's edges as the map's), then mixes and encodes them into the texels of <paramref name="kept"/>, which lies at
    /// least <see cref="Reach"/> texels inside the window's edges that are not the map's.
    /// </summary>
    private void Blur(int width, RectInt window, RectInt kept)
    {
        for (int y = window.yMin; y < window.yMax; y++)
        {
            int row = y * width;
            for (int x = window.xMin; x < window.xMax; x++)
            {
                near[row + x] = signed[row + x];
                far[row + x] = signed[row + x];
            }
        }

        for (int pass = 0; pass < BlurPasses; pass++)
        {
            BoxBlur(near, width, window, true, NearBlurRadius);
            BoxBlur(near, width, window, false, NearBlurRadius);
            BoxBlur(far, width, window, true, FarBlurRadius);
            BoxBlur(far, width, window, false, FarBlurRadius);
        }

        for (int y = kept.yMin; y < kept.yMax; y++)
        {
            int row = y * width;
            for (int x = kept.xMin; x < kept.xMax; x++)
            {
                int i = row + x;
                float nearValue = near[i];
                float blend = (nearValue - FarFrom) / FarBlend;
                blend = blend < 0f ? 0f : blend > 1f ? 1f : blend;
                float level = (nearValue + (far[i] - nearValue) * blend - MinDistance) / DistanceRange;
                level = level < 0f ? 0f : level > 1f ? 1f : level;
                encoded[i] = (ushort)(level * 65535f + 0.5f);
            }
        }
    }

    /// <summary>
    /// <paramref name="rect"/> widened by <paramref name="by"/> texels each way, within the map's
    /// <paramref name="width"/> x <paramref name="height"/> texels.
    /// </summary>
    private static RectInt Widen(RectInt rect, int by, int width, int height)
    {
        int xMin = Math.Max(0, rect.xMin - by);
        int yMin = Math.Max(0, rect.yMin - by);
        int xMax = Math.Min(width, rect.xMax + by);
        int yMax = Math.Min(height, rect.yMax + by);
        return new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    /// <summary>
    /// Destroys the texture.
    /// </summary>
    public void Dispose()
    {
        if (texture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(texture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }

        texture = null;
    }

    /// <summary>
    /// Sizes the buffers and the texture for <paramref name="width"/> x <paramref name="height"/> texels.
    /// </summary>
    private void Prepare(int width, int height)
    {
        PrepareBuffers(width, height);
        if (texture == null)
        {
            texture = new Texture2D(width, height, TextureFormat.R16, false, true)
            {
                name = textureName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
        }
        else if (texture.width != width || texture.height != height)
        {
            texture.Reinitialize(width, height);
        }
    }

    /// <summary>
    /// Sizes the buffers for <paramref name="width"/> x <paramref name="height"/> texels, forgetting the last build when
    /// the size changed.
    /// </summary>
    private void PrepareBuffers(int width, int height)
    {
        int count = width * height;
        if (sea.Length != count || width != builtWidth)
        {
            built = false;
            builtWidth = width;
            sea = new bool[count];
            toLand = new float[count];
            toSea = new float[count];
            signed = new float[count];
            near = new float[count];
            far = new float[count];
            encoded = new ushort[count];
        }
    }

    /// <summary>
    /// Writes the encoded distances into the texture.
    /// </summary>
    private void Upload()
    {
        texture.SetPixelData(encoded, 0);
        texture.Apply(false, false);
    }

    /// <summary>
    /// Fills <paramref name="distance"/> with each texel's chamfer distance, in texels, to the nearest texel whose sea
    /// flag differs from <paramref name="from"/>; 0 for those texels themselves. Texels past the map's edge count as
    /// far.
    /// </summary>
    private void Chamfer(int width, int height, bool from, float[] distance)
    {
        for (int i = 0; i < width * height; i++)
        {
            distance[i] = sea[i] == from ? Far : 0f;
        }

        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int i = row + x;
                float d = distance[i];
                if (d == 0f)
                {
                    continue;
                }

                float c;
                if (x > 0 && (c = distance[i - 1] + 1f) < d)
                {
                    d = c;
                }

                if (y > 0)
                {
                    int up = i - width;
                    if ((c = distance[up] + 1f) < d)
                    {
                        d = c;
                    }

                    if (x > 0 && (c = distance[up - 1] + Diagonal) < d)
                    {
                        d = c;
                    }

                    if (x < width - 1 && (c = distance[up + 1] + Diagonal) < d)
                    {
                        d = c;
                    }
                }

                distance[i] = d;
            }
        }

        for (int y = height - 1; y >= 0; y--)
        {
            int row = y * width;
            for (int x = width - 1; x >= 0; x--)
            {
                int i = row + x;
                float d = distance[i];
                if (d == 0f)
                {
                    continue;
                }

                float c;
                if (x < width - 1 && (c = distance[i + 1] + 1f) < d)
                {
                    d = c;
                }

                if (y < height - 1)
                {
                    int down = i + width;
                    if ((c = distance[down] + 1f) < d)
                    {
                        d = c;
                    }

                    if (x < width - 1 && (c = distance[down + 1] + Diagonal) < d)
                    {
                        d = c;
                    }

                    if (x > 0 && (c = distance[down - 1] + Diagonal) < d)
                    {
                        d = c;
                    }
                }

                distance[i] = d;
            }
        }
    }

    /// <summary>
    /// Averages <paramref name="values"/> over <paramref name="radius"/> texels each way along the rows
    /// (<paramref name="alongRows"/>) or columns of <paramref name="window"/>, in a map <paramref name="width"/> texels
    /// wide, the window's edge texels repeating past it, using the land distances as scratch. The running sum is a
    /// double, so a texel's average does not depend on where its line starts: a coast map baked over a window of the
    /// world gives the same texels as one over the whole world, and a window inside the map the same texels as the whole
    /// map but within the radius of its edges inside the map.
    /// </summary>
    private void BoxBlur(float[] values, int width, RectInt window, bool alongRows, int radius)
    {
        int lines = alongRows ? window.height : window.width;
        int length = alongRows ? window.width : window.height;
        int stride = alongRows ? 1 : width;
        double scale = 1.0 / (2 * radius + 1);
        int last = (length - 1) * stride;
        for (int line = 0; line < lines; line++)
        {
            int start = alongRows ? (window.yMin + line) * width + window.xMin : window.yMin * width + window.xMin + line;
            double sum = 0.0;
            for (int k = -radius; k <= radius; k++)
            {
                sum += values[start + (k < 0 ? 0 : k > length - 1 ? length - 1 : k) * stride];
            }

            float first = values[start];
            float end = values[start + last];
            for (int p = 0; p < length; p++)
            {
                int at = start + p * stride;
                toLand[at] = (float)(sum * scale);
                int add = p + radius + 1;
                int remove = p - radius;
                sum += add < length ? values[start + add * stride] : end;
                sum -= remove >= 0 ? values[start + remove * stride] : first;
            }

            for (int p = 0, at = start; p < length; p++, at += stride)
            {
                values[at] = toLand[at];
            }
        }
    }
}
