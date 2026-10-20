using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="ChunkStreamer"/>: worlds and chunks load around the players nearest first, one action at a
/// time, unload past the hysteresis, an empty world waits out its grace before going, and a chunk another machine holds
/// never unloads until released.
/// </summary>
public class ChunkStreamerTests
{
    private static readonly Vector2Int View = new(20, 12);
    private const int LoadMargin = 16;
    private const int Hysteresis = 8;
    private const float Grace = 10f;

    private readonly List<Object> created = new();

    [TearDown]
    public void TearDown()
    {
        foreach (Object made in created)
        {
            Object.DestroyImmediate(made);
        }

        created.Clear();
    }

    /// <summary>
    /// A player's world loads first, then the chunks within the view plus the load margin of it, nearest first, and
    /// nothing more.
    /// </summary>
    [Test]
    public void LoadsTheWorld_ThenTheNearestChunksAroundAPlayer_AndNothingElse()
    {
        WorldDefinition world = World("A", Vector2Int.zero, 4);
        ChunkStreamer streamer = Streamer();

        streamer.SetInterests(new[] { new StreamInterest(world, new Vector2(60.5f, 30.5f)) }, 0f);

        Assert.That(Drain(streamer), Is.EqualTo(new[] { "LoadWorld A", "LoadChunk A (0, 0)", "LoadChunk A (1, 0)" }));
    }

    /// <summary>
    /// Positions count from the world's origin, so a world far out in the layout streams the same chunks.
    /// </summary>
    [Test]
    public void CountsCellsFromTheWorldsOrigin()
    {
        WorldDefinition world = World("A", new Vector2Int(2048, 4096), 4);
        ChunkStreamer streamer = Streamer();

        streamer.SetInterests(new[] { new StreamInterest(world, new Vector2(2048 + 100.5f, 4096 + 100.5f)) }, 0f);

        Assert.That(Drain(streamer), Is.EqualTo(new[] { "LoadWorld A", "LoadChunk A (1, 1)" }));
    }

    /// <summary>
    /// A chunk stays while a player is within the hysteresis beyond the load margin of it, and unloads once the player
    /// goes further.
    /// </summary>
    [Test]
    public void UnloadsAChunk_OnlyPastTheHysteresis()
    {
        WorldDefinition world = World("A", Vector2Int.zero, 4);
        ChunkStreamer streamer = Streamer();
        streamer.SetInterests(new[] { new StreamInterest(world, new Vector2(70.5f, 30.5f)) }, 0f);
        Drain(streamer);

        streamer.SetInterests(new[] { new StreamInterest(world, new Vector2(95.5f, 30.5f)) }, 1f);
        Assert.That(Drain(streamer), Is.Empty, "Chunk (0, 0) is 32 cells away, within the 34 the view, margin and hysteresis keep.");
        Assert.That(streamer.IsChunkLoaded(world, new Vector2Int(0, 0)), Is.True);

        streamer.SetInterests(new[] { new StreamInterest(world, new Vector2(100.5f, 30.5f)) }, 2f);
        Assert.That(Drain(streamer), Is.EqualTo(new[] { "UnloadChunk A (0, 0)" }));
    }

    /// <summary>
    /// A chunk the world has no scene for is never loaded.
    /// </summary>
    [Test]
    public void SkipsChunksTheWorldHasNoSceneFor()
    {
        WorldDefinition world = WorldWith("A", Vector2Int.zero, new Vector2Int(2, 2), new Vector2Int(0, 0));
        ChunkStreamer streamer = Streamer();

        streamer.SetInterests(new[] { new StreamInterest(world, new Vector2(63.5f, 63.5f)) }, 0f);

        Assert.That(Drain(streamer), Is.EqualTo(new[] { "LoadWorld A", "LoadChunk A (0, 0)" }));
    }

    /// <summary>
    /// Players in two worlds load both worlds and the chunks around each.
    /// </summary>
    [Test]
    public void LoadsEveryWorldAPlayerIsIn()
    {
        WorldDefinition a = World("A", Vector2Int.zero, 2);
        WorldDefinition b = World("B", new Vector2Int(2048, 0), 2);
        ChunkStreamer streamer = Streamer();

        streamer.SetInterests(new[] { new StreamInterest(a, new Vector2(30.5f, 30.5f)), new StreamInterest(b, new Vector2(2048 + 100.5f, 100.5f)) }, 0f);

        Assert.That(Drain(streamer), Is.EquivalentTo(new[] { "LoadWorld A", "LoadWorld B", "LoadChunk A (0, 0)", "LoadChunk B (1, 1)" }));
    }

