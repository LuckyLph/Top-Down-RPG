using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

public class NetworkPlayersPlayModeTests
{
    private NetworkManager remoteClient;
    private IObjectResolver clientContainer;

    [TearDown]
    public void TearDown()
    {
        if (remoteClient != null)
        {
            remoteClient.Shutdown();
            Object.Destroy(remoteClient.gameObject);
            remoteClient = null;
        }

        clientContainer?.Dispose();
        clientContainer = null;
    }

    [UnityTest]
    public IEnumerator HostedGame_SpawnsAPlayerForAReadyClient_AndReplicatesItsMovementAndAttacks()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.IsInGame, Is.True);

        IObjectResolver gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        LocalPlayerTracker hostLocal = gameplay.Resolve<LocalPlayerTracker>();
        NetworkObject playerPrefab = gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab;
        Assert.That(hostPlayers.Players.Count, Is.EqualTo(1));
        Assert.That(hostLocal.Current.Controller.GetComponent<NetworkObject>().IsOwner, Is.True, "The host's own player should be spawned and owned by the host.");

        FakeInput clientInput = new();
        PlayerRegistry clientPlayers = new();
        LocalPlayerTracker clientLocal = new();
        clientContainer = BuildClientContainer(clientPlayers, clientLocal, clientInput);
        remoteClient = StartRemoteClient(session, playerPrefab, clientContainer);
        yield return SceneBootTestHelper.WaitUntil(() => remoteClient.IsConnectedClient, "the remote client to connect");

        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }

        Assert.That(clientPlayers.Players, Is.Empty, "Nothing is sent to a client before it reports ready.");
        Assert.That(hostPlayers.Players.Count, Is.EqualTo(1), "The host waits for the client to be ready before spawning its player.");

        using (FastBufferWriter writer = new(0, Allocator.Temp))
        {
            remoteClient.CustomMessagingManager.SendNamedMessage(NetworkSession.ClientReadyMessage, NetworkManager.ServerClientId, writer);
        }

        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && clientPlayers.Players.Count == 2 && clientLocal.Current != null,
            "both sides to see both players");

        PlayerController clientOwned = clientLocal.Current.Controller;
        PlayerController hostCopy = FindCopyOwnedBy(hostPlayers, remoteClient.LocalClientId);
        Assert.That(hostCopy, Is.Not.Null, "The host should hold a copy of the client's player owned by that client.");
        Assert.That(hostCopy.SimulatesMovement, Is.False);
        Assert.That(clientOwned.SimulatesMovement, Is.True);
        Assert.That(FindCopyOwnedBy(clientPlayers, NetworkManager.ServerClientId), Is.Not.Null, "The client should see the host's player.");
        DisableColliders(clientPlayers);

        float startX = hostCopy.transform.position.x;
        clientInput.Move = Vector2.right;
        yield return SceneBootTestHelper.WaitUntil(() => hostCopy.transform.position.x > startX + 0.5f, "the client's movement to reach the host");
        clientInput.Move = Vector2.zero;
        Assert.That(hostCopy.FacingDirection.x, Is.GreaterThan(0.9f));

        clientInput.AttackPressedThisFrame = true;
        yield return null;
        clientInput.AttackPressedThisFrame = false;
        yield return SceneBootTestHelper.WaitUntil(() => HasSlashOwnedBy(hostCopy.transform), "the client's attack to play on the host");

        remoteClient.Shutdown();
        yield return SceneBootTestHelper.WaitUntil(() => hostPlayers.Players.Count == 1, "the host to drop the disconnected client's player");
        Assert.That(hostLocal.Current, Is.Not.Null);
    }

    private static IObjectResolver BuildClientContainer(PlayerRegistry players, LocalPlayerTracker localPlayer, IPlayerInput input)
    {
        ContainerBuilder builder = new();
        builder.RegisterInstance(players).As<IPlayerRegistry>().AsSelf();
        builder.RegisterInstance(localPlayer);
        builder.RegisterInstance(input);
        builder.RegisterInstance<IClock>(UnityClock.Shared);
        builder.RegisterInstance<IGameAuthority>(new ClientAuthority());
        builder.Register<ActiveSpawnPoint>(Lifetime.Singleton);
        builder.Register<LocalPlayerCommandSource>(Lifetime.Singleton);
        builder.Register<PlayerBinder>(Lifetime.Singleton);
        builder.Register<CombatEvents>(Lifetime.Singleton);
        builder.Register<DamageService>(Lifetime.Singleton);
        builder.Register<SlashSpawner>(Lifetime.Singleton);
        return builder.Build();
    }

    private static NetworkManager StartRemoteClient(NetworkSession session, NetworkObject playerPrefab, IObjectResolver container)
    {
        GameObject clientObject = Object.Instantiate(session.NetworkManager.gameObject);
        clientObject.name = "RemoteClient";
        NetworkManager client = clientObject.GetComponent<NetworkManager>();
        clientObject.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", session.Settings.Port);
        client.PrefabHandler.AddHandler(playerPrefab, new InjectingNetworkPrefabHandler(playerPrefab, container));
        Assert.That(client.StartClient(), Is.True, "The remote client should start.");
        return client;
    }

    private static PlayerController FindCopyOwnedBy(PlayerRegistry players, ulong ownerClientId)
    {
        foreach (PlayerHandle handle in players.Players)
        {
            NetworkObject networkObject = handle.Transform.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.OwnerClientId == ownerClientId)
            {
                return handle.Transform.GetComponent<PlayerController>();
            }
        }

        return null;
    }

    private static void DisableColliders(PlayerRegistry players)
    {
        foreach (PlayerHandle handle in players.Players)
        {
            foreach (Collider2D playerCollider in handle.Transform.GetComponentsInChildren<Collider2D>())
            {
                playerCollider.enabled = false;
            }
        }
    }

    private static bool HasSlashOwnedBy(Transform owner)
    {
        foreach (SwordSlashAttack slash in Object.FindObjectsByType<SwordSlashAttack>())
        {
            if (slash.OwnerRoot == owner)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class FakeInput : IPlayerInput
    {
        public Vector2 Move { get; set; }
        public bool AttackPressedThisFrame { get; set; }
        public bool GameplayEnabled => true;
    }

    private sealed class ClientAuthority : IGameAuthority
    {
        public bool IsAuthoritative => false;
    }
}
