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

    /// <summary>
    /// Creates a gate that follows <paramref name="gameFlow"/>'s transitions and switches the maps on
    /// <paramref name="playerInput"/>, with no blockers.
    /// </summary>
    public GameplayInputGate(GameFlow gameFlow, PlayerInputService playerInput)
    {
        this.gameFlow = gameFlow;
        this.playerInput = playerInput;
    }

    public bool IsBlocked => blockers.IsHeld;

    /// <summary>
    /// Subscribes to transition start and end and applies the current state right away.
    /// </summary>
    public void Start()
    {
        gameFlow.TransitionStarted += Refresh;
        gameFlow.TransitionFinished += Refresh;
        Refresh();
    }

    /// <summary>
    /// Unsubscribes from the transition events.
    /// </summary>
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

    /// <summary>
    /// Drops <paramref name="owner"/>'s block; gameplay input comes back once no owner blocks it and the game is
    /// running outside a transition.
    /// </summary>
    public void Unblock(object owner)
    {
        if (blockers.Release(owner))
        {
            Refresh();
        }
    }

    /// <summary>
    /// Enables or disables the <c>Player</c> and <c>Menus</c> maps for the current game flow and blocker state.
    /// </summary>
    private void Refresh()
    {
        bool inGame = !gameFlow.IsTransitioning && gameFlow.IsInGame;
        playerInput.SetGameplayEnabled(inGame && !blockers.IsHeld);
        playerInput.SetMenusEnabled(inGame);
    }
}
