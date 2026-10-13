using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

/// <summary>
/// Draws an area's ground layers with one blending shader at 32 px per unit (Docs/TerrainBlending.md), in place of
/// their render tilemaps. Sits on the area's Grid and runs in edit mode too. From its layers (data tilemaps, bottom
/// first, each drawing a <see cref="TerrainMaterial"/>) and its <see cref="TerrainStyle"/> it builds a
/// <see cref="TerrainControlMap"/>, texture arrays of the materials' albedo, cell field and height textures (a slice per
/// layer), the lookup texture of the style and materials (<see cref="TerrainBlendLut"/>), a runtime material, and one
/// quad per chunk of cells over the control map's bounds on the Terrain sorting layer, each its own renderer so
/// off-screen chunks are culled (chunks the bounds no longer reach are removed); nothing is drawn where no layer is
/// filled. Everything is created at runtime, flagged so it is never saved, and destroyed when disabled. Tile changes on
/// a layer (painting, the scene generator, <see cref="AreaTerrain"/> edits on the host or replicated on a client) patch
/// the control map at once, an undo or redo in the editor reads the layers again, and the texture is uploaded once
/// before the next render; nothing runs per frame while no cell changes. A brush's hover preview
/// (<see cref="SetPreview"/>, <see cref="ClearPreview"/>) shows cells of a layer filled or empty the same way without
/// touching its tilemap. Its <see cref="BlendMode"/>, a developer toggle, only switches the soft look's shader keyword
/// on the runtime material. Animated materials (water) move in the shader from the frame's time, with nothing done
/// per frame here. The order of its layers is both the draw order and the navigation priority: a layer whose
/// tilemap has a <see cref="NavigationTerrainSource2D"/> must have its index as priority
/// (<see cref="NavigationPriorityOf"/>); its inspector keeps them in step, and a mismatch is logged as an error while
/// the ground is still drawn. Reads its style, materials, textures and navigation sources and never writes them.
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

    /// <summary>One chunk quad: its renderer and its mesh.</summary>
    private sealed class Chunk
    {
        /// <summary>
        /// Holds <paramref name="renderer"/> and <paramref name="mesh"/>.
        /// </summary>
        public Chunk(MeshRenderer renderer, Mesh mesh)
        {
            Renderer = renderer;
            Mesh = mesh;
        }

        public MeshRenderer Renderer { get; }
        public Mesh Mesh { get; }
    }

    private static readonly int ControlMapId = Shader.PropertyToID("_ControlMap");
    private static readonly int LutId = Shader.PropertyToID("_TerrainLut");
    private static readonly int AlbedoArrayId = Shader.PropertyToID("_AlbedoArray");
    private static readonly int FieldArrayId = Shader.PropertyToID("_FieldArray");
    private static readonly int HeightArrayId = Shader.PropertyToID("_HeightArray");
    private static readonly int ControlRectId = Shader.PropertyToID("_ControlRect");
    private static readonly int InfoId = Shader.PropertyToID("_TerrainInfo");
    private const string SoftKeywordName = "_TERRAIN_SOFT";

    [Tooltip("The look shared by every material: the terrain shader, the shared edge settings and the pair rules. Read, never written.")]
    [SerializeField] private TerrainStyle style;
    [Tooltip("Ground data tilemaps, bottom layer first, each with the material it draws. Upper layers draw over lower ones, and a layer's place here is also its navigation priority: reordering the layers in this inspector sets each tilemap's Navigation Terrain Source 2D priority to its new index. At most 16.")]
    [SerializeField] private Layer[] layers = new Layer[0];
    [Tooltip("Cells per side of each chunk quad; each chunk is culled on its own.")]
    [SerializeField, Min(1)] private int chunkSize = 16;
    [Tooltip("Sorting layer of the chunk quads. The walls' render tilemap (order 10) and the units and props draw over them.")]
    [SerializeField] private string sortingLayerName = "Terrain";
    [Tooltip("Order of the chunk quads on their sorting layer.")]
    [SerializeField] private int sortingOrder;
    [Tooltip("Developer toggle for the look: crisp (designed edges with rims and outlines) or soft (shapes blended from the cells and the materials' heights). Switching only flips the shader keyword, rebuilding nothing. Keep areas saved as Crisp.")]
    [SerializeField] private BlendMode mode;

    private readonly Dictionary<Vector2Int, Chunk> chunksByKey = new();
    private readonly List<MeshRenderer> chunks = new();
    private readonly List<Vector2Int> staleChunks = new();
    private Action<ScriptableRenderContext, List<Camera>> flushHandler;
    private Grid grid;
    private Tilemap[] layerTilemaps = new Tilemap[0];
    private TerrainControlMap controlMap;
    private Texture2DArray albedoArray;
    private Texture2DArray fieldArray;
    private Texture2DArray heightArray;
    private Texture2D lut;
    private Material material;
    [NonSerialized] private bool flushScheduled;
    [NonSerialized] private bool debugView;
    [NonSerialized] private bool timeFixed;
    [NonSerialized] private float fixedTime;
    [NonSerialized] private float animationFramesPerSecond;
    [NonSerialized] private bool previewGrewBounds;
