using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Ties the network session to the game flow: shuts the session down whenever a transition ends in the menu, and
/// returns to the menu when the connection is lost outside the idle menu, waiting for a running transition to finish
/// first. A joined client that loses its host leaves the menu a <see cref="MenuNotice"/> (Docs/UiRevamp.md, Phase 9):
/// lost while joining when its player did not stand in a world yet (the joined game's own transition still running),
/// else the host gone, each naming the host's address.
/// </summary>
public sealed class NetworkSessionLifecycle : IStartable, IDisposable
{
    private readonly ISessionConnection session;
    private readonly IGameFlow gameFlow;
    private readonly MenuNotice notice;
    private bool returnToMenuPending;

    /// <summary>
    /// Creates the lifecycle for <paramref name="session"/>, driven by <paramref name="gameFlow"/>, leaving
    /// <paramref name="notice"/> for the menu.
    /// </summary>
    public NetworkSessionLifecycle(ISessionConnection session, IGameFlow gameFlow, MenuNotice notice)
    {
        this.session = session;
        this.gameFlow = gameFlow;
        this.notice = notice;
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
    /// In the idle menu (a join that has not started its game) just ends the session; anywhere else leaves a joined
    /// client's menu its notice, warns and returns to the menu, now or once the running transition finishes.
    /// </summary>
    private void HandleConnectionLost()
    {
        if (gameFlow.IsInMenu && !gameFlow.IsTransitioning)
        {
            session.Shutdown();
            return;
        }

        if (session.IsFollowingHost)
        {
            bool joining = !gameFlow.IsInGame || (gameFlow.IsTransitioning && !gameFlow.IsTravelling);
            notice.Leave(joining ? MenuNoticeKind.LostWhileJoining : MenuNoticeKind.HostGone, session.JoinedAddress);
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
