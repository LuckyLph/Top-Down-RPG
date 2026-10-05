using System.Collections.Generic;
using Unity.Netcode;
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

    /// <summary>
    /// Adds <paramref name="prefab"/> to <see cref="Registered"/>; the resolver and scene are ignored.
    /// </summary>
    public void RegisterPrefab(NetworkObject prefab, IObjectResolver resolver, Scene targetScene)
    {
        Registered.Add(prefab);
    }

    /// <summary>
    /// Removes one entry of <paramref name="prefab"/> from <see cref="Registered"/>.
    /// </summary>
    public void UnregisterPrefab(NetworkObject prefab)
    {
        Registered.Remove(prefab);
    }

    /// <summary>
    /// Adds <paramref name="instance"/> to <see cref="Spawned"/> without spawning it on the network.
    /// </summary>
    public void Spawn(NetworkObject instance)
    {
        Spawned.Add(instance);
    }
}
