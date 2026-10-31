using System;
using UnityEngine;

/// <summary>
/// How strongly each spot near glowing ground (lava) is lit by it (Docs/Lighting.md, Phase 5), as the cookie of the
/// Sprite Light 2D a terrain renderer draws its ground's glow with: <see cref="TexelsPerCell"/> texels per cell side
/// over <see cref="Area"/>, the cells within the reach of a glowing cell inside the area it is built for, each texel
/// (1 - distance / reach) squared, 1 on the glowing cells and 0 from the reach on, in its colour channels (alpha 1),
/// bilinear and clamped. The distance is the exact distance from each texel's centre to the nearest texel centre of a
/// glowing cell, worked out over the area grown by the reach, so glowing cells just outside the area (in a chunk's
/// skirt) light into it too. Built at a renderer's build and whenever its control map changes; owns its texture and
/// sprite, <see cref="Dispose"/> destroys them.
/// </summary>
public sealed class TerrainGlowMap : IDisposable
{
    public const int TexelsPerCell = 4;
    private const float Far = 1e20f;

    private readonly string textureName;
    private Texture2D texture;
    private Sprite sprite;
    private float[] distance = new float[0];
    private float[] row = new float[0];
    private float[] envelope = new float[0];
    private int[] parabolas = new int[0];
    private Color32[] texels = new Color32[0];

    /// <summary>
    /// Creates an empty map whose texture will be named <paramref name="name"/>.
    /// </summary>
    public TerrainGlowMap(string name)
    {
        textureName = name;
    }

    public Texture2D Texture => texture;
    public Sprite Sprite => sprite;

    /// <summary>
    /// The cells the texture covers, empty when nothing glows.
    /// </summary>
    public RectInt Area { get; private set; }

    /// <summary>
    /// Whether nothing glows within the area it was built for.
    /// </summary>
    public bool IsEmpty => Area.width <= 0 || Area.height <= 0;

