using System;
using VContainer.Unity;

/// <summary>
/// A chunk scope's entry point that adds the chunk's navigation page to its world's <see cref="WorldNavigation"/> once
/// the scene is up (in <c>Start</c>, after every collider of the scene is in the physics world), and takes it out when the
/// chunk unloads and its scope is disposed.
/// </summary>
public sealed class ChunkNavigation : IStartable, IDisposable
{
    private readonly WorldNavigation navigation;
    private readonly ChunkIdentity identity;
    private readonly NavigationGrid2D grid;
    private bool added;

    /// <summary>
    /// Adds <paramref name="chunkGrid"/>'s page for the chunk <paramref name="chunk"/> to
    /// <paramref name="worldNavigation"/>.
    /// </summary>
    public ChunkNavigation(WorldNavigation worldNavigation, ChunkIdentity chunk, NavigationGrid2D chunkGrid)
    {
        navigation = worldNavigation;
        identity = chunk;
        grid = chunkGrid;
    }

    /// <summary>
    /// Builds the page and joins it to the loaded chunks around it.
    /// </summary>
    public void Start()
    {
        added = navigation.AddChunk(grid, identity.Coords);
    }

    /// <summary>
    /// Takes the page out again.
    /// </summary>
    public void Dispose()
    {
        if (added)
        {
            navigation.RemoveChunk(identity.Coords);
            added = false;
        }
    }
}
