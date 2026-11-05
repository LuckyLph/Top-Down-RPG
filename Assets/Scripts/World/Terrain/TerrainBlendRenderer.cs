using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;
using VContainer;

/// <summary>
/// Draws an area's ground layers with one blending shader at 32 px per unit (Docs/TerrainBlending.md), in place of
/// their render tilemaps. Sits on the area's Grid and runs in edit mode too. From its layers (data tilemaps, bottom
/// first, each drawing a <see cref="TerrainMaterial"/>) and its <see cref="TerrainStyle"/> it builds a
/// <see cref="TerrainControlMap"/>, the texture arrays of the materials' albedo, cell field and height textures
/// (<see cref="TerrainMaterialArrays"/>: those of its style's layer order, shared with every renderer of the style, when
/// it is given <see cref="SharedTerrainArrays"/> holding all its materials, else its own with a slice per layer), the
/// lookup texture of the style and materials (<see cref="TerrainBlendLut"/>, with each layer's slice), a runtime
/// material, and one quad per chunk of cells over the control map's bounds on the Terrain sorting layer, each its own
/// renderer so off-screen chunks are culled (chunks the bounds no longer reach are removed); nothing is drawn where no
/// layer is filled. Everything is created at runtime, flagged so it is never saved, and destroyed (shared arrays
/// released) when disabled. Its build's parts each have a profiler marker under TerrainBlendRenderer.Build. Tile changes on
/// a layer (painting, the scene generator, <see cref="AreaTerrain"/> edits on the host or replicated on a client) patch
/// the control map at once, an undo or redo in the editor reads the layers again, and the texture is uploaded once
/// before the next render; nothing runs per frame while no cell changes. A brush's hover preview
/// (<see cref="SetPreview"/>, <see cref="ClearPreview"/>) shows cells of a layer filled or empty the same way without
/// touching its tilemap. Its <see cref="BlendMode"/>, a developer toggle, only switches the soft look's shader keyword
/// on the runtime material. Past <see cref="TerrainControlMap.NarrowLayers"/> layers it builds a wide control map,
/// which the shader reads in its wide variant. Animated materials (water) move in the shader from the frame's time,
/// with nothing done per frame here. The order of its layers is both the draw order and the navigation priority: a
/// layer whose tilemap has a <see cref="NavigationTerrainSource2D"/> must have its index as priority
/// (<see cref="NavigationPriorityOf"/>); its inspector keeps them in step, and a mismatch is logged as an error while
/// the ground is still drawn. Reads its style, materials, textures and navigation sources and never writes them. Its
/// chunks are a <see cref="TerrainChunkSet"/>, its build checks <see cref="TerrainBlendChecks"/>, and in the editor
/// <see cref="TerrainBlendRebuilds"/> decides when an inspector change rebuilds it. A chunk of a seamless world
/// (<see cref="SetChunk"/>) builds its control map over its own cells and a skirt around them, reads its neighbours'
/// border cells there (<see cref="SetSkirt"/>), draws quads over its own cells only, and takes its coast map from a
/// texture baked for it (Docs/WorldStreaming.md: Terrain). Ground of a glowing material (lava) lights what lies near it
/// at night through a Sprite Light 2D drawing a <see cref="TerrainGlowMap"/> (<see cref="GlowLight"/>, Docs/Lighting.md,
/// Phase 5), off until <see cref="PropLighting"/> burns it.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Grid))]
public class TerrainBlendRenderer : MonoBehaviour
{
    /// <summary>
    /// The look the ground is drawn in: crisp, designed rounded edges with rims and outlines, or soft, shapes blended
    /// from the cells' weights and the materials' heights.
    /// </summary>
    public enum BlendMode
    {
        Crisp,
        Soft
    }

    /// <summary>One ground layer: a data tilemap and the material it draws.</summary>
    [Serializable]
    public class Layer
    {
        [SerializeField] private Tilemap tilemap;
        [Tooltip("The material this layer draws.")]
        [SerializeField] private TerrainMaterial material;

        /// <summary>
        /// Creates an empty layer, for Unity serialization and the inspector list.
        /// </summary>
        public Layer()
        {
        }

        /// <summary>
        /// Creates a layer drawing <paramref name="tilemap"/>'s filled cells with <paramref name="material"/>.
        /// </summary>
        public Layer(Tilemap tilemap, TerrainMaterial material)
        {
            this.tilemap = tilemap;
            this.material = material;
        }

        public Tilemap Tilemap => tilemap;
        public TerrainMaterial Material => material;
    }

    private static readonly ProfilerMarker BuildMarker = new("TerrainBlendRenderer.Build");
    private static readonly ProfilerMarker ControlMapMarker = new("TerrainBlendRenderer.ControlMap");
    private static readonly ProfilerMarker CoastMapMarker = new("TerrainBlendRenderer.CoastMap");
    private static readonly ProfilerMarker GlowMapMarker = new("TerrainBlendRenderer.GlowMap");
    private static readonly ProfilerMarker ArraysMarker = new("TerrainBlendRenderer.Arrays");
    private static readonly ProfilerMarker ElevationMapMarker = new("TerrainBlendRenderer.ElevationMap");
    private static readonly ProfilerMarker LutMarker = new("TerrainBlendRenderer.Lut");
    private static readonly ProfilerMarker MaterialMarker = new("TerrainBlendRenderer.Material");
    private static readonly ProfilerMarker ChunksMarker = new("TerrainBlendRenderer.Chunks");
    private static readonly ProfilerMarker SkirtMarker = new("TerrainBlendRenderer.Skirt");
    private static readonly ProfilerMarker TeardownMarker = new("TerrainBlendRenderer.Teardown");
    private static readonly int ControlMapId = Shader.PropertyToID("_ControlMap");
    private static readonly int LutId = Shader.PropertyToID("_TerrainLut");
    private static readonly int AlbedoArrayId = Shader.PropertyToID("_AlbedoArray");
    private static readonly int FieldArrayId = Shader.PropertyToID("_FieldArray");
    private static readonly int HeightArrayId = Shader.PropertyToID("_HeightArray");
    private static readonly int ControlRectId = Shader.PropertyToID("_ControlRect");
    private static readonly int InfoId = Shader.PropertyToID("_TerrainInfo");
    private static readonly int CoastMapId = Shader.PropertyToID("_TerrainCoast");
    private static readonly int RaisedMapId = Shader.PropertyToID("_TerrainRaised");
    private static readonly int TurnMapId = Shader.PropertyToID("_TerrainTurns");
    internal const string WideKeywordName = "_TERRAIN_WIDE";
    private const string SoftKeywordName = "_TERRAIN_SOFT";
    private const string RaisedKeywordName = "_TERRAIN_RAISED";
    private static readonly MeshRenderer[] NoChunks = new MeshRenderer[0];
    private static readonly string[] GlowSortingLayers = { "Default", "Terrain", "Entities" };