    /// <summary>
    /// Builds the map over <paramref name="area"/> (cells) for the cells of <paramref name="controlMap"/> where a layer
    /// of <paramref name="glowLayers"/> (a bit per layer) is filled, lighting <paramref name="reach"/> cells around
    /// them, and uploads it. Returns whether anything glows; when nothing does the texture and sprite are kept for a
    /// later build but <see cref="Area"/> is empty.
    /// </summary>
    public bool Build(TerrainControlMap controlMap, uint glowLayers, RectInt area, float reach)
    {
        int grow = Mathf.CeilToInt(reach);
        RectInt glowing = GlowingBounds(controlMap, glowLayers, new RectInt(area.xMin - grow, area.yMin - grow, area.width + 2 * grow, area.height + 2 * grow));
        if (glowLayers == 0u || reach <= 0f || glowing.width <= 0)
        {
            Area = new RectInt(0, 0, 0, 0);
            return false;
        }

        int xMin = Mathf.Max(area.xMin, glowing.xMin - grow);
        int yMin = Mathf.Max(area.yMin, glowing.yMin - grow);
        int xMax = Mathf.Min(area.xMax, glowing.xMax + grow);
        int yMax = Mathf.Min(area.yMax, glowing.yMax + grow);
        if (xMax <= xMin || yMax <= yMin)
        {
            Area = new RectInt(0, 0, 0, 0);
            return false;
        }

        Area = new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        RectInt field = new(xMin - grow, yMin - grow, Area.width + 2 * grow, Area.height + 2 * grow);
        int fieldWidth = field.width * TexelsPerCell;
        int fieldHeight = field.height * TexelsPerCell;
        Prepare(fieldWidth, fieldHeight);
        for (int cy = 0; cy < field.height; cy++)
        {
            for (int cx = 0; cx < field.width; cx++)
            {
                bool glows = (controlMap.GetCell(new Vector3Int(field.xMin + cx, field.yMin + cy, 0)) & glowLayers) != 0u;
                float value = glows ? 0f : Far;
                for (int ty = 0; ty < TexelsPerCell; ty++)
                {
                    int start = (cy * TexelsPerCell + ty) * fieldWidth + cx * TexelsPerCell;
                    for (int tx = 0; tx < TexelsPerCell; tx++)
                    {
                        distance[start + tx] = value;
                    }
                }
            }
        }

        for (int y = 0; y < fieldHeight; y++)
        {
            Transform(y * fieldWidth, 1, fieldWidth);
        }

        for (int x = 0; x < fieldWidth; x++)
        {
            Transform(x, fieldWidth, fieldHeight);
        }

        int width = Area.width * TexelsPerCell;
        int height = Area.height * TexelsPerCell;
        Allocate(width, height);
        float reachTexels = reach * TexelsPerCell;
        int offset = grow * TexelsPerCell;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float d = Mathf.Sqrt(distance[(y + offset) * fieldWidth + x + offset]);
                float lit = d >= reachTexels ? 0f : (1f - d / reachTexels) * (1f - d / reachTexels);
                byte b = (byte)Mathf.RoundToInt(lit * 255f);
                texels[y * width + x] = new Color32(b, b, b, 255);
            }
        }

        texture.SetPixels32(texels);
        texture.Apply(false);
        return true;
    }

    /// <summary>
    /// The texel at (<paramref name="x"/>, <paramref name="y"/>) of the last build, from its bottom left, clamped.
    /// </summary>
    public byte EncodedAt(int x, int y)
    {
        int width = Area.width * TexelsPerCell;
        int height = Area.height * TexelsPerCell;
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        return texels[Mathf.Clamp(y, 0, height - 1) * width + Mathf.Clamp(x, 0, width - 1)].r;
    }

    /// <summary>
    /// Destroys the texture and sprite.
    /// </summary>
    public void Dispose()
    {
        Destroy(sprite);
        Destroy(texture);
        sprite = null;
        texture = null;
    }

    /// <summary>
    /// The cells inside <paramref name="within"/> where a glowing layer is filled, their bounds; empty when none is.
    /// </summary>
    private static RectInt GlowingBounds(TerrainControlMap controlMap, uint glowLayers, RectInt within)
    {
        BoundsInt bounds = controlMap.Bounds;
        int x0 = Mathf.Max(within.xMin, bounds.xMin), x1 = Mathf.Min(within.xMax, bounds.xMax);
        int y0 = Mathf.Max(within.yMin, bounds.yMin), y1 = Mathf.Min(within.yMax, bounds.yMax);
        int xMin = int.MaxValue, yMin = int.MaxValue, xMax = int.MinValue, yMax = int.MinValue;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                if ((controlMap.GetCell(new Vector3Int(x, y, 0)) & glowLayers) == 0u)
                {
                    continue;
                }

                xMin = Mathf.Min(xMin, x);
                yMin = Mathf.Min(yMin, y);
                xMax = Mathf.Max(xMax, x + 1);
                yMax = Mathf.Max(yMax, y + 1);
            }
        }

        return xMin == int.MaxValue ? new RectInt(0, 0, 0, 0) : new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    /// <summary>
    /// Replaces <paramref name="count"/> squared distances of <see cref="distance"/> from <paramref name="start"/>,
    /// <paramref name="stride"/> apart, with their one-dimensional distance transform (Felzenszwalb and Huttenlocher's
    /// lower envelope of parabolas), so a pass along the rows and one along the columns give exact squared distances.
    /// </summary>
    private void Transform(int start, int stride, int count)
    {
        for (int i = 0; i < count; i++)
        {
            row[i] = distance[start + i * stride];
        }

        int k = 0;
        parabolas[0] = 0;
        envelope[0] = float.NegativeInfinity;
        envelope[1] = float.PositiveInfinity;
        for (int q = 1; q < count; q++)
        {
            float s = Intersection(q, parabolas[k]);
            while (s <= envelope[k])
            {
                k--;
                s = Intersection(q, parabolas[k]);
            }

            k++;
            parabolas[k] = q;
            envelope[k] = s;
            envelope[k + 1] = float.PositiveInfinity;
        }

        k = 0;
        for (int q = 0; q < count; q++)
        {
            while (envelope[k + 1] < q)
            {
                k++;
            }

            int p = parabolas[k];
            distance[start + q * stride] = (q - p) * (q - p) + row[p];
        }
    }

    /// <summary>
    /// Where the parabolas rooted at <paramref name="q"/> and <paramref name="p"/> meet.
    /// </summary>
    private float Intersection(int q, int p)
    {
        return (row[q] - row[p] + (q * q - p * p)) / (2f * (q - p));
    }

    /// <summary>
    /// Sizes the work buffers for a field of <paramref name="width"/> x <paramref name="height"/> texels.
    /// </summary>
    private void Prepare(int width, int height)
    {
        int cells = width * height;
        if (distance.Length < cells)
        {
            distance = new float[cells];
        }

        int line = Mathf.Max(width, height);
        if (row.Length < line)
        {
            row = new float[line];
            parabolas = new int[line];
            envelope = new float[line + 1];
        }
    }

    /// <summary>
    /// Makes the texture, its texels and its sprite <paramref name="width"/> x <paramref name="height"/> texels when they
    /// are not.
    /// </summary>
    private void Allocate(int width, int height)
    {
        if (texture != null && texture.width == width && texture.height == height)
        {
            return;
        }

        Destroy(sprite);
        Destroy(texture);
        texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = textureName,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        texels = new Color32[width * height];
        sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), Vector2.zero, TexelsPerCell, 0, SpriteMeshType.FullRect);
        sprite.name = textureName;
        sprite.hideFlags = HideFlags.DontSave;
    }

    /// <summary>
    /// Destroys <paramref name="target"/>, at once outside play mode.
    /// </summary>
    private static void Destroy(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(target);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
