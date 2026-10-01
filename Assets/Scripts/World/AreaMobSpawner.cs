using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

public sealed class AreaMobSpawner : IStartable, IDisposable
{
    private readonly IObjectResolver resolver;
    private readonly MobSpawnPoint[] spawnPoints;
    private readonly Scene scene;
    private readonly INetworkObjectSpawner networkObjects;
    private readonly List<NetworkObject> registeredPrefabs = new();

    public AreaMobSpawner(IObjectResolver resolver, MobSpawnPoint[] spawnPoints, Scene scene, INetworkObjectSpawner networkObjects)
    {
        this.resolver = resolver;
        this.spawnPoints = spawnPoints;
        this.scene = scene;
        this.networkObjects = networkObjects;
    }

    public void RegisterNetworkPrefabs()
    {
        if (!networkObjects.IsActive || networkObjects.IsServer)
        {
            return;
        }

        foreach (MobSpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint == null || spawnPoint.MobPrefab == null)
            {
                continue;
            }

            NetworkObject prefab = spawnPoint.MobPrefab.GetComponent<NetworkObject>();
            if (prefab != null && !registeredPrefabs.Contains(prefab))
            {
                networkObjects.RegisterPrefab(prefab, resolver, scene);
                registeredPrefabs.Add(prefab);
            }
        }
    }

    public void Start()
    {
        if (!networkObjects.IsActive || networkObjects.IsServer)
        {
            SpawnAll();
        }
    }

    public void SpawnAll()
    {
        foreach (MobSpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint == null)
            {
                continue;
            }

            if (spawnPoint.MobPrefab == null)
            {
                Debug.LogError($"{spawnPoint.name} has no mob prefab assigned; nothing spawns there.", spawnPoint);
                continue;
            }

            MobController mob = resolver.Instantiate(spawnPoint.MobPrefab, spawnPoint.transform.position, Quaternion.identity);
            mob.name = spawnPoint.MobPrefab.name;
            SceneManager.MoveGameObjectToScene(mob.gameObject, scene);

            if (networkObjects.IsActive && mob.TryGetComponent(out NetworkObject networkObject))
            {
                networkObjects.Spawn(networkObject);
            }
        }
    }

    public void Dispose()
    {
        foreach (NetworkObject prefab in registeredPrefabs)
        {
            networkObjects.UnregisterPrefab(prefab);
        }

        registeredPrefabs.Clear();
    }
}
