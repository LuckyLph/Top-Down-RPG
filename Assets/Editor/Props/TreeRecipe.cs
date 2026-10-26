using UnityEngine;

/// <summary>
/// The crowns a <see cref="TreeRecipe"/> draws.
/// </summary>
public enum TreeForm
{
    Round,
    Pine,
    Bare
}

/// <summary>
/// A tree (Docs/PropGeneration.md) on a tapering trunk with roots splayed over the ground. Round: branches reach into a
/// crown of leaf clumps (<see cref="PropCrown"/>), its top on screen at the tree's height. A pine: a short trunk under
/// tiers of streaked needles (<see cref="PropPine"/>). Bare: a trunk forking into twisted limbs (<see cref="PropLimbs"/>),
/// as a dead or charred tree. Any of them can take a cover where it faces up (<see cref="PropCover"/>: snow, ash).
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Props/Tree Recipe", fileName = "Tree_Broadleaf")]
public sealed class TreeRecipe : PropRecipe
{
    private const float TrunkSink = 0.05f;
    private const float TrunkTop = 0.35f;
    private const float TrunkLean = 0.6f;
    private const float BranchDrop = 0.25f;
    private const float BranchRadius = 0.5f;
    private const float BranchTip = 0.22f;
    private const float PineTrunkInside = 0.3f;
    private static readonly Vector3 BranchReachMin = new(-0.7f, -0.3f, -0.35f);
    private static readonly Vector3 BranchReachMax = new(0.7f, 0.2f, 0.2f);

    [Tooltip("A round crown of leaf clumps, a pine's tiers, or bare limbs.")]
    [SerializeField] private TreeForm form = TreeForm.Round;

    [Header("Size (cells)")]
    [Tooltip("How tall the tree stands on screen, from its base to the top of its crown; for bare limbs, the height its trunk is a share of.")]
    [SerializeField, Min(0.5f)] private float height = 3.5f;
    [Tooltip("The crown's width, or a pine's lowest tier's.")]
    [SerializeField, Min(0.2f)] private float crownWidth = 2.5f;
    [Tooltip("The crown's height on screen.")]
    [SerializeField, Min(0.2f)] private float crownHeight = 2.4f;
    [Tooltip("The crown's depth, as a share of its width.")]
    [SerializeField, Range(0.3f, 1f)] private float crownDepth = 0.9f;
    [Tooltip("How far the crown's middle may wander from above the trunk, east and west then north and south.")]
    [SerializeField] private Vector2 crownWander = new(0.08f, 0.05f);

    [Header("Trunk")]
    [Tooltip("The trunk's radius at its foot, in cells.")]
    [SerializeField, Min(0.01f)] private float trunkRadius = 0.16f;
    [Tooltip("The trunk's radius under the crown, as a share of its foot's.")]
    [SerializeField, Range(0.1f, 1f)] private float trunkTaper = 0.62f;
    [Tooltip("How many branches reach from the trunk into a round crown.")]
    [SerializeField, Min(0)] private int branches = 3;
    [SerializeField] private PropRoots roots = new();

    [Header("Crown")]
    [SerializeField] private PropCrown crown = new();
    [SerializeField] private PropPine pine = new();
    [SerializeField] private PropLimbs limbs = new();

    [Header("Colours")]
    [SerializeField] private ColourRamp bark = Ramp(0x1e140e, 0x3e2a1c, 0x604230, 0x8a6644);
    [Tooltip("The bark's grooves per cell around the trunk.")]
    [SerializeField, Min(1f)] private float barkGrooves = 16f;
    [Tooltip("The leaves of a round crown, or a pine's needles.")]
    [SerializeField] private ColourRamp leaves = Ramp(0x0f2a22, 0x1f4f38, 0x33753f, 0x6ea24a);
    [Tooltip("A pine's needle streaks: the noise's cycles around its trunk per radian.")]
    [SerializeField, Min(0.1f)] private float needleStreaks = 6f;

    [Header("Cover")]
    [SerializeField] private PropCover cover = new(false, Ramp(0x5a6a8c, 0xa9b8d6, 0xd6e0f0, 0xf6f9ff), 0.55f, 0.35f, 6f);

