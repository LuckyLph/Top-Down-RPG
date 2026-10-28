using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;

/// <summary>
/// PlayMode tests for props in play (Docs/PropGeneration.md, Phase 4) in Testlands, whose start chunk holds the woods'
/// looks: in play a tree and the player still sort on one layer at one order, by the 2D renderer's custom axis at the
/// tree's base and the player's feet (Docs/WorldScale.md, Phase 1; the order they draw in is checked by
/// <c>SpriteSortingTests</c>); walking from in front of a tree to behind it goes around its trunk, which the player
/// never enters, and the tree fades while the player stands behind it and comes back once the player is in front again;
/// a client's player behind a tree does not fade it on the host; and a forest of props, the player fading its trees,
/// allocates nothing per frame once warm. Explicit benchmarks record a forest's frame cost and batches.
/// </summary>
public class PropsPlayModeTests
{
    private const float WalkTimeoutSeconds = 10f;
    private const int ForestSize = 45;
    private const float ForestSpacing = 2.2f;
    private const int WarmFrames = 60;
    private const int SampleFrames = 240;
    private const float BenchmarkSeconds = 6f;
    private const string BenchmarkPath = "Temp/PropBenchmark.txt";
    private const string BenchmarkLabelPath = "Temp/PropBenchmarkLabel.txt";

    private IObjectResolver gameplay;
    private PlayerController player;
    private Scene startChunk;
    private ScriptedCommands commands;
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
    /// Once the chunk is up and the player has moved about, a tree still draws at order 0 on the Entities layer, sorted at
    /// its sprite's pivot, which lies on its base, and the player as a group at its feet on the same layer and order, so
    /// the 2D renderer's custom axis orders them by where they stand; nothing in play wrote an order.
    /// </summary>
    [UnityTest]
    public IEnumerator ATreeAndThePlayer_SortOnOneLayerAtOneOrder_ByWhereTheyStand()
    {
        yield return Boot();
        PropFade tree = TallTree(startChunk);
        Teleport(player, (Vector2)tree.transform.position + Vector2.up * 0.6f);
        yield return null;
        Teleport(player, (Vector2)tree.transform.position + Vector2.down * 0.6f);
        yield return null;

        Assert.That(tree.Visuals.sortingLayerName, Is.EqualTo("Entities"));
        Assert.That(tree.Visuals.sortingOrder, Is.Zero);
        Assert.That(tree.Visuals.spriteSortPoint, Is.EqualTo(SpriteSortPoint.Pivot));
        Assert.That((Vector2)tree.Visuals.transform.position, Is.EqualTo((Vector2)tree.transform.position), "The tree's pivot lies on its base.");
        SortingGroup group = player.GetComponent<SortingGroup>();
        Assert.That(group, Is.Not.Null, "The player sorts as a group at its feet.");
        Assert.That(group.sortingLayerName, Is.EqualTo("Entities"));
        Assert.That(group.sortingOrder, Is.Zero);
        Assert.That(player.GetComponentInChildren<SpriteRenderer>().sortingOrder, Is.Zero);
    }

    /// <summary>
    /// Walking from in front of a tree to behind it goes around its trunk, never inside its footprint; the tree fades
    /// to the settings' opacity while the player stands behind it, and is opaque again once the player has walked back
    /// in front.
    /// </summary>
    [UnityTest]
    public IEnumerator WalkingBehindATree_GoesAroundItsTrunk_AndFadesItWhileThere()
    {
        yield return Boot();
        PropFade tree = TallTree(startChunk);
        PropFadeSettings settings = gameplay.Resolve<PropFadeSettings>();
        Bounds trunk = tree.GetComponent<BoxCollider2D>().bounds;
        Vector2 front = (Vector2)tree.transform.position + Vector2.down * 1.5f;
        Vector2 behind = (Vector2)tree.transform.position + Vector2.up * 1.2f;
        Teleport(player, front);
        yield return WaitFrames(Mathf.CeilToInt(settings.FadeSeconds * 120f));
        Assert.That(tree.Visuals.color.a, Is.EqualTo(1f), "In front of the tree, it does not fade.");

        yield return WalkTo(behind, trunk);
        yield return new WaitForSeconds(settings.FadeSeconds * 2f);
        Assert.That(tree.Visuals.color.a, Is.EqualTo(settings.FadedAlpha).Within(0.01f), "Behind the tree, it fades.");

        yield return WalkTo(front, trunk);
        yield return new WaitForSeconds(settings.FadeSeconds * 2f);
        Assert.That(tree.Visuals.color.a, Is.EqualTo(1f), "Back in front, the tree is opaque again.");
    }

