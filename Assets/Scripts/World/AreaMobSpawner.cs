using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Area-scope entry point that spawns one mob per <see cref="MobSpawnPoint"/> into the area scene. Offline or on
/// the host it instantiates them through the area container and, in a session, spawns them as network objects; on
/// a client it registers the mob prefabs so the host's mobs arrive injected and in this area's scene. Shares its
/// spawning with <see cref="ChunkMobSpawner"/> through <see cref="MobSpawning"/>.
/// </summary>
public sealed class AreaMobSpawner : IStartable, IDisposable
{
    private readonly IObjectResolver resolver;
    private readonly MobSpawnPoint[] spawnPoints;
    private readonly Scene scene;
    private readonly INetworkObjectSpawner networkObjects;
    private readonly List<NetworkObject> registeredPrefabs = new();

    /// <summary>
    /// Creates a spawner for the area <paramref name="scene"/>'s <paramref name="spawnPoints"/>, instantiating
    /// through <paramref name="resolver"/>.
    /// </summary>
    public AreaMobSpawner(IObjectResolver resolver, MobSpawnPoint[] spawnPoints, Scene scene, INetworkObjectSpawner networkObjects)
    {
        this.resolver = resolver;
        this.spawnPoints = spawnPoints;
        this.scene = scene;
        this.networkObjects = networkObjects;
    }

    /// <summary>
    /// Client only: registers each distinct networked mob prefab so the copies the host spawns are injected by the
    /// area container and moved into the area scene. Runs from the area scope's build callback; does nothing
    /// offline or on the host.
    /// </summary>
    public void RegisterNetworkPrefabs()
    {
        if (!MobSpawning.IsClient(networkObjects) || registeredPrefabs.Count > 0)
        {
            return;
        }

        MobSpawning.CollectNetworkPrefabs(spawnPoints, registeredPrefabs);
        foreach (NetworkObject prefab in registeredPrefabs)
        {
            networkObjects.RegisterPrefab(prefab, resolver, scene);
        }
    }

    /// <summary>
    /// Spawns the area's mobs when this machine has authority (offline or the host); clients wait for the host's.
    /// </summary>
    public void Start()
    {
        if (MobSpawning.HasAuthority(networkObjects))
        {
            SpawnAll();
        }
    }

    /// <summary>
    /// Instantiates each spawn point's mob prefab at its position in the area scene, and network-spawns it in a
    /// session. Logs an error for a spawn point without a prefab and skips it.
    /// </summary>
    public void SpawnAll()
    {
        foreach (MobSpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint == null)
            {
                continue;
            }

            MobController mob = MobSpawning.Instantiate(spawnPoint, resolver, scene);
            if (mob != null)
            {
                MobSpawning.NetworkSpawn(mob, networkObjects);
            }
        }
    }

    /// <summary>
    /// Unregisters the prefab handlers added by <see cref="RegisterNetworkPrefabs"/> when the area scope goes away.
    /// </summary>
    public void Dispose()
    {
        foreach (NetworkObject prefab in registeredPrefabs)
        {
            networkObjects.UnregisterPrefab(prefab);
        }

        registeredPrefabs.Clear();
    }
}
