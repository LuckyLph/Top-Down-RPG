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
/// whenever the control map changes, which only works the distances out again when the coast moved; owns its texture
/// and buffers, <see cref="Dispose"/> destroys the texture.
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
    private const float Far = 1e6f;
    private const float Diagonal = 1.41421356f;

    private readonly string textureName;
    private Texture2D texture;
    private bool[] sea = new bool[0];
    private float[] toLand = new float[0];
    private float[] toSea = new float[0];
    private float[] blurred = new float[0];
    private ushort[] encoded = new ushort[0];
    private bool built;
    private int builtWidth;

    /// <summary>
    /// Creates an empty map whose texture will be named <paramref name="textureName"/>.
    /// </summary>
    public TerrainCoastMap(string textureName)
    {
        this.textureName = textureName;
    }

    public Texture2D Texture => texture;

    /// <summary>
    /// Builds the map over <paramref name="controlMap"/>'s bounds for the layer at <paramref name="waveLayer"/> (-1 for
    /// none), <paramref name="bridges"/> holding a bit for each bridge layer, and uploads it; keeps the texture as it is
    /// when no cell changed sides since the last build.
    /// </summary>
    public void Build(TerrainControlMap controlMap, int waveLayer, uint bridges)
    {
        BoundsInt bounds = controlMap.Bounds;
        bool used = waveLayer >= 0 && !controlMap.IsEmpty;
        int width = used ? bounds.size.x * TexelsPerCell : 1;
        int height = used ? bounds.size.y * TexelsPerCell : 1;
        Prepare(width, height);
        if (!used)
        {
            built = false;
            encoded[0] = 0;
            Upload();
            return;
        }

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

        built = true;
        Chamfer(width, height, true, toLand);
        Chamfer(width, height, false, toSea);
        for (int i = 0; i < width * height; i++)
        {
            float texels = sea[i] ? toLand[i] : -toSea[i];
            texels = texels > DistanceRange ? DistanceRange : texels < -DistanceRange ? -DistanceRange : texels;
            blurred[i] = (texels - (sea[i] ? 0.5f : -0.5f)) * PixelsPerTexel;
            toSea[i] = blurred[i];
        }

        for (int pass = 0; pass < BlurPasses; pass++)
        {
            BoxBlur(blurred, width, height, true, NearBlurRadius);
            BoxBlur(blurred, width, height, false, NearBlurRadius);
            BoxBlur(toSea, width, height, true, FarBlurRadius);
            BoxBlur(toSea, width, height, false, FarBlurRadius);
        }

        for (int i = 0; i < width * height; i++)
        {
            float near = blurred[i];
            float far = (near - FarFrom) / FarBlend;
            far = far < 0f ? 0f : far > 1f ? 1f : far;
            float level = (near + (toSea[i] - near) * far - MinDistance) / DistanceRange;
            level = level < 0f ? 0f : level > 1f ? 1f : level;
            encoded[i] = (ushort)(level * 65535f + 0.5f);
        }

        Upload();
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
        int count = width * height;
        if (sea.Length != count || width != builtWidth)
        {
            built = false;
            builtWidth = width;
            sea = new bool[count];
            toLand = new float[count];
            toSea = new float[count];
            blurred = new float[count];
            encoded = new ushort[count];
        }

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
    /// Averages <paramref name="values"/> over <paramref name="radius"/> texels each way along rows
    /// (<paramref name="alongRows"/>) or columns, the map's edge texels repeating past it, using the land distances as
    /// scratch.
    /// </summary>
    private void BoxBlur(float[] values, int width, int height, bool alongRows, int radius)
    {
        int lines = alongRows ? height : width;
        int length = alongRows ? width : height;
        int stride = alongRows ? 1 : width;
        float scale = 1f / (2 * radius + 1);
        int last = (length - 1) * stride;
        for (int line = 0; line < lines; line++)
        {
            int start = alongRows ? line * width : line;
            float sum = 0f;
            for (int k = -radius; k <= radius; k++)
            {
                sum += values[start + (k < 0 ? 0 : k > length - 1 ? length - 1 : k) * stride];
            }

            float first = values[start];
            float end = values[start + last];
            for (int p = 0; p < length; p++)
            {
                int at = start + p * stride;
                toLand[at] = sum * scale;
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