    /// <summary>
    /// A client's player standing behind a tree does not fade it on the host, whose own player is away; the host's player
    /// standing there does.
    /// </summary>
    [UnityTest]
    public IEnumerator AClientsPlayerBehindATree_DoesNotFadeItOnTheHost()
    {
        yield return Host();
        yield return Join();
        PropFade tree = TallTree(startChunk);
        PropFadeSettings settings = gameplay.Resolve<PropFadeSettings>();
        Vector2 behind = (Vector2)tree.transform.position + Vector2.up * 1.2f;
        Teleport(player, (Vector2)tree.transform.position + new Vector2(6f, -3f));
        PlayerController hostCopy = InProcessClient.FindCopyOwnedBy(gameplay.Resolve<PlayerRegistry>(), client.Manager.LocalClientId);
        Teleport(client.LocalPlayer.Current.Controller, behind);
        yield return SceneBootTestHelper.WaitUntil(() => Vector2.Distance(hostCopy.transform.position, behind) < 0.2f, "the client's player to stand behind the tree on the host");
        yield return new WaitForSeconds(settings.FadeSeconds * 2f);

        Assert.That(tree.Visuals.color.a, Is.EqualTo(1f), "Another machine's player never fades a prop here.");
        Assert.That(gameplay.Resolve<PropFading>().Shown, Is.Zero);

        Teleport(player, behind + Vector2.right * 0.3f);
        yield return new WaitForSeconds(settings.FadeSeconds * 2f);
        Assert.That(tree.Visuals.color.a, Is.EqualTo(settings.FadedAlpha).Within(0.01f), "The host's own player behind it does.");
    }

    /// <summary>
    /// A forest of two thousand trees around the player, handed to the fading as a chunk's props are, each at order 0; once
    /// warm, the game's update allocates nothing per frame while the player stands behind tree after tree, fading
    /// them and letting them come back (<see cref="FrameAllocationProbe"/>).
    /// </summary>
    [UnityTest]
    public IEnumerator AForest_AllocatesNothingPerFrame_WhileThePlayerFadesItsTrees()
    {
        yield return Boot();
        PropFade[] forest = PlantForest(TallTree(startChunk), (Vector2)player.transform.position + Vector2.right * 40f);
        PropFading fading = gameplay.Resolve<PropFading>();
        fading.Add(forest);
        Vector2[] spots = SpotsBehind(forest);
        yield return Wander(spots, WarmFrames);

        foreach (PropFade tree in forest)
        {
            Assert.That(tree.Visuals.sortingOrder, Is.Zero, "Nothing writes a tree's order: the axis sorts it where it stands.");
        }

        using (FrameAllocationProbe probe = new())
        {
            yield return Wander(spots, SampleFrames);
            Assert.That(probe.Frames, Is.GreaterThan(0), "The probe counted frames.");
            Assert.That(probe.Bytes, Is.Zero, $"{probe.Frames} frames among the forest allocated {probe.Bytes} bytes (at most {probe.WorstFrame} in one).");
        }

        fading.Remove(forest);
        Object.Destroy(forest[0].transform.parent.gameObject);
    }

    /// <summary>
    /// Explicit benchmark: the frame rate, main thread time, draw calls and set passes over <see cref="BenchmarkSeconds"/>
    /// with the camera over a forest of two thousand trees, appended to <c>Temp/PropBenchmark.txt</c> under the label
    /// in <c>Temp/PropBenchmarkLabel.txt</c> (if any).
    /// </summary>
    [UnityTest, Explicit, Category("Benchmark")]
    public IEnumerator Benchmark_AForest()
    {
        yield return Benchmark(true);
    }

    /// <summary>
    /// Explicit benchmark: <see cref="Benchmark_AForest"/> with the same camera over the same ground and no forest.
    /// </summary>
    [UnityTest, Explicit, Category("Benchmark")]
    public IEnumerator Benchmark_NoForest()
    {
        yield return Benchmark(false);
    }

