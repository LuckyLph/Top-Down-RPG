using System;
using UnityEngine;
using VContainer.Unity;

public sealed class NetworkAreaSync : IStartable, IDisposable
{
    private readonly NetworkSession session;
    private readonly GameFlow gameFlow;
    private readonly GameScenes gameScenes;
    private AreaAnnouncement? pending;

    public NetworkAreaSync(NetworkSession session, GameFlow gameFlow, GameScenes gameScenes)
    {
        this.session = session;
        this.gameFlow = gameFlow;
        this.gameScenes = gameScenes;
    }

    public void Start()
    {
        gameFlow.AreaLoading += HandleAreaLoading;
        gameFlow.TransitionFinished += ApplyPending;
        session.AreaAnnounced += HandleAreaAnnounced;
    }

    public void Dispose()
    {
        gameFlow.AreaLoading -= HandleAreaLoading;
        gameFlow.TransitionFinished -= ApplyPending;
        session.AreaAnnounced -= HandleAreaAnnounced;
    }

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

    private void HandleAreaAnnounced(AreaAnnouncement announcement)
    {
        pending = announcement;
        ApplyPending();
    }

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
