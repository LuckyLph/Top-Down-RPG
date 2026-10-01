using UnityEngine;
using VContainer;
using VContainer.Unity;

public class GameplayLifetimeScope : LifetimeScope
{
    [SerializeField] private PlayerController player;
    [SerializeField] private DamagePopupLayer damagePopupLayer;
    [SerializeField] private GameplaySettings settings;

    protected override void Configure(IContainerBuilder builder)
    {
        if (Parent == null)
        {
            Debug.LogError(
                $"{name}: the Gameplay scene must be loaded by GameFlow from the Main scene. " +
                "Press Play with 'Tools/TopDownRPG/Boot From Main' enabled.",
                this);
            return;
        }

        LocalPlayer localPlayer = new(player);
        PlayerRegistry players = new();
        players.Add(localPlayer.Handle);
        builder.RegisterInstance(localPlayer);
        builder.RegisterInstance(players).As<IPlayerRegistry>().AsSelf();
        builder.RegisterComponent(damagePopupLayer);
        builder.RegisterInstance(settings);
        builder.RegisterComponentInHierarchy<PlayerHudView>();

        builder.Register<CombatEvents>(Lifetime.Singleton);
        builder.Register<SlashSpawner>(Lifetime.Singleton);
        builder.Register<EffectSpawner>(Lifetime.Singleton);

        builder.RegisterEntryPoint<DamagePopupPresenter>();
        builder.RegisterEntryPoint<GameplayEntryPoint>();
        builder.RegisterEntryPoint<PlayerDeathHandler>();
        builder.RegisterEntryPoint<PlayerHudPresenter>();

        builder.RegisterBuildCallback(resolver => resolver.InjectGameObject(player.gameObject));
    }
}
