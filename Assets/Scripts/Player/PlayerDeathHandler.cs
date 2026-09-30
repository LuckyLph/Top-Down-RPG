using System;
using System.Threading;
using UnityEngine;
using VContainer.Unity;

public sealed class PlayerDeathHandler : IStartable, IDisposable
{
    private readonly IPlayerLocator player;
    private readonly GameFlow gameFlow;
    private readonly GameplaySettings settings;
    private readonly CancellationTokenSource disposeCancellation = new();

    public PlayerDeathHandler(IPlayerLocator player, GameFlow gameFlow, GameplaySettings settings)
    {
        this.player = player;
        this.gameFlow = gameFlow;
        this.settings = settings;
    }

    public void Start()
    {
        if (player.Health != null)
        {
            player.Health.Died += HandleDied;
        }
    }

    public void Dispose()
    {
        if (player.Health != null)
        {
            player.Health.Died -= HandleDied;
        }

        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
    }

    private void HandleDied(Health deadPlayer)
    {
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
