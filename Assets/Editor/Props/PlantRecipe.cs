using System;
using UnityEngine;

/// <summary>
/// The shapes a <see cref="PlantRecipe"/> draws.
/// </summary>
public enum PlantForm
{
    Fern,
    Blades,
    Mushrooms
}

/// <summary>
/// A small plant (Docs/PropGeneration.md): a fern of arching fronds (<see cref="PlantFronds"/>), a tuft of blades with
/// cattail heads or none, as reeds, cattails and beach grass (<see cref="PlantBlades"/>), or a cluster of mushrooms
/// (<see cref="PlantCaps"/>). Plants are walked through, so they touch the ground only for a recipe made to block.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Props/Plant Recipe", fileName = "Fern")]
public sealed class PlantRecipe : PropRecipe
{
    [Tooltip("A fern, a tuft of blades or a cluster of mushrooms; only that form's settings are used.")]
    [SerializeField] private PlantForm form = PlantForm.Fern;
    [SerializeField] private PlantFronds fronds = new();
    [SerializeField] private PlantBlades blades = new();
    [SerializeField] private PlantCaps caps = new();

    [Header("Colours")]
    [Tooltip("The fronds', blades' or caps' colours.")]
    [SerializeField] private ColourRamp main = Ramp(0x133a18, 0x2f7a2c, 0x52a03a, 0x92c856);
    [Tooltip("The fern's stems, the cattails' heads or the mushrooms' stems.")]
    [SerializeField] private ColourRamp second = Ramp(0x0e2a12, 0x1f5a22, 0x2f7a2c, 0x52a03a);
    [Tooltip("The mushrooms' spots.")]
    [SerializeField] private Color32 spots = new(240, 232, 216, 255);

    /// <inheritdoc />
    public override void Draw(PropDrawing drawing)
    {
        switch (form)
        {
            case PlantForm.Blades:
                blades.Draw(drawing, drawing.AddMaterial(new ArtMaterial(main, outlined: false, toneHeight: blades.Tallest), true), drawing.AddMaterial(new ArtMaterial(second), false));
                break;
            case PlantForm.Mushrooms:
                int capId = drawing.AddMaterial(new ArtMaterial(main), false);
                int stemId = drawing.AddMaterial(new ArtMaterial(second), true);
                caps.Draw(drawing, capId, stemId, drawing.AddMaterial(new ArtMaterial(new ColourRamp(spots, spots, spots, spots), flat: true, outlined: false), false));
                break;
            default:
                int leafId = drawing.AddMaterial(new ArtMaterial(main), false);
                fronds.Draw(drawing, leafId, drawing.AddMaterial(new ArtMaterial(second, outlined: false), true));
                break;
        }
    }

    /// <summary>
    /// The point at <paramref name="t"/> along the quadratic curve from <paramref name="a"/> through the pull of
    /// <paramref name="b"/> to <paramref name="c"/>.
    /// </summary>
    internal static Vector3 Curve(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        float s = 1f - t;
        return a * (s * s) + b * (2f * s * t) + c * (t * t);
    }

    /// <summary>
    /// The direction the quadratic curve from <paramref name="a"/> through the pull of <paramref name="b"/> to
    /// <paramref name="c"/> runs at <paramref name="t"/>.
    /// </summary>
    internal static Vector3 CurveDirection(Vector3 a, Vector3 b, Vector3 c, float t)
    {
        return ((b - a) * (1f - t) + (c - b) * t).normalized;
    }

    /// <summary>
    /// Draws the quadratic curve from <paramref name="a"/> through the pull of <paramref name="b"/> to
    /// <paramref name="c"/> as a chain of <paramref name="segments"/> capsules, from <paramref name="from"/> to
    /// <paramref name="to"/> along it, tapering from <paramref name="radiusA"/> to <paramref name="radiusB"/>, at least
    /// <paramref name="minPixels"/> pixels across.
    /// </summary>
    internal static void Stroke(ArtCanvas canvas, Vector3 a, Vector3 b, Vector3 c, float from, float to, float radiusA, float radiusB, int segments, int material, int part, float minPixels)
    {
        for (int i = 0; i < segments; i++)
        {
            float t0 = Mathf.Lerp(from, to, i / (float)segments);
            float t1 = Mathf.Lerp(from, to, (i + 1) / (float)segments);
            float r0 = Mathf.Lerp(radiusA, radiusB, i / (float)segments);
            float r1 = Mathf.Lerp(radiusA, radiusB, (i + 1) / (float)segments);
            canvas.Capsule(Curve(a, b, c, t0), Curve(a, b, c, t1), r0, r1, material, part, minRadius: minPixels);
        }
    }
}

