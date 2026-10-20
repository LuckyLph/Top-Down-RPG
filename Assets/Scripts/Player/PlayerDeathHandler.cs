using System;
using System.Collections.Generic;
using VContainer.Unity;

/// <summary>
/// Each player's death flow, acting only where <see cref="IGameAuthority.IsAuthoritative"/> (Docs/WorldStreaming.md:
/// Death and respawn): a dead player respawns on its own after <see cref="GameplaySettings.RespawnDelaySeconds"/> of
/// game time. No rule looks at the other players, so nothing happens when every player is dead at once. A player that
/// leaves the registry forgets its pending respawn, and so does everything on dispose.
/// </summary>
public sealed class PlayerDeathHandler : IStartable, ITickable, IDisposable
{
    private readonly IPlayerRegistry players;
    private readonly PlayerRespawner respawner;
    private readonly IGameAuthority authority;
    private readonly GameplaySettings settings;
    private readonly IClock clock;
    private readonly Dictionary<PlayerHandle, float> pending = new();
    private readonly List<PlayerHandle> due = new();

    /// <summary>
    /// Captures the registry, the respawner, the authority, the delay settings and the game clock the delay runs on.
    /// </summary>
    public PlayerDeathHandler(IPlayerRegistry players, PlayerRespawner respawner, IGameAuthority authority, GameplaySettings settings, IClock clock)
    {
        this.players = players;
        this.respawner = respawner;
        this.authority = authority;
        this.settings = settings;
        this.clock = clock;
    }

    /// <summary>
    /// The number of players waiting to respawn, for tests.
    /// </summary>
    internal int PendingCount => pending.Count;

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
    /// Respawns every dead player whose delay has run out.
    /// </summary>
    public void Tick()
    {
        if (pending.Count == 0)
        {
            return;
        }

        float now = clock.Time;
        due.Clear();
        foreach (KeyValuePair<PlayerHandle, float> respawn in pending)
        {
            if (now >= respawn.Value)
            {
                due.Add(respawn.Key);
            }
        }

        for (int i = 0; i < due.Count; i++)
        {
            pending.Remove(due[i]);
            respawner.TryRespawn(due[i]);
        }
    }

    /// <summary>
    /// Stops listening to the registry and every player, and forgets every pending respawn.
    /// </summary>
    public void Dispose()
    {
        players.PlayerAdded -= Subscribe;
        players.PlayerRemoved -= HandlePlayerRemoved;

        foreach (PlayerHandle player in players.Players)
        {
            Unsubscribe(player);
        }

        pending.Clear();
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
    /// Stops listening to a player that left and forgets its pending respawn.
    /// </summary>
    private void HandlePlayerRemoved(PlayerHandle player)
    {
        Unsubscribe(player);
        pending.Remove(player);
    }

    /// <summary>
    /// On the authority, schedules the dead player's respawn after the respawn delay.
    /// </summary>
    private void HandleDied(Health deadHealth)
    {
        if (!authority.IsAuthoritative)
        {
            return;
        }

        PlayerHandle deadPlayer = FindPlayer(deadHealth);
        if (deadPlayer != null)
        {
            pending[deadPlayer] = clock.Time + settings.RespawnDelaySeconds;
        }
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
}
