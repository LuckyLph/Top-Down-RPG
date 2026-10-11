using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The look of the blended ground (Docs/TerrainBlending.md): the terrain shader, the materials that a
/// <see cref="TerrainBlendRenderer"/>'s layers draw with, each with its textures, corner radii, chips and default
/// edge, and the edges that pairs of an upper material over a lower one (or over nothing) draw instead. Read-only at
/// runtime: the renderer builds its runtime material and textures from it and never writes it.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Style", fileName = "TerrainStyle")]
public class TerrainStyle : ScriptableObject
{
    public const int PixelsPerCell = 32;
    public const int NoMaterial = -1;

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

    /// <summary>
    /// How an upper material's edge is drawn over what lies under it, from the outside in: a translucent shadow, an
    /// outline (or the rim's outer colour) on the edge pixel, then the rim over the interior, with optional tufts
    /// growing out of the edge.
    /// </summary>
    [Serializable]
    public class EdgeSettings
    {
        [Tooltip("Width in pixels of the band inside the edge drawn with the rim gradient; 0 for none.")]
        [SerializeField, Range(0f, 15f)] private float rimWidth = 1.5f;
        [Tooltip("The rim from the edge (left) to its inner end (right). Alpha is how much of the material's interior it covers. Its left end also fills the edge pixel when no outline is drawn.")]
        [SerializeField] private Gradient rim = new();
        [SerializeField] private bool drawOutline = true;
        [SerializeField] private Color outlineColour = Color.black;
        [Tooltip("Colour of the translucent shadow outside the edge.")]
        [SerializeField] private Color shadowColour = Color.black;
        [SerializeField, Range(0f, 1f)] private float shadowAlpha = 0.3f;
        [Tooltip("Width in pixels of the shadow outside the edge pixel.")]
        [SerializeField, Range(0f, 4f)] private float shadowWidth = 1f;
        [Tooltip("Height in pixels of the tufts (spikes) that grow out of the edge, drawn with its outline and rim and always attached to it; 0 for none.")]
        [SerializeField, Range(0f, 6f)] private float tuftHeight;

        /// <summary>
        /// Creates default settings, for Unity serialization and the inspector list.
        /// </summary>
        public EdgeSettings()
        {
        }

        /// <summary>
        /// Creates settings with every value given, for tests.
        /// </summary>
        internal EdgeSettings(float rimWidth, Gradient rim, bool drawOutline, Color outlineColour, Color shadowColour, float shadowAlpha, float shadowWidth, float tuftHeight = 0f)
        {
            this.tuftHeight = tuftHeight;
            this.rimWidth = rimWidth;
            this.rim = rim;
            this.drawOutline = drawOutline;
            this.outlineColour = outlineColour;
            this.shadowColour = shadowColour;
            this.shadowAlpha = shadowAlpha;
            this.shadowWidth = shadowWidth;
        }

        public float RimWidth => rimWidth;
        public bool DrawOutline => drawOutline;
        public Color OutlineColour => outlineColour;
        public Color ShadowColour => shadowColour;
        public float ShadowAlpha => shadowAlpha;
        public float ShadowWidth => shadowWidth;
        public float TuftHeight => tuftHeight;

        /// <summary>
        /// The rim's colour at <paramref name="position"/> across it, from 0 at the edge to 1 at its inner end; alpha
        /// is how much of the interior it covers. Clear when the rim has no gradient.
        /// </summary>
        public Color EvaluateRim(float position)
        {
            return rim != null ? rim.Evaluate(position) : Color.clear;
        }
    }

    /// <summary>
    /// One ground material: its textures (32 px per cell, repeating every few cells), its shape's corner radii, how
    /// its chips or stones meet an edge, and its edge over any material without a pair override.
    /// </summary>
    [Serializable]
    public class MaterialSettings
    {
        [SerializeField] private string name = "Material";
        [Header("Textures")]
        [Tooltip("The interior: repeat x 32 px square, RGBA32 with sRGB off (the shader treats its bytes as sRGB itself).")]
        [SerializeField] private Texture2D albedo;
        [Tooltip("Optional: for every pixel, the offset to its chip's or stone's centre (see tools/terrain-art/materials.py). Same size and format as the albedo. Needed unless Cell Mode is None.")]
        [SerializeField] private Texture2D cellField;
        [Tooltip("Optional: the height the soft look uses (Phase 2). Same size and format as the albedo.")]
        [SerializeField] private Texture2D height;
        [Tooltip("Cells the textures cover before they repeat.")]
        [SerializeField, Min(1)] private int repeatCells = 4;
        [Header("Shape")]
        [Tooltip("Radius in pixels of an outer corner. Under half a cell, so edges stay straight across cell borders. Materials whose edges meet should share their radii, or the lower one shows at corners.")]
        [SerializeField, Range(0f, 15.5f)] private float outerRadius = 14f;
        [Tooltip("Radius in pixels of an inner corner's fillet. Under half a cell.")]
        [SerializeField, Range(0f, 15.5f)] private float innerRadius = 11f;
        [Header("Chips")]
        [SerializeField] private CellMode cellMode = CellMode.None;
        [Tooltip("Colour under the chips or stones, shown where one is left out or cut; the cell field's blue channel shades it per pixel. Must match materials.py's base colour.")]
        [SerializeField] private Color baseColour = Color.gray;
        [Tooltip("Whole chips: a chip is drawn only where all of it (as far as its field's reach) lies at least this many pixels inside the rim, else left out. Cut stones: a stone is drawn only where its centre lies at least this many pixels inside the rim; negative keeps stones whose centre lies in the rim, cut along its line.")]
        [SerializeField, Range(-4f, 6f)] private float keepMargin = 1f;
        [Tooltip("Cut stones: width in pixels of the base colour line between the rim and a stone cut along it.")]
        [SerializeField, Range(0f, 3f)] private float cutGap = 0.5f;
        [Tooltip("Cut stones: how much the light changes a stone's colour along its cut, in 0-255 steps.")]
        [SerializeField, Range(0f, 64f)] private float cutBevel = 18f;
        [Header("Edge")]
        [Tooltip("How this material's edge is drawn over any material, or over nothing, without a pair override.")]
        [SerializeField] private EdgeSettings edge = new();

