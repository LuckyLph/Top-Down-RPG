using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gameplay-scope registry of the worlds loaded on this machine and their chunks, kept by <see cref="WorldStreaming"/>.
/// Objects that outlive a world (players, the presentation) find a world's scope and scenes here.
/// </summary>
public sealed class Worlds
{
    private readonly List<LoadedWorld> loaded = new();

    public event Action<LoadedWorld> WorldLoaded;
    public event Action<LoadedWorld> WorldUnloading;
    public event Action<LoadedWorld, Vector2Int> ChunkLoaded;
    public event Action<LoadedWorld, Vector2Int> ChunkUnloading;

    public IReadOnlyList<LoadedWorld> Loaded => loaded;

    /// <summary>
    /// The loaded world of <paramref name="definition"/>; false when it is not loaded.
    /// </summary>
    public bool TryGet(WorldDefinition definition, out LoadedWorld world)
    {
        for (int i = 0; i < loaded.Count; i++)
        {
            if (loaded[i].Definition == definition)
            {
                world = loaded[i];
                return true;
            }
        }

        world = null;
        return false;
    }

    /// <summary>
    /// Records a world whose root scene has loaded and raises <see cref="WorldLoaded"/>.
    /// </summary>
    public void Add(LoadedWorld world)
    {
        loaded.Add(world);
        WorldLoaded?.Invoke(world);
    }

    /// <summary>
    /// Raises <see cref="WorldUnloading"/> and forgets <paramref name="world"/>, before its root scene unloads.
    /// </summary>
    public void Remove(LoadedWorld world)
    {
        WorldUnloading?.Invoke(world);
        loaded.Remove(world);
    }

    /// <summary>
    /// Records the loaded chunk at <paramref name="coords"/> of <paramref name="world"/> and raises
    /// <see cref="ChunkLoaded"/>.
    /// </summary>
    public void AddChunk(LoadedWorld world, Vector2Int coords, UnityEngine.SceneManagement.Scene scene)
    {
        world.AddChunk(coords, scene);
        ChunkLoaded?.Invoke(world, coords);
    }

    /// <summary>
    /// Raises <see cref="ChunkUnloading"/> and forgets the chunk at <paramref name="coords"/> of
    /// <paramref name="world"/>, before its scene unloads.
    /// </summary>
    public void RemoveChunk(LoadedWorld world, Vector2Int coords)
    {
        ChunkUnloading?.Invoke(world, coords);
        world.RemoveChunk(coords);
    }
}
