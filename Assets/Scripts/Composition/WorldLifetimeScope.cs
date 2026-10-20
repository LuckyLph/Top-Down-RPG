using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// The composition root of a world's root scene, a child of the Gameplay scope created as <see cref="WorldStreaming"/>
/// loads the world, and the parent of its chunks' scopes. Its <see cref="WorldDefinition"/> comes from the streaming's
/// enqueued registration. It registers the world's <see cref="WorldNavigation"/>, which its chunks add their pages to,
/// and its <see cref="WorldPlayers"/>, the players its chunks' mobs consider.
/// </summary>
public class WorldLifetimeScope : LifetimeScope
{
    /// <summary>
    /// Registers the world's <see cref="WorldNavigation"/> (with the Gameplay scope's shared
    /// <see cref="NavigationPagePool"/>), also as <see cref="INavigation2D"/>, and its <see cref="WorldPlayers"/> (the
    /// Gameplay scope's players narrowed to this world), also as <see cref="IPlayerRegistry"/> so that whatever this scope
    /// and its chunks' scopes inject sees only the players in this world; both disposed with the scope. Logs an error and
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
        LifetimeScope gameplayScope = Parent;
        builder.Register(
                resolver => new WorldPlayers(
                    gameplayScope.Container.Resolve<IPlayerRegistry>(),
                    resolver.Resolve<PlayerWorlds>(),
                    resolver.Resolve<WorldDefinition>()),
                Lifetime.Singleton)
            .As<IPlayerRegistry>()
            .AsSelf();
    }
}
