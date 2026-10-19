using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// One seamless world: its root scene, its place in the global layout of worlds, its size in chunks, the scene of
/// every chunk it has, and the entries it is entered at. A world's cells are counted from its origin, so the chunk at
/// (cx, cy) holds the cells from (cx, cy) * <see cref="ChunkCoords.Size"/> up to the next chunk.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/World Definition", fileName = "World")]
public class WorldDefinition : ScriptableObject
{
    public const int MaxChunksPerSide = 16;

    /// <summary>
    /// The scene of the chunk at <see cref="Coords"/>.
    /// </summary>
    [Serializable]
    public class Chunk
    {
        [SerializeField] private Vector2Int coords;
        [SerializeField] private SceneDefinition scene;

        /// <summary>
        /// Creates an empty entry for the serializer.
        /// </summary>
        public Chunk()
        {
        }

        /// <summary>
        /// Creates the entry of the chunk at <paramref name="chunkCoords"/> whose scene is <paramref name="chunkScene"/>.
        /// </summary>
        public Chunk(Vector2Int chunkCoords, SceneDefinition chunkScene)
        {
            coords = chunkCoords;
            scene = chunkScene;
        }

        public Vector2Int Coords => coords;
        public SceneDefinition Scene => scene;
    }

    /// <summary>
    /// A named place the world is entered at, in world units from its origin; a waystone is one players discover.
    /// </summary>
    [Serializable]
    public class Entry
    {
        [SerializeField] private string name;
        [SerializeField] private Vector2 position;
        [SerializeField] private bool waystone;

        /// <summary>
        /// Creates an empty entry for the serializer.
        /// </summary>
        public Entry()
        {
        }

        /// <summary>
        /// Creates the entry <paramref name="entryName"/> at <paramref name="localPosition"/> from the world's origin.
        /// </summary>
        public Entry(string entryName, Vector2 localPosition, bool isWaystone)
        {
            name = entryName;
            position = localPosition;
            waystone = isWaystone;
        }

        public string Name => name;
        public Vector2 Position => position;
        public bool Waystone => waystone;
    }

    [Tooltip("Stable id of this world, its asset's GUID; set by the editor.")]
    [SerializeField] private string worldId;
    [SerializeField] private SceneDefinition rootScene;
    [Tooltip("Where the world's cell (0, 0) lies, in world units. Worlds stay apart in one global layout (Docs/WorldStreaming.md).")]
    [SerializeField] private Vector2Int origin;
    [Tooltip("How many chunks the world has along each side, at most 16.")]
    [SerializeField] private Vector2Int sizeInChunks = Vector2Int.one;
    [SerializeField] private Chunk[] chunks = new Chunk[0];
    [Tooltip("The entries the world is entered at; the first is its default.")]
    [SerializeField] private Entry[] entries = new Entry[0];

    public string WorldId => worldId;
    public SceneDefinition RootScene => rootScene;
    public Vector2Int Origin => origin;
    public Vector2Int SizeInChunks => sizeInChunks;
    public IReadOnlyList<Chunk> Chunks => chunks;
    public IReadOnlyList<Entry> Entries => entries;
    public Entry DefaultEntry => entries.Length > 0 ? entries[0] : null;

    /// <summary>
    /// The world's cells as a rectangle in its own cells: from (0, 0) to its size in chunks times the chunk size.
    /// </summary>
    public RectInt LocalBounds => new(0, 0, sizeInChunks.x * ChunkCoords.Size, sizeInChunks.y * ChunkCoords.Size);

    /// <summary>
    /// The scene of the chunk at <paramref name="coords"/>; false when the world has no chunk there.
    /// </summary>
    public bool TryGetChunkScene(Vector2Int coords, out SceneDefinition scene)
    {
        for (int i = 0; i < chunks.Length; i++)
        {
            if (chunks[i] != null && chunks[i].Coords == coords && chunks[i].Scene != null)
            {
                scene = chunks[i].Scene;
                return true;
            }
        }

        scene = null;
        return false;
    }

    /// <summary>
    /// Whether the world has a chunk scene at <paramref name="coords"/>.
    /// </summary>
    public bool HasChunk(Vector2Int coords)
    {
        return TryGetChunkScene(coords, out _);
    }

    /// <summary>
    /// The entry named <paramref name="entryName"/>; the default entry, with a warning, when none has that name; null
    /// when the world has no entry.
    /// </summary>
    public Entry FindEntry(string entryName)
    {
        foreach (Entry entry in entries)
        {
            if (entry != null && entry.Name == entryName)
            {
                return entry;
            }
        }

        if (!string.IsNullOrEmpty(entryName) && DefaultEntry != null)
        {
            Debug.LogWarning($"{name} has no entry '{entryName}'; using '{DefaultEntry.Name}' instead.", this);
        }

        return DefaultEntry;
    }

