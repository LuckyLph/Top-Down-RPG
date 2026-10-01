using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;
using VContainer.Unity;

public class MainLifetimeScope : LifetimeScope
{
    [SerializeField] private GameScenes gameScenes;
    [SerializeField] private InputActionAsset inputActions;
    [SerializeField] private ScreenFader screenFader;
    [SerializeField] private CameraFollow2D cameraFollow;
    [SerializeField] private Camera mainCamera;

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

        builder.Register<PlayerInputService>(Lifetime.Singleton)
            .WithParameter(inputActions)
            .As<IPlayerInput>()
            .AsSelf();
        builder.RegisterEntryPoint<GameplayInputGate>();

        builder.RegisterEntryPoint<BootFlow>();
    }
}
