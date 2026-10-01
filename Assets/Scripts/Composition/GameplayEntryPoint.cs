using System;
using VContainer.Unity;

public sealed class GameplayEntryPoint : IStartable, IDisposable
{
    private readonly CameraFollow2D cameraFollow;
    private readonly LocalPlayerTracker localPlayer;

    public GameplayEntryPoint(CameraFollow2D cameraFollow, LocalPlayerTracker localPlayer)
    {
        this.cameraFollow = cameraFollow;
        this.localPlayer = localPlayer;
    }

    public void Start()
    {
        localPlayer.Changed += Follow;
        Follow(localPlayer.Current);
    }

    public void Dispose()
    {
        localPlayer.Changed -= Follow;

        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(null);
        }
    }

    private void Follow(LocalPlayer player)
    {
        if (player == null)
        {
            return;
        }

        cameraFollow.SetTarget(player.Transform);
        cameraFollow.SnapToTarget();
    }
}