    /// <summary>
    /// Whether <paramref name="scenePath"/> is this world's root scene or one of its chunk scenes.
    /// </summary>
    public bool OwnsScene(string scenePath)
    {
        if (rootScene != null && rootScene.Matches(scenePath))
        {
            return true;
        }

        foreach (Chunk chunk in chunks)
        {
            if (chunk != null && chunk.Scene != null && chunk.Scene.Matches(scenePath))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The world position of <paramref name="localPosition"/>, a position in units from the world's origin.
    /// </summary>
    public Vector2 ToWorld(Vector2 localPosition)
    {
        return localPosition + (Vector2)origin;
    }

    /// <summary>
    /// The position of <paramref name="worldPosition"/> in units from the world's origin.
    /// </summary>
    public Vector2 ToLocal(Vector2 worldPosition)
    {
        return worldPosition - (Vector2)origin;
    }

    /// <summary>
    /// Builds an unsaved world for tests, named <paramref name="worldName"/>, whose root and chunk scenes are transient
    /// definitions of made-up paths (<c>Test/World_name.unity</c>, <c>Test/World_name_x_y.unity</c>).
    /// </summary>
    internal static WorldDefinition Create(string worldName, Vector2Int worldOrigin, Vector2Int size, IEnumerable<Vector2Int> chunkCoords, params Entry[] worldEntries)
    {
        WorldDefinition world = CreateInstance<WorldDefinition>();
        world.name = worldName;
        world.hideFlags = HideFlags.DontSave;
        world.worldId = $"test-{worldName}";
        world.rootScene = SceneDefinition.CreateTransient($"Test/World_{worldName}.unity");
        world.origin = worldOrigin;
        world.sizeInChunks = size;
        List<Chunk> made = new();
        foreach (Vector2Int coords in chunkCoords)
        {
            made.Add(new Chunk(coords, SceneDefinition.CreateTransient($"Test/World_{worldName}_{coords.x}_{coords.y}.unity")));
        }

        world.chunks = made.ToArray();
        world.entries = worldEntries ?? new Entry[0];
        return world;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor only: sets the root scene, origin and size of a world being created, and its id from its asset.
    /// </summary>
    public void Configure(SceneDefinition root, Vector2Int worldOrigin, Vector2Int size)
    {
        rootScene = root;
        origin = worldOrigin;
        sizeInChunks = new Vector2Int(Mathf.Clamp(size.x, 1, MaxChunksPerSide), Mathf.Clamp(size.y, 1, MaxChunksPerSide));
        RefreshId();
        EditorUtility.SetDirty(this);
    }

    /// <summary>
    /// Editor only: records <paramref name="scene"/> as the chunk at <paramref name="coords"/>, replacing any entry
    /// there.
    /// </summary>
    public void SetChunk(Vector2Int coords, SceneDefinition scene)
    {
        List<Chunk> kept = new();
        foreach (Chunk chunk in chunks)
        {
            if (chunk != null && chunk.Coords != coords)
            {
                kept.Add(chunk);
            }
        }

        kept.Add(new Chunk(coords, scene));
        kept.Sort((a, b) => a.Coords.y != b.Coords.y ? a.Coords.y.CompareTo(b.Coords.y) : a.Coords.x.CompareTo(b.Coords.x));
        chunks = kept.ToArray();
        EditorUtility.SetDirty(this);
    }

    /// <summary>
    /// Editor only: replaces the world's entries.
    /// </summary>
    public void SetEntries(params Entry[] worldEntries)
    {
        entries = worldEntries ?? new Entry[0];
        EditorUtility.SetDirty(this);
    }

    /// <summary>
    /// Keeps the id equal to the asset's GUID, so a copied asset gets its own.
    /// </summary>
    private void RefreshId()
    {
        string path = AssetDatabase.GetAssetPath(this);
        string guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
        if (!string.IsNullOrEmpty(guid) && guid != worldId)
        {
            worldId = guid;
            EditorUtility.SetDirty(this);
        }
    }

    /// <summary>
    /// Keeps the id in step with the asset and the size within its bounds.
    /// </summary>
    private void OnValidate()
    {
        sizeInChunks = new Vector2Int(Mathf.Clamp(sizeInChunks.x, 1, MaxChunksPerSide), Mathf.Clamp(sizeInChunks.y, 1, MaxChunksPerSide));
        RefreshId();
    }
#endif
}
