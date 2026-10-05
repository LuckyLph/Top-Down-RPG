using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Ties the network session to the game flow: shuts the session down whenever a transition ends in the menu, and
/// returns to the menu when the connection is lost outside the idle menu, waiting for a running transition to finish
/// first.
/// </summary>
public sealed class NetworkSessionLifecycle : IStartable, IDisposable
{
    private readonly NetworkSession session;
    private readonly GameFlow gameFlow;
    private bool returnToMenuPending;

    /// <summary>
    /// Creates the lifecycle for <paramref name="session"/>, driven by <paramref name="gameFlow"/>.
    /// </summary>
    public NetworkSessionLifecycle(NetworkSession session, GameFlow gameFlow)
    {
        this.session = session;
        this.gameFlow = gameFlow;
    }

    /// <summary>
    /// Subscribes to transition ends and connection loss.
    /// </summary>
    public void Start()
    {
        gameFlow.TransitionFinished += HandleTransitionFinished;
        session.ConnectionLost += HandleConnectionLost;
    }

    /// <summary>
    /// Unsubscribes from the game flow and session events.
    /// </summary>
    public void Dispose()
    {
        gameFlow.TransitionFinished -= HandleTransitionFinished;
        session.ConnectionLost -= HandleConnectionLost;
    }

    /// <summary>
    /// Shuts the session down when the transition ended in the menu, then acts on a connection loss that happened
    /// during the transition.
    /// </summary>
    private void HandleTransitionFinished()
    {
        if (gameFlow.IsInMenu)
        {
            session.Shutdown();
        }

        ReturnToMenuIfPending();
    }

    /// <summary>
    /// In the idle menu (a join still waiting for the host's announcement) just ends the session; anywhere else warns
    /// and returns to the menu, now or once the running transition finishes.
    /// </summary>
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

    /// <summary>
    /// Shows the main menu for a pending connection loss once no transition runs; the menu's transition then shuts
    /// the session down. Clears the pending flag without a transition when already in the menu.
    /// </summary>
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
