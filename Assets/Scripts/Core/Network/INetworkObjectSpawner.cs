using Unity.Netcode;
using UnityEngine.SceneManagement;
using VContainer;

/// <summary>
/// The slice of the network session that scene code needs to register and spawn networked objects, so EditMode tests
/// can fake it.
/// </summary>
public interface INetworkObjectSpawner
{
    bool IsActive { get; }
    bool IsServer { get; }

    /// <summary>
    /// Makes client copies of <paramref name="prefab"/> be instantiated through <paramref name="resolver"/> (so they
    /// are injected) and moved into <paramref name="targetScene"/> when it is loaded.
    /// </summary>
    void RegisterPrefab(NetworkObject prefab, IObjectResolver resolver, Scene targetScene);
    /// <summary>
    /// Makes client copies of <paramref name="prefab"/> be created and destroyed by <paramref name="handler"/>, for
    /// prefabs whose copies need choosing a resolver and scene per instance. Netcode keeps one handler per prefab.
    /// </summary>
    void RegisterPrefab(NetworkObject prefab, INetworkPrefabInstanceHandler handler);
    /// <summary>
    /// Removes the handler <see cref="RegisterPrefab(NetworkObject, IObjectResolver, Scene)"/> added for
    /// <paramref name="prefab"/>.
    /// </summary>
    void UnregisterPrefab(NetworkObject prefab);
    /// <summary>
    /// Spawns <paramref name="instance"/> as a server-owned network object (host only) that reaches every client once
    /// it has joined and is destroyed with its scene.
    /// </summary>
    void Spawn(NetworkObject instance);
    /// <summary>
    /// Spawns <paramref name="instance"/> as a server-owned network object (host only) whose home is
    /// <paramref name="homeChunk"/>: it reaches each client that holds that chunk, is created there in that chunk's
    /// scope, and is destroyed with its scene.
    /// </summary>
    void Spawn(NetworkObject instance, ChunkId homeChunk);
    /// <summary>
    /// Host only: despawns <paramref name="instance"/> so every client drops its copy, and destroys it; destroys one that
    /// is not spawned. Nothing for null.
    /// </summary>
    void Despawn(NetworkObject instance);
}
