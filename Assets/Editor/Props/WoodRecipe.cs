using System;
using UnityEngine;

/// <summary>
/// The shapes a <see cref="WoodRecipe"/> draws.
/// </summary>
public enum WoodForm
{
    Stump,
    Log
}

/// <summary>
/// Dead wood on the ground, drawn as a field marched along the view rays. A stump: a short trunk whose foot flares into
/// the ground, its outline bulging in a few lobes and its broken top tilted and jagged; its cut end (where the surface
/// faces up) is wood with rings around the middle, the rest bark, with roots splayed over the ground
/// (<see cref="PropRoots"/>). A log: a fallen trunk lying sunk a little into the ground, turned from lying east and
/// west, bowed and tapering, its broken ends (where the surface faces along it) wood with rings around its axis, its
/// bark grooved along it, with a few branch stubs; a fallen log, or driftwood in bleached colours. Either can take a
/// cover where it faces up (<see cref="PropCover"/>: moss, snow, ash), and a stump embers smouldering in its bark and
/// cut end (<see cref="PropEmbers"/>, Docs/Lighting.md's Phase 6).
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Props/Wood Recipe", fileName = "Stump_Oak")]
public sealed class WoodRecipe : PropRecipe
{
    private const int Lobes = 5;
    private const float JagWaves = 1.6f;
    private const float BoundsReach = 1.6f;
    private const float BoundsBelow = 0.05f;
    private const float BoundsAbove = 0.12f;
    private const float StubSpan = 0.3f;
    private const float StubStart = 0.6f;
    private const float StubLean = 0.4f;
    private const float StubFoot = 0.4f;
    private const float StubTip = 0.28f;
    private const float KnotWaves = 7f;

    [Tooltip("A stump standing on its roots, or a log lying on the ground.")]
    [SerializeField] private WoodForm form = WoodForm.Stump;

    [Header("Stump (cells)")]
    [Tooltip("The stump's radius above its flare.")]
    [SerializeField, Min(0.05f)] private float radius = 0.27f;
    [Tooltip("How high its top stands above the ground.")]
    [SerializeField, Min(0.05f)] private float height = 0.42f;
    [Tooltip("How much wider its foot is, as a share of its radius.")]
    [SerializeField, Min(0f)] private float flare = 0.4f;
    [Tooltip("Over how high the flare narrows.")]
    [SerializeField, Min(0.01f)] private float flareHeight = 0.06f;
    [Tooltip("How far its broken top rises and falls around it.")]
    [SerializeField, Min(0f)] private float jag = 0.08f;
    [Tooltip("How far its top may tilt, in cells of height per cell east or west.")]
    [SerializeField, Min(0f)] private float slant = 0.1f;
    [Tooltip("How much its outline bulges in its lobes, as a share of its radius.")]
    [SerializeField, Range(0f, 0.3f)] private float wobble = 0.05f;
    [Tooltip("Its cut end is where the surface faces up past this.")]
    [SerializeField, Range(0f, 1f)] private float cutFacing = 0.72f;
    [SerializeField] private PropRoots roots = new(4, new Vector2(1.2f, 1.62f), new Vector2(0.33f, 0.12f), 0.72f);