/// <summary>
/// A fern's fronds: arching curves spread evenly around the base from a random first angle, each a thin stem with no
/// line and pairs of leaflets along it, shrinking toward its tip, each frond its own part so inner lines part them.
/// </summary>
[Serializable]
public sealed class PlantFronds
{
    private const float Foot = 0.03f;
    private const float FootHeight = 0.02f;
    private const float Pull = 0.35f;
    private const float AngleJitter = 0.3f;
    private const float FirstLeaflet = 0.18f;
    private const float LeafletForward = 0.5f;
    private const float LeafletDroop = 0.3f;
    private const float LeafletThickness = 0.015f;
    private const int StemSegments = 8;
    private const float MinPixels = 0.71f;

    [Tooltip("How many fronds the fern has.")]
    [SerializeField, Min(1)] private int count = 8;
    [Tooltip("How far out from the base a frond's tip lies, in cells: the nearest and farthest.")]
    [SerializeField] private Vector2 reach = new(0.36f, 0.5f);
    [Tooltip("How high a frond's arch peaks, in cells: the lowest and highest.")]
    [SerializeField] private Vector2 rise = new(0.2f, 0.28f);
    [Tooltip("How high a frond's tip lies above the ground, in cells.")]
    [SerializeField, Min(0f)] private float tipHeight = 0.03f;
    [Tooltip("How many pairs of leaflets a frond has.")]
    [SerializeField, Min(1)] private int leaflets = 7;
    [Tooltip("A leaflet's length near the frond's foot and at its tip, in cells.")]
    [SerializeField] private Vector2 leafletLength = new(0.12f, 0.04f);
    [Tooltip("A leaflet's half width, in cells.")]
    [SerializeField, Min(0.005f)] private float leafletWidth = 0.03f;
    [Tooltip("The stem's radius, in cells.")]
    [SerializeField, Min(0.005f)] private float stemRadius = 0.016f;

    /// <summary>
    /// Draws the fronds, their leaflets in <paramref name="leaflet"/> and their stems in <paramref name="stem"/>.
    /// </summary>
    public void Draw(PropDrawing drawing, int leaflet, int stem)
    {
        ArtCanvas canvas = drawing.Canvas;
        float phase = drawing.Range(0f, 2f * Mathf.PI);
        for (int k = 0; k < count; k++)
        {
            float angle = phase + k * 2f * Mathf.PI / count + drawing.Range(-AngleJitter, AngleJitter);
            Vector3 outward = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            float length = drawing.Range(reach);
            float peak = drawing.Range(rise);
            Vector3 a = outward * Foot + Vector3.forward * FootHeight;
            Vector3 c = outward * length + Vector3.forward * tipHeight;
            Vector3 b = outward * length * Pull + Vector3.forward * (2f * peak - (a.z + c.z) * 0.5f);
            int part = drawing.NewPart();
            PlantRecipe.Stroke(canvas, a, b, c, 0f, 1f, stemRadius, stemRadius * 0.6f, StemSegments, stem, part, MinPixels);
            for (int i = 0; i < leaflets; i++)
            {
                float share = (i + 0.5f) / leaflets;
                float t = Mathf.Lerp(FirstLeaflet, 1f, share);
                Vector3 at = PlantRecipe.Curve(a, b, c, t);
                Vector3 along = PlantRecipe.CurveDirection(a, b, c, t);
                Vector3 across = Vector3.Cross(along, Vector3.forward).normalized;
                float size = Mathf.Lerp(leafletLength.x, leafletLength.y, share);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 direction = (across * s + along * LeafletForward - Vector3.forward * LeafletDroop).normalized;
                    Vector3 width = Vector3.Cross(Vector3.forward, direction).normalized;
                    Vector3 normal = Vector3.Cross(direction, width).normalized;
                    if (normal.z < 0f)
                    {
                        normal = -normal;
                    }

                    canvas.Ellipsoid(at + direction * size * 0.5f, direction * size * 0.5f, width * leafletWidth, normal * LeafletThickness, leaflet, part);
                }
            }
        }
    }
}

/// <summary>
/// A tuft of blades, as reeds and beach grass: thin curves with no line rising from feet scattered over a small disc,
/// leaning out from its middle and arching over when they droop, lit by how high they reach; and, for cattails, a few
/// stiffer stems each holding a lined head below its tip.
/// </summary>
[Serializable]
public sealed class PlantBlades
{
    private const float Sink = 0.02f;
    private const float Pull = 0.3f;
    private const float PullHeight = 0.6f;
    private const float HeadStem = 0.6f;
    private const float HeadLean = 0.3f;
    private static readonly Vector2 HeadHeight = new(1.1f, 1.25f);
    private const float StemThinning = 0.6f;
    private const int Segments = 8;
    private const float MinPixels = 0.71f;

