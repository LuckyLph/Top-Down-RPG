using System;
using VContainer.Unity;

// Points the Main scene's camera at the session's player for the lifetime of the Gameplay scene.
public sealed class GameplayEntryPoint : IStartable, IDisposable
{
    private readonly CameraFollow2D cameraFollow;
    private readonly PlayerController player;

    public GameplayEntryPoint(CameraFollow2D cameraFollow, PlayerController player)
    {
        this.cameraFollow = cameraFollow;
        this.player = player;
    }

    public void Start()
    {
        cameraFollow.SetTarget(player.transform);
        cameraFollow.SnapToTarget();
    }

    public void Dispose()
    {
        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(null);
        }
    }
}
