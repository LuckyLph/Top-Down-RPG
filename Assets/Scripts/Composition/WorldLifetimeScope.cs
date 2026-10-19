using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// The composition root of a world's root scene, a child of the Gameplay scope created as <see cref="WorldStreaming"/>
/// loads the world, and the parent of its chunks' scopes. Its <see cref="WorldDefinition"/> comes from the streaming's
/// enqueued registration.
/// </summary>
public class WorldLifetimeScope : LifetimeScope
{
    /// <summary>
    /// Registers nothing of its own yet; logs an error without a parent scope, since world scenes are loaded by the
    /// world streaming under a Gameplay scene.
    /// </summary>
    protected override void Configure(IContainerBuilder builder)
    {
        if (Parent == null)
        {
            Debug.LogError(
                $"{name}: world scenes must be loaded by the world streaming under a Gameplay scene. " +
                "Press Play with 'Tools/TopDownRPG/Boot From Main' enabled.",
                this);
        }
    }
}
