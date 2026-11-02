using UnityEngine;
using UnityEngine.Rendering.Universal;
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
    /// prefabs it registers in a session once the container is built, and, when the chunk has an
    /// <see cref="AreaTerrain"/>, that terrain and its <see cref="ChunkTerrainSync"/>, whose prefab is registered the same
    /// way, and, when it has props, the <see cref="ChunkPropFading"/> that hands them to the Gameplay scope's
    /// <see cref="PropFading"/>, and, when it has a <see cref="TerrainBlendRenderer"/>, that renderer, injected with the
    /// Gameplay scope's <see cref="SharedTerrainArrays"/> before it first builds, and, when it has prop lights, props'
    /// shadow casters or a renderer, the <see cref="ChunkPropLights"/> that hands them and its ground's glow to the
    /// Gameplay scope's <see cref="PropLighting"/>. Without a grid it logs an error and registers neither the navigation nor the mobs.
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

        AreaTerrain terrain = SceneQuery.FindFirst<AreaTerrain>(gameObject.scene);
        if (terrain != null)
        {
            builder.RegisterComponent(terrain);
            builder.RegisterEntryPoint<ChunkTerrainSync>()
                .WithParameter(gameObject.scene)
                .AsSelf();
            builder.RegisterBuildCallback(resolver => resolver.Resolve<ChunkTerrainSync>().RegisterNetworkPrefab());
        }

        TerrainBlendRenderer ground = SceneQuery.FindFirst<TerrainBlendRenderer>(gameObject.scene);
        if (ground != null)
        {
            builder.RegisterComponent(ground);
        }

        PropFade[] props = SceneQuery.FindAll<PropFade>(gameObject.scene);
        if (props.Length > 0)
        {
            builder.RegisterEntryPoint<ChunkPropFading>().WithParameter(props);
        }

        PropLight[] lights = SceneQuery.FindAll<PropLight>(gameObject.scene);
        ShadowCaster2D[] casters = ChunkPropLights.CastersOf(props);
        if (lights.Length > 0 || casters.Length > 0 || ground != null)
        {
            builder.RegisterEntryPoint<ChunkPropLights>()
                .WithParameter(lights)
                .WithParameter(casters)
                .WithParameter(typeof(TerrainBlendRenderer), ground);
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
