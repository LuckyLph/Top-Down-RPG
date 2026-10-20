using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for the world save from the main menu (Docs/WorldStreaming.md: Persistence): New Game enters the
/// starting world and saves the local player's place; without a usable save Continue is off; Continue and Host Game
/// start from the save, the local player at its saved world and position; a player the host knows by its player id
/// joins at its saved place, once the host has loaded that world, and its place is kept as it leaves.
/// </summary>
public class GameSavePlayModeTests
{
    private static readonly Vector2 SavedSandboxPlace = new(28.5f, 41.5f);

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
    /// New Game starts the starting world at its entry and, as the player enters it, writes a version 2 save with this
    /// install's player in that world at that entry.
    /// </summary>
    [UnityTest]
    public IEnumerator NewGame_EntersTheStartingWorld_AndSavesThePlayersPlace()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        WorldDefinition world = gameFlow.Scenes.StartingWorld;

        Object.FindAnyObjectByType<MainMenuController>().StartNewGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(world), "New Game plays the starting world.");
        yield return SceneBootTestHelper.WaitUntil(() => saveStore.Contents != null, "entering the world to write a save");

        Assert.That(GameSaveData.TryParse(saveStore.Contents, out GameSaveData data), Is.True);
        Assert.That(data.Version, Is.EqualTo(2));
        GameSaveData.Player player = FindPlayer(data, SceneBootTestHelper.ResolveFromMain<NetworkSession>().LocalPlayerId);
        Assert.That(player, Is.Not.Null, "The save keys the local player by this install's player id.");
        Assert.That(player.WorldId, Is.EqualTo(world.WorldId));
        Assert.That(player.Position, Is.EqualTo(world.EntryPosition(gameFlow.Scenes.StartingWorldEntry)));
        Assert.That(player.Waystones, Is.Empty);
    }

    /// <summary>
    /// With an empty save store, or a version 1 save (which held an area), the main menu's Continue button is disabled
    /// and not selected.
    /// </summary>
    [UnityTest]
    public IEnumerator MainMenu_WithoutAUsableSave_DisablesContinue()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu(new MemorySaveStore());
        yield return AssertContinueDisabled();

        LogAssert.Expect(LogType.Warning, new Regex("format version 1"));
        yield return SceneBootTestHelper.BootIntoMainMenu(new MemorySaveStore { Contents = "{\"version\":1,\"areaSceneGuid\":\"0\",\"spawnId\":\"start\"}" });
        yield return AssertContinueDisabled();
    }

    /// <summary>
    /// Continue, offline, puts the local player in its saved world at its saved position, continues the save's world
    /// clock and brings back its worlds' states.
    /// </summary>
    [UnityTest]
    public IEnumerator Continue_LandsInTheSavedWorldAndPosition()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        WorldDefinition sandbox = FindWorld(gameFlow, "Sandbox");
        string localId = SceneBootTestHelper.ResolveFromMain<NetworkSession>().LocalPlayerId;
        saveStore.Contents = SaveWith(sandbox, localId, sandbox.ToWorld(SavedSandboxPlace)).ToJson();

        Object.FindAnyObjectByType<MainMenuController>().ContinueGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(SceneBootTestHelper.ResolveFromMain<NetworkSession>().IsActive, Is.False, "Continue plays offline.");
        AssertLocalPlayerAt(gameFlow, sandbox, sandbox.ToWorld(SavedSandboxPlace));
        SessionSave session = SceneBootTestHelper.ResolveFromMain<SessionSave>();
        Assert.That(session.Time, Is.GreaterThanOrEqualTo(1000d), "The world clock continues from the saved time.");
        Assert.That(session.TryGetWorld(sandbox.WorldId, out WorldState state) && state.TryGetChunk(Vector2Int.one, out ChunkState chunk) && chunk.TryGetMobDeath("saved-mob", out _), Is.True, "The saved world state is back.");
    }

    /// <summary>
    /// Host Game with a save hosts from it: the host's player starts at its saved world and position, and the save it
    /// writes keeps it there; leaving to the menu ends the session.
    /// </summary>
    [UnityTest]
    public IEnumerator HostGame_LandsInTheSavedWorldAndPosition()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        WorldDefinition sandbox = FindWorld(gameFlow, "Sandbox");
        saveStore.Contents = SaveWith(sandbox, session.LocalPlayerId, sandbox.ToWorld(SavedSandboxPlace)).ToJson();
        int writesBefore = saveStore.WriteCount;

        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        Assert.That(session.IsHost, Is.True);
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        AssertLocalPlayerAt(gameFlow, sandbox, sandbox.ToWorld(SavedSandboxPlace));
        yield return SceneBootTestHelper.WaitUntil(() => saveStore.WriteCount > writesBefore, "the host to save as its player enters the world");
        Assert.That(GameSaveData.TryParse(saveStore.Contents, out GameSaveData data), Is.True);
        Assert.That(FindPlayer(data, session.LocalPlayerId).Position, Is.EqualTo(sandbox.ToWorld(SavedSandboxPlace)));
        Assert.That(data.Time, Is.GreaterThanOrEqualTo(1000d));

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return SceneBootTestHelper.WaitUntil(() => !session.IsActive, "the session to shut down in the menu");
    }

    /// <summary>
    /// Hosting the starting world with a save that knows a client's player id in the Sandbox: when that client joins,
    /// the host loads the Sandbox around its saved place before spawning its player there, puts it in the Sandbox on
    /// both machines and saves its place; when it leaves after walking, its new place is kept for its next join.
    /// </summary>
    [UnityTest]
    public IEnumerator AKnownPlayerJoining_SpawnsAtItsSavedPlace_OnceTheHostLoadedIt()
    {
        const string KnownId = "known-client";
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        WorldDefinition sandbox = FindWorld(gameFlow, "Sandbox");
        Vector2 saved = sandbox.ToWorld(SavedSandboxPlace);
        saveStore.Contents = SaveWith(sandbox, KnownId, saved).ToJson();

        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(gameFlow.Scenes.StartingWorld), "The host, unknown to the save, starts at the starting entry.");
        Worlds hostWorlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        Assert.That(hostWorlds.TryGet(sandbox, out _), Is.False, "Nobody is in the Sandbox yet.");
        PlayerRegistry hostPlayers = SceneBootTestHelper.ResolveFromGameplay<PlayerRegistry>();
        PlayerWorlds hostPlayerWorlds = SceneBootTestHelper.ResolveFromGameplay<PlayerWorlds>();
        bool groundLoadedAtSpawn = false;
        void RecordGround(PlayerHandle _) => groundLoadedAtSpawn = hostWorlds.TryGet(sandbox, out LoadedWorld world) && world.Chunks.ContainsKey(Vector2Int.zero);
        hostPlayers.PlayerAdded += RecordGround;

        try
        {
            client = InProcessClient.Start(session, SceneBootTestHelper.ResolveFromGameplay<PlayerSpawner>().PlayerNetworkPrefab, KnownId);
            yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
            client.SendJoined();
            yield return SceneBootTestHelper.WaitUntil(
                () => client.LocalPlayer.Current != null && hostPlayers.Players.Count == 2,
                "the host to spawn the known player");
        }
        finally
        {
            hostPlayers.PlayerAdded -= RecordGround;
        }

        client.DisableColliders();
        Assert.That(session.PlayerIdOf(client.Manager.LocalClientId), Is.EqualTo(KnownId));
        Assert.That(groundLoadedAtSpawn, Is.True, "The host loads the saved world around the place before spawning the player there.");
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        Assert.That(Vector2.Distance(hostCopy.transform.position, saved), Is.LessThan(0.01f), "The known player spawns at its saved place.");
        Assert.That(hostPlayerWorlds.Get(hostCopy.GetComponent<PlayerNetworkSync>().Handle), Is.SameAs(sandbox));
        PlayerHandle clientHandle = client.LocalPlayer.Current.Handle;
        yield return SceneBootTestHelper.WaitUntil(() => client.Resolve<PlayerWorlds>().Get(clientHandle) == sandbox, "the client to learn its player is in the Sandbox");
        yield return SceneBootTestHelper.WaitUntil(
            () => GameSaveData.TryParse(saveStore.Contents, out GameSaveData data) && FindPlayer(data, KnownId) != null && FindPlayer(data, session.LocalPlayerId) != null,
            "the host to save as the known player enters its world");

        Vector2 walked = sandbox.ToWorld(new Vector2(40.5f, 41.5f));
        client.LocalPlayer.Current.Controller.Teleport(walked);
        yield return SceneBootTestHelper.WaitUntil(() => Vector2.Distance(hostCopy.transform.position, walked) < 0.01f, "the client's move to reach the host");
        client.Manager.Shutdown();
        yield return SceneBootTestHelper.WaitUntil(() => hostPlayers.Players.Count == 1, "the host to drop the client's player");

        Assert.That(SceneBootTestHelper.ResolveFromMain<SessionSave>().TryGetPlace(KnownId, out string worldId, out Vector2 kept), Is.True);
        Assert.That(worldId, Is.EqualTo(sandbox.WorldId));
        Assert.That(kept, Is.EqualTo(walked), "A leaving player's place is kept for its next join.");
    }

    /// <summary>
    /// Asserts the main menu's Continue button is disabled and not selected, a frame after the menu started.
    /// </summary>
    private static IEnumerator AssertContinueDisabled()
    {
        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        yield return null;

        Assert.That(menu.ContinueButton, Is.Not.Null, "The menu should have a Continue button assigned.");
        Assert.That(menu.ContinueButton.interactable, Is.False);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.SameAs(menu.ContinueButton.gameObject));
    }

    /// <summary>
    /// Asserts the session runs in <paramref name="world"/> with the local player there at <paramref name="position"/>.
    /// </summary>
    private static void AssertLocalPlayerAt(GameFlow gameFlow, WorldDefinition world, Vector2 position)
    {
        Assert.That(gameFlow.IsInGame, Is.True);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(world));
        LocalPlayer local = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current;
        Assert.That((Vector2)local.Transform.position, Is.EqualTo(position));
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<PlayerWorlds>().Get(local.Handle), Is.SameAs(world));
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        Assert.That(worlds.TryGet(world, out LoadedWorld loaded) && loaded.Chunks.ContainsKey(ChunkCoords.ChunkOf(ChunkCoords.CellOf(world.ToLocal(position)))), Is.True, "The ground under the saved place is loaded.");
    }

    /// <summary>
    /// A version 2 save at 1000 s of game time holding <paramref name="playerId"/> at <paramref name="position"/> in
    /// <paramref name="world"/>, and a killed mob in that world's chunk (1, 1).
    /// </summary>
    private static GameSaveData SaveWith(WorldDefinition world, string playerId, Vector2 position)
    {
        return new GameSaveData(
            1000d,
            new[] { new GameSaveData.Player(playerId, world.WorldId, position) },
            new[]
            {
                new GameSaveData.World(world.WorldId, new[]
                {
                    new GameSaveData.Chunk(Vector2Int.one, new[] { new GameSaveData.MobDeath("saved-mob", 990d) }, new GameSaveData.CellEdit[0]),
                }),
            });
    }

    /// <summary>
    /// The record of <paramref name="playerId"/> in <paramref name="data"/>, or null.
    /// </summary>
    private static GameSaveData.Player FindPlayer(GameSaveData data, string playerId)
    {
        foreach (GameSaveData.Player player in data.Players)
        {
            if (player.PlayerId == playerId)
            {
                return player;
            }
        }

        return null;
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
