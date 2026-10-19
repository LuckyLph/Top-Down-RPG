using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

/// <summary>
/// A world loaded on this machine: its definition, its root scene and scope, its lighting, and the scenes of its
/// loaded chunks.
/// </summary>
public sealed class LoadedWorld
{
    private readonly Dictionary<Vector2Int, Scene> chunks = new();

    /// <summary>
    /// Records <paramref name="definition"/>'s root <paramref name="scene"/>, its <paramref name="scope"/> (the parent of
    /// its chunks' scopes) and its <paramref name="lighting"/> (null when the scene has none).
    /// </summary>
    public LoadedWorld(WorldDefinition definition, Scene scene, LifetimeScope scope, WorldLighting lighting)
    {
        Definition = definition;
        Scene = scene;
        Scope = scope;
        Lighting = lighting;
    }

    public WorldDefinition Definition { get; }
    public Scene Scene { get; }
    public LifetimeScope Scope { get; }
    public WorldLighting Lighting { get; }
    public IReadOnlyDictionary<Vector2Int, Scene> Chunks => chunks;

    /// <summary>
    /// Records the loaded scene of the chunk at <paramref name="coords"/>.
    /// </summary>
    public void AddChunk(Vector2Int coords, Scene scene)
    {
        chunks[coords] = scene;
    }

    /// <summary>
    /// Forgets the chunk at <paramref name="coords"/>.
    /// </summary>
    public void RemoveChunk(Vector2Int coords)
    {
        chunks.Remove(coords);
    }
}