    [Tooltip("The look shared by every material: the terrain shader, the shared edge settings and the pair rules. Read, never written.")]
    [SerializeField] private TerrainStyle style;
    [Tooltip("Ground data tilemaps, bottom layer first, each with the material it draws. Upper layers draw over lower ones, and a layer's place here is also its navigation priority: reordering the layers in this inspector sets each tilemap's Navigation Terrain Source 2D priority to its new index. At most 32; past 16 it builds a wide control map, which the shader draws in its slower wide variant.")]
    [SerializeField] private Layer[] layers = new Layer[0];
    [Tooltip("Optional: the tilemap whose cells are raised ground, drawn with a cliff face along their bottom edge, or a ramp where its tile is a ramp (Raised Ground Tile). Read, never written.")]
    [SerializeField] private Tilemap raisedTilemap;
    [Tooltip("Cells per side of each chunk quad; each chunk is culled on its own.")]
    [SerializeField, Min(1)] private int chunkSize = 16;
    [Tooltip("Sorting layer of the chunk quads. The walls' render tilemap (order 10) and the units and props draw over them.")]
    [SerializeField] private string sortingLayerName = "Terrain";
    [Tooltip("Order of the chunk quads on their sorting layer.")]
    [SerializeField] private int sortingOrder;
    [Tooltip("Developer toggle for the look: crisp (designed edges with rims and outlines) or soft (shapes blended from the cells and the materials' heights). Switching only flips the shader keyword, rebuilding nothing. Keep areas saved as Crisp.")]
    [SerializeField] private BlendMode mode;

    private Action<ScriptableRenderContext, List<Camera>> flushHandler;
    private Grid grid;
    private Tilemap[] layerTilemaps = new Tilemap[0];
    private TerrainControlMap controlMap;
    private TerrainCoastMap coastMap;
    private TerrainGlowMap glowMap;
    private Light2D glowLight;
    private TerrainMaterial glowMaterial;
    [NonSerialized] private uint glowBits;
    private TerrainElevationMap elevationMap;
    private Tilemap builtRaisedTilemap;
    [NonSerialized] private bool builtRaised;
    [NonSerialized] private bool elevationChanged;
    private TerrainTurnMap turnMap;
    [NonSerialized] private uint turnBits;
    [NonSerialized] private bool turnsChanged;
    [NonSerialized] private bool chunked;
    [NonSerialized] private RectInt chunkCells;
    [NonSerialized] private int skirtCells;
    [NonSerialized] private Texture2D chunkCoastMap;
    [NonSerialized] private bool coastBaked;
    [NonSerialized] private Texture2D chunkGlowMap;
    [NonSerialized] private RectInt chunkGlowArea;
    [NonSerialized] private bool glowBaked;
    [NonSerialized] private TerrainSkirtSource[] skirt = new TerrainSkirtSource[0];
    [NonSerialized] private TerrainMaterial[] builtMaterials = new TerrainMaterial[0];
    [NonSerialized] private readonly List<TerrainSkirtSource> unbuiltSkirt = new();
    private int waveLayer = -1;
    private uint bridgeBits;
    [NonSerialized] private SharedTerrainArrays sharedArrays;
    [NonSerialized] private TerrainMaterialArrays arrays;
    [NonSerialized] private SharedTerrainArrays arraysSource;
    private Texture2D lut;
    private Material material;
    private TerrainChunkSet chunkSet;
    [NonSerialized] private bool flushScheduled;
    [NonSerialized] private bool debugView;
    [NonSerialized] private bool timeFixed;
    [NonSerialized] private float fixedTime;
    [NonSerialized] private float animationFramesPerSecond;
    [NonSerialized] private bool previewGrewBounds;
    [NonSerialized] private bool alwaysWide;
    [NonSerialized] private bool live;
#if UNITY_EDITOR
    [NonSerialized] private bool enabledOnce;
    [NonSerialized] private TerrainBlendRebuilds rebuilds;
    [NonSerialized] private TerrainBlendSoftWarmUp softWarmUp;
#endif

    public TerrainStyle Style => style;
    public IReadOnlyList<Layer> Layers => layers;
    public Tilemap RaisedTilemap => raisedTilemap;

    /// <summary>
    /// The navigation priority of the layer at <paramref name="layerIndex"/>, bottom first: its index, so the layer
    /// drawn on top of a cell is also the one that decides the cell for navigation.
    /// </summary>
    public static int NavigationPriorityOf(int layerIndex)
    {
        return layerIndex;
    }

