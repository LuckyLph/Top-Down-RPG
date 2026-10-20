using System;
using System.Collections.Generic;
using VContainer.Unity;

/// <summary>
/// Each player's death flow, acting only where <see cref="IGameAuthority.IsAuthoritative"/> (Docs/WorldStreaming.md:
/// Death and respawn). <see cref="GameplaySettings.RespawnDelaySeconds"/> of game time after a player dies (and once a
/// trip it was on has arrived), <see cref="PlayerRespawner"/> sends it to the nearest waystone it discovered in its
/// world; once it stands there its health is restored. No rule looks at the other players, so nothing happens when every
/// player is dead at once. A player that leaves the registry, or comes back to life some other way, is forgotten, and so
/// is everything on dispose.
/// </summary>
public sealed class PlayerDeathHandler : IStartable, ITickable, IDisposable
{
    private struct Pending
    {
        public float DueTime;
        public bool Travelling;
        public PlayerRespawner.Respawn Respawn;
    }

    private readonly IPlayerRegistry players;
    private readonly PlayerRespawner respawner;
    private readonly IGameAuthority authority;
    private readonly GameplaySettings settings;
    private readonly IClock clock;
    private readonly Dictionary<PlayerHandle, Pending> pending = new();
    private readonly List<PlayerHandle> waiting = new();

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
    /// The number of players waiting to respawn or travelling to their waystone, for tests.
    /// </summary>
    internal int PendingCount => pending.Count;

    /// <summary>
    /// Whether <paramref name="player"/> is on its way to its waystone, for tests.
    /// </summary>
    internal bool IsTravellingToWaystone(PlayerHandle player)
    {
        return player != null && pending.TryGetValue(player, out Pending state) && state.Travelling;
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
    /// Sends every dead player whose delay has run out, and that is on no trip, to its waystone (restoring it where it
    /// stands when it has none), and restores every player that reached its waystone.
    /// </summary>
    public void Tick()
    {
        if (pending.Count == 0)
        {
            return;
        }

        float now = clock.Time;
        waiting.Clear();
        waiting.AddRange(pending.Keys);
        for (int i = 0; i < waiting.Count; i++)
        {
            PlayerHandle player = waiting[i];
            Pending state = pending[player];
            if (player.Health == null || !player.Health.IsDead)
            {
                pending.Remove(player);
            }
            else if (state.Travelling)
            {
                if (respawner.HasArrived(player, state.Respawn))
                {
                    pending.Remove(player);
                    respawner.TryRestore(player);
                }
            }
            else if (now >= state.DueTime && !respawner.IsTravelling(player))
            {
                Depart(player);
            }
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
    /// Sends <paramref name="player"/> to its waystone and waits for it there, or restores it where it stands when it
    /// has none.
    /// </summary>
    private void Depart(PlayerHandle player)
    {
        if (respawner.TrySendToWaystone(player, out PlayerRespawner.Respawn respawn))
        {
            pending[player] = new Pending { Travelling = true, Respawn = respawn };
            return;
        }

        pending.Remove(player);
        respawner.TryRestore(player);
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
            pending[deadPlayer] = new Pending { DueTime = clock.Time + settings.RespawnDelaySeconds };
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
