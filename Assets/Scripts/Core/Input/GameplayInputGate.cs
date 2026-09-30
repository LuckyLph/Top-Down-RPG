using System;
using VContainer.Unity;

// Gameplay input is only live while a session is running and no scene transition is in progress.
public sealed class GameplayInputGate : IStartable, IDisposable
{
    private readonly GameFlow gameFlow;
    private readonly PlayerInputService playerInput;

    public GameplayInputGate(GameFlow gameFlow, PlayerInputService playerInput)
    {
        this.gameFlow = gameFlow;
        this.playerInput = playerInput;
    }

    public void Start()
    {
        gameFlow.TransitionStarted += Refresh;
        gameFlow.TransitionFinished += Refresh;
        Refresh();
    }

    public void Dispose()
    {
        gameFlow.TransitionStarted -= Refresh;
        gameFlow.TransitionFinished -= Refresh;
    }

    private void Refresh()
    {
        playerInput.SetGameplayEnabled(!gameFlow.IsTransitioning && gameFlow.IsInGame);
    }
}
