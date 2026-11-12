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
/// a trip the host sends (also while a chunk's release waits for the host), losing the host mid-join and mid-trip, a
/// client's copy of its place in the host's session carrying it on alone once the host is gone, join refusals, version
/// checks and join timeouts.
/// </summary>
public class NetworkSessionPlayModeTests
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";
    private const string CombatSettingsPath = "Assets/Data/Combat/CombatSettings.asset";
    private const string GameplaySettingsPath = "Assets/Data/GameplaySettings.asset";

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
    /// Hosting from the main menu without a save starts a session in the starting world at its entry, saves the host's
    /// place as its player enters, and shuts down when the game returns to the menu.
    /// </summary>
    [UnityTest]
    public IEnumerator HostFromMenu_EntersTheStartingWorld_AndReturningToTheMenuEndsTheSession()
    {
        MemorySaveFolder saveStore = new();
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
        yield return SceneBootTestHelper.WaitUntil(() => saveStore.WriteCount > 0, "the host to save as its player enters the world");
        Assert.That(GameSaveData.TryParse(saveStore.Contents, out GameSaveData data), Is.True);
        Assert.That(data.Players, Has.Count.EqualTo(1));
        Assert.That(data.Players[0].PlayerId, Is.EqualTo(session.LocalPlayerId));
        Assert.That(data.Players[0].WorldId, Is.EqualTo(world.WorldId));

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return SceneBootTestHelper.WaitUntil(() => !session.IsActive, "the session to shut down in the menu");
    }

    /// <summary>
    /// A client joining from the menu sends its player id, loads Gameplay and joins; the host spawns its player in a
    /// world, and the client loads that world around it and reports the chunk ready for trip 0. Walking its player into
    /// another chunk, it releases each chunk it leaves and keeps the chunk loaded until the host acknowledges. A trip
    /// the host sends takes it into the interior, where it reports the new chunk for trip 1; it returns to the menu
    /// when the host leaves, having saved nothing but its copy of its place in the host's session.
    /// </summary>
    [UnityTest]
    public IEnumerator JoinFromMenu_LoadsTheHostAssignedWorld_ReportsAndReleasesChunks_AndTravelsWhenTheHostSaysSo()
    {
        MemorySaveFolder saveStore = new();
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
        Assert.That(saveStore.Names(), Is.EqualTo(new[] { GameSave.CopyNameOf(remoteHost.SessionId) }), "A joined client saves no session of its own, only its copy of its place in the host's.");
    }

    /// <summary>
    /// A joined client keeps a copy of its own place in the host's session (Docs/KnownGaps.md, Phase 5), named by the
    /// session id the host tells it, holding its player's world, place and weapon and no world's state, and saying the
    /// host is still there; when the host is gone, the client returns to the menu, whose dialog says so (Docs/UiRevamp.md,
    /// Phase 9) and offers Continue alone, which carries on alone, offline, from where the player stood, holding its
    /// weapon, as a session of its own saved beside the copy.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientWhoseHostIsGone_CarriesOnAlone_FromItsCopy()
    {
        MemorySaveFolder saves = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saves);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        WorldDefinition testlands = gameFlow.Scenes.StartingWorld;
        remoteHost = StartRemoteHost(session, gameFlow.Scenes);
        remoteHost.SpawnWorld = testlands;
        remoteHost.SpawnPosition = testlands.ToWorld(testlands.FindEntry("start").Position);
        string copyName = GameSave.CopyNameOf(remoteHost.SessionId);

        Object.FindAnyObjectByType<MainMenuController>().JoinGame();
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInGame && !gameFlow.IsTransitioning, "the client to load the world it was placed in");
        yield return SceneBootTestHelper.WaitUntil(() => saves.Read(copyName) != null, "the client to copy its place as its player enters the world");
        Assert.That(GameSaveData.TryParse(saves.Read(copyName), out GameSaveData entered), Is.True);
        Assert.That(entered.IsCopy && !entered.HostGone, Is.True, "a copy whose host is still there");
        Assert.That(entered.Players, Has.Count.EqualTo(1), "the copy holds its own player alone");
        Assert.That(entered.Players[0].PlayerId, Is.EqualTo(session.LocalPlayerId));
        Assert.That(entered.Worlds, Is.Empty, "no world's state, which is the host's");
        Assert.That(SceneBootTestHelper.ResolveFromMain<GameSave>().HasSave, Is.False, "while the host may be there, the copy is no save to continue");

        LocalPlayer local = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current;
        Assert.That(local.Weapon.EquipFromCatalog(2), Is.True);
        string weaponId = local.Weapon.CurrentWeapon.WeaponId;
        Vector2 stood = testlands.ToWorld(new Vector2(36.5f, 40.5f));
        local.Controller.Teleport(stood);
        for (int frame = 0; frame < 5; frame++)
        {
            yield return null;
        }

        remoteHost.Manager.Shutdown();
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInMenu && !gameFlow.IsTransitioning, "the client to return to the menu after the host left");
        Assert.That(GameSaveData.TryParse(saves.Read(copyName), out GameSaveData left), Is.True);
        Assert.That(left.HostGone, Is.True, "the copy says the host is gone");
        Assert.That(left.Players[0].Position, Is.EqualTo(stood), "where the player stood as the host went");
        Assert.That(left.Players[0].Weapon, Is.EqualTo(weaponId));
        MainMenuController menu = null;
        yield return SceneBootTestHelper.WaitUntil(() => (menu = Object.FindAnyObjectByType<MainMenuController>()) != null && menu.Dialog.IsShown, "the menu's host-gone dialog");
        Assert.That(menu.Dialog.Message, Is.EqualTo(DialogNoticeView.BySentence(MainMenuController.HostGoneMessage)), "the menu says the host is gone");
        Assert.That(menu.ContinueButton.gameObject.activeSelf, Is.True, "the copy is a save Continue loads");
        Assert.That(menu.Dialog.PrimaryButton.gameObject.activeSelf && menu.Dialog.SecondaryButton.gameObject.activeSelf, Is.True, "Continue alone and Back");

        menu.Dialog.PrimaryButton.onClick.Invoke();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.IsInGame, Is.True);
        Assert.That(session.IsActive, Is.False, "the game goes on offline");
        Assert.That(session.IsAuthoritative, Is.True);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(testlands));
        LocalPlayer alone = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current;
        Assert.That((Vector2)alone.Transform.position, Is.EqualTo(stood), "back on its own where it stood");
        Assert.That(alone.Weapon.CurrentWeapon.WeaponId, Is.EqualTo(weaponId), "holding its weapon");
        SessionSave sessionSave = SceneBootTestHelper.ResolveFromMain<SessionSave>();
        yield return SceneBootTestHelper.WaitUntil(() => saves.Read(GameSave.SessionPrefix + sessionSave.Id + FileSaveFolder.Extension) != null, "the session going on alone to save as its player enters the world");
        Assert.That(saves.Names(), Has.Count.EqualTo(2), "the copy is kept beside the session of its own");
        Assert.That(saves.Read(copyName), Is.Not.Null);
    }

    /// <summary>
    /// A trip reaching the client while it waits for the host to acknowledge the release of a chunk (a trip back into a
    /// world as its grace runs out) does not report that chunk ready again, nor lands the player on it: the chunk goes
    /// once the host acknowledges, then loads again and is reported for the new trip, and only then does the client
    /// arrive.
    /// </summary>
    [UnityTest]
    public IEnumerator ATripDuringAChunksRelease_ReportsTheChunkOnlyOnceItHasLoadedAgain()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu(new MemorySaveFolder());
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        WorldDefinition testlands = gameFlow.Scenes.StartingWorld;
        Vector2 entry = testlands.ToWorld(testlands.FindEntry("start").Position);
        remoteHost = StartRemoteHost(session, gameFlow.Scenes);
        remoteHost.SpawnWorld = testlands;
        remoteHost.SpawnPosition = entry;
        Object.FindAnyObjectByType<MainMenuController>().JoinGame();
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInGame && !gameFlow.IsTransitioning, "the client to load the world it was placed in");
        ChunkId start = ChunkId.Of(testlands, Vector2Int.zero);
        yield return SceneBootTestHelper.WaitUntil(() => remoteHost.Ready.Contains((0, start)), "the client to report its first chunk ready for trip 0");
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        LocalPlayer local = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current;

        remoteHost.HeldAcknowledgements.Add(start);
        local.Controller.Teleport(testlands.ToWorld(new Vector2(100.5f, 100.5f)));
        yield return SceneBootTestHelper.WaitUntil(() => remoteHost.Releasing.Contains(start), "the client to release the chunk it walked away from");
        PlayerController hostCopy = remoteHost.PlayerOf(remoteHost.LastClientId);
        remoteHost.PlayerWorlds.Travel(hostCopy.GetComponent<PlayerNetworkSync>().Handle, testlands, "start");
        yield return SceneBootTestHelper.WaitUntil(() => Vector2.Distance(local.Transform.position, entry) < 1f, "the client to bring its player to the entry");
        for (int frame = 0; frame < 30; frame++)
        {
            yield return null;
        }

        Assert.That(remoteHost.Ready.Contains((1, start)), Is.False, "The chunk being released is not reported for the trip.");
        Assert.That(gameFlow.IsTransitioning, Is.True, "The client waits for the ground at the entry to load again.");

        remoteHost.Acknowledge(remoteHost.LastClientId, start);
        yield return SceneBootTestHelper.WaitUntil(() => remoteHost.Ready.Contains((1, start)), "the chunk to load again and be reported for the trip");
        yield return SceneBootTestHelper.WaitUntil(() => !gameFlow.IsTransitioning, "the client to arrive");
        Assert.That(worlds.TryGet(testlands, out LoadedWorld loaded) && loaded.Chunks.ContainsKey(Vector2Int.zero), Is.True, "The client stands on its loaded chunk.");

        remoteHost.Manager.Shutdown();
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsInMenu && !gameFlow.IsTransitioning, "the client to return to the menu after the host left");
    }

    /// <summary>
    /// When the host leaves while a client is still joining (its player not placed yet), the client stays
    /// non-authoritative, returns to the menu once the join ends, without saving, where a dialog says the connection was
    /// lost while joining the host's address (Docs/UiRevamp.md, Phase 9), and is authoritative again for an offline game
    /// that saves as usual.
    /// </summary>
    [UnityTest]
    public IEnumerator HostLeavingWhileAClientJoins_ReturnsTheClientToTheMenu_WithoutSaving()
    {
        MemorySaveFolder saveStore = new();
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
        Assert.That(saveStore.WriteCount, Is.Zero, "A client that lost its host before its player stood in a world saves nothing, no copy either.");
        Assert.That(session.IsAuthoritative, Is.True, "Back in the menu, an offline game decides its own state again.");
        MainMenuController menu = null;
        yield return SceneBootTestHelper.WaitUntil(() => (menu = Object.FindAnyObjectByType<MainMenuController>()) != null && menu.Dialog.IsShown, "the menu's lost-while-joining dialog");
        Assert.That(menu.Dialog.Message, Is.EqualTo(string.Format(MainMenuController.LostWhileJoiningFormat, $"127.0.0.1:{session.Settings.Port}")));
        menu.Dialog.SecondaryButton.onClick.Invoke();
        Assert.That(menu.Dialog.IsShown, Is.False, "Back closes the dialog.");

        Object.FindAnyObjectByType<MainMenuController>().StartNewGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.IsInGame, Is.True);
        yield return SceneBootTestHelper.WaitUntil(() => saveStore.WriteCount > 0, "the offline game to save as its player enters the world");
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
        remoteHost = InProcessHost.Start(session, null, null, null, null, JoinApproval.Full(4));
        Awaitable<bool> join = session.JoinAsync("127.0.0.1", session.Settings.Port, 10f, CancellationToken.None);
        yield return SceneBootTestHelper.WaitUntil(() => join.GetAwaiter().IsCompleted, "the refused join to end");

        Assert.That(join.GetAwaiter().GetResult(), Is.False);
        Assert.That(session.JoinRefusal, Is.EqualTo(JoinApproval.Full(4)));
        Assert.That(session.JoinedAddress, Is.EqualTo($"127.0.0.1:{session.Settings.Port}"));
        Assert.That(remoteHost.Manager.ConnectedClientsIds.Count, Is.EqualTo(1), "Only the host itself is connected.");
        yield return SceneBootTestHelper.WaitUntil(() => !session.IsActive, "the refused client to shut down");
    }

    /// <summary>
    /// The host disconnects a client whose join payload carries another game version, with a refusal naming the host's
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
            Assert.That(JoinApproval.IsOtherBuild(refusal, out string hostVersion), Is.True, refusal);
            Assert.That(hostVersion, Is.EqualTo(Application.version));
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
        Awaitable<bool> join = session.JoinAsync("127.0.0.1", session.Settings.Port, 1f, CancellationToken.None);
        yield return SceneBootTestHelper.WaitUntil(() => join.GetAwaiter().IsCompleted, "the join attempt to time out");

        Assert.That(join.GetAwaiter().GetResult(), Is.False);
        yield return SceneBootTestHelper.WaitUntil(() => !session.IsActive, "the failed client to shut down");
    }

    /// <summary>
    /// Starts an in-process host on the session's port that spawns joined clients' players, built from the project's
    /// player prefab, combat and gameplay settings and <paramref name="gameScenes"/>.
    /// </summary>
    private static InProcessHost StartRemoteHost(NetworkSession session, GameScenes gameScenes)
    {
#if UNITY_EDITOR
        NetworkObject playerPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(PlayerPrefabPath);
        CombatSettings combatSettings = AssetDatabase.LoadAssetAtPath<CombatSettings>(CombatSettingsPath);
        GameplaySettings gameplaySettings = AssetDatabase.LoadAssetAtPath<GameplaySettings>(GameplaySettingsPath);
        Assert.That(playerPrefab, Is.Not.Null);
        Assert.That(combatSettings, Is.Not.Null);
        Assert.That(gameplaySettings, Is.Not.Null);
        return InProcessHost.Start(session, playerPrefab, gameScenes, combatSettings, gameplaySettings);
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
