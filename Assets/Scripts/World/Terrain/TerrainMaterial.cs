using System;
using UnityEngine;

/// <summary>
/// One ground material a <see cref="TerrainBlendRenderer"/> layer draws (Docs/TerrainBlending.md): its textures (32 px
/// per cell, repeating every few cells), its shape's corner radii, how its chips or stones meet an edge, and its edge
/// over any material the <see cref="TerrainStyle"/> has no pair override for. Read-only at runtime: renderers copy its
/// textures and settings and never write it. In the editor, a terrain material recipe generates its textures and writes
/// their base colour, repeat and cell mode. In the soft look its height texture, times its height scale, moves its
/// edges. An animated material (water) lights its glint and sparkle pixels in turn and swells its rims in and out, in
/// the shader alone, at its frames per second; its edges never move. A swaying material (grass) leans the pixels its
/// height texture weighs, the blades of its clumps, in a wind that rolls across the ground, in the shader too, and with whole
/// clumps each weed leans as one in the gust at its foot while the ground inside its box stays put. A sunken material
/// (water) lies below the ground
/// around it: along its edges that face up the screen over another material, that material's bank shows inside its
/// edge, in the other material's bank colour (grass shows the dirt under it), and casts a shadow on it. A bridge
/// (planks) lies on what is painted under it, so the layers under it do not go on beneath it, and a material that
/// turns to its runs turns its textures a quarter where its layer runs left and right, so its boards lie across the run.
/// A glowing material (lava) lights the ground, props and units near it at night, in its glow colour, through its
/// renderer's glow light (Docs/Lighting.md, Phase 5). A material with relief (cobbles) has its stones' bevels, painted
/// lit from the top left, turned by the terrain shader to the sun and to each local light (Phase 11).
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Terrain Material", fileName = "TerrainMaterial")]
public class TerrainMaterial : ScriptableObject
{
    public const float MaxHeightScale = 2.5f;
    public const float DefaultHeightScale = 2f;
    public const float MaxFramesPerSecond = 30f;
    public const float MinPeriod = 0.1f;
    public const float MaxSinkDepth = 6f;
    public const float MaxSwayAmplitude = 3f;
    public const float MaxSwash = 12f;
    public const float MaxWaveSpacing = 128f;
    public const float MaxGlowReach = 4f;

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
    /// How a material's stones are bevelled (Docs/Lighting.md, Phase 11), as its recipe paints them lit from the top
    /// left, so the terrain shader can take the painted bevel away and draw it again from the side of the sun and of
    /// each local light: how much a light changes a stone along its edge and away from its centre, over what widths, how
    /// much darker the side facing away is and from how far away, and the mortar's width inside every stone's cell
    /// edge. The direction each pixel's bevel faces is in the height texture's alpha. All zero (the default) for a
    /// material without relief. Written by the material's recipe.
    /// </summary>
    [Serializable]
    public struct BevelRelief
    {
        [Tooltip("How much a light from the side a stone's edge faces changes its colour along that edge, in 0-255 steps; 0, with no radial, for no relief.")]
        [SerializeField, Min(0f)] private float bevel;
        [Tooltip("Width in pixels of the bevel inside the mortar.")]
        [SerializeField, Min(0f)] private float bevelWidth;
        [Tooltip("How much the light changes a stone's colour away from its centre, in 0-255 steps, for a rounded look.")]
        [SerializeField, Min(0f)] private float radial;
        [Tooltip("Distance in pixels from a stone's centre at which the rounded look is strongest.")]
        [SerializeField, Min(0f)] private float radialRadius;
        [Tooltip("How much darker the bevel is on a stone's side that faces away from the light, in 0-255 steps.")]
        [SerializeField, Min(0f)] private float shadedSide;
        [Tooltip("How far a side must face away from the light to be shaded, from 0 (sideways) to 1 (straight away).")]
        [SerializeField, Range(0f, 1f)] private float shadedSideFrom;
        [Tooltip("Pixels this close to their stone's cell edge, in pixels, are mortar, where the bevel starts.")]
        [SerializeField, Min(0f)] private float mortarWidth;

        /// <summary>
        /// A stone material's relief with these settings.
        /// </summary>
        public BevelRelief(float bevel, float bevelWidth, float radial, float radialRadius, float shadedSide, float shadedSideFrom, float mortarWidth)
        {
            this.bevel = bevel;
            this.bevelWidth = bevelWidth;
            this.radial = radial;
            this.radialRadius = radialRadius;
            this.shadedSide = shadedSide;
            this.shadedSideFrom = shadedSideFrom;
            this.mortarWidth = mortarWidth;
        }