    [Header("Log (cells)")]
    [Tooltip("The log's length.")]
    [SerializeField, Min(0.1f)] private float length = 1.6f;
    [Tooltip("The log's radius at its thick end.")]
    [SerializeField, Min(0.02f)] private float logRadius = 0.2f;
    [Tooltip("Its radius at its thin end, as a share of its thick end's.")]
    [SerializeField, Range(0.2f, 1f)] private float taper = 0.85f;
    [Tooltip("How far it may turn from lying east and west, in radians either way.")]
    [SerializeField, Range(0f, 1.5f)] private float turn = 0.9f;
    [Tooltip("How far its middle bows out sideways from a straight line, in cells: the least and most.")]
    [SerializeField] private Vector2 bend = new(0f, 0.06f);
    [Tooltip("How much of its radius lies under the ground.")]
    [SerializeField, Range(0f, 0.8f)] private float sink = 0.2f;
    [Tooltip("How much its girth swells and narrows along it, as a share of its radius.")]
    [SerializeField, Range(0f, 0.3f)] private float knots = 0.04f;
    [Tooltip("How far its broken ends reach in and out around them.")]
    [SerializeField, Min(0f)] private float endJag = 0.03f;
    [Tooltip("Its broken ends are where the surface faces along it past this.")]
    [SerializeField, Range(0f, 1f)] private float endFacing = 0.7f;
    [Tooltip("How many branch stubs stick out of it.")]
    [SerializeField, Min(0)] private int stubs = 1;
    [Tooltip("How far a stub sticks out past the bark: the shortest and longest.")]
    [SerializeField] private Vector2 stubLength = new(0.1f, 0.2f);

    [Header("Colours")]
    [SerializeField] private ColourRamp bark = Ramp(0x1e140e, 0x3e2a1c, 0x604230, 0x8a6644);
    [Tooltip("The bark's grooves per cell around the wood.")]
    [SerializeField, Min(1f)] private float barkGrooves = 16f;
    [SerializeField] private ColourRamp cutEnd = Ramp(0x3a2618, 0x9a7048, 0xc09464, 0xe0bc86);
    [Tooltip("The cut end's rings per cell from its middle.")]
    [SerializeField, Min(1f)] private float rings = 11f;

    [Header("Cover")]
    [SerializeField] private PropCover cover = new(false, Ramp(0x1c3412, 0x3e6a24, 0x5e8e30, 0x92b648), 0.85f, 0.5f, 6f);

    [Header("Embers")]
    [SerializeField] private PropEmbers embers = new(false, Ramp(0x3a0a04, 0x8a2208, 0xc8401a, 0xffa040));

    /// <inheritdoc />
    public override void Draw(PropDrawing drawing)
    {
        int coverId = cover.Add(drawing);
        if (form == WoodForm.Log)
        {
            DrawLog(drawing, coverId);
            return;
        }

        int barkId = drawing.AddMaterial(cover.Under(bark, coverId, ArtPattern.Bark, barkGrooves), true);
        int endId = drawing.AddMaterial(cover.Under(cutEnd, coverId, ArtPattern.Rings, rings), false);
        float tilt = drawing.Range(-slant, slant);
        float phase = drawing.Range(0f, 10f);
        Func<Vector3, float> field = q =>
        {
            float across = Mathf.Sqrt(q.x * q.x + q.y * q.y);
            float angle = Mathf.Atan2(q.y, q.x);
            float r = radius * (1f + flare * Mathf.Exp(-Mathf.Max(q.z, 0f) / flareHeight)) * (1f + wobble * Mathf.Sin(angle * Lobes + phase));
            float rim = height + jag * (ArtNoise.Value(new Vector3(angle * JagWaves + phase, 0.5f, 0.5f)) - 0.5f) * 2f + tilt * q.x;
            return Mathf.Max(across - r, q.z - rim);
        };
        float reach = radius * (1f + flare) * BoundsReach;
        Vector3 min = new(-reach, -reach, -BoundsBelow);
        Vector3 max = new(reach, reach, height + jag + BoundsAbove);
        int part = drawing.NewPart();
        drawing.Canvas.Field(field, new Bounds((min + max) * 0.5f, max - min), (position, surfaceNormal) => surfaceNormal.z > cutFacing ? endId : barkId, part);
        roots.Draw(drawing, radius, barkId, part);
        embers.Apply(drawing, embers.Add(drawing), new[] { barkId }, new[] { endId });
    }