    /// <summary>
    /// Records the frame cost with the player in a forest or, without <paramref name="withForest"/>, at the same place.
    /// Draw calls (those through the SRP
    /// Batcher and dynamic batches among them) and set passes are the editor's own counts of the frame it last drew.
    /// </summary>
    private IEnumerator Benchmark(bool withForest)
    {
        yield return Boot();
        Vector2 middle = (Vector2)player.transform.position + Vector2.right * 40f;
        PropFade[] forest = withForest ? PlantForest(TallTree(startChunk), middle) : new PropFade[0];
        gameplay.Resolve<PropFading>().Add(forest);
        Teleport(player, middle + new Vector2(0.3f, 0.5f));
        yield return WaitFrames(WarmFrames);

        using ProfilerRecorder mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
        using ProfilerRecorder lateUpdates = new("PreLateUpdate.ScriptRunBehaviourLateUpdate", 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately);
        int frames = 0;
        double mainSum = 0, lateSum = 0, batchSum = 0, drawSum = 0, passSum = 0, srpSum = 0;
        float start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < BenchmarkSeconds)
        {
            yield return null;
            frames++;
            mainSum += mainThread.LastValue;
            lateSum += lateUpdates.LastValue;
#if UNITY_EDITOR
            batchSum += UnityEditor.UnityStats.dynamicBatches;
            drawSum += UnityEditor.UnityStats.drawCalls;
            passSum += UnityEditor.UnityStats.setPassCalls;
            srpSum += UnityEditor.UnityStats.srpBatcherDrawCalls;
#endif
        }

