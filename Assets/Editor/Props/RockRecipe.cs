using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A faceted rock (Docs/PropGeneration.md, Phase 0 notes): a body and a few smaller lumps leaning on its sides, each a
/// convex solid inside an octagon around its ellipsoid, cut by random planes leaning up and flattened on top, sunk
/// partly into the ground, in speckled stone with an optional moss cover where it faces up. Its width counts its lumps,
/// so rocks and boulders are this one kind; scattered, its lumps are small stones lying apart around a middle one.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Props/Rock Recipe", fileName = "Rock_Grey")]
public sealed class RockRecipe : PropRecipe
{
    private const float BodyShare = 0.42f;
    private static readonly Vector2 LumpSpread = new(0.22f, 0.3f);
    private static readonly Vector2 LumpWidth = new(0.15f, 0.22f);
    private static readonly Vector2 LumpDepth = new(0.14f, 0.2f);
    private static readonly Vector2 LumpHeight = new(0.4f, 0.65f);
    private static readonly Vector2 LumpDrift = new(-0.1f, 0.05f);
    private const float LumpDepthSide = 0.6f;
    private const float LumpTurn = 0.6f;
    private const float FacetUp = 0.9f;
    private const float FacetDown = 0.15f;
    private const float BottomDepth = 0.6f;
    private const float BoundsMargin = 1.2f;
    private const int BoundingSides = 8;
    private const float MiddleStone = 1.2f;
    private const float ScatterJitter = 0.4f;

    [Header("Size (cells)")]
    [Tooltip("The rock's width, its lumps included.")]
    [SerializeField, Min(0.1f)] private float width = 1.1f;
    [Tooltip("How high its top stands above the ground.")]
    [SerializeField, Min(0.1f)] private float height = 0.7f;
    [Tooltip("Its body's depth, as a share of its width: the least and most.")]
    [SerializeField] private Vector2 depth = new(0.7f, 0.9f);
    [Tooltip("How much of each lump lies under the ground, as a share of its height.")]
    [SerializeField, Range(0f, 0.45f)] private float sink = 0.3f;

    [Header("Facets")]
    [Tooltip("How many planes cut each lump's faces.")]
    [SerializeField, Min(4)] private int facets = 14;
    [Tooltip("Where each plane cuts, as a share of the lump's reach along its normal: the deepest and shallowest.")]
    [SerializeField] private Vector2 cut = new(0.88f, 1f);
    [Tooltip("Where each lump's flattened top lies, as a share of its height radius above its middle: the lowest and highest.")]
    [SerializeField] private Vector2 top = new(0.86f, 0.98f);
    [Tooltip("How many smaller lumps lean on its sides, or lie around it when scattered.")]
    [SerializeField, Min(0)] private int lumps = 2;
    [Tooltip("Whether its lumps lie apart as small stones around a middle one a little larger, instead of leaning on a body.")]
    [SerializeField] private bool scattered;
    [Tooltip("How far from the middle each scattered stone lies, as a share of half the width: the nearest and farthest.")]
    [SerializeField] private Vector2 scatter = new(0.5f, 0.8f);

    [Header("Colours")]
    [SerializeField] private ColourRamp stone = Ramp(0x26282e, 0x5e626c, 0x868a92, 0xb4b6b4);
    [Tooltip("The share of the stone's pixels speckled one tone darker or lighter.")]
    [SerializeField, Range(0f, 0.5f)] private float speckles = 0.1f;

    [Header("Moss")]
    [Tooltip("Whether moss covers the rock where it faces up.")]
    [SerializeField] private bool moss = true;
    [SerializeField] private ColourRamp mossColours = Ramp(0x1c3412, 0x3e6a24, 0x5e8e30, 0x92b648);
    [Tooltip("Moss covers the stone where its surface faces up past this.")]
    [SerializeField, Range(-1f, 1f)] private float mossFacing = 0.8f;
    [Tooltip("How much noise breaks up the moss's edge: the noise's full range.")]
    [SerializeField, Range(0f, 2f)] private float mossNoise = 0.4f;
    [Tooltip("The moss noise's cycles per cell.")]
    [SerializeField, Min(0.1f)] private float mossNoiseScale = 6f;

