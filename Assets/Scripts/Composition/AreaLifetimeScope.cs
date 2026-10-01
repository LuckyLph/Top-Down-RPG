using UnityEngine;
using VContainer;
using VContainer.Unity;

public class AreaLifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        if (Parent == null)
        {
            Debug.LogError(
                $"{name}: area scenes must be loaded by GameFlow under a Gameplay scene. " +
                "Press Play with 'Tools/TopDownRPG/Boot From Main' enabled.",
                this);
            return;
        }

        foreach (MobController placedMob in SceneQuery.FindAll<MobController>(gameObject.scene))
        {
            Debug.LogError($"{placedMob.name} is placed in the area scene; use a {nameof(MobSpawnPoint)} instead so it is spawned at runtime.", placedMob);
        }

        builder.Register<AreaEntry>(Lifetime.Singleton)
            .As<IAreaEntry>()
            .WithParameter(SceneQuery.FindAll<SpawnPoint>(gameObject.scene));
        builder.RegisterEntryPoint<AreaClientReady>();

        MobSpawnPoint[] mobSpawnPoints = SceneQuery.FindAll<MobSpawnPoint>(gameObject.scene);
        NavigationGrid2D navigationGrid = SceneQuery.FindFirst<NavigationGrid2D>(gameObject.scene);
        if (navigationGrid == null)
        {
            if (mobSpawnPoints.Length > 0)
            {
                Debug.LogError($"{name}: the area has mob spawn points but no {nameof(NavigationGrid2D)}; no mobs are spawned.", this);
            }

            return;
        }

        builder.RegisterComponent(navigationGrid);
        builder.RegisterEntryPoint<AreaMobSpawner>()
            .WithParameter(mobSpawnPoints)
            .WithParameter(gameObject.scene)
            .AsSelf();
        builder.RegisterBuildCallback(resolver => resolver.Resolve<AreaMobSpawner>().RegisterNetworkPrefabs());
    }
}
