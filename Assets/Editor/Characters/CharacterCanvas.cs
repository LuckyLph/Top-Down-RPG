using UnityEngine;

/// <summary>
/// One frame of a character being drawn. World points (x east, y north, z up, in rig units) are projected through an
/// orthographic camera pitched <see cref="ViewPitch"/> degrees down, the game's 3/4 view, at <see cref="Scale"/> pixels
/// per rig unit, with the pivot pixel at the world origin. Spheres and triangles are drawn straight onto the pixel grid
/// with a depth test, each pixel keeping its depth, material, part and surface normal; <see cref="Shade"/> then turns
/// them into colours: flat ramps, a light colour only where the surface faces the light strongly, inner lines where a
/// nearer part overlaps a farther one, and an outline around everything. Pixels are stored rows from the bottom.
/// </summary>
public sealed class CharacterCanvas
{
    public const float ViewPitch = 30f;
    private const int Empty = -1;

    private static readonly float SinPitch = Mathf.Sin(ViewPitch * Mathf.Deg2Rad);
    private static readonly float CosPitch = Mathf.Cos(ViewPitch * Mathf.Deg2Rad);

    /// <summary>
    /// The direction toward the light in view space (x right, y up, z away from the camera): from the top left in the
    /// screen plane, (-0.6, 0.8) as the terrain's, and from in front.
    /// </summary>
    public static readonly Vector3 Light = new Vector3(-0.51f, 0.68f, -0.55f).normalized;

    public static readonly Vector3 ViewRight = new(1f, 0f, 0f);
    public static readonly Vector3 ViewUp = new(0f, SinPitch, CosPitch);
    public static readonly Vector3 ViewForward = new(0f, CosPitch, -SinPitch);

    private readonly float[] depth;
    private readonly int[] material;
    private readonly int[] part;
    private readonly Vector3[] normal;

    /// <summary>
    /// Creates an empty frame of <paramref name="width"/> x <paramref name="height"/> pixels whose world origin falls on
    /// pixel <paramref name="pivot"/> (from the bottom left), drawn at <paramref name="scale"/> pixels per rig unit.
    /// </summary>
    public CharacterCanvas(int width, int height, Vector2Int pivot, float scale)
    {
        Width = width;
        Height = height;
        Pivot = pivot;
        Scale = scale;
        depth = new float[width * height];
        material = new int[width * height];
        part = new int[width * height];
        normal = new Vector3[width * height];
        Clear();
    }

    public int Width { get; }
    public int Height { get; }
    public Vector2Int Pivot { get; }
    public float Scale { get; }

