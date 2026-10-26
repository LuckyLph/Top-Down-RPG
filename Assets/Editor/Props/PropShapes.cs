using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One leaf clump a <see cref="PropCrown"/> drew: its middle, its radius and its part, so flowers can be set on it.
/// </summary>
public readonly struct PropClump
{
    /// <summary>
    /// Creates the clump of <paramref name="radius"/> around <paramref name="centre"/> drawn as <paramref name="part"/>.
    /// </summary>
    public PropClump(Vector3 centre, float radius, int part)
    {
        Centre = centre;
        Radius = radius;
        Part = part;
    }

    public Vector3 Centre { get; }
    public float Radius { get; }
    public int Part { get; }
}

/// <summary>
/// A crown of leaf clumps, shared by trees and bushes (Docs/PropGeneration.md, Phase 0 notes): a core ellipsoid one
/// tone darker that shows between the clumps, and clumps of three sizes set on its surface where it faces the camera,
/// each its own part so inner lines scallop the crown, their normals leaning toward the whole crown's so it is lit as
/// one mass. Clumps stretched taller than wide hang, as a willow's.
/// </summary>
[Serializable]
public sealed class PropCrown
{
    private const float CoreInset = 0.8f;
    private const float CoreShrink = 0.95f;
    private const float CoreBlend = 0.8f;
    private const float HiddenFacing = -0.3f;
    private const float SizeJitter = 0.08f;
    private const int Attempts = 30;

    [Tooltip("How many leaf clumps the crown has.")]
    [SerializeField, Min(1)] private int clumps = 26;
    [Tooltip("The smallest and largest clump radius, in cells; the third size lies halfway.")]
    [SerializeField] private Vector2 clumpRadius = new(0.28f, 0.5f);
    [Tooltip("How far each clump's normal leans toward the whole crown's.")]
    [SerializeField, Range(0f, 1f)] private float blend = 0.45f;
    [Tooltip("How far from the core's middle the clumps sit, as a share of its radius: the nearest and farthest.")]
    [SerializeField] private Vector2 shell = new(0.88f, 1.02f);
    [Tooltip("How tall each clump is, as a share of its width: above 1 the clumps hang, as a willow's.")]
    [SerializeField, Min(0.2f)] private float stretch = 1f;

    /// <summary>
    /// Creates a crown of the clumps a tree has, for Unity serialization.
    /// </summary>
    public PropCrown()
    {
    }

    /// <summary>
    /// Creates a crown of <paramref name="clumpCount"/> clumps of <paramref name="radius"/>, leaning by
    /// <paramref name="leanBlend"/>, set at <paramref name="shellRange"/> of the core's radius.
    /// </summary>
    public PropCrown(int clumpCount, Vector2 radius, float leanBlend, Vector2 shellRange)
    {
        clumps = clumpCount;
        clumpRadius = radius;
        blend = leanBlend;
        shell = shellRange;
    }

