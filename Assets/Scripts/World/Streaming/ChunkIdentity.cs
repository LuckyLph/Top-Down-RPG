using UnityEngine;

/// <summary>
/// Which chunk a chunk scene is. <see cref="WorldStreaming"/> registers it into the chunk scene's scope as the scene
/// loads.
/// </summary>
public sealed class ChunkIdentity
{
    /// <summary>
    /// Creates the identity of the chunk at <paramref name="coords"/> of <paramref name="world"/>.
    /// </summary>
    public ChunkIdentity(WorldDefinition world, Vector2Int coords)
    {
        World = world;
        Coords = coords;
    }

    public WorldDefinition World { get; }
    public Vector2Int Coords { get; }
}
