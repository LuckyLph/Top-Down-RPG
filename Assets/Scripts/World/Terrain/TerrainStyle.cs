using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The look of the blended ground (Docs/TerrainBlending.md): the terrain shader, the settings every edge shares, and
/// the edges that pairs of an upper <see cref="TerrainMaterial"/> over a lower one (or over nothing) draw in place of
/// the upper material's default edge, in the crisp look and in the soft one, and the project-wide draw order of the
/// ground materials, bottom first, which every area's list of layers follows (Docs/TerrainTypes.md, "One shared draw
/// order"), so a pair is always the same material over the same one and its override keeps its meaning. A
/// <see cref="TerrainBlendRenderer"/>'s layers each name their own material. Read-only at runtime: the renderer builds
/// its runtime material and textures from it and never writes it.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Style", fileName = "TerrainStyle")]
public class TerrainStyle : ScriptableObject
{
    public const int PixelsPerCell = 32;

    /// <summary>
    /// How an upper material's edge is drawn over what lies under it. Crisp look, from the outside in: a translucent
    /// shadow, an outline (or the rim's outer colour) on the edge pixel, then the rim over the interior, with optional
    /// tufts growing out of the edge. Soft look: the same edge pixel and rim, unless a band of one colour replaces both,
    /// or, over a material with cut stones, whole stones make the edge. A seamless edge draws its edge pixel with the
    /// upper material's own interior, so with no outline, rim or shadow the upper material blends into a lower one of the
    /// same look (weeds over grass).
    /// </summary>
    [Serializable]
    public class EdgeSettings
    {
        public const float MaxSoftBandWidth = 4f;

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
        [Tooltip("Soft look: width in pixels of a band of one colour inside the edge, drawn in place of the outline and the rim; 0 keeps the outline and the rim as in the crisp look.")]
        [SerializeField, Range(0f, MaxSoftBandWidth)] private float softBandWidth;
        [Tooltip("Soft look: the band's colour. A material with a cell field shades it per pixel by the field's grit, as it shades its base colour.")]
        [SerializeField] private Color softBandColour = Color.gray;
        [Tooltip("Soft look, over a material with cut stones: each stone is drawn whole or not at all, kept where its centre, moved by its own height (the height texture's green channel times the material's height scale), lies the keep margin inside the edge, so whole cobbles make the edge. No band or rim is drawn there.")]
        [SerializeField] private bool softWholeStones;
        [Tooltip("Draw the edge pixel with this material's own interior instead of the outline or the rim's outer colour, so with no outline, rim or shadow the edge does not show: for a material that looks like the one under it, such as weeds over grass.")]
        [SerializeField] private bool seamless;

        /// <summary>
        /// Creates default settings, for Unity serialization and the inspector list.
        /// </summary>
        public EdgeSettings()
        {
        }

        /// <summary>
        /// Creates settings with every value given, for tests.
        /// </summary>
        internal EdgeSettings(float rimWidth, Gradient rim, bool drawOutline, Color outlineColour, Color shadowColour, float shadowAlpha, float shadowWidth, float tuftHeight = 0f, float softBandWidth = 0f, Color softBandColour = default, bool softWholeStones = false, bool seamless = false)
        {
            this.seamless = seamless;
            this.tuftHeight = tuftHeight;
            this.rimWidth = rimWidth;
            this.rim = rim;
            this.drawOutline = drawOutline;
            this.outlineColour = outlineColour;
            this.shadowColour = shadowColour;
            this.shadowAlpha = shadowAlpha;
            this.shadowWidth = shadowWidth;
            this.softBandWidth = softBandWidth;
            this.softBandColour = softBandColour;
            this.softWholeStones = softWholeStones;
        }

        public float RimWidth => rimWidth;
        public bool DrawOutline => drawOutline;
        public Color OutlineColour => outlineColour;
        public Color ShadowColour => shadowColour;
        public float ShadowAlpha => shadowAlpha;
        public float ShadowWidth => shadowWidth;
        public float TuftHeight => tuftHeight;
        public float SoftBandWidth => Mathf.Clamp(softBandWidth, 0f, MaxSoftBandWidth);
        public Color SoftBandColour => softBandColour;
        public bool SoftWholeStones => softWholeStones;
        public bool Seamless => seamless;

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
    /// The edge an upper material draws over one lower material, or over nothing (no lower material), in place of the
    /// upper material's default edge.
    /// </summary>
    [Serializable]
    public class PairOverride
    {
        [SerializeField] private TerrainMaterial upper;
        [Tooltip("The material under it, or none for nothing (the map's edge).")]
        [SerializeField] private TerrainMaterial lower;
        [SerializeField] private EdgeSettings edge = new();

        /// <summary>
        /// Creates an empty override, for Unity serialization and the inspector list.
        /// </summary>
        public PairOverride()
        {
        }

