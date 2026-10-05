using System;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Keeps every machine in the host's area. On the host it despawns the networked objects of the scenes about to unload
/// and announces each area it loads; on a client it follows the host's announcements through <see cref="GameFlow"/>,
/// keeping only the latest one while a transition runs.
/// </summary>
public sealed class NetworkAreaSync : IStartable, IDisposable
{
    private readonly NetworkSession session;
    private readonly GameFlow gameFlow;
    private readonly GameScenes gameScenes;
    private AreaAnnouncement? pending;

    /// <summary>
    /// Creates the sync for <paramref name="session"/>, driving <paramref name="gameFlow"/> and resolving announced
    /// scene paths against <paramref name="gameScenes"/>.
    /// </summary>
    public NetworkAreaSync(NetworkSession session, GameFlow gameFlow, GameScenes gameScenes)
    {
        this.session = session;
        this.gameFlow = gameFlow;
        this.gameScenes = gameScenes;
    }

    /// <summary>
    /// Subscribes to area loads, transition ends and the session's area announcements.
    /// </summary>
    public void Start()
    {
        gameFlow.AreaLoading += HandleAreaLoading;
        gameFlow.TransitionFinished += ApplyPending;
        session.AreaAnnounced += HandleAreaAnnounced;
    }

    /// <summary>
    /// Unsubscribes from the game flow and session events.
    /// </summary>
    public void Dispose()
    {
        gameFlow.AreaLoading -= HandleAreaLoading;
        gameFlow.TransitionFinished -= ApplyPending;
        session.AreaAnnounced -= HandleAreaAnnounced;
    }

    /// <summary>
    /// Host only: despawns the networked objects in the current area scene (and the gameplay scene for a new session)
    /// so clients drop their copies before anything unloads, then announces <paramref name="transition"/>'s area.
    /// </summary>
    private void HandleAreaLoading(AreaTransition transition)
    {
        if (!session.IsActive || !session.IsServer)
        {
            return;
        }

        session.DespawnObjectsIn(gameFlow.AreaScene);
        if (transition.NewSession)
        {
            session.DespawnObjectsIn(gameFlow.GameplayScene);
        }

        session.AnnounceArea(transition.Area.ScenePath, transition.SpawnId, transition.NewSession);
    }

    /// <summary>
    /// Keeps <paramref name="announcement"/> as the pending one, replacing any older, and applies it if it can.
    /// </summary>
    private void HandleAreaAnnounced(AreaAnnouncement announcement)
    {
        pending = announcement;
        ApplyPending();
    }

    /// <summary>
    /// On a connected client outside a transition, loads the pending announced area: a new game for a new session or
    /// when not in game, otherwise an area change. An area this build does not list logs an error, leaves the
    /// session and returns to the main menu.
    /// </summary>
    private void ApplyPending()
    {
        if (!pending.HasValue || gameFlow.IsTransitioning || !session.IsConnectedClient)
        {
            return;
        }

        AreaAnnouncement announcement = pending.Value;
        pending = null;

        SceneDefinition area = gameScenes.FindArea(announcement.ScenePath);
        if (area == null)
        {
            Debug.LogError($"The host moved to '{announcement.ScenePath}', which is not one of this build's areas; leaving the session.");
            session.Shutdown();
            _ = gameFlow.ShowMainMenuAsync();
            return;
        }

        session.BeginAnnouncedArea(announcement);
        if (announcement.NewSession || !gameFlow.IsInGame)
        {
            _ = gameFlow.StartNewGameAsync(area, announcement.SpawnId);
        }
        else
        {
            _ = gameFlow.ChangeAreaAsync(area, announcement.SpawnId);
        }
    }
}
