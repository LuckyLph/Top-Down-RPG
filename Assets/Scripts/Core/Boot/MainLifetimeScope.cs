using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Root DI scope of the app, living in the Main scene for the whole run: registers the scene flow, input, clock,
/// menus, the save and the session's saved state, user settings and network session that the menu, gameplay, area and
/// world scopes inherit.
/// </summary>
public class MainLifetimeScope : LifetimeScope
{
    [SerializeField] private GameScenes gameScenes;
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private RebindableActionList rebindableActions;
    [SerializeField] private EventSystem eventSystem;
    [SerializeField] private ScreenFader screenFader;
    [SerializeField] private CameraFollow2D cameraFollow;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private NetworkManager networkManagerPrefab;
    [SerializeField] private NetworkSettings networkSettings;
    [SerializeField, Tooltip("How the UI zoom is chosen (Docs/DesignSystem.md, Foundations: Scale).")]
    private UiScaleSettings uiScaleSettings;

    /// <summary>
    /// Registers the app-wide services and entry points, logging an error and skipping the network services when
    /// the NetworkManager prefab or <see cref="NetworkSettings"/> is missing, and falling back to the default UI scale
    /// settings without <see cref="UiScaleSettings"/>.
    /// </summary>
    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterInstance(gameScenes);
        builder.RegisterComponent(mainCamera);
        builder.RegisterComponent(screenFader);
        builder.RegisterComponent(cameraFollow).As<ICameraKick>().AsSelf();
        builder.Register<IClock, UnityClock>(Lifetime.Singleton);
        builder.RegisterInstance<IRandom>(new SystemRandom());

        builder.Register<SceneLoader>(Lifetime.Singleton);
        builder.Register<GameFlow>(Lifetime.Singleton)
            .WithParameter<LifetimeScope>(this)
            .As<IGameFlow>()
            .AsSelf();
        builder.RegisterEntryPoint<LoadingMessage>().AsSelf();
        builder.Register<MenuNotice>(Lifetime.Singleton);

        if (eventSystem == null)
        {
            Debug.LogError($"{name} has no EventSystem assigned; clicks over UI will reach gameplay.", this);
        }

        builder.Register<UISelection>(Lifetime.Singleton).WithParameter(eventSystem);
        builder.Register<RuntimeInputActions>(Lifetime.Singleton)
            .WithParameter(inputActions)
            .WithParameter(eventSystem);
        builder.Register<PlayerInputService>(Lifetime.Singleton)
            .WithParameter<InputActionAsset>(resolver => resolver.Resolve<RuntimeInputActions>().Actions)
            .WithParameter(eventSystem)
            .As<IPlayerInput>()
            .As<IMenuInput>()
            .As<IChatInput>()
            .AsSelf();
        builder.RegisterEntryPoint<GameplayInputGate>().AsSelf();
        builder.Register<TimeFreeze>(Lifetime.Singleton);
        builder.RegisterEntryPoint<MenuStack>().AsSelf();
        builder.RegisterEntryPoint<TransitionTimeFreeze>();

        builder.RegisterInstance<ISaveFolder>(new FileSaveFolder(DataPath(FileSaveFolder.DefaultFolderName), DataPath(FileSaveStore.DefaultFileName)));
        builder.Register<IWallClock, SystemWallClock>(Lifetime.Singleton);
        builder.Register<GameSave>(Lifetime.Singleton);
        builder.Register<SessionSave>(Lifetime.Singleton);
        builder.Register<PlayerStarts>(Lifetime.Singleton);
        builder.RegisterInstance<ISaveStore>(new FileSaveStore(DataPath(UserSettings.FileName)))
            .Keyed(UserSettings.FileName);
        builder.Register<UserSettings>(Lifetime.Singleton);

        if (uiScaleSettings == null)
        {
            Debug.LogError($"{name} has no UiScaleSettings assigned; the UI uses the default scale settings.", this);
        }

        builder.RegisterInstance(uiScaleSettings != null ? uiScaleSettings : UiScaleSettings.Create());
        builder.RegisterEntryPoint<UiScale>().AsSelf();

        builder.RegisterEntryPoint<InputBindings>()
            .WithParameter<InputActionAsset>(resolver => resolver.Resolve<RuntimeInputActions>().Actions)
            .WithParameter(rebindableActions)
            .AsSelf();

        if (networkManagerPrefab == null || networkSettings == null)
        {
            Debug.LogError($"{name} needs a NetworkManager prefab and NetworkSettings; online play is unavailable.", this);
        }
        else
        {
            builder.Register<NetworkSession>(Lifetime.Singleton)
                .As<IGameAuthority>()
                .As<INetworkObjectSpawner>()
                .As<IClientReadiness>()
                .As<IChunkHandshake>()
                .As<IChunkHolds>()
                .As<ISessionStatus>()
                .As<IPlayerIdSource>()
                .As<INetworkClock>()
                .As<IHostLink>()
                .As<ISessionConnection>()
                .AsSelf()
                .WithParameter(networkManagerPrefab)
                .WithParameter(networkSettings);
            builder.RegisterEntryPoint<NetworkSessionLifecycle>();
            builder.Register<INetworkInterfaceSource, SystemNetworkInterfaces>(Lifetime.Singleton);
            builder.Register<HostAddresses>(Lifetime.Singleton).WithParameter(networkSettings.Port);
        }

        builder.RegisterEntryPoint<BootFlow>();
    }

    /// <summary>
    /// The path of this instance's file <paramref name="fileName"/> in the persistent data folder (a Multiplayer Play
    /// Mode virtual player's own file in the editor, see <see cref="InstanceFiles"/>).
    /// </summary>
    private static string DataPath(string fileName)
    {
        return Path.Combine(Application.persistentDataPath, InstanceFiles.NameFor(fileName));
    }
}