    [Tooltip("How many blades the tuft has.")]
    [SerializeField, Min(1)] private int count = 13;
    [Tooltip("The radius of the disc the blades rise from, in cells.")]
    [SerializeField, Min(0f)] private float tuft = 0.08f;
    [Tooltip("How tall a blade grows, in cells: the shortest and tallest.")]
    [SerializeField] private Vector2 height = new(0.5f, 0.85f);
    [Tooltip("How far out a blade's tip leans, as a share of its height: the least and most.")]
    [SerializeField] private Vector2 lean = new(0.08f, 0.3f);
    [Tooltip("How far a blade's tip hangs below the height it rose to, as a share of its height: 0 stands, more arches over.")]
    [SerializeField, Range(0f, 1f)] private float droop;
    [Tooltip("A blade's radius at its foot and at its tip, in cells.")]
    [SerializeField] private Vector2 thickness = new(0.035f, 0.012f);
    [Tooltip("How many stems hold a head, as cattails do.")]
    [SerializeField, Min(0)] private int heads;
    [Tooltip("A head's length, in cells: the shortest and longest.")]
    [SerializeField] private Vector2 headLength = new(0.14f, 0.18f);
    [Tooltip("A head's radius, in cells.")]
    [SerializeField, Min(0.005f)] private float headRadius = 0.04f;
    [Tooltip("How far up its stem a head's top lies, as a share of the stem's length.")]
    [SerializeField, Range(0.3f, 1f)] private float headAt = 0.85f;

    /// <summary>
    /// The tallest a blade or head's stem grows, in cells: the height over which its tone rises.
    /// </summary>
    public float Tallest => heads > 0 ? height.y * HeadHeight.y : height.y;

    /// <summary>
    /// Draws the blades in <paramref name="blade"/> and the heads in <paramref name="head"/>.
    /// </summary>
    public void Draw(PropDrawing drawing, int blade, int head)
    {
        ArtCanvas canvas = drawing.Canvas;
        for (int k = 0; k < count; k++)
        {
            Foot(drawing, tuft, out Vector3 foot, out Vector3 outward);
            float h = drawing.Range(height);
            float reachOut = drawing.Range(lean) * h;
            Vector3 a = foot - Vector3.forward * Sink;
            Vector3 b = foot + outward * reachOut * Pull + Vector3.forward * h * (PullHeight + droop);
            Vector3 c = foot + outward * reachOut + Vector3.forward * h * (1f - droop);
            PlantRecipe.Stroke(canvas, a, b, c, 0f, 1f, thickness.x, thickness.y, Segments, blade, drawing.NewPart(), MinPixels);
        }

        for (int k = 0; k < heads; k++)
        {
            Foot(drawing, tuft * HeadStem, out Vector3 foot, out Vector3 outward);
            float h = height.y * drawing.Range(HeadHeight);
            float reachOut = drawing.Range(lean) * h * HeadLean;
            Vector3 a = foot - Vector3.forward * Sink;
            Vector3 b = foot + outward * reachOut * Pull + Vector3.forward * h * PullHeight;
            Vector3 c = foot + outward * reachOut + Vector3.forward * h;
            float stemRadius = thickness.x * StemThinning;
            PlantRecipe.Stroke(canvas, a, b, c, 0f, 1f, stemRadius, thickness.y, Segments, blade, drawing.NewPart(), MinPixels);
            float top = headAt;
            float bottom = Mathf.Max(0.1f, top - drawing.Range(headLength) / h);
            canvas.Capsule(PlantRecipe.Curve(a, b, c, bottom), PlantRecipe.Curve(a, b, c, top), headRadius, headRadius, head, drawing.NewPart());
        }
    }

    /// <summary>
    /// A foot picked evenly over a disc of <paramref name="radius"/> around the base, and the way out from its middle.
    /// </summary>
    private static void Foot(PropDrawing drawing, float radius, out Vector3 foot, out Vector3 outward)
    {
        float angle = drawing.Range(0f, 2f * Mathf.PI);
        outward = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
        foot = outward * radius * Mathf.Sqrt((float)drawing.Random.NextDouble());
    }
}

/// <summary>
/// A cluster of mushrooms: one at the base and the rest around it, each a stem leaning out a little under a flattened
/// cap, the larger caps on the taller stems, the stem and cap each their own part so a line parts them. A cap is a field
/// marched along the view rays, its underside (where it faces down) in the stems' colours as gills, with spots dotted
/// over its top where it faces the camera.
/// </summary>
[Serializable]
public sealed class PlantCaps
{
    private const float Sink = 0.02f;
    private const float Nearest = 0.45f;
    private const float StemTaper = 0.85f;
    private const float CapRise = 0.2f;
    private const float SpotFacing = 0.25f;
    private const float SpotLift = 0.02f;
    private const int SpotAttempts = 12;
    private const float GillFacing = -0.05f;
    private const float CapBounds = 1.15f;

