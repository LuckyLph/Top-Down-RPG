using System;
using System.Threading;
using UnityEngine;
using VContainer.Unity;

public sealed class PlayerDeathHandler : IStartable, IDisposable
{
    private readonly IPlayerRegistry players;
    private readonly PlayerRespawner respawner;
    private readonly GameFlow gameFlow;
    private readonly GameplaySettings settings;
    private readonly CancellationTokenSource disposeCancellation = new();
    private bool restartPending;

    public PlayerDeathHandler(IPlayerRegistry players, PlayerRespawner respawner, GameFlow gameFlow, GameplaySettings settings)
    {
        this.players = players;
        this.respawner = respawner;
        this.gameFlow = gameFlow;
        this.settings = settings;
    }

    public void Start()
    {
        foreach (PlayerHandle player in players.Players)
        {
            Subscribe(player);
        }

        players.PlayerAdded += Subscribe;
        players.PlayerRemoved += HandlePlayerRemoved;
    }

    public void Dispose()
    {
        players.PlayerAdded -= Subscribe;
        players.PlayerRemoved -= HandlePlayerRemoved;

        foreach (PlayerHandle player in players.Players)
        {
            Unsubscribe(player);
        }

        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private void Subscribe(PlayerHandle player)
    {
        if (player.Health != null)
        {
            player.Health.Died += HandleDied;
        }
    }

    private void Unsubscribe(PlayerHandle player)
    {
        if (player.Health != null)
        {
            player.Health.Died -= HandleDied;
        }
    }

    private void HandlePlayerRemoved(PlayerHandle player)
    {
        Unsubscribe(player);
        RestartIfPartyWiped();
    }

    private void HandleDied(Health deadHealth)
    {
        if (RestartIfPartyWiped())
        {
            return;
        }

        PlayerHandle deadPlayer = FindPlayer(deadHealth);
        if (deadPlayer != null)
        {
            _ = RespawnAfterDelayAsync(deadPlayer, disposeCancellation.Token);
        }
    }

    private bool RestartIfPartyWiped()
    {
        if (restartPending)
        {
            return true;
        }

        if (players.Players.Count == 0 || players.AnyAlive)
        {
            return false;
        }

        restartPending = true;
        _ = RestartAfterDelayAsync(disposeCancellation.Token);
        return true;
    }

    private PlayerHandle FindPlayer(Health health)
    {
        foreach (PlayerHandle player in players.Players)
        {
            if (player.Health == health)
            {
                return player;
            }
        }

        return null;
    }

    private async Awaitable RespawnAfterDelayAsync(PlayerHandle player, CancellationToken cancellation)
    {
        try
        {
            await Awaitable.WaitForSecondsAsync(settings.RespawnDelaySeconds, cancellation);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!restartPending)
        {
            respawner.TryRespawn(player);
        }
    }

    private async Awaitable RestartAfterDelayAsync(CancellationToken cancellation)
    {
        try
        {
            await Awaitable.WaitForSecondsAsync(settings.RestartDelaySeconds, cancellation);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await gameFlow.StartNewGameAsync(gameFlow.CurrentArea);
    }
}
