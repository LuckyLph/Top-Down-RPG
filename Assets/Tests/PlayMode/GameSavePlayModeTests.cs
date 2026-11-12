using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests for the world save from the main menu (Docs/WorldStreaming.md: Persistence): New Game enters the
/// starting world and saves the local player's place and weapon; an editor boot straight into a world is a throwaway
/// session that leaves the save alone; without a usable save Continue and Saved games are off, and a joined copy counts
/// as one; Continue and Host Game start from the save, the local player at its saved world and position holding its
/// saved weapon, and the host's weapon switch is saved; Saved games hosts an older session than the latest
/// (Docs/UiRevamp.md, Phase 10); a player the host knows by its player id joins at its saved place, once the host has
/// loaded that world, holding its saved weapon on both machines, and its place is kept as it leaves.
/// </summary>
public class GameSavePlayModeTests
{
    /// <summary>
    /// The weapon id the tests' saves hold: the longbow's, its asset's GUID.
    /// </summary>
    private const string SavedWeapon = "9f2cea8bb4988f2418ddaa9824d9fec9";

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
    /// New Game starts the starting world at its entry and, as the player enters it, writes a save in the current format
    /// with this install's player in that world at that entry, holding its starting weapon.
    /// </summary>
    [UnityTest]
    public IEnumerator NewGame_EntersTheStartingWorld_AndSavesThePlayersPlace()
    {
        MemorySaveFolder saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        WorldDefinition world = gameFlow.Scenes.StartingWorld;

        Object.FindAnyObjectByType<MainMenuController>().StartNewGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(world), "New Game plays the starting world.");
        yield return SceneBootTestHelper.WaitUntil(() => saveStore.Contents != null, "entering the world to write a save");

