using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// The composition root of a world's root scene, a child of the Gameplay scope created as <see cref="WorldStreaming"/>
/// loads the world, and the parent of its chunks' scopes. Its <see cref="WorldDefinition"/> comes from the streaming's
/// enqueued registration. It registers the world's <see cref="WorldNavigation"/>, which its chunks add their pages to.
/// </summary>
public class WorldLifetimeScope : LifetimeScope
{
    /// <summary>
    /// Registers the world's <see cref="WorldNavigation"/> (with the Gameplay scope's shared
    /// <see cref="NavigationPagePool"/>), also as <see cref="INavigation2D"/>, disposed with the scope; logs an error and
    /// registers nothing without a parent scope, since world scenes are loaded by the world streaming under a Gameplay
    /// scene.
    /// </summary>
    protected override void Configure(IContainerBuilder builder)
    {
        if (Parent == null)
        {
            Debug.LogError(
                $"{name}: world scenes must be loaded by the world streaming under a Gameplay scene. " +
                "Press Play with 'Tools/TopDownRPG/Boot From Main' enabled.",
                this);
            return;
        }

        builder.Register<WorldNavigation>(Lifetime.Singleton)
            .As<INavigation2D>()
            .AsSelf();
    }
}