    /// <summary>
    /// Draws a log lying across the base, its middle on it, with its stubs, in one part; its thick end lies either way.
    /// </summary>
    private void DrawLog(PropDrawing drawing, int coverId)
    {
        float angle = drawing.Range(-turn, turn);
        Vector3 along = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
        if (drawing.Random.NextDouble() < 0.5)
        {
            along = -along;
        }

        Vector3 side = new(-along.y, along.x, 0f);
        Vector3 centre = new(0f, 0f, logRadius * (1f - sink));
        float bow = drawing.Range(bend) * (drawing.Random.NextDouble() < 0.5 ? -1f : 1f);
        float phase = drawing.Range(0f, 10f);
        float half = length * 0.5f;
        int barkId = drawing.AddMaterial(cover.Under(bark, coverId, ArtPattern.Bark, barkGrooves, patternOrigin: centre, patternAxis: along), true);
        int endId = drawing.AddMaterial(cover.Under(cutEnd, coverId, ArtPattern.Rings, rings, patternOrigin: centre, patternAxis: along), true);
        Func<Vector3, float> field = q =>
        {
            Vector3 d = q - centre;
            float u = Vector3.Dot(d, along);
            float s = Vector3.Dot(d, side) - Bow(u, half, bow);
            float r = Girth(u, half, phase);
            float around = Mathf.Atan2(d.z, s);
            float end = half + endJag * (ArtNoise.Value(new Vector3(around * JagWaves + phase, u > 0f ? 0.5f : 7.5f, 0.5f)) - 0.5f) * 2f;
            return Mathf.Max(Mathf.Sqrt(s * s + d.z * d.z) - r, Mathf.Abs(u) - end);
        };
        float reachAlong = half + endJag;
        float reachAcross = logRadius * (1f + knots) + Mathf.Abs(bow);
        Vector3 extent = new(Mathf.Abs(along.x) * reachAlong + Mathf.Abs(side.x) * reachAcross, Mathf.Abs(along.y) * reachAlong + Mathf.Abs(side.y) * reachAcross, 0f);
        Vector3 min = new(-extent.x, -extent.y, -BoundsBelow);
        Vector3 max = new(extent.x, extent.y, centre.z + reachAcross + BoundsAbove);
        int part = drawing.NewPart();
        drawing.Canvas.Field(field, new Bounds((min + max) * 0.5f, max - min), (position, surfaceNormal) => Mathf.Abs(Vector3.Dot(surfaceNormal, along)) > endFacing ? endId : barkId, part);
        for (int k = 0; k < stubs; k++)
        {
            float u = drawing.Range(-StubSpan, StubSpan) * length;
            float lift = drawing.Range(0f, Mathf.PI);
            Vector3 direction = (side * Mathf.Cos(lift) + Vector3.forward * Mathf.Sin(lift) + along * drawing.Range(-StubLean, StubLean)).normalized;
            float r = Girth(u, half, phase);
            Vector3 foot = centre + along * u + side * Bow(u, half, bow) + direction * r * StubStart;
            Vector3 tip = foot + direction * (r * (1f - StubStart) + drawing.Range(stubLength));
            drawing.Canvas.Capsule(foot, tip, r * StubFoot, r * StubTip, barkId, part);
        }
    }

    /// <summary>
    /// How far the log's middle line lies sideways at <paramref name="u"/> along it, bowed by <paramref name="bow"/> in
    /// the middle and straight at its ends <paramref name="half"/> from it.
    /// </summary>
    private static float Bow(float u, float half, float bow)
    {
        float t = u / half;
        return bow * (1f - t * t);
    }

    /// <summary>
    /// The log's radius at <paramref name="u"/> along it: tapering from its thick end to its thin one, swelling and
    /// narrowing by its knots.
    /// </summary>
    private float Girth(float u, float half, float phase)
    {
        float t = Mathf.Clamp01((u + half) / (2f * half));
        return logRadius * Mathf.Lerp(1f, taper, t) * (1f + knots * Mathf.Sin(u * KnotWaves + phase));
    }
}
