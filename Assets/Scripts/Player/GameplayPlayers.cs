using System;
using VContainer;
using VContainer.Unity;

public sealed class GameplayPlayers : IStartable, IDisposable
{
    private readonly PlayerSpawner spawner;
    private readonly NetworkSession session;
    private readonly IObjectResolver resolver;
    private bool registeredPrefab;
    private bool listeningForClients;

    public GameplayPlayers(PlayerSpawner spawner, NetworkSession session, IObjectResolver resolver)
    {
        this.spawner = spawner;
        this.session = session;
        this.resolver = resolver;
    }

    public void RegisterNetworkPrefabs()
    {
        if (!session.IsActive || registeredPrefab)
        {
            return;
        }

        session.RegisterPrefab(spawner.PlayerNetworkPrefab, resolver, spawner.PlayersScene);
        registeredPrefab = true;
    }

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
