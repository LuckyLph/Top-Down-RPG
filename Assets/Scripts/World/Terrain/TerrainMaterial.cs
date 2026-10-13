using System;
using UnityEngine;

/// <summary>
/// One ground material a <see cref="TerrainBlendRenderer"/> layer draws (Docs/TerrainBlending.md): its textures (32 px
/// per cell, repeating every few cells), its shape's corner radii, how its chips or stones meet an edge, and its edge
/// over any material the <see cref="TerrainStyle"/> has no pair override for. Read-only at runtime: renderers copy its
/// textures and settings and never write it. In the editor, a terrain material recipe generates its textures and writes
/// their base colour, repeat and cell mode. In the soft look its height texture, times its height scale, moves its
/// edges. An animated material (water) lights its glint and sparkle pixels in turn and swells its rims in and out, in
/// the shader alone, at its frames per second; its edges never move. A sunken material (water) lies below the ground
/// around it: along its edges that face up the screen over another material, that material's bank shows inside its
/// edge, in the other material's bank colour (grass shows the dirt under it), and casts a shadow on it.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Material", fileName = "TerrainMaterial")]
public class TerrainMaterial : ScriptableObject
{
    public const float MaxHeightScale = 2.5f;
    public const float DefaultHeightScale = 2f;
    public const float MaxFramesPerSecond = 30f;
    public const float MinPeriod = 0.1f;
    public const float MaxSinkDepth = 6f;

    /// <summary>
    /// What a material's cell field holds and how its chips or stones meet an edge: none, chips drawn whole or left
    /// out, or stones left out or cut along the rim.
    /// </summary>
    public enum CellMode
    {
        None,
        WholeChips,
        CutStones
    }

    [Header("Textures")]
    [Tooltip("The interior: repeat x 32 px square, RGBA32 with sRGB off (the shader treats its bytes as sRGB itself). Written by the material's recipe.")]
    [SerializeField] private Texture2D albedo;
    [Tooltip("Optional: for every pixel, the offset to its chip's or stone's centre (see Docs/Architecture.md, World and areas). Same size and format as the albedo. Needed unless Cell Mode is None. Written by the material's recipe.")]
    [SerializeField] private Texture2D cellField;
    [Tooltip("Optional: the height the soft look reads (red a smooth height, green one value per chip or stone), and the glint and sparkle phases an animated material reads (blue: 0 for neither, 1 to 127 a glint, 128 to 255 a sparkle). Same size and format as the albedo. Without it the soft look moves this material's edges by nothing and nothing glints. Written by the material's recipe.")]
    [SerializeField] private Texture2D height;
    [Tooltip("Cells the textures cover before they repeat. Written by the material's recipe.")]
    [SerializeField, Min(1)] private int repeatCells = 4;
    [Header("Shape")]
    [Tooltip("Radius in pixels of an outer corner. Under half a cell, so edges stay straight across cell borders. Materials whose edges meet should share their radii, or the lower one shows at corners.")]
    [SerializeField, Range(0f, 15.5f)] private float outerRadius = 14f;
    [Tooltip("Radius in pixels of an inner corner's fillet. Under half a cell.")]
    [SerializeField, Range(0f, 15.5f)] private float innerRadius = 11f;
    [Header("Chips")]
    [Tooltip("What the cell field holds: nothing, whole chips or cut stones. Written by the material's recipe.")]
    [SerializeField] private CellMode cellMode = CellMode.None;
    [Tooltip("Colour under the chips or stones, shown where one is left out or cut; the cell field's blue channel shades it per pixel. Written by the material's recipe with its textures.")]
    [SerializeField] private Color baseColour = Color.gray;
    [Tooltip("Whole chips: a chip is drawn only where all of it (as far as its field's reach) lies at least this many pixels inside the rim, else left out. Cut stones: a stone is drawn only where its centre lies at least this many pixels inside the rim; negative keeps stones whose centre lies in the rim, cut along its line.")]
    [SerializeField, Range(-4f, 6f)] private float keepMargin = 1f;
    [Tooltip("Cut stones: width in pixels of the base colour line between the rim and a stone cut along it.")]
    [SerializeField, Range(0f, 3f)] private float cutGap = 0.5f;
    [Tooltip("Cut stones: how much the light changes a stone's colour along its cut, in 0-255 steps.")]
    [SerializeField, Range(0f, 64f)] private float cutBevel = 18f;
    [Header("Soft look")]
    [Tooltip("How many pixels the height texture can move this material's edges in the soft look, either way: its red channel moves them smoothly, and where a pair shows whole stones its green channel moves each stone's centre. At most 2.5 px, which keeps the height's slope below the weight's so no blob can detach (Docs/TerrainBlending.md, Phase 2 notes).")]
    [SerializeField, Range(0f, MaxHeightScale)] private float heightScale = DefaultHeightScale;
    [Header("Animation")]
    [Tooltip("Frames per second of the material's animation (its glints and the swell of its rims), stepped like a sprite animation so every animated pixel changes on the same frames; 0 for a still material.")]
    [SerializeField, Range(0f, MaxFramesPerSecond)] private float framesPerSecond;
    [Tooltip("Colour a glint pixel lights up with: a pixel whose height texture's blue channel holds a glint phase, which an animated recipe (water's) writes. At half strength over the first and last quarter of each glint. Written by the material's recipe.")]
    [SerializeField] private Color glintColour = Color.white;
    [Tooltip("Seconds between two glints of the same pixel.")]
    [SerializeField, Range(MinPeriod, 30f)] private float glintPeriod = 3f;
    [Tooltip("Fraction of the glint period a glint pixel stays lit.")]
    [SerializeField, Range(0f, 1f)] private float glintDuty = 0.2f;
    [Tooltip("Colour a sparkle pixel lights up with: a pixel whose height texture's blue channel holds a sparkle phase. Like a glint, on its own colour and timing. Written by the material's recipe.")]
    [SerializeField] private Color sparkleColour = Color.white;
    [Tooltip("Seconds between two sparkles of the same pixel.")]
    [SerializeField, Range(MinPeriod, 30f)] private float sparklePeriod = 4f;
    [Tooltip("Fraction of the sparkle period a sparkle pixel stays lit.")]
    [SerializeField, Range(0f, 1f)] private float sparkleDuty = 0.25f;
    [Tooltip("How many pixels further in this material's rims reach at the top of a swell, so a shore's foam washes in and out; 0 for none. The edge itself never moves.")]
    [SerializeField, Range(0f, 4f)] private float rimSwell;
    [Tooltip("Seconds of one swell, in and out. Its phase drifts along the edge, so the swell rolls along a shore.")]
    [SerializeField, Range(MinPeriod, 30f)] private float swellPeriod = 4f;
    [Header("Sunken")]
    [Tooltip("How many pixels of bank show inside this material's edges where they face straight up the screen, as if it lay that far below the ground around it; less as an edge turns sideways, none along edges facing down or over nothing. The bank is the bank colour of the material it meets, its lowest pixel, at the waterline, darker. 0 for a material level with the ground.")]
    [SerializeField, Range(0f, MaxSinkDepth)] private float sinkDepth;
    [Tooltip("How much darker the bank's shadow makes this material just below the bank, over as many pixels as the bank is high.")]
    [SerializeField, Range(0f, 1f)] private float bankShadow = 0.25f;
    [Tooltip("Colour of this material's face where a sunken material (water) cuts into it, the bank above a pond's top edge: what the ground shows when cut, such as the dirt under grass.")]
    [SerializeField] private Color bankColour = new(0.38f, 0.29f, 0.2f, 1f);
    [Header("Edge")]
    [Tooltip("How this material's edge is drawn over any material, or over nothing, without a pair override in the style.")]
    [SerializeField] private TerrainStyle.EdgeSettings edge = new();

