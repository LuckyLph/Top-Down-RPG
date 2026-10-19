using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// The composition root of a chunk scene, a child of its world's scope created as <see cref="WorldStreaming"/> loads
/// the chunk. Its <see cref="ChunkIdentity"/> comes from the streaming's enqueued registration.
/// </summary>
public class ChunkLifetimeScope : LifetimeScope
{
    /// <summary>
    /// Registers nothing of its own yet; logs an error without a parent scope, and for every mob placed directly in
    /// the chunk, since mobs come from spawn points.
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
    }
}