        /// <summary>
        /// Creates an override of <paramref name="upper"/> over <paramref name="lower"/> (null for nothing), for tests.
        /// </summary>
        internal PairOverride(TerrainMaterial upper, TerrainMaterial lower, EdgeSettings edge)
        {
            this.upper = upper;
            this.lower = lower;
            this.edge = edge;
        }

        public TerrainMaterial Upper => upper;
        public TerrainMaterial Lower => lower;
        public EdgeSettings Edge => edge;

        /// <summary>
        /// Whether a lower material was set but is gone (its asset deleted, or the object destroyed). Unity's == treats
        /// that like no lower material, which means over nothing, so the override would wrongly apply at the map's edge.
        /// </summary>
        public bool HasMissingLower()
        {
            return !ReferenceEquals(lower, null) && lower == null && lower.GetEntityId().IsValid();
        }
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
    [Tooltip("Edges drawn in place of the upper material's default edge, each for one upper material over one lower material or over nothing.")]
    [SerializeField] private PairOverride[] pairOverrides = new PairOverride[0];
    [Header("Layers")]
    [Tooltip("Every ground material, bottom first: the order areas draw their layers in. Each area lists a subsequence of it (Docs/TerrainTypes.md).")]
    [SerializeField] private TerrainMaterial[] layerOrder = new TerrainMaterial[0];

    public Shader Shader => shader;
    public float EdgeNoise => edgeNoise;
    public float PairMargin => pairMargin;
    public float PairBlend => pairBlend;
    public IReadOnlyList<PairOverride> PairOverrides => pairOverrides;
    public IReadOnlyList<TerrainMaterial> LayerOrder => layerOrder;

#if UNITY_EDITOR
    internal event Action Validated;
#endif

    /// <summary>
    /// The edge <paramref name="upper"/> draws over <paramref name="lower"/> (null for nothing): the pair's override
    /// when there is one, else the upper material's default edge. Overrides without an upper material, or whose lower
    /// material is missing, never apply.
    /// </summary>
    public EdgeSettings GetEdge(TerrainMaterial upper, TerrainMaterial lower)
    {
        if (upper == null)
        {
            throw new ArgumentNullException(nameof(upper));
        }

        foreach (PairOverride pair in pairOverrides)
        {
            if (pair != null && pair.Upper != null && !pair.HasMissingLower() && pair.Upper == upper && pair.Lower == lower)
            {
                return pair.Edge;
            }
        }

        return upper.Edge;
    }

    /// <summary>
    /// The place of <paramref name="material"/> in the layer order, bottom first, or -1 when it is not listed (or is
    /// null).
    /// </summary>
    public int LayerOrderIndexOf(TerrainMaterial material)
    {
        if (material == null)
        {
            return -1;
        }

        for (int i = 0; i < layerOrder.Length; i++)
        {
            if (layerOrder[i] == material)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Creates a style in memory with the given shader and overrides, the default shared settings and no layer order,
    /// for tests.
    /// </summary>
    internal static TerrainStyle Create(Shader shader, params PairOverride[] pairOverrides)
    {
        return Create(shader, new TerrainMaterial[0], pairOverrides);
    }

    /// <summary>
    /// Creates a style in memory with the given shader, layer order and overrides and the default shared settings, for
    /// tests.
    /// </summary>
    internal static TerrainStyle Create(Shader shader, TerrainMaterial[] layerOrder, params PairOverride[] pairOverrides)
    {
        TerrainStyle style = CreateInstance<TerrainStyle>();
        style.shader = shader;
        style.layerOrder = layerOrder;
        style.pairOverrides = pairOverrides;
        return style;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Warns about pair overrides without an upper material or whose lower material is missing, which never apply, and
    /// about empty or repeated entries in the layer order, and tells renderers using this style to rebuild after an
    /// inspector change.
    /// </summary>
    private void OnValidate()
    {
        for (int i = 0; i < pairOverrides.Length; i++)
        {
            if (pairOverrides[i] != null && pairOverrides[i].Upper == null)
            {
                Debug.LogWarning($"{name}: pair override {i} has no upper material, so it never applies.", this);
            }
            else if (pairOverrides[i] != null && pairOverrides[i].HasMissingLower())
            {
                Debug.LogWarning($"{name}: pair override {i}'s lower material is missing, so it never applies. Clear it to mean over nothing (the map's edge).", this);
            }
        }

        for (int i = 0; i < layerOrder.Length; i++)
        {
            if (layerOrder[i] == null)
            {
                Debug.LogWarning($"{name}: entry {i} of the layer order has no material.", this);
            }
            else if (LayerOrderIndexOf(layerOrder[i]) != i)
            {
                Debug.LogWarning($"{name}: {layerOrder[i].name} is listed twice in the layer order; only its first place counts.", this);
            }
        }

        Validated?.Invoke();
    }
#endif
}
