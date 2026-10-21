using System;
using UnityEngine;

/// <summary>
/// What a world generator keeps about one chunk of its world (Docs/WorldGeneration.md: Architecture): the hash of what
/// the writer last wrote there, whether the writer leaves it alone, whether it may draw past 16 layers in the
/// renderer's wide variant, and a seed that re-rolls its wilds alone.
/// </summary>
[Serializable]
public sealed class ChunkRecord
{
    [Tooltip("The chunk's coordinates in the world.")]
    [SerializeField] private Vector2Int coords;
    [Tooltip("Hash of what the writer last wrote into the chunk; set by the writer.")]
    [SerializeField] private string writtenHash = string.Empty;
    [Tooltip("The writer never writes the chunk, so hand work in it stays.")]
    [SerializeField] private bool locked;
    [Tooltip("The chunk may list more than 16 materials, drawn by the renderer's slower wide variant.")]
    [SerializeField] private bool allowWide;
    [Tooltip("Mixed into the seed of the chunk's wilds stream: change it to lay the chunk's wilds patches and mobs again, leaving every other chunk's.")]
    [SerializeField] private int wildsSeed;

    /// <summary>
    /// Creates an empty record, for Unity serialization and the inspector.
    /// </summary>
    public ChunkRecord()
    {
    }

    /// <summary>
    /// Creates the record of the chunk at <paramref name="chunkCoords"/>, for tests and tools.
    /// </summary>
    public ChunkRecord(Vector2Int chunkCoords, bool isLocked, bool mayBeWide, int chunkWildsSeed)
    {
        coords = chunkCoords;
        locked = isLocked;
        allowWide = mayBeWide;
        wildsSeed = chunkWildsSeed;
    }

    public Vector2Int Coords => coords;
    public string WrittenHash => writtenHash;
    public bool Locked => locked;
    public bool AllowWide => allowWide;
    public int WildsSeed => wildsSeed;

    /// <summary>
    /// Records <paramref name="hash"/> as the hash of what the writer last wrote into the chunk.
    /// </summary>
    internal void SetWrittenHash(string hash)
    {
        writtenHash = hash ?? string.Empty;
    }
}