    /// <summary>
    /// Draws the crown around <paramref name="centre"/> inside the envelope of <paramref name="radii"/>, its clumps in
    /// <paramref name="leaves"/> and its core in <paramref name="core"/>, adding each clump to <paramref name="placed"/>
    /// when given.
    /// </summary>
    public void Draw(PropDrawing drawing, Vector3 centre, Vector3 radii, int leaves, int core, List<PropClump> placed = null)
    {
        ArtEnvelope envelope = new(centre, radii);
        float mean = (clumpRadius.x + clumpRadius.y) * 0.5f;
        float inset = mean * CoreInset;
        Vector3 inner = new(Mathf.Max(0.1f, radii.x - inset), Mathf.Max(0.1f, radii.y - inset), Mathf.Max(0.1f, radii.z - inset));
        drawing.Canvas.Ellipsoid(centre, new Vector3(inner.x * CoreShrink, 0f, 0f), new Vector3(0f, inner.y * CoreShrink, 0f), new Vector3(0f, 0f, inner.z * CoreShrink), core, drawing.NewPart(), envelope, CoreBlend);
        float[] sizes = { clumpRadius.x, mean, clumpRadius.y };
        int count = 0;
        for (int attempt = 0; count < clumps && attempt < clumps * Attempts; attempt++)
        {
            Vector3 direction = drawing.OnSphere();
            if (Vector3.Dot(direction, PropDrawing.ToCamera) < HiddenFacing)
            {
                continue;
            }

            float radius = sizes[drawing.Random.Next(sizes.Length)] * drawing.Range(1f - SizeJitter, 1f + SizeJitter);
            Vector3 at = centre + Vector3.Scale(direction, inner) * drawing.Range(shell);
            int part = drawing.NewPart();
            if (stretch == 1f)
            {
                drawing.Canvas.Sphere(at, radius, leaves, part, 0f, envelope, blend);
            }
            else
            {
                drawing.Canvas.Ellipsoid(at, new Vector3(radius, 0f, 0f), new Vector3(0f, radius, 0f), new Vector3(0f, 0f, radius * stretch), leaves, part, envelope, blend);
            }

            placed?.Add(new PropClump(at, radius, part));
            count++;
        }
    }
}

/// <summary>
/// Roots splayed over the ground around a trunk's foot, shared by trees and stumps, in the trunk's part so they are not
/// lined against it.
/// </summary>
[Serializable]
public sealed class PropRoots
{
    private const float Sink = 0.02f;
    private const float Start = 0.3f;
    private const float AngleJitter = 0.35f;

    [Tooltip("How many roots there are.")]
    [SerializeField, Min(0)] private int count = 4;
    [Tooltip("How far a root reaches from the middle, in trunk radii: the shortest and longest.")]
    [SerializeField] private Vector2 reach = new(2f, 2.7f);
    [Tooltip("A root's radius where it leaves the trunk and at its tip, in trunk radii.")]
    [SerializeField] private Vector2 thickness = new(0.55f, 0.2f);
    [Tooltip("How high on the trunk the roots leave it, in trunk radii.")]
    [SerializeField, Min(0f)] private float rise = 1.2f;

    /// <summary>
    /// Creates a tree's roots, for Unity serialization.
    /// </summary>
    public PropRoots()
    {
    }

    /// <summary>
    /// Creates <paramref name="rootCount"/> roots reaching <paramref name="reachRange"/> trunk radii, of
    /// <paramref name="radius"/> at their start and tip, leaving the trunk <paramref name="height"/> radii up.
    /// </summary>
    public PropRoots(int rootCount, Vector2 reachRange, Vector2 radius, float height)
    {
        count = rootCount;
        reach = reachRange;
        thickness = radius;
        rise = height;
    }

    /// <summary>
    /// Draws the roots of a trunk of <paramref name="trunkRadius"/> standing at the origin, in
    /// <paramref name="material"/> and <paramref name="part"/>, spread evenly from a random first angle.
    /// </summary>
    public void Draw(PropDrawing drawing, float trunkRadius, int material, int part)
    {
        float phase = drawing.Range(0f, 2f * Mathf.PI);
        for (int k = 0; k < count; k++)
        {
            float angle = phase + k * 2f * Mathf.PI / count + drawing.Range(-AngleJitter, AngleJitter);
            Vector3 direction = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            Vector3 from = direction * trunkRadius * Start + new Vector3(0f, 0f, trunkRadius * rise);
            Vector3 to = direction * trunkRadius * drawing.Range(reach) + new Vector3(0f, 0f, -Sink);
            drawing.Canvas.Capsule(from, to, trunkRadius * thickness.x, trunkRadius * thickness.y, material, part);
        }
    }
}

/// <summary>
/// The colours of one kind of flower: its petals and its heart.
/// </summary>
[Serializable]
public struct PropFlowerColour
{
    [SerializeField] private Color32 petal;
    [SerializeField] private Color32 heart;

