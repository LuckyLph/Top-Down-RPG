using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One frame of generated art being drawn, shared by the characters (<see cref="CharacterCanvas"/>) and the props.
/// World points (x east, y north, z up, in the caller's units) are projected through an orthographic camera pitched
/// <see cref="ViewPitch"/> degrees down, the game's 3/4 view, at <see cref="Scale"/> pixels per unit, with the pivot
/// pixel's bottom left corner at the world origin. Spheres, ellipsoids, convex solids, fields marched along each pixel's
/// view ray, triangles, flat discs and single dots are drawn straight onto the pixel grid with a depth test, each pixel
/// keeping its depth, material id, part id, surface normal in view and world space and its world position;
/// <see cref="Shade"/> then turns them into colours through a table of <see cref="ArtMaterial"/>s: covers, flat ramps,
/// a light colour only where the surface faces the light strongly, patterns and tone offsets, inner lines where a nearer
/// part overlaps a farther one, and an outline around everything. Points under the ground can be hidden, so a prop sinks
/// into it. Pixels are stored rows from the bottom.
/// </summary>
public class ArtCanvas
{
    public const float ViewPitch = 30f;
    protected const int Empty = -1;

    private const float BarkStretch = 2.2f / 16f;
    private const float BarkShare = 0.3f;
    private const float SpeckleDarkShare = 0.6f;
    private const float RingNoiseScale = 9f;
    private const float RingNoise = 0.5f;
    private const float RingShare = 0.3f;
    private const float StreakStretch = 1.5f;
    private const float StreakShare = 0.32f;
    private const float JointNoiseScale = 4f;
    private const float JointNoise = 0.45f;
    private const float JointShare = 0.16f;
    private const float HeightFacing = 0.35f;
    private const float HeightRise = 0.85f;
    private const float HeightBias = 0.05f;
    private const float GradientStep = 0.5f;
    private const int RefineSteps = 10;

    public static readonly float SinPitch = Mathf.Sin(ViewPitch * Mathf.Deg2Rad);
    public static readonly float CosPitch = Mathf.Cos(ViewPitch * Mathf.Deg2Rad);

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
    private readonly Vector3[] worldNormal;
    private readonly Vector3[] worldPosition;

    /// <summary>
    /// Creates an empty frame of <paramref name="width"/> x <paramref name="height"/> pixels whose world origin falls on
    /// pixel <paramref name="pivot"/> (from the bottom left), drawn at <paramref name="scale"/> pixels per unit.
    /// </summary>
    public ArtCanvas(int width, int height, Vector2Int pivot, float scale)
    {
        Width = width;
        Height = height;
        Pivot = pivot;
        Scale = scale;
        depth = new float[width * height];
        material = new int[width * height];
        part = new int[width * height];
        normal = new Vector3[width * height];
        worldNormal = new Vector3[width * height];
        worldPosition = new Vector3[width * height];
        Clear();
    }

    public int Width { get; }
    public int Height { get; }
    public Vector2Int Pivot { get; }
    public float Scale { get; }

    /// <summary>
    /// Whether points under the ground (z below 0) are hidden, as the ground in front of them would hide them.
    /// </summary>
    public bool HidesBelowGround { get; set; }

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
            worldNormal[i] = Vector3.zero;
            worldPosition[i] = Vector3.zero;
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
    /// A world direction in view space.
    /// </summary>
    public static Vector3 ToView(Vector3 world)
    {
        return new Vector3(world.x, world.y * SinPitch + world.z * CosPitch, world.y * CosPitch - world.z * SinPitch);
    }

