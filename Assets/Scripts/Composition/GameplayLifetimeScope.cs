using UnityEngine;
using VContainer;
using VContainer.Unity;

public class GameplayLifetimeScope : LifetimeScope
{
    [SerializeField] private PlayerController playerPrefab;
    [SerializeField] private DamagePopupLayer damagePopupLayer;
    [SerializeField] private PointerFeedbackLayer pointerFeedbackLayer;
    [SerializeField] private GameplaySettings settings;
    [SerializeField] private CombatSettings combatSettings;

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

        if (playerPrefab.ControlSettings == null)
        {
            Debug.LogError($"{name}: the player prefab has no {nameof(PlayerControlSettings)}; no player can be spawned.", this);
            return;
        }

        if (combatSettings == null)
        {
            Debug.LogError($"{name} has no {nameof(CombatSettings)} assigned; hits cannot be resolved.", this);
            return;
        }

        builder.RegisterInstance(playerPrefab.ControlSettings);
        builder.Register<PlayerRegistry>(Lifetime.Singleton).As<IPlayerRegistry>().AsSelf();
        builder.Register<LocalPlayerTracker>(Lifetime.Singleton);
        builder.Register<PointerTargetPicker>(Lifetime.Singleton);
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
        builder.RegisterInstance(combatSettings);
        builder.RegisterComponentInHierarchy<PlayerHudView>();
        builder.RegisterComponentInHierarchy<AbilityBarView>();

        builder.Register<CombatEvents>(Lifetime.Singleton);
        builder.Register<DamageService>(Lifetime.Singleton);
        builder.RegisterEntryPoint<StatusEffectService>().AsSelf();
        builder.Register<HitService>(Lifetime.Singleton);
        builder.Register<AbilityService>(Lifetime.Singleton);
        builder.Register<SlashSpawner>(Lifetime.Singleton);
        builder.Register<EffectSpawner>(Lifetime.Singleton);

        builder.RegisterEntryPoint<GameplayPlayers>().AsSelf();
        builder.RegisterEntryPoint<DamagePopupPresenter>();
        builder.RegisterEntryPoint<GameplayEntryPoint>();
        builder.RegisterEntryPoint<PlayerDeathHandler>();
        builder.RegisterEntryPoint<PlayerHudPresenter>();
        builder.RegisterEntryPoint<AbilityBarPresenter>();

        if (pointerFeedbackLayer == null)
        {
            Debug.LogError($"{name} has no {nameof(PointerFeedbackLayer)}; there will be no hover or move feedback.", this);
        }
        else
        {
            builder.RegisterComponent(pointerFeedbackLayer);
            builder.RegisterEntryPoint<PointerFeedbackPresenter>();
        }

        builder.RegisterBuildCallback(resolver => resolver.Resolve<GameplayPlayers>().RegisterNetworkPrefabs());
    }
}
