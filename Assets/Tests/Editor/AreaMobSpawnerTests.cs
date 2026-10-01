using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

public class AreaMobSpawnerTests
{
    private const string WeaselPrefabPath = "Assets/Prefabs/Mobs/Weasel.prefab";

    private readonly List<GameObject> createdObjects = new();

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

    [Test]
    public void Offline_RegistersNothing()
    {
        MobSpawnPoint spawnPoint = CreateSpawnPoint("MobSpawn_A", AssetDatabase.LoadAssetAtPath<MobController>(WeaselPrefabPath));
        FakeNetworkObjects network = new();
        AreaMobSpawner spawner = new(new ContainerBuilder().Build(), new[] { spawnPoint }, SceneManager.GetActiveScene(), network);

        spawner.RegisterNetworkPrefabs();

        Assert.That(network.Registered, Is.Empty);
    }

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

    private sealed class FakeNetworkObjects : INetworkObjectSpawner
    {
        public bool IsActive { get; set; }
        public bool IsServer { get; set; }
        public List<NetworkObject> Registered { get; } = new();
        public List<NetworkObject> Spawned { get; } = new();

        public void RegisterPrefab(NetworkObject prefab, IObjectResolver resolver, Scene targetScene)
        {
            Registered.Add(prefab);
        }

        public void UnregisterPrefab(NetworkObject prefab)
        {
            Registered.Remove(prefab);
        }

        public void Spawn(NetworkObject instance)
        {
            Spawned.Add(instance);
        }
    }
}
