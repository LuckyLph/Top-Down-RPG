using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// The composition root of the Gameplay scene, a child of the Main scope and parent of each world's scope:
/// registers the players, combat (projectiles included), abilities, statuses, HUD, menus, pointer feedback, the world
/// streaming, the trips between worlds, waystones and respawns, and the saving of world sessions for the session.
/// </summary>
public class GameplayLifetimeScope : LifetimeScope
{
    [SerializeField] private PlayerController playerPrefab;
    [SerializeField] private DamagePopupLayer damagePopupLayer;
    [SerializeField] private PointerFeedbackLayer pointerFeedbackLayer;
    [SerializeField] private GameplaySettings settings;
    [SerializeField] private CombatSettings combatSettings;
    [SerializeField] private WorldStreamingSettings worldStreamingSettings;

    /// <summary>
    /// Registers the Gameplay services and entry points and, once built, the player network prefabs. Logs an error
    /// and registers nothing without a parent scope, a player prefab, its control settings or the combat settings;
    /// skips pointer feedback, with an error, when its layer is missing.
    /// </summary>
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
        builder.Register<PlayerNavigation>(Lifetime.Singleton);
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
        builder.RegisterComponentInHierarchy<StatusBarView>();

        builder.Register<CombatEvents>(Lifetime.Singleton);
        builder.Register<DamageService>(Lifetime.Singleton);
        builder.RegisterEntryPoint<StatusEffectService>().AsSelf();
        builder.Register<HitService>(Lifetime.Singleton);
        builder.Register<StatusVisualPool>(Lifetime.Singleton).WithParameter(gameObject.scene);
        builder.Register<AbilityService>(Lifetime.Singleton);
        builder.Register<AttackService>(Lifetime.Singleton);
        builder.Register<ProjectileVisualPool>(Lifetime.Singleton).WithParameter(gameObject.scene);
        builder.RegisterEntryPoint<ProjectileService>().AsSelf();
        builder.Register<EffectSpawner>(Lifetime.Singleton);

        builder.RegisterEntryPoint<GameplayPlayers>().AsSelf();
        builder.RegisterEntryPoint<DamagePopupPresenter>();
        builder.RegisterEntryPoint<GameplayEntryPoint>();
        builder.RegisterEntryPoint<PlayerDeathHandler>();
        builder.RegisterEntryPoint<PlayerHudPresenter>();
        builder.RegisterEntryPoint<AbilityBarPresenter>();
        builder.RegisterEntryPoint<StatusBarPresenter>();
        builder.RegisterComponentInHierarchy<OptionsMenuView>();
        builder.RegisterEntryPoint<OptionsMenuController>().AsSelf();
        builder.RegisterComponentInHierarchy<GameMenuView>();
        builder.RegisterEntryPoint<GameMenuController>().AsSelf();

        if (pointerFeedbackLayer == null)
        {
            Debug.LogError($"{name} has no {nameof(PointerFeedbackLayer)}; there will be no hover or move feedback.", this);
        }
        else
        {
            builder.RegisterComponent(pointerFeedbackLayer);
            builder.RegisterEntryPoint<PointerFeedbackPresenter>();
        }

        builder.Register<Worlds>(Lifetime.Singleton);
        builder.Register<PlayerWorlds>(Lifetime.Singleton);
        builder.Register<ChunkNetworkPrefabs>(Lifetime.Singleton);
        builder.Register<TripArrivals>(Lifetime.Singleton);
        builder.Register<PlayerIds>(Lifetime.Singleton);
        builder.Register<WalkablePlaces>(Lifetime.Singleton);
        builder.Register<WaystoneNotices>(Lifetime.Singleton);
        builder.RegisterEntryPoint<WaystoneDiscovery>();
        builder.RegisterEntryPoint<WaystonePopupPresenter>();
        builder.Register<WorldExits>(Lifetime.Singleton);
        builder.Register<LocalTripAuthority>(Lifetime.Singleton);
        builder.Register<NetworkTripAuthority>(Lifetime.Singleton).As<ITripAuthority>().AsSelf();
        builder.RegisterEntryPoint<GameSaveRecorder>().AsSelf();
        builder.RegisterInstance(new NavigationPagePool());
        if (worldStreamingSettings == null)
        {
            Debug.LogError($"{name} has no {nameof(WorldStreamingSettings)}; no world session can start.", this);
        }
        else
        {
            builder.RegisterEntryPoint<WorldStreaming>()
                .WithParameter(worldStreamingSettings)
                .WithParameter<LifetimeScope>(this)
                .As<IWorldSession>()
                .AsSelf();
            builder.RegisterEntryPoint<LocalWorldPresentation>().WithParameter(worldStreamingSettings);
            builder.RegisterEntryPoint<LocalTripTraveller>().AsSelf();
        }

        builder.RegisterBuildCallback(resolver => resolver.Resolve<GameplayPlayers>().RegisterNetworkPrefabs());
    }
}
