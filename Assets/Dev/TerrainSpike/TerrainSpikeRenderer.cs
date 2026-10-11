using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

/// <summary>
/// Throwaway Phase 0 spike of Docs/TerrainBlending.md: draws an area's ground data tilemaps (grass, gravel, stone,
/// bottom first) with one blending shader at 32 px per unit, in the crisp or soft look, as one quad per chunk on the
/// Terrain sorting layer. Builds a control map (one R8 texel per cell, one bit per layer), the material texture arrays
/// and the chunk meshes when enabled, patches the control map when a layer's tiles change, and destroys everything
/// when disabled. Lives on an object that is never saved.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class TerrainSpikeRenderer : MonoBehaviour
{
    /// <summary>
    /// The two looks the shader can draw, picked by a keyword.
    /// </summary>
    public enum Look
    {
        Crisp,
        Soft
    }

    private const string SoftKeyword = "_TERRAIN_SOFT";
    private const int Margin = 1;

    private static readonly int ControlMapId = Shader.PropertyToID("_ControlMap");
    private static readonly int AlbedoArrayId = Shader.PropertyToID("_AlbedoArray");
    private static readonly int FieldArrayId = Shader.PropertyToID("_FieldArray");
    private static readonly int HeightArrayId = Shader.PropertyToID("_HeightArray");
    private static readonly int ControlRectId = Shader.PropertyToID("_ControlRect");
    private static readonly int BackdropId = Shader.PropertyToID("_Backdrop");
    private static readonly int DebugViewId = Shader.PropertyToID("_DebugView");

    [SerializeField] private Grid grid;
    [Tooltip("Ground data tilemaps, bottom layer first: grass, gravel, stone. Index i is control map bit i and material slice i.")]
    [SerializeField] private Tilemap[] layers = new Tilemap[0];
    [SerializeField] private Shader shader;
    [Tooltip("Albedo per material, 128 px, uncompressed RGBA32, sRGB off.")]
    [SerializeField] private Texture2D[] albedo = new Texture2D[0];
    [Tooltip("Cell field per material (empty for grass), same format.")]
    [SerializeField] private Texture2D[] field = new Texture2D[0];
    [Tooltip("Height per material for the soft look, same format.")]
    [SerializeField] private Texture2D[] height = new Texture2D[0];
    [SerializeField] private Look look = Look.Crisp;
    [Tooltip("Fill cells with no ground with this colour, so the render tilemaps underneath cannot show through.")]
    [SerializeField] private bool backdrop = true;
    [SerializeField] private Color backdropColour = new Color(0.192f, 0.302f, 0.475f, 1f);
    [SerializeField] private string sortingLayerName = "Terrain";
    [SerializeField] private int sortingOrder = 5;
    [SerializeField, Min(1)] private int chunkSize = 16;
    [SerializeField] private bool debugLayers;

    private readonly List<GameObject> chunks = new List<GameObject>();
    private readonly List<Mesh> meshes = new List<Mesh>();
    private Texture2D controlMap;
    private Texture2DArray albedoArray;
    private Texture2DArray fieldArray;
    private Texture2DArray heightArray;
    private Material material;
    private BoundsInt bounds;
    private byte[] cells;

    public Look CurrentLook => look;
    public Material RuntimeMaterial => material;
    public BoundsInt Bounds => bounds;
    public IReadOnlyList<GameObject> Chunks => chunks;

    /// <summary>
    /// Sets what to draw and rebuilds if enabled.
    /// </summary>
    public void Configure(Grid targetGrid, Tilemap[] groundLayers, Shader terrainShader, Texture2D[] albedoTextures, Texture2D[] fieldTextures, Texture2D[] heightTextures)
    {
        grid = targetGrid;
        layers = groundLayers;
        shader = terrainShader;
        albedo = albedoTextures;
        field = fieldTextures;
        height = heightTextures;
        Rebuild();
    }

    /// <summary>
    /// Switches between the crisp and soft looks.
    /// </summary>
    public void SetLook(Look value)
    {
        look = value;
        ApplySettings();
    }

    /// <summary>
    /// Shows the index of the topmost visible layer instead of colours, for blob checks.
    /// </summary>
    public void SetDebugLayers(bool value)
    {
        debugLayers = value;
        ApplySettings();
    }

    /// <summary>
    /// Flips between crisp and soft.
    /// </summary>
    [ContextMenu("Flip Look")]
    public void FlipLook()
    {
        SetLook(look == Look.Crisp ? Look.Soft : Look.Crisp);
    }

    /// <summary>
    /// Tears down and rebuilds the control map, arrays, material and chunks from the current tilemaps.
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
    /// Builds everything and starts listening to tile changes.
    /// </summary>
    private void OnEnable()
    {
        Tilemap.tilemapPositionsChanged += HandleTilemapPositionsChanged;
        Build();
    }

    /// <summary>
    /// Stops listening to tile changes and destroys everything built at runtime.
    /// </summary>
    private void OnDisable()
    {
        Tilemap.tilemapPositionsChanged -= HandleTilemapPositionsChanged;
        Teardown();
    }

    /// <summary>
    /// Pushes inspector changes to the runtime material.
    /// </summary>
    private void OnValidate()
    {
        ApplySettings();
    }

    /// <summary>
    /// Builds the control map, texture arrays, runtime material and chunks; does nothing until configured.
    /// </summary>
    private void Build()
    {
        if (grid == null || shader == null || layers == null || layers.Length == 0)
        {
            return;
        }
        if (layers.Length > 8 || albedo.Length < layers.Length || height.Length < layers.Length)
        {
            Debug.LogError("TerrainSpikeRenderer needs at most 8 layers and an albedo and height texture per layer.", this);
            return;
        }
        if (grid.cellSize.x != 1f || grid.cellSize.y != 1f || grid.transform.rotation != Quaternion.identity)
        {
            Debug.LogWarning("TerrainSpikeRenderer assumes an unrotated grid of 1-unit cells.", this);
        }

        bounds = ComputeBounds();
        if (bounds.size.x <= 0 || bounds.size.y <= 0)
        {
            return;
        }
        BuildControlMap();
        albedoArray = BuildArray(albedo, "TerrainSpike Albedo");
        fieldArray = BuildArray(field, "TerrainSpike Field");
        heightArray = BuildArray(height, "TerrainSpike Height");

        material = new Material(shader) { name = "TerrainSpike (runtime)", hideFlags = HideFlags.DontSave };
        material.SetTexture(ControlMapId, controlMap);
        material.SetTexture(AlbedoArrayId, albedoArray);
        material.SetTexture(FieldArrayId, fieldArray);
        material.SetTexture(HeightArrayId, heightArray);
        Vector3 origin = grid.CellToWorld(bounds.min);
        material.SetVector(ControlRectId, new Vector4(origin.x, origin.y, bounds.size.x, bounds.size.y));
        ApplySettings();
        BuildChunks(origin);
    }

    /// <summary>
    /// The union of the layers' cell bounds, grown by a margin of one cell so edge shadows have room.
    /// </summary>
    private BoundsInt ComputeBounds()
    {
        bool any = false;
        Vector3Int min = Vector3Int.zero;
        Vector3Int max = Vector3Int.zero;
        foreach (Tilemap layer in layers)
        {
            if (layer == null || layer.GetUsedTilesCount() == 0)
            {
                continue;
            }
            BoundsInt b = layer.cellBounds;
            min = any ? Vector3Int.Min(min, b.min) : b.min;
            max = any ? Vector3Int.Max(max, b.max) : b.max;
            any = true;
        }
        if (!any)
        {
            return new BoundsInt();
        }
        min -= new Vector3Int(Margin, Margin, 0);
        max += new Vector3Int(Margin, Margin, 0);
        return new BoundsInt(min.x, min.y, 0, max.x - min.x, max.y - min.y, 1);
    }

    /// <summary>
    /// Packs each cell's layer bits into a byte and uploads them as a point-filtered R8 texture.
    /// </summary>
    private void BuildControlMap()
    {
        int width = bounds.size.x;
        int heightCells = bounds.size.y;
        cells = new byte[width * heightCells];
        for (int i = 0; i < layers.Length; i++)
        {
            Tilemap layer = layers[i];
            if (layer == null)
            {
                continue;
            }
            TileBase[] tiles = layer.GetTilesBlock(bounds);
            byte bit = (byte)(1 << i);
            for (int index = 0; index < tiles.Length; index++)
            {
                if (tiles[index] != null)
                {
                    cells[index] |= bit;
                }
            }
        }
        controlMap = new Texture2D(width, heightCells, TextureFormat.R8, false, true)
        {
            name = "TerrainSpike Control Map",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        controlMap.SetPixelData(cells, 0);
        controlMap.Apply(false, false);
    }

    /// <summary>
    /// Copies one texture per layer into a linear array on the GPU; a missing texture becomes a slice of zeros.
    /// </summary>
    private Texture2DArray BuildArray(Texture2D[] sources, string arrayName)
    {
        int size = albedo[0].width;
        var array = new Texture2DArray(size, size, layers.Length, TextureFormat.RGBA32, false, true)
        {
            name = arrayName,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
            hideFlags = HideFlags.DontSave
        };
        Texture2D blank = null;
        for (int slice = 0; slice < layers.Length; slice++)
        {
            Texture2D source = slice < sources.Length ? sources[slice] : null;
            if (source != null && (source.width != size || source.height != size || source.format != TextureFormat.RGBA32))
            {
                Debug.LogError($"TerrainSpikeRenderer: {source.name} must be {size} px RGBA32 to go into {arrayName}.", this);
                source = null;
            }
            if (source == null)
            {
                if (blank == null)
                {
                    blank = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.DontSave };
                    blank.SetPixelData(new byte[size * size * 4], 0);
                    blank.Apply(false, false);
                }
                source = blank;
            }
            Graphics.CopyTexture(source, 0, 0, array, slice, 0);
        }
        DestroyRuntime(blank);
        return array;
    }

    /// <summary>
    /// Creates one quad per chunk of cells over the bounds, each its own renderer on the sorting layer so off-screen chunks are culled.
    /// </summary>
    private void BuildChunks(Vector3 origin)
    {
        int sortingLayerId = SortingLayer.NameToID(sortingLayerName);
        for (int cy = 0; cy < bounds.size.y; cy += chunkSize)
        {
            for (int cx = 0; cx < bounds.size.x; cx += chunkSize)
            {
                int w = Mathf.Min(chunkSize, bounds.size.x - cx);
                int h = Mathf.Min(chunkSize, bounds.size.y - cy);
                Vector3 min = transform.InverseTransformPoint(origin + new Vector3(cx, cy, 0f));
                Vector3 max = transform.InverseTransformPoint(origin + new Vector3(cx + w, cy + h, 0f));
                var mesh = new Mesh { name = $"TerrainSpike Chunk {cx},{cy}", hideFlags = HideFlags.DontSave };
                mesh.SetVertices(new[] { new Vector3(min.x, min.y, 0f), new Vector3(max.x, min.y, 0f), new Vector3(min.x, max.y, 0f), new Vector3(max.x, max.y, 0f) });
                mesh.SetNormals(new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
                mesh.SetTangents(new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f) });
                mesh.SetColors(new[] { Color.white, Color.white, Color.white, Color.white });
                mesh.SetUVs(0, new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one });
                mesh.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
                mesh.RecalculateBounds();
                meshes.Add(mesh);

                var chunk = new GameObject(mesh.name) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
                chunk.transform.SetParent(transform, false);
                chunk.AddComponent<MeshFilter>().sharedMesh = mesh;
                var meshRenderer = chunk.AddComponent<MeshRenderer>();
                meshRenderer.sharedMaterial = material;
                meshRenderer.sortingLayerID = sortingLayerId;
                meshRenderer.sortingOrder = sortingOrder;
                meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
                meshRenderer.lightProbeUsage = LightProbeUsage.Off;
                meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                meshRenderer.allowOcclusionWhenDynamic = false;
                chunks.Add(chunk);
            }
        }
    }

    /// <summary>
    /// Sets the look keyword, backdrop, debug view and sorting order on the runtime material and chunks.
    /// </summary>
    private void ApplySettings()
    {
        if (material == null)
        {
            return;
        }
        if (look == Look.Soft)
        {
            material.EnableKeyword(SoftKeyword);
        }
        else
        {
            material.DisableKeyword(SoftKeyword);
        }
        Color c = backdropColour;
        material.SetVector(BackdropId, backdrop ? new Vector4(c.r, c.g, c.b, 1f) : Vector4.zero);
        material.SetVector(DebugViewId, new Vector4(debugLayers ? 1f : 0f, 0f, 0f, 0f));
        foreach (GameObject chunk in chunks)
        {
            if (chunk != null)
            {
                chunk.GetComponent<MeshRenderer>().sortingOrder = sortingOrder;
            }
        }
    }

    /// <summary>
    /// Patches the control map for cells changed on one of the layers, and rebuilds when a cell lands outside the bounds.
    /// </summary>
    private void HandleTilemapPositionsChanged(Tilemap tilemap, NativeArray<Vector3Int> positions)
    {
        if (controlMap == null)
        {
            return;
        }
        int layer = System.Array.IndexOf(layers, tilemap);
        if (layer < 0)
        {
            return;
        }
        byte bit = (byte)(1 << layer);
        bool changed = false;
        foreach (Vector3Int position in positions)
        {
            if (!bounds.Contains(new Vector3Int(position.x, position.y, 0)))
            {
                Rebuild();
                return;
            }
            int index = (position.y - bounds.yMin) * bounds.size.x + position.x - bounds.xMin;
            byte value = tilemap.HasTile(position) ? (byte)(cells[index] | bit) : (byte)(cells[index] & ~bit);
            if (value != cells[index])
            {
                cells[index] = value;
                changed = true;
            }
        }
        if (changed)
        {
            controlMap.SetPixelData(cells, 0);
            controlMap.Apply(false, false);
        }
    }

    /// <summary>
    /// Destroys the chunks, meshes, material and textures built at runtime.
    /// </summary>
    private void Teardown()
    {
        foreach (GameObject chunk in chunks)
        {
            DestroyRuntime(chunk);
        }
        chunks.Clear();
        foreach (Mesh mesh in meshes)
        {
            DestroyRuntime(mesh);
        }
        meshes.Clear();
        DestroyRuntime(material);
        DestroyRuntime(controlMap);
        DestroyRuntime(albedoArray);
        DestroyRuntime(fieldArray);
        DestroyRuntime(heightArray);
        material = null;
        controlMap = null;
        albedoArray = null;
        fieldArray = null;
        heightArray = null;
        cells = null;
    }

    /// <summary>
    /// Destroys an object made at runtime, immediately outside play mode.
    /// </summary>
    private static void DestroyRuntime(Object target)
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
