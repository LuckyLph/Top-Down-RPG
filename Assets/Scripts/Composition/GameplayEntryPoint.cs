using System;
using VContainer.Unity;

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