#if UNITY_EDITOR
    [NonSerialized] private readonly List<TerrainMaterial> subscribedMaterials = new();
    [NonSerialized] private bool enabledOnce;
    [NonSerialized] private TerrainStyle subscribedStyle;
    [NonSerialized] private bool rebuildScheduled;
    [NonSerialized] private TerrainStyle builtStyle;
    [NonSerialized] private readonly List<Tilemap> builtTilemaps = new();
    [NonSerialized] private readonly List<TerrainMaterial> builtMaterials = new();
    [NonSerialized] private int builtChunkSize;
    [NonSerialized] private string builtSortingLayerName;
    [NonSerialized] private int builtSortingOrder;
#endif

    public TerrainStyle Style => style;
    public IReadOnlyList<Layer> Layers => layers;

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
    /// once and in edit mode too, rebuilding nothing and allocating nothing; it is not saved per machine. Set from code
    /// it does not mark the scene changed, but a later save of the scene keeps it.
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
    internal Material RuntimeMaterial => material;
    internal Texture2DArray AlbedoArray => albedoArray;
    internal Texture2DArray FieldArray => fieldArray;
    internal Texture2DArray HeightArray => heightArray;
    internal Texture2D Lut => lut;
    internal IReadOnlyList<MeshRenderer> Chunks => chunks;
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
    internal bool IsRebuildScheduled => rebuildScheduled;
