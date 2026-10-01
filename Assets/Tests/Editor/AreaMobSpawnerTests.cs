using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

public class AreaMobSpawnerTests
{
    private GameObject spawnPointObject;

    [TearDown]
    public void TearDown()
    {
        if (spawnPointObject != null)
        {
            Object.DestroyImmediate(spawnPointObject);
        }
    }

    [Test]
    public void SpawnAll_LogsAnErrorAndSkipsSpawnPointsWithoutAPrefab()
    {
        spawnPointObject = new GameObject("MobSpawn_Empty");
        MobSpawnPoint spawnPoint = spawnPointObject.AddComponent<MobSpawnPoint>();
        int mobsBefore = Object.FindObjectsByType<MobController>().Length;
        AreaMobSpawner spawner = new(new ContainerBuilder().Build(), new[] { spawnPoint, null }, SceneManager.GetActiveScene());

        LogAssert.Expect(LogType.Error, "MobSpawn_Empty has no mob prefab assigned; nothing spawns there.");
        spawner.SpawnAll();

        Assert.That(Object.FindObjectsByType<MobController>().Length, Is.EqualTo(mobsBefore));
    }
}