    /// <summary>
    /// A world its last player left keeps itself and its chunks for the grace, is reused when a player comes back
    /// within it, and otherwise unloads its chunks and then itself.
    /// </summary>
    [Test]
    public void KeepsAnEmptyWorldForTheGrace_ThenUnloadsItsChunksThenItself()
    {
        WorldDefinition a = World("A", Vector2Int.zero, 2);
        WorldDefinition b = World("B", new Vector2Int(2048, 0), 2);
        ChunkStreamer streamer = Streamer();
        streamer.SetInterests(new[] { new StreamInterest(a, new Vector2(30.5f, 30.5f)) }, 0f);
        Drain(streamer);

        streamer.SetInterests(new[] { new StreamInterest(b, new Vector2(2048 + 30.5f, 30.5f)) }, 5f);
        Assert.That(Drain(streamer), Is.EqualTo(new[] { "LoadWorld B", "LoadChunk B (0, 0)" }));
        streamer.SetInterests(new[] { new StreamInterest(b, new Vector2(2048 + 30.5f, 30.5f)) }, 14.9f);
        Assert.That(Drain(streamer), Is.Empty, "A is kept for its grace.");

        streamer.SetInterests(new[] { new StreamInterest(a, new Vector2(30.5f, 30.5f)) }, 14.9f);
        Assert.That(Drain(streamer), Is.Empty, "A came back within its grace and B starts its own.");

        streamer.SetInterests(new[] { new StreamInterest(a, new Vector2(30.5f, 30.5f)) }, 25f);
        Assert.That(Drain(streamer), Is.EqualTo(new[] { "UnloadChunk B (0, 0)", "UnloadWorld B" }));
        Assert.That(streamer.IsWorldLoaded(b), Is.False);
        Assert.That(streamer.IsWorldLoaded(a), Is.True);
    }

    /// <summary>
    /// Only one action runs at a time: no next action until the running one completes.
    /// </summary>
    [Test]
    public void RunsOneActionAtATime()
    {
        WorldDefinition world = World("A", Vector2Int.zero, 2);
        ChunkStreamer streamer = Streamer();
        streamer.SetInterests(new[] { new StreamInterest(world, new Vector2(30.5f, 30.5f)) }, 0f);

        Assert.That(streamer.TryNext(out StreamAction first), Is.True);
        Assert.That(streamer.IsBusy, Is.True);
        Assert.That(streamer.TryNext(out _), Is.False);
        Assert.That(streamer.IsWorldLoaded(world), Is.False, "A load counts once it completes.");

        streamer.Complete(true);
        Assert.That(first.Kind, Is.EqualTo(StreamActionKind.LoadWorld));
        Assert.That(streamer.IsWorldLoaded(world), Is.True);
        Assert.That(streamer.TryNext(out _), Is.True);
    }

    /// <summary>
    /// Releasing everything unloads every chunk before its world, whatever the players' positions, and leaves the
    /// streamer empty; resuming streams again.
    /// </summary>
    [Test]
    public void ReleaseAll_UnloadsChunksBeforeWorlds_AndEndsEmpty()
    {
        WorldDefinition world = World("A", Vector2Int.zero, 2);
        ChunkStreamer streamer = Streamer();
        StreamInterest[] interests = { new(world, new Vector2(60.5f, 30.5f)) };
        streamer.SetInterests(interests, 0f);
        Drain(streamer);

        streamer.ReleaseAll();
        streamer.SetInterests(interests, 1f);

        Assert.That(Drain(streamer), Is.EqualTo(new[] { "UnloadChunk A (0, 0)", "UnloadChunk A (1, 0)", "UnloadWorld A" }));
        Assert.That(streamer.IsEmpty, Is.True);

        streamer.Resume();
        streamer.SetInterests(interests, 2f);
        Assert.That(Drain(streamer).First(), Is.EqualTo("LoadWorld A"));
    }

