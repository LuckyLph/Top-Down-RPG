using System.Collections;
using System.Threading;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;

public class NetworkSessionPlayModeTests
{
    private NetworkManager remoteHost;

    [TearDown]
    public void TearDown()
    {
        if (remoteHost != null)
        {
            remoteHost.Shutdown();
            Object.Destroy(remoteHost.gameObject);
            remoteHost = null;
        }
    }

    [UnityTest]
    public IEnumerator HostFromMenu_EntersTheGame_AndReturningToTheMenuEndsTheSession()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();

        Assert.That(session.IsHost, Is.True);
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.IsInGame, Is.True);
        Assert.That(session.IsActive, Is.True, "Entering the game must keep the session running.");

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return SceneBootTestHelper.WaitUntil(() => !session.IsActive, "the session to shut down in the menu");
    }

    [UnityTest]
    public IEnumerator JoinFromMenu_ConnectsToAHost_AndReturnsToTheMenuWhenTheHostLeaves()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        remoteHost = StartRemoteHost(session);

        Object.FindAnyObjectByType<MainMenuController>().JoinGame();
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInGame && !gameFlow.IsTransitioning, "the client to join and enter the game");

        Assert.That(session.IsConnectedClient, Is.True);
        Assert.That(session.IsHost, Is.False);
        Assert.That(remoteHost.ConnectedClientsIds.Count, Is.EqualTo(2), "The host should see itself and the joined client.");

        remoteHost.Shutdown();
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInMenu && !gameFlow.IsTransitioning, "the client to return to the menu after the host left");
        Assert.That(session.IsActive, Is.False);
    }

    [UnityTest]
    public IEnumerator Join_GivesUpWhenNoHostAnswers()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Awaitable<bool> join = session.JoinAsync("127.0.0.1", 1f, CancellationToken.None);
        yield return SceneBootTestHelper.WaitUntil(() => join.GetAwaiter().IsCompleted, "the join attempt to time out");

        Assert.That(join.GetAwaiter().GetResult(), Is.False);
        yield return SceneBootTestHelper.WaitUntil(() => !session.IsActive, "the failed client to shut down");
    }

    private static NetworkManager StartRemoteHost(NetworkSession session)
    {
        NetworkManager host = InProcessClient.CloneNetworkManager(session, "RemoteHost");
        GameObject hostObject = host.gameObject;
        hostObject.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", session.Settings.Port, "127.0.0.1");
        Assert.That(host.StartHost(), Is.True, "The remote host should start.");
        return host;
    }
}
