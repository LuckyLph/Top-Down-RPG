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
