using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using VContainer;
using VContainer.Unity;

/// <summary>
/// PlayMode tests for what persists of a world on one machine (Docs/WorldStreaming.md: Persistence): a killed mob stays
/// dead after its chunk unloads and loads again, and comes back once its respawn delay has passed; a runtime terrain
/// edit survives its chunk unloading; leaving to the menu saves the world's state and the player's place, and Continue
/// brings both back. A saved place the player can no longer stand on (Docs/WorldGeneration.md: Runtime changes) moves
/// to the nearest walkable cell, offline as Continue starts and on the host for a joining player it knows, or, with no
/// walkable cell near, to the player's nearest discovered waystone.
/// </summary>
public class WorldPersistencePlayModeTests
{
    private const string SandboxRootPath = "Assets/Worlds/Sandbox/World_Sandbox.unity";
    private const string KnownId = "known-client";
    private static readonly Vector2 FarCorner = new(100.5f, 100.5f);
    private static readonly Vector2 SandboxPond = new(40.5f, 90.5f);
    private static readonly Vector2 OverworldOpenSea = new(2.5f, 2.5f);

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
    /// In the Sandbox, a mob of the start chunk is killed: its death is recorded by its spawn point's id. With the
    /// player gone and the chunk unloaded and loaded again, that spawn point stays empty while the others spawn; once
    /// the Weasel's respawn delay has passed (its death moved that far back on the world clock), the next load brings
    /// it back and forgets the death.
    /// </summary>
    [UnityTest]
    public IEnumerator AKilledMob_StaysDeadAfterItsChunkReloads_AndReturnsAfterItsDelay()
    {
        yield return SceneBootTestHelper.BootIntoWorld(SandboxRootPath);
        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        SessionSave sessionSave = SceneBootTestHelper.ResolveFromMain<SessionSave>();
        ChunkMobSpawner spawner = SpawnerOf(world, Vector2Int.zero);
        int spawned = spawner.Mobs.Count;
        Assert.That(spawned, Is.EqualTo(3), "The Sandbox's start chunk has three mob spawn points.");

        MobController killed = spawner.Mobs[0];
        float respawnDelay = killed.Config.RespawnDelaySeconds;
        Assert.That(killed.Config.Respawns, Is.True);
        Health health = killed.GetComponent<Health>();
        health.ApplyDamage(health.MaxHealth);
        Assert.That(sessionSave.TryGetWorld(world.WorldId, out WorldState state), Is.True);
        Assert.That(state.TryGetChunk(Vector2Int.zero, out ChunkState chunk) && chunk.DeadMobCount == 1, Is.True, "The death is recorded in the chunk's record.");
        string mobId = chunk.ToData().DeadMobs[0].Id;

        yield return ReloadChunk(world, Vector2Int.zero);
        Assert.That(SpawnerOf(world, Vector2Int.zero).Mobs.Count, Is.EqualTo(spawned - 1), "A killed mob stays dead after its chunk reloads.");
        Assert.That(chunk.TryGetMobDeath(mobId, out _), Is.True);

        chunk.RecordMobDeath(mobId, sessionSave.Time - respawnDelay - 1d);
        yield return ReloadChunk(world, Vector2Int.zero);
        Assert.That(SpawnerOf(world, Vector2Int.zero).Mobs.Count, Is.EqualTo(spawned), "The mob comes back once its delay has passed and its chunk loads.");
        Assert.That(chunk.TryGetMobDeath(mobId, out _), Is.False, "The death is forgotten once the mob is back.");
    }

    /// <summary>
    /// In the Sandbox, a hole opened in the start chunk's grass at runtime is still there after the chunk unloads and
    /// loads again: the new chunk's terrain restores it from the world's state, as its own edit.
    /// </summary>
    [UnityTest]
    public IEnumerator ATerrainEdit_SurvivesItsChunkUnloading()
    {
        yield return SceneBootTestHelper.BootIntoWorld(SandboxRootPath);
        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        Vector3Int cell = new(30, 30, 0);
        Tilemap grass = GrassOf(world, Vector2Int.zero);
        Assert.That(grass.HasTile(cell), Is.True, "The cell starts as grass.");

        Assert.That(TerrainOf(world, Vector2Int.zero).TrySetCells(grass, new[] { cell }, false), Is.True);
        Assert.That(grass.HasTile(cell), Is.False);

        yield return ReloadChunk(world, Vector2Int.zero);
        Tilemap reloaded = GrassOf(world, Vector2Int.zero);
        Assert.That(reloaded, Is.Not.SameAs(grass), "The chunk was loaded again.");
        Assert.That(reloaded.HasTile(cell), Is.False, "The edit survives the chunk unloading.");
        Assert.That(TerrainOf(world, Vector2Int.zero).Edits, Has.Count.EqualTo(1), "The restored edit is the terrain's own, so it would replicate.");
    }

