using System;
using System.Threading;
using UnityEngine;
using VContainer.Unity;

public sealed class PlayerDeathHandler : IStartable, IDisposable
{
    private readonly IPlayerRegistry players;
    private readonly GameFlow gameFlow;
    private readonly GameplaySettings settings;
    private readonly CancellationTokenSource disposeCancellation = new();
    private bool restartPending;

    public PlayerDeathHandler(IPlayerRegistry players, GameFlow gameFlow, GameplaySettings settings)
    {
        this.players = players;
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

    private void HandleDied(Health deadPlayer)
    {
        RestartIfPartyWiped();
    }

    private void RestartIfPartyWiped()
    {
        if (restartPending || players.Players.Count == 0 || players.AnyAlive)
        {
            return;
        }

        restartPending = true;
        _ = RestartAfterDelayAsync(disposeCancellation.Token);
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
