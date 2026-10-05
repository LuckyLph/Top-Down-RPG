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
    /// Removes the handler <see cref="RegisterPrefab"/> added for <paramref name="prefab"/>.
    /// </summary>
    void UnregisterPrefab(NetworkObject prefab);
    /// <summary>
    /// Spawns <paramref name="instance"/> as a server-owned network object (host only) that reaches each client once
    /// it is ready and is destroyed with its scene.
    /// </summary>
    void Spawn(NetworkObject instance);
}