    /// <summary>
    /// Continuing a save in the Sandbox from the main menu, a killed mob and the player's place are saved as the game
    /// returns to the menu; Continue puts the player back there with the mob still dead.
    /// </summary>
    [UnityTest]
    public IEnumerator LeavingToTheMenu_SavesTheWorld_AndContinueBringsItBack()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        WorldDefinition world = FindWorld(gameFlow, "Sandbox");
        string playerId = SceneBootTestHelper.ResolveFromMain<NetworkSession>().LocalPlayerId;
        saveStore.Contents = SaveWith(new GameSaveData.Player(playerId, world.WorldId, world.ToWorld(new Vector2(32.5f, 44.5f)))).ToJson();
        Object.FindAnyObjectByType<MainMenuController>().ContinueGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(world));
        ChunkMobSpawner spawner = SpawnerOf(world, Vector2Int.zero);
        int spawned = spawner.Mobs.Count;
        Health health = spawner.Mobs[1].GetComponent<Health>();
        health.ApplyDamage(health.MaxHealth);
        Vector2 place = world.ToWorld(new Vector2(36.5f, 44.5f));
        LocalPlayer().Controller.Teleport(place);
        yield return null;

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(GameSaveData.TryParse(saveStore.Contents, out GameSaveData data), Is.True);
        Assert.That(data.Players, Has.Count.EqualTo(1));
        Assert.That(data.Players[0].WorldId, Is.EqualTo(world.WorldId));
        Assert.That(data.Players[0].Position, Is.EqualTo(place), "The session's end saves where the player stands.");
        Assert.That(data.Worlds, Has.Count.EqualTo(1));
        Assert.That(data.Worlds[0].Chunks[0].DeadMobs, Has.Count.EqualTo(1));

        Object.FindAnyObjectByType<MainMenuController>().ContinueGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(world));
        Assert.That((Vector2)LocalPlayer().Transform.position, Is.EqualTo(place));
        Assert.That(SpawnerOf(world, Vector2Int.zero).Mobs.Count, Is.EqualTo(spawned - 1), "The killed mob is still dead after Continue.");
    }

    /// <summary>
    /// A save placing this install's player in the middle of the Sandbox's pond, as if the ground there had turned to
    /// water since: Continue starts the player on the walkable cell nearest that place, with one warning, and leaving
    /// to the menu saves it there.
    /// </summary>
    [UnityTest]
    public IEnumerator APlaceSavedOnWater_StartsOnTheNearestWalkableCell_AndIsSavedThere()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        string playerId = SceneBootTestHelper.ResolveFromMain<NetworkSession>().LocalPlayerId;
        WorldDefinition sandbox = FindWorld(gameFlow, "Sandbox");
        Vector2 saved = sandbox.ToWorld(SandboxPond);
        saveStore.Contents = SaveWith(new GameSaveData.Player(playerId, sandbox.WorldId, saved)).ToJson();

        LogAssert.Expect(LogType.Warning, new Regex("is not walkable ground any more; it starts at .*, the nearest walkable cell"));
        Object.FindAnyObjectByType<MainMenuController>().ContinueGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(sandbox));
        PlayerController player = LocalPlayer().Controller;
        Vector2 start = player.transform.position;
        AssertOnTheNearestWalkableCell(sandbox, player.ControlSettings.MovementProfile, saved, start);

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(GameSaveData.TryParse(saveStore.Contents, out GameSaveData data), Is.True);
        Assert.That(data.Players[0].Position, Is.EqualTo(start), "The next save keeps the corrected place.");
    }

    /// <summary>
    /// A save placing this install's player in the Overworld's open sea, far from any walkable cell, having discovered
    /// the Lakeside's waystone: Continue starts the player at that waystone, the nearest it discovered (nearer than the
    /// Clearing's, the default entry), with one warning.
    /// </summary>
    [UnityTest]
    public IEnumerator APlaceSavedFarOutAtSea_StartsAtTheNearestDiscoveredWaystone()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        string playerId = SceneBootTestHelper.ResolveFromMain<NetworkSession>().LocalPlayerId;
        WorldDefinition overworld = FindWorld(gameFlow, "Overworld");
        Vector2 saved = overworld.ToWorld(OverworldOpenSea);
        GameSaveData.Waystone[] discovered = { new(overworld.WorldId, "lakeside") };
        saveStore.Contents = SaveWith(new GameSaveData.Player(playerId, overworld.WorldId, saved, discovered)).ToJson();

        LogAssert.Expect(LogType.Warning, new Regex("no walkable cell lies within \\d+ cells; it starts at .*, its nearest discovered waystone lakeside"));
        Object.FindAnyObjectByType<MainMenuController>().ContinueGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(overworld));
        PlayerController player = LocalPlayer().Controller;
        Assert.That((Vector2)player.transform.position, Is.EqualTo(overworld.EntryPosition("lakeside")), "The player starts at the Lakeside's waystone.");

        WorldNavigation navigation = NavigationOf(overworld);
        TerrainMovementProfile2D profile = player.ControlSettings.MovementProfile;
        int radius = SceneBootTestHelper.ResolveFromGameplay<GameplaySettings>().SavedPlaceSearchCells;
        Vector3Int from = navigation.WorldToCell(saved);
        Assert.That(navigation.HasChunk(Vector2Int.zero), Is.True, "The chunk of the saved place is still loaded.");
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = Mathf.Abs(dx) - radius; dy <= radius - Mathf.Abs(dx); dy++)
            {
                Assert.That(navigation.IsCellWalkable(from + new Vector3Int(dx, dy, 0), profile), Is.False, $"No walkable cell within {radius} cells of the saved place ({dx}, {dy}).");
            }
        }
    }

    /// <summary>
    /// Hosting the starting world with a save that places a client's player id in the Sandbox's pond: when that client
    /// joins, the host spawns its player on the walkable cell nearest that place, with one warning, the client's own
    /// copy arrives there too, and the host saves it there.
    /// </summary>
    [UnityTest]
    public IEnumerator AJoiningPlayerSavedOnWater_SpawnsOnTheNearestWalkableCell()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoMainMenu(saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        WorldDefinition sandbox = FindWorld(gameFlow, "Sandbox");
        Vector2 saved = sandbox.ToWorld(SandboxPond);
        saveStore.Contents = SaveWith(new GameSaveData.Player(KnownId, sandbox.WorldId, saved)).ToJson();

        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        Assert.That(gameFlow.CurrentWorld, Is.SameAs(gameFlow.Scenes.StartingWorld), "The host, unknown to the save, starts at the starting entry.");
        PlayerRegistry hostPlayers = SceneBootTestHelper.ResolveFromGameplay<PlayerRegistry>();

        LogAssert.Expect(LogType.Warning, new Regex($"player {KnownId}'s place .* is not walkable ground any more; it starts at .*, the nearest walkable cell"));
        client = InProcessClient.Start(session, SceneBootTestHelper.ResolveFromGameplay<PlayerSpawner>().PlayerNetworkPrefab, KnownId);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendJoined();
        yield return SceneBootTestHelper.WaitUntil(() => client.LocalPlayer.Current != null && hostPlayers.Players.Count == 2, "the host to spawn the known player");
        client.DisableColliders();

        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        Vector2 start = hostCopy.transform.position;
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<PlayerWorlds>().Get(hostCopy.GetComponent<PlayerNetworkSync>().Handle), Is.SameAs(sandbox));
        AssertOnTheNearestWalkableCell(sandbox, hostCopy.ControlSettings.MovementProfile, saved, start);
        Assert.That(Vector2.Distance(client.LocalPlayer.Current.Transform.position, start), Is.LessThan(0.01f), "The client's own player arrives at the place the host chose.");
        yield return SceneBootTestHelper.WaitUntil(
            () => GameSaveData.TryParse(saveStore.Contents, out GameSaveData data) && FindPlayer(data, KnownId) != null && FindPlayer(data, KnownId).Position == start,
            "the host to save the known player at its corrected place");
    }

    /// <summary>
    /// Sends the local player to the far corner until the chunk at <paramref name="coords"/> unloads, then back to the
    /// world's default entry until it is loaded again.
    /// </summary>
    private static IEnumerator ReloadChunk(WorldDefinition world, Vector2Int coords)
    {
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        worlds.TryGet(world, out LoadedWorld loaded);
        LocalPlayer().Controller.Teleport(world.ToWorld(FarCorner));
        yield return SceneBootTestHelper.WaitUntil(() => !loaded.Chunks.ContainsKey(coords), $"chunk {coords} to unload");
        LocalPlayer().Controller.Teleport(world.EntryPosition(null));
        yield return SceneBootTestHelper.WaitUntil(() => loaded.Chunks.ContainsKey(coords), $"chunk {coords} to load again");
        yield return null;
    }

    /// <summary>
    /// Asserts that <paramref name="saved"/> lies on a cell <paramref name="profile"/> cannot walk in
    /// <paramref name="world"/>'s navigation, and that <paramref name="start"/> is the centre of the walkable cell
    /// nearest it by Manhattan distance, within the saved-place search radius: every nearer cell is unwalkable.
    /// </summary>
    private static void AssertOnTheNearestWalkableCell(WorldDefinition world, TerrainMovementProfile2D profile, Vector2 saved, Vector2 start)
    {
        WorldNavigation navigation = NavigationOf(world);
        int radius = SceneBootTestHelper.ResolveFromGameplay<GameplaySettings>().SavedPlaceSearchCells;
        Vector3Int from = navigation.WorldToCell(saved);
        Vector3Int to = navigation.WorldToCell(start);
        Assert.That(navigation.IsCellWalkable(from, profile), Is.False, "The saved place is not walkable.");
        Assert.That(navigation.IsCellWalkable(to, profile), Is.True, "The player starts on a walkable cell.");
        Assert.That(start, Is.EqualTo((Vector2)navigation.CellToWorldCenter(to)), "The player starts at the centre of its cell.");
        int distance = Mathf.Abs(to.x - from.x) + Mathf.Abs(to.y - from.y);
        Assert.That(distance, Is.InRange(1, radius), "The cell lies within the search radius.");
        for (int dx = 1 - distance; dx < distance; dx++)
        {
            for (int dy = Mathf.Abs(dx) - distance + 1; dy < distance - Mathf.Abs(dx); dy++)
            {
                Assert.That(navigation.IsCellWalkable(from + new Vector3Int(dx, dy, 0), profile), Is.False, $"No nearer walkable cell ({dx}, {dy}).");
            }
        }
    }

    /// <summary>
    /// A version 2 save at 1000 s of game time holding only <paramref name="player"/>.
    /// </summary>
    private static GameSaveData SaveWith(GameSaveData.Player player)
    {
        return new GameSaveData(1000d, new[] { player }, new GameSaveData.World[0]);
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

    /// <summary>
    /// The navigation of the loaded <paramref name="world"/>; fails the test when it is not loaded.
    /// </summary>
    private static WorldNavigation NavigationOf(WorldDefinition world)
    {
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        Assert.That(worlds.TryGet(world, out LoadedWorld loaded), Is.True, $"{world.name} is loaded.");
        return loaded.Navigation;
    }

    /// <summary>
    /// The mob spawner of the loaded chunk at <paramref name="coords"/> of <paramref name="world"/>.
    /// </summary>
    private static ChunkMobSpawner SpawnerOf(WorldDefinition world, Vector2Int coords)
    {
        return ChunkScope(world, coords).Container.Resolve<ChunkMobSpawner>();
    }

    /// <summary>
    /// The runtime-editable terrain of the loaded chunk at <paramref name="coords"/> of <paramref name="world"/>.
    /// </summary>
    private static AreaTerrain TerrainOf(WorldDefinition world, Vector2Int coords)
    {
        return SceneQuery.FindFirst<AreaTerrain>(ChunkScene(world, coords));
    }

    /// <summary>
    /// The grass layer of the loaded chunk at <paramref name="coords"/> of <paramref name="world"/>.
    /// </summary>
    private static Tilemap GrassOf(WorldDefinition world, Vector2Int coords)
    {
        foreach (Tilemap tilemap in SceneQuery.FindAll<Tilemap>(ChunkScene(world, coords)))
        {
            if (tilemap.name == "GrassTilemap")
            {
                return tilemap;
            }
        }

        Assert.Fail($"Chunk {coords} has no GrassTilemap.");
        return null;
    }

    /// <summary>
    /// The scope of the loaded chunk at <paramref name="coords"/> of <paramref name="world"/>.
    /// </summary>
    private static LifetimeScope ChunkScope(WorldDefinition world, Vector2Int coords)
    {
        return LifetimeScope.Find<ChunkLifetimeScope>(ChunkScene(world, coords));
    }

    /// <summary>
    /// The scene of the loaded chunk at <paramref name="coords"/> of <paramref name="world"/>; fails the test when it is
    /// not loaded.
    /// </summary>
    private static Scene ChunkScene(WorldDefinition world, Vector2Int coords)
    {
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        Assert.That(worlds.TryGet(world, out LoadedWorld loaded) && loaded.Chunks.TryGetValue(coords, out _), Is.True, $"Chunk {coords} is loaded.");
        return loaded.Chunks[coords];
    }

    /// <summary>
    /// This machine's player.
    /// </summary>
    private static LocalPlayer LocalPlayer()
    {
        return SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current;
    }
}
