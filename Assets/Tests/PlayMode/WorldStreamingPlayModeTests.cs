using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

/// <summary>
/// PlayMode tests for seamless worlds on one machine: booting into a world, the chunks loading around the player under
/// the right scopes, chunks following the player across the world, loaded neighbours drawing each other's border cells
/// with the texture arrays the Gameplay scope shares between them, with only the chunks near the local player drawn,
/// the player pathing across a border on its world's navigation, each chunk's mobs spawning into its scene and going
/// with it, a mob chasing the player across a border stopping at its leash, and leaving to the menu unloading
/// everything, mobs included.
/// </summary>
public class WorldStreamingPlayModeTests
{
    private const string SandboxRootPath = "Assets/Worlds/Sandbox/World_Sandbox.unity";
    private const string WorldsFolder = "Assets/Worlds/";
    private const float LeashTestTimeoutSeconds = 30f;

    /// <summary>
    /// Booting with the Sandbox's root scene open starts a session in that world at its default entry: the player
    /// stands there, the world's scope is a child of Gameplay and each chunk's scope a child of the world's, its
    /// lighting is on, and exactly the chunks within the view plus the load margin are loaded, its ground drawn.
    /// </summary>
    [UnityTest]
    public IEnumerator Boot_EntersTheWorld_WithTheChunksAroundThePlayerUnderTheWorldsScope()
    {
        yield return SceneBootTestHelper.BootIntoWorld(SandboxRootPath);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        WorldDefinition world = gameFlow.CurrentWorld;
        Assert.That(world, Is.Not.Null);
        Assert.That(world.name, Is.EqualTo("Sandbox"));

        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        Assert.That(worlds.TryGet(world, out LoadedWorld loaded), Is.True);
        LifetimeScope gameplayScope = Object.FindAnyObjectByType<GameplayLifetimeScope>();
        Assert.That(loaded.Scope, Is.InstanceOf<WorldLifetimeScope>());
        Assert.That(loaded.Scope.Parent, Is.SameAs(gameplayScope));
        Assert.That(loaded.Lighting, Is.Not.Null);
        Assert.That(loaded.Lighting.gameObject.activeSelf, Is.True, "The local player's world shows its lighting.");

        Assert.That(loaded.Chunks.Keys, Is.EquivalentTo(new[] { new Vector2Int(0, 0) }),
            "The entry at (30.5, 42) reaches 22 cells across and 18 up and down: chunk (0, 0) only.");
        foreach (Scene chunkScene in loaded.Chunks.Values)
        {
            LifetimeScope chunkScope = LifetimeScope.Find<LifetimeScope>(chunkScene);
            Assert.That(chunkScope, Is.InstanceOf<ChunkLifetimeScope>());
            Assert.That(chunkScope.Parent, Is.SameAs(loaded.Scope));
            Assert.That(SceneQuery.FindFirst<WorldChunk>(chunkScene).IsDrawn, Is.True, "A chunk around the local player draws its ground.");
        }

        PlayerController player = LocalPlayer().Controller;
        Assert.That((Vector2)player.transform.position, Is.EqualTo(world.ToWorld(world.DefaultEntry.Position)));
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<PlayerWorlds>().Get(LocalPlayer().Handle), Is.SameAs(world));
    }

    /// <summary>
    /// Standing near the border of two chunks loads both, and each draws the other's border cells: their terrain
    /// renderers read each other's ground in a strip along the border and their walls each other's wall cells. Both
    /// renderers draw with the same texture arrays, their style's, which the Gameplay scope shares.
    /// </summary>
    [UnityTest]
    public IEnumerator NeighbouringChunks_LinkTheirGroundAndWallsAcrossTheBorder()
    {
        yield return SceneBootTestHelper.BootIntoWorld(SandboxRootPath);

        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        worlds.TryGet(world, out LoadedWorld loaded);
        LocalPlayer().Controller.Teleport(world.ToWorld(new Vector2(30.5f, 60.5f)));
        yield return SceneBootTestHelper.WaitUntil(
            () => loaded.Chunks.ContainsKey(new Vector2Int(0, 0)) && loaded.Chunks.ContainsKey(new Vector2Int(0, 1)),
            "both chunks along the border at y = 64 to load");

        WorldChunk south = SceneQuery.FindFirst<WorldChunk>(loaded.Chunks[new Vector2Int(0, 0)]);
        WorldChunk north = SceneQuery.FindFirst<WorldChunk>(loaded.Chunks[new Vector2Int(0, 1)]);
        Assert.That(south.Neighbours, Does.Contain(north));
        Assert.That(north.Neighbours, Does.Contain(south));
        Assert.That(south.IsDrawn && north.IsDrawn, Is.True);
        Assert.That(south.Terrain.Skirt, Is.Not.Empty, "The southern chunk reads the northern one's border cells.");
        Assert.That(south.Terrain.SharesArrays && north.Terrain.SharesArrays, Is.True, "Both chunks draw with the shared arrays.");
        Assert.That(north.Terrain.AlbedoArray, Is.SameAs(south.Terrain.AlbedoArray));
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<SharedTerrainArrays>().AliveCount, Is.EqualTo(1));
        DualGridTilemapModule southWalls = south.GetComponentInChildren<DualGridTilemapModule>();
        Assert.That(southWalls.HasChunk, Is.True);
        Assert.That(southWalls.Neighbours, Does.Contain(north.GetComponentInChildren<DualGridTilemapModule>()));
    }

    /// <summary>
    /// Each loaded chunk adds its page to its world's navigation, and the player paths on that navigation: told to
    /// walk to a point across the border between two loaded chunks, it gets there.
    /// </summary>
    [UnityTest]
    public IEnumerator ThePlayer_PathsAcrossAChunkBorder_OnItsWorldsNavigation()
    {
        yield return SceneBootTestHelper.BootIntoWorld(SandboxRootPath);

        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        worlds.TryGet(world, out LoadedWorld loaded);
        WorldNavigation navigation = loaded.Navigation;
        Assert.That(navigation, Is.Not.Null, "The world's scope registers its navigation.");
        PlayerController player = LocalPlayer().Controller;
        player.Teleport(world.ToWorld(new Vector2(30.5f, 58.5f)));
        yield return SceneBootTestHelper.WaitUntil(
            () => navigation.HasChunk(new Vector2Int(0, 0)) && navigation.HasChunk(new Vector2Int(0, 1)),
            "both chunks along the border at y = 64 to add their pages");

        Assert.That(SceneBootTestHelper.ResolveFromGameplay<PlayerNavigation>().Of(player.transform), Is.SameAs(navigation));
        Vector2 goal = world.ToWorld(new Vector2(30.5f, 70.5f));
        TerrainMovementProfile2D profile = player.ControlSettings.MovementProfile;
        Assert.That(navigation.IsCellWalkable(navigation.WorldToCell(goal), profile), Is.True);
        Assert.That(navigation.AreCellsConnected(navigation.WorldToCell(player.transform.position), navigation.WorldToCell(goal), profile), Is.True);

        PlayerMotor2D motor = player.GetComponent<PlayerMotor2D>();
        Assert.That(motor.MoveTo(goal), Is.True);
        Assert.That(motor.Follower.Navigation, Is.SameAs(navigation));
        yield return SceneBootTestHelper.WaitUntil(() => motor.ReachedDestination, "the player to walk across the border");

        Assert.That(Vector2.Distance(player.transform.position, goal), Is.LessThan(0.25f));
    }

    /// <summary>
    /// When the player moves to the far corner of the world, the chunks there load and the ones left behind unload;
    /// going back to the menu unloads every scene of the world.
    /// </summary>
    [UnityTest]
    public IEnumerator Moving_StreamsChunksAroundThePlayer_AndTheMenuUnloadsTheWorld()
    {
        yield return SceneBootTestHelper.BootIntoWorld(SandboxRootPath);

        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        WorldDefinition world = gameFlow.CurrentWorld;
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        worlds.TryGet(world, out LoadedWorld loaded);

        LocalPlayer().Controller.Teleport(world.ToWorld(new Vector2(100.5f, 100.5f)));
        yield return SceneBootTestHelper.WaitUntil(
            () => loaded.Chunks.Count == 1 && loaded.Chunks.ContainsKey(new Vector2Int(1, 1)),
            "the chunks to follow the player to (1, 1)");
        yield return null;

        Assert.That(LoadedWorldScenes(), Is.EquivalentTo(new[] { "World_Sandbox", "World_Sandbox_1_1" }));
        Assert.That(Object.FindObjectsByType<MobController>().Length, Is.EqualTo(MobSpawnerOf(loaded, new Vector2Int(1, 1)).Mobs.Count),
            "Only the loaded chunk's mobs are about.");

        gameFlow.ShowMainMenuAsync();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        Assert.That(gameFlow.IsInMenu, Is.True);
        Assert.That(gameFlow.CurrentWorld, Is.Null);
        Assert.That(LoadedWorldScenes(), Is.Empty, "Leaving the session unloads every world and chunk scene.");
        Assert.That(Object.FindObjectsByType<MobController>(), Is.Empty, "Leaving the session leaves no mob behind.");
    }

    /// <summary>
    /// A loaded chunk spawns a mob at each of its spawn points into its own scene, on walkable ground, injected with
    /// its world's navigation and players and given the chunk as its home; when the chunk unloads, its mobs go with it.
    /// </summary>
    [UnityTest]
    public IEnumerator ChunkMobs_SpawnIntoTheirChunksScene_AndGoWhenItUnloads()
    {
        yield return SceneBootTestHelper.BootIntoWorld(SandboxRootPath);

        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        worlds.TryGet(world, out LoadedWorld loaded);
        Vector2Int coords = Vector2Int.zero;
        Scene chunkScene = loaded.Chunks[coords];
        ChunkMobSpawner spawner = MobSpawnerOf(loaded, coords);
        MobSpawnPoint[] spawnPoints = SceneQuery.FindAll<MobSpawnPoint>(chunkScene);
        PlayerHandle player = LocalPlayer().Handle;

        Assert.That(spawnPoints, Is.Not.Empty);
        Assert.That(spawner.Mobs.Count, Is.EqualTo(spawnPoints.Length), "Each spawn point spawns one mob.");
        Rect home = new(world.ToWorld(Vector2.zero), new Vector2(ChunkCoords.Size, ChunkCoords.Size));
        List<MobController> mobs = new(spawner.Mobs);
        foreach (MobSpawnPoint spawnPoint in spawnPoints)
        {
            MobController mob = mobs.Find(candidate => Vector2.Distance(candidate.Patrol.SpawnPosition, spawnPoint.transform.position) < 0.01f);
            Assert.That(mob, Is.Not.Null, $"No mob spawned at {spawnPoint.name}.");
            Assert.That(mob.gameObject.scene, Is.EqualTo(chunkScene));
            Assert.That(mob.Navigation, Is.SameAs(loaded.Navigation));
            Assert.That(mob.Players, Is.InstanceOf<WorldPlayers>());
            Assert.That(((WorldPlayers)mob.Players).World, Is.SameAs(world));
            Assert.That(mob.Players.Contains(player), Is.True, "The player in the world is one the mob considers.");
            Assert.That(mob.Patrol.HomeArea, Is.EqualTo(home));
            Vector3Int cell = loaded.Navigation.WorldToCell(spawnPoint.transform.position);
            Assert.That(loaded.Navigation.IsCellWalkable(cell, mob.Config.MovementProfile), Is.True, $"{spawnPoint.name} stands on walkable ground.");
        }

        LocalPlayer().Controller.Teleport(world.ToWorld(new Vector2(100.5f, 100.5f)));
        yield return SceneBootTestHelper.WaitUntil(() => !loaded.Chunks.ContainsKey(coords), "chunk (0, 0) to unload");
        yield return null;

        Assert.That(mobs.TrueForAll(mob => mob == null), Is.True, "A chunk's mobs go with it.");
    }

    /// <summary>
    /// A mob of chunk (0, 0) chases the player north across the border into chunk (0, 1), the player staying a few
    /// cells ahead of it, gives up once the player is beyond its leash (8 cells past the border), never going further
    /// itself, and walks home into its own chunk.
    /// </summary>
    [UnityTest]
    public IEnumerator AMobChasingThePlayerAcrossABorder_StopsAtItsLeash_AndGoesHome()
    {
        yield return SceneBootTestHelper.BootIntoWorld(SandboxRootPath);

        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        Worlds worlds = SceneBootTestHelper.ResolveFromGameplay<Worlds>();
        worlds.TryGet(world, out LoadedWorld loaded);
        MobController mob = null;
        foreach (MobController candidate in MobSpawnerOf(loaded, Vector2Int.zero).Mobs)
        {
            if (mob == null || candidate.Patrol.SpawnPosition.y > mob.Patrol.SpawnPosition.y)
            {
                mob = candidate;
            }
        }

        Assert.That(mob, Is.Not.Null);
        Assert.That(mob.Config.LeashCells, Is.EqualTo(8));
        float border = world.ToWorld(new Vector2(0f, ChunkCoords.Size)).y;
        float leashEdge = border + mob.Config.LeashCells;
        PlayerController player = LocalPlayer().Controller;
        float playerX = mob.transform.position.x;
        float playerY = mob.transform.position.y + 3.5f;
        player.Teleport(new Vector2(playerX, playerY));
        yield return SceneBootTestHelper.WaitUntil(() => loaded.Navigation.HasChunk(new Vector2Int(0, 1)), "chunk (0, 1) to add its page");

        float deadline = Time.realtimeSinceStartup + LeashTestTimeoutSeconds;
        float highest = mob.transform.position.y;
        bool chased = false;
        while (!mob.Patrol.IsLeashed)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), $"Timed out waiting for the mob to give up; it is in {mob.CurrentStateId} at {(Vector2)mob.transform.position}.");
            chased |= mob.CurrentStateId == MobStateId.Chase;
            highest = Mathf.Max(highest, mob.transform.position.y);
            float ahead = mob.transform.position.y + 3.5f;
            if (ahead > playerY)
            {
                playerY = ahead;
                player.Teleport(new Vector2(playerX, playerY));
            }

            yield return null;
        }

        Assert.That(chased, Is.True, "The mob chased the player.");
        Assert.That(highest, Is.GreaterThan(border), "The mob followed the player across the border.");
        Assert.That(highest, Is.LessThanOrEqualTo(leashEdge + 0.5f), "The mob never went beyond its leash.");
        Assert.That(mob.CurrentStateId, Is.EqualTo(MobStateId.Return));

        deadline = Time.realtimeSinceStartup + LeashTestTimeoutSeconds;
        while (mob.transform.position.y >= border - 0.5f || mob.CurrentStateId == MobStateId.Return)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), $"Timed out waiting for the mob to get home; it is in {mob.CurrentStateId} at {(Vector2)mob.transform.position}.");
            Assert.That(mob.CurrentStateId, Is.Not.EqualTo(MobStateId.Chase), "A leashed mob ignores the player on its way home.");
            yield return null;
        }

        Assert.That(mob.Patrol.HomeArea.Contains(mob.transform.position), Is.True, "The mob is back in its home chunk.");
    }

    private static LocalPlayer LocalPlayer()
    {
        return SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current;
    }

    /// <summary>
    /// The mob spawner of the loaded chunk at <paramref name="coords"/> of <paramref name="world"/>, from its scope.
    /// </summary>
    private static ChunkMobSpawner MobSpawnerOf(LoadedWorld world, Vector2Int coords)
    {
        LifetimeScope scope = LifetimeScope.Find<ChunkLifetimeScope>(world.Chunks[coords]);
        return scope.Container.Resolve<ChunkMobSpawner>();
    }

    /// <summary>
    /// The names of the loaded scenes that belong to a world.
    /// </summary>
    private static List<string> LoadedWorldScenes()
    {
        List<string> names = new();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && scene.path.StartsWith(WorldsFolder))
            {
                names.Add(scene.name);
            }
        }

        return names;
    }
}