    /// <summary>
    /// Creates a flower of <paramref name="petalColour"/> petals around a <paramref name="heartColour"/> heart.
    /// </summary>
    public PropFlowerColour(Color32 petalColour, Color32 heartColour)
    {
        petal = petalColour;
        heart = heartColour;
    }

    public Color32 Petal => petal;
    public Color32 Heart => heart;
}

/// <summary>
/// Flowers dotted over a crown's clumps, as the meadow's are drawn on the ground: a heart pixel with a petal on each
/// side, in flat colours, set where a clump faces the camera and shows, all of one colour a variant picks (the meadow's
/// four by default). They are lined as the leaves under them are, in the leaves' outline, so they change no line.
/// </summary>
[Serializable]
public sealed class PropFlowers
{
    private const float Lift = 0.6f;
    private const float Jitter = 0.5f;
    private const int Attempts = 20;
    private static readonly Vector2Int[] Petals = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

    [Tooltip("How many flowers dot the crown.")]
    [SerializeField, Min(0)] private int count;
    [Tooltip("The colours each variant picks one from, for all its flowers.")]
    [SerializeField] private PropFlowerColour[] colours =
    {
        new(new Color32(236, 234, 214, 255), new Color32(232, 190, 64, 255)),
        new(new Color32(240, 206, 74, 255), new Color32(196, 128, 40, 255)),
        new(new Color32(226, 138, 176, 255), new Color32(246, 212, 226, 255)),
        new(new Color32(140, 128, 222, 255), new Color32(236, 224, 150, 255))
    };

    /// <summary>
    /// Sets the flowers on <paramref name="clumps"/>, each on a pixel its clump shows, lined in the leaves'
    /// <paramref name="outline"/>.
    /// </summary>
    public void Draw(PropDrawing drawing, IReadOnlyList<PropClump> clumps, Color32 outline)
    {
        if (count == 0 || colours.Length == 0 || clumps.Count == 0)
        {
            return;
        }

        PropFlowerColour colour = colours[drawing.Random.Next(colours.Length)];
        int petal = drawing.AddMaterial(new ArtMaterial(Flat(colour.Petal, outline), flat: true), false);
        int heart = drawing.AddMaterial(new ArtMaterial(Flat(colour.Heart, outline), flat: true), false);
        ArtCanvas canvas = drawing.Canvas;
        int placed = 0;
        for (int attempt = 0; placed < count && attempt < count * Attempts; attempt++)
        {
            PropClump clump = clumps[drawing.Random.Next(clumps.Count)];
            Vector3 direction = (PropDrawing.ToCamera + Vector3.forward * Lift + drawing.OnSphere() * Jitter).normalized;
            Vector2Int at = canvas.PixelOf(clump.Centre + direction * clump.Radius);
            if (!Shows(canvas, at, clump.Part, petal, heart))
            {
                continue;
            }

            canvas.SetMaterialId(at.x, at.y, heart);
            foreach (Vector2Int step in Petals)
            {
                Vector2Int next = at + step;
                if (Shows(canvas, next, clump.Part, petal, heart))
                {
                    canvas.SetMaterialId(next.x, next.y, petal);
                }
            }

            placed++;
        }
    }

    /// <summary>
    /// Whether <paramref name="pixel"/> shows the clump drawn as <paramref name="part"/> and holds no flower yet.
    /// </summary>
    private static bool Shows(ArtCanvas canvas, Vector2Int pixel, int part, int petal, int heart)
    {
        if (!canvas.IsFilled(pixel.x, pixel.y) || canvas.PartIdAt(pixel.x, pixel.y) != part)
        {
            return false;
        }

        int material = canvas.MaterialIdAt(pixel.x, pixel.y);
        return material != petal && material != heart;
    }

