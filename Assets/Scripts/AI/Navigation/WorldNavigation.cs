using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A world's navigation: a <see cref="NavigationMap2D"/> with a page per loaded chunk, each built from the chunk's
/// <see cref="NavigationGrid2D"/> over its 64 x 64 cells and placed in the <see cref="NavigationPagePool"/> every loaded
/// world shares. Steps, edge costs, cliff sides, cliff faces and regions read across the borders of loaded chunks; a
/// chunk that is not loaded is blocked, so paths stay inside the loaded chunks and a partial path toward a goal beyond
/// them ends at the loaded edge. Registered by the world's scope; each chunk's scope adds and removes its page
/// (<see cref="ChunkNavigation"/>).
/// </summary>
public sealed class WorldNavigation : NavigationMap2D, IDisposable
{
    private readonly Dictionary<Vector2Int, NavigationGrid2D> chunks = new();
    private readonly List<Vector2Int> removing = new();

    /// <summary>
    /// The navigation of a world of up to <see cref="WorldDefinition.MaxChunksPerSide"/> chunks a side, its pages in
    /// <paramref name="pool"/>.
    /// </summary>
    public WorldNavigation(NavigationPagePool pool)
        : base(
            pool,
            Vector2Int.zero,
            new Vector2Int(ChunkCoords.Size, ChunkCoords.Size),
            new Vector2Int(WorldDefinition.MaxChunksPerSide, WorldDefinition.MaxChunksPerSide),
            singleArea: false)
    {
    }

    /// <summary>
    /// Whether the chunk at <paramref name="coords"/> has its page here.
    /// </summary>
    public bool HasChunk(Vector2Int coords)
    {
        return chunks.ContainsKey(coords);
    }

    /// <summary>
    /// Builds <paramref name="grid"/>'s page over the cells of the chunk at <paramref name="coords"/> and joins it to the
    /// pages of the loaded chunks around it. False, with an error, for a chunk outside the world or already added, or a
    /// grid that cannot build.
    /// </summary>
    public bool AddChunk(NavigationGrid2D grid, Vector2Int coords)
    {
        if (grid == null)
        {
            return false;
        }

        if (coords.x < 0 || coords.y < 0 || coords.x >= WorldDefinition.MaxChunksPerSide || coords.y >= WorldDefinition.MaxChunksPerSide
            || chunks.ContainsKey(coords))
        {
            Debug.LogError($"{nameof(WorldNavigation)} cannot add a chunk at {coords}: it is outside the world or already added.", grid);
            return false;
        }

        if (!grid.PlaceIn(this, ChunkCoords.Bounds(coords)))
        {
            return false;
        }

        chunks.Add(coords, grid);
        return true;
    }

    /// <summary>
    /// Takes the page of the chunk at <paramref name="coords"/> out, so its cells are blocked again; nothing when it has
    /// none.
    /// </summary>
    public void RemoveChunk(Vector2Int coords)
    {
        if (chunks.Remove(coords, out NavigationGrid2D grid) && !ReferenceEquals(grid, null))
        {
            grid.Unbuild();
        }
    }

    /// <summary>
    /// Takes every page out as the world's scope goes, freeing their slots in the shared pool.
    /// </summary>
    public void Dispose()
    {
        removing.Clear();
        removing.AddRange(chunks.Keys);
        for (int i = 0; i < removing.Count; i++)
        {
            RemoveChunk(removing[i]);
        }

        removing.Clear();
    }
}
