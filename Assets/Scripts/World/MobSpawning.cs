using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

/// <summary>
/// What <see cref="ChunkMobSpawner"/> spawns mobs with: who spawns mobs, the distinct networked
/// mob prefabs of a set of spawn points, and spawning and despawning one mob.
/// </summary>
public static class MobSpawning
{
    /// <summary>
    /// Whether this machine spawns mobs itself: offline or the host. A client receives the host's.
    /// </summary>
    public static bool HasAuthority(INetworkObjectSpawner networkObjects)
    {
        return !networkObjects.IsActive || networkObjects.IsServer;
    }

    /// <summary>
    /// Whether this machine is a client of a session, which registers the mob prefabs instead of spawning.
    /// </summary>
    public static bool IsClient(INetworkObjectSpawner networkObjects)
    {
        return networkObjects.IsActive && !networkObjects.IsServer;
    }

    /// <summary>
    /// Adds to <paramref name="prefabs"/> the <see cref="NetworkObject"/> of each distinct mob prefab of
    /// <paramref name="spawnPoints"/>, skipping null spawn points, spawn points without a prefab and prefabs without a
    /// <see cref="NetworkObject"/>.
    /// </summary>
    public static void CollectNetworkPrefabs(IReadOnlyList<MobSpawnPoint> spawnPoints, List<NetworkObject> prefabs)
    {
        for (int i = 0; i < spawnPoints.Count; i++)
        {
            MobSpawnPoint spawnPoint = spawnPoints[i];
            if (spawnPoint == null || spawnPoint.MobPrefab == null)
            {
                continue;
            }

            NetworkObject prefab = spawnPoint.MobPrefab.GetComponent<NetworkObject>();
            if (prefab != null && !prefabs.Contains(prefab))
            {
                prefabs.Add(prefab);
            }
        }
    }

    /// <summary>
    /// Instantiates <paramref name="spawnPoint"/>'s mob prefab through <paramref name="resolver"/> at the point's
    /// position and moves it into <paramref name="scene"/>. Null, with an error, for a spawn point without a prefab.
    /// </summary>
    public static MobController Instantiate(MobSpawnPoint spawnPoint, IObjectResolver resolver, Scene scene)
    {
        if (spawnPoint.MobPrefab == null)
        {
            Debug.LogError($"{spawnPoint.name} has no mob prefab assigned; nothing spawns there.", spawnPoint);
            return null;
        }

        MobController mob = resolver.Instantiate(spawnPoint.MobPrefab, spawnPoint.transform.position, Quaternion.identity);
        mob.name = spawnPoint.MobPrefab.name;
        SceneManager.MoveGameObjectToScene(mob.gameObject, scene);
        return mob;
    }

    /// <summary>
    /// Spawns <paramref name="mob"/> as a network object when a session is active and it has a
    /// <see cref="NetworkObject"/>; nothing offline.
    /// </summary>
    public static void NetworkSpawn(MobController mob, INetworkObjectSpawner networkObjects)
    {
        if (networkObjects.IsActive && mob.TryGetComponent(out NetworkObject networkObject))
        {
            networkObjects.Spawn(networkObject);
        }
    }

    /// <summary>
    /// Spawns <paramref name="mob"/> as a network object of its <paramref name="homeChunk"/> when a session is active
    /// and it has a <see cref="NetworkObject"/>; nothing offline.
    /// </summary>
    public static void NetworkSpawn(MobController mob, INetworkObjectSpawner networkObjects, ChunkId homeChunk)
    {
        if (networkObjects.IsActive && mob.TryGetComponent(out NetworkObject networkObject))
        {
            networkObjects.Spawn(networkObject, homeChunk);
        }
    }

    /// <summary>
    /// Takes <paramref name="mob"/> out of the game: network-despawns it in a session, so clients drop their copies,
    /// and destroys it offline. Nothing for a mob already destroyed.
    /// </summary>
    public static void Despawn(MobController mob, INetworkObjectSpawner networkObjects)
    {
        if (mob == null)
        {
            return;
        }

        if (networkObjects.IsActive && mob.TryGetComponent(out NetworkObject networkObject))
        {
            networkObjects.Despawn(networkObject);
        }
        else if (Application.isPlaying)
        {
            Object.Destroy(mob.gameObject);
        }
        else
        {
            Object.DestroyImmediate(mob.gameObject);
        }
    }
}