    /// <summary>
    /// A ramp of one colour, lined in <paramref name="outline"/>.
    /// </summary>
    private static ColourRamp Flat(Color32 colour, Color32 outline)
    {
        return new ColourRamp(outline, colour, colour, colour);
    }
}

/// <summary>
/// Bare twigs poking up out of a crown, as a dry bush's: thin tapering capsules with no line, from inside the crown out
/// past its edge, some forking once.
/// </summary>
[Serializable]
public sealed class PropTwigs
{
    private const float Inside = 0.55f;
    private const float Upward = 0.35f;
    private const float ForkChance = 0.5f;
    private const float ForkAt = 0.55f;
    private const float ForkTurn = 0.6f;
    private const float ForkLength = 0.5f;
    private const float MinPixels = 0.71f;

    [Tooltip("How many twigs poke out of the crown.")]
    [SerializeField, Min(0)] private int count;
    [Tooltip("How far past the crown's edge a twig reaches, in cells: the shortest and longest.")]
    [SerializeField] private Vector2 reach = new(0.08f, 0.2f);
    [Tooltip("A twig's radius at its foot and at its tip, in cells.")]
    [SerializeField] private Vector2 thickness = new(0.025f, 0.01f);

    public int Count => count;

    /// <summary>
    /// Draws the twigs of a crown around <paramref name="centre"/> inside the envelope of <paramref name="radii"/>, in
    /// <paramref name="material"/>, each pointing up out of it.
    /// </summary>
    public void Draw(PropDrawing drawing, Vector3 centre, Vector3 radii, int material)
    {
        ArtCanvas canvas = drawing.Canvas;
        for (int k = 0; k < count; k++)
        {
            Vector3 direction = drawing.OnSphere();
            direction.z = Mathf.Abs(direction.z) * (1f - Upward) + Upward;
            direction.Normalize();
            Vector3 edge = centre + Vector3.Scale(direction, radii);
            Vector3 from = centre + Vector3.Scale(direction, radii) * Inside;
            float length = drawing.Range(reach);
            Vector3 to = edge + direction * length;
            int part = drawing.NewPart();
            canvas.Capsule(from, to, thickness.x, thickness.y, material, part, minRadius: MinPixels);
            if (drawing.Random.NextDouble() >= ForkChance)
            {
                continue;
            }

            Vector3 fork = Vector3.Lerp(from, to, drawing.Range(ForkAt, 1f));
            Vector3 aside = (direction + drawing.OnSphere() * ForkTurn).normalized;
            canvas.Capsule(fork, fork + aside * length * ForkLength, thickness.y * 1.5f, thickness.y, material, part, minRadius: MinPixels);
        }
    }
}

/// <summary>
/// Strands of leaves hanging from a crown, as a willow's: thin curves with no line from the crown's surface facing the
/// camera, bulging out a little and falling to just above the ground, lit by how high they hang.
/// </summary>
[Serializable]
public sealed class PropStrands
{
    private const float Surface = 0.95f;
    private const float HiddenFacing = -0.1f;
    private const float Bulge = 0.12f;
    private const int Segments = 6;
    private const float MinPixels = 0.71f;

    [Tooltip("How many strands hang from the crown.")]
    [SerializeField, Min(0)] private int count;
    [Tooltip("Where on the crown a strand starts, from its bottom (-1) to its top (1): the lowest and highest.")]
    [SerializeField] private Vector2 start = new(-0.6f, 0.5f);
    [Tooltip("How high above the ground a strand ends, in cells: the lowest and highest.")]
    [SerializeField] private Vector2 end = new(0.02f, 0.2f);
    [Tooltip("A strand's radius at its top and at its end, in cells.")]
    [SerializeField] private Vector2 thickness = new(0.025f, 0.012f);

    public int Count => count;

