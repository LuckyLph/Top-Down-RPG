using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;

/// <summary>
/// Records prefab registrations and spawns instead of talking to Netcode, for area entry point tests.
/// </summary>
public sealed class FakeNetworkObjects : INetworkObjectSpawner
{
    public bool IsActive { get; set; }
    public bool IsServer { get; set; }
    public List<NetworkObject> Registered { get; } = new();
    public List<NetworkObject> Spawned { get; } = new();
    public List<NetworkObject> Despawned { get; } = new();
    public Dictionary<NetworkObject, INetworkPrefabInstanceHandler> Handlers { get; } = new();
    public Dictionary<NetworkObject, ChunkId> HomeChunks { get; } = new();

    /// <summary>
    /// Adds <paramref name="prefab"/> to <see cref="Registered"/>; the resolver and scene are ignored.
    /// </summary>
    public void RegisterPrefab(NetworkObject prefab, IObjectResolver resolver, Scene targetScene)
    {
        Registered.Add(prefab);
    }

    /// <summary>
    /// Adds <paramref name="prefab"/> to <see cref="Registered"/> and records its <paramref name="handler"/> in
    /// <see cref="Handlers"/>.
    /// </summary>
    public void RegisterPrefab(NetworkObject prefab, INetworkPrefabInstanceHandler handler)
    {
        Registered.Add(prefab);
        Handlers[prefab] = handler;
    }

    /// <summary>
    /// Removes one entry of <paramref name="prefab"/> from <see cref="Registered"/>, and its handler.
    /// </summary>
    public void UnregisterPrefab(NetworkObject prefab)
    {
        Registered.Remove(prefab);
        Handlers.Remove(prefab);
    }

    /// <summary>
    /// Adds <paramref name="instance"/> to <see cref="Spawned"/> without spawning it on the network.
    /// </summary>
    public void Spawn(NetworkObject instance)
    {
        Spawned.Add(instance);
    }

    /// <summary>
    /// Adds <paramref name="instance"/> to <see cref="Spawned"/> and records <paramref name="homeChunk"/> in
    /// <see cref="HomeChunks"/>, without spawning it on the network.
    /// </summary>
    public void Spawn(NetworkObject instance, ChunkId homeChunk)
    {
        Spawned.Add(instance);
        HomeChunks[instance] = homeChunk;
    }

    /// <summary>
    /// Adds <paramref name="instance"/> to <see cref="Despawned"/> and destroys its object at once.
    /// </summary>
    public void Despawn(NetworkObject instance)
    {
        Despawned.Add(instance);
        Object.DestroyImmediate(instance.gameObject);
    }
}
