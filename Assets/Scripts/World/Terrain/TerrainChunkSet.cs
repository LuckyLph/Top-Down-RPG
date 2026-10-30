using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The chunk quads a <see cref="TerrainBlendRenderer"/> draws its ground with: one child object per chunk of
/// <see cref="ChunkSize"/> cells on a fixed grid of chunks, each a mesh renderer of the runtime material on the ground's
/// sorting layer, never saved or shown in the hierarchy, so off-screen chunks are culled one by one. Plain C#: it creates,
/// fits and destroys the chunks; the renderer decides when.
/// </summary>
internal sealed class TerrainChunkSet
{
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

    private readonly Dictionary<Vector2Int, Chunk> chunksByKey = new();
    private readonly List<MeshRenderer> renderers = new();
    private readonly List<Vector2Int> staleChunks = new();
    private readonly GameObject owner;
    private readonly Grid grid;

    /// <summary>
    /// Creates an empty set whose chunks are children of <paramref name="owner"/>, on its layer and named after it, and
    /// cover cells of <paramref name="grid"/>, <paramref name="chunkSize"/> per side.
    /// </summary>
    public TerrainChunkSet(GameObject owner, Grid grid, int chunkSize)
    {
        this.owner = owner;
        this.grid = grid;
        ChunkSize = chunkSize;
    }

    public int ChunkSize { get; }
    public IReadOnlyList<MeshRenderer> Renderers => renderers;

    /// <summary>
    /// Removes the chunks <paramref name="bounds"/> no longer reach (all of them when <paramref name="empty"/>), creates
    /// the chunks covering the bounds that do not exist yet, drawing with <paramref name="material"/> on the given
    /// sorting layer and order, and fits every chunk's quad to the bounds, on <paramref name="renderingLayerMask"/>.
    /// Chunks sit on a fixed grid, so growing bounds only add chunks.
    /// </summary>
    public void Fit(BoundsInt bounds, bool empty, Material material, int sortingLayerId, int sortingOrder, uint renderingLayerMask)
    {
        staleChunks.Clear();
        foreach (KeyValuePair<Vector2Int, Chunk> entry in chunksByKey)
        {
            if (empty || !Overlaps(entry.Key, bounds))
            {
                staleChunks.Add(entry.Key);
            }
        }

        foreach (Vector2Int key in staleChunks)
        {
            DestroyChunk(key);
        }

        if (empty)
        {
            return;
        }

        for (int cy = FloorDiv(bounds.yMin, ChunkSize); cy <= FloorDiv(bounds.yMax - 1, ChunkSize); cy++)
        {
            for (int cx = FloorDiv(bounds.xMin, ChunkSize); cx <= FloorDiv(bounds.xMax - 1, ChunkSize); cx++)
            {
                Vector2Int key = new(cx, cy);
                if (!chunksByKey.TryGetValue(key, out Chunk chunk))
                {
                    chunk = CreateChunk(key, material, sortingLayerId, sortingOrder);
                }

                int xMin = Mathf.Max(cx * ChunkSize, bounds.xMin);
                int yMin = Mathf.Max(cy * ChunkSize, bounds.yMin);
                int xMax = Mathf.Min((cx + 1) * ChunkSize, bounds.xMax);
                int yMax = Mathf.Min((cy + 1) * ChunkSize, bounds.yMax);
                SetQuad(chunk.Mesh, xMin, yMin, xMax, yMax);
                if (chunk.Renderer.renderingLayerMask != renderingLayerMask)
                {
                    chunk.Renderer.renderingLayerMask = renderingLayerMask;
                }
            }
        }
    }

    /// <summary>
    /// Destroys every chunk, its object and its mesh.
    /// </summary>
    public void Clear()
    {
        foreach (Chunk chunk in chunksByKey.Values)
        {
            if (chunk.Renderer != null)
            {
                DestroyRuntime(chunk.Renderer.gameObject);
            }

            DestroyRuntime(chunk.Mesh);
        }

        chunksByKey.Clear();
        renderers.Clear();
    }

    /// <summary>
    /// Whether the chunk at chunk coordinates <paramref name="key"/> covers any cell of <paramref name="bounds"/>.
    /// </summary>
    private bool Overlaps(Vector2Int key, BoundsInt bounds)
    {
        return key.x * ChunkSize < bounds.xMax && (key.x + 1) * ChunkSize > bounds.xMin
            && key.y * ChunkSize < bounds.yMax && (key.y + 1) * ChunkSize > bounds.yMin;
    }

    /// <summary>
    /// Creates the chunk at chunk coordinates <paramref name="key"/>: a child object, never saved or shown in the
    /// hierarchy, with a mesh renderer of <paramref name="material"/> on the sorting layer.
    /// </summary>
    private Chunk CreateChunk(Vector2Int key, Material material, int sortingLayerId, int sortingOrder)
    {
        string chunkName = $"{owner.name} Terrain Chunk {key.x},{key.y}";
        Mesh mesh = new() { name = chunkName, hideFlags = HideFlags.DontSave };
        GameObject chunkObject = new(chunkName) { hideFlags = HideFlags.HideAndDontSave, layer = owner.layer };
        chunkObject.transform.SetParent(owner.transform, false);
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
        renderers.Add(meshRenderer);
        return chunk;
    }

    /// <summary>
    /// Destroys the chunk at chunk coordinates <paramref name="key"/>, its object and its mesh.
    /// </summary>
    private void DestroyChunk(Vector2Int key)
    {
        Chunk chunk = chunksByKey[key];
        chunksByKey.Remove(key);
        for (int i = renderers.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(renderers[i], chunk.Renderer))
            {
                renderers.RemoveAt(i);
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
    /// (<paramref name="xMax"/>, <paramref name="yMax"/>), exclusive, in the owner's space.
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
            Object.Destroy(target);
        }
        else
        {
            Object.DestroyImmediate(target);
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