    /// <summary>
    /// Draws the strands of a crown around <paramref name="centre"/> inside the envelope of <paramref name="radii"/>,
    /// in <paramref name="material"/>.
    /// </summary>
    public void Draw(PropDrawing drawing, Vector3 centre, Vector3 radii, int material)
    {
        ArtCanvas canvas = drawing.Canvas;
        for (int k = 0, attempt = 0; k < count && attempt < count * 20; attempt++)
        {
            float height = drawing.Range(start);
            float angle = drawing.Range(0f, 2f * Mathf.PI);
            float around = Mathf.Sqrt(Mathf.Max(0f, 1f - height * height));
            Vector3 direction = new(Mathf.Cos(angle) * around, Mathf.Sin(angle) * around, height);
            if (Vector3.Dot(direction, PropDrawing.ToCamera) < HiddenFacing)
            {
                continue;
            }

            Vector3 a = centre + Vector3.Scale(direction, radii) * Surface;
            Vector3 outward = new Vector3(direction.x, direction.y, 0f).normalized;
            Vector3 c = new(a.x + outward.x * Bulge * 0.5f, a.y + outward.y * Bulge * 0.5f, drawing.Range(end));
            Vector3 b = new(a.x + outward.x * Bulge, a.y + outward.y * Bulge, Mathf.Lerp(a.z, c.z, 0.3f));
            PlantRecipe.Stroke(canvas, a, b, c, 0f, 1f, thickness.x, thickness.y, Segments, material, drawing.NewPart(), MinPixels);
            k++;
        }
    }
}

/// <summary>
/// A cover a prop's surfaces take where they face up (Docs/PropGeneration.md, The canvas): moss, snow or ash. Whether it
/// lies on the prop, its colours, the facing past which it lies and the value noise breaking up its edge. A recipe adds
/// it to its drawing's materials and builds the materials that take it through <see cref="Under"/>.
/// </summary>
[Serializable]
public sealed class PropCover
{
    [Tooltip("Whether the cover lies on the prop where it faces up.")]
    [SerializeField] private bool enabled;
    [SerializeField] private ColourRamp colours;
    [Tooltip("The cover lies where the surface faces up past this.")]
    [SerializeField, Range(-1f, 1f)] private float facing = 0.8f;
    [Tooltip("How much noise breaks up the cover's edge: the noise's full range.")]
    [SerializeField, Range(0f, 2f)] private float noise = 0.4f;
    [Tooltip("The cover noise's cycles per cell.")]
    [SerializeField, Min(0.1f)] private float noiseScale = 6f;

    /// <summary>
    /// Creates no cover, for Unity serialization.
    /// </summary>
    public PropCover()
    {
    }

    /// <summary>
    /// Creates a cover of <paramref name="ramp"/>, lying on the prop when <paramref name="lies"/>, past
    /// <paramref name="facingPast"/> with noise of <paramref name="noiseRange"/> at <paramref name="cycles"/> a cell.
    /// </summary>
    public PropCover(bool lies, ColourRamp ramp, float facingPast, float noiseRange, float cycles)
    {
        enabled = lies;
        colours = ramp;
        facing = facingPast;
        noise = noiseRange;
        noiseScale = cycles;
    }

    /// <summary>
    /// Adds the cover's material to <paramref name="drawing"/> when it lies on the prop and returns its id, or -1.
    /// </summary>
    public int Add(PropDrawing drawing)
    {
        return enabled ? drawing.AddMaterial(new ArtMaterial(colours), false) : -1;
    }

    /// <summary>
    /// A material in <paramref name="ramp"/> that takes the cover <paramref name="coverId"/> (none, for -1), with its
    /// pattern, outlined or not, its pattern in the frame of <paramref name="patternAxis"/> through
    /// <paramref name="patternOrigin"/> (the world's, for zero), and its tone offset.
    /// </summary>
    public ArtMaterial Under(ColourRamp ramp, int coverId, ArtPattern pattern = ArtPattern.None, float patternScale = 1f, bool outlined = true,
        Vector3 patternOrigin = default, Vector3 patternAxis = default, int toneOffset = 0)
    {
        return new ArtMaterial(ramp, outlined: outlined, pattern: pattern, patternScale: patternScale, toneOffset: toneOffset, cover: coverId, coverThreshold: facing,
            coverNoise: noise, coverNoiseScale: noiseScale, patternOrigin: patternOrigin, patternAxis: patternAxis);
    }
}

