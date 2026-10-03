using System;
using System.Collections.Generic;
using VContainer.Unity;

/// <summary>
/// Enables the <c>Player</c> map only in game, outside transitions and while nothing blocks it (the in-game menu
/// does), and the <c>Menus</c> map in game outside transitions.
/// </summary>
public sealed class GameplayInputGate : IStartable, IDisposable
{
    private readonly GameFlow gameFlow;
    private readonly PlayerInputService playerInput;
    private readonly HashSet<object> blockers = new();

    public GameplayInputGate(GameFlow gameFlow, PlayerInputService playerInput)
    {
        this.gameFlow = gameFlow;
        this.playerInput = playerInput;
    }

    public bool IsBlocked => blockers.Count > 0;

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
        if (owner != null && blockers.Add(owner))
        {
            Refresh();
        }
    }

    public void Unblock(object owner)
    {
        if (owner != null && blockers.Remove(owner))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        bool inGame = !gameFlow.IsTransitioning && gameFlow.IsInGame;
        playerInput.SetGameplayEnabled(inGame && blockers.Count == 0);
        playerInput.SetMenusEnabled(inGame);
    }
}
