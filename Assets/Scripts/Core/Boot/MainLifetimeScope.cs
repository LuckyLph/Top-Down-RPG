using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;
using VContainer.Unity;

// Root composition scope, living in the persistent Main scene. GameFlow parents every other
// scene's scope to this one, directly (MainMenu, Gameplay) or through Gameplay (areas).
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

        builder.Register<SceneLoader>(Lifetime.Singleton);
        // GameFlow parents scene scopes to this one. Passed as a parameter rather than registered so
        // child containers never see it (VContainer already resolves LifetimeScope to each scope itself).
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
