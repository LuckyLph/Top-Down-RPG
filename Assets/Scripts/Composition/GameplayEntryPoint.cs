using System;
using VContainer.Unity;

// Connects the session's player to scene-independent systems: the Main camera follows it and
// its input is locked while GameFlow is transitioning between scenes.
public sealed class GameplayEntryPoint : IStartable, IDisposable
{
    private readonly GameFlow gameFlow;
    private readonly CameraFollow2D cameraFollow;
    private readonly PlayerController player;

    public GameplayEntryPoint(GameFlow gameFlow, CameraFollow2D cameraFollow, PlayerController player)
    {
        this.gameFlow = gameFlow;
        this.cameraFollow = cameraFollow;
        this.player = player;
    }

    public void Start()
    {
        cameraFollow.SetTarget(player.transform);
        cameraFollow.SnapToTarget();

        gameFlow.TransitionStarted += HandleTransitionStarted;
        gameFlow.TransitionFinished += HandleTransitionFinished;
        player.SetInputEnabled(!gameFlow.IsTransitioning);
    }

    public void Dispose()
    {
        gameFlow.TransitionStarted -= HandleTransitionStarted;
        gameFlow.TransitionFinished -= HandleTransitionFinished;

        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(null);
        }
    }

    private void HandleTransitionStarted()
    {
        if (player != null)
        {
            player.SetInputEnabled(false);
        }
    }

    private void HandleTransitionFinished()
    {
        if (player != null)
        {
            player.SetInputEnabled(true);
        }
    }
}
