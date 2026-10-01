using System;
using VContainer.Unity;

/// <summary>Saves the area once a transition into it finishes, on the machine that decides game state only.</summary>
public sealed class GameSaveRecorder : IStartable, IDisposable
{
    private readonly GameFlow gameFlow;
    private readonly GameSave gameSave;
    private readonly IGameAuthority authority;
    private AreaTransition? pending;

    public GameSaveRecorder(GameFlow gameFlow, GameSave gameSave, IGameAuthority authority)
    {
        this.gameFlow = gameFlow;
        this.gameSave = gameSave;
        this.authority = authority;
    }

    public void Start()
    {
        gameFlow.AreaLoading += HandleAreaLoading;
        gameFlow.TransitionFinished += HandleTransitionFinished;
    }

    public void Dispose()
    {
        gameFlow.AreaLoading -= HandleAreaLoading;
        gameFlow.TransitionFinished -= HandleTransitionFinished;
    }

    private void HandleAreaLoading(AreaTransition transition)
    {
        pending = authority.IsAuthoritative ? transition : null;
    }

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
