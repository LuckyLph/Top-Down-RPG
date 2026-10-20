using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// The composition root of a chunk scene, a child of its world's scope created as <see cref="WorldStreaming"/> loads
/// the chunk. Its <see cref="ChunkIdentity"/> comes from the streaming's enqueued registration, and its world's
/// <see cref="WorldNavigation"/> and <see cref="WorldPlayers"/> from the parent scope.
/// </summary>
public class ChunkLifetimeScope : LifetimeScope
{
    /// <summary>
    /// Registers the <see cref="ChunkNavigation"/> entry point that adds the chunk's <see cref="NavigationGrid2D"/> page
    /// to its world's navigation, and the <see cref="ChunkMobSpawner"/> of the chunk's mob spawn points, whose network
    /// prefabs a client registers once the container is built. Without a grid it logs an error and registers neither.
    /// Logs an error and registers nothing without a parent scope, and logs one for every mob placed directly in the
    /// chunk, since mobs come from spawn points.
    /// </summary>
    protected override void Configure(IContainerBuilder builder)
    {
        if (Parent == null)
        {
            Debug.LogError(
                $"{name}: chunk scenes must be loaded by the world streaming under their world's scene. " +
                "Press Play with 'Tools/TopDownRPG/Boot From Main' enabled.",
                this);
            return;
        }

        foreach (MobController placedMob in SceneQuery.FindAll<MobController>(gameObject.scene))
        {
            Debug.LogError($"{placedMob.name} is placed in the chunk scene; use a {nameof(MobSpawnPoint)} instead so it is spawned at runtime.", placedMob);
        }

        NavigationGrid2D navigationGrid = SceneQuery.FindFirst<NavigationGrid2D>(gameObject.scene);
        if (navigationGrid == null)
        {
            Debug.LogError($"{name}: the chunk has no {nameof(NavigationGrid2D)}; nothing can path on it and no mobs are spawned.", this);
            return;
        }

        builder.RegisterEntryPoint<ChunkNavigation>().WithParameter(navigationGrid);
        builder.RegisterEntryPoint<ChunkMobSpawner>()
            .WithParameter(SceneQuery.FindAll<MobSpawnPoint>(gameObject.scene))
            .WithParameter(gameObject.scene)
            .AsSelf();
        builder.RegisterBuildCallback(resolver => resolver.Resolve<ChunkMobSpawner>().RegisterNetworkPrefabs());
    }
}