        float seconds = Time.realtimeSinceStartup - start;
        string label = System.IO.File.Exists(BenchmarkLabelPath) ? System.IO.File.ReadAllText(BenchmarkLabelPath).Trim() : "unlabelled";
        string kind = withForest ? "forest" : "no forest";
        string line = $"{label} {kind}: {frames / seconds:F1} fps over {seconds:F1} s, main thread {mainSum / frames / 1e6:F2} ms, late updates {lateSum / frames / 1e6:F3} ms, "
            + $"{drawSum / frames:F1} draw calls ({srpSum / frames:F1} through the SRP Batcher, {batchSum / frames:F1} dynamic batches), {passSum / frames:F1} set passes, {forest.Length} trees, focused {Application.isFocused}";
        System.IO.File.AppendAllText(BenchmarkPath, line + "\n");
        TestContext.WriteLine(line);
    }

    /// <summary>
    /// Boots into Testlands with the mobs off, keeping the Gameplay scope, the local player, the chunk it started in
    /// and scripted commands driving it.
    /// </summary>
    private IEnumerator Boot()
    {
        yield return SceneBootTestHelper.BootIntoTestlands();
        yield return null;
        DisableMobs();
        FindStartChunk();
        commands = new ScriptedCommands();
        player.SetCommandSource(commands);
        yield return null;
    }

    /// <summary>
    /// Boots into the main menu and hosts a game in Testlands, deactivating every mob.
    /// </summary>
    private IEnumerator Host()
    {
        yield return SceneBootTestHelper.BootIntoMainMenu();
        GameFlow gameFlow = SceneBootTestHelper.ResolveGameFlow();
        Object.FindAnyObjectByType<MainMenuController>().HostGame();
        yield return SceneBootTestHelper.WaitForTransition(gameFlow);
        DisableMobs();
        FindStartChunk();
    }

    /// <summary>
    /// Starts the in-process client and waits until both sides see both players, their colliders off.
    /// </summary>
    private IEnumerator Join()
    {
        PlayerRegistry hostPlayers = gameplay.Resolve<PlayerRegistry>();
        client = InProcessClient.Start(SceneBootTestHelper.ResolveFromMain<NetworkSession>(), gameplay.Resolve<PlayerSpawner>().PlayerNetworkPrefab);
        yield return SceneBootTestHelper.WaitUntil(() => client.Manager.IsConnectedClient, "the in-process client to connect");
        client.SendReady();
        yield return SceneBootTestHelper.WaitUntil(
            () => hostPlayers.Players.Count == 2 && client.Players.Players.Count == 2 && client.LocalPlayer.Current != null,
            "both sides to see both players");
        client.DisableColliders();
    }

    /// <summary>
    /// Keeps the Gameplay scope, the local player and the chunk scene it stands in.
    /// </summary>
    private void FindStartChunk()
    {
        gameplay = Object.FindAnyObjectByType<GameplayLifetimeScope>().Container;
        player = gameplay.Resolve<LocalPlayerTracker>().Current.Controller;
        WorldDefinition world = SceneBootTestHelper.ResolveGameFlow().CurrentWorld;
        Assert.That(gameplay.Resolve<Worlds>().TryGet(world, out LoadedWorld loaded), Is.True);
        startChunk = loaded.Chunks[ChunkCoords.ChunkOf(ChunkCoords.CellOf(world.ToLocal(player.transform.position)))];
    }

    /// <summary>
    /// Deactivates every mob, so none walks into the scene under test.
    /// </summary>
    private static void DisableMobs()
    {
        foreach (MobController mob in Object.FindObjectsByType<MobController>())
        {
            mob.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// The first prop of <paramref name="chunk"/> that fades and stands over three units tall: a broadleaf tree.
    /// </summary>
    private static PropFade TallTree(Scene chunk)
    {
        foreach (PropFade prop in SceneQuery.FindAll<PropFade>(chunk))
        {
            if (prop.Fades && prop.Cover.yMax > 3f)
            {
                return prop;
            }
        }

        Assert.Fail("The start chunk should hold a broadleaf tree.");
        return null;
    }

    /// <summary>
    /// Clicks <paramref name="goal"/> and waits until the player stands there, failing if it ever stands inside
    /// <paramref name="trunk"/>.
    /// </summary>
    private IEnumerator WalkTo(Vector2 goal, Bounds trunk)
    {
        commands.Click(goal, default);
        yield return null;
        float deadline = Time.realtimeSinceStartup + WalkTimeoutSeconds;
        while (player.CurrentOrder != PlayerOrderKind.Idle)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The walk should finish.");
            Vector2 at = player.transform.position;
            Assert.That(trunk.Contains(new Vector3(at.x, at.y, trunk.center.z)), Is.False, "The player walks around the trunk, never through it.");
            yield return null;
        }

        Assert.That(Vector2.Distance(player.transform.position, goal), Is.LessThan(0.25f), "The player should arrive.");
    }

    /// <summary>
    /// Plants <see cref="ForestSize"/> squared copies of <paramref name="tree"/> around <paramref name="middle"/>, a
    /// spacing apart, in the start chunk's scene, and returns their fades.
    /// </summary>
    private PropFade[] PlantForest(PropFade tree, Vector2 middle)
    {
        GameObject root = new("TestForest");
        SceneManager.MoveGameObjectToScene(root, startChunk);
        List<PropFade> forest = new();
        float half = (ForestSize - 1) * ForestSpacing * 0.5f;
        for (int x = 0; x < ForestSize; x++)
        {
            for (int y = 0; y < ForestSize; y++)
            {
                Vector2 at = middle + new Vector2(x * ForestSpacing - half, y * ForestSpacing - half);
                forest.Add(Object.Instantiate(tree.gameObject, at, Quaternion.identity, root.transform).GetComponent<PropFade>());
            }
        }

        return forest.ToArray();
    }

    /// <summary>
    /// A spot just behind each of the first trees of <paramref name="forest"/>'s middle row.
    /// </summary>
    private static Vector2[] SpotsBehind(PropFade[] forest)
    {
        Vector2[] spots = new Vector2[8];
        int row = ForestSize / 2;
        for (int i = 0; i < spots.Length; i++)
        {
            spots[i] = (Vector2)forest[(row + i) * ForestSize + row].transform.position + new Vector2(0.4f, 1f);
        }

        return spots;
    }

    /// <summary>
    /// For <paramref name="frames"/> frames, stands the player behind one tree after another, every twenty frames.
    /// </summary>
    private IEnumerator Wander(Vector2[] spots, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            if (i % 20 == 0)
            {
                Teleport(player, spots[i / 20 % spots.Length]);
            }

            yield return null;
        }
    }

    /// <summary>
    /// Waits <paramref name="frames"/> frames.
    /// </summary>
    private static IEnumerator WaitFrames(int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            yield return null;
        }
    }

    /// <summary>
    /// Stands <paramref name="unit"/> at <paramref name="position"/> at once.
    /// </summary>
    private static void Teleport(PlayerController unit, Vector2 position)
    {
        Rigidbody2D body = unit.GetComponent<Rigidbody2D>();
        body.position = position;
        unit.transform.position = position;
        Physics2D.SyncTransforms();
    }

    /// <summary>
    /// A command source the test drives: a queued move click, read once.
    /// </summary>
    private sealed class ScriptedCommands : IPlayerCommandSource
    {
        private PlayerCommand next;

        /// <summary>
        /// Queues a single move click at <paramref name="point"/> on <paramref name="target"/> for the next read.
        /// </summary>
        public void Click(Vector2 point, UnitTarget target)
        {
            next = new PlayerCommand(point, target, movePressed: true, moveHeld: false, stopPressed: false);
        }

        /// <summary>
        /// Returns the queued command and clears it, so each click is read once; an empty command otherwise.
        /// </summary>
        public PlayerCommand ReadCommand()
        {
            PlayerCommand command = next;
            next = default;
            return command;
        }
    }
}