        /// <summary>
        /// Creates a default material, for Unity serialization and the inspector list.
        /// </summary>
        public MaterialSettings()
        {
        }

        /// <summary>
        /// Creates a material with its textures, chips and edge, keeping the default corner radii, for tests.
        /// </summary>
        internal MaterialSettings(string name, Texture2D albedo, Texture2D cellField, int repeatCells, CellMode cellMode, Color baseColour, EdgeSettings edge)
        {
            this.name = name;
            this.albedo = albedo;
            this.cellField = cellField;
            this.repeatCells = repeatCells;
            this.cellMode = cellMode;
            this.baseColour = baseColour;
            this.edge = edge;
        }

        public string Name => name;
        public Texture2D Albedo => albedo;
        public Texture2D CellField => cellField;
        public Texture2D Height => height;
        public int RepeatCells => repeatCells;
        public int RepeatPixels => repeatCells * PixelsPerCell;
        public float OuterRadius => outerRadius;
        public float InnerRadius => innerRadius;
        public CellMode Cells => cellMode;
        public Color BaseColour => baseColour;
        public float KeepMargin => keepMargin;
        public float CutGap => cutGap;
        public float CutBevel => cutBevel;
        public EdgeSettings Edge => edge;
    }

    /// <summary>
    /// The edge an upper material draws over one lower material, or over nothing (<see cref="NoMaterial"/>), in place
    /// of the upper material's default edge. Materials are indices into <see cref="Materials"/>.
    /// </summary>
    [Serializable]
    public class PairOverride
    {
        [SerializeField, Min(0)] private int upper;
        [Tooltip("The material under it, or -1 for nothing (the map's edge).")]
        [SerializeField, Min(NoMaterial)] private int lower = NoMaterial;
        [SerializeField] private EdgeSettings edge = new();

        /// <summary>
        /// Creates an empty override, for Unity serialization and the inspector list.
        /// </summary>
        public PairOverride()
        {
        }

        /// <summary>
        /// Creates an override of <paramref name="upper"/> over <paramref name="lower"/>, for tests.
        /// </summary>
        internal PairOverride(int upper, int lower, EdgeSettings edge)
        {
            this.upper = upper;
            this.lower = lower;
            this.edge = edge;
        }

        public int Upper => upper;
        public int Lower => lower;
        public EdgeSettings Edge => edge;
    }

    [Tooltip("The terrain blending shader (Assets/Shaders/Terrain/TerrainBlend.shadergraph).")]
    [SerializeField] private Shader shader;
    [Header("Shared")]
    [Tooltip("Height in pixels of the smooth noise that moves every edge. Above 3 its slope can pinch off blobs.")]
    [SerializeField, Range(0f, 3f)] private float edgeNoise = 1f;
    [Tooltip("A lower layer is drawn under an upper one only where its edge lies at least this many pixels beyond the upper's; where edges coincide, only the upper one's edge shows.")]
    [SerializeField, Range(0.5f, 4f)] private float pairMargin = 1.5f;
    [Tooltip("Width in pixels over which an edge's pair settings blend where the pair margin is crossed.")]
    [SerializeField, Range(0f, 4f)] private float pairBlend = 1.5f;
    [SerializeField] private MaterialSettings[] materials = new MaterialSettings[0];
    [SerializeField] private PairOverride[] pairOverrides = new PairOverride[0];

    public Shader Shader => shader;
    public float EdgeNoise => edgeNoise;
    public float PairMargin => pairMargin;
    public float PairBlend => pairBlend;
    public IReadOnlyList<MaterialSettings> Materials => materials;
    public IReadOnlyList<PairOverride> PairOverrides => pairOverrides;

#if UNITY_EDITOR
    internal event Action Validated;
#endif

    /// <summary>
    /// The edge material <paramref name="upper"/> draws over <paramref name="lower"/> (<see cref="NoMaterial"/> for
    /// nothing): the pair's override when there is one, else the upper material's default edge.
    /// </summary>
    public EdgeSettings GetEdge(int upper, int lower)
    {
        foreach (PairOverride pair in pairOverrides)
        {
            if (pair != null && pair.Upper == upper && pair.Lower == lower)
            {
                return pair.Edge;
            }
        }

        return materials[upper].Edge;
    }

    /// <summary>
    /// Creates a style in memory with the given shader, materials and overrides and the default shared settings,
    /// for tests.
    /// </summary>
    internal static TerrainStyle Create(Shader shader, MaterialSettings[] materials, params PairOverride[] pairOverrides)
    {
        TerrainStyle style = CreateInstance<TerrainStyle>();
        style.shader = shader;
        style.materials = materials;
        style.pairOverrides = pairOverrides;
        return style;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Warns about pair overrides naming a material the style does not have, and tells renderers using this style to
    /// rebuild after an inspector change.
    /// </summary>
    private void OnValidate()
    {
        foreach (PairOverride pair in pairOverrides)
        {
            if (pair != null && (pair.Upper >= materials.Length || pair.Lower >= materials.Length))
            {
                Debug.LogWarning($"{name}: a pair override names material {Mathf.Max(pair.Upper, pair.Lower)}, but the style has {materials.Length}.", this);
            }
        }

        Validated?.Invoke();
    }
#endif
}
