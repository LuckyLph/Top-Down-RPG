using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using VContainer;
using VContainer.Unity;

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

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterInstance(gameScenes);
        builder.RegisterComponent(mainCamera);
        builder.RegisterComponent(screenFader);
        builder.RegisterComponent(cameraFollow);
        builder.RegisterInstance<IClock>(UnityClock.Shared);
        builder.RegisterInstance<IRandom>(new SystemRandom());

        builder.Register<SceneLoader>(Lifetime.Singleton);
        builder.Register<GameFlow>(Lifetime.Singleton)
            .WithParameter<LifetimeScope>(this);

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
            .AsSelf();
        builder.RegisterEntryPoint<GameplayInputGate>().AsSelf();
        builder.Register<TimeFreeze>(Lifetime.Singleton);
        builder.RegisterEntryPoint<MenuStack>().AsSelf();
        builder.RegisterEntryPoint<TransitionTimeFreeze>();

        builder.RegisterInstance<ISaveStore>(new FileSaveStore(Path.Combine(Application.persistentDataPath, FileSaveStore.DefaultFileName)));
        builder.Register<GameSave>(Lifetime.Singleton);
        builder.RegisterInstance<ISaveStore>(new FileSaveStore(Path.Combine(Application.persistentDataPath, UserSettings.FileName)))
            .Keyed(UserSettings.FileName);
        builder.Register<UserSettings>(Lifetime.Singleton);

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
                .As<ISessionStatus>()
                .AsSelf()
                .WithParameter(networkManagerPrefab)
                .WithParameter(networkSettings);
            builder.RegisterEntryPoint<NetworkSessionLifecycle>();
            builder.RegisterEntryPoint<NetworkAreaSync>();
            builder.RegisterEntryPoint<GameSaveRecorder>();
        }

        builder.RegisterEntryPoint<BootFlow>();
    }
}