/// <summary>
/// A pine's crown (Docs/PropGeneration.md, Phase 0 notes): tiers hanging under their apexes around the world's up axis,
/// each a field marched along the view rays whose radius below its apex grows as a power of its depth below it (flat on
/// top, steep at the edge), its lower edge cut in teeth whose tips hang lower and reach further out. The radii shrink
/// from the lowest tier to the top one, which is pointier, and the tiers' bottoms are spaced evenly from just above the
/// trunk to under the top tier; each tier its own part.
/// </summary>
[Serializable]
public sealed class PropPine
{
    private const float FieldStep = 0.5f;
    private const float BoundsMargin = 1.15f;
    private const float BoundsAbove = 0.1f;

    [Tooltip("How many tiers the pine has.")]
    [SerializeField, Min(1)] private int tiers = 5;
    [Tooltip("How high the lowest tier's bottom lies above the ground, in cells.")]
    [SerializeField, Min(0f)] private float clearance = 0.3f;
    [Tooltip("The top tier's radius, as a share of the lowest's.")]
    [SerializeField, Range(0.1f, 1f)] private float topShare = 0.32f;
    [Tooltip("How tall each tier is, in its radii.")]
    [SerializeField, Min(0.2f)] private float tierHeight = 1.25f;
    [Tooltip("How much taller the top tier is, as a share, so it comes to a point.")]
    [SerializeField, Min(1f)] private float point = 1.6f;
    [Tooltip("How a tier's radius grows below its apex, as a power of its depth: lower is flatter on top and steeper at the edge.")]
    [SerializeField, Range(0.2f, 1f)] private float profile = 0.6f;
    [Tooltip("How many teeth cut a tier's lower edge: the fewest and most.")]
    [SerializeField] private Vector2Int teeth = new(7, 9);
    [Tooltip("How far a tooth's tip hangs below its tier's edge, in cells.")]
    [SerializeField, Min(0f)] private float drop = 0.14f;
    [Tooltip("How much further out a tooth's tip reaches, as a share of its tier's radius.")]
    [SerializeField, Range(0f, 0.5f)] private float reach = 0.12f;

    public float Clearance => clearance;

    /// <summary>
    /// Draws the tiers of a pine whose top stands <paramref name="height"/> high on screen and whose lowest tier is
    /// <paramref name="width"/> across, in <paramref name="needles"/>.
    /// </summary>
    public void Draw(PropDrawing drawing, float height, float width, int needles)
    {
        float bottomRadius = width * 0.5f;
        float topRadius = bottomRadius * topShare;
        float topBottom = height / ArtCanvas.CosPitch - topRadius * tierHeight * point;
        for (int i = 0; i < tiers; i++)
        {
            float t = tiers == 1 ? 1f : i / (float)(tiers - 1);
            float radius = Mathf.Lerp(bottomRadius, topRadius, t);
            float tall = radius * tierHeight * (i == tiers - 1 ? point : 1f);
            float bottom = Mathf.Lerp(clearance, topBottom, t);
            int count = drawing.Random.Next(teeth.x, teeth.y + 1);
            float phase = drawing.Range(0f, 1f);
            Tier(drawing, bottom + tall, tall, radius, count, phase, needles);
        }
    }

