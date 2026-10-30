using UnityEngine;

/// <summary>
/// The folder of <c>Assets/Prefabs/Props</c> a prop's prefabs go in, which the Prop Placement window lists.
/// </summary>
public enum PropCategory
{
    Trees,
    Bushes,
    Rocks,
    Wood,
    Plants
}

/// <summary>
/// An editor-only recipe for a natural prop (Docs/PropGeneration.md), made like the character recipes: the prop's name
/// and category, a seed and how many variants to bake, the frame each variant is drawn in and the base pixel it stands
/// on, whether it blocks, the shading, its shadow, whether it fades while it hides the local player, and the terrains
/// its inspector previews it on. Each kind of prop is a kind of recipe holding its shape and palette and drawing a
/// variant onto a <see cref="PropDrawing"/>: trees (<see cref="TreeRecipe"/>), bushes (<see cref="BushRecipe"/>), rocks
/// (<see cref="RockRecipe"/>), wood (<see cref="WoodRecipe"/>) and plants (<see cref="PlantRecipe"/>).
/// <see cref="PropBaker"/> draws the variants without touching an asset, the same recipe always giving the same pixels;
/// <see cref="PropWriter"/> writes the sheet and a prefab per variant.
/// </summary>
public abstract class PropRecipe : ScriptableObject
{
    [Tooltip("The prop's name in its sheet, slice and prefab names: <name>.png, <name>_<variant>. Letters, digits and underscores, starting with a letter.")]
    [SerializeField] private string propName = "Prop";
    [Tooltip("The folder of Assets/Prefabs/Props its prefabs go in.")]
    [SerializeField] private PropCategory category = PropCategory.Trees;
    [Tooltip("Every variant draws its random values from this seed and its index.")]
    [SerializeField] private int seed = 1;
    [Tooltip("How many variants to bake, each its own prefab. Lowering it is refused while a prefab of a dropped variant exists, since placed props reference it.")]
    [SerializeField, Min(1)] private int variantCount = 4;
    [Tooltip("The size of each variant's frame in pixels. The drawing may not reach its edge.")]
    [SerializeField] private Vector2Int frameSize = new(96, 128);
    [Tooltip("The pixel of the frame the prop's base stands on, from its bottom left: the sprite's pivot and the prefab's root.")]
    [SerializeField] private Vector2Int basePixel = new(48, 10);
    [Tooltip("Whether the prop blocks the cells under its footprint (a variant of BlockingProp) or is walked through (a variant of Prop).")]
    [SerializeField] private bool blocking = true;
    [Tooltip("How high a drawn surface may lie, in cells, and still count as touching the ground, for the footprint.")]
    [SerializeField, Min(0.01f)] private float contactHeight = 0.12f;

    [Header("Shading")]
    [Tooltip("A surface facing the light more than this takes the light tone.")]
    [SerializeField, Range(-1f, 1f)] private float lightThreshold = 0.64f;
    [Tooltip("A surface facing the light this much or less takes the shadow tone.")]
    [SerializeField, Range(-1f, 1f)] private float shadowThreshold = 0.28f;
    [Tooltip("A pixel beside another part nearer by more than this many depth pixels is lined.")]
    [SerializeField, Min(0f)] private float innerLineGap = 2.6f;

    [Header("Shadow")]
    [Tooltip("Whether the prop casts the sun's shadow, its sprite laid on the ground away from the sun (Docs/Lighting.md). Off for props lying flat on the ground.")]
    [SerializeField] private bool castsShadow = true;

    [Header("In play")]
    [Tooltip("Whether the prop fades while it hides the local player, as trees and spires do: its prefab's fade covers its drawing.")]
    [SerializeField] private bool fades;

    [Header("Preview")]
    [Tooltip("The terrains the inspector shows the variants on.")]
    [SerializeField] private TerrainMaterial[] previewTerrains = new TerrainMaterial[0];

    public string PropName => propName;
    public PropCategory Category => category;
    public int Seed => seed;
    public int VariantCount => variantCount;
    public Vector2Int FrameSize => frameSize;
    public Vector2Int BasePixel => basePixel;
    public bool Blocking => blocking;
    public float ContactHeight => contactHeight;
    public float LightThreshold => lightThreshold;
    public float ShadowThreshold => shadowThreshold;
    public float InnerLineGap => innerLineGap;
    public bool CastsShadow => castsShadow;
    public bool Fades => fades;
    public TerrainMaterial[] PreviewTerrains => previewTerrains;

    /// <summary>
    /// Draws one variant onto <paramref name="drawing"/>, in cells with the base at the world origin, drawing every
    /// random value from its random stream.
    /// </summary>
    public abstract void Draw(PropDrawing drawing);

    /// <summary>
    /// Whether the recipe can bake: a name of letters, digits and underscores starting with a letter, and a frame of
    /// at least 8 px a side with its base pixel inside. Returns the reason in <paramref name="problem"/> when it cannot.
    /// </summary>
    public bool CanGenerate(out string problem)
    {
        problem = null;
        if (!CharacterRecipe.IsName(propName, true))
        {
            problem = "the prop name must be letters, digits and underscores, starting with a letter.";
            return false;
        }

        if (frameSize.x < 8 || frameSize.y < 8 || basePixel.x < 0 || basePixel.y < 0 || basePixel.x >= frameSize.x || basePixel.y >= frameSize.y)
        {
            problem = "the frame must be at least 8 px a side with its base pixel inside.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// A ramp from four colours written as hexadecimal RGB, darkest first.
    /// </summary>
    protected static ColourRamp Ramp(uint outline, uint shadow, uint middle, uint light)
    {
        return new ColourRamp(Colour(outline), Colour(shadow), Colour(middle), Colour(light));
    }

    /// <summary>
    /// An opaque colour from <paramref name="rgb"/> written as hexadecimal RGB.
    /// </summary>
    private static Color32 Colour(uint rgb)
    {
        return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }
}