        Assert.That(GameSaveData.TryParse(saveStore.Contents, out GameSaveData data), Is.True);
        Assert.That(data.Version, Is.EqualTo(GameSaveData.CurrentVersion));
        GameSaveData.Player player = FindPlayer(data, SceneBootTestHelper.ResolveFromMain<NetworkSession>().LocalPlayerId);
        Assert.That(player, Is.Not.Null, "The save keys the local player by this install's player id.");
        Assert.That(player.WorldId, Is.EqualTo(world.WorldId));
        Assert.That(player.Position, Is.EqualTo(world.EntryPosition(gameFlow.Scenes.StartingWorldEntry)));
        Assert.That(player.Waystones, Is.Empty);
        PlayerWeaponController weapon = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Weapon;
        Assert.That(player.Weapon, Is.EqualTo(weapon.StartingWeapon.WeaponId), "The save names the weapon the player holds.");
    }

    /// <summary>
    /// Booting the editor straight into a world, rather than starting from the main menu, is a throwaway session: it
    /// writes nothing to the save file as it enters the world, when a save is asked for or as it leaves to the menu, so
    /// a save stored meanwhile is left as it was, and Continue then loads it, which makes a real session that saves
    /// again.
    /// </summary>
    [UnityTest]
    public IEnumerator BootingIntoAWorldFromTheEditor_IsAThrowaway_ThatLeavesTheSaveAlone()
    {
        MemorySaveFolder saveStore = new();
        yield return SceneBootTestHelper.BootIntoTestlands(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        SessionSave session = SceneBootTestHelper.ResolveFromMain<SessionSave>();
        Assert.That(session.IsThrowaway, Is.True, "An editor boot into a world is a throwaway session.");
        WorldDefinition sandbox = FindWorld(gameFlow, "Sandbox");
        string stored = SaveWith(sandbox, SceneBootTestHelper.ResolveFromMain<NetworkSession>().LocalPlayerId, sandbox.ToWorld(SavedSandboxPlace)).ToJson();
        saveStore.Contents = stored;
        yield return null;
        yield return null;
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<GameSaveRecorder>().Save(), Is.False, "A throwaway session writes no save.");
        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(saveStore.WriteCount, Is.Zero, "Entering the world and leaving it wrote nothing.");
        Assert.That(saveStore.Contents, Is.EqualTo(stored), "The stored save is left as it was.");

        Object.FindAnyObjectByType<MainMenuController>().ContinueGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        AssertLocalPlayerAt(gameFlow, sandbox, sandbox.ToWorld(SavedSandboxPlace));
        Assert.That(session.IsThrowaway, Is.False, "Continue from the menu is a real session.");
        yield return SceneBootTestHelper.WaitUntil(() => saveStore.WriteCount > 0, "the continued session to save as its player enters the world");
    }

    /// <summary>
    /// With no save, or an old save of an earlier build (version 1, which held an area), ignored with its warning, the
    /// main menu's Continue button, its line and Saved games hide and New game is selected (Docs/UiRevamp.md, Phases 9
    /// and 10); a copy of this machine's place in a host's session that may still be there plays on its own, so with only
    /// that copy they show.
    /// </summary>
    [UnityTest]
    public IEnumerator MainMenu_WithoutAUsableSave_HidesContinueAndSavedGames()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu(new MemorySaveFolder());
        yield return AssertContinueHidden();

        MemorySaveFolder oldSave = new();
        oldSave.LegacyStore.Contents = "{\"version\":1,\"areaSceneGuid\":\"0\",\"spawnId\":\"start\"}";
        LogAssert.Expect(LogType.Warning, new Regex("The save file of an earlier build has format version 1"));
        yield return SceneBootTestHelper.BootIntoMainMenu(oldSave);
        yield return AssertContinueHidden();

        MemorySaveFolder copyOnly = new();
        GameSaveData copy = new(0d, new[] { new GameSaveData.Player("someone", "world", Vector2.zero) }, new GameSaveData.World[0]);
        copy.SetHost("a-host-session", false);
        copyOnly.Seed(copy);
        yield return SceneBootTestHelper.BootIntoMainMenu(copyOnly);
        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        yield return null;
        Assert.That(menu.ContinueButton.gameObject.activeSelf && menu.SavedGamesButton.gameObject.activeSelf, Is.True, "A copy plays on its own.");
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.ContinueButton.gameObject));
    }

    /// <summary>
    /// Play together's Host another saved game opens Saved games to host, and a card's Host hosts that older session
    /// rather than the latest: the host's player starts at that session's saved place in its world, under that
    /// session's id, and the host's first save goes to that session's file.
    /// </summary>
    [UnityTest]
    public IEnumerator HostingAnOlderSession_FromSavedGames_LandsAtThatSessionsPlace()
    {
        MemorySaveFolder saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        WorldDefinition sandbox = FindWorld(gameFlow, "Sandbox");
        WorldDefinition starting = gameFlow.Scenes.StartingWorld;
        GameSaveData older = SaveWith(sandbox, session.LocalPlayerId, sandbox.ToWorld(SavedSandboxPlace));
        older.SetSession("older-session");
        older.Stamp(new System.DateTime(2026, 11, 1, 12, 0, 0, System.DateTimeKind.Utc));
        string olderFile = saveStore.Seed(older);
        GameSaveData newer = SaveWith(starting, session.LocalPlayerId, starting.EntryPosition(gameFlow.Scenes.StartingWorldEntry));
        newer.SetSession("newer-session");
        newer.Stamp(new System.DateTime(2026, 11, 2, 12, 0, 0, System.DateTimeKind.Utc));
        saveStore.Seed(newer);
        int writesBefore = saveStore.WriteCount;

        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        menu.OpenPlayTogether();
        PlayTogetherView together = Object.FindAnyObjectByType<PlayTogetherView>();
        Assert.That(together.HostSavedButton.gameObject.activeSelf, Is.True, "There is a save besides the latest.");
        together.HostSavedButton.onClick.Invoke();
        SavedGamesView saved = Object.FindAnyObjectByType<SavedGamesView>();
        Assert.That(saved.IsShown, Is.True);
        Assert.That(saved.CardCount, Is.EqualTo(2));
        Assert.That(saved.Card(1).PlayButton.gameObject.activeSelf, Is.False, "Opened to host, the cards have no Play.");
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(saved.Card(0).HostButton.gameObject));
        saved.Card(1).HostButton.onClick.Invoke();
        Assert.That(session.IsHost, Is.True);
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        AssertLocalPlayerAt(gameFlow, sandbox, sandbox.ToWorld(SavedSandboxPlace));
        Assert.That(SceneBootTestHelper.ResolveFromMain<SessionSave>().Id, Is.EqualTo("older-session"));
        yield return SceneBootTestHelper.WaitUntil(() => saveStore.WriteCount > writesBefore, "the host to save as its player enters the world");
        Assert.That(saveStore.LastWrittenName, Is.EqualTo(olderFile), "The host saves into the older session's file.");
    }

    /// <summary>
    /// The PlayMode tests' in-memory folder deletes a save as the disk's does, keeping its text as its one copy, a
    /// second delete replacing it.
    /// </summary>
    [Test]
    public void MemorySaveFolder_DeletesASave_KeepingOneCopy()
    {
        MemorySaveFolder folder = new();
        Assert.That(folder.Delete("session-a.json"), Is.False);
        folder.Open("session-a.json").Write("first");
        Assert.That(folder.Delete("session-a.json"), Is.True);
        Assert.That(folder.Names(), Is.Empty);
        Assert.That(folder.DeletedCopyOf("session-a.json"), Is.EqualTo("first"));
        folder.Open("session-a.json").Write("second");
        Assert.That(folder.Delete("session-a.json"), Is.True);
        Assert.That(folder.DeletedCopyOf("session-a.json"), Is.EqualTo("second"));
    }

    /// <summary>
    /// Continue, offline, puts the local player in its saved world at its saved position holding its saved weapon,
    /// continues the save's world clock and brings back its worlds' states.
    /// </summary>
    [UnityTest]
    public IEnumerator Continue_LandsInTheSavedWorldAndPosition()
    {
        MemorySaveFolder saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        WorldDefinition sandbox = FindWorld(gameFlow, "Sandbox");
        string localId = SceneBootTestHelper.ResolveFromMain<NetworkSession>().LocalPlayerId;
        saveStore.Contents = SaveWith(sandbox, localId, sandbox.ToWorld(SavedSandboxPlace), SavedWeapon).ToJson();

        Object.FindAnyObjectByType<MainMenuController>().ContinueGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(SceneBootTestHelper.ResolveFromMain<NetworkSession>().IsActive, Is.False, "Continue plays offline.");
        AssertLocalPlayerAt(gameFlow, sandbox, sandbox.ToWorld(SavedSandboxPlace));
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Weapon.CurrentWeapon.WeaponId, Is.EqualTo(SavedWeapon), "The player holds its saved weapon.");
        SessionSave session = SceneBootTestHelper.ResolveFromMain<SessionSave>();
        Assert.That(session.Time, Is.GreaterThanOrEqualTo(1000d), "The world clock continues from the saved time.");
        Assert.That(session.TryGetWorld(sandbox.WorldId, out WorldState state) && state.TryGetChunk(Vector2Int.one, out ChunkState chunk) && chunk.TryGetMobDeath("saved-mob", out _), Is.True, "The saved world state is back.");
    }

    /// <summary>
    /// Host Game with a save hosts from it: the host's player starts at its saved world and position, holding its saved
    /// weapon, and the save it writes keeps it there; a weapon switch is saved as the host leaves to the menu, which ends
    /// the session.
    /// </summary>
    [UnityTest]
    public IEnumerator HostGame_LandsInTheSavedWorldAndPosition()
    {
        MemorySaveFolder saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        WorldDefinition sandbox = FindWorld(gameFlow, "Sandbox");
        saveStore.Contents = SaveWith(sandbox, session.LocalPlayerId, sandbox.ToWorld(SavedSandboxPlace), SavedWeapon).ToJson();
        int writesBefore = saveStore.WriteCount;

        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        Assert.That(session.IsHost, Is.True);
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        AssertLocalPlayerAt(gameFlow, sandbox, sandbox.ToWorld(SavedSandboxPlace));
        yield return SceneBootTestHelper.WaitUntil(() => saveStore.WriteCount > writesBefore, "the host to save as its player enters the world");
        Assert.That(GameSaveData.TryParse(saveStore.Contents, out GameSaveData data), Is.True);
        Assert.That(FindPlayer(data, session.LocalPlayerId).Position, Is.EqualTo(sandbox.ToWorld(SavedSandboxPlace)));
        Assert.That(data.Time, Is.GreaterThanOrEqualTo(1000d));
        PlayerWeaponController weapon = SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current.Weapon;
        Assert.That(weapon.CurrentWeapon.WeaponId, Is.EqualTo(SavedWeapon), "The host's player holds its saved weapon.");
        Assert.That(FindPlayer(data, session.LocalPlayerId).Weapon, Is.EqualTo(SavedWeapon));
        PlayerWeapon switched = weapon.Catalog.Next(weapon.CurrentWeapon);
        weapon.Equip(switched);

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        yield return SceneBootTestHelper.WaitUntil(() => !session.IsActive, "the session to shut down in the menu");
        Assert.That(GameSaveData.TryParse(saveStore.Contents, out GameSaveData left), Is.True);
        Assert.That(FindPlayer(left, session.LocalPlayerId).Weapon, Is.EqualTo(switched.WeaponId), "Leaving saves the weapon switched to.");
    }

    /// <summary>
    /// Hosting the starting world with a save that knows a client's player id in the Sandbox: when that client joins,
    /// the host loads the Sandbox around its saved place before spawning its player there, puts it in the Sandbox on
    /// both machines, its own player and the host's copy holding its saved weapon, and saves its place and weapon; when
    /// it leaves after walking, its new place is kept for its next join.
    /// </summary>
    [UnityTest]
    public IEnumerator AKnownPlayerJoining_SpawnsAtItsSavedPlace_OnceTheHostLoadedIt()
    {
        const string KnownId = "known-client";
        MemorySaveFolder saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        WorldDefinition sandbox = FindWorld(gameFlow, "Sandbox");
        Vector2 saved = sandbox.ToWorld(SavedSandboxPlace);
        saveStore.Contents = SaveWith(sandbox, KnownId, saved, SavedWeapon).ToJson();

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
        PlayerWeaponController clientWeapon = client.LocalPlayer.Current.Weapon;
        PlayerWeaponController copyWeapon = hostCopy.GetComponent<PlayerWeaponController>();
        yield return SceneBootTestHelper.WaitUntil(() => clientWeapon.CurrentWeapon.WeaponId == SavedWeapon, "the client to equip its saved weapon");
        Assert.That(copyWeapon.CurrentWeapon.WeaponId, Is.EqualTo(SavedWeapon), "The host's copy shows the saved weapon from the spawn on.");
        yield return SceneBootTestHelper.WaitUntil(
            () => GameSaveData.TryParse(saveStore.Contents, out GameSaveData data) && FindPlayer(data, KnownId) != null && FindPlayer(data, session.LocalPlayerId) != null,
            "the host to save as the known player enters its world");
        Assert.That(GameSaveData.TryParse(saveStore.Contents, out GameSaveData joined), Is.True);
        Assert.That(FindPlayer(joined, KnownId).Weapon, Is.EqualTo(SavedWeapon), "The host saves the client's weapon.");

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
    /// Asserts the main menu's Continue button, its line and Saved games hide and New game is selected, a frame after the
    /// menu started.
    /// </summary>
    private static IEnumerator AssertContinueHidden()
    {
        MainMenuController menu = Object.FindAnyObjectByType<MainMenuController>();
        yield return null;

        Assert.That(menu.ContinueButton, Is.Not.Null, "The menu should have a Continue button assigned.");
        Assert.That(menu.ContinueButton.gameObject.activeSelf, Is.False);
        Assert.That(menu.ContinueLineText.gameObject.activeSelf, Is.False);
        Assert.That(menu.SavedGamesButton.gameObject.activeSelf, Is.False);
        Assert.That(EventSystem.current.currentSelectedGameObject, Is.SameAs(menu.NewGameButton.gameObject));
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
    /// A save at 1000 s of game time holding <paramref name="playerId"/> at <paramref name="position"/> in
    /// <paramref name="world"/>, holding the weapon <paramref name="weapon"/> (none by default), and a killed mob in that
    /// world's chunk (1, 1).
    /// </summary>
    private static GameSaveData SaveWith(WorldDefinition world, string playerId, Vector2 position, string weapon = null)
    {
        return new GameSaveData(
            1000d,
            new[] { new GameSaveData.Player(playerId, world.WorldId, position, null, weapon) },
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
