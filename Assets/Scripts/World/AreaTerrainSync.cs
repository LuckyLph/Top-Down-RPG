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

    /// <summary>
    /// Creates the sync for <paramref name="terrain"/> in the area <paramref name="scene"/>, instantiating through
    /// the area's <paramref name="resolver"/>.
    /// </summary>
    public AreaTerrainSync(IObjectResolver resolver, AreaTerrain terrain, Scene scene, INetworkObjectSpawner networkObjects)
    {
        this.resolver = resolver;
        this.terrain = terrain;
        this.scene = scene;
        this.networkObjects = networkObjects;
    }

    /// <summary>
    /// Client only: registers the terrain's sync prefab so the host's copy is injected and moved into the area scene.
    /// Runs from the area scope's build callback; does nothing offline, on the host, without a sync prefab or when
    /// already registered.
    /// </summary>
    public void RegisterNetworkPrefab()
    {
        if (!networkObjects.IsActive || networkObjects.IsServer || terrain.SyncPrefab == null || registeredPrefab != null)
        {
            return;
        }

        registeredPrefab = terrain.SyncPrefab;
        networkObjects.RegisterPrefab(registeredPrefab, resolver, scene);
    }

    /// <summary>
    /// Host only: instantiates the sync prefab through the area container, moves it into the area scene and
    /// network-spawns it. Logs an error when the terrain has no sync prefab.
    /// </summary>
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

    /// <summary>
    /// Unregisters the prefab handler added by <see cref="RegisterNetworkPrefab"/>, if any.
    /// </summary>
    public void Dispose()
    {
        if (registeredPrefab != null)
        {
            networkObjects.UnregisterPrefab(registeredPrefab);
            registeredPrefab = null;
        }
    }
}