    /// <summary>
    /// A view direction in world space.
    /// </summary>
    public static Vector3 ToWorld(Vector3 view)
    {
        return new Vector3(view.x, view.y * SinPitch + view.z * CosPitch, view.y * CosPitch - view.z * SinPitch);
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
    /// The material id of a filled pixel.
    /// </summary>
    public int MaterialIdAt(int x, int y)
    {
        return material[y * Width + x];
    }

    /// <summary>
    /// The part id of a filled pixel.
    /// </summary>
    public int PartIdAt(int x, int y)
    {
        return part[y * Width + x];
    }

    /// <summary>
    /// The surface normal of a filled pixel, in view space.
    /// </summary>
    public Vector3 NormalAt(int x, int y)
    {
        return normal[y * Width + x];
    }

    /// <summary>
    /// The surface normal of a filled pixel, in world space.
    /// </summary>
    public Vector3 WorldNormalAt(int x, int y)
    {
        return worldNormal[y * Width + x];
    }

    /// <summary>
    /// Where in the world a filled pixel's surface lies.
    /// </summary>
    public Vector3 WorldPositionAt(int x, int y)
    {
        return worldPosition[y * Width + x];
    }

    /// <summary>
    /// The depth of a pixel, infinite when empty.
    /// </summary>
    public float DepthAt(int x, int y)
    {
        return depth[y * Width + x];
    }

    /// <summary>
    /// Gives a filled pixel another material id, keeping its depth, part and normal.
    /// </summary>
    public void SetMaterialId(int x, int y, int value)
    {
        material[y * Width + x] = value;
    }

    /// <summary>
    /// How many pixels hold material id <paramref name="value"/>.
    /// </summary>
    public int CountId(int value)
    {
        int count = 0;
        for (int i = 0; i < material.Length; i++)
        {
            count += material[i] == value ? 1 : 0;
        }

        return count;
    }

    /// <summary>
    /// Draws a sphere of <paramref name="radius"/> units, at least <paramref name="minRadius"/> pixels, around
    /// <paramref name="world"/>: each pixel whose centre lies inside its disc and in front of what is there takes the
    /// sphere's front surface. With an <paramref name="envelope"/>, each normal leans toward the envelope's by
    /// <paramref name="blend"/>. A <paramref name="depthOffset"/> (pixels of depth, negative nearer) moves it in the depth
    /// test only, not where it lands or how it is lit.
    /// </summary>
    public void Sphere(Vector3 world, float radius, int value, int owner, float minRadius = 0f, ArtEnvelope envelope = null, float blend = 0f, float depthOffset = 0f)
    {
        Vector3 centre = Project(world);
        centre.z += depthOffset;
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

                Vector3 viewNormal = new(dx / r, dy / r, -dz / r);
                Vector3 surfaceNormal = ToWorld(viewNormal);
                Vector3 surface = world + surfaceNormal * (r / Scale);
                if (HidesBelowGround && surface.z < 0f)
                {
                    continue;
                }

                if (envelope != null && blend > 0f)
                {
                    surfaceNormal = Vector3.Lerp(surfaceNormal, envelope.NormalAt(surface), blend).normalized;
                    viewNormal = ToView(surfaceNormal);
                }

                depth[i] = z;
                material[i] = value;
                part[i] = owner;
                normal[i] = viewNormal;
                worldNormal[i] = surfaceNormal;
                worldPosition[i] = surface;
            }
        }
    }

    /// <summary>
    /// Draws a flat disc facing the camera, <paramref name="radius"/> pixels around where <paramref name="world"/>
    /// lands and at its depth: each pixel whose centre lies inside it and in front of what is there.
    /// </summary>
    public void Disc(Vector3 world, float radius, int value, int owner)
    {
        Vector3 centre = Project(world);
        float r2 = radius * radius;
        int x0 = Mathf.Max(Mathf.FloorToInt(centre.x + Pivot.x - radius - 1f), 0);
        int x1 = Mathf.Min(Mathf.CeilToInt(centre.x + Pivot.x + radius + 1f), Width);
        int y0 = Mathf.Max(Mathf.FloorToInt(centre.y + Pivot.y - radius - 1f), 0);
        int y1 = Mathf.Min(Mathf.CeilToInt(centre.y + Pivot.y + radius + 1f), Height);
        for (int y = y0; y < y1; y++)
        {
            float dy = y + 0.5f - Pivot.y - centre.y;
            for (int x = x0; x < x1; x++)
            {
                float dx = x + 0.5f - Pivot.x - centre.x;
                if (dx * dx + dy * dy <= r2)
                {
                    Put(y * Width + x, centre.z, WorldOf(centre.x + dx, centre.y + dy, centre.z), value, owner);
                }
            }
        }
    }

    /// <summary>
    /// Draws the one pixel <paramref name="world"/> falls in, at its depth, when it is inside the frame and in front of
    /// what is there.
    /// </summary>
    public void Dot(Vector3 world, int value, int owner)
    {
        Vector2Int pixel = PixelOf(world);
        if (Contains(pixel.x, pixel.y))
        {
            Put(pixel.y * Width + pixel.x, Project(world).z, world, value, owner);
        }
    }

    /// <summary>
    /// Draws a chain of spheres from <paramref name="a"/> to <paramref name="b"/>, their radius going from
    /// <paramref name="radiusA"/> to <paramref name="radiusB"/>, a sphere every <paramref name="step"/> pixels.
    /// </summary>
    public void Capsule(Vector3 a, Vector3 b, float radiusA, float radiusB, int value, int owner, float step = 0.3f, float minRadius = 0f)
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
    public void Triangle(Vector3 a, Vector3 b, Vector3 c, int value, int owner)
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
                Put(y * Width + x, z, WorldOf(px, py, z), value, owner);
            }
        }
    }

    /// <summary>
    /// Draws an ellipsoid around <paramref name="centre"/> whose half axes are <paramref name="axisX"/>,
    /// <paramref name="axisY"/> and <paramref name="axisZ"/> (perpendicular, in world units, at any orientation): each
    /// pixel whose view ray meets it takes its nearest surface. With an <paramref name="envelope"/>, each normal leans
    /// toward the envelope's by <paramref name="blend"/>. A <paramref name="depthOffset"/> (pixels of depth, negative
    /// nearer) moves it in the depth test only.
    /// </summary>
    public void Ellipsoid(Vector3 centre, Vector3 axisX, Vector3 axisY, Vector3 axisZ, int value, int owner, ArtEnvelope envelope = null, float blend = 0f, float depthOffset = 0f)
    {
        Vector3 extent = new(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
        if (!PixelBounds(new Bounds(centre, extent * 2f), out int x0, out int x1, out int y0, out int y1))
        {
            return;
        }

        Vector3 ra = axisX / axisX.sqrMagnitude;
        Vector3 rb = axisY / axisY.sqrMagnitude;
        Vector3 rc = axisZ / axisZ.sqrMagnitude;
        Vector3 ray = RayStep();
        Vector3 qd = new(Vector3.Dot(ray, ra), Vector3.Dot(ray, rb), Vector3.Dot(ray, rc));
        float a = qd.sqrMagnitude;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                Vector3 o = RayOrigin(x, y) - centre;
                Vector3 q0 = new(Vector3.Dot(o, ra), Vector3.Dot(o, rb), Vector3.Dot(o, rc));
                float b = 2f * Vector3.Dot(q0, qd);
                float c = q0.sqrMagnitude - 1f;
                float discriminant = b * b - 4f * a * c;
                if (discriminant < 0f)
                {
                    continue;
                }

                float t = (-b - Mathf.Sqrt(discriminant)) / (2f * a);
                Vector3 q = q0 + qd * t;
                Vector3 surface = centre + o + ray * t;
                Vector3 surfaceNormal = (ra * q.x + rb * q.y + rc * q.z).normalized;
                if (envelope != null && blend > 0f)
                {
                    surfaceNormal = Vector3.Lerp(surfaceNormal, envelope.NormalAt(surface), blend);
                }

                Write(y * Width + x, t + depthOffset, surface, surfaceNormal, value, owner);
            }
        }
    }

    /// <summary>
    /// Draws the convex solid where every plane of <paramref name="planes"/> (xyz its outward normal, w its distance
    /// from the origin, inside where the point's dot product with the normal is at most w) holds, inside
    /// <paramref name="bounds"/>: each pixel takes the face its view ray enters through, shaded by that face's normal.
    /// </summary>
    public void Convex(IReadOnlyList<Vector4> planes, Bounds bounds, int value, int owner)
    {
        if (!PixelBounds(bounds, out int x0, out int x1, out int y0, out int y1))
        {
            return;
        }

        Vector3 ray = RayStep();
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                Vector3 o = RayOrigin(x, y);
                float tIn = float.NegativeInfinity, tOut = float.PositiveInfinity;
                int entered = Empty;
                bool missed = false;
                for (int k = 0; k < planes.Count; k++)
                {
                    Vector3 n = planes[k];
                    float along = Vector3.Dot(n, ray);
                    float room = planes[k].w - Vector3.Dot(n, o);
                    if (Mathf.Abs(along) < 1e-9f)
                    {
                        if (room < 0f)
                        {
                            missed = true;
                            break;
                        }

                        continue;
                    }

                    float t = room / along;
                    if (along < 0f)
                    {
                        if (t > tIn)
                        {
                            tIn = t;
                            entered = k;
                        }
                    }
                    else if (t < tOut)
                    {
                        tOut = t;
                    }
                }

                if (missed || entered == Empty || tIn > tOut)
                {
                    continue;
                }

                Write(y * Width + x, tIn, o + ray * tIn, planes[entered], value, owner);
            }
        }
    }

    /// <summary>
    /// Draws the solid where <paramref name="field"/> is below zero inside <paramref name="bounds"/>, marched along each
    /// pixel's view ray in steps of <paramref name="step"/> depth pixels and refined by bisection; its gradient is the
    /// normal, and <paramref name="materialAt"/> picks each pixel's material id from its world position and normal.
    /// </summary>
    public void Field(Func<Vector3, float> field, Bounds bounds, Func<Vector3, Vector3, int> materialAt, int owner, float step = 0.35f)
    {
        if (!PixelBounds(bounds, out int x0, out int x1, out int y0, out int y1))
        {
            return;
        }

        Vector3 ray = RayStep();
        float eps = GradientStep / Scale;
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                Vector3 o = RayOrigin(x, y);
                float t0 = float.NegativeInfinity, t1 = float.PositiveInfinity;
                if (!Slab(o.x, ray.x, min.x, max.x, ref t0, ref t1) || !Slab(o.y, ray.y, min.y, max.y, ref t0, ref t1) || !Slab(o.z, ray.z, min.z, max.z, ref t0, ref t1))
                {
                    continue;
                }

                int i = y * Width + x;
                if (t0 >= depth[i])
                {
                    continue;
                }

                float outside = t0;
                float t = t0;
                bool hit = field(o + ray * t0) < 0f;
                while (!hit && t < t1)
                {
                    outside = t;
                    t = Mathf.Min(t + step, t1);
                    hit = field(o + ray * t) < 0f;
                    if (t >= t1)
                    {
                        break;
                    }
                }

                if (!hit)
                {
                    continue;
                }

                float inside = t;
                for (int k = 0; k < RefineSteps && inside > outside; k++)
                {
                    float middle = (outside + inside) * 0.5f;
                    if (field(o + ray * middle) < 0f)
                    {
                        inside = middle;
                    }
                    else
                    {
                        outside = middle;
                    }
                }

                Vector3 surface = o + ray * inside;
                Vector3 gradient = new(
                    field(surface + new Vector3(eps, 0f, 0f)) - field(surface - new Vector3(eps, 0f, 0f)),
                    field(surface + new Vector3(0f, eps, 0f)) - field(surface - new Vector3(0f, eps, 0f)),
                    field(surface + new Vector3(0f, 0f, eps)) - field(surface - new Vector3(0f, 0f, eps)));
                Vector3 surfaceNormal = gradient.sqrMagnitude > 1e-12f ? gradient.normalized : -ray.normalized;
                Write(i, inside, surface, surfaceNormal, materialAt(surface, surfaceNormal), owner);
            }
        }
    }

    /// <summary>
    /// The frame's colours, rows from the bottom, each material id drawn as <paramref name="materials"/> says. A pixel
    /// whose material has a cover takes the cover where its surface faces up past the cover's threshold, with value noise
    /// shifted by <paramref name="noiseOffset"/>. Each filled pixel is then in its material's ramp: the middle tone when
    /// flat, otherwise the light tone above <paramref name="lightThreshold"/>, the shadow tone at or below
    /// <paramref name="shadowThreshold"/> and the middle tone between, by how much its surface faces the light (or how
    /// high it lies, for a material with a tone height), moved by the material's tone offset and pattern within the
    /// ramp's three tones; a pixel beside another part nearer by more than <paramref name="innerGap"/> in its outline
    /// colour; and every empty pixel beside the drawing in the outline colour of the nearest part next to it. Materials
    /// without an outline neither line nor are lined.
    /// </summary>
    public Color32[] Shade(IReadOnlyList<ArtMaterial> materials, float lightThreshold, float shadowThreshold, float innerGap, Vector3 noiseOffset = default)
    {
        int[] shown = new int[material.Length];
        for (int i = 0; i < shown.Length; i++)
        {
            int m = material[i];
            shown[i] = m != Empty && materials[m].Cover != Empty && IsCovered(i, materials[m], noiseOffset) ? materials[m].Cover : m;
        }

        Color32[] pixels = new Color32[Width * Height];
        for (int i = 0; i < pixels.Length; i++)
        {
            int m = shown[i];
            if (m == Empty)
            {
                continue;
            }

            ArtMaterial art = materials[m];
            int tone = 1;
            if (!art.Flat)
            {
                float lit = Vector3.Dot(normal[i], Light);
                if (art.ToneHeight > 0f)
                {
                    lit = lit * HeightFacing + worldPosition[i].z / art.ToneHeight * HeightRise - HeightBias;
                }

                tone = lit > lightThreshold ? 2 : lit > shadowThreshold ? 1 : 0;
            }

            tone = Mathf.Clamp(tone + art.ToneOffset + PatternOffset(art, i), 0, 2);
            ColourRamp ramp = art.Ramp;
            pixels[i] = Opaque(tone == 2 ? ramp.Light : tone == 1 ? ramp.Middle : ramp.Shadow);
        }

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                int m = shown[i];
                if (m == Empty)
                {
                    int nearest = NearestOutlinedNeighbour(x, y, shown, materials);
                    if (nearest != Empty)
                    {
                        pixels[i] = Opaque(materials[shown[nearest]].Ramp.Outline);
                    }
                }
                else if (materials[m].Outlined && HasNearerNeighbour(x, y, innerGap, shown, materials))
                {
                    pixels[i] = Opaque(materials[m].Ramp.Outline);
                }
            }
        }

        return pixels;
    }

    /// <summary>
    /// Whether pixel <paramref name="index"/> faces up enough to take <paramref name="art"/>'s cover.
    /// </summary>
    private bool IsCovered(int index, ArtMaterial art, Vector3 noiseOffset)
    {
        float noise = art.CoverNoise > 0f ? (ArtNoise.Value(worldPosition[index] * art.CoverNoiseScale + noiseOffset) - 0.5f) * art.CoverNoise : 0f;
        return worldNormal[index].z + noise > art.CoverThreshold;
    }

    /// <summary>
    /// How many tones <paramref name="art"/>'s pattern moves pixel <paramref name="index"/>: bark one darker in its
    /// grooves, stone one darker or lighter on its speckles, a cut end one darker on its rings, needles one darker on
    /// their streaks (value noise on a circle around the axis, so it has no seam) and basalt one darker on its joints,
    /// read in the material's pattern frame when it has one.
    /// </summary>
    private int PatternOffset(ArtMaterial art, int index)
    {
        Vector3 p = art.HasPatternFrame ? art.PatternRotation * (worldPosition[index] - art.PatternOrigin) : worldPosition[index];
        switch (art.Pattern)
        {
            case ArtPattern.Bark:
                return ArtNoise.Value(new Vector3(p.x * art.PatternScale, p.y * art.PatternScale, p.z * art.PatternScale * BarkStretch)) < BarkShare ? -1 : 0;
            case ArtPattern.Speckle:
            {
                Vector3 pixel = p * Scale;
                float h = ArtNoise.Hash(Mathf.RoundToInt(pixel.x), Mathf.RoundToInt(pixel.y), Mathf.RoundToInt(pixel.z));
                return h < art.PatternScale * SpeckleDarkShare ? -1 : h > 1f - art.PatternScale * (1f - SpeckleDarkShare) ? 1 : 0;
            }
            case ArtPattern.Rings:
            {
                float ring = new Vector2(p.x, p.y).magnitude * art.PatternScale + ArtNoise.Value(p * RingNoiseScale) * RingNoise;
                return ring - Mathf.Floor(ring) < RingShare ? -1 : 0;
            }
            case ArtPattern.Streaks:
            {
                float angle = Mathf.Atan2(p.y, p.x);
                Vector3 around = new(Mathf.Cos(angle) * art.PatternScale, Mathf.Sin(angle) * art.PatternScale, p.z * StreakStretch);
                return ArtNoise.Value(around) < StreakShare ? -1 : 0;
            }
            case ArtPattern.Joints:
            {
                float joint = p.z * art.PatternScale + ArtNoise.Value(p * JointNoiseScale) * JointNoise;
                return joint - Mathf.Floor(joint) < JointShare ? -1 : 0;
            }
            default:
                return 0;
        }
    }

    /// <summary>
    /// Gives pixel <paramref name="index"/> a flat surface facing the camera at depth <paramref name="z"/> and world
    /// position <paramref name="world"/> when that is in front of what is there.
    /// </summary>
    private void Put(int index, float z, Vector3 world, int value, int owner)
    {
        if (z >= depth[index] || (HidesBelowGround && world.z < 0f))
        {
            return;
        }

        depth[index] = z;
        material[index] = value;
        part[index] = owner;
        normal[index] = new Vector3(0f, 0f, -1f);
        worldNormal[index] = ToWorld(normal[index]);
        worldPosition[index] = world;
    }

    /// <summary>
    /// Gives pixel <paramref name="index"/> the surface at depth <paramref name="t"/>, world position
    /// <paramref name="world"/> and world normal <paramref name="surfaceNormal"/> when that is in front of what is there.
    /// </summary>
    private void Write(int index, float t, Vector3 world, Vector3 surfaceNormal, int value, int owner)
    {
        if (t >= depth[index] || (HidesBelowGround && world.z < 0f))
        {
            return;
        }

        surfaceNormal.Normalize();
        depth[index] = t;
        material[index] = value;
        part[index] = owner;
        worldNormal[index] = surfaceNormal;
        normal[index] = ToView(surfaceNormal);
        worldPosition[index] = world;
    }

    /// <summary>
    /// The world point at view position (<paramref name="viewX"/>, <paramref name="viewY"/>) pixels from the pivot and
    /// depth <paramref name="viewZ"/>.
    /// </summary>
    private Vector3 WorldOf(float viewX, float viewY, float viewZ)
    {
        return ToWorld(new Vector3(viewX, viewY, viewZ)) / Scale;
    }

    /// <summary>
    /// The world point where pixel (<paramref name="x"/>, <paramref name="y"/>)'s view ray crosses depth 0.
    /// </summary>
    private Vector3 RayOrigin(int x, int y)
    {
        return WorldOf(x + 0.5f - Pivot.x, y + 0.5f - Pivot.y, 0f);
    }

    /// <summary>
    /// How far a view ray moves in the world per pixel of depth.
    /// </summary>
    private Vector3 RayStep()
    {
        return ToWorld(new Vector3(0f, 0f, 1f)) / Scale;
    }

    /// <summary>
    /// The pixels <paramref name="bounds"/> (in world units) can cover, clamped to the frame; false when none.
    /// </summary>
    private bool PixelBounds(Bounds bounds, out int x0, out int x1, out int y0, out int y1)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        float left = float.PositiveInfinity, right = float.NegativeInfinity, bottom = float.PositiveInfinity, top = float.NegativeInfinity;
        for (int k = 0; k < 8; k++)
        {
            Vector3 corner = new((k & 1) == 0 ? min.x : max.x, (k & 2) == 0 ? min.y : max.y, (k & 4) == 0 ? min.z : max.z);
            Vector3 view = Project(corner);
            left = Mathf.Min(left, view.x);
            right = Mathf.Max(right, view.x);
            bottom = Mathf.Min(bottom, view.y);
            top = Mathf.Max(top, view.y);
        }

        x0 = Mathf.Max(Mathf.FloorToInt(left + Pivot.x) - 1, 0);
        x1 = Mathf.Min(Mathf.CeilToInt(right + Pivot.x) + 1, Width);
        y0 = Mathf.Max(Mathf.FloorToInt(bottom + Pivot.y) - 1, 0);
        y1 = Mathf.Min(Mathf.CeilToInt(top + Pivot.y) + 1, Height);
        return x0 < x1 && y0 < y1;
    }

    /// <summary>
    /// Narrows [<paramref name="t0"/>, <paramref name="t1"/>] to where a ray from <paramref name="origin"/> moving
    /// <paramref name="delta"/> a step lies between <paramref name="low"/> and <paramref name="high"/> on one axis;
    /// false when it never does.
    /// </summary>
    private static bool Slab(float origin, float delta, float low, float high, ref float t0, ref float t1)
    {
        if (Mathf.Abs(delta) < 1e-9f)
        {
            return origin >= low && origin <= high;
        }

        float a = (low - origin) / delta;
        float b = (high - origin) / delta;
        t0 = Mathf.Max(t0, Mathf.Min(a, b));
        t1 = Mathf.Min(t1, Mathf.Max(a, b));
        return t0 <= t1;
    }

    /// <summary>
    /// The index of the nearest filled, outlined pixel among the four beside (<paramref name="x"/>,
    /// <paramref name="y"/>), or <see cref="Empty"/>.
    /// </summary>
    private int NearestOutlinedNeighbour(int x, int y, int[] shown, IReadOnlyList<ArtMaterial> materials)
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
            if (shown[j] == Empty || !materials[shown[j]].Outlined || depth[j] >= bestDepth)
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
    private bool HasNearerNeighbour(int x, int y, float gap, int[] shown, IReadOnlyList<ArtMaterial> materials)
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
            if (shown[j] != Empty && part[j] != part[i] && depth[j] < depth[i] - gap && materials[shown[j]].Outlined)
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
