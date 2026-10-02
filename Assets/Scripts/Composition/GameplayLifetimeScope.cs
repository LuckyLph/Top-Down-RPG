using UnityEngine;
using VContainer;
using VContainer.Unity;

public class GameplayLifetimeScope : LifetimeScope
{
    [SerializeField] private PlayerController playerPrefab;
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

        if (playerPrefab == null)
        {
            Debug.LogError($"{name} has no player prefab assigned; no player can be spawned.", this);
            return;
        }

        builder.Register<PlayerRegistry>(Lifetime.Singleton).As<IPlayerRegistry>().AsSelf();
        builder.Register<LocalPlayerTracker>(Lifetime.Singleton);
        builder.Register<LocalPlayerCommandSource>(Lifetime.Singleton);
        builder.Register<ActiveSpawnPoint>(Lifetime.Singleton);
        builder.Register<ActiveNavigationGrid>(Lifetime.Singleton);
        builder.Register<PlayerBinder>(Lifetime.Singleton);
        builder.Register<PlayerRespawner>(Lifetime.Singleton);
        builder.Register<PlayerSpawner>(Lifetime.Singleton)
            .WithParameter(playerPrefab)
            .WithParameter(gameObject.scene);
        builder.RegisterComponent(damagePopupLayer);
        builder.RegisterInstance(settings);
        builder.RegisterComponentInHierarchy<PlayerHudView>();

        builder.Register<CombatEvents>(Lifetime.Singleton);
        builder.Register<DamageService>(Lifetime.Singleton);
        builder.Register<SlashSpawner>(Lifetime.Singleton);
        builder.Register<EffectSpawner>(Lifetime.Singleton);

        builder.RegisterEntryPoint<GameplayPlayers>().AsSelf();
        builder.RegisterEntryPoint<DamagePopupPresenter>();
        builder.RegisterEntryPoint<GameplayEntryPoint>();
        builder.RegisterEntryPoint<PlayerDeathHandler>();
        builder.RegisterEntryPoint<PlayerHudPresenter>();

        builder.RegisterBuildCallback(resolver => resolver.Resolve<GameplayPlayers>().RegisterNetworkPrefabs());
    }
}
