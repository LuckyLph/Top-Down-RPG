using System;
using UnityEngine;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that decides who spawns players: offline it spawns the local player; on the host it
/// spawns the host's player plus one per joined client, now and as clients join, each where <see cref="PlayerStarts"/>
/// places it; on a client it tells the host this machine has joined, so the host spawns its player and shows it the
/// players.
/// </summary>
public sealed class GameplayPlayers : IStartable, IDisposable
{
    private readonly PlayerSpawner spawner;
    private readonly NetworkSession session;
    private readonly PlayerStarts starts;
    private readonly IObjectResolver resolver;
    private bool registeredPrefab;
    private bool listeningForClients;

    /// <summary>
    /// Captures the spawner, the session, where joining players start and the Gameplay scope's resolver that injects
    /// network-spawned players.
    /// </summary>
    public GameplayPlayers(PlayerSpawner spawner, NetworkSession session, PlayerStarts starts, IObjectResolver resolver)
    {
        this.spawner = spawner;
        this.session = session;
        this.starts = starts;
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
            spawner.SpawnLocalPlayer();
            return;
        }

        if (!session.IsServer)
        {
            session.NotifyJoined();
            return;
        }

        spawner.SpawnLocalPlayer();
        foreach (ulong clientId in session.JoinedClients)
        {
            SpawnFor(clientId);
        }

        session.ClientJoined += SpawnFor;
        listeningForClients = true;
    }

    /// <summary>
    /// Stops spawning for newly joined clients and unregisters the player prefab, whichever of them was set up.
    /// </summary>
    public void Dispose()
    {
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
    /// Host only: spawns <paramref name="clientId"/>'s player where its player id starts, unless it already has one.
    /// </summary>
    private void SpawnFor(ulong clientId)
    {
        if (session.HasPlayerObject(clientId))
        {
            return;
        }

        if (starts.TryGetStart(session.PlayerIdOf(clientId), out WorldDefinition world, out Vector2 position))
        {
            spawner.SpawnRemotePlayer(clientId, world, position);
        }
    }
}
