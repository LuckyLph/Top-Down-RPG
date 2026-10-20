using System.Collections;
using System.Threading;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// PlayMode tests for <see cref="NetworkSession"/> hosting and joining from the main menu: hosting the starting world,
/// a joined client following an in-process host into the world it is placed in, reporting and releasing chunks, taking
/// a trip the host sends, losing the host mid-join and mid-trip, join refusals, version checks and join timeouts.
/// </summary>
public class NetworkSessionPlayModeTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";
    private const string CombatSettingsPath = "Assets/Data/Combat/CombatSettings.asset";

    private InProcessHost remoteHost;

    /// <summary>
    /// Shuts down and destroys the remote host when the test started one.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        remoteHost?.Dispose();
        remoteHost = null;
    }

    /// <summary>
    /// Hosting from the main menu starts a session in the starting world at its entry, writes no save (worlds are not
    /// saved yet), and shuts down when the game returns to the menu.
    /// </summary>
    [UnityTest]
    public IEnumerator HostFromMenu_EntersTheStartingWorld_AndReturningToTheMenuEndsTheSession()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();

        Assert.That(session.IsHost, Is.True);
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.IsInGame, Is.True);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(gameFlow.Scenes.StartingWorld), "Host Game starts in the starting world.");
        LocalPlayer local = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current;
        WorldDefinition world = gameFlow.Scenes.StartingWorld;
        Assert.That((Vector2)local.Transform.position, Is.EqualTo(world.ToWorld(world.FindEntry(gameFlow.Scenes.StartingWorldEntry).Position)));
        Assert.That(local.Controller.GetComponent<PlayerWorldSync>().State.WorldId, Is.EqualTo(world.WorldId), "The host's player carries its world.");
        Assert.That(session.IsActive, Is.True, "Entering the game must keep the session running.");
        Assert.That(saveStore.WriteCount, Is.Zero, "Worlds are not saved yet.");

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return SceneBootTestHelper.WaitUntil(() => !session.IsActive, "the session to shut down in the menu");
    }

    /// <summary>
    /// A client joining from the menu sends its player id, loads Gameplay and joins; the host spawns its player in a
    /// world, and the client loads that world around it and reports the chunk ready for trip 0. Walking its player into
    /// another chunk, it releases each chunk it leaves and keeps the chunk loaded until the host acknowledges. A trip
    /// the host sends takes it into the interior, where it reports the new chunk for trip 1; it returns to the menu
    /// when the host leaves and never saves.
    /// </summary>
    [UnityTest]
    public IEnumerator JoinFromMenu_LoadsTheHostAssignedWorld_ReportsAndReleasesChunks_AndTravelsWhenTheHostSaysSo()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        WorldDefinition testlands = gameFlow.Scenes.StartingWorld;
        WorldDefinition interior = FindWorld(gameFlow, "SandboxInterior");
        remoteHost = StartRemoteHost(session, gameFlow.Scenes);
        remoteHost.SpawnWorld = testlands;
        remoteHost.SpawnPosition = testlands.ToWorld(testlands.FindEntry("start").Position);

        Object.FindAnyObjectByType<MainMenuController>().JoinGame();
        yield return SceneBootTestHelper.WaitUntil(() => session.IsConnectedClient, "the client to connect");
        yield return SceneBootTestHelper.WaitUntil(() => remoteHost.Joined.Count == 1, "the client to join once its Gameplay scene is up");
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInGame && !gameFlow.IsTransitioning, "the client to load the world it was placed in");

        Assert.That(session.IsHost, Is.False);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(testlands), "The client follows the host into the world the host put its player in.");
        ChunkId start = ChunkId.Of(testlands, Vector2Int.zero);
        yield return SceneBootTestHelper.WaitUntil(() => remoteHost.Ready.Contains((0, start)), "the client to report its first chunk ready for trip 0");
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        LocalPlayer local = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current;
        Assert.That(local.Controller.GetComponent<NetworkObject>().IsOwner, Is.True);

        remoteHost.HeldAcknowledgements.Add(start);
        local.Controller.Teleport(testlands.ToWorld(new Vector2(100.5f, 100.5f)));
        yield return SceneBootTestHelper.WaitUntil(() => remoteHost.Releasing.Contains(start), "the client to release the chunk it walked away from");
        for (int frame = 0; frame < 20; frame++)
        {
            yield return null;
        }

        Assert.That(worlds.TryGet(testlands, out LoadedWorld loaded) && loaded.Chunks.ContainsKey(Vector2Int.zero), Is.True, "A released chunk stays loaded until the host acknowledges.");
        remoteHost.Acknowledge(remoteHost.LastClientId, start);
        yield return SceneBootTestHelper.WaitUntil(
            () => worlds.TryGet(testlands, out LoadedWorld after) && !after.Chunks.ContainsKey(Vector2Int.zero),
            "the client to unload the chunk once the host acknowledged");
        Assert.That(remoteHost.Ready.Contains((0, ChunkId.Of(testlands, Vector2Int.one))), Is.True, "The chunk it walked into was reported ready.");

        PlayerController hostCopy = remoteHost.PlayerOf(remoteHost.LastClientId);
        remoteHost.PlayerWorlds.Travel(hostCopy.GetComponent<PlayerNetworkSync>().Handle, interior, "start");
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsTravelling, "the host's trip to reach the client");
        yield return SceneBootTestHelper.WaitUntil(() => !gameFlow.IsTransitioning, "the client to arrive");
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(interior));
        Assert.That((Vector2)local.Transform.position, Is.EqualTo(interior.ToWorld(interior.FindEntry("start").Position)), "The client moves its own player to the entry.");
        yield return SceneBootTestHelper.WaitUntil(() => remoteHost.Ready.Contains((1, ChunkId.Of(interior, Vector2Int.zero))), "the client to report the interior's chunk for trip 1");
        yield return SceneBootTestHelper.WaitUntil(
            () => Vector2.Distance(hostCopy.transform.position, local.Transform.position) < 0.01f,
            "the client's teleport to snap the host's copy to the entry");

        remoteHost.Manager.Shutdown();
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInMenu && !gameFlow.IsTransitioning, "the client to return to the menu after the host left");
        Assert.That(session.IsActive, Is.False);
        Assert.That(saveStore.WriteCount, Is.Zero, "A joined client never saves.");
    }

    /// <summary>
    /// When the host leaves while a client is still joining (its player not placed yet), the client stays
    /// non-authoritative, returns to the menu once the join ends, without saving, and is authoritative again for an
    /// offline game that saves as usual.
    /// </summary>
    [UnityTest]
    public IEnumerator HostLeavingWhileAClientJoins_ReturnsTheClientToTheMenu_WithoutSaving()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        remoteHost = StartRemoteHost(session, gameFlow.Scenes);
        remoteHost.SpawnsJoinedPlayers = false;
        bool lostDuringTransition = false;
        int connectionLosses = 0;
        session.ConnectionLost += () =>
        {
            connectionLosses++;
            lostDuringTransition = gameFlow.IsTransitioning;
        };

        Object.FindAnyObjectByType<MainMenuController>().JoinGame();
        yield return SceneBootTestHelper.WaitUntil(() => remoteHost.Joined.Count == 1, "the client to load Gameplay and join");
        Assert.That(gameFlow.IsTransitioning, Is.True, "The client waits for its player behind the fade.");
        Assert.That(session.IsAuthoritative, Is.False);

        remoteHost.Manager.Shutdown();
        yield return SceneBootTestHelper.WaitUntil(() => connectionLosses > 0, "the client to notice the host left");
        Assert.That(lostDuringTransition, Is.True, "The host must leave while the client is still joining for this test to mean anything.");
        Assert.That(session.IsAuthoritative, Is.False, "A client that lost its host must not start deciding game state on its own.");

        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInMenu && !gameFlow.IsTransitioning, "the client to return to the menu after its join");
        Assert.That(gameFlow.IsInGame, Is.False);
        Assert.That(session.IsActive, Is.False);
        Assert.That(saveStore.WriteCount, Is.Zero, "A client that lost its host never saves.");
        Assert.That(session.IsAuthoritative, Is.True, "Back in the menu, an offline game decides its own state again.");

        Object.FindAnyObjectByType<MainMenuController>().StartNewGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.IsInGame, Is.True);
        Assert.That(saveStore.WriteCount, Is.EqualTo(1), "An offline game after the lost session saves as usual.");
    }

    /// <summary>
    /// Host and Join do nothing while the main menu is still transitioning in, and Host works once it has settled.
    /// </summary>
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

    /// <summary>
    /// A join the host refuses fails, exposes the host's reason as the join refusal and shuts the client down.
    /// </summary>
    [UnityTest]
    public IEnumerator Join_ReportsTheHostsRefusal()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        remoteHost = InProcessHost.Start(session, null, null, null, "the game is full (4 players).");
        Awaitable<bool> join = session.JoinAsync("127.0.0.1", 10f, CancellationToken.None);
        yield return SceneBootTestHelper.WaitUntil(() => join.GetAwaiter().IsCompleted, "the refused join to end");

        Assert.That(join.GetAwaiter().GetResult(), Is.False);
        Assert.That(session.JoinRefusal, Is.EqualTo("the game is full (4 players)."));
        Assert.That(remoteHost.Manager.ConnectedClientsIds.Count, Is.EqualTo(1), "Only the host itself is connected.");
        yield return SceneBootTestHelper.WaitUntil(() => !session.IsActive, "the refused client to shut down");
    }

    /// <summary>
    /// The host disconnects a client whose join payload carries another game version, with a refusal naming that
    /// version.
    /// </summary>
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
            outdated.NetworkConfig.ConnectionData = JoinApproval.CreatePayload(Application.version + "-other", "outdated");
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

    /// <summary>
    /// The host keys clients by the player id they join with; a client whose id is already in the session plays as a
    /// new player with an id of its own for the session.
    /// </summary>
    [UnityTest]
    public IEnumerator Host_KeysClientsByTheirPlayerId_AndGivesADuplicateANewOne()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();

        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Assert.That(session.StartHost(), Is.True);
        Assert.That(session.LocalPlayerId, Is.Not.Empty, "The host has a player id of its own.");
        NetworkManager first = InProcessClient.CloneNetworkManager(session, "FirstClient");
        NetworkManager twin = InProcessClient.CloneNetworkManager(session, "TwinClient");
        try
        {
            foreach (NetworkManager client in new[] { first, twin })
            {
                client.GetComponent<UnityTransport>().SetConnectionData("127.0.0.1", session.Settings.Port);
                client.NetworkConfig.ConnectionData = JoinApproval.CreatePayload(Application.version, "same-install");
            }

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("shares the player id same-install"));
            Assert.That(first.StartClient(), Is.True);
            yield return SceneBootTestHelper.WaitUntil(() => first.IsConnectedClient, "the first client to connect");
            Assert.That(twin.StartClient(), Is.True);
            yield return SceneBootTestHelper.WaitUntil(() => twin.IsConnectedClient, "the second client to connect");

            Assert.That(session.PlayerIdOf(first.LocalClientId), Is.EqualTo("same-install"));
            string twinId = session.PlayerIdOf(twin.LocalClientId);
            Assert.That(twinId, Is.Not.Empty.And.Not.EqualTo("same-install"), "A duplicate id is replaced for the session.");
            Assert.That(session.PlayerIdOf(NetworkManager.ServerClientId), Is.EqualTo(session.LocalPlayerId));
        }
        finally
        {
            first.Shutdown();
            twin.Shutdown();
            Object.Destroy(first.gameObject);
            Object.Destroy(twin.gameObject);
            session.Shutdown();
        }
    }

    /// <summary>
    /// A join with no host listening fails after its timeout and shuts the client down.
    /// </summary>
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

    /// <summary>
    /// Starts an in-process host on the session's port that spawns joined clients' players, built from the project's
    /// player prefab and combat settings and <paramref name="gameScenes"/>.
    /// </summary>
    private static InProcessHost StartRemoteHost(NetworkSession session, GameScenes gameScenes)
    {
#if UNITY_EDITOR
        NetworkObject playerPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(PlayerPrefabPath);
        CombatSettings combatSettings = AssetDatabase.LoadAssetAtPath<CombatSettings>(CombatSettingsPath);
        Assert.That(playerPrefab, Is.Not.Null);
        Assert.That(combatSettings, Is.Not.Null);
        return InProcessHost.Start(session, playerPrefab, gameScenes, combatSettings);
#else
        Assert.Ignore("The in-process host loads the player prefab through the AssetDatabase.");
        return null;
#endif
    }

    /// <summary>
    /// The world <see cref="GameScenes"/> lists under <paramref name="worldName"/>; fails the test without one.
    /// </summary>
    private static WorldDefinition FindWorld(GameFlow gameFlow, string worldName)
    {
        foreach (WorldDefinition world in gameFlow.Scenes.Worlds)
        {
            if (world != null && world.name == worldName)
            {
                return world;
            }
        }

        Assert.Fail($"Game Scenes lists no world named {worldName}.");
        return null;
    }
}
