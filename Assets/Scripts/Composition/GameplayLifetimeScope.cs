using VContainer;
using VContainer.Unity;

// Scope of the Gameplay scene (player + HUD), alive for one play session. Parent of area scopes.
public class GameplayLifetimeScope : LifetimeScope
{
    protected override LifetimeScope FindParent()
    {
        return Find<MainLifetimeScope>();
    }

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterComponentInHierarchy<PlayerController>();
        builder.RegisterComponentInHierarchy<PlayerHudController>();
        builder.RegisterEntryPoint<GameplayEntryPoint>();
    }
}
