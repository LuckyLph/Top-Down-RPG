using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for seamless worlds online with an in-process client (Docs/WorldStreaming.md: Networking): a joined
/// client sees the players at once and a chunk's objects only once it reports that chunk ready, each arriving with its
/// home chunk; the host keeps a chunk the client holds after every player left it, and hides its objects before
/// acknowledging the client's release, then unloads it; a client's trip goes by RPC while the host stays put, with a
/// report from before the trip ignored; the host's trip leaves the client where it is; a disconnect releases every
/// chunk the client held.
/// </summary>
public class NetworkWorldsPlayModeTests
{
    private const float TripTimeoutSeconds = 20f;

    private InProcessClient client;

    /// <summary>
    /// Disposes the in-process client when the test started one.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        client?.Dispose();
        client = null;
    }

    /// <summary>
    /// Hosting Testlands: a client that joined gets both players, its own in Testlands on both machines and held, but
    /// none of the chunk's mobs; once it reports the chunk ready the mobs arrive with that home chunk and its player is
    /// released. With every player gone from the chunk, the host keeps it while the client holds it; the client's release
    /// hides the mobs from it before the acknowledgement arrives, and the host then unloads the chunk. A disconnect
    /// releases what the client held.
    /// </summary>
    [UnityTest]
    public IEnumerator AChunksObjects_ReachAClientOnlyWhileItHoldsTheChunk_AndTheHostKeepsAHeldChunk()
    {
        yield return HostFromMenu();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        Worlds hostWorlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        PlayerRegistry hostPlayers = SceneBootTestHelper.ResolveFromGameplay<PlayerRegistry>();
        PlayerWorlds hostPlayerWorlds = SceneBootTestHelper.ResolveFromGameplay<PlayerWorlds>();
        PlayerController hostPlayer = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Controller;
        ChunkId start = ChunkId.Of(world, Vector2Int.zero);
        Assert.That(hostWorlds.TryGet(world, out LoadedWorld loaded) && loaded.Chunks.ContainsKey(Vector2Int.zero), Is.True);
        Assert.That(CountMobsManagedBy(session.NetworkManager), Is.GreaterThan(0), "The host's chunk has mobs.");

        client = StartClient(session);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendJoined();
        yield return SceneBootTestHelper.WaitUntil(
            () => client.LocalPlayer.Current != null && client.Players.Players.Count == 2 && hostPlayers.Players.Count == 2,
            "the joined client to receive both players");
        client.DisableColliders();
        ulong clientId = client.Manager.LocalClientId;
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, clientId);
        PlayerHandle clientHandle = client.LocalPlayer.Current.Handle;
        Assert.That(hostPlayerWorlds.Get(HandleOf(hostPlayers, hostCopy)), Is.SameAs(world), "The host put the client's player in the starting world.");
        yield return SceneBootTestHelper.WaitUntil(() => client.Resolve<PlayerWorlds>().Get(clientHandle) == world, "the client to learn its player's world");
        Assert.That(hostCopy.GetComponent<NetworkStatusEffects>().IsHeld, Is.True, "A client with no chunk ready has its player held.");
        yield return Frames(15);
        Assert.That(CountMobsManagedBy(client.Manager), Is.Zero, "No chunk object reaches a client before it holds the chunk.");

        client.SendChunkReady(0, start);
        yield return SceneBootTestHelper.WaitUntil(() => CountMobsManagedBy(client.Manager) > 0, "the chunk's mobs to reach the client");
        Assert.That(MobHomes(), Is.All.EqualTo(start), "Each mob arrives with its home chunk.");
        Assert.That(hostCopy.GetComponent<NetworkStatusEffects>().IsHeld, Is.False, "A client with ground under its player is no longer held.");
        Assert.That(session.IsChunkHeld(start), Is.True);

        Vector2 away = world.ToWorld(new Vector2(100.5f, 100.5f));
        hostPlayer.Teleport(away);
        client.LocalPlayer.Current.Controller.Teleport(away);
        yield return SceneBootTestHelper.WaitUntil(
            () => hostWorlds.TryGet(world, out LoadedWorld now) && now.Chunks.ContainsKey(Vector2Int.one),
            "the host to load the chunk both players moved to");
        yield return Frames(60);
        Assert.That(loaded.Chunks.ContainsKey(Vector2Int.zero), Is.True, "The host keeps a chunk the client holds, though no player is near it.");
        Assert.That(CountHomedIn(start), Is.GreaterThan(0), "The client keeps its copies of the held chunk's objects.");

        int startMobs = CountHomedIn(start);
        Assert.That(startMobs, Is.GreaterThan(0));
        client.SendChunkReleasing(start);
        yield return SceneBootTestHelper.WaitUntil(() => client.Released.Contains(start), "the host to acknowledge the release");
        Assert.That(CountHomedIn(start), Is.Zero, "The chunk's objects were hidden before the acknowledgement arrived.");
        yield return SceneBootTestHelper.WaitUntil(() => !loaded.Chunks.ContainsKey(Vector2Int.zero), "the host to unload the released chunk");

        ChunkId corner = ChunkId.Of(world, Vector2Int.one);
        client.SendChunkReady(0, corner);
        yield return SceneBootTestHelper.WaitUntil(() => session.IsChunkHeld(corner), "the client to hold the chunk it stands in");
        client.Manager.Shutdown();
        yield return SceneBootTestHelper.WaitUntil(() => hostPlayers.Players.Count == 1, "the host to drop the disconnected client's player");
        Assert.That(session.IsChunkHeld(corner), Is.False, "A disconnect releases what the client held.");
        Assert.That(session.Readiness.IsJoined(clientId), Is.False);
    }

    /// <summary>
    /// Hosting the Sandbox, the client's player steps onto the exit and asks for the trip: the request goes by RPC,
    /// with the client's own position, and the host approves it without fading or moving its own player, puts the
    /// client's player in the interior for its next trip and loads the interior for it. A chunk report from before the
    /// trip is ignored; one for the trip shows the interior's objects and releases the player.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsTrip_GoesByRpc_WhileTheHostStaysPut()
    {
        yield return HostSandboxWithClient();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        WorldDefinition sandbox = gameFlow.CurrentWorld;
        WorldDefinition interior = FindWorld(gameFlow, "SandboxInterior");
        PlayerRegistry hostPlayers = SceneBootTestHelper.ResolveFromGameplay<PlayerRegistry>();
        PlayerWorlds hostPlayerWorlds = SceneBootTestHelper.ResolveFromGameplay<PlayerWorlds>();
        Worlds hostWorlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        PlayerController hostPlayer = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Controller;
        Vector2 hostPlayerAt = hostPlayer.transform.position;
        ulong clientId = client.Manager.LocalClientId;
        PlayerHandle hostCopy = HandleOf(hostPlayers, InProcessClient.FindCopyOwnedBy(hostPlayers, clientId));
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<WorldExits>().TryFindAt(sandbox, new Vector2Int(36, 41), out WorldExit exit), Is.True);

        LocalPlayer clientLocal = client.LocalPlayer.Current;
        clientLocal.Controller.Teleport(sandbox.ToWorld(new Vector2(36.5f, 41.5f)));
        NetworkTripAuthority clientTrips = client.Resolve<NetworkTripAuthority>();
        List<TripApproval> delivered = new();
        clientTrips.Approved += delivered.Add;
        clientTrips.Request(new TripRequest(clientLocal.Handle, exit.Id));

        yield return SceneBootTestHelper.WaitUntil(() => delivered.Count == 1, "the host's approval to reach the client");
        Assert.That(delivered[0].World, Is.SameAs(interior));
        Assert.That(delivered[0].EntryName, Is.EqualTo("start"));
        Assert.That(delivered[0].Position, Is.EqualTo(interior.ToWorld(interior.FindEntry("start").Position)));
        Assert.That(client.Resolve<PlayerWorlds>().TripOf(clientLocal.Handle), Is.EqualTo(2));
        Assert.That(client.Resolve<TripArrivals>().IsHeld(clientLocal.Handle), Is.True, "The client streams its arrival.");
        Assert.That(hostPlayerWorlds.Get(hostCopy), Is.SameAs(interior), "The host put the client's player in the interior.");
        Assert.That(hostPlayerWorlds.TripOf(hostCopy), Is.EqualTo(2));
        Assert.That(session.Readiness.TripOf(clientId), Is.EqualTo(2));
        Assert.That(session.IsClientReady(clientId), Is.False, "A travelling client's player is held until it has ground again.");
        Assert.That(gameFlow.IsTransitioning, Is.False, "The host does not fade for a client's trip.");
        Assert.That(hostPlayerWorlds.Get(HandleOf(hostPlayers, hostPlayer)), Is.SameAs(sandbox));
        Assert.That((Vector2)hostPlayer.transform.position, Is.EqualTo(hostPlayerAt), "The host's own player stays put.");
        yield return SceneBootTestHelper.WaitUntil(
            () => hostWorlds.TryGet(interior, out LoadedWorld loadedInterior) && loadedInterior.Chunks.ContainsKey(Vector2Int.zero),
            "the host to load the interior for the client's player");

        ChunkId interiorChunk = ChunkId.Of(interior, Vector2Int.zero);
        client.SendChunkReady(1, interiorChunk);
        yield return Frames(15);
        Assert.That(session.IsClientReady(clientId, interiorChunk), Is.False, "A report from before the trip is ignored.");
        Assert.That(CountHomedIn(interiorChunk), Is.Zero);

        clientLocal.Controller.Teleport(delivered[0].Position);
        client.SendChunkReady(2, interiorChunk);
        yield return SceneBootTestHelper.WaitUntil(() => CountHomedIn(interiorChunk) > 0, "the interior's objects to reach the client after its report for the trip");
        Assert.That(session.IsClientReady(clientId), Is.True);
        Assert.That(hostWorlds.TryGet(sandbox, out _), Is.True, "The host keeps simulating its own world.");
        yield return SceneBootTestHelper.WaitUntil(
            () => !SceneBootTestHelper.ResolveFromGameplay<TripArrivals>().IsHeld(hostCopy),
            "the host to see the client's player arrive at the entry");
    }

    /// <summary>
    /// Hosting the Sandbox with a client beside the host, the host walks into the exit: it travels to the interior while
    /// the client stays, keeping its copies of the Sandbox's mobs; the client sees the host's player in the interior,
    /// snapped to its entry, and the host keeps the Sandbox loaded for the client.
    /// </summary>
    [UnityTest]
    public IEnumerator TheHostsTrip_LeavesTheClientWhereItIs()
    {
        yield return HostSandboxWithClient();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        WorldDefinition sandbox = gameFlow.CurrentWorld;
        WorldDefinition interior = FindWorld(gameFlow, "SandboxInterior");
        Worlds hostWorlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        PlayerController hostPlayer = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Controller;
        int clientMobs = CountMobsManagedBy(client.Manager);
        Assert.That(clientMobs, Is.GreaterThan(0));
        PlayerController hostOnClient = InProcessClient.FindCopyOwnedBy(client.Players, NetworkManager.ServerClientId);
        PlayerHandle hostOnClientHandle = HandleOf(client.Players, hostOnClient);
        Vector2 clientAt = client.LocalPlayer.Current.Transform.position;

        hostPlayer.Teleport(sandbox.ToWorld(new Vector2(34.5f, 42f)));
        yield return null;
        Assert.That(hostPlayer.GetComponent<PlayerMotor2D>().MoveTo(sandbox.ToWorld(new Vector2(38.5f, 42f))), Is.True);
        yield return SceneBootTestHelper.WaitUntil(() => gameFlow.IsTravelling, "the host to step into the exit");
        float deadline = Time.realtimeSinceStartup + TripTimeoutSeconds;
        while (gameFlow.IsTransitioning)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The host's trip should end.");
            yield return null;
        }

        Assert.That(gameFlow.CurrentWorld, Is.SameAs(interior));
        Vector2 entry = interior.ToWorld(interior.FindEntry("start").Position);
        yield return SceneBootTestHelper.WaitUntil(() => client.Resolve<PlayerWorlds>().Get(hostOnClientHandle) == interior, "the client to see the host's player in the interior");
        Assert.That(client.Resolve<PlayerWorlds>().TripOf(hostOnClientHandle), Is.EqualTo(1));
        yield return SceneBootTestHelper.WaitUntil(
            () => Vector2.Distance(hostOnClient.transform.position, entry) < 0.01f,
            "the host's teleport to snap its copy on the client to the entry");
        Assert.That(Vector2.Distance(client.LocalPlayer.Current.Transform.position, clientAt), Is.LessThan(0.01f), "The client stays where it is.");
        Assert.That(client.Resolve<PlayerWorlds>().Get(client.LocalPlayer.Current.Handle), Is.SameAs(sandbox));
        Assert.That(CountMobsManagedBy(client.Manager), Is.EqualTo(clientMobs), "The client keeps its copies of the Sandbox's mobs.");
        Assert.That(hostWorlds.TryGet(sandbox, out LoadedWorld kept) && kept.Chunks.ContainsKey(Vector2Int.zero), Is.True, "The host keeps the client's world loaded.");
    }

    /// <summary>
    /// Boots to the menu and hosts the starting world from it.
    /// </summary>
    private IEnumerator HostFromMenu()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return null;
        Assert.That(gameFlow.IsInGame, Is.True);
    }

    /// <summary>
    /// Hosts a session in the Sandbox with an in-process client: the client joins and starts in the starting world, as
    /// a player the host does not know does, and the host then sends it to the Sandbox's entry on its first trip, where
    /// it arrives and reports the chunk ready for that trip, so it holds the Sandbox's chunk with both players seen on
    /// both sides and its colliders off.
    /// </summary>
    private IEnumerator HostSandboxWithClient()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        WorldDefinition sandbox = FindWorld(gameFlow, "Sandbox");
        Assert.That(session.StartHost(), Is.True);
        gameFlow.StartWorldGameAsync(sandbox, "start");
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(sandbox));

        PlayerRegistry hostPlayers = SceneBootTestHelper.ResolveFromGameplay<PlayerRegistry>();
        client = StartClient(session);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendJoined();
        yield return SceneBootTestHelper.WaitUntil(
            () => client.LocalPlayer.Current != null && client.Players.Players.Count == 2 && hostPlayers.Players.Count == 2,
            "both sides to see both players");
        client.DisableColliders();
        PlayerHandle clientHandle = client.LocalPlayer.Current.Handle;
        PlayerWorlds clientWorlds = client.Resolve<PlayerWorlds>();
        yield return SceneBootTestHelper.WaitUntil(() => clientWorlds.Get(clientHandle) == gameFlow.Scenes.StartingWorld, "the client to start in the starting world");

        PlayerHandle hostCopy = HandleOf(hostPlayers, InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId));
        SceneBootTestHelper.ResolveFromGameplay<PlayerWorlds>().Travel(hostCopy, sandbox, "start");
        yield return SceneBootTestHelper.WaitUntil(() => clientWorlds.Get(clientHandle) == sandbox && clientWorlds.TripOf(clientHandle) == 1, "the host's trip to reach the client");
        client.LocalPlayer.Current.Controller.Teleport(sandbox.ToWorld(sandbox.FindEntry("start").Position));
        client.SendChunkReady(1, ChunkId.Of(sandbox, Vector2Int.zero));
        yield return SceneBootTestHelper.WaitUntil(() => CountMobsManagedBy(client.Manager) > 0, "the client to receive the Sandbox's mobs");
    }

    /// <summary>
    /// Starts an in-process client against <paramref name="session"/> with the host's player prefab.
    /// </summary>
    private static InProcessClient StartClient(NetworkSession session)
    {
        return InProcessClient.Start(session, SceneBootTestHelper.ResolveFromGameplay<PlayerSpawner>().PlayerNetworkPrefab);
    }

    /// <summary>
    /// The handle in <paramref name="players"/> of <paramref name="player"/>, or null.
    /// </summary>
    private static PlayerHandle HandleOf(IPlayerRegistry players, PlayerController player)
    {
        foreach (PlayerHandle handle in players.Players)
        {
            if (player != null && handle.Transform == player.transform)
            {
                return handle;
            }
        }

        return null;
    }

    /// <summary>
    /// How many spawned mobs belong to <paramref name="manager"/> (host or client side).
    /// </summary>
    private static int CountMobsManagedBy(NetworkManager manager)
    {
        int count = 0;
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            NetworkObject networkObject = mob.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned && networkObject.NetworkManager == manager)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The home chunk each of the client's mob copies arrived with.
    /// </summary>
    private List<ChunkId> MobHomes()
    {
        List<ChunkId> homes = new();
        foreach (KeyValuePair<NetworkObject, ChunkId> copy in client.HomeChunks)
        {
            if (copy.Key != null && copy.Key.TryGetComponent(out MobController _))
            {
                homes.Add(copy.Value);
            }
        }

        return homes;
    }

    /// <summary>
    /// How many of the client's spawned chunk copies (mobs, terrain sync) have <paramref name="chunk"/> as their home.
    /// </summary>
    private int CountHomedIn(ChunkId chunk)
    {
        int count = 0;
        foreach (KeyValuePair<NetworkObject, ChunkId> copy in client.HomeChunks)
        {
            if (copy.Key != null && copy.Key.IsSpawned && copy.Value.Equals(chunk))
            {
                count++;
            }
        }

        return count;
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

    /// <summary>
    /// Yields <paramref name="count"/> frames.
    /// </summary>
    private static IEnumerator Frames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return null;
        }
    }
}
