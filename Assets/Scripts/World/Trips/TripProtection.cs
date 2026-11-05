using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope record of the travelling players kept out of play where game state is decided (Docs/KnownGaps.md,
/// Phase 2). From the approval of a trip (<see cref="LocalTripAuthority"/>, offline or on the host) until the player's
/// machine reports its fade in at the end of that trip (<see cref="ITripAuthority.ReportArrived"/>), or until
/// <see cref="GameplaySettings.TripProtectionSeconds"/> of game time have passed, a player is untargetable: its world's
/// mobs do not see it (<see cref="WorldPlayers"/>) and <see cref="HitService"/> ignores hits on it; and
/// <see cref="StatusEffectService"/> holds its statuses still. A report names its trip, so one from an earlier trip ends
/// nothing. Nothing is protected on a client, where no trip is approved.
/// </summary>
public sealed class TripProtection : IUnitProtection, ITickable, IDisposable
{
    private readonly IPlayerRegistry players;
    private readonly PlayerWorlds playerWorlds;
    private readonly IClock clock;
    private readonly GameplaySettings settings;
    private readonly List<Protection> protections = new();

    /// <summary>
    /// Creates the record, reading the trip a protection is for from <paramref name="worldsOfPlayers"/>, timing its
    /// limit from <paramref name="gameplaySettings"/> on <paramref name="gameClock"/>, and forgetting the players that
    /// leave <paramref name="registry"/>.
    /// </summary>
    public TripProtection(IPlayerRegistry registry, PlayerWorlds worldsOfPlayers, IClock gameClock, GameplaySettings gameplaySettings)
    {
        players = registry;
        playerWorlds = worldsOfPlayers;
        clock = gameClock;
        settings = gameplaySettings;
        players.PlayerRemoved += Forget;
    }

    /// <summary>
    /// Raised with a player whose protection started or ended.
    /// </summary>
    public event Action<PlayerHandle> Changed;

    public int Count => protections.Count;

    /// <summary>
    /// Protects <paramref name="player"/> for the trip <see cref="PlayerWorlds"/> has it on now, until its machine
    /// reports that trip's fade in or the time limit passes; a player already protected is protected for this trip
    /// instead, from now.
    /// </summary>
    public void Protect(PlayerHandle player)
    {
        if (player == null)
        {
            return;
        }

        float limit = settings != null ? settings.TripProtectionSeconds : 0f;
        Protection protection = new(player, playerWorlds.TripOf(player), clock.Time + limit);
        int index = IndexOf(player);
        if (index >= 0)
        {
            protections[index] = protection;
            return;
        }

        protections.Add(protection);
        Changed?.Invoke(player);
    }

    /// <summary>
    /// Ends <paramref name="player"/>'s protection once its machine faded in at the end of its trip number
    /// <paramref name="trip"/>, unless that is an earlier trip than the one it is protected for.
    /// </summary>
    public void End(PlayerHandle player, int trip)
    {
        int index = IndexOf(player);
        if (index >= 0 && trip >= protections[index].Trip)
        {
            Remove(index);
        }
    }

    /// <summary>
    /// Whether <paramref name="player"/> is protected.
    /// </summary>
    public bool IsProtected(PlayerHandle player)
    {
        return IndexOf(player) >= 0;
    }

    /// <summary>
    /// Whether <paramref name="unit"/> is a protected player's, matched by its <see cref="Health"/>.
    /// </summary>
    public bool IsProtected(DamageReceiver unit)
    {
        if (protections.Count == 0 || unit == null)
        {
            return false;
        }

        Health health = unit.Health;
        for (int i = 0; i < protections.Count; i++)
        {
            Health protectedHealth = protections[i].Player.Health;
            if (!ReferenceEquals(protectedHealth, null) && ReferenceEquals(protectedHealth, health))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Each frame: ends the protections whose time limit has passed, each with a warning, since its machine should have
    /// reported its fade in before.
    /// </summary>
    public void Tick()
    {
        for (int i = protections.Count - 1; i >= 0; i--)
        {
            if (clock.Time >= protections[i].Until)
            {
                Transform body = protections[i].Player.Transform;
                Debug.LogWarning($"The protection of {(body != null ? body.name : "a player")} on its trip {protections[i].Trip} ran out before its machine reported fading in.");
                Remove(i);
            }
        }
    }

    /// <summary>
    /// Stops following the registry.
    /// </summary>
    public void Dispose()
    {
        players.PlayerRemoved -= Forget;
    }

    /// <summary>
    /// The index of <paramref name="player"/>'s protection, or -1.
    /// </summary>
    private int IndexOf(PlayerHandle player)
    {
        for (int i = 0; i < protections.Count; i++)
        {
            if (protections[i].Player == player)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Ends the protection at <paramref name="index"/> and raises <see cref="Changed"/>.
    /// </summary>
    private void Remove(int index)
    {
        PlayerHandle player = protections[index].Player;
        protections.RemoveAt(index);
        Changed?.Invoke(player);
    }

    /// <summary>
    /// Ends the protection of a player that left the registry.
    /// </summary>
    private void Forget(PlayerHandle player)
    {
        int index = IndexOf(player);
        if (index >= 0)
        {
            Remove(index);
        }
    }

    /// <summary>
    /// One protected player: the trip it is protected for and the game time its protection runs out at.
    /// </summary>
    private readonly struct Protection
    {
        /// <summary>
        /// Protects <paramref name="player"/> on its trip <paramref name="trip"/> until <paramref name="until"/>.
        /// </summary>
        public Protection(PlayerHandle player, int trip, float until)
        {
            Player = player;
            Trip = trip;
            Until = until;
        }

        public PlayerHandle Player { get; }
        public int Trip { get; }
        public float Until { get; }
    }
}
