using UnityEngine;

/// <summary>
/// A mob's death puff (Docs/Animation.md, Phase 6): a white pop, then round blobs of smoke shaded with the light from
/// the top left that swell, rise, spread and shrink, dissolving through an ordered dither toward the end, drawn by
/// <see cref="EffectPainter"/>. One variant facing one way, its frames pivoted on the feet so it sorts where the mob
/// stood; drawn at the game's pixel density, so it plays unscaled.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/Effects/Puff Effect Recipe", fileName = "DeathPuff")]
public sealed class PuffEffectRecipe : EffectRecipe
{
    [Header("Frames")]
    [SerializeField, Range(8, 128)] private int width = 48;
    [SerializeField, Range(8, 128)] private int height = 56;
    [Tooltip("The feet, where the frame pivots: a pixel corner, in pixels from the frame's bottom left.")]
    [SerializeField] private Vector2Int feet = new(24, 4);
    [SerializeField, Range(1, 32)] private int frameCount = 10;

    [Header("Look")]
    [Tooltip("Outline, shadow, middle and light, darkest first.")]
    [SerializeField] private ColourRamp colours = new(new Color32(52, 44, 64, 255), new Color32(98, 88, 114, 255), new Color32(150, 140, 164, 255), new Color32(208, 202, 218, 255));
    [Tooltip("Where the light comes from on the screen.")]
    [SerializeField] private Vector2 lightDirection = new(-0.6f, 0.8f);
    [Tooltip("How squarely a blob's edge must face the light to take the light colour, and face away from it to take the shadow.")]
    [SerializeField, Range(-1f, 1f)] private float lightFacing = 0.45f;
    [SerializeField, Range(-1f, 1f)] private float shadowFacing = -0.35f;

    [Header("Pop")]
    [Tooltip("The first frame is a white oval of these radii, centred this high above the feet.")]
    [SerializeField] private bool pop = true;
    [SerializeField] private Vector2 popRadii = new(7f, 11f);
    [SerializeField, Min(0f)] private float popHeight = 14f;

    [Header("Blobs")]
    [SerializeField] private PuffBlob[] blobs = DefaultBlobs();
    [Tooltip("How long a blob takes to swell to its size, in seconds, from this share of it.")]
    [SerializeField, Min(0.001f)] private float swellSeconds = 0.12f;
    [SerializeField, Range(0f, 1f)] private float startSize = 0.55f;
    [Tooltip("How much a blob grows each second after that.")]
    [SerializeField, Min(0f)] private float growth = 0.1f;
    [Tooltip("How much a blob shrinks over the shrink time, more the higher it sits (its height over 30 pixels, plus 0.4).")]
    [SerializeField, Range(0f, 1f)] private float shrink = 0.6f;
    [SerializeField, Min(0.001f)] private float shrinkSeconds = 0.7f;
    [Tooltip("How fast blobs spread sideways, as a share of their distance from the middle each second.")]
    [SerializeField, Min(0f)] private float spread = 1.4f;
    [Tooltip("How fast blobs rise, as a share of their height each second, plus pixels each second.")]
    [SerializeField, Min(0f)] private float rise = 0.5f;
    [SerializeField, Min(0f)] private float lift = 10f;

    [Header("Dissolve")]
    [Tooltip("When the dither starts eating the puff, in seconds; it is gone by the end of the last frame.")]
    [SerializeField, Min(0f)] private float dissolveFrom = 0.38f;
    [Tooltip("The outline goes once the dissolve has gone this far.")]
    [SerializeField, Range(0f, 1f)] private float outlineUntil = 0.6f;

    public ColourRamp Colours => colours;
    public Vector2 LightDirection => lightDirection;
    public float LightFacing => lightFacing;
    public float ShadowFacing => shadowFacing;
    public bool Pop => pop;
    public Vector2 PopRadii => popRadii;
    public float PopHeight => popHeight;
    public PuffBlob[] Blobs => blobs;
    public float SwellSeconds => swellSeconds;
    public float StartSize => startSize;
    public float Growth => growth;
    public float Shrink => shrink;
    public float ShrinkSeconds => shrinkSeconds;
    public float Spread => spread;
    public float Rise => rise;
    public float Lift => lift;
    public float DissolveFrom => dissolveFrom;
    public float OutlineUntil => outlineUntil;
    public Vector2Int Feet => feet;
    public override int FrameWidth => width;
    public override int FrameHeight => height;
    public override int FrameCount => frameCount;
    public override int VariantCount => 1;
    public override int DirectionCount => 1;
    public override Vector2 Pivot => new((float)feet.x / width, (float)feet.y / height);

    /// <summary>
    /// Creates an unsaved recipe of the shipped grey smoke puff, for tests and the first asset.
    /// </summary>
    public static PuffEffectRecipe Create(string name)
    {
        PuffEffectRecipe recipe = CreateInstance<PuffEffectRecipe>();
        recipe.SetSheet(name, 0.07f);
        recipe.hideFlags = HideFlags.HideAndDontSave;
        return recipe;
    }

    /// <summary>
    /// Draws frame <paramref name="frame"/>; the puff has one variant and one direction.
    /// </summary>
    public override Color32[] BakeFrame(int variant, int direction, int frame)
    {
        return EffectPainter.Puff(this, frame);
    }

    /// <summary>
    /// Whether the recipe can bake: a name, blobs, and its feet inside the frame.
    /// </summary>
    public override bool CanGenerate(out string problem)
    {
        if (!base.CanGenerate(out problem))
        {
            return false;
        }

        if (blobs == null || blobs.Length == 0)
        {
            problem = "Add the blobs.";
            return false;
        }

        if (feet.x < 0 || feet.y < 0 || feet.x >= width || feet.y >= height)
        {
            problem = "The feet must lie inside the frame.";
            return false;
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// The shipped puff's blobs, roughly a body's shape around the feet, the lower ones first.
    /// </summary>
    private static PuffBlob[] DefaultBlobs()
    {
        return new[]
        {
            new PuffBlob(new Vector2(0f, 9f), 6.5f, 0f),
            new PuffBlob(new Vector2(-6f, 6f), 5f, 0.02f),
            new PuffBlob(new Vector2(6f, 6f), 5f, 0.03f),
            new PuffBlob(new Vector2(-4f, 16f), 6f, 0.04f),
            new PuffBlob(new Vector2(4f, 17f), 5.5f, 0.05f),
            new PuffBlob(new Vector2(0f, 24f), 5.5f, 0.07f),
            new PuffBlob(new Vector2(-8f, 13f), 4f, 0.06f),
            new PuffBlob(new Vector2(8f, 13f), 4f, 0.08f),
            new PuffBlob(new Vector2(-2f, 30f), 4f, 0.1f),
            new PuffBlob(new Vector2(3f, 3f), 4f, 0.01f)
        };
    }
}