    /// <summary>
    /// A chunk that failed to load is not tried again, and counts as done for <see cref="ChunkStreamer.IsSettled"/>,
    /// which holds only once the world and every chunk around the player are loaded.
    /// </summary>
    [Test]
    public void IsSettled_OnceTheWorldAndTheChunksAroundAreDone_FailedOnesIncluded()
    {
        WorldDefinition world = World("A", Vector2Int.zero, 2);
        ChunkStreamer streamer = Streamer();
        StreamInterest player = new(world, new Vector2(60.5f, 30.5f));
        streamer.SetInterests(new[] { player }, 0f);
        Assert.That(streamer.IsSettled(player), Is.False);

        streamer.TryNext(out _);
        streamer.Complete(true);
        Assert.That(streamer.IsSettled(player), Is.False, "The world is up but not its chunks.");

        streamer.TryNext(out StreamAction chunk);
        streamer.Complete(false);
        Assert.That(chunk.Chunk, Is.EqualTo(new Vector2Int(0, 0)));
        Assert.That(Drain(streamer), Is.EqualTo(new[] { "LoadChunk A (1, 0)" }), "The failed chunk is not tried again.");
        Assert.That(streamer.IsSettled(player), Is.True);
        Assert.That(streamer.IsChunkLoaded(world, new Vector2Int(0, 0)), Is.False);
    }

    /// <summary>
    /// The host never unloads a chunk a client holds: not when no player keeps it, and not with its world past the
    /// grace, which waits for it; once released it goes, and then its world. Letting everything go, as the session
    /// ends, ignores the holds.
    /// </summary>
    [Test]
    public void NeverUnloadsAHeldChunk_OrItsWorld_UntilItIsReleased()
    {
        WorldDefinition a = World("A", Vector2Int.zero, 4);
        WorldDefinition b = World("B", new Vector2Int(2048, 0), 2);
        FakeHolds holds = new();
        ChunkStreamer streamer = new(View, LoadMargin, Hysteresis, Grace, holds);
        streamer.SetInterests(new[] { new StreamInterest(a, new Vector2(70.5f, 30.5f)) }, 0f);
        Drain(streamer);
        holds.Held.Add(ChunkId.Of(a, new Vector2Int(0, 0)));

        streamer.SetInterests(new[] { new StreamInterest(a, new Vector2(100.5f, 30.5f)) }, 1f);
        Assert.That(Drain(streamer), Is.Empty, "A held chunk stays though no player keeps it.");

        streamer.SetInterests(new[] { new StreamInterest(b, new Vector2(2048 + 30.5f, 30.5f)) }, 2f);
        Drain(streamer);
        streamer.SetInterests(new[] { new StreamInterest(b, new Vector2(2048 + 30.5f, 30.5f)) }, 30f);
        Assert.That(Drain(streamer), Is.EqualTo(new[] { "UnloadChunk A (1, 0)" }), "Past the grace only the chunk nobody holds goes.");
        Assert.That(streamer.IsWorldLoaded(a), Is.True, "The world waits for its held chunk.");

        holds.Held.Clear();
        Assert.That(Drain(streamer), Is.EqualTo(new[] { "UnloadChunk A (0, 0)", "UnloadWorld A" }));

        holds.Held.Add(ChunkId.Of(b, new Vector2Int(0, 0)));
        streamer.ReleaseAll();
        Assert.That(Drain(streamer), Is.EqualTo(new[] { "UnloadChunk B (0, 0)", "UnloadWorld B" }), "Ending the session ignores the holds.");
    }

    private static ChunkStreamer Streamer()
    {
        return new ChunkStreamer(View, LoadMargin, Hysteresis, Grace);
    }

    /// <summary>
    /// Chunks another machine holds, as the test sets them.
    /// </summary>
    private sealed class FakeHolds : IChunkHolds
    {
        public HashSet<ChunkId> Held { get; } = new();

        /// <summary>
        /// Whether the test holds <paramref name="chunk"/>.
        /// </summary>
        public bool IsChunkHeld(ChunkId chunk)
        {
            return Held.Contains(chunk);
        }
    }

    /// <summary>
    /// Runs every action the streamer asks for, completing each, and returns them as text.
    /// </summary>
    private static List<string> Drain(ChunkStreamer streamer)
    {
        List<string> actions = new();
        for (int i = 0; i < 100 && streamer.TryNext(out StreamAction action); i++)
        {
            actions.Add(action.ToString());
            streamer.Complete(true);
        }

        return actions;
    }

    private WorldDefinition World(string worldName, Vector2Int origin, int side)
    {
        List<Vector2Int> chunks = new();
        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                chunks.Add(new Vector2Int(x, y));
            }
        }

        return WorldWith(worldName, origin, new Vector2Int(side, side), chunks.ToArray());
    }

    private WorldDefinition WorldWith(string worldName, Vector2Int origin, Vector2Int size, params Vector2Int[] chunks)
    {
        WorldDefinition world = WorldDefinition.Create(worldName, origin, size, chunks);
        created.Add(world);
        created.Add(world.RootScene);
        foreach (WorldDefinition.Chunk chunk in world.Chunks)
        {
            created.Add(chunk.Scene);
        }

        return world;
    }
}