    /// <inheritdoc />
    public override void Draw(PropDrawing drawing)
    {
        int coverId = cover.Add(drawing);
        switch (form)
        {
            case TreeForm.Pine:
                DrawPine(drawing, coverId);
                break;
            case TreeForm.Bare:
                DrawBare(drawing, coverId);
                break;
            default:
                DrawRound(drawing, coverId);
                break;
        }
    }

    /// <summary>
    /// Draws a round crown on its trunk, roots and branches.
    /// </summary>
    private void DrawRound(PropDrawing drawing, int coverId)
    {
        ArtCanvas canvas = drawing.Canvas;
        int barkId = drawing.AddMaterial(cover.Under(bark, coverId, ArtPattern.Bark, barkGrooves), true);
        int leafId = drawing.AddMaterial(cover.Under(leaves, coverId), false);
        int coreId = drawing.AddMaterial(cover.Under(leaves, coverId, toneOffset: -1), false);
        float rx = crownWidth * 0.5f;
        float ry = rx * crownDepth;
        float halfHeight = crownHeight * 0.5f;
        float rz = Mathf.Sqrt(Mathf.Max(0.05f, halfHeight * halfHeight - ry * ArtCanvas.SinPitch * ry * ArtCanvas.SinPitch)) / ArtCanvas.CosPitch;
        Vector3 centre = new(drawing.Range(-crownWander.x, crownWander.x), drawing.Range(-crownWander.y, crownWander.y), (height - halfHeight) / ArtCanvas.CosPitch);
        Vector3 top = new(centre.x * TrunkLean, 0f, centre.z - rz * TrunkTop);
        int trunk = drawing.NewPart();
        canvas.Capsule(new Vector3(0f, 0f, -TrunkSink), top, trunkRadius, trunkRadius * trunkTaper, barkId, trunk);
        roots.Draw(drawing, trunkRadius, barkId, trunk);
        Vector3 radii = new(rx, ry, rz);
        for (int k = 0; k < branches; k++)
        {
            Vector3 reach = new(drawing.Range(BranchReachMin.x, BranchReachMax.x), drawing.Range(BranchReachMin.y, BranchReachMax.y), drawing.Range(BranchReachMin.z, BranchReachMax.z));
            canvas.Capsule(top - new Vector3(0f, 0f, BranchDrop), centre + Vector3.Scale(reach, radii), trunkRadius * BranchRadius, trunkRadius * BranchTip, barkId, trunk);
        }

        crown.Draw(drawing, centre, radii, leafId, coreId);
    }

    /// <summary>
    /// Draws a pine: its trunk up into its lowest tier, its roots, and its tiers of streaked needles.
    /// </summary>
    private void DrawPine(PropDrawing drawing, int coverId)
    {
        int barkId = drawing.AddMaterial(cover.Under(bark, coverId, ArtPattern.Bark, barkGrooves), true);
        int needleId = drawing.AddMaterial(cover.Under(leaves, coverId, ArtPattern.Streaks, needleStreaks), false);
        int trunk = drawing.NewPart();
        drawing.Canvas.Capsule(new Vector3(0f, 0f, -TrunkSink), new Vector3(0f, 0f, pine.Clearance + PineTrunkInside), trunkRadius, trunkRadius * trunkTaper, barkId, trunk);
        roots.Draw(drawing, trunkRadius, barkId, trunk);
        pine.Draw(drawing, height, crownWidth, needleId);
    }

    /// <summary>
    /// Draws a bare tree: its trunk and limbs, lined while thick, and its roots.
    /// </summary>
    private void DrawBare(PropDrawing drawing, int coverId)
    {
        int barkId = drawing.AddMaterial(cover.Under(bark, coverId, ArtPattern.Bark, barkGrooves), true);
        int twigId = drawing.AddMaterial(cover.Under(bark, coverId, ArtPattern.Bark, barkGrooves, false), true);
        int trunk = limbs.Draw(drawing, height, trunkRadius, barkId, twigId);
        roots.Draw(drawing, trunkRadius, barkId, trunk);
    }
}