    public Texture2D Albedo => albedo;
    public Texture2D CellField => cellField;
    public Texture2D Height => height;
    public int RepeatCells => repeatCells;
    public int RepeatPixels => repeatCells * TerrainStyle.PixelsPerCell;
    public float OuterRadius => outerRadius;
    public float InnerRadius => innerRadius;
    public CellMode Cells => cellMode;
    public Color BaseColour => baseColour;
    public float KeepMargin => keepMargin;
    public float CutGap => cutGap;
    public float CutBevel => cutBevel;
    public float HeightScale => Mathf.Clamp(heightScale, 0f, MaxHeightScale);
    public bool IsAnimated => framesPerSecond > 0f;
    public float FramesPerSecond => Mathf.Clamp(framesPerSecond, 0f, MaxFramesPerSecond);
    public Color GlintColour => glintColour;
    public float GlintPeriod => Mathf.Max(glintPeriod, MinPeriod);
    public float GlintDuty => Mathf.Clamp01(glintDuty);
    public Color SparkleColour => sparkleColour;
    public float SparklePeriod => Mathf.Max(sparklePeriod, MinPeriod);
    public float SparkleDuty => Mathf.Clamp01(sparkleDuty);
    public float RimSwell => Mathf.Max(rimSwell, 0f);
    public float SwellPeriod => Mathf.Max(swellPeriod, MinPeriod);
    public float SinkDepth => Mathf.Clamp(sinkDepth, 0f, MaxSinkDepth);
    public float BankShadow => Mathf.Clamp01(bankShadow);
    public Color BankColour => bankColour;
    public TerrainStyle.EdgeSettings Edge => edge;

#if UNITY_EDITOR
    internal event Action Validated;
#endif

    /// <summary>
    /// Creates a material in memory with its textures, chips, edge and soft height, keeping the default corner radii,
    /// for tests.
    /// </summary>
    internal static TerrainMaterial Create(string materialName, Texture2D albedo, Texture2D cellField, int repeatCells, CellMode cellMode, Color baseColour, TerrainStyle.EdgeSettings edge, Texture2D height = null, float heightScale = DefaultHeightScale)
    {
        TerrainMaterial material = CreateInstance<TerrainMaterial>();
        material.name = materialName;
        material.albedo = albedo;
        material.cellField = cellField;
        material.repeatCells = repeatCells;
        material.cellMode = cellMode;
        material.baseColour = baseColour;
        material.edge = edge;
        material.height = height;
        material.heightScale = heightScale;
        return material;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Tells the renderers using this material to rebuild, as an inspector change does; the material recipe calls it
    /// after rewriting the textures, whose references may not have changed.
    /// </summary>
    internal void NotifyChanged()
    {
        Validated?.Invoke();
    }

    /// <summary>
    /// Warns about a height scale outside its bound, which only an edited asset file can hold and which is used
    /// clamped, and tells the renderers using this material to rebuild after an inspector change.
    /// </summary>
    private void OnValidate()
    {
        if (heightScale < 0f || heightScale > MaxHeightScale)
        {
            Debug.LogWarning($"{name}: height scale {heightScale} is outside 0 to {MaxHeightScale} px, the bound that keeps the soft look's blobs attached, so {HeightScale} is used.", this);
        }

        Validated?.Invoke();
    }
#endif
}
