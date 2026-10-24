using System;
using UnityEngine;

/// <summary>
/// A crown of leaf clumps, shared by trees and bushes (Docs/PropGeneration.md, Phase 0 notes): a core ellipsoid one
/// tone darker that shows between the clumps, and clumps of three sizes set on its surface where it faces the camera,
/// each its own part so inner lines scallop the crown, their normals leaning toward the whole crown's so it is lit as
/// one mass.
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
    /// <paramref name="leaves"/> and its core in <paramref name="core"/>.
    /// </summary>
    public void Draw(PropDrawing drawing, Vector3 centre, Vector3 radii, int leaves, int core)
    {
        ArtEnvelope envelope = new(centre, radii);
        float mean = (clumpRadius.x + clumpRadius.y) * 0.5f;
        float inset = mean * CoreInset;
        Vector3 inner = new(Mathf.Max(0.1f, radii.x - inset), Mathf.Max(0.1f, radii.y - inset), Mathf.Max(0.1f, radii.z - inset));
        drawing.Canvas.Ellipsoid(centre, new Vector3(inner.x * CoreShrink, 0f, 0f), new Vector3(0f, inner.y * CoreShrink, 0f), new Vector3(0f, 0f, inner.z * CoreShrink), core, drawing.NewPart(), envelope, CoreBlend);
        float[] sizes = { clumpRadius.x, mean, clumpRadius.y };
        int placed = 0;
        for (int attempt = 0; placed < clumps && attempt < clumps * Attempts; attempt++)
        {
            Vector3 direction = drawing.OnSphere();
            if (Vector3.Dot(direction, PropDrawing.ToCamera) < HiddenFacing)
            {
                continue;
            }

            float radius = sizes[drawing.Random.Next(sizes.Length)] * drawing.Range(1f - SizeJitter, 1f + SizeJitter);
            Vector3 at = centre + Vector3.Scale(direction, inner) * drawing.Range(shell);
            drawing.Canvas.Sphere(at, radius, leaves, drawing.NewPart(), 0f, envelope, blend);
            placed++;
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