    /// <summary>
    /// Draws one tier with its apex <paramref name="apex"/> high, <paramref name="tall"/> deep to its edge,
    /// <paramref name="radius"/> across at its edge and <paramref name="count"/> teeth turned by
    /// <paramref name="phase"/>, as its own part.
    /// </summary>
    private void Tier(PropDrawing drawing, float apex, float tall, float radius, int count, float phase, int needles)
    {
        Func<Vector3, float> field = q =>
        {
            float across = Mathf.Sqrt(q.x * q.x + q.y * q.y);
            float below = apex - q.z;
            float around = Mathf.Atan2(q.y, q.x) / (2f * Mathf.PI) * count + phase;
            float tooth = 1f - Mathf.Abs(2f * (around - Mathf.Floor(around)) - 1f);
            float hang = tooth * drop;
            float r = below <= tall
                ? radius * Mathf.Pow(Mathf.Max(below, 0f) / tall, profile)
                : radius * (1f + reach * tooth * Mathf.Min(1f, (below - tall) / Mathf.Max(hang, 1e-4f)));
            return Mathf.Max(across - r, Mathf.Max(-below, below - tall - hang));
        };
        float across = radius * (1f + reach) * BoundsMargin;
        float low = apex - tall - drop - BoundsAbove;
        float high = apex + BoundsAbove;
        drawing.Canvas.Field(field, new Bounds(new Vector3(0f, 0f, (low + high) * 0.5f), new Vector3(across * 2f, across * 2f, high - low)), (position, surfaceNormal) => needles, drawing.NewPart(), FieldStep);
    }
}

/// <summary>
/// Bare branching, as a dead or charred tree's (Docs/PropGeneration.md, Phase 0 notes): limbs as chains of tapering
/// capsules in short segments, each segment turned by a random jitter and pulled back toward its limb's aim. The trunk
/// is a share of the tree's height and leans within a limit of up; each limb forks at its end into two, sometimes
/// three, until its depth runs out, and grows side branches along its upper part. Each limb is its own part, lined
/// while thick and unlined once thinner than a twig.
/// </summary>
[Serializable]
public sealed class PropLimbs
{
    private const float ForkSpread = 20f;
    private const float ForkThickness = 0.85f;
    private const float SideAngleMin = 30f;
    private const float SideAngleMax = 60f;
    private const float SideLength = 0.5f;
    private const float SideThickness = 0.6f;
    private const float SideStart = 0.4f;
    private const float MinPixels = 0.71f;

    [Tooltip("How long the trunk is, as a share of the tree's height.")]
    [SerializeField, Range(0.1f, 1f)] private float trunkShare = 0.42f;
    [Tooltip("How far the trunk may lean from up, in degrees.")]
    [SerializeField, Range(0f, 60f)] private float lean = 20f;
    [Tooltip("How long each segment of a limb is, in cells.")]
    [SerializeField, Min(0.02f)] private float segment = 0.16f;
    [Tooltip("How far each segment turns at random.")]
    [SerializeField, Range(0f, 1f)] private float jitter = 0.3f;
    [Tooltip("How far each segment is pulled back toward its limb's aim.")]
    [SerializeField, Range(0f, 1f)] private float pull = 0.35f;
    [Tooltip("How many times the limbs fork, from the trunk out.")]
    [SerializeField, Range(0, 5)] private int depth = 3;
    [Tooltip("The chance a limb forks into three rather than two.")]
    [SerializeField, Range(0f, 1f)] private float threeChance = 0.3f;
    [Tooltip("The angle between a fork and its limb, in degrees: the least and most.")]
    [SerializeField] private Vector2 forkAngle = new(22f, 42f);
    [Tooltip("A fork's length, as a share of its limb's: the least and most.")]
    [SerializeField] private Vector2 forkLength = new(0.54f, 0.72f);
    [Tooltip("The chance each segment grows a side branch.")]
    [SerializeField, Range(0f, 1f)] private float sideChance = 0.22f;
    [Tooltip("A limb's radius at its tip, as a share of its foot's.")]
    [SerializeField, Range(0.1f, 1f)] private float taper = 0.6f;
    [Tooltip("Limbs thinner than this many pixels in radius are unlined, as twigs are.")]
    [SerializeField, Min(0f)] private float twigPixels = 1.1f;