    /// <summary>
    /// The index of the first layer drawing <paramref name="tilemap"/>, bottom first, or -1 when no layer does.
    /// </summary>
    public int LayerIndexOf(Tilemap tilemap)
    {
        for (int i = 0; tilemap != null && layers != null && i < layers.Length; i++)
        {
            if (layers[i] != null && layers[i].Tilemap == tilemap)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The terrain renderer that draws <paramref name="tilemap"/> as a ground layer, with that layer's index in
    /// <paramref name="layer"/>: the nearest renderer on the tilemap or one of its parents (the area's Grid) that lists
    /// it, or null and -1. The one rule the renderer, its inspector, the navigation source and the scene writer share
    /// for which layer sets a tilemap's navigation priority.
    /// </summary>
    public static TerrainBlendRenderer FindLayerDrawing(Tilemap tilemap, out int layer)
    {
        TerrainBlendRenderer renderer = tilemap != null ? tilemap.GetComponentInParent<TerrainBlendRenderer>(true) : null;
        layer = renderer != null ? renderer.LayerIndexOf(tilemap) : -1;
        return layer >= 0 ? renderer : null;
    }

    /// <summary>
    /// The navigation source whose priority layer <paramref name="layer"/>'s place sets: the one on the layer's tilemap,
    /// when <see cref="FindLayerDrawing"/> finds this renderer and this layer for that tilemap (so a tilemap listed
    /// twice counts at its first layer); null otherwise.
    /// </summary>
    public NavigationTerrainSource2D NavigationSourceOf(int layer)
    {
        Tilemap tilemap = layers != null && layer >= 0 && layer < layers.Length ? layers[layer]?.Tilemap : null;
        if (tilemap == null || FindLayerDrawing(tilemap, out int drawnAt) != this || drawnAt != layer)
        {
            return null;
        }

        return tilemap.TryGetComponent(out NavigationTerrainSource2D source) ? source : null;
    }

    /// <summary>
    /// Whether every layer's navigation source (see <see cref="NavigationSourceOf"/>) has the layer's priority.
    /// </summary>
    public bool HasNavigationPrioritiesInStep()
    {
        for (int i = 0; layers != null && i < layers.Length; i++)
        {
            NavigationTerrainSource2D source = NavigationSourceOf(i);
            if (source != null && source.Priority != NavigationPriorityOf(i))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The look the ground is drawn in. Setting it only switches the soft look's keyword on the runtime material, at
    /// once and in edit mode too, rebuilding nothing and allocating nothing; it is not saved per machine. In the editor,
    /// a switch to soft before the soft look's shader variants are compiled keeps drawing crisp while they compile in
    /// the background and switches once they are (<see cref="TerrainBlendSoftWarmUp"/>). Set from code it does not mark
    /// the scene changed, but a later save of the scene keeps it.
    /// </summary>
    public BlendMode Mode
    {
        get => mode;
        set
        {
            mode = value;
            ApplyMode();
        }
    }

    internal TerrainControlMap ControlMap => controlMap;

    /// <summary>
    /// Whether it draws a chunk of a seamless world (<see cref="SetChunk"/>).
    /// </summary>
    internal bool IsChunk => chunked;
    internal RectInt ChunkCells => chunkCells;
    internal IReadOnlyList<TerrainSkirtSource> Skirt => skirt;

    /// <summary>
    /// The materials of the layers it built from, bottom first: its own layers', with any material its skirt shows
    /// that none of them draws at its place in the style's layer order.
    /// </summary>
    internal IReadOnlyList<TerrainMaterial> BuiltMaterials => builtMaterials;

    /// <summary>
    /// Whether it draws the coast map baked for its chunk instead of building one.
    /// </summary>
    internal bool IsCoastBaked => coastBaked;

    /// <summary>
    /// The coast map the runtime material reads: the baked one, or the one it builds; null when nothing is built.
    /// </summary>
    internal Texture CoastTexture => material != null ? material.GetTexture(CoastMapId) : null;

    /// <summary>
    /// The Sprite Light 2D that draws the glow of its glowing ground (lava) over its cells, off until
    /// <see cref="PropLighting"/> burns it; null when nothing it draws glows (Docs/Lighting.md, Phase 5).
    /// </summary>
    public Light2D GlowLight => glowLight;

    /// <summary>
    /// The strength its glow light shines at at night: its first glowing material's.
    /// </summary>
    public float GlowStrength => glowMaterial != null ? glowMaterial.GlowStrength : 0f;

    /// <summary>
    /// The glow map its glow light draws, null when none of its layers' materials glows.
    /// </summary>
    internal TerrainGlowMap GlowMap => glowMap;

    /// <summary>
    /// Whether its glow light draws the glow map baked for its chunk instead of one it builds (Docs/LightingGaps.md,
    /// Phase 5).
    /// </summary>
    internal bool IsGlowBaked => glowBaked;

    /// <summary>
    /// Whether it is built wide: an RGBA32 control map, read by the shader's wide variant (_TERRAIN_WIDE).
    /// </summary>
    internal bool IsWide => controlMap != null && controlMap.IsWide;
    internal Material RuntimeMaterial => material;
    internal Texture2DArray AlbedoArray => arrays?.Albedo;
    internal Texture2DArray FieldArray => arrays?.Field;
    internal Texture2DArray HeightArray => arrays?.Height;

    /// <summary>
    /// Whether it draws with the arrays shared by its style's renderers (<see cref="SharedTerrainArrays"/>) instead of
    /// its own.
    /// </summary>
    internal bool SharesArrays => arraysSource != null;
    internal Texture2D Lut => lut;
    internal TerrainElevationMap ElevationMap => elevationMap;
    internal TerrainTurnMap TurnMap => turnMap;
    internal IReadOnlyList<MeshRenderer> Chunks => chunkSet != null ? chunkSet.Renderers : NoChunks;
    internal bool IsFlushScheduled => flushScheduled;

    /// <summary>
    /// The frames per second of the fastest animated material among the layers it built from, 0 when none is animated
    /// or nothing is built; the editor repaints the Scene view at that rate so animated ground keeps moving there.
    /// </summary>
    internal float AnimationFramesPerSecond => animationFramesPerSecond;
    internal int HandledChangeCount { get; private set; }
    internal int RenderCallbackCount { get; private set; }
#if UNITY_EDITOR
    internal int HandledUndoCount { get; private set; }
    internal bool IsRebuildScheduled => Rebuilds.IsScheduled;

    private TerrainBlendRebuilds Rebuilds => rebuilds ??= new TerrainBlendRebuilds(RebuildIfEnabled);
    internal TerrainBlendSoftWarmUp SoftWarmUp => softWarmUp ??= new TerrainBlendSoftWarmUp(ApplyMode);

    /// <summary>
    /// Editor only: compiles the soft look's shader variants now, if the renderer draws soft or will, and switches to it,
    /// so a check that renders at once draws the soft look (<see cref="TerrainBlendSoftWarmUp.CompileNow"/>).
    /// </summary>
    internal void CompileModeNow()
    {
        if (material == null || mode != BlendMode.Soft)
        {
            return;
        }

        LocalKeyword softKeyword = new(material.shader, SoftKeywordName);
        if (softKeyword.isValid)
        {
            SoftWarmUp.CompileNow(material, softKeyword);
            ApplyMode();
        }
    }
#endif

    /// <summary>
    /// Has it draw with <paramref name="shared"/>'s arrays of its style's layer order, shared with the other renderers of
    /// the style, whenever they hold all its materials (null to build its own again); rebuilds when built. A chunk's
    /// scope injects the Gameplay scope's; elsewhere (the editor, tests, scratch scenes) it builds its own unless given.
    /// </summary>
    [Inject]
    public void Construct(SharedTerrainArrays shared)
    {
        sharedArrays = shared;
        if (controlMap != null)
        {
            Rebuild();
        }
    }

    /// <summary>
    /// Sets the style and layers in code (tests, editor tools) and rebuilds when enabled.
    /// </summary>
    public void Configure(TerrainStyle terrainStyle, params Layer[] groundLayers)
    {
        style = terrainStyle;
        layers = groundLayers;
        Rebuild();
    }

    /// <summary>
    /// Sets the style, the raised ground tilemap (null for none) and the layers in code (tests, editor tools) and
    /// rebuilds when enabled.
    /// </summary>
    public void Configure(TerrainStyle terrainStyle, Tilemap raised, params Layer[] groundLayers)
    {
        raisedTilemap = raised;
        Configure(terrainStyle, groundLayers);
    }

    /// <summary>
    /// The cells a chunk's control map covers: its own <paramref name="cells"/> and <paramref name="skirtWidth"/> cells
    /// around them, plus the control map's margin of one empty cell, so the coast map baked for the chunk lines up with
    /// it.
    /// </summary>
    public static RectInt ChunkControlArea(RectInt cells, int skirtWidth)
    {
        int around = Mathf.Max(0, skirtWidth) + TerrainControlMap.Margin;
        return new RectInt(cells.xMin - around, cells.yMin - around, cells.width + 2 * around, cells.height + 2 * around);
    }

    /// <summary>
    /// Makes it draw the chunk of a seamless world owning <paramref name="cells"/>: its control map covers those cells
    /// and <paramref name="skirtWidth"/> cells around them (<see cref="ChunkControlArea"/>), its quads only the cells,
    /// and its waves read <paramref name="coastMap"/>, the map baked for that area, when it is not null and has the
    /// area's size (otherwise it builds one from what it reads); its glow light draws <paramref name="glowMap"/>, the
    /// glow map baked for the chunk over <paramref name="glowArea"/>, when it is not null and has that area's size and
    /// lies in the chunk (otherwise it builds one, Docs/LightingGaps.md, Phase 5). Not saved; rebuilds when enabled.
    /// </summary>
    internal void SetChunk(RectInt cells, int skirtWidth, Texture2D coastMap, Texture2D glowMap = null, RectInt glowArea = default)
    {
        chunked = true;
        chunkCells = cells;
        skirtCells = Mathf.Max(0, skirtWidth);
        chunkCoastMap = coastMap;
        chunkGlowMap = glowMap;
        chunkGlowArea = glowArea;
        Rebuild();
    }

    /// <summary>
    /// Has it read <paramref name="sources"/>, the border cells of its loaded neighbours, too: a ground source counts
    /// for its layer of the same material, and a material none of its layers draws adds a layer for it at its place
    /// in the style's order, rebuilding it, once its source has a tile; a raised source counts as raised ground. Its
    /// own tilemaps are never written. Not saved. When built, rebuilds only if the layers or raised ground it needs
    /// changed, and otherwise reads its control map and elevation map again.
    /// </summary>
    internal void SetSkirt(IReadOnlyList<TerrainSkirtSource> sources)
    {
        int count = sources != null ? sources.Count : 0;
        skirt = new TerrainSkirtSource[count];
        for (int i = 0; i < count; i++)
        {
            skirt[i] = sources[i];
        }

        if (controlMap == null)
        {
            return;
        }

        if (!SameMaterials(LayerMaterials(out bool raised), builtMaterials) || builtRaised != raised)
        {
            Rebuild();
            return;
        }

        using (SkirtMarker.Auto())
        {
            BoundsInt before = controlMap.Bounds;
            controlMap.SetSources(ControlMapSources());
            controlMap.Rebuild();
            elevationChanged |= builtRaised;
            turnsChanged |= turnBits != 0u;
            FollowControlMapChange(before);
        }
    }

    /// <summary>
    /// Destroys everything built at runtime and builds it again from the layers and style, when enabled (once its own
    /// <c>OnEnable</c> has run, so a component enabled before it in the same load does not build it twice).
    /// </summary>
    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        if (!live || !isActiveAndEnabled)
        {
            return;
        }

        Teardown();
        Build();
    }

    /// <summary>
    /// Shows the topmost visible layer of every pixel as a pure colour instead of the ground (TBDebugColour in the
    /// terrain shader: a distinct colour per layer, each channel off, at half or full, so no two layers' colours are
    /// the same up to the lights' intensity), for blob checks. Not saved.
    /// </summary>
    internal void SetDebugView(bool on)
    {
        debugView = on;
        UpdateInfo();
    }

    /// <summary>
    /// Builds wide (<see cref="IsWide"/>) whatever the number of layers while <paramref name="on"/>, as it does past
    /// <see cref="TerrainControlMap.NarrowLayers"/> layers, so tests and checks can compare the wide path with the narrow
    /// one on the same layers; rebuilds when that changes. Not saved.
    /// </summary>
    internal void SetAlwaysWide(bool on)
    {
        if (alwaysWide == on)
        {
            return;
        }

        alwaysWide = on;
        Rebuild();
    }

    /// <summary>
    /// Draws animated materials at <paramref name="seconds"/> instead of the frame's time, so checks and captures of an
    /// animated ground are repeatable; null goes back to the frame's time. Not saved.
    /// </summary>
    internal void SetFixedTime(float? seconds)
    {
        timeFixed = seconds.HasValue;
        fixedTime = seconds ?? 0f;
        UpdateInfo();
    }

    /// <summary>
    /// Whether the renderer is built and draws <paramref name="tilemap"/> as one of the layers it built from. After an
    /// inspector change to its layers this follows the old layers until the rebuild.
    /// </summary>
    internal bool IsDrawing(Tilemap tilemap)
    {
        return controlMap != null && tilemap != null && BuiltLayerIndexOf(tilemap) >= 0;
    }

    /// <summary>
    /// Shows the ground layer drawn from <paramref name="tilemap"/> as filled or empty in <paramref name="cells"/>
    /// (their z is ignored) in place of any preview shown before, without changing the tilemap, navigation or anything
    /// saved, for a brush's hover in the editor. Adds chunks when filled preview cells grow the control map's bounds,
    /// and has the texture uploaded before the next render. The preview stays until <see cref="ClearPreview"/> or a
    /// rebuild. Returns false, changing nothing, when the renderer is not drawing the tilemap
    /// (<see cref="IsDrawing"/>). Allocates nothing for cells inside the bounds once the preview has held as many
    /// cells, as long as nothing else waits on the render callback the upload is scheduled on.
    /// </summary>
    internal bool SetPreview(Tilemap tilemap, IReadOnlyList<Vector3Int> cells, bool filled)
    {
        if (!IsDrawing(tilemap))
        {
            return false;
        }

        BoundsInt before = controlMap.Bounds;
        controlMap.ClearPreview();
        controlMap.SetPreview(BuiltLayerIndexOf(tilemap), cells, filled);
        previewGrewBounds |= controlMap.Bounds != before;
        FollowControlMapChange(before);
        return true;
    }

    /// <summary>
    /// Whether the renderer is built drawing raised ground and reads <paramref name="tilemap"/> as its raised tilemap.
    /// </summary>
    internal bool IsDrawingRaised(Tilemap tilemap)
    {
        return controlMap != null && builtRaised && tilemap != null && tilemap == builtRaisedTilemap;
    }

    /// <summary>
    /// Shows raised ground in <paramref name="cells"/> (their z is ignored) as <paramref name="kinds"/>
    /// (<see cref="RaisedGround"/>'s values, one per cell; null for level everywhere) in place of any raised preview shown
    /// before, without changing a tilemap, navigation or anything saved, for a brush's hover in the editor
    /// (Docs/KnownGaps.md, Phase 6): its elevation map is built again with them before the next render. Cells outside the
    /// control map's bounds show nothing. Returns false, changing nothing, when it draws no raised ground.
    /// </summary>
    internal bool SetRaisedPreview(IReadOnlyList<Vector3Int> cells, IReadOnlyList<byte> kinds)
    {
        if (controlMap == null || !builtRaised || elevationMap == null)
        {
            return false;
        }

        elevationMap.SetPreview(cells, kinds);
        elevationChanged = true;
        ScheduleFlush();
        return true;
    }

    /// <summary>
    /// Removes the raised preview, so the cells show their raised ground again from the next render.
    /// </summary>
    internal void ClearRaisedPreview()
    {
        if (elevationMap != null && elevationMap.ClearPreview())
        {
            elevationChanged = true;
            ScheduleFlush();
        }
    }

    /// <summary>
    /// Removes the preview, so the cells show their real bits again from the next render. When a preview grew the
    /// bounds since the last clear, reads the layers again so the bounds and chunks shrink back to the filled cells.
    /// Does nothing when no preview is shown and none grew the bounds.
    /// </summary>
    internal void ClearPreview()
    {
        if (controlMap == null || (!controlMap.HasPreview && !previewGrewBounds))
        {
            return;
        }

        BoundsInt before = controlMap.Bounds;
        controlMap.ClearPreview();
        if (previewGrewBounds)
        {
            previewGrewBounds = false;
            controlMap.Rebuild();
        }

        FollowControlMapChange(before);
    }

    /// <summary>
    /// Uploads the control map's pending changes now instead of before the next render, and stops waiting for it.
    /// </summary>
    internal void FlushPendingChanges()
    {
        if (flushScheduled)
        {
            RenderPipelineManager.beginContextRendering -= flushHandler;
            flushScheduled = false;
        }

        if (controlMap != null && controlMap.Apply())
        {
            UpdateControlRect();
            if (waveLayer >= 0 && !coastBaked)
            {
                coastMap.Build(controlMap, waveLayer, bridgeBits);
            }

            if (glowMap != null && !glowBaked)
            {
                BuildGlow();
            }

            elevationChanged |= builtRaised;
            turnsChanged |= turnBits != 0u;
        }

        if (elevationChanged && elevationMap != null)
        {
            elevationChanged = false;
            elevationMap.Build(controlMap, builtRaisedTilemap, skirt);
            material.SetTexture(RaisedMapId, elevationMap.Texture);
        }

        if (turnsChanged && turnMap != null)
        {
            turnsChanged = false;
            turnMap.Build(controlMap, layerTilemaps, turnBits, controlMap.Sources);
            material.SetTexture(TurnMapId, turnMap.Texture);
        }
    }

    /// <summary>
    /// Caches the grid the layers sit on.
    /// </summary>
    private void Awake()
    {
        grid = GetComponent<Grid>();
    }

    /// <summary>
    /// Starts following tile changes (and, in the editor, undo and redo) and builds everything.
    /// </summary>
    private void OnEnable()
    {
        Tilemap.tilemapPositionsChanged += HandleTilemapPositionsChanged;
#if UNITY_EDITOR
        UnityEditor.Undo.undoRedoPerformed += HandleUndoRedo;
#endif
        live = true;
        Build();
#if UNITY_EDITOR
        enabledOnce = true;
#endif
    }

    /// <summary>
    /// Stops following tile changes, undo and redo, and destroys everything built at runtime.
    /// </summary>
    private void OnDisable()
    {
        Tilemap.tilemapPositionsChanged -= HandleTilemapPositionsChanged;
#if UNITY_EDITOR
        UnityEditor.Undo.undoRedoPerformed -= HandleUndoRedo;
#endif
        live = false;
        Teardown();
    }

#if UNITY_EDITOR
    /// <summary>
    /// After an inspector change (or an undo of one), switches the look at once and rebuilds when anything the build
    /// reads changed, once the editor is done with the change (objects cannot be destroyed during validation), so
    /// flipping the mode rebuilds nothing. No rebuild while loading, before the first build.
    /// </summary>
    private void OnValidate()
    {
        ApplyMode();
        if (enabledOnce && Rebuilds.Changed(style, layers, raisedTilemap, chunkSize, sortingLayerName, sortingOrder))
        {
            Rebuilds.Schedule();
        }
    }

    /// <summary>
    /// Reads every layer again after an undo or redo, which restores tilemaps without reporting the cells it changed.
    /// </summary>
    private void HandleUndoRedo()
    {
        HandledUndoCount++;
        if (controlMap == null)
        {
            return;
        }

        BoundsInt before = controlMap.Bounds;
        controlMap.Rebuild();
        elevationChanged |= builtRaised;
        turnsChanged |= turnBits != 0u;
        FollowControlMapChange(before);
    }

    /// <summary>
    /// Runs the scheduled rebuild, if one is still waiting, unless the renderer has been destroyed or disabled since.
    /// The editor calls it once it is done with the change that scheduled it; tests call it directly.
    /// </summary>
    internal void RunScheduledRebuild()
    {
        Rebuilds.RunScheduled();
    }

    /// <summary>
    /// Rebuilds unless the renderer has been destroyed or disabled since a rebuild was scheduled.
    /// </summary>
    private void RebuildIfEnabled()
    {
        if (this != null && isActiveAndEnabled)
        {
            Rebuild();
        }
    }

    /// <summary>
    /// Rebuilds after a change to the navigation source of one of the layers' tilemaps (its inspector, a script through
    /// a serialized object, or one added later), so the build checks its priority again; the source calls it from its
    /// validation. Ignored while disabled or loading.
    /// </summary>
    internal void HandleNavigationSourceChanged()
    {
        if (enabledOnce && isActiveAndEnabled)
        {
            Rebuilds.Schedule();
        }
    }
#endif

    /// <summary>
    /// Builds the control map, texture arrays, lookup texture, runtime material and chunks; logs what is wrong and
    /// builds nothing when the style, layers or materials are not usable (<see cref="TerrainBlendChecks"/>). In the
    /// editor it follows the inspector changes of its style and of its layers' materials either way
    /// (<see cref="TerrainBlendRebuilds"/>), so fixing one rebuilds a renderer that could not build.
    /// </summary>
    private void Build()
    {
        using ProfilerMarker.AutoScope build = BuildMarker.Auto();
        if (grid == null)
        {
            grid = GetComponent<Grid>();
        }

#if UNITY_EDITOR
        Rebuilds.Remember(style, layers, raisedTilemap, chunkSize, sortingLayerName, sortingOrder);
        Rebuilds.Follow(style, layers);
#endif
        bool wide = alwaysWide || (layers != null && layers.Length > TerrainControlMap.NarrowLayers);
        if (!TerrainBlendChecks.IsUsable(this, grid, sortingLayerName, wide))
        {
            return;
        }

        TerrainMaterial[] materials = LayerMaterials(out bool raised);
        if (materials.Length > TerrainControlMap.MaxLayers)
        {
            Debug.LogError($"{name}: its layers and the materials its skirt shows make {materials.Length} layers, more than the {TerrainControlMap.MaxLayers} a renderer draws; the skirt's are left out.", this);
            skirt = new TerrainSkirtSource[0];
            materials = LayerMaterials(out raised);
        }

        int count = materials.Length;
        wide = alwaysWide || count > TerrainControlMap.NarrowLayers;
        layerTilemaps = new Tilemap[count];
        for (int i = 0, own = 0; i < count; i++)
        {
            bool isOwn = own < layers.Length && layers[own].Material == materials[i];
            layerTilemaps[i] = isOwn ? layers[own].Tilemap : null;
            own += isOwn ? 1 : 0;
        }

        builtMaterials = materials;

        animationFramesPerSecond = 0f;
        foreach (TerrainMaterial layerMaterial in materials)
        {
            animationFramesPerSecond = Mathf.Max(animationFramesPerSecond, layerMaterial.IsAnimated ? layerMaterial.FramesPerSecond : 0f);
        }

        RectInt? area = chunked ? ChunkControlArea(chunkCells, skirtCells) : null;
        using (ControlMapMarker.Auto())
        {
            controlMap = new TerrainControlMap(layerTilemaps, wide, $"{name} Terrain Control Map", area, ControlMapSources());
            controlMap.Apply();
        }

        previewGrewBounds = false;
        waveLayer = Array.FindIndex(materials, m => m.HasWaves);
        bridgeBits = TerrainBlendLut.BridgeBits(materials);
        coastBaked = waveLayer >= 0 && chunked && IsBakedFor(chunkCoastMap, area.Value);
        if (!coastBaked)
        {
            using (CoastMapMarker.Auto())
            {
                coastMap = new TerrainCoastMap($"{name} Terrain Coast Map");
                coastMap.Build(controlMap, waveLayer, bridgeBits);
            }
        }

        glowBits = 0u;
        glowMaterial = null;
        for (int i = 0; i < count; i++)
        {
            if (materials[i].Glows)
            {
                glowBits |= 1u << i;
                if (glowMaterial == null)
                {
                    glowMaterial = materials[i];
                }
            }
        }

        if (glowMaterial != null)
        {
            using (GlowMapMarker.Auto())
            {
                glowMap = new TerrainGlowMap($"{name} Terrain Glow");
                BuildGlow();
            }
        }

        int[] slices = new int[count];
        using (ArraysMarker.Auto())
        {
            arrays = sharedArrays != null ? sharedArrays.Acquire(style) : null;
            if (arrays != null && !arrays.HoldsAll(materials))
            {
                sharedArrays.Release(arrays);
                arrays = null;
            }

            arraysSource = arrays != null ? sharedArrays : null;
            arrays ??= new TerrainMaterialArrays(materials, $"{name} Terrain");
            for (int i = 0; i < count; i++)
            {
                slices[i] = arrays.SliceOf(materials[i]);
            }
        }

        builtRaisedTilemap = raisedTilemap;
        builtRaised = raised;
        using (ElevationMapMarker.Auto())
        {
            elevationMap = new TerrainElevationMap($"{name} Terrain Elevation Map");
            elevationMap.Build(controlMap, builtRaisedTilemap, skirt);
        }

        elevationChanged = false;
        turnBits = 0u;
        for (int i = 0; i < count; i++)
        {
            turnBits |= materials[i].TurnsToRuns ? 1u << i : 0u;
        }

        turnMap = new TerrainTurnMap($"{name} Terrain Turns");
        turnMap.Build(controlMap, layerTilemaps, turnBits, controlMap.Sources);
        turnsChanged = false;
        using (LutMarker.Auto())
        {
            lut = TerrainBlendLut.Create(style, materials, $"{name} Terrain Lut", builtRaised, slices);
        }

        using (MaterialMarker.Auto())
        {
            material = new Material(style.Shader) { name = $"{name} Terrain (runtime)", hideFlags = HideFlags.DontSave };
            material.SetTexture(ControlMapId, controlMap.Texture);
            material.SetTexture(LutId, lut);
            material.SetTexture(AlbedoArrayId, arrays.Albedo);
            material.SetTexture(FieldArrayId, arrays.Field);
            material.SetTexture(HeightArrayId, arrays.Height);
            material.SetTexture(CoastMapId, coastBaked ? chunkCoastMap : coastMap.Texture);
            material.SetTexture(RaisedMapId, elevationMap.Texture);
            material.SetTexture(TurnMapId, turnMap.Texture);
            ApplyMode();
            UpdateControlRect();
            UpdateInfo();
        }

        using (ChunksMarker.Auto())
        {
            chunkSet = new TerrainChunkSet(gameObject, grid, chunkSize);
            UpdateChunks();
        }
    }

    /// <summary>
    /// Switches the soft look's keyword on the runtime material to match the mode, the raised ground keyword to
    /// whether it was built with a raised tilemap, and the wide keyword to whether it was built wide; warns instead
    /// when the style's shader has no such keyword and it would be set. The keyword is looked up each time, which
    /// allocates nothing, since one kept from an earlier import of the shader no longer switches anything once the
    /// shader is reimported.
    /// </summary>
    private void ApplyMode()
    {
        if (material == null)
        {
            return;
        }

        LocalKeyword softKeyword = new(material.shader, SoftKeywordName);
        if (softKeyword.isValid)
        {
            bool soft = mode == BlendMode.Soft;
#if UNITY_EDITOR
            soft = soft && (material.IsKeywordEnabled(softKeyword) || SoftWarmUp.IsReady(material, softKeyword));
#endif
            material.SetKeyword(softKeyword, soft);
        }
        else if (mode == BlendMode.Soft)
        {
            Debug.LogWarning($"{name}: the terrain shader {material.shader.name} has no {SoftKeywordName} keyword, so the soft look draws as crisp.", this);
        }

        LocalKeyword raisedKeyword = new(material.shader, RaisedKeywordName);
        if (raisedKeyword.isValid)
        {
            material.SetKeyword(raisedKeyword, builtRaised);
        }
        else if (builtRaised)
        {
            Debug.LogWarning($"{name}: the terrain shader {material.shader.name} has no {RaisedKeywordName} keyword, so raised ground is drawn level.", this);
        }

        LocalKeyword wideKeyword = new(material.shader, WideKeywordName);
        if (wideKeyword.isValid)
        {
            material.SetKeyword(wideKeyword, IsWide);
        }
        else if (IsWide)
        {
            Debug.LogWarning($"{name}: the terrain shader {material.shader.name} has no {WideKeywordName} keyword, so its wide control map is read as a narrow one.", this);
        }
    }

    /// <summary>
    /// Fits the chunks to the control map's bounds (<see cref="TerrainChunkSet.Fit"/>): removes those the bounds no longer
    /// reach, as after an undo shrank them, and adds those growing bounds need.
    /// </summary>
    private void UpdateChunks()
    {
        BoundsInt bounds = controlMap.Bounds;
        bool empty = controlMap.IsEmpty;
        if (chunked && !empty)
        {
            int xMin = Mathf.Max(bounds.xMin, chunkCells.xMin);
            int yMin = Mathf.Max(bounds.yMin, chunkCells.yMin);
            int xMax = Mathf.Min(bounds.xMax, chunkCells.xMax);
            int yMax = Mathf.Min(bounds.yMax, chunkCells.yMax);
            empty = xMax <= xMin || yMax <= yMin;
            bounds = new BoundsInt(xMin, yMin, 0, Mathf.Max(0, xMax - xMin), Mathf.Max(0, yMax - yMin), 1);
        }

        uint layers = RenderingLayerMask.defaultRenderingLayerMask.value | (builtRaised ? style.CliffShadowLayers.value : 0u);
        chunkSet.Fit(bounds, empty, material, SortingLayer.NameToID(sortingLayerName), sortingOrder, layers);
    }

    /// <summary>
    /// The materials to build layers for, bottom first: each layer's, then each material of a ground source of the
    /// skirt that has a tile and that no layer draws, at its place in the style's layer order (after the layers when
    /// the order does not list it); and in <paramref name="raised"/> whether it draws raised ground: it has a raised
    /// tilemap, or a raised source of the skirt has a tile. Records the skirt's sources that a tile would make it
    /// rebuild for: the ground sources left without a layer, and without raised ground the raised sources.
    /// </summary>
    private TerrainMaterial[] LayerMaterials(out bool raised)
    {
        List<TerrainMaterial> materials = new(layers.Length);
        foreach (Layer layer in layers)
        {
            materials.Add(layer.Material);
        }

        unbuiltSkirt.Clear();
        raised = raisedTilemap != null;
        foreach (TerrainSkirtSource source in skirt)
        {
            if (source.IsRaised && source.Tilemap != null && !raised && source.HasAnyTile())
            {
                raised = true;
            }
        }

        foreach (TerrainSkirtSource source in skirt)
        {
            if (source.IsRaised && source.Tilemap != null && !raised)
            {
                unbuiltSkirt.Add(source);
            }

            if (source.IsRaised || source.Tilemap == null || materials.Contains(source.Material))
            {
                continue;
            }

            if (!source.HasAnyTile())
            {
                unbuiltSkirt.Add(source);
                continue;
            }

            int order = style != null ? style.LayerOrderIndexOf(source.Material) : -1;
            int at = materials.Count;
            for (int i = 0; order >= 0 && i < materials.Count; i++)
            {
                int other = style.LayerOrderIndexOf(materials[i]);
                if (other < 0 || other > order)
                {
                    at = i;
                    break;
                }
            }

            materials.Insert(at, source.Material);
        }

        return materials.ToArray();
    }

    /// <summary>
    /// The skirt's ground sources as control map sources, each counted for the first built layer drawing its
    /// material; those no built layer draws are left out (and wait among the unbuilt skirt).
    /// </summary>
    private List<TerrainControlMap.Source> ControlMapSources()
    {
        List<TerrainControlMap.Source> sources = new(skirt.Length);
        foreach (TerrainSkirtSource source in skirt)
        {
            int layer = source.IsRaised ? -1 : Array.IndexOf(builtMaterials, source.Material);
            if (layer >= 0 && source.Tilemap != null)
            {
                sources.Add(new TerrainControlMap.Source(source.Tilemap, layer, source.Cells));
            }
        }

        return sources;
    }

    /// <summary>
    /// Whether <paramref name="a"/> and <paramref name="b"/> list the same materials in the same order.
    /// </summary>
    private static bool SameMaterials(IReadOnlyList<TerrainMaterial> a, IReadOnlyList<TerrainMaterial> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="baked"/> is a glow map over <paramref name="area"/>, cells inside its chunk:
    /// <see cref="TerrainGlowMap.TexelsPerCell"/> texels per cell of it. A baked map that is not warns and is built
    /// instead.
    /// </summary>
    private bool IsGlowBakedFor(Texture2D baked, RectInt area)
    {
        if (baked == null)
        {
            return false;
        }

        if (area.width <= 0 || area.height <= 0 || area.xMin < chunkCells.xMin || area.yMin < chunkCells.yMin || area.xMax > chunkCells.xMax || area.yMax > chunkCells.yMax
            || baked.width != area.width * TerrainGlowMap.TexelsPerCell || baked.height != area.height * TerrainGlowMap.TexelsPerCell)
        {
            Debug.LogWarning($"{name}: the baked glow map {baked.name} ({baked.width} x {baked.height}) does not fit its area {area} in its chunk {chunkCells}, so the glow map is built instead; bake the world's glow maps again.", this);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="baked"/> is a coast map for a control map over <paramref name="area"/>:
    /// <see cref="TerrainCoastMap.TexelsPerCell"/> texels per cell of it.
    /// </summary>
    private bool IsBakedFor(Texture2D baked, RectInt area)
    {
        if (baked == null)
        {
            return false;
        }

        if (baked.width != area.width * TerrainCoastMap.TexelsPerCell || baked.height != area.height * TerrainCoastMap.TexelsPerCell)
        {
            Debug.LogWarning($"{name}: the baked coast map {baked.name} is {baked.width} x {baked.height}, not the {area.width * TerrainCoastMap.TexelsPerCell} x {area.height * TerrainCoastMap.TexelsPerCell} of its chunk's control map, so the coast map is built instead; bake the world's coast maps again.", this);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Builds the glow map over its cells (a world chunk's own, else the control map's bounds) from the cells of its
    /// glowing layers, within the reach and in the colour of the first of their materials, or takes the one baked for
    /// its chunk (<see cref="IsGlowBakedFor"/>), and fits the glow light to it: made, off and at no strength, when
    /// something glows and it has none, its cookie the map's sprite and its corner on the map's first cell; destroyed
    /// when nothing glows any more.
    /// </summary>
    private void BuildGlow()
    {
        BoundsInt bounds = controlMap.Bounds;
        RectInt area = chunked ? chunkCells : new RectInt(bounds.xMin, bounds.yMin, bounds.size.x, bounds.size.y);
        glowBaked = chunked && IsGlowBakedFor(chunkGlowMap, chunkGlowArea);
        if (glowBaked)
        {
            glowMap.UseBaked(chunkGlowMap, chunkGlowArea);
        }
        else if (!glowMap.Build(controlMap, glowBits, area, glowMaterial.GlowReach))
        {
            if (glowLight != null)
            {
                DestroyRuntime(glowLight.gameObject);
                glowLight = null;
            }

            return;
        }

        if (glowLight == null)
        {
            GameObject glowObject = new($"{name} Terrain Glow") { hideFlags = HideFlags.HideAndDontSave, layer = gameObject.layer };
            glowObject.SetActive(false);
            glowObject.transform.SetParent(transform, false);
            glowLight = glowObject.AddComponent<Light2D>();
            glowLight.lightCookieSprite = glowMap.Sprite;
            glowLight.lightType = Light2D.LightType.Sprite;
            glowLight.blendStyleIndex = 0;
            glowLight.shadowsEnabled = false;
            glowLight.intensity = 0f;
            int[] layerIds = new int[GlowSortingLayers.Length];
            for (int i = 0; i < layerIds.Length; i++)
            {
                layerIds[i] = SortingLayer.NameToID(GlowSortingLayers[i]);
            }

            glowLight.targetSortingLayers = layerIds;
            glowLight.enabled = false;
            glowObject.SetActive(true);
        }

        RectInt glowing = glowMap.Area;
        glowLight.transform.localPosition = grid.CellToLocal(new Vector3Int(glowing.xMin, glowing.yMin, 0));
        glowLight.lightCookieSprite = glowMap.Sprite;
        glowLight.color = glowMaterial.GlowColour;
    }

    /// <summary>
    /// Tells the material where the control map lies: the world position of its first cell and its size in cells.
    /// </summary>
    private void UpdateControlRect()
    {
        if (material == null)
        {
            return;
        }

        BoundsInt bounds = controlMap.Bounds;
        Vector3 origin = grid.CellToWorld(bounds.min);
        material.SetVector(ControlRectId, new Vector4(origin.x, origin.y, bounds.size.x, bounds.size.y));
    }

    /// <summary>
    /// Tells the material how many layers it was built with (the lookup texture's rows follow that count, not the
    /// layers the inspector may have changed since), whether to draw the debug view, and the fixed time animated
    /// materials are drawn at, if any.
    /// </summary>
    private void UpdateInfo()
    {
        if (material != null)
        {
            material.SetVector(InfoId, new Vector4(layerTilemaps.Length, debugView ? 1f : 0f, timeFixed ? 1f : 0f, fixedTime));
        }
    }

    /// <summary>
    /// Patches the control map for cells changed on one of the layers, adds chunks when its bounds grow, and has its
    /// texture uploaded before the next render. Allocates nothing for cells inside the bounds.
    /// </summary>
    private void HandleTilemapPositionsChanged(Tilemap tilemap, NativeArray<Vector3Int> positions)
    {
        HandledChangeCount++;
        if (controlMap == null)
        {
            return;
        }

        if (builtRaised && IsRaisedSource(tilemap))
        {
            elevationChanged = true;
            ScheduleFlush();
            return;
        }

        BoundsInt before = controlMap.Bounds;
        turnsChanged |= IsTurningSource(tilemap);
        if (controlMap.Refresh(tilemap, positions))
        {
            FollowControlMapChange(before);
            return;
        }

        if (turnsChanged)
        {
            ScheduleFlush();
        }

        if (FillsUnbuiltSkirt(tilemap, positions))
        {
            Rebuild();
        }
    }

    /// <summary>
    /// Whether <paramref name="tilemap"/> is a layer it built from whose material turns to its runs, or a skirt source
    /// of such a layer, so a change of its tiles' facing changes the turn map though no bit of the control map changes.
    /// </summary>
    private bool IsTurningSource(Tilemap tilemap)
    {
        if (turnBits == 0u)
        {
            return false;
        }

        int layer = BuiltLayerIndexOf(tilemap);
        if (layer >= 0)
        {
            return (turnBits >> layer & 1u) != 0u;
        }

        IReadOnlyList<TerrainControlMap.Source> sources = controlMap.Sources;
        for (int i = 0; i < sources.Count; i++)
        {
            if (sources[i].Tilemap == tilemap && (turnBits >> sources[i].Layer & 1u) != 0u)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="tilemap"/> is the raised tilemap it built from or a raised source of its skirt.
    /// </summary>
    private bool IsRaisedSource(Tilemap tilemap)
    {
        if (builtRaisedTilemap != null && tilemap == builtRaisedTilemap)
        {
            return true;
        }

        for (int i = 0; i < skirt.Length; i++)
        {
            if (skirt[i].IsRaised && skirt[i].Tilemap == tilemap)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a change to <paramref name="tilemap"/> at <paramref name="positions"/> put a tile in the cells of a
    /// source of the skirt it did not build for: a ground source whose material no built layer draws, which then needs
    /// a layer, or a raised source while it draws no raised ground.
    /// </summary>
    private bool FillsUnbuiltSkirt(Tilemap tilemap, NativeArray<Vector3Int> positions)
    {
        for (int s = 0; s < unbuiltSkirt.Count; s++)
        {
            if (unbuiltSkirt[s].Tilemap != tilemap)
            {
                continue;
            }

            for (int i = 0; i < positions.Length; i++)
            {
                if (positions[i].z == 0 && unbuiltSkirt[s].Contains(positions[i]) && tilemap.HasTile(positions[i]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The index of <paramref name="tilemap"/> among the layers the renderer built from, or -1.
    /// </summary>
    private int BuiltLayerIndexOf(Tilemap tilemap)
    {
        for (int i = 0; i < layerTilemaps.Length; i++)
        {
            if (layerTilemaps[i] == tilemap)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// After a change to the control map, adds and removes chunks when its bounds moved from
    /// <paramref name="before"/>, and has its texture uploaded before the next render when anything changed.
    /// </summary>
    private void FollowControlMapChange(BoundsInt before)
    {
        if (controlMap.Bounds != before)
        {
            UpdateChunks();
        }

        if (controlMap.HasPendingChanges || elevationChanged || turnsChanged)
        {
            ScheduleFlush();
        }
    }

    /// <summary>
    /// Has the control map's pending changes uploaded before the next render, once however often asked.
    /// </summary>
    private void ScheduleFlush()
    {
        if (flushScheduled)
        {
            return;
        }

        flushHandler ??= HandleBeginContextRendering;
        RenderPipelineManager.beginContextRendering += flushHandler;
        flushScheduled = true;
    }

    /// <summary>
    /// Uploads the control map's pending changes before the frame renders, once.
    /// </summary>
    private void HandleBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        RenderCallbackCount++;
        FlushPendingChanges();
    }

    /// <summary>
    /// Stops waiting to upload, stops following the style and materials, destroys the chunks, meshes, material and
    /// textures (the control map's and the lookup texture), and destroys its own arrays or releases the shared ones.
    /// </summary>
    private void Teardown()
    {
        using ProfilerMarker.AutoScope teardown = TeardownMarker.Auto();
        if (flushScheduled)
        {
            RenderPipelineManager.beginContextRendering -= flushHandler;
            flushScheduled = false;
        }

#if UNITY_EDITOR
        Rebuilds.StopFollowing();
        softWarmUp?.Stop();
#endif
        chunkSet?.Clear();
        chunkSet = null;
        controlMap?.Dispose();
        controlMap = null;
        coastMap?.Dispose();
        coastMap = null;
        if (glowLight != null)
        {
            DestroyRuntime(glowLight.gameObject);
        }

        glowLight = null;
        glowMap?.Dispose();
        glowMap = null;
        glowMaterial = null;
        glowBits = 0u;
        elevationMap?.Dispose();
        elevationMap = null;
        turnMap?.Dispose();
        turnMap = null;
        turnBits = 0u;
        turnsChanged = false;
        builtRaisedTilemap = null;
        builtRaised = false;
        coastBaked = false;
        glowBaked = false;
        builtMaterials = new TerrainMaterial[0];
        elevationChanged = false;
        waveLayer = -1;
        DestroyRuntime(material);
        DestroyRuntime(lut);
        material = null;
        lut = null;
        if (arraysSource != null)
        {
            arraysSource.Release(arrays);
        }
        else
        {
            arrays?.Dispose();
        }

        arrays = null;
        arraysSource = null;
        layerTilemaps = new Tilemap[0];
        animationFramesPerSecond = 0f;
    }

    /// <summary>
    /// Destroys an object made at runtime, immediately outside play mode.
    /// </summary>
    private static void DestroyRuntime(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