    /// <summary>
    /// Empties every pixel.
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < depth.Length; i++)
        {
            depth[i] = float.PositiveInfinity;
            material[i] = Empty;
            part[i] = Empty;
            normal[i] = Vector3.zero;
        }
    }

    /// <summary>
    /// Where <paramref name="world"/> lands: x and y in pixels from the pivot, z its depth (larger is farther).
    /// </summary>
    public Vector3 Project(Vector3 world)
    {
        return new Vector3(world.x * Scale, (world.y * SinPitch + world.z * CosPitch) * Scale, (world.y * CosPitch - world.z * SinPitch) * Scale);
    }

    /// <summary>
    /// The pixel <paramref name="world"/> falls in, whether or not it lies inside the frame.
    /// </summary>
    public Vector2Int PixelOf(Vector3 world)
    {
        Vector3 view = Project(world);
        return new Vector2Int(Mathf.FloorToInt(view.x + Pivot.x), Mathf.FloorToInt(view.y + Pivot.y));
    }

    /// <summary>
    /// Whether (<paramref name="x"/>, <paramref name="y"/>) lies inside the frame.
    /// </summary>
    public bool Contains(int x, int y)
    {
        return x >= 0 && x < Width && y >= 0 && y < Height;
    }

    /// <summary>
    /// Whether pixel (<paramref name="x"/>, <paramref name="y"/>) holds anything.
    /// </summary>
    public bool IsFilled(int x, int y)
    {
        return Contains(x, y) && material[y * Width + x] != Empty;
    }

    /// <summary>
    /// The material of a filled pixel.
    /// </summary>
    public CharacterMaterial MaterialAt(int x, int y)
    {
        return (CharacterMaterial)material[y * Width + x];
    }

    /// <summary>
    /// The part of a filled pixel.
    /// </summary>
    public CharacterPart PartAt(int x, int y)
    {
        return (CharacterPart)part[y * Width + x];
    }

    /// <summary>
    /// The surface normal of a filled pixel, in view space.
    /// </summary>
    public Vector3 NormalAt(int x, int y)
    {
        return normal[y * Width + x];
    }

    /// <summary>
    /// The depth of a pixel, infinite when empty.
    /// </summary>
    public float DepthAt(int x, int y)
    {
        return depth[y * Width + x];
    }

    /// <summary>
    /// Gives a filled pixel another material, keeping its depth, part and normal.
    /// </summary>
    public void SetMaterial(int x, int y, CharacterMaterial value)
    {
        material[y * Width + x] = (int)value;
    }

    /// <summary>
    /// How many pixels hold <paramref name="value"/>.
    /// </summary>
    public int Count(CharacterMaterial value)
    {
        int count = 0;
        for (int i = 0; i < material.Length; i++)
        {
            count += material[i] == (int)value ? 1 : 0;
        }

        return count;
    }

    /// <summary>
    /// Draws a sphere of <paramref name="radius"/> rig units, at least <paramref name="minRadius"/> pixels, around
    /// <paramref name="world"/>: each pixel whose centre lies inside its disc and in front of what is there takes the
    /// sphere's front surface.
    /// </summary>
    public void Sphere(Vector3 world, float radius, CharacterMaterial value, CharacterPart owner, float minRadius = 0f)
    {
        Vector3 centre = Project(world);
        float r = Mathf.Max(radius * Scale, minRadius);
        float r2 = r * r;
        int x0 = Mathf.Max(Mathf.FloorToInt(centre.x + Pivot.x - r - 1f), 0);
        int x1 = Mathf.Min(Mathf.CeilToInt(centre.x + Pivot.x + r + 1f), Width);
        int y0 = Mathf.Max(Mathf.FloorToInt(centre.y + Pivot.y - r - 1f), 0);
        int y1 = Mathf.Min(Mathf.CeilToInt(centre.y + Pivot.y + r + 1f), Height);
        for (int y = y0; y < y1; y++)
        {
            float dy = y + 0.5f - Pivot.y - centre.y;
            for (int x = x0; x < x1; x++)
            {
                float dx = x + 0.5f - Pivot.x - centre.x;
                float d2 = dx * dx + dy * dy;
                if (d2 > r2)
                {
                    continue;
                }

                float dz = Mathf.Sqrt(r2 - d2);
                float z = centre.z - dz;
                int i = y * Width + x;
                if (z >= depth[i])
                {
                    continue;
                }

                depth[i] = z;
                material[i] = (int)value;
                part[i] = (int)owner;
                normal[i] = new Vector3(dx / r, dy / r, -dz / r);
            }
        }
    }

    /// <summary>
    /// Draws a chain of spheres from <paramref name="a"/> to <paramref name="b"/>, their radius going from
    /// <paramref name="radiusA"/> to <paramref name="radiusB"/>, a sphere every <paramref name="step"/> pixels.
    /// </summary>
    public void Capsule(Vector3 a, Vector3 b, float radiusA, float radiusB, CharacterMaterial value, CharacterPart owner, float step = 0.3f, float minRadius = 0f)
    {
        float length = (b - a).magnitude * Scale;
        int count = Mathf.Max(2, Mathf.CeilToInt(length / step) + 1);
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1);
            Sphere(a + (b - a) * t, radiusA + (radiusB - radiusA) * t, value, owner, minRadius);
        }
    }

    /// <summary>
    /// Draws a flat triangle facing the camera, its depth interpolated between its corners.
    /// </summary>
    public void Triangle(Vector3 a, Vector3 b, Vector3 c, CharacterMaterial value, CharacterPart owner)
    {
        Vector3 pa = Project(a);
        Vector3 pb = Project(b);
        Vector3 pc = Project(c);
        int x0 = Mathf.Max(Mathf.FloorToInt(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x)) + Pivot.x), 0);
        int x1 = Mathf.Min(Mathf.CeilToInt(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x)) + Pivot.x) + 1, Width);
        int y0 = Mathf.Max(Mathf.FloorToInt(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y)) + Pivot.y), 0);
        int y1 = Mathf.Min(Mathf.CeilToInt(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y)) + Pivot.y) + 1, Height);
        float denominator = (pb.y - pc.y) * (pa.x - pc.x) + (pc.x - pb.x) * (pa.y - pc.y);
        if (Mathf.Abs(denominator) < 1e-9f)
        {
            return;
        }

        for (int y = y0; y < y1; y++)
        {
            float py = y + 0.5f - Pivot.y;
            for (int x = x0; x < x1; x++)
            {
                float px = x + 0.5f - Pivot.x;
                float w0 = ((pb.y - pc.y) * (px - pc.x) + (pc.x - pb.x) * (py - pc.y)) / denominator;
                float w1 = ((pc.y - pa.y) * (px - pc.x) + (pa.x - pc.x) * (py - pc.y)) / denominator;
                float w2 = 1f - w0 - w1;
                if (w0 < -1e-6f || w1 < -1e-6f || w2 < -1e-6f)
                {
                    continue;
                }

                float z = w0 * pa.z + w1 * pb.z + w2 * pc.z;
                int i = y * Width + x;
                if (z >= depth[i])
                {
                    continue;
                }

                depth[i] = z;
                material[i] = (int)value;
                part[i] = (int)owner;
                normal[i] = new Vector3(0f, 0f, -1f);
            }
        }
    }

    /// <summary>
    /// The frame's colours, rows from the bottom: each filled pixel in its material's ramp (the middle tone when flat,
    /// otherwise the light tone above <paramref name="lightThreshold"/>, the shadow tone at or below
    /// <paramref name="shadowThreshold"/> and the middle tone between, by how much its surface faces the light); a
    /// pixel beside another part nearer by more than <paramref name="innerGap"/> in its outline colour; and every empty
    /// pixel beside the character in the outline colour of the nearest part next to it. Materials without an outline
    /// neither line nor are lined.
    /// </summary>
    public Color32[] Shade(ColourRamp[] ramps, float lightThreshold, float shadowThreshold, float innerGap)
    {
        Color32[] pixels = new Color32[Width * Height];
        for (int i = 0; i < pixels.Length; i++)
        {
            int m = material[i];
            if (m == Empty)
            {
                continue;
            }

            ColourRamp ramp = ramps[m];
            Color32 colour;
            if (CharacterMaterials.IsFlat((CharacterMaterial)m))
            {
                colour = ramp.Middle;
            }
            else
            {
                float lit = Vector3.Dot(normal[i], Light);
                colour = lit > lightThreshold ? ramp.Light : lit > shadowThreshold ? ramp.Middle : ramp.Shadow;
            }

            pixels[i] = Opaque(colour);
        }

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                int m = material[i];
                if (m == Empty)
                {
                    int nearest = NearestOutlinedNeighbour(x, y);
                    if (nearest != Empty)
                    {
                        pixels[i] = Opaque(ramps[material[nearest]].Outline);
                    }
                }
                else if (CharacterMaterials.HasOutline((CharacterMaterial)m) && HasNearerNeighbour(x, y, innerGap))
                {
                    pixels[i] = Opaque(ramps[m].Outline);
                }
            }
        }

        return pixels;
    }

    /// <summary>
    /// The index of the nearest filled, outlined pixel among the four beside (<paramref name="x"/>,
    /// <paramref name="y"/>), or <see cref="Empty"/>.
    /// </summary>
    private int NearestOutlinedNeighbour(int x, int y)
    {
        int best = Empty;
        float bestDepth = float.PositiveInfinity;
        for (int k = 0; k < 4; k++)
        {
            int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0);
            int ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
            if (!Contains(nx, ny))
            {
                continue;
            }

            int j = ny * Width + nx;
            if (material[j] == Empty || !CharacterMaterials.HasOutline((CharacterMaterial)material[j]) || depth[j] >= bestDepth)
            {
                continue;
            }

            best = j;
            bestDepth = depth[j];
        }

        return best;
    }

    /// <summary>
    /// Whether a pixel of another outlined part lies beside (<paramref name="x"/>, <paramref name="y"/>) nearer than
    /// it by more than <paramref name="gap"/>.
    /// </summary>
    private bool HasNearerNeighbour(int x, int y, float gap)
    {
        int i = y * Width + x;
        for (int k = 0; k < 4; k++)
        {
            int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0);
            int ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
            if (!Contains(nx, ny))
            {
                continue;
            }

            int j = ny * Width + nx;
            if (material[j] != Empty && part[j] != part[i] && depth[j] < depth[i] - gap && CharacterMaterials.HasOutline((CharacterMaterial)material[j]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <paramref name="colour"/> with full alpha.
    /// </summary>
    private static Color32 Opaque(Color32 colour)
    {
        return new Color32(colour.r, colour.g, colour.b, 255);
    }
}