    /// <summary>
    /// Draws the trunk and its limbs, the trunk <paramref name="trunkRadius"/> at its foot, of a tree
    /// <paramref name="height"/> tall, in <paramref name="lined"/> and in <paramref name="twig"/> once thin; returns the
    /// trunk's part, for its roots.
    /// </summary>
    public int Draw(PropDrawing drawing, float height, float trunkRadius, int lined, int twig)
    {
        Vector3 tiltAxis = new(drawing.Range(-1f, 1f), drawing.Range(-1f, 1f), 0f);
        Vector3 aim = Quaternion.AngleAxis(drawing.Range(0f, lean), tiltAxis.sqrMagnitude > 1e-4f ? tiltAxis.normalized : Vector3.right) * Vector3.forward;
        return Limb(drawing, Vector3.zero, aim, height * trunkShare, trunkRadius, depth, lined, twig);
    }

    /// <summary>
    /// Draws a limb from <paramref name="from"/> toward <paramref name="aim"/>, <paramref name="length"/> long and
    /// <paramref name="radius"/> at its foot, with its side branches and, while <paramref name="level"/> is above
    /// zero, its forks; returns its part.
    /// </summary>
    private int Limb(PropDrawing drawing, Vector3 from, Vector3 aim, float length, float radius, int level, int lined, int twig)
    {
        ArtCanvas canvas = drawing.Canvas;
        int part = drawing.NewPart();
        int segments = Mathf.Max(1, Mathf.RoundToInt(length / segment));
        float tip = radius * taper;
        Vector3 direction = aim;
        Vector3 at = from;
        for (int s = 0; s < segments; s++)
        {
            direction = (direction + drawing.OnSphere() * jitter).normalized;
            direction = Vector3.Lerp(direction, aim, pull).normalized;
            Vector3 next = at + direction * (length / segments);
            float r0 = Mathf.Lerp(radius, tip, s / (float)segments);
            float r1 = Mathf.Lerp(radius, tip, (s + 1) / (float)segments);
            bool thin = r1 * canvas.Scale < twigPixels;
            canvas.Capsule(at, next, r0, r1, thin ? twig : lined, part, minRadius: thin ? MinPixels : 0f);
            if (level > 0 && s >= segments * SideStart && drawing.Random.NextDouble() < sideChance)
            {
                Vector3 side = Quaternion.AngleAxis(drawing.Range(SideAngleMin, SideAngleMax), Across(drawing, direction)) * direction;
                Limb(drawing, next, side, length * (1f - (s + 1) / (float)segments) * SideLength + segment, r1 * SideThickness, level - 1, lined, twig);
            }

            at = next;
        }

        if (level <= 0)
        {
            return part;
        }

        int forks = drawing.Random.NextDouble() < threeChance ? 3 : 2;
        Vector3 across = Across(drawing, direction);
        for (int k = 0; k < forks; k++)
        {
            Vector3 axis = Quaternion.AngleAxis(k * 360f / forks + drawing.Range(-ForkSpread, ForkSpread), direction) * across;
            Vector3 fork = Quaternion.AngleAxis(drawing.Range(forkAngle), axis) * direction;
            Limb(drawing, at, fork, length * drawing.Range(forkLength), tip * ForkThickness, level - 1, lined, twig);
        }

        return part;
    }

    /// <summary>
    /// A direction square to <paramref name="direction"/>, picked at random.
    /// </summary>
    private static Vector3 Across(PropDrawing drawing, Vector3 direction)
    {
        while (true)
        {
            Vector3 across = Vector3.Cross(direction, drawing.OnSphere());
            if (across.sqrMagnitude > 1e-4f)
            {
                return across.normalized;
            }
        }
    }
}
