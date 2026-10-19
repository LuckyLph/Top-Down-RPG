using UnityEngine;

/// <summary>
/// What a <see cref="ChunkStreamer"/> asks to load or unload next.
/// </summary>
public enum StreamActionKind
{
    LoadWorld,
    LoadChunk,
    UnloadChunk,
    UnloadWorld
}

/// <summary>
/// One load or unload a <see cref="ChunkStreamer"/> asks for: a world's root scene, or one of its chunks.
/// </summary>
public readonly struct StreamAction
{
    /// <summary>
    /// Creates an action of <paramref name="kind"/> on <paramref name="world"/>, and on its chunk at
    /// <paramref name="chunk"/> for a chunk action.
    /// </summary>
    public StreamAction(StreamActionKind kind, WorldDefinition world, Vector2Int chunk)
    {
        Kind = kind;
        World = world;
        Chunk = chunk;
    }

    public StreamActionKind Kind { get; }
    public WorldDefinition World { get; }
    public Vector2Int Chunk { get; }

    /// <summary>
    /// The action for logs and test messages.
    /// </summary>
    public override string ToString()
    {
        string world = World != null ? World.name : "no world";
        return Kind == StreamActionKind.LoadChunk || Kind == StreamActionKind.UnloadChunk ? $"{Kind} {world} {Chunk}" : $"{Kind} {world}";
    }
}
