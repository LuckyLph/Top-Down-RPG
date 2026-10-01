using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

public sealed class AreaMobSpawner
{
    private readonly IObjectResolver resolver;
    private readonly MobSpawnPoint[] spawnPoints;
    private readonly Scene scene;

    public AreaMobSpawner(IObjectResolver resolver, MobSpawnPoint[] spawnPoints, Scene scene)
    {
        this.resolver = resolver;
        this.spawnPoints = spawnPoints;
        this.scene = scene;
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
        }
    }
}
