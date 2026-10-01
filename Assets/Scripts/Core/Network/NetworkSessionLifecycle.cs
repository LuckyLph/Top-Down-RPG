using System;
using UnityEngine;
using VContainer.Unity;

public sealed class NetworkSessionLifecycle : IStartable, IDisposable
{
    private readonly NetworkSession session;
    private readonly GameFlow gameFlow;

    public NetworkSessionLifecycle(NetworkSession session, GameFlow gameFlow)
    {
        this.session = session;
        this.gameFlow = gameFlow;
    }

    public void Start()
    {
        gameFlow.TransitionFinished += HandleTransitionFinished;
        session.ConnectionLost += HandleConnectionLost;
    }

    public void Dispose()
    {
        gameFlow.TransitionFinished -= HandleTransitionFinished;
        session.ConnectionLost -= HandleConnectionLost;
    }

    private void HandleTransitionFinished()
    {
        if (gameFlow.IsInMenu && session.IsActive)
        {
            session.Shutdown();
        }
    }

    private void HandleConnectionLost()
    {
        if (!gameFlow.IsInGame)
        {
            return;
        }

        Debug.LogWarning("Lost the connection to the session; returning to the main menu.");
        _ = gameFlow.ShowMainMenuAsync();
    }
}
