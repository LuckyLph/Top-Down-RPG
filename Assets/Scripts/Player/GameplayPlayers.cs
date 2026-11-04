using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that decides who spawns players: offline it spawns the local player; on the host it
/// spawns the host's player plus one per joined client, now and as clients join, each holding the weapon the
/// <see cref="SessionSave"/> has for its player id and where <see cref="PlayerStarts"/> places it (its saved place by
/// player id, else the starting world's entry), once the host has loaded that world and
/// the chunks around the place (<see cref="IWorldSession.PrepareAsync"/>, which moves a place the player can no longer
/// stand on to walkable ground); on a client it tells the host this machine has joined, so the host spawns its player
/// and shows it the players.
/// </summary>
public sealed class GameplayPlayers : IStartable, IDisposable
{
    private readonly PlayerSpawner spawner;
    private readonly NetworkSession session;
    private readonly PlayerStarts starts;
    private readonly SessionSave sessionSave;
    private readonly IObjectResolver resolver;
    private readonly HashSet<ulong> preparing = new();
    private readonly CancellationTokenSource disposeCancellation = new();
    private IWorldSession worldSession;
    private bool registeredPrefab;
    private bool listeningForClients;

    /// <summary>
    /// Captures the spawner, the session, where players start, the saved weapons and the Gameplay scope's resolver that
    /// injects network-spawned players and provides the world session, when there is one.
    /// </summary>
    public GameplayPlayers(PlayerSpawner spawner, NetworkSession session, PlayerStarts starts, SessionSave sessionSave, IObjectResolver resolver)
    {
        this.spawner = spawner;
        this.session = session;
        this.starts = starts;
        this.sessionSave = sessionSave;
        this.resolver = resolver;
    }

    /// <summary>
    /// Registers the player prefab with the session so copies Netcode spawns are built through the Gameplay resolver
    /// in the players scene. Runs from the scope's build callback; does nothing offline or when already registered.
    /// </summary>
    public void RegisterNetworkPrefabs()
    {
        if (!session.IsActive || registeredPrefab)
        {
            return;
        }

        session.RegisterPrefab(spawner.PlayerNetworkPrefab, resolver, spawner.PlayersScene);
        registeredPrefab = true;
    }

    /// <summary>
    /// Offline, spawns the local player. On the host, spawns its own player and one per client already joined, then one
    /// for each client that joins later. On a client, tells the host this machine has joined.
    /// </summary>
    public void Start()
    {
        if (!session.IsActive)
        {
            spawner.SpawnLocalPlayer(sessionSave.WeaponOf(session.LocalPlayerId));
            return;
        }

        if (!session.IsServer)
        {
            session.NotifyJoined();
            return;
        }

        resolver.TryResolve(out worldSession);
        spawner.SpawnLocalPlayer(sessionSave.WeaponOf(session.LocalPlayerId));
        foreach (ulong clientId in session.JoinedClients)
        {
            SpawnFor(clientId);
        }

        session.ClientJoined += SpawnFor;
        listeningForClients = true;
    }

    /// <summary>
    /// Stops spawning for newly joined clients, drops the spawns still waiting for their world and unregisters the
    /// player prefab, whichever of them was set up.
    /// </summary>
    public void Dispose()
    {
        disposeCancellation.Cancel();
        disposeCancellation.Dispose();
        if (listeningForClients)
        {
            session.ClientJoined -= SpawnFor;
        }

        if (registeredPrefab)
        {
            session.UnregisterPrefab(spawner.PlayerNetworkPrefab);
        }
    }

    /// <summary>
    /// Host only: spawns <paramref name="clientId"/>'s player where its player id starts, once that place is loaded,
    /// unless it already has one or one is on its way.
    /// </summary>
    private void SpawnFor(ulong clientId)
    {
        if (session.HasPlayerObject(clientId) || preparing.Contains(clientId))
        {
            return;
        }

        string playerId = session.PlayerIdOf(clientId);
        if (starts.TryGetStart(playerId, out WorldDefinition world, out Vector2 position))
        {
            _ = SpawnWhenPreparedAsync(clientId, playerId, world, position);
        }
    }

    /// <summary>
    /// Has the world session load <paramref name="world"/> around <paramref name="position"/> (at once when it already
    /// is), then spawns <paramref name="clientId"/>'s player there, or on the walkable ground the session moved the
    /// place of <paramref name="playerId"/> to, if the client is still joined and has none.
    /// </summary>
    private async Awaitable SpawnWhenPreparedAsync(ulong clientId, string playerId, WorldDefinition world, Vector2 position)
    {
        preparing.Add(clientId);
        try
        {
            if (worldSession != null)
            {
                position = await worldSession.PrepareAsync(world, position, playerId, disposeCancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            preparing.Remove(clientId);
        }

        if (session.IsServer && session.IsClientJoined(clientId) && !session.HasPlayerObject(clientId))
        {
            spawner.SpawnRemotePlayer(clientId, world, position, sessionSave.WeaponOf(playerId));
        }
    }
}
