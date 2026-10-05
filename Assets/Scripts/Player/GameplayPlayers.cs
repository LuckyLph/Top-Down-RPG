using System;
using VContainer;
using VContainer.Unity;

/// <summary>
/// Gameplay-scope entry point that decides who spawns players: offline it spawns the local player; on the host it
/// spawns the host's player plus one per ready client, now and as clients become ready. Clients spawn nothing and
/// receive the host's spawns through Netcode.
/// </summary>
public sealed class GameplayPlayers : IStartable, IDisposable
{
    private readonly PlayerSpawner spawner;
    private readonly NetworkSession session;
    private readonly IObjectResolver resolver;
    private bool registeredPrefab;
    private bool listeningForClients;

    /// <summary>
    /// Captures the spawner, the session and the Gameplay scope's resolver that injects network-spawned players.
    /// </summary>
    public GameplayPlayers(PlayerSpawner spawner, NetworkSession session, IObjectResolver resolver)
    {
        this.spawner = spawner;
        this.session = session;
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
    /// Offline, spawns the local player. On the host, spawns its own player and one per client already ready, then
    /// spawns one for each client that becomes ready later. Does nothing on a client.
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
            return;
        }

        spawner.SpawnLocalPlayer();
        foreach (ulong clientId in session.ReadyClients)
        {
            spawner.SpawnRemotePlayer(clientId);
        }

        session.ClientReady += spawner.SpawnRemotePlayer;
        listeningForClients = true;
    }

    /// <summary>
    /// Stops spawning for newly ready clients and unregisters the player prefab, whichever of them was set up.
    /// </summary>
    public void Dispose()
    {
        if (listeningForClients)
        {
            session.ClientReady -= spawner.SpawnRemotePlayer;
        }

        if (registeredPrefab)
        {
            session.UnregisterPrefab(spawner.PlayerNetworkPrefab);
        }
    }
}
