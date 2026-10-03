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

        builder.Register<PlayerInputService>(Lifetime.Singleton)
            .WithParameter(inputActions)
            .WithParameter(eventSystem)
            .As<IPlayerInput>()
            .AsSelf();
        builder.RegisterEntryPoint<GameplayInputGate>();
        builder.RegisterEntryPoint<TransitionTimeFreeze>();

        builder.RegisterInstance<ISaveStore>(new FileSaveStore(Path.Combine(Application.persistentDataPath, FileSaveStore.DefaultFileName)));
        builder.Register<GameSave>(Lifetime.Singleton);

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
