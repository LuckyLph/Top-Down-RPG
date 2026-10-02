using System;
using UnityEngine;
using VContainer.Unity;

public sealed class NetworkSessionLifecycle : IStartable, IDisposable
{
    private readonly NetworkSession session;
    private readonly GameFlow gameFlow;
    private bool returnToMenuPending;

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
        if (gameFlow.IsInMenu)
        {
            session.Shutdown();
        }

        ReturnToMenuIfPending();
    }

    private void HandleConnectionLost()
    {
        if (gameFlow.IsInMenu && !gameFlow.IsTransitioning)
        {
            session.Shutdown();
            return;
        }

        Debug.LogWarning("Lost the connection to the session; returning to the main menu.");
        returnToMenuPending = true;
        ReturnToMenuIfPending();
    }

    private void ReturnToMenuIfPending()
    {
        if (!returnToMenuPending || gameFlow.IsTransitioning)
        {
            return;
        }

        returnToMenuPending = false;
        if (!gameFlow.IsInMenu)
        {
            _ = gameFlow.ShowMainMenuAsync();
        }
    }
}
