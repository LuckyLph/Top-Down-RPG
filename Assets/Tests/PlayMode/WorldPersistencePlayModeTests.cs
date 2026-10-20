using System.Collections;
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
/// brings both back.
/// </summary>
public class WorldPersistencePlayModeTests
{
    private const string SandboxRootPath = "Assets/Worlds/Sandbox/World_Sandbox.unity";
    private static readonly Vector2 FarCorner = new(100.5f, 100.5f);

    /// <summary>
    /// In the Sandbox, a mob of the start chunk is killed: its death is recorded by its spawn point's id. With the
    /// player gone and the chunk unloaded and loaded again, that spawn point stays empty while the others spawn; once
    /// the Weasel's respawn delay has passed (its death moved that far back on the world clock), the next load brings
    /// it back and forgets the death.
    /// </summary>
    [UnityTest]
    public IEnumerator AKilledMob_StaysDeadAfterItsChunkReloads_AndReturnsAfterItsDelay()
    {
        yield return SceneBootTestHelper.BootIntoArea(SandboxRootPath);
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
        yield return SceneBootTestHelper.BootIntoArea(SandboxRootPath);
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
    /// Booted into the Sandbox (a new game there), a killed mob and the player's place are saved as the game returns to
    /// the menu; Continue puts the player back there with the mob still dead.
    /// </summary>
    [UnityTest]
    public IEnumerator LeavingToTheMenu_SavesTheWorld_AndContinueBringsItBack()
    {
        MemorySaveStore saveStore = new();
        yield return SceneBootTestHelper.BootIntoArea(SandboxRootPath, saveStore);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        WorldDefinition world = gameFlow.CurrentWorld;
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
