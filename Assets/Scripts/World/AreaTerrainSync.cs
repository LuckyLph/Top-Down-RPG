using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Area entry point for an area with an <see cref="AreaTerrain"/>. The host spawns its <see cref="NetworkAreaTerrain"/>
/// into the area scene; a client registers the prefab so the host's copy arrives injected with this area's terrain,
/// and unregisters it on dispose. Offline nothing is spawned: the edits only need to reach this machine.
/// </summary>
public sealed class AreaTerrainSync : IStartable, IDisposable
{
    private readonly IObjectResolver resolver;
    private readonly AreaTerrain terrain;
    private readonly Scene scene;
    private readonly INetworkObjectSpawner networkObjects;
    private NetworkObject registeredPrefab;

    public AreaTerrainSync(IObjectResolver resolver, AreaTerrain terrain, Scene scene, INetworkObjectSpawner networkObjects)
    {
        this.resolver = resolver;
        this.terrain = terrain;
        this.scene = scene;
        this.networkObjects = networkObjects;
    }

    public void RegisterNetworkPrefab()
    {
        if (!networkObjects.IsActive || networkObjects.IsServer || terrain.SyncPrefab == null || registeredPrefab != null)
        {
            return;
        }

        registeredPrefab = terrain.SyncPrefab;
        networkObjects.RegisterPrefab(registeredPrefab, resolver, scene);
    }

    public void Start()
    {
        if (!networkObjects.IsActive || !networkObjects.IsServer)
        {
            return;
        }

        if (terrain.SyncPrefab == null)
        {
            Debug.LogError($"{terrain.name} has no sync prefab; its terrain edits will not reach clients.", terrain);
            return;
        }

        NetworkObject instance = resolver.Instantiate(terrain.SyncPrefab);
        instance.name = terrain.SyncPrefab.name;
        SceneManager.MoveGameObjectToScene(instance.gameObject, scene);
        networkObjects.Spawn(instance);
    }

    public void Dispose()
    {
        if (registeredPrefab != null)
        {
            networkObjects.UnregisterPrefab(registeredPrefab);
            registeredPrefab = null;
        }
    }
}
