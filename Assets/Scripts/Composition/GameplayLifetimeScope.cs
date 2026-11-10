using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// The composition root of the Gameplay scene, a child of the Main scope and parent of each world's scope:
/// registers the players, combat (projectiles included), abilities, statuses, hit feedback and pooled effects, HUD,
/// menus, pointer feedback, the props fading over the local player, the world streaming, the trips between worlds,
/// waystones and respawns, the day and night with the region skies of the loaded chunks, and the saving of world
/// sessions for the session.
/// </summary>
public class GameplayLifetimeScope : LifetimeScope
{
    [SerializeField] private PlayerController playerPrefab;
    [SerializeField] private DamagePopupLayer damagePopupLayer;
    [SerializeField] private PointerFeedbackLayer pointerFeedbackLayer;
    [SerializeField] private GameplaySettings settings;
    [SerializeField] private CombatSettings combatSettings;
    [SerializeField] private HitFeedbackSettings hitFeedbackSettings;
    [SerializeField] private PropFadeSettings propFadeSettings;
    [SerializeField] private WorldStreamingSettings worldStreamingSettings;
    [SerializeField] private DayNightSettings dayNightSettings;

    /// <summary>
    /// Registers the Gameplay services and entry points and, once built, the player network prefabs. Logs an error
    /// and registers nothing without a parent scope, a player prefab, its control settings, the combat settings or the
    /// gameplay settings;
    /// skips pointer feedback, or hit feedback, with an error, when its layer, or its settings, are missing; without the
    /// prop fade settings it logs an error and props never fade; without the day and night settings it logs an error and
    /// the worlds keep their authored light.
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

        if (settings == null)
        {
            Debug.LogError($"{name} has no {nameof(GameplaySettings)} assigned; respawns, trips and outfits cannot be set up.", this);
            return;
        }

        builder.RegisterInstance(playerPrefab.ControlSettings);
        builder.Register<PlayerRegistry>(Lifetime.Singleton).As<IPlayerRegistry>().AsSelf();
        builder.Register<LocalPlayerTracker>(Lifetime.Singleton);
        builder.Register<PointerTargetPicker>(Lifetime.Singleton);
        builder.Register<LocalPlayerCommandSource>(Lifetime.Singleton);
        builder.Register<PlayerNavigation>(Lifetime.Singleton);
        builder.Register<PlayerBinder>(Lifetime.Singleton);
        builder.Register<CharacterOutfits>(Lifetime.Singleton)
            .WithParameter("spareOutfits", settings.SpareOutfits)
            .WithParameter("spritesPerFrame", settings.OutfitSpritesPerFrame);
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
        builder.Register<EffectPool>(Lifetime.Singleton).WithParameter(gameObject.scene);

        builder.RegisterEntryPoint<GameplayPlayers>().AsSelf();
        builder.RegisterEntryPoint<DamagePopupPresenter>();
        if (hitFeedbackSettings == null)
        {
            Debug.LogError($"{name} has no {nameof(HitFeedbackSettings)}; hits will show no flash, hit-stop, nudge, impact or camera kick.", this);
        }
        else
        {
            builder.RegisterInstance(hitFeedbackSettings);
            builder.RegisterEntryPoint<HitFeedbackPresenter>().AsSelf();
        }

        if (propFadeSettings == null)
        {
            Debug.LogError($"{name} has no {nameof(PropFadeSettings)}; tall props will not fade while they hide the player.", this);
            builder.RegisterEntryPoint<PropFading>().WithParameter(typeof(PropFadeSettings), null).AsSelf();
        }
        else
        {
            builder.RegisterInstance(propFadeSettings);
            builder.RegisterEntryPoint<PropFading>().AsSelf();
        }
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
        builder.RegisterEntryPoint<TripProtection>().As<IUnitProtection>().AsSelf();
        builder.Register<PlayerIds>(Lifetime.Singleton);
        builder.Register<WalkablePlaces>(Lifetime.Singleton);
        builder.Register<WaystoneNotices>(Lifetime.Singleton);
        builder.RegisterEntryPoint<WaystoneDiscovery>();
        builder.RegisterEntryPoint<WaystonePopupPresenter>();
        builder.Register<KnownWaystones>(Lifetime.Singleton);
        builder.RegisterEntryPoint<WaystoneMarkers>().AsSelf();
        builder.Register<WorldExits>(Lifetime.Singleton);
        builder.Register<LocalTripAuthority>(Lifetime.Singleton);
        builder.Register<NetworkTripAuthority>(Lifetime.Singleton).As<ITripAuthority>().AsSelf();
        builder.RegisterEntryPoint<GameSaveRecorder>().AsSelf();
        builder.RegisterEntryPoint<HostSessionSync>().AsSelf();
        builder.RegisterInstance(new NavigationPagePool());
        builder.Register<SharedTerrainArrays>(Lifetime.Singleton);
        builder.Register<RegionSkies>(Lifetime.Singleton);
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

        if (dayNightSettings == null)
        {
            Debug.LogError($"{name} has no {nameof(DayNightSettings)}; the worlds keep their authored light.", this);
            builder.RegisterEntryPoint<PropLighting>().WithParameter(typeof(DayClock), (object)null).AsSelf();
            builder.RegisterEntryPoint<JoinedSaveRecorder>().WithParameter(typeof(DayClock), (object)null).AsSelf();
        }
        else
        {
            builder.RegisterInstance(dayNightSettings);
            builder.Register<DayClock>(Lifetime.Singleton);
            builder.RegisterEntryPoint<DayClockSync>().AsSelf();
            builder.RegisterEntryPoint<DayNightPresenter>().AsSelf();
            builder.RegisterEntryPoint<PropLighting>().AsSelf();
            builder.RegisterEntryPoint<JoinedSaveRecorder>().AsSelf();
        }

        builder.RegisterEntryPoint<LightWarmUp>().AsSelf();

        builder.RegisterBuildCallback(resolver => resolver.Resolve<GameplayPlayers>().RegisterNetworkPrefabs());
    }
}
