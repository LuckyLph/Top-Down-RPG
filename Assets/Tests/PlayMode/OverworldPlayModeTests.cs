using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// PlayMode tests on the Overworld (Docs/WorldGeneration.md: Testing), booted into by its root scene: the player starts
/// at the Clearing's waystone, the default entry, and the chunks around it load, link with their loaded neighbours, draw
/// and spawn a mob per spawn point; the player walks the Clearing's road east out through its east gate, past the
/// region's outline, and on along the world road across the border into chunk (2, 1); and in each region a player sent
/// to its waystone discovers it, dies a few cells away and comes back at it after the respawn delay. From the menu, with
/// the project's own starting world (the only tests not pinned to Testlands), a new game, a hosted game with no save and
/// a joining player the host does not know all start at the Clearing's waystone. An explicit benchmark records the
/// frames of streaming the world along a route through every region.
/// </summary>
public class OverworldPlayModeTests
{
    private const string RootPath = "Assets/Worlds/Overworld/World_Overworld.unity";
    private const int ClearingEastOutline = 108;
    private const int DeathDistance = 4;
    private static readonly Vector2 AcrossTheBorder = new(130.5f, 122.5f);
    private const string BenchmarkPath = "Temp/StreamingBenchmark.txt";
    private const string BenchmarkLabelPath = "Temp/StreamingBenchmarkLabel.txt";
    private static readonly string[] Regions = { "clearing", "villageForest", "lakeside", "snowfield", "coast", "lavaFields" };

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
    /// With the project's own Game Scenes, New Game from the menu starts in the Overworld on the Clearing's waystone.
    /// </summary>
    [UnityTest]
    public IEnumerator NewGame_WithTheProjectsStartingWorld_StartsAtTheClearingsWaystone()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu(null, null);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Object.FindAnyObjectByType<MainMenuController>().StartNewGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        WorldDefinition world = gameFlow.CurrentWorld;
        Assert.That(world != null && world.name == "Overworld", Is.True, "New Game starts in the Overworld");
        Assert.That((Vector2)LocalPlayer().Transform.position, Is.EqualTo(world.EntryPosition("clearing")), "on the Clearing's waystone");
    }

    /// <summary>
    /// With the project's own Game Scenes, Host Game with no save starts the host on the Clearing's waystone, and a
    /// joining player the host does not know spawns there too, on the next entry slot, in the Overworld.
    /// </summary>
    [UnityTest]
    public IEnumerator HostGame_AndAnUnknownJoiningPlayer_WithTheProjectsStartingWorld_StartAtTheClearingsWaystone()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu(null, null);
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        NetworkSession session = SceneBootTestHelper.ResolveFromMain<NetworkSession>();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);

        WorldDefinition world = gameFlow.CurrentWorld;
        Vector2 clearing = world.EntryPosition("clearing");
        Assert.That(world.name, Is.EqualTo("Overworld"), "Host Game starts in the Overworld");
        Assert.That((Vector2)LocalPlayer().Transform.position, Is.EqualTo(clearing), "the host on the Clearing's waystone");

        PlayerRegistry hostPlayers = SceneBootTestHelper.ResolveFromGameplay<PlayerRegistry>();
        client = InProcessClient.Start(session, SceneBootTestHelper.ResolveFromGameplay<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendJoined();
        yield return SceneBootTestHelper.WaitUntil(() => client.LocalPlayer.Current != null && hostPlayers.Players.Count == 2, "the host to spawn the joining player");
        client.DisableColliders();

        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(hostPlayers, client.Manager.LocalClientId);
        float spacing = SceneBootTestHelper.ResolveFromGameplay<GameplaySettings>().EntrySlotSpacing;
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<PlayerWorlds>().Get(hostCopy.GetComponent<PlayerNetworkSync>().Handle), Is.SameAs(world), "the joining player is in the Overworld");
        Assert.That(Vector2.Distance(hostCopy.transform.position, clearing), Is.EqualTo(spacing).Within(0.01f), "on the entry slot beside the host, at the Clearing's waystone");
    }

    /// <summary>
    /// Booting into the Overworld puts the player on the Clearing's waystone, the default entry; once the chunks around
    /// it settle, each loaded chunk draws its ground, links with each loaded neighbour, and holds one spawned mob per
    /// spawn point.
    /// </summary>
    [UnityTest]
    public IEnumerator Boot_StartsAtTheClearingsWaystone_WithTheChunksAroundLoadedLinkedDrawnAndSpawned()
    {
        yield return SceneBootTestHelper.BootIntoWorld(RootPath);
        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        Assert.That(world.name, Is.EqualTo("Overworld"));
        Assert.That((world.DefaultEntry.Name, world.DefaultEntry.Waystone), Is.EqualTo(("clearing", true)), "the default entry");
        PlayerController player = LocalPlayer().Controller;
        Assert.That((Vector2)player.transform.position, Is.EqualTo(world.ToWorld(world.DefaultEntry.Position)), "the player starts on it");

        WorldStreaming streaming = SceneBootTestHelper.ResolveFromGameplay<WorldStreaming>();
        yield return SceneBootTestHelper.WaitUntil(() => streaming.Streamer.IsSettled(new StreamInterest(world, player.transform.position)), "the chunks around the player to load");
        SceneBootTestHelper.ResolveFromGameplay<Worlds>().TryGet(world, out LoadedWorld loaded);
        Assert.That(loaded.Chunks.Count, Is.GreaterThan(1), "several chunks load around the Clearing's waystone");
        yield return SceneBootTestHelper.WaitUntil(() => EveryChunkSpawned(loaded), "every loaded chunk to spawn its mobs");

        int mobs = 0;
        foreach (KeyValuePair<Vector2Int, Scene> chunk in loaded.Chunks)
        {
            WorldChunk marker = SceneQuery.FindFirst<WorldChunk>(chunk.Value);
            Assert.That(marker.IsDrawn, Is.True, $"chunk {chunk.Key} draws its ground");
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Vector2Int other = chunk.Key + new Vector2Int(dx, dy);
                    if (other != chunk.Key && loaded.Chunks.TryGetValue(other, out Scene neighbour))
                    {
                        Assert.That(marker.Neighbours, Does.Contain(SceneQuery.FindFirst<WorldChunk>(neighbour)), $"chunk {chunk.Key} links with chunk {other}");
                    }
                }
            }

            mobs += SceneQuery.FindAll<MobController>(chunk.Value).Length;
        }

        Assert.That(mobs, Is.GreaterThan(0), "the loaded chunks hold mobs");
    }

    /// <summary>
    /// From the Clearing's waystone the player walks the region's road east to the Clearing's east gate, two cells past
    /// the outline, then on along the world road to a cell of chunk (2, 1), across the border at x = 128, each leg
    /// walkable and connected on the world's navigation; mobs are kept out of the way, and nothing is logged but the editor
    /// boot's note that the session is a throwaway one (Docs/KnownGaps.md, Phase 2).
    /// </summary>
    [UnityTest]
    public IEnumerator ThePlayer_WalksOutOfTheClearingThroughItsEastGate_AndOnAcrossAChunkBorder()
    {
        LogAssert.Expect(LogType.Log, new Regex("^Booted into Overworld from the editor: a throwaway session"));
        yield return SceneBootTestHelper.BootIntoWorld(RootPath);
        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        SceneBootTestHelper.ResolveFromGameplay<Worlds>().TryGet(world, out LoadedWorld loaded);
        PlayerController player = LocalPlayer().Controller;
        PlayerMotor2D motor = player.GetComponent<PlayerMotor2D>();
        WorldNavigation navigation = loaded.Navigation;
        TerrainMovementProfile2D profile = player.ControlSettings.MovementProfile;

        Vector2 gate = world.EntryPosition("clearingEast");
        Assert.That(ChunkCoords.CellOf(world.ToLocal(gate)).x, Is.GreaterThan(ClearingEastOutline), "the gate stands past the Clearing's outline");
        yield return WalkTo(motor, navigation, profile, gate, "the player to walk to the Clearing's east gate");
        Assert.That(Vector2.Distance(player.transform.position, gate), Is.LessThan(0.25f), "the player stands at the gate");
        Assert.That(ChunkOf(world, player), Is.EqualTo(new Vector2Int(1, 1)), "the gate lies in the Clearing's chunk");

        Vector2 beyond = world.ToWorld(AcrossTheBorder);
        yield return SceneBootTestHelper.WaitUntil(() => navigation.HasChunk(new Vector2Int(2, 1)), "chunk (2, 1) to add its page");
        yield return WalkTo(motor, navigation, profile, beyond, "the player to walk on across the border");
        Assert.That(Vector2.Distance(player.transform.position, beyond), Is.LessThan(0.25f), "the player stands across the border");
        Assert.That(ChunkOf(world, player), Is.EqualTo(new Vector2Int(2, 1)), "the player is in chunk (2, 1)");
        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// A player sent to <paramref name="region"/>'s waystone discovers it (the Clearing's, the default entry, counts as
    /// discovered from the start); dying <see cref="DeathDistance"/> cells away along the ground, with the mobs kept
    /// away, it comes back alive at that waystone after the respawn delay.
    /// </summary>
    [UnityTest]
    public IEnumerator ADeadPlayer_RespawnsAtTheRegionsWaystone_OnceItIsDiscovered([ValueSource(nameof(Regions))] string region)
    {
        yield return SceneBootTestHelper.BootIntoWorld(RootPath);
        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        SceneBootTestHelper.ResolveFromGameplay<Worlds>().TryGet(world, out LoadedWorld loaded);
        PlayerController player = LocalPlayer().Controller;
        Health health = player.GetComponent<Health>();
        SessionSave session = SceneBootTestHelper.ResolveFromMain<SessionSave>();
        string playerId = SceneBootTestHelper.ResolveFromMain<NetworkSession>().LocalPlayerId;
        WorldDefinition.Entry waystone = FindEntry(world, region);
        Assert.That(waystone.Waystone, Is.True, $"{region}'s entry is a waystone");
        Vector2 at = world.ToWorld(waystone.Position);

        yield return TeleportAndSettle(player, world, at);
        DeactivateMobs();
        yield return SceneBootTestHelper.WaitUntil(() => WaystoneRules.IsDiscovered(world, waystone, session.WaystonesOf(playerId)), $"standing at {region}'s waystone to discover it");

        Vector2 away = FirstWalkableAway(loaded.Navigation, player.ControlSettings.MovementProfile, at);
        yield return TeleportAndSettle(player, world, away);
        DeactivateMobs();
        health.ApplyDamage(health.MaxHealth);
        Assert.That(health.IsDead, Is.True, "the player dies");
        yield return SceneBootTestHelper.WaitUntil(() => !health.IsDead, "the player to respawn");
        Assert.That(Vector2.Distance(player.transform.position, at), Is.LessThan(1.5f), $"it respawns at {region}'s waystone");
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
    }

    /// <summary>
    /// Explicit benchmark of streaming the Overworld (<see cref="StreamingBenchmark"/>, shared with a player run):
    /// once the chunks around the Clearing's waystone are up and warm, the player is carried a fixed step a frame
    /// along the benchmark's route, from waystone to waystone through every region, its mobs deactivated as their
    /// chunks load. Appends the benchmark's line to <c>Temp/StreamingBenchmark.txt</c>, under the label in
    /// <c>Temp/StreamingBenchmarkLabel.txt</c> (if any).
    /// </summary>
    [UnityTest, Explicit, Category("Benchmark")]
    public IEnumerator Benchmark_StreamingAlongARouteThroughEveryRegion()
    {
        yield return SceneBootTestHelper.BootIntoWorld(RootPath);
        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        Assert.That(SceneBootTestHelper.ResolveFromGameplay<Worlds>().TryGet(world, out LoadedWorld loaded), Is.True, "the Overworld is loaded");
        StreamingBenchmark benchmark = new(world, loaded, SceneBootTestHelper.ResolveFromGameplay<WorldStreaming>(), LocalPlayer().Controller);
        yield return benchmark.Run();
        Assert.That(benchmark.Failure, Is.Null, "the benchmark ran to the end");

        string label = System.IO.File.Exists(BenchmarkLabelPath) ? System.IO.File.ReadAllText(BenchmarkLabelPath).Trim() : "unlabelled";
        string line = $"{label}: {benchmark.Line}\n{label}, loads: {benchmark.LoadDetails}";
        System.IO.File.AppendAllText(BenchmarkPath, line + "\n");
        TestContext.WriteLine(line);
    }

    /// <summary>
    /// Has <paramref name="motor"/> walk to <paramref name="goal"/>, which must be walkable and connected to where it
    /// stands, and waits until it arrives, deactivating every mob each frame, the newly loaded chunks' included.
    /// </summary>
    private static IEnumerator WalkTo(PlayerMotor2D motor, WorldNavigation navigation, TerrainMovementProfile2D profile, Vector2 goal, string description)
    {
        Vector3Int cell = navigation.WorldToCell(goal);
        Assert.That(navigation.IsCellWalkable(cell, profile), Is.True, $"{goal} is walkable");
        Assert.That(navigation.AreCellsConnected(navigation.WorldToCell(motor.transform.position), cell, profile), Is.True, $"{goal} is connected to the player");
        DeactivateMobs();
        Assert.That(motor.MoveTo(goal), Is.True, $"a path to {goal}");
        yield return SceneBootTestHelper.WaitUntil(() =>
        {
            DeactivateMobs();
            return motor.ReachedDestination;
        }, description);
    }

    /// <summary>
    /// The first of the four points <see cref="DeathDistance"/> cells east, west, north or south of
    /// <paramref name="at"/> standing on walkable ground.
    /// </summary>
    private static Vector2 FirstWalkableAway(WorldNavigation navigation, TerrainMovementProfile2D profile, Vector2 at)
    {
        foreach (Vector2 direction in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
        {
            Vector2 candidate = at + direction * DeathDistance;
            if (navigation.IsCellWalkable(navigation.WorldToCell(candidate), profile))
            {
                return candidate;
            }
        }

        Assert.Fail($"no walkable ground {DeathDistance} cells from {at}");
        return at;
    }

    /// <summary>
    /// Whether every loaded chunk of <paramref name="loaded"/> holds one mob per spawn point.
    /// </summary>
    private static bool EveryChunkSpawned(LoadedWorld loaded)
    {
        foreach (Scene chunk in loaded.Chunks.Values)
        {
            if (SceneQuery.FindAll<MobController>(chunk).Length != SceneQuery.FindAll<MobSpawnPoint>(chunk).Length)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Teleports <paramref name="player"/> to <paramref name="position"/> in <paramref name="world"/> and waits until the
    /// chunks around it are loaded.
    /// </summary>
    private static IEnumerator TeleportAndSettle(PlayerController player, WorldDefinition world, Vector2 position)
    {
        player.Teleport(position);
        WorldStreaming streaming = SceneBootTestHelper.ResolveFromGameplay<WorldStreaming>();
        yield return SceneBootTestHelper.WaitUntil(() => streaming.Streamer.IsSettled(new StreamInterest(world, position)), "the chunks around the player to load");
        yield return null;
    }

    /// <summary>
    /// Deactivates every loaded mob, so none attacks or blocks the player during the test.
    /// </summary>
    private static void DeactivateMobs()
    {
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// The chunk the player stands in.
    /// </summary>
    private static Vector2Int ChunkOf(WorldDefinition world, PlayerController player)
    {
        return ChunkCoords.ChunkOf(ChunkCoords.CellOf(world.ToLocal(player.transform.position)));
    }

    /// <summary>
    /// The entry of <paramref name="world"/> named <paramref name="name"/>; fails the test without one.
    /// </summary>
    private static WorldDefinition.Entry FindEntry(WorldDefinition world, string name)
    {
        foreach (WorldDefinition.Entry entry in world.Entries)
        {
            if (entry != null && entry.Name == name)
            {
                return entry;
            }
        }

        Assert.Fail($"{world.name} has no entry {name}");
        return null;
    }

    /// <summary>
    /// The local player.
    /// </summary>
    private static LocalPlayer LocalPlayer()
    {
        return SceneBootTestHelper.ResolveFromGameplay<LocalPlayerTracker>().Current;
    }
}