    /// <inheritdoc />
    public override void Draw(PropDrawing drawing)
    {
        int mossId = moss ? drawing.AddMaterial(new ArtMaterial(mossColours), true) : -1;
        int stoneId = drawing.AddMaterial(new ArtMaterial(stone, pattern: ArtPattern.Speckle, patternScale: speckles, cover: mossId, coverThreshold: mossFacing, coverNoise: mossNoise, coverNoiseScale: mossNoiseScale), true);
        float turn = drawing.Range(0f, Mathf.PI);
        if (scattered)
        {
            Scatter(drawing, turn, stoneId);
            return;
        }

        float bodyHalf = width * BodyShare;
        Lump(drawing, Vector3.zero, bodyHalf, bodyHalf * drawing.Range(depth), height, turn, stoneId);
        for (int k = 0; k < lumps; k++)
        {
            float side = (k % 2 == 0 ? 1f : -1f) * drawing.Range(LumpSpread) * width;
            Vector3 offset = new(Mathf.Cos(turn) * side, Mathf.Sin(turn) * side * LumpDepthSide + drawing.Range(LumpDrift) * width, 0f);
            Lump(drawing, offset, width * drawing.Range(LumpWidth), width * drawing.Range(LumpDepth), height * drawing.Range(LumpHeight), turn + drawing.Range(-LumpTurn, LumpTurn), stoneId);
        }
    }

    /// <summary>
    /// Draws the lumps as small stones: one in the middle, a little larger than the others, and the rest around it at
    /// even turns from <paramref name="turn"/>, jittered, each at the scatter's distance.
    /// </summary>
    private void Scatter(PropDrawing drawing, float turn, int material)
    {
        float middle = width * drawing.Range(LumpWidth) * MiddleStone;
        Lump(drawing, Vector3.zero, middle, middle * drawing.Range(depth), height, turn, material);
        for (int k = 0; k < lumps; k++)
        {
            float angle = turn + k * 2f * Mathf.PI / lumps + drawing.Range(-ScatterJitter, ScatterJitter);
            float distance = drawing.Range(scatter) * width * 0.5f;
            Vector3 offset = new(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance, 0f);
            float half = width * drawing.Range(LumpWidth);
            Lump(drawing, offset, half, half * drawing.Range(depth), height * drawing.Range(LumpHeight), drawing.Range(0f, Mathf.PI), material);
        }
    }

    /// <summary>
    /// Draws one lump standing at <paramref name="at"/>: an ellipsoid of half width <paramref name="rx"/>, half depth
    /// <paramref name="ry"/> and top <paramref name="lumpHeight"/> above the ground, turned <paramref name="turn"/>
    /// radians, cut by the recipe's random planes and flattened on top, as its own part.
    /// </summary>
    private void Lump(PropDrawing drawing, Vector3 at, float rx, float ry, float lumpHeight, float turn, int material)
    {
        float rz = lumpHeight / (2f - 2f * sink);
        Vector3 centre = at + new Vector3(0f, 0f, rz * (1f - 2f * sink));
        Vector3 ex = new(Mathf.Cos(turn), Mathf.Sin(turn), 0f);
        Vector3 ey = new(-Mathf.Sin(turn), Mathf.Cos(turn), 0f);
        List<Vector4> planes = new();
        for (int k = 0; k < BoundingSides; k++)
        {
            Vector3 n = new(Mathf.Cos(turn + k * 2f * Mathf.PI / BoundingSides), Mathf.Sin(turn + k * 2f * Mathf.PI / BoundingSides), 0f);
            float reach = Mathf.Sqrt(Square(rx * Vector3.Dot(n, ex)) + Square(ry * Vector3.Dot(n, ey)));
            planes.Add(new Vector4(n.x, n.y, n.z, Vector3.Dot(n, centre) + reach));
        }

        for (int k = 0; k < facets; k++)
        {
            Vector3 n = drawing.OnSphere();
            n.z = Mathf.Abs(n.z) * FacetUp - FacetDown;
            n.Normalize();
            float reach = Mathf.Sqrt(Square(rx * Vector3.Dot(n, ex)) + Square(ry * Vector3.Dot(n, ey)) + Square(rz * n.z));
            planes.Add(new Vector4(n.x, n.y, n.z, Vector3.Dot(n, centre) + reach * drawing.Range(cut)));
        }

        planes.Add(new Vector4(0f, 0f, 1f, centre.z + rz * drawing.Range(top)));
        planes.Add(new Vector4(0f, 0f, -1f, BottomDepth));
        float across = Mathf.Max(rx, ry) * BoundsMargin;
        drawing.Canvas.Convex(planes, new Bounds(centre, new Vector3(across, across, rz * BoundsMargin) * 2f), material, drawing.NewPart());
    }

    /// <summary>
    /// <paramref name="value"/> squared.
    /// </summary>
    private static float Square(float value)
    {
        return value * value;
    }
}
