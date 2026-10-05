using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// Tests for <see cref="AreaMobSpawner"/>: invalid spawn points, and network prefab registration on clients and
/// offline.
/// </summary>
public class AreaMobSpawnerTests
{
    private const string WeaselPrefabPath = "Assets/Prefabs/Mobs/Weasel.prefab";

    private readonly List<GameObject> createdObjects = new();

    /// <summary>
    /// Destroys the spawn point objects the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (GameObject createdObject in createdObjects)
        {
            if (createdObject != null)
            {
                Object.DestroyImmediate(createdObject);
            }
        }

        createdObjects.Clear();
    }

    /// <summary>
    /// Spawning skips a null spawn point and logs an error for a spawn point with no mob prefab, creating no mobs.
    /// </summary>
    [Test]
    public void SpawnAll_LogsAnErrorAndSkipsSpawnPointsWithoutAPrefab()
    {
        MobSpawnPoint spawnPoint = CreateSpawnPoint("MobSpawn_Empty", null);
        int mobsBefore = Object.FindObjectsByType<MobController>().Length;
        AreaMobSpawner spawner = new(new ContainerBuilder().Build(), new[] { spawnPoint, null }, SceneManager.GetActiveScene(), new FakeNetworkObjects());

        LogAssert.Expect(LogType.Error, "MobSpawn_Empty has no mob prefab assigned; nothing spawns there.");
        spawner.SpawnAll();

        Assert.That(Object.FindObjectsByType<MobController>().Length, Is.EqualTo(mobsBefore));
    }

    /// <summary>
    /// On a connected client the spawner registers each distinct mob prefab once, spawns no mobs of its own, and
    /// unregisters the prefabs when disposed.
    /// </summary>
    [Test]
    public void OnAClient_RegistersEachMobPrefabOnce_SpawnsNothing_AndUnregistersOnDispose()
    {
        MobController weasel = AssetDatabase.LoadAssetAtPath<MobController>(WeaselPrefabPath);
        MobSpawnPoint first = CreateSpawnPoint("MobSpawn_A", weasel);
        MobSpawnPoint second = CreateSpawnPoint("MobSpawn_B", weasel);
        FakeNetworkObjects network = new() { IsActive = true, IsServer = false };
        int mobsBefore = Object.FindObjectsByType<MobController>().Length;
        AreaMobSpawner spawner = new(new ContainerBuilder().Build(), new[] { first, second }, SceneManager.GetActiveScene(), network);

        spawner.RegisterNetworkPrefabs();
        spawner.Start();

        Assert.That(network.Registered, Is.EquivalentTo(new[] { weasel.GetComponent<NetworkObject>() }));
        Assert.That(network.Spawned, Is.Empty);
        Assert.That(Object.FindObjectsByType<MobController>().Length, Is.EqualTo(mobsBefore), "Clients receive mobs from the host instead of spawning their own.");

        spawner.Dispose();
        Assert.That(network.Registered, Is.Empty);
    }

    /// <summary>
    /// With no active network session the spawner registers no network prefabs.
    /// </summary>
    [Test]
    public void Offline_RegistersNothing()
    {
        MobSpawnPoint spawnPoint = CreateSpawnPoint("MobSpawn_A", AssetDatabase.LoadAssetAtPath<MobController>(WeaselPrefabPath));
        FakeNetworkObjects network = new();
        AreaMobSpawner spawner = new(new ContainerBuilder().Build(), new[] { spawnPoint }, SceneManager.GetActiveScene(), network);

        spawner.RegisterNetworkPrefabs();

        Assert.That(network.Registered, Is.Empty);
    }

    /// <summary>
    /// Creates a tracked mob spawn point named <paramref name="name"/> that spawns <paramref name="prefab"/>
    /// (null for none).
    /// </summary>
    private MobSpawnPoint CreateSpawnPoint(string name, MobController prefab)
    {
        GameObject spawnObject = new(name);
        createdObjects.Add(spawnObject);
        MobSpawnPoint spawnPoint = spawnObject.AddComponent<MobSpawnPoint>();
        SerializedObject serialized = new(spawnPoint);
        serialized.FindProperty("mobPrefab").objectReferenceValue = prefab;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return spawnPoint;
    }
}