    [Tooltip("How many mushrooms the cluster has: the fewest and most.")]
    [SerializeField] private Vector2Int count = new(2, 4);
    [Tooltip("How far from the base the others stand, in cells.")]
    [SerializeField, Min(0f)] private float spread = 0.17f;
    [Tooltip("A cap's radius, in cells: the smallest and largest.")]
    [SerializeField] private Vector2 capRadius = new(0.1f, 0.16f);
    [Tooltip("A cap's height, as a share of its radius.")]
    [SerializeField, Range(0.2f, 1.2f)] private float flatness = 0.55f;
    [Tooltip("How tall a stem stands under its cap, in cells: the shortest and tallest.")]
    [SerializeField] private Vector2 stemHeight = new(0.14f, 0.26f);
    [Tooltip("A stem's radius, as a share of its cap's.")]
    [SerializeField, Range(0.1f, 0.8f)] private float stemRadius = 0.32f;
    [Tooltip("How far a mushroom may lean out from the cluster's middle, in radians.")]
    [SerializeField, Range(0f, 1f)] private float lean = 0.3f;
    [Tooltip("How many spots each cap has.")]
    [SerializeField, Min(0)] private int spots;

    /// <summary>
    /// Draws the mushrooms, their caps in <paramref name="cap"/>, their stems and gills in <paramref name="stem"/> and
    /// their spots in <paramref name="spot"/>.
    /// </summary>
    public void Draw(PropDrawing drawing, int cap, int stem, int spot)
    {
        ArtCanvas canvas = drawing.Canvas;
        int mushrooms = drawing.Random.Next(Mathf.Max(1, count.x), Mathf.Max(count.x, count.y) + 1);
        float phase = drawing.Range(0f, 2f * Mathf.PI);
        for (int k = 0; k < mushrooms; k++)
        {
            float angle = phase + k * 2f * Mathf.PI / Mathf.Max(1, mushrooms - 1) + drawing.Range(-0.5f, 0.5f);
            Vector3 outward = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            Vector3 foot = k == 0 ? Vector3.zero : outward * spread * drawing.Range(Nearest, 1f);
            float size = (float)drawing.Random.NextDouble();
            float radius = Mathf.Lerp(capRadius.x, capRadius.y, size);
            float tall = Mathf.Lerp(stemHeight.x, stemHeight.y, size * 0.7f + drawing.Range(0f, 0.3f));
            float tilt = k == 0 ? drawing.Range(0f, lean * 0.3f) : drawing.Range(lean * 0.3f, lean);
            Vector3 up = (Vector3.forward * Mathf.Cos(tilt) + outward * Mathf.Sin(tilt)).normalized;
            Vector3 top = foot + up * tall;
            canvas.Capsule(foot - Vector3.forward * Sink, top, radius * stemRadius, radius * stemRadius * StemTaper, stem, drawing.NewPart());
            Vector3 e1 = Vector3.Cross(up, Vector3.up).normalized;
            Vector3 e2 = Vector3.Cross(up, e1).normalized;
            Vector3 middle = top + up * radius * flatness * CapRise;
            int part = drawing.NewPart();
            float height = radius * flatness;
            Func<Vector3, float> field = q =>
            {
                Vector3 d = q - middle;
                Vector3 local = new(Vector3.Dot(d, e1) / radius, Vector3.Dot(d, e2) / radius, Vector3.Dot(d, up) / height);
                return (local.magnitude - 1f) * height;
            };
            canvas.Field(field, new Bounds(middle, Vector3.one * radius * CapBounds * 2f), (position, surfaceNormal) => Vector3.Dot(surfaceNormal, up) < GillFacing ? stem : cap, part);
            for (int s = 0, attempt = 0; s < spots && attempt < spots * SpotAttempts; attempt++)
            {
                Vector3 d = drawing.OnSphere();
                Vector3 local = new(d.x, d.y, Mathf.Abs(d.z));
                Vector3 normal = (e1 * local.x + e2 * local.y + up * (local.z / flatness)).normalized;
                if (Vector3.Dot(normal, PropDrawing.ToCamera) < SpotFacing)
                {
                    continue;
                }

                canvas.Dot(middle + e1 * radius * local.x + e2 * radius * local.y + up * radius * flatness * local.z + PropDrawing.ToCamera * SpotLift, spot, part);
                s++;
            }
        }
    }
}
