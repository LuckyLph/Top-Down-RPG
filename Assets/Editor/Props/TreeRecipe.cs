using UnityEngine;

/// <summary>
/// A tree with a round crown (Docs/PropGeneration.md): a tapering trunk with roots splayed over the ground and branches
/// reaching into a crown of leaf clumps (<see cref="PropCrown"/>), its top on screen at the tree's height.
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
    private static readonly Vector3 BranchReachMin = new(-0.7f, -0.3f, -0.35f);
    private static readonly Vector3 BranchReachMax = new(0.7f, 0.2f, 0.2f);

    [Header("Size (cells)")]
    [Tooltip("How tall the tree stands on screen, from its base to the top of its crown.")]
    [SerializeField, Min(0.5f)] private float height = 3.5f;
    [Tooltip("The crown's width.")]
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
    [Tooltip("How many branches reach from the trunk into the crown.")]
    [SerializeField, Min(0)] private int branches = 3;
    [SerializeField] private PropRoots roots = new();

    [Header("Crown")]
    [SerializeField] private PropCrown crown = new();

    [Header("Colours")]
    [SerializeField] private ColourRamp bark = Ramp(0x1e140e, 0x3e2a1c, 0x604230, 0x8a6644);
    [Tooltip("The bark's grooves per cell around the trunk.")]
    [SerializeField, Min(1f)] private float barkGrooves = 16f;
    [SerializeField] private ColourRamp leaves = Ramp(0x0f2a22, 0x1f4f38, 0x33753f, 0x6ea24a);

    /// <inheritdoc />
    public override void Draw(PropDrawing drawing)
    {
        ArtCanvas canvas = drawing.Canvas;
        int barkId = drawing.AddMaterial(new ArtMaterial(bark, pattern: ArtPattern.Bark, patternScale: barkGrooves), true);
        int leafId = drawing.AddMaterial(new ArtMaterial(leaves), false);
        int coreId = drawing.AddMaterial(new ArtMaterial(leaves, toneOffset: -1), false);
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
}
