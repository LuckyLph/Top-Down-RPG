using System;
using VContainer.Unity;

/// <summary>Saves the area once a transition into it finishes, on the machine that decides game state only.</summary>
public sealed class GameSaveRecorder : IStartable, IDisposable
{
    private readonly GameFlow gameFlow;
    private readonly GameSave gameSave;
    private readonly IGameAuthority authority;
    private AreaTransition? pending;

    /// <summary>
    /// Creates a recorder that watches <paramref name="gameFlow"/> and writes through <paramref name="gameSave"/>
    /// when <paramref name="authority"/> says this machine decides game state.
    /// </summary>
    public GameSaveRecorder(GameFlow gameFlow, GameSave gameSave, IGameAuthority authority)
    {
        this.gameFlow = gameFlow;
        this.gameSave = gameSave;
        this.authority = authority;
    }

    /// <summary>
    /// Subscribes to area loads and transition ends.
    /// </summary>
    public void Start()
    {
        gameFlow.AreaLoading += HandleAreaLoading;
        gameFlow.TransitionFinished += HandleTransitionFinished;
    }

    /// <summary>
    /// Unsubscribes from the game flow events.
    /// </summary>
    public void Dispose()
    {
        gameFlow.AreaLoading -= HandleAreaLoading;
        gameFlow.TransitionFinished -= HandleTransitionFinished;
    }

    /// <summary>
    /// Remembers <paramref name="transition"/> to save when it finishes, or forgets any pending one when this machine
    /// is not authoritative.
    /// </summary>
    private void HandleAreaLoading(AreaTransition transition)
    {
        pending = authority.IsAuthoritative ? transition : null;
    }

    /// <summary>
    /// Saves the pending transition's area and spawn id once the game is running in that area; drops it when the
    /// transition ended anywhere else (cancelled, failed, or back in the menu).
    /// </summary>
    private void HandleTransitionFinished()
    {
        if (!pending.HasValue)
        {
            return;
        }

        AreaTransition transition = pending.Value;
        pending = null;
        if (gameFlow.IsInGame && gameFlow.CurrentArea == transition.Area)
        {
            gameSave.SaveArea(transition.Area, transition.SpawnId);
        }
    }
}
