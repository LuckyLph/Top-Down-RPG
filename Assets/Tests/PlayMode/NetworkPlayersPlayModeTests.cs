using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

public class NetworkPlayersPlayModeTests
{
    private InProcessClient client;

    [TearDown]
    public void TearDown()
    {
        client?.Dispose();
        client = null;
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
        Assert.That(hostPlayers.Players.Count, Is.EqualTo(1));
        Assert.That(hostLocal.Current.Controller.GetComponent<NetworkObject>().IsOwner, Is.True, "The host's own player should be spawned and owned by the host.");

        client = InProcessClient.Start(session, gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");

        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }

        Assert.That(client.Players.Players, Is.Empty, "Nothing is sent to a client before it reports ready.");
        Assert.That(hostPlayers.Players.Count, Is.EqualTo(1), "The host waits for the client to be ready before spawning its player.");

        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");

        PlayerController clientOwned = client.LocalPlayer.Current.Controller;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        Assert.That(hostCopy, Is.Not.Null, "The host should hold a copy of the client's player owned by that client.");
        Assert.That(hostCopy.SimulatesMovement, Is.False);
        Assert.That(clientOwned.SimulatesMovement, Is.True);
        Assert.That(InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId), Is.Not.Null, "The client should see the host's player.");
        client.DisableColliders();

        float startX = hostCopy.transform.position.x;
        client.Input.Move = Vector2.right;
        yield return SceneBootTestHelper.WaitUntil(() => hostCopy.transform.position.x > startX + 0.5f, "the client's movement to reach the host");
        client.Input.Move = Vector2.zero;
        Assert.That(hostCopy.FacingDirection.x, Is.GreaterThan(0.9f));

        client.Input.AttackPressedThisFrame = true;
        yield return null;
        client.Input.AttackPressedThisFrame = false;
        yield return SceneBootTestHelper.WaitUntil(() => HasSlashOwnedBy(hostCopy.transform), "the client's attack to play on the host");

        client.Manager.Shutdown();
        yield return SceneBootTestHelper.WaitUntil(() => hostPlayers.Players.Count == 1, "the host to drop the disconnected client's player");
        Assert.That(hostLocal.Current, Is.Not.Null);
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
}
