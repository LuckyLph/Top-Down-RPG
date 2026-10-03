using System;
using VContainer.Unity;

/// <summary>
/// Enables the <c>Player</c> map only in game, outside transitions and while nothing blocks it (the in-game menu
/// does), and the <c>Menus</c> map in game outside transitions.
/// </summary>
public sealed class GameplayInputGate : IStartable, IDisposable
{
    private readonly GameFlow gameFlow;
    private readonly PlayerInputService playerInput;
    private readonly HoldSet blockers = new();

    public GameplayInputGate(GameFlow gameFlow, PlayerInputService playerInput)
    {
        this.gameFlow = gameFlow;
        this.playerInput = playerInput;
    }

    public bool IsBlocked => blockers.IsHeld;

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

    /// <summary>
    /// Keeps gameplay input off until <paramref name="owner"/> calls <see cref="Unblock"/>.
    /// </summary>
    public void Block(object owner)
    {
        if (blockers.Hold(owner))
        {
            Refresh();
        }
    }

    public void Unblock(object owner)
    {
        if (blockers.Release(owner))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        bool inGame = !gameFlow.IsTransitioning && gameFlow.IsInGame;
        playerInput.SetGameplayEnabled(inGame && !blockers.IsHeld);
        playerInput.SetMenusEnabled(inGame);
    }
}