        public float Bevel => bevel;
        public float BevelWidth => bevelWidth;
        public float Radial => radial;
        public float RadialRadius => radialRadius;
        public float ShadedSide => shadedSide;
        public float ShadedSideFrom => shadedSideFrom;
        public float MortarWidth => mortarWidth;
        public bool HasRelief => bevel > 0f || radial > 0f;
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
    [Tooltip("How many pixels past its edge this material washes out over the ground it meets at the top of each swell, as a wave runs up a beach and back: its rim drawn outwards, the rim's first colour (the foam) at the wave's front, on the swell's timing; none over nothing or along a seamless pair. 0 for none.")]
    [SerializeField, Range(0f, MaxSwash)] private float swash;
    [Header("Waves")]
    [Tooltip("Pixels between two waves rolling in toward the shore over this material, which needs frames per second above 0; 0 for none. The waves follow the distance to the coast (TerrainCoastMap), so they bend to meet it, and time the swash: each wave washes up the shore as it arrives.")]
    [SerializeField, Range(0f, MaxWaveSpacing)] private float waveSpacing;
    [Tooltip("Seconds between two waves arriving at the same spot.")]
    [SerializeField, Range(MinPeriod, 30f)] private float wavePeriod = 3.2f;
    [Tooltip("Pixels from a wave's crest back out to sea over which its band of lighter water fades out.")]
    [SerializeField, Range(1f, 32f)] private float waveWidth = 10f;
    [Tooltip("Degrees the waves' fronts turn away from the coast's line, so they reach a straight shore at one end first and run along it: this much along a coast running east to west or north to south, up to 1.4 times it along a diagonal one and none along the other diagonal.")]
    [SerializeField, Range(-45f, 45f)] private float waveAngle = 8.5f;
    [Tooltip("Pixels from the coast within which every wave runs whole; further out they break into stretches that grow and fade.")]
    [SerializeField, Range(0f, 256f)] private float waveWholeWithin = 40f;
    [Tooltip("How much of each wave shows out at sea, where waves break into stretches that grow and fade: higher gives longer stretches, lower shorter and fewer.")]
    [SerializeField, Range(-1f, 1f)] private float waveStretch = 0.2f;
    [Tooltip("Colour of a wave's band of lighter water, at its crest; it fades into the material behind it.")]
    [SerializeField] private Color waveColour = new(0.17f, 0.37f, 0.59f, 1f);
    [Tooltip("Colour of a wave's crest, a broken line one pixel thick along its shoreward face.")]
    [SerializeField] private Color waveCrestColour = new(0.55f, 0.72f, 0.85f, 1f);
    [Tooltip("Colour of the trough just ahead of a wave, drawn at half strength one pixel thick.")]
    [SerializeField] private Color waveTroughColour = new(0.05f, 0.16f, 0.31f, 1f);
    [Tooltip("Colour of the swash's foam at its front, as a wave runs up the shore.")]
    [SerializeField] private Color washFoamColour = new(0.94f, 0.96f, 0.97f, 1f);
    [Tooltip("Colour of the foam's back pixel, behind its front.")]
    [SerializeField] private Color washFoamShadeColour = new(0.84f, 0.89f, 0.93f, 1f);
    [Tooltip("Colour of the film of water behind the foam, near the front.")]
    [SerializeField] private Color washFilmColour = new(0.59f, 0.75f, 0.87f, 1f);
    [Tooltip("Colour of the film of water further behind the foam, toward the sea.")]
    [SerializeField] private Color washFilmBackColour = new(0.44f, 0.65f, 0.8f, 1f);
    [Tooltip("How much darker the shore is just after the swash pulls back off it, drying over a second.")]
    [SerializeField, Range(0f, 1f)] private float washWet = 0.15f;
    [Header("Sway")]
    [Tooltip("How many pixels the tips of this material's weeds lean at the top of a gust; 0 for none. A pixel leans by this times its weight in the height texture's blue channel (written by a swaying recipe, the weeds recipe that draws the grass's clumps), which then holds no glints or sparkles. Needs frames per second above 0.")]
    [SerializeField, Range(0f, MaxSwayAmplitude)] private float swayAmplitude;
    [Tooltip("Seconds of one gust, leaning and back.")]
    [SerializeField, Range(MinPeriod, 30f)] private float swayPeriod = 2.5f;
    [Tooltip("Cells a gust travels in one period: the wind rolls across the ground this many cells per period, from the bottom left to the top right.")]
    [SerializeField, Range(1f, 64f)] private float gustLength = 12f;
    [Tooltip("Each weed leans whole: its gust is taken at the cell of its foot, so a weed that crosses a cell row does not shear, and only its own pixels move: where a leaning pixel would show the ground inside its box (a texel the cell field holds unchanged), the ground stays where it is instead of sliding sideways with the weed.")]
    [SerializeField] private bool wholeClumps;
    [Header("Sunken")]
    [Tooltip("How many pixels of bank show inside this material's edges where they face straight up the screen, as if it lay that far below the ground around it; less as an edge turns sideways, none along edges facing down or over nothing. The bank is the bank colour of the material it meets, its lowest pixel, at the waterline, darker. 0 for a material level with the ground.")]
    [SerializeField, Range(0f, MaxSinkDepth)] private float sinkDepth;
    [Tooltip("How much darker the bank's shadow makes this material just below the bank, over as many pixels as the bank is high.")]
    [SerializeField, Range(0f, 1f)] private float bankShadow = 0.25f;
    [Tooltip("Colour of this material's face where a sunken material (water) cuts into it, the bank above a pond's top edge: what the ground shows when cut, such as the dirt under grass.")]
    [SerializeField] private Color bankColour = new(0.38f, 0.29f, 0.2f, 1f);
    [Header("Reflection")]
    [Tooltip("How strongly it reflects the lights (Docs/Lighting.md, Phase 10): a light's glitter path down it toward the camera from a light above it on the screen, and its glints catching the light falling on them; 1 for open water, 0 for a material that reflects nothing.")]
    [SerializeField, Range(0f, 1f)] private float reflection;
    [Header("Relief")]
    [Tooltip("How its stones are bevelled, so the terrain shader turns their painted bevels to the sun and the local lights (Docs/Lighting.md, Phase 11); all zero for none. Written by the material's recipe with its textures.")]
    [SerializeField] private BevelRelief relief;
    [Header("Glow")]
    [Tooltip("How strongly it lights what lies near it at night, at its own cells; 0 for a material that does not glow.")]
    [SerializeField, Min(0f)] private float glowStrength;
    [Tooltip("How far its glow reaches from its cells, in cells, at most a chunk's skirt, so the glow runs on across chunk borders.")]
    [SerializeField, Range(0f, MaxGlowReach)] private float glowReach = 2.5f;
    [Tooltip("The colour of its glow.")]
    [SerializeField] private Color glowColour = new(1f, 0.51f, 0.2f, 1f);
    [Header("Placement")]
    [Tooltip("Lies on whatever is painted under it, like a dock or a bridge, instead of the layers under it going on beneath it: a lower layer's shape counts this layer's cells only where that layer (or another above it that is no bridge) is painted, so beside it each lower layer keeps its own edge. Without it, the water under a dock's sea end would also reach under its beach end and round off there.")]
    [SerializeField] private bool bridge;
    [Tooltip("Turns the textures a quarter in a cell where this layer runs further left and right than up and down (up to 3 cells each way; a tie keeps them), so what runs along the texture's x, such as a board, lies across a path of this layer whichever way it goes.")]
    [SerializeField] private bool turnToRuns;
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
    public float Swash => Mathf.Clamp(swash, 0f, MaxSwash);
    public bool HasWaves => IsAnimated && waveSpacing > 0f;
    public float WaveSpacing => Mathf.Clamp(waveSpacing, 0f, MaxWaveSpacing);
    public float WavePeriod => Mathf.Max(wavePeriod, MinPeriod);
    public float WaveWidth => Mathf.Max(waveWidth, 1f);
    public float WaveAngle => Mathf.Clamp(waveAngle, -45f, 45f);
    public float WaveWholeWithin => Mathf.Max(waveWholeWithin, 0f);
    public float WaveStretch => Mathf.Clamp(waveStretch, -1f, 1f);
    public Color WaveColour => waveColour;
    public Color WaveCrestColour => waveCrestColour;
    public Color WaveTroughColour => waveTroughColour;
    public Color WashFoamColour => washFoamColour;
    public Color WashFoamShadeColour => washFoamShadeColour;
    public Color WashFilmColour => washFilmColour;
    public Color WashFilmBackColour => washFilmBackColour;
    public float WashWet => Mathf.Clamp01(washWet);
    public bool Sways => IsAnimated && swayAmplitude > 0f;
    public float SwayAmplitude => Mathf.Clamp(swayAmplitude, 0f, MaxSwayAmplitude);
    public float SwayPeriod => Mathf.Max(swayPeriod, MinPeriod);
    public float GustLength => Mathf.Max(gustLength, 1f);
    public bool WholeClumps => wholeClumps;
    public float SinkDepth => Mathf.Clamp(sinkDepth, 0f, MaxSinkDepth);
    public float BankShadow => Mathf.Clamp01(bankShadow);
    public Color BankColour => bankColour;
    public float Reflection => Mathf.Clamp01(reflection);
    public BevelRelief Relief => relief;
    public bool HasRelief => relief.HasRelief && cellMode == CellMode.CutStones && height != null;
    public bool Glows => glowStrength > 0f && glowReach > 0f;
    public float GlowStrength => Mathf.Max(glowStrength, 0f);
    public float GlowReach => Mathf.Clamp(glowReach, 0f, MaxGlowReach);
    public Color GlowColour => glowColour;
    public bool Bridge => bridge;
    public bool TurnsToRuns => turnToRuns;
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

    /// <summary>
    /// Makes it glow at <paramref name="strength"/> over <paramref name="reach"/> cells in <paramref name="colour"/>, for
    /// tests.
    /// </summary>
    internal void SetGlow(float strength, float reach, Color colour)
    {
        glowStrength = strength;
        glowReach = reach;
        glowColour = colour;
    }

    /// <summary>
    /// Makes it reflect the lights at <paramref name="strength"/>, for tests.
    /// </summary>
    internal void SetReflection(float strength)
    {
        reflection = strength;
    }

    /// <summary>
    /// Gives its stones <paramref name="bevels"/>, for tests.
    /// </summary>
    internal void SetRelief(BevelRelief bevels)
    {
        relief = bevels;
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
