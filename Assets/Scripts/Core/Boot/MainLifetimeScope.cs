using UnityEngine;
using VContainer;
using VContainer.Unity;

// Root composition scope, living in the persistent Main scene. Every other scene's scope
// parents to this one, directly (MainMenu, Gameplay) or through Gameplay (areas).
public class MainLifetimeScope : LifetimeScope
{
    [SerializeField] private GameScenes gameScenes;
    [SerializeField] private ScreenFader screenFader;
    [SerializeField] private CameraFollow2D cameraFollow;
    [SerializeField] private Camera mainCamera;

    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterInstance(gameScenes);
        builder.RegisterComponent(mainCamera);
        builder.RegisterComponent(screenFader);
        builder.RegisterComponent(cameraFollow);
        builder.Register<SceneLoader>(Lifetime.Singleton);
        builder.Register<GameFlow>(Lifetime.Singleton);
        builder.RegisterEntryPoint<BootFlow>();
    }
}
