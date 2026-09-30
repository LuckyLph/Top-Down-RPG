using UnityEngine;
using VContainer;
using VContainer.Unity;

// Scope of the Gameplay scene (player, HUD, combat presentation), alive for one play session.
// Parent of area scopes, so area objects resolve the session's player and combat services from here.
public class GameplayLifetimeScope : LifetimeScope
{
    [SerializeField] private PlayerController player;
    [SerializeField] private DamagePopupLayer damagePopupLayer;

    protected override LifetimeScope FindParent()
    {
        return Find<MainLifetimeScope>();
    }

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterInstance(player);
        builder.RegisterComponent(damagePopupLayer);
        builder.RegisterComponentInHierarchy<PlayerHudController>();

        builder.Register<CombatEvents>(Lifetime.Singleton);
        builder.Register<SlashSpawner>(Lifetime.Singleton);

        builder.RegisterEntryPoint<DamagePopupPresenter>();
        builder.RegisterEntryPoint<GameplayEntryPoint>();

        // Every component on the player (controller, weapon, damage receiver) gets its dependencies.
        builder.RegisterBuildCallback(resolver => resolver.InjectGameObject(player.gameObject));
    }
}
