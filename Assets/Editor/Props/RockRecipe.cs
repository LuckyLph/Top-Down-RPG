using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The shapes a <see cref="RockRecipe"/> draws.
/// </summary>
public enum RockForm
{
    Lumps,
    Scattered,
    Crag
}

/// <summary>
/// A rock (Docs/PropGeneration.md, Phase 0 notes) in speckled or jointed stone, with an optional cover where it faces up
/// (<see cref="PropCover"/>: moss, snow). Of lumps: a body and a few smaller lumps leaning on its sides, each a convex
/// solid inside an octagon around its ellipsoid, cut by random planes leaning up and flattened on top, sunk partly into
/// the ground; its width counts its lumps, so rocks and boulders are this one kind. Scattered: its lumps are small
/// stones lying apart around a middle one. A crag: blocks with a few flat sides stacked into a spire, each narrower than
/// the one under it, sitting a little off it with its top tilted, the last pointed, and a smaller spire beside it that
/// does not count as touching the ground.
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
    private const float CragFoot = 0.3f;
    private const float CompanionFoot = 0.62f;
    private const float CompanionSpread = 0.42f;
    private const float CompanionDrift = 0.12f;
    private const float BlockOverlap = 0.08f;
    private const float BlockBounds = 1.4f;

    [Tooltip("A body with lumps leaning on it, small stones scattered apart, or a crag of stacked blocks.")]
    [SerializeField] private RockForm form = RockForm.Lumps;

    [Header("Size (cells)")]
    [Tooltip("The rock's width, its lumps or the crag's smaller spire included.")]
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
    [Tooltip("How far from the middle each scattered stone lies, as a share of half the width: the nearest and farthest.")]
    [SerializeField] private Vector2 scatter = new(0.5f, 0.8f);

    [Header("Crag")]
    [Tooltip("How many blocks the crag stacks, each narrower than the one under it, the last pointed.")]
    [SerializeField, Min(1)] private int blocks = 3;
    [Tooltip("How many flat sides each block has.")]
    [SerializeField, Range(3, 12)] private int sides = 6;
    [Tooltip("How much narrower each block is than the one under it, as a share.")]
    [SerializeField, Range(0f, 0.8f)] private float narrowing = 0.25f;
    [Tooltip("How much each block narrows from its foot to its top, as a share.")]
    [SerializeField, Range(0f, 0.8f)] private float taper = 0.12f;
    [Tooltip("How far each block may sit off the one under it, in cells.")]
    [SerializeField, Min(0f)] private float shift = 0.05f;
    [Tooltip("How far each block's top may tilt, in cells of height per cell across.")]
    [SerializeField, Min(0f)] private float tilt = 0.3f;
    [Tooltip("The smaller spire beside the crag, as a share of its height; 0 for none.")]
    [SerializeField, Range(0f, 1f)] private float companion = 0.5f;

    [Header("Colours")]
    [SerializeField] private ColourRamp stone = Ramp(0x26282e, 0x5e626c, 0x868a92, 0xb4b6b4);
    [Tooltip("The share of the stone's pixels speckled one tone darker or lighter.")]
    [SerializeField, Range(0f, 0.5f)] private float speckles = 0.1f;
    [Tooltip("Joints across the stone per cell up, as basalt's cracks, drawn instead of speckles; 0 for none.")]
    [SerializeField, Min(0f)] private float joints;

    [Header("Cover")]
    [SerializeField] private PropCover cover = new(true, Ramp(0x1c3412, 0x3e6a24, 0x5e8e30, 0x92b648), 0.8f, 0.4f, 6f);

    /// <inheritdoc />
    public override void Draw(PropDrawing drawing)
    {
        int coverId = cover.Add(drawing);
        ArtMaterial stoneMaterial = joints > 0f ? cover.Under(stone, coverId, ArtPattern.Joints, joints) : cover.Under(stone, coverId, ArtPattern.Speckle, speckles);
        int stoneId = drawing.AddMaterial(stoneMaterial, true);
        float turn = drawing.Range(0f, Mathf.PI);
        if (form == RockForm.Scattered)
        {
            Scatter(drawing, turn, stoneId);
            return;
        }

        if (form == RockForm.Crag)
        {
            Crag(drawing, turn, stoneId, drawing.AddMaterial(stoneMaterial, false));
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
    /// Draws the crag on the base, turned <paramref name="turn"/>, in <paramref name="material"/>, and its smaller spire
    /// east or west of it in <paramref name="beside"/>, a block fewer.
    /// </summary>
    private void Crag(PropDrawing drawing, float turn, int material, int beside)
    {
        Spire(drawing, Vector3.zero, width * CragFoot, height, blocks, turn, material);
        if (companion <= 0f)
        {
            return;
        }

        float side = drawing.Random.NextDouble() < 0.5 ? -1f : 1f;
        Vector3 at = new(side * width * CompanionSpread, drawing.Range(-CompanionDrift, CompanionDrift), 0f);
        Spire(drawing, at, width * CragFoot * CompanionFoot, height * companion, Mathf.Max(1, blocks - 1), drawing.Range(0f, Mathf.PI), beside);
    }

    /// <summary>
    /// Draws a spire of <paramref name="count"/> blocks standing at <paramref name="foot"/>, its lowest block
    /// <paramref name="apothem"/> from its middle to each side, <paramref name="tall"/> high: each block a convex solid
    /// with the recipe's sides leaning in by its taper, its own part, taller the lower it is, the last pointed.
    /// </summary>
    private void Spire(PropDrawing drawing, Vector3 foot, float apothem, float tall, int count, float turn, int material)
    {
        float total = 0f;
        for (int i = 0; i < count; i++)
        {
            total += count + 2 - i;
        }

        Vector3 centre = foot;
        float z = 0f;
        float foot0 = apothem;
        for (int i = 0; i < count; i++)
        {
            bool last = i == count - 1;
            float top = z + tall * (count + 2 - i) / total;
            float bottomApothem = foot0;
            float topApothem = last ? 0f : bottomApothem * (1f - taper);
            float rotation = turn + drawing.Range(0f, 2f * Mathf.PI / sides);
            Vector2 lean = Random2(drawing) * drawing.Range(0f, tilt);
            List<Vector4> planes = new();
            for (int k = 0; k < sides; k++)
            {
                float angle = rotation + k * 2f * Mathf.PI / sides;
                Vector3 outward = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                Vector3 n = (outward * (top - z) + Vector3.forward * (bottomApothem - topApothem)).normalized;
                planes.Add(new Vector4(n.x, n.y, n.z, Vector3.Dot(n, centre + outward * bottomApothem + Vector3.forward * z)));
            }

            float floor = i == 0 ? -BottomDepth : z - BlockOverlap - bottomApothem * tilt;
            planes.Add(new Vector4(0f, 0f, -1f, -floor));
            if (!last)
            {
                Vector3 up = new Vector3(lean.x, lean.y, 1f).normalized;
                planes.Add(new Vector4(up.x, up.y, up.z, Vector3.Dot(up, centre + Vector3.forward * top)));
            }

            float across = bottomApothem * BlockBounds;
            float above = top + bottomApothem * tilt + BlockOverlap;
            Vector3 middle = new(centre.x, centre.y, (floor + above) * 0.5f);
            drawing.Canvas.Convex(planes, new Bounds(middle, new Vector3(across * 2f, across * 2f, above - floor)), material, drawing.NewPart());
            Vector2 step = Random2(drawing) * drawing.Range(0f, shift);
            centre += new Vector3(step.x, step.y, 0f);
            z = top;
            foot0 = bottomApothem * (1f - narrowing);
        }
    }

    /// <summary>
    /// A direction picked evenly around the ground plane.
    /// </summary>
    private static Vector2 Random2(PropDrawing drawing)
    {
        float angle = drawing.Range(0f, 2f * Mathf.PI);
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
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
