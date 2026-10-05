using System;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that points the camera at the local player, now and whenever the local player
/// changes, and releases the camera target when the scope is disposed.
/// </summary>
public sealed class GameplayEntryPoint : IStartable, IDisposable
{
    private readonly CameraFollow2D cameraFollow;
    private readonly LocalPlayerTracker localPlayer;

    /// <summary>
    /// Captures the camera follow and the local player tracker.
    /// </summary>
    public GameplayEntryPoint(CameraFollow2D cameraFollow, LocalPlayerTracker localPlayer)
    {
        this.cameraFollow = cameraFollow;
        this.localPlayer = localPlayer;
    }

    /// <summary>
    /// Subscribes to local player changes and follows the current local player, if any.
    /// </summary>
    public void Start()
    {
        localPlayer.Changed += Follow;
        Follow(localPlayer.Current);
    }

    /// <summary>
    /// Unsubscribes and clears the camera's target if the camera still exists.
    /// </summary>
    public void Dispose()
    {
        localPlayer.Changed -= Follow;

        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(null);
        }
    }

    /// <summary>
    /// Makes the camera follow <paramref name="player"/> and snaps it there; ignores a null player, keeping the
    /// previous target.
    /// </summary>
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
