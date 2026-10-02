using System.Collections;
using System.Threading;
using NUnit.Framework;
using Unity.Collections;
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
    public IEnumerator HostFromMenu_EntersTheGame_SavesIt_AndReturningToTheMenuEndsTheSession()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();

        Assert.That(session.IsHost, Is.True);
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.IsInGame, Is.True);
        Assert.That(session.IsActive, Is.True, "Entering the game must keep the session running.");
        Assert.That(saveStore.WriteCount, Is.EqualTo(1), "The host saves the area it entered.");

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return SceneBootTestHelper.WaitUntil(() => !session.IsActive, "the session to shut down in the menu");
    }

    [UnityTest]
    public IEnumerator JoinFromMenu_FollowsTheHostsAreaAnnouncements_AndReturnsToTheMenuWhenTheHostLeaves()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        remoteHost = StartRemoteHost(session);
        int lastReadyEpoch = 0;
        remoteHost.CustomMessagingManager.RegisterNamedMessageHandler(
            NetworkSession.ClientReadyMessage,
            (sender, reader) =>
            {
                reader.ReadValueSafe(out int epoch);
                lastReadyEpoch = epoch;
            });

        Object.FindAnyObjectByType<MainMenuController>().JoinGame();
        yield return SceneBootTestHelper.WaitUntil(() => session.IsConnectedClient, "the client to connect");
        for (int i = 0; i < 10; i++)
        {
            yield return null;
        }

        Assert.That(gameFlow.IsInMenu, Is.True, "A client waits in the menu until the host says which area to load.");

        string areaPath = gameFlow.Scenes.StartingArea.ScenePath;
        ulong clientId = remoteHost.ConnectedClientsIds[remoteHost.ConnectedClientsIds.Count - 1];
        Announce(clientId, new AreaAnnouncement(areaPath, gameFlow.Scenes.StartingSpawnId, 1, true));
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInGame && !gameFlow.IsTransitioning, "the client to load the announced area");
        yield return SceneBootTestHelper.WaitUntil(() => lastReadyEpoch == 1, "the client to report ready for that area");

        Assert.That(session.IsConnectedClient, Is.True);
        Assert.That(session.IsHost, Is.False);
        Assert.That(gameFlow.CurrentArea.ScenePath, Is.EqualTo(areaPath));
        Assert.That(remoteHost.ConnectedClientsIds.Count, Is.EqualTo(2), "The host should see itself and the joined client.");

        UnityEngine.SceneManagement.Scene firstAreaScene = gameFlow.AreaScene;
        Announce(clientId, new AreaAnnouncement(areaPath, gameFlow.Scenes.StartingSpawnId, 2, false));
        yield return SceneBootTestHelper.WaitUntil(
            () => lastReadyEpoch == 2 && gameFlow.IsInGame && !gameFlow.IsTransitioning,
            "the client to follow the host into the next area and report ready again");
        Assert.That(gameFlow.AreaScene, Is.Not.EqualTo(firstAreaScene), "A non-session announcement reloads the area.");

        UnityEngine.SceneManagement.Scene firstGameplayScene = gameFlow.GameplayScene;
        Announce(clientId, new AreaAnnouncement(areaPath, gameFlow.Scenes.StartingSpawnId, 3, true));
        yield return SceneBootTestHelper.WaitUntil(
            () => lastReadyEpoch == 3 && gameFlow.IsInGame && !gameFlow.IsTransitioning,
            "the client to restart with the host and report ready again");
        Assert.That(gameFlow.GameplayScene, Is.Not.EqualTo(firstGameplayScene), "A new-session announcement reloads the whole session.");

        remoteHost.Shutdown();
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInMenu && !gameFlow.IsTransitioning, "the client to return to the menu after the host left");
        Assert.That(session.IsActive, Is.False);
        Assert.That(saveStore.WriteCount, Is.Zero, "A joined client never saves the host's areas.");
    }

    [UnityTest]
    public IEnumerator HostLeavingWhileAClientLoadsItsArea_ReturnsTheClientToTheMenu_WithoutSaving()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        remoteHost = StartRemoteHost(session);
        bool lostDuringTransition = false;
        int connectionLosses = 0;
        session.ConnectionLost += () =>
        {
            connectionLosses++;
            lostDuringTransition = gameFlow.IsTransitioning;
        };

        Object.FindAnyObjectByType<MainMenuController>().JoinGame();
        yield return SceneBootTestHelper.WaitUntil(() => session.IsConnectedClient, "the client to connect");

        ulong clientId = remoteHost.ConnectedClientsIds[remoteHost.ConnectedClientsIds.Count - 1];
        Announce(clientId, new AreaAnnouncement(gameFlow.Scenes.StartingArea.ScenePath, gameFlow.Scenes.StartingSpawnId, 1, true));
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsTransitioning, "the client to start loading the announced area");
        Assert.That(session.IsAuthoritative, Is.False);

        remoteHost.Shutdown();
        yield return SceneBootTestHelper.WaitUntil(() => connectionLosses > 0, "the client to notice the host left");
        Assert.That(lostDuringTransition, Is.True, "The host must leave while the client is still loading for this test to mean anything.");
        Assert.That(session.IsAuthoritative, Is.False, "A client that lost its host must not start deciding game state on its own.");

        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInMenu && !gameFlow.IsTransitioning, "the client to return to the menu after its load");
        Assert.That(gameFlow.IsInGame, Is.False);
        Assert.That(session.IsActive, Is.False);
        Assert.That(saveStore.WriteCount, Is.Zero, "A client that lost its host never saves the host's area.");
        Assert.That(session.IsAuthoritative, Is.True, "Back in the menu, an offline game decides its own state again.");

        Object.FindAnyObjectByType<MainMenuController>().StartNewGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.IsInGame, Is.True);
        Assert.That(saveStore.WriteCount, Is.EqualTo(1), "An offline game after the lost session saves as usual.");
    }

    [UnityTest]
    public IEnumerator MenuButtons_DoNothingWhileTheMenuIsStillFading()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        gameFlow.ShowMainMenuAsync();
        Assert.That(gameFlow.IsTransitioning, Is.True);

        menu.HostGame();
        Assert.That(session.IsActive, Is.False, "Hosting during a transition would be shut down when the menu finishes fading in.");
        menu.JoinGame();
        Assert.That(session.IsActive, Is.False, "Joining during a transition would be shut down when the menu finishes fading in.");

        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.IsInMenu, Is.True);
        Assert.That(session.IsActive, Is.False);

        menu.HostGame();
        Assert.That(session.IsHost, Is.True, "Once the menu has settled, Host Game works.");
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.IsInGame, Is.True);
    }

    [UnityTest]
    public IEnumerator Join_ReportsTheHostsRefusal()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        remoteHost = StartRemoteHost(session, "the game is full (4 players).");
        Awaitable<bool> join = session.JoinAsync("127.0.0.1", 10f, CancellationToken.None);
        yield return SceneBootTestHelper.WaitUntil(() => join.GetAwaiter().IsCompleted, "the refused join to end");

        Assert.That(join.GetAwaiter().GetResult(), Is.False);
        Assert.That(session.JoinRefusal, Is.EqualTo("the game is full (4 players)."));
        Assert.That(remoteHost.ConnectedClientsIds.Count, Is.EqualTo(1), "Only the host itself is connected.");
        yield return SceneBootTestHelper.WaitUntil(() => !session.IsActive, "the refused client to shut down");
    }

    [UnityTest]
    public IEnumerator Host_RefusesAClientRunningAnotherVersion()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Assert.That(session.StartHost(), Is.True);

        NetworkManager outdated = InProcessClient.CloneNetworkManager(session, "OutdatedClient");
        try
        {
            outdated.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", session.Settings.Port);
            outdated.NetworkConfig.ConnectionData = JoinApproval.CreatePayload(Application.version + "-other");
            Assert.That(outdated.StartClient(), Is.True);
            yield return SceneBootTestHelper.WaitUntil(() => !outdated.IsListening, "the host to refuse the outdated client");

            Assert.That(JoinApproval.TryDecodeRefusal(outdated.DisconnectReason, out string refusal), Is.True, outdated.DisconnectReason);
            Assert.That(refusal, Does.Contain(Application.version + "-other"));
            Assert.That(session.NetworkManager.ConnectedClientsIds.Count, Is.EqualTo(1), "Only the host itself is connected.");
        }
        finally
        {
            Object.Destroy(outdated.gameObject);
            session.Shutdown();
        }
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

    private void Announce(ulong clientId, AreaAnnouncement announcement)
    {
        using FastBufferWriter writer = new(1024, Allocator.Temp);
        announcement.Write(writer);
        remoteHost.CustomMessagingManager.SendNamedMessage(NetworkSession.AreaAnnouncementMessage, clientId, writer);
    }

    private static NetworkManager StartRemoteHost(NetworkSession session, string refusal = null)
    {
        NetworkManager host = InProcessClient.CloneNetworkManager(session, "RemoteHost");
        GameObject hostObject = host.gameObject;
        hostObject.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", session.Settings.Port, "127.0.0.1");
        host.ConnectionApprovalCallback = (request, response) =>
        {
            bool refuse = refusal != null && request.ClientNetworkId != NetworkManager.ServerClientId;
            response.Approved = !refuse;
            response.Reason = refuse ? JoinApproval.EncodeRefusal(refusal) : null;
        };
        Assert.That(host.StartHost(), Is.True, "The remote host should start.");
        return host;
    }
}
