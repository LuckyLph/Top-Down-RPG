using System;
using System.Threading;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// The party's death flow, acting only where <see cref="IGameAuthority.IsAuthoritative"/>: a dead player respawns
/// after the respawn delay, and once every registered player is dead the game restarts in the current area after the
/// restart delay. Pending respawns and restarts are cancelled on dispose.
/// </summary>
public sealed class PlayerDeathHandler : IStartable, IDisposable
{
    private readonly IPlayerRegistry players;
    private readonly PlayerRespawner respawner;
    private readonly IGameAuthority authority;
    private readonly GameFlow gameFlow;
    private readonly GameplaySettings settings;
    private readonly CancellationTokenSource disposeCancellation = new();
    private bool restartPending;

    /// <summary>
    /// Captures the registry, the respawner, the authority, the game flow that restarts and the delay settings.
    /// </summary>
    public PlayerDeathHandler(
        IPlayerRegistry players,
        PlayerRespawner respawner,
        IGameAuthority authority,
        GameFlow gameFlow,
        GameplaySettings settings)
    {
        this.players = players;
        this.respawner = respawner;
        this.authority = authority;
        this.gameFlow = gameFlow;
        this.settings = settings;
    }

    /// <summary>
    /// Listens for the death of every registered player, now and as players are added, and for players leaving.
    /// </summary>
    public void Start()
    {
        foreach (PlayerHandle player in players.Players)
        {
            Subscribe(player);
        }

        players.PlayerAdded += Subscribe;
        players.PlayerRemoved += HandlePlayerRemoved;
    }

    /// <summary>
    /// Stops listening to the registry and every player, and cancels any pending respawn or restart.
    /// </summary>
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

    /// <summary>
    /// Listens for <paramref name="player"/> dying, when it has a health component.
    /// </summary>
    private void Subscribe(PlayerHandle player)
    {
        if (player.Health != null)
        {
            player.Health.Died += HandleDied;
        }
    }

    /// <summary>
    /// Stops listening for <paramref name="player"/> dying.
    /// </summary>
    private void Unsubscribe(PlayerHandle player)
    {
        if (player.Health != null)
        {
            player.Health.Died -= HandleDied;
        }
    }

    /// <summary>
    /// Stops listening to a player that left and restarts when everyone still registered is dead.
    /// </summary>
    private void HandlePlayerRemoved(PlayerHandle player)
    {
        Unsubscribe(player);
        RestartIfPartyWiped();
    }

    /// <summary>
    /// On the authority, restarts if this death wiped the party and otherwise schedules the dead player's respawn.
    /// </summary>
    private void HandleDied(Health deadHealth)
    {
        if (!authority.IsAuthoritative || RestartIfPartyWiped())
        {
            return;
        }

        PlayerHandle deadPlayer = FindPlayer(deadHealth);
        if (deadPlayer != null)
        {
            _ = RespawnAfterDelayAsync(deadPlayer, disposeCancellation.Token);
        }
    }

    /// <summary>
    /// Schedules a restart, once, when there are players and none is alive. Returns true when a restart is pending
    /// or this machine is not the authority, so the caller does nothing else.
    /// </summary>
    private bool RestartIfPartyWiped()
    {
        if (restartPending || !authority.IsAuthoritative)
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

    /// <summary>
    /// The registered player whose health is <paramref name="health"/>, or null.
    /// </summary>
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

    /// <summary>
    /// Waits the respawn delay, then respawns <paramref name="player"/> unless a restart became pending meanwhile.
    /// Returns quietly when cancelled.
    /// </summary>
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

    /// <summary>
    /// Waits the restart delay and for any running transition (a trip between worlds keeps game time running, so the
    /// delay can end during one), then starts the session over in the current area or world. Returns quietly when
    /// cancelled.
    /// </summary>
    private async Awaitable RestartAfterDelayAsync(CancellationToken cancellation)
    {
        try
        {
            await Awaitable.WaitForSecondsAsync(settings.RestartDelaySeconds, cancellation);
            while (gameFlow.IsTransitioning)
            {
                await Awaitable.NextFrameAsync(cancellation);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await gameFlow.RestartAsync();
    }
}