#endif

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
    /// Destroys everything built at runtime and builds it again from the layers and style, when enabled.
    /// </summary>
    [ContextMenu("Rebuild")]
    public void Rebuild()
    {
        if (!isActiveAndEnabled)
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
        if (enabledOnce && BuildInputsChanged())
        {
            ScheduleRebuild();
        }
    }

    /// <summary>
    /// Remembers what the build reads (the style, the layers' tilemaps and materials, the chunk size and the sorting),
    /// for <see cref="BuildInputsChanged"/>.
    /// </summary>
    private void RememberBuildInputs()
    {
        builtStyle = style;
        builtTilemaps.Clear();
        builtMaterials.Clear();
        if (layers != null)
        {
            foreach (Layer layer in layers)
            {
                builtTilemaps.Add(layer?.Tilemap);
                builtMaterials.Add(layer?.Material);
            }
        }

        builtChunkSize = chunkSize;
        builtSortingLayerName = sortingLayerName;
        builtSortingOrder = sortingOrder;
    }

    /// <summary>
    /// Whether anything the last build read has changed since.
    /// </summary>
    private bool BuildInputsChanged()
    {
        int count = layers?.Length ?? 0;
        if (!ReferenceEquals(style, builtStyle) || count != builtTilemaps.Count || chunkSize != builtChunkSize
            || sortingLayerName != builtSortingLayerName || sortingOrder != builtSortingOrder)
        {
            return true;
        }

        for (int i = 0; i < count; i++)
        {
            if (!ReferenceEquals(layers[i]?.Tilemap, builtTilemaps[i]) || !ReferenceEquals(layers[i]?.Material, builtMaterials[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rebuilds after the editor finishes its current work, once however often asked.
    /// </summary>
    private void ScheduleRebuild()
    {
        if (rebuildScheduled)
        {
            return;
        }

        rebuildScheduled = true;
        UnityEditor.EditorApplication.delayCall += RunScheduledRebuild;
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
        FollowControlMapChange(before);
    }

    /// <summary>
    /// Runs the scheduled rebuild, if one is still waiting, unless the renderer has been destroyed or disabled since.
    /// The editor calls it once it is done with the change that scheduled it; tests call it directly.
    /// </summary>
    internal void RunScheduledRebuild()
    {
        if (!rebuildScheduled)
        {
            return;
        }

        rebuildScheduled = false;
        if (this != null && isActiveAndEnabled)
        {
            Rebuild();
        }
    }

    /// <summary>
    /// Has inspector changes to the style and to each layer's material (once per material, however many layers draw
    /// it) schedule a rebuild.
    /// </summary>
    private void FollowStyleAndMaterials()
    {
        if (style != null)
        {
            subscribedStyle = style;
            subscribedStyle.Validated += ScheduleRebuild;
        }

        if (layers == null)
        {
            return;
        }

        foreach (Layer layer in layers)
        {
            TerrainMaterial layerMaterial = layer?.Material;
            if (layerMaterial != null && !subscribedMaterials.Contains(layerMaterial))
            {
                subscribedMaterials.Add(layerMaterial);
                layerMaterial.Validated += ScheduleRebuild;
            }
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
            ScheduleRebuild();
        }
    }

    /// <summary>
    /// Stops following the style and the materials.
    /// </summary>
    private void StopFollowingStyleAndMaterials()
    {
        if (subscribedStyle != null)
        {
            subscribedStyle.Validated -= ScheduleRebuild;
        }

        subscribedStyle = null;
        foreach (TerrainMaterial subscribed in subscribedMaterials)
        {
            if (subscribed != null)
            {
                subscribed.Validated -= ScheduleRebuild;
            }
        }

        subscribedMaterials.Clear();
    }
#endif

    /// <summary>
    /// Builds the control map, texture arrays, lookup texture, runtime material and chunks; logs what is wrong and
    /// builds nothing when the style, layers or materials are not usable. In the editor it follows the inspector
    /// changes of its style and of its layers' materials either way, so fixing one rebuilds a renderer that could not
    /// build.
    /// </summary>
    private void Build()
    {
        if (grid == null)
        {
            grid = GetComponent<Grid>();
        }

#if UNITY_EDITOR
        RememberBuildInputs();
        FollowStyleAndMaterials();
#endif
        if (!IsUsable())
        {
            return;
        }

        int count = layers.Length;
        layerTilemaps = new Tilemap[count];
        TerrainMaterial[] materials = new TerrainMaterial[count];
        for (int i = 0; i < count; i++)
        {
            layerTilemaps[i] = layers[i].Tilemap;
            materials[i] = layers[i].Material;
        }

        animationFramesPerSecond = 0f;
        foreach (TerrainMaterial layerMaterial in materials)
        {
            animationFramesPerSecond = Mathf.Max(animationFramesPerSecond, layerMaterial.IsAnimated ? layerMaterial.FramesPerSecond : 0f);
        }

        controlMap = new TerrainControlMap(layerTilemaps, $"{name} Terrain Control Map");
        controlMap.Apply();
        previewGrewBounds = false;
        albedoArray = BuildArray(materials, m => m.Albedo, $"{name} Terrain Albedo");
        fieldArray = BuildArray(materials, m => m.CellField, $"{name} Terrain Cell Field");
        heightArray = BuildArray(materials, m => m.Height, $"{name} Terrain Height");
        lut = TerrainBlendLut.Create(style, materials, $"{name} Terrain Lut");

        material = new Material(style.Shader) { name = $"{name} Terrain (runtime)", hideFlags = HideFlags.DontSave };
        material.SetTexture(ControlMapId, controlMap.Texture);
        material.SetTexture(LutId, lut);
        material.SetTexture(AlbedoArrayId, albedoArray);
        material.SetTexture(FieldArrayId, fieldArray);
        material.SetTexture(HeightArrayId, heightArray);
        ApplyMode();
        UpdateControlRect();
        UpdateInfo();
        UpdateChunks();
    }

    /// <summary>
    /// Switches the soft look's keyword on the runtime material to match the mode; warns instead when the style's
    /// shader has no such keyword and the mode is soft. The keyword is looked up each time, which allocates nothing,
    /// since one kept from an earlier import of the shader no longer switches anything once the shader is reimported.
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
            material.SetKeyword(softKeyword, mode == BlendMode.Soft);
        }
        else if (mode == BlendMode.Soft)
        {
            Debug.LogWarning($"{name}: the terrain shader {material.shader.name} has no {SoftKeywordName} keyword, so the soft look draws as crisp.", this);
        }
    }

    /// <summary>
    /// Returns false without a word for a renderer not configured at all (no style, no layers) outside play mode, as
    /// right after it is added. Otherwise logs an error for each thing that stops the renderer from drawing (no style
    /// or shader, no layers or more than <see cref="TerrainControlMap.MaxLayers"/>, a graphics device that cannot sample
    /// the control map's format, a layer without a tilemap, a tilemap listed twice, a layer without a material, a
    /// material's textures missing or of the wrong size or format, an unknown sorting layer) and returns whether there
    /// was none; a material several layers draw is checked once.
    /// Also logs an error for each layer whose navigation source's priority is not its index, which does not stop the
    /// drawing (the drawing never reads the priority), warns about each layer whose tilemap has an enabled
    /// <see cref="TilemapRenderer"/>, which would draw its tiles' sprites as well, and warns when the grid is not made
    /// of unrotated one-unit cells, which the shader assumes.
    /// </summary>
    private bool IsUsable()
    {
        if (style == null && (layers == null || layers.Length == 0) && !Application.isPlaying)
        {
            return false;
        }

        if (style == null || style.Shader == null)
        {
            Debug.LogError($"{name}: {nameof(TerrainBlendRenderer)} needs a terrain style with a shader.", this);
            return false;
        }

        if (layers == null || layers.Length == 0 || layers.Length > TerrainControlMap.MaxLayers)
        {
            Debug.LogError($"{name}: {nameof(TerrainBlendRenderer)} needs 1 to {TerrainControlMap.MaxLayers} ground layers.", this);
            return false;
        }

        if (!TerrainControlMap.IsFormatSupported)
        {
            Debug.LogError($"{name}: this graphics device ({SystemInfo.graphicsDeviceType}) cannot sample the R16 control map {nameof(TerrainBlendRenderer)} draws from, so the ground is not drawn.", this);
            return false;
        }

        bool usable = true;
        List<TerrainMaterial> checkedMaterials = new();
        for (int i = 0; i < layers.Length; i++)
        {
            Layer layer = layers[i];
            int first = LayerIndexOf(layer?.Tilemap);
            int earlier = first >= 0 && first < i ? first : -1;
            if (layer == null || layer.Tilemap == null)
            {
                Debug.LogError($"{name}: ground layer {i} has no tilemap.", this);
                usable = false;
            }
            else if (earlier >= 0)
            {
                Debug.LogError($"{name}: ground layers {earlier} and {i} both list {layer.Tilemap.name}; each tilemap is one layer.", this);
                usable = false;
            }
            else if (layer.Material == null)
            {
                Debug.LogError($"{name}: ground layer {i} ({layer.Tilemap.name}) has no material.", this);
                usable = false;
            }
            else if (!checkedMaterials.Contains(layer.Material))
            {
                checkedMaterials.Add(layer.Material);
                usable &= HasUsableTextures(layer.Material);
            }

            NavigationTerrainSource2D source = NavigationSourceOf(i);
            if (source != null && source.Priority != NavigationPriorityOf(i))
            {
                Debug.LogError($"{name}: ground layer {i} ({layer.Tilemap.name}) has navigation priority {source.Priority}, but a ground layer's navigation priority is its place in the layers, its draw order: {NavigationPriorityOf(i)}. Set it from the draw order in the {nameof(TerrainBlendRenderer)} inspector.", this);
            }

            if (layer != null && layer.Tilemap != null && layer.Tilemap.TryGetComponent(out TilemapRenderer tilemapRenderer) && tilemapRenderer.enabled)
            {
                Debug.LogWarning($"{name}: ground layer {i} ({layer.Tilemap.name}) has an enabled Tilemap Renderer, which draws its tiles' sprites (their palette swatches) beside the blended ground. A ground layer is a data tilemap: remove its Tilemap Renderer.", this);
            }
        }

        if (!SortingLayer.IsValid(SortingLayer.NameToID(sortingLayerName)))
        {
            Debug.LogError($"{name}: there is no sorting layer named {sortingLayerName}.", this);
            usable = false;
        }

        if (grid.cellSize.x != 1f || grid.cellSize.y != 1f || grid.cellLayout != GridLayout.CellLayout.Rectangle || grid.transform.rotation != Quaternion.identity)
        {
            Debug.LogWarning($"{name}: {nameof(TerrainBlendRenderer)} assumes an unrotated rectangular grid of one-unit cells.", this);
        }

        return usable;
    }

    /// <summary>
    /// Logs an error and returns false when <paramref name="terrainMaterial"/> has no albedo, lacks the cell field its
    /// cell mode needs, or has a texture (the height included) that is not repeat x 32 px square RGBA32 with sRGB off.
    /// A material without a height texture is usable: the soft look moves its edges by nothing.
    /// </summary>
    private static bool HasUsableTextures(TerrainMaterial terrainMaterial)
    {
        if (terrainMaterial.Albedo == null)
        {
            Debug.LogError($"Terrain material {terrainMaterial.name} has no albedo texture.", terrainMaterial);
            return false;
        }

        if (terrainMaterial.Cells != TerrainMaterial.CellMode.None && terrainMaterial.CellField == null)
        {
            Debug.LogError($"Terrain material {terrainMaterial.name} has chips or stones but no cell field texture.", terrainMaterial);
            return false;
        }

        return HasUsableFormat(terrainMaterial, terrainMaterial.Albedo)
            && (terrainMaterial.CellField == null || HasUsableFormat(terrainMaterial, terrainMaterial.CellField))
            && (terrainMaterial.Height == null || HasUsableFormat(terrainMaterial, terrainMaterial.Height));
    }

    /// <summary>
    /// Logs an error and returns false unless <paramref name="texture"/> is the material's repeat x 32 px square and
    /// RGBA32 with sRGB off, so it can be copied into the arrays as it is.
    /// </summary>
    private static bool HasUsableFormat(TerrainMaterial terrainMaterial, Texture2D texture)
    {
        int size = terrainMaterial.RepeatPixels;
        if (texture.width == size && texture.height == size && texture.graphicsFormat == GraphicsFormat.R8G8B8A8_UNorm)
        {
            return true;
        }

        Debug.LogError($"{texture.name} of terrain material {terrainMaterial.name} must be {size} px square (its repeat of {terrainMaterial.RepeatCells} cells) and RGBA32 with sRGB off.", texture);
        return false;
    }

    /// <summary>
    /// Copies each layer's texture of one kind (albedo, cell field or height, picked by <paramref name="kind"/>) into
    /// a slice of a new array as large as the largest material, on the GPU. The array keeps no copy in memory, so it is
    /// uploaded empty and made unreadable before the copies, which a later upload would otherwise overwrite. A slice
    /// whose material has no such texture is left empty: the shader never reads a missing cell field, and the lookup
    /// texture gives a material without a height a height scale of 0.
    /// </summary>
    private static Texture2DArray BuildArray(TerrainMaterial[] materials, Func<TerrainMaterial, Texture2D> kind, string arrayName)
    {
        int size = TerrainStyle.PixelsPerCell;
        foreach (TerrainMaterial layerMaterial in materials)
        {
            size = Mathf.Max(size, layerMaterial.RepeatPixels);
        }

        Texture2DArray array = new(size, size, materials.Length, TextureFormat.RGBA32, false, true)
        {
            name = arrayName,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.DontSave
        };
        array.Apply(false, true);
        for (int slice = 0; slice < materials.Length; slice++)
        {
            Texture2D source = kind(materials[slice]);
            if (source != null)
            {
                Graphics.CopyTexture(source, 0, 0, 0, 0, source.width, source.height, array, slice, 0, 0, 0);
            }
        }

        return array;
    }

    /// <summary>
    /// Removes the chunks the control map's bounds no longer reach (after an undo shrank them), creates the chunks
    /// covering the bounds that do not exist yet and fits every chunk's quad to the bounds. Chunks sit on a fixed grid
    /// of <see cref="chunkSize"/> cells, so growing bounds only add chunks.
    /// </summary>
    private void UpdateChunks()
    {
        BoundsInt bounds = controlMap.Bounds;
        staleChunks.Clear();
        foreach (KeyValuePair<Vector2Int, Chunk> entry in chunksByKey)
        {
            if (controlMap.IsEmpty || !Overlaps(entry.Key, bounds))
            {
                staleChunks.Add(entry.Key);
            }
        }

        foreach (Vector2Int key in staleChunks)
        {
            DestroyChunk(key);
        }

        if (controlMap.IsEmpty)
        {
            return;
        }

        int sortingLayerId = SortingLayer.NameToID(sortingLayerName);
        for (int cy = FloorDiv(bounds.yMin, chunkSize); cy <= FloorDiv(bounds.yMax - 1, chunkSize); cy++)
        {
            for (int cx = FloorDiv(bounds.xMin, chunkSize); cx <= FloorDiv(bounds.xMax - 1, chunkSize); cx++)
            {
                Vector2Int key = new(cx, cy);
                if (!chunksByKey.TryGetValue(key, out Chunk chunk))
                {
                    chunk = CreateChunk(key, sortingLayerId);
                }

                int xMin = Mathf.Max(cx * chunkSize, bounds.xMin);
                int yMin = Mathf.Max(cy * chunkSize, bounds.yMin);
                int xMax = Mathf.Min((cx + 1) * chunkSize, bounds.xMax);
                int yMax = Mathf.Min((cy + 1) * chunkSize, bounds.yMax);
                SetQuad(chunk.Mesh, xMin, yMin, xMax, yMax);
            }
        }
    }

    /// <summary>
    /// Whether the chunk at chunk coordinates <paramref name="key"/> covers any cell of <paramref name="bounds"/>.
    /// </summary>
    private bool Overlaps(Vector2Int key, BoundsInt bounds)
    {
        return key.x * chunkSize < bounds.xMax && (key.x + 1) * chunkSize > bounds.xMin
            && key.y * chunkSize < bounds.yMax && (key.y + 1) * chunkSize > bounds.yMin;
    }

    /// <summary>
    /// Creates the chunk at chunk coordinates <paramref name="key"/>: a child object, never saved or shown in the
    /// hierarchy, with a mesh renderer of the runtime material on the sorting layer.
    /// </summary>
    private Chunk CreateChunk(Vector2Int key, int sortingLayerId)
    {
        string chunkName = $"{name} Terrain Chunk {key.x},{key.y}";
        Mesh mesh = new() { name = chunkName, hideFlags = HideFlags.DontSave };
        GameObject chunkObject = new(chunkName) { hideFlags = HideFlags.HideAndDontSave, layer = gameObject.layer };
        chunkObject.transform.SetParent(transform, false);
        chunkObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.sortingLayerID = sortingLayerId;
        meshRenderer.sortingOrder = sortingOrder;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        meshRenderer.allowOcclusionWhenDynamic = false;
        Chunk chunk = new(meshRenderer, mesh);
        chunksByKey.Add(key, chunk);
        chunks.Add(meshRenderer);
        return chunk;
    }

    /// <summary>
    /// Destroys the chunk at chunk coordinates <paramref name="key"/>, its object and its mesh.
    /// </summary>
    private void DestroyChunk(Vector2Int key)
    {
        Chunk chunk = chunksByKey[key];
        chunksByKey.Remove(key);
        for (int i = chunks.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(chunks[i], chunk.Renderer))
            {
                chunks.RemoveAt(i);
            }
        }

        if (chunk.Renderer != null)
        {
            DestroyRuntime(chunk.Renderer.gameObject);
        }

        DestroyRuntime(chunk.Mesh);
    }

    /// <summary>
    /// Makes <paramref name="mesh"/> a quad over the cells from (<paramref name="xMin"/>, <paramref name="yMin"/>) to
    /// (<paramref name="xMax"/>, <paramref name="yMax"/>), exclusive, in this object's space.
    /// </summary>
    private void SetQuad(Mesh mesh, int xMin, int yMin, int xMax, int yMax)
    {
        Vector3 min = grid.CellToLocal(new Vector3Int(xMin, yMin, 0));
        Vector3 max = grid.CellToLocal(new Vector3Int(xMax, yMax, 0));
        mesh.Clear();
        mesh.SetVertices(new[] { new Vector3(min.x, min.y, 0f), new Vector3(max.x, min.y, 0f), new Vector3(min.x, max.y, 0f), new Vector3(max.x, max.y, 0f) });
        mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
        mesh.SetTangents(new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f) });
        mesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
        mesh.SetUVs(0, new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one });
        mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
        mesh.RecalculateBounds();
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
    /// Tells the material how many layers there are, whether to draw the debug view, and the fixed time animated
    /// materials are drawn at, if any.
    /// </summary>
    private void UpdateInfo()
    {
        if (material != null)
        {
            material.SetVector(InfoId, new Vector4(layers.Length, debugView ? 1f : 0f, timeFixed ? 1f : 0f, fixedTime));
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

        int layer = BuiltLayerIndexOf(tilemap);
        if (layer < 0)
        {
            return;
        }

        BoundsInt before = controlMap.Bounds;
        controlMap.Refresh(layer, positions);
        FollowControlMapChange(before);
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

        if (controlMap.HasPendingChanges)
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
    /// Stops waiting to upload, stops following the style and materials, and destroys the chunks, meshes, material and
    /// textures (the control map's, the three arrays and the lookup texture).
    /// </summary>
    private void Teardown()
    {
        if (flushScheduled)
        {
            RenderPipelineManager.beginContextRendering -= flushHandler;
            flushScheduled = false;
        }

#if UNITY_EDITOR
        StopFollowingStyleAndMaterials();
#endif
        foreach (Chunk chunk in chunksByKey.Values)
        {
            if (chunk.Renderer != null)
            {
                DestroyRuntime(chunk.Renderer.gameObject);
            }

            DestroyRuntime(chunk.Mesh);
        }

        chunksByKey.Clear();
        chunks.Clear();
        controlMap?.Dispose();
        controlMap = null;
        DestroyRuntime(material);
        DestroyRuntime(albedoArray);
        DestroyRuntime(fieldArray);
        DestroyRuntime(heightArray);
        DestroyRuntime(lut);
        material = null;
        albedoArray = null;
        fieldArray = null;
        heightArray = null;
        lut = null;
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

    /// <summary>
    /// <paramref name="value"/> divided by <paramref name="divisor"/>, rounded down.
    /// </summary>
    private static int FloorDiv(int value, int divisor)
    {
        return value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
    }
}
