using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// The composition root of an area scene, a child of the Gameplay scope created as <see cref="GameFlow"/> loads the
/// area. Registers what the scene contains: its entry, client-ready signal, terrain sync and mob spawner.
/// </summary>
public class AreaLifetimeScope : LifetimeScope
{
    /// <summary>
    /// Registers the <see cref="AreaEntry"/> with the scene's spawn points and navigation grid and the
    /// <see cref="AreaClientReady"/> entry point; adds <see cref="AreaTerrainSync"/> when the scene has an
    /// <see cref="AreaTerrain"/>, and the navigation grid and <see cref="AreaMobSpawner"/> when it has a grid, with
    /// their network prefabs registered once the container is built. Logs an error and registers nothing without a
    /// parent scope, and logs errors for mobs placed directly in the scene and for mob spawn points without a grid.
    /// </summary>
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

        NavigationGrid2D navigationGrid = SceneQuery.FindFirst<NavigationGrid2D>(gameObject.scene);
        builder.Register<AreaEntry>(Lifetime.Singleton)
            .As<IAreaEntry>()
            .WithParameter(SceneQuery.FindAll<SpawnPoint>(gameObject.scene))
            .WithParameter(navigationGrid);
        builder.RegisterEntryPoint<AreaClientReady>();

        AreaTerrain areaTerrain = SceneQuery.FindFirst<AreaTerrain>(gameObject.scene);
        if (areaTerrain != null)
        {
            builder.RegisterComponent(areaTerrain);
            builder.RegisterEntryPoint<AreaTerrainSync>()
                .WithParameter(gameObject.scene)
                .AsSelf();
            builder.RegisterBuildCallback(resolver => resolver.Resolve<AreaTerrainSync>().RegisterNetworkPrefab());
        }

        MobSpawnPoint[] mobSpawnPoints = SceneQuery.FindAll<MobSpawnPoint>(gameObject.scene);
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
