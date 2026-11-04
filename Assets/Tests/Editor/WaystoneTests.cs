using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for waystones (Docs/WorldStreaming.md: Death and respawn) on worlds built in memory: the discovery rules
/// (<see cref="WaystoneRules"/>, <see cref="WaystoneDiscovery"/>), the nearest-waystone choice, the discoveries kept per
/// player id in the <see cref="SessionSave"/> and round-tripping through the save, <see cref="PlayerIds"/>, and the
/// place a dead player is saved at.
/// </summary>
public class WaystoneTests
{
    private const string Alice = "alice-id";
    private const string Bob = "bob-id";

    private readonly List<Object> created = new();
    private WorldDefinition world;
    private WorldDefinition other;

    /// <summary>
    /// Builds a world at (2048, 0) of 2 x 1 chunks whose entries are the default `start`, the waystones `east` and
    /// `west` (equally far from the middle between them), a plain entry `door` and the waystone `far`, and a second world
    /// with a waystone named `east` too.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        world = Track(WorldDefinition.Create(
            "Waystones",
            new Vector2Int(2048, 0),
            new Vector2Int(2, 1),
            new[] { Vector2Int.zero, Vector2Int.right },
            new WorldDefinition.Entry("start", new Vector2(10.5f, 10.5f), true),
            new WorldDefinition.Entry("west", new Vector2(40.5f, 30.5f), true),
            new WorldDefinition.Entry("east", new Vector2(60.5f, 30.5f), true),
            new WorldDefinition.Entry("door", new Vector2(50.5f, 31.5f), false),
            new WorldDefinition.Entry("far", new Vector2(120.5f, 60.5f), true)));
        other = Track(WorldDefinition.Create("OtherWaystones", new Vector2Int(4096, 0), Vector2Int.one, new[] { Vector2Int.zero },
            new WorldDefinition.Entry("start", new Vector2(5.5f, 5.5f), true),
            new WorldDefinition.Entry("east", new Vector2(30.5f, 30.5f), true)));
    }

    /// <summary>
    /// Destroys what the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        for (int i = created.Count - 1; i >= 0; i--)
        {
            if (created[i] != null)
            {
                Object.DestroyImmediate(created[i]);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// The default entry counts as discovered from the start; a waystone only once the list names it in its own world;
    /// a plain entry never.
    /// </summary>
    [Test]
    public void TheDefaultEntry_IsDiscoveredFromTheStart_AndAWaystoneOnceListed()
    {
        List<GameSaveData.Waystone> discovered = new() { new GameSaveData.Waystone(other.WorldId, "east") };

        Assert.That(WaystoneRules.IsDiscovered(world, world.FindEntry("start"), discovered), Is.True);
        Assert.That(WaystoneRules.IsDiscovered(world, world.FindEntry("east"), discovered), Is.False, "Another world's `east` is not this one's.");
        Assert.That(WaystoneRules.IsDiscovered(other, other.FindEntry("east"), discovered), Is.True);

        discovered.Add(new GameSaveData.Waystone(world.WorldId, "east"));
        discovered.Add(new GameSaveData.Waystone(world.WorldId, "door"));
        Assert.That(WaystoneRules.IsDiscovered(world, world.FindEntry("east"), discovered), Is.True);
        Assert.That(WaystoneRules.IsDiscovered(world, world.FindEntry("door"), discovered), Is.False, "A plain entry is no waystone.");
    }

    /// <summary>
    /// A waystone is discovered from up to two cells away along each axis, and not from three; one already discovered,
    /// the default entry and a plain entry are not discovered again.
    /// </summary>
    [Test]
    public void AWaystone_IsDiscoveredWithinItsReach_Once()
    {
        List<GameSaveData.Waystone> discovered = new();
        Vector2 east = world.ToWorld(new Vector2(60.5f, 30.5f));

        Assert.That(WaystoneRules.TryFindDiscovery(world, east + new Vector2(2f, -2f), 2, discovered, out WorldDefinition.Entry found), Is.True);
        Assert.That(found.Name, Is.EqualTo("east"));
        Assert.That(WaystoneRules.TryFindDiscovery(world, east + new Vector2(3f, 0f), 2, discovered, out _), Is.False);
        Assert.That(WaystoneRules.TryFindDiscovery(world, world.ToWorld(new Vector2(50.5f, 31.5f)), 0, discovered, out _), Is.False, "The plain entry `door` is never discovered.");
        Assert.That(WaystoneRules.TryFindDiscovery(world, world.ToWorld(new Vector2(10.5f, 10.5f)), 2, discovered, out _), Is.False, "The default entry is discovered already.");

        discovered.Add(new GameSaveData.Waystone(world.WorldId, "east"));
        Assert.That(WaystoneRules.TryFindDiscovery(world, east, 2, discovered, out _), Is.False);
    }

    /// <summary>
    /// A dead player respawns at the default entry while it discovered nothing nearer; at the nearest discovered
    /// waystone of its own world otherwise, in a straight line; a tie goes to the entry listed first; a world without
    /// entries has none.
    /// </summary>
    [Test]
    public void TheNearestDiscoveredWaystone_OfThePlayersWorld_IsChosen_TheFirstListedOnATie()
    {
        List<GameSaveData.Waystone> discovered = new() { new GameSaveData.Waystone(other.WorldId, "east") };
        Vector2 middle = world.ToWorld(new Vector2(50.5f, 30.5f));

        Assert.That(WaystoneRules.TryFindNearest(world, middle, discovered, out WorldDefinition.Entry nearest), Is.True);
        Assert.That(nearest.Name, Is.EqualTo("start"), "Only the default entry counts until one of this world's is discovered.");

        discovered.Add(new GameSaveData.Waystone(world.WorldId, "east"));
        discovered.Add(new GameSaveData.Waystone(world.WorldId, "far"));
        Assert.That(WaystoneRules.TryFindNearest(world, middle, discovered, out nearest), Is.True);
        Assert.That(nearest.Name, Is.EqualTo("east"));

        discovered.Add(new GameSaveData.Waystone(world.WorldId, "west"));
        Assert.That(WaystoneRules.TryFindNearest(world, middle, discovered, out nearest), Is.True);
        Assert.That(nearest.Name, Is.EqualTo("west"), "`west` and `east` are as far; `west` is listed first.");

        Assert.That(WaystoneRules.TryFindNearest(world, world.ToWorld(new Vector2(110f, 50f)), discovered, out nearest), Is.True);
        Assert.That(nearest.Name, Is.EqualTo("far"));

        WorldDefinition empty = Track(WorldDefinition.Create("NoEntries", new Vector2Int(6144, 0), Vector2Int.one, new[] { Vector2Int.zero }));
        Assert.That(WaystoneRules.TryFindNearest(empty, new Vector2(6150f, 10f), discovered, out nearest), Is.False);
        Assert.That(nearest, Is.Null);
    }

    /// <summary>
    /// Discoveries are kept per player id, once each; they survive a new place and round-trip through the save file,
    /// keyed by player id, in the same format version.
    /// </summary>
    [Test]
    public void Discoveries_AreKeptPerPlayerId_AndRoundTripThroughTheSave()
    {
        SessionSave session = new(new ManualClock());
        session.StartNew();
        Assert.That(session.Discover(Alice, world.WorldId, "east"), Is.True);
        Assert.That(session.Discover(Alice, world.WorldId, "east"), Is.False, "A discovery is recorded once.");
        Assert.That(session.Discover(Alice, other.WorldId, "east"), Is.True, "Another world's waystone of the same name is another one.");
        Assert.That(session.Discover(Bob, world.WorldId, "far"), Is.True);
        Assert.That(session.Discover(string.Empty, world.WorldId, "far"), Is.False);
        Assert.That(session.Discover(Bob, world.WorldId, string.Empty), Is.False);
        session.SetPlace(Alice, world.WorldId, world.ToWorld(new Vector2(60.5f, 30.5f)));
        session.SetPlace(Bob, other.WorldId, other.ToWorld(new Vector2(5.5f, 5.5f)));
        Assert.That(session.WaystonesOf(Alice), Has.Count.EqualTo(2), "A new place keeps the discoveries.");

        MemorySaveStore store = new();
        Assert.That(new GameSave(store).Write(session.ToData()), Is.True);
        Assert.That(store.Contents, Does.Contain("waystones"));
        Assert.That(new GameSave(store).TryLoad(out GameSaveData data), Is.True);
        Assert.That(data.Version, Is.EqualTo(GameSaveData.CurrentVersion));
        Assert.That(GameSaveData.CurrentVersion, Is.EqualTo(3), "Waystones needed no format bump; the saved weapon raised it to 3.");

        SessionSave loaded = new(new ManualClock());
        loaded.StartFrom(data);
        Assert.That(WaystoneRules.Contains(loaded.WaystonesOf(Alice), world.WorldId, "east"), Is.True);
        Assert.That(WaystoneRules.Contains(loaded.WaystonesOf(Alice), other.WorldId, "east"), Is.True);
        Assert.That(WaystoneRules.Contains(loaded.WaystonesOf(Alice), world.WorldId, "far"), Is.False);
        Assert.That(loaded.WaystonesOf(Bob), Has.Count.EqualTo(1));
        Assert.That(loaded.WaystonesOf("carol-id"), Is.Empty);

        loaded.StartNew();
        Assert.That(loaded.WaystonesOf(Alice), Is.Empty, "A new game forgets them.");
    }

    /// <summary>
    /// A living player walking within reach of a waystone discovers it once, recorded under its player id, raised on the
    /// notices and sent to its machine; a dead player, a player in no world and a machine that is not the authority
    /// discover nothing.
    /// </summary>
    [Test]
    public void WalkingToAWaystone_DiscoversIt_OnTheAuthorityOnly()
    {
        PlayerRegistry players = new();
        PlayerWorlds playerWorlds = new(players);
        FakePlayerIdSource ids = new() { LocalPlayerId = Alice };
        PlayerIds playerIds = new(players, ids, new FakeSessionStatus());
        SessionSave session = new(new ManualClock());
        WaystoneNotices notices = new();
        List<string> told = new();
        notices.Discovered += (player, entry) => told.Add(entry);
        GameObject body = Track(new GameObject("Player"));
        Health health = body.AddComponent<Health>();
        PlayerHandle player = new(body.transform, health);
        players.Add(player);
        WaystoneDiscovery discovery = new(players, playerWorlds, playerIds, session, FixedGameAuthority.Authoritative, notices, Track(GameplaySettings.Create()));
        RecordingLink link = new();
        discovery.LinkFinder = found => found == body.transform ? link : null;
        discovery.Start();
        try
        {
            body.transform.position = world.ToWorld(new Vector2(58.5f, 30.5f));
            discovery.Tick();
            Assert.That(session.WaystonesOf(Alice), Is.Empty, "A player in no world discovers nothing.");

            playerWorlds.Set(player, world);
            discovery.Tick();
            Assert.That(WaystoneRules.Contains(session.WaystonesOf(Alice), world.WorldId, "east"), Is.True);
            Assert.That(told, Is.EqualTo(new[] { "east" }));
            Assert.That(link.Sent, Is.EqualTo(new[] { "east" }));

            body.transform.position = world.ToWorld(new Vector2(60.5f, 30.5f));
            discovery.Tick();
            Assert.That(told, Has.Count.EqualTo(1), "A waystone is discovered once.");

            health.ApplyDamage(health.MaxHealth);
            body.transform.position = world.ToWorld(new Vector2(40.5f, 30.5f));
            discovery.Tick();
            Assert.That(WaystoneRules.Contains(session.WaystonesOf(Alice), world.WorldId, "west"), Is.False, "A dead player discovers nothing.");

            health.Restore();
            WaystoneDiscovery follower = new(players, playerWorlds, playerIds, session, new FixedGameAuthority(false), notices, Track(GameplaySettings.Create()));
            follower.Tick();
            Assert.That(WaystoneRules.Contains(session.WaystonesOf(Alice), world.WorldId, "west"), Is.False, "A client discovers nothing itself.");

            discovery.Tick();
            Assert.That(WaystoneRules.Contains(session.WaystonesOf(Alice), world.WorldId, "west"), Is.True, "Alive again, it discovers the waystone it stands on.");
        }
        finally
        {
            discovery.Dispose();
            playerIds.Dispose();
            playerWorlds.Dispose();
        }
    }

    /// <summary>
    /// Offline every player's id is this install's; a player's id stays readable while the registry announces it
    /// leaving, and is forgotten once another player registers.
    /// </summary>
    [Test]
    public void PlayerIds_AreLearnedAsPlayersRegister_AndOutliveTheirRemovalNotice()
    {
        PlayerRegistry players = new();
        FakePlayerIdSource ids = new() { LocalPlayerId = Alice };
        PlayerHandle first = new(Track(new GameObject("First")).transform, null);
        players.Add(first);
        PlayerIds playerIds = new(players, ids, new FakeSessionStatus());
        string seenOnRemoval = null;
        players.PlayerRemoved += player => seenOnRemoval = playerIds.IdOf(player);
        try
        {
            Assert.That(playerIds.IdOf(first), Is.EqualTo(Alice), "A player registered before is learned too.");
            ids.LocalPlayerId = Bob;
            Assert.That(playerIds.IdOf(first), Is.EqualTo(Alice), "The id learned at registering stays.");

            players.Remove(first);
            Assert.That(seenOnRemoval, Is.EqualTo(Alice));

            PlayerHandle second = new(Track(new GameObject("Second")).transform, null);
            players.Add(second);
            Assert.That(playerIds.IdOf(second), Is.EqualTo(Bob));
            Assert.That(playerIds.IdOf(first), Is.EqualTo(Bob), "Forgotten, the first is looked up afresh.");
            Assert.That(playerIds.IdOf(null), Is.Empty);
        }
        finally
        {
            playerIds.Dispose();
        }
    }

    /// <summary>
    /// A dead player is saved at the waystone it will respawn at: the nearest it discovered, or the default entry.
    /// </summary>
    [Test]
    public void ADeadPlayer_IsSavedAtTheWaystoneItRespawnsAt()
    {
        Vector2 nearEast = world.ToWorld(new Vector2(70f, 30f));
        List<GameSaveData.Waystone> discovered = new();

        Assert.That(GameSaveRecorder.TryGetRespawnPlace(world, nearEast, discovered, out Vector2 place), Is.True);
        Assert.That(place, Is.EqualTo(world.ToWorld(new Vector2(10.5f, 10.5f))));

        discovered.Add(new GameSaveData.Waystone(world.WorldId, "east"));
        Assert.That(GameSaveRecorder.TryGetRespawnPlace(world, nearEast, discovered, out place), Is.True);
        Assert.That(place, Is.EqualTo(world.ToWorld(new Vector2(60.5f, 30.5f))));
    }

    private T Track<T>(T item) where T : Object
    {
        created.Add(item);
        return item;
    }

    /// <summary>
    /// A player body's <see cref="IWaystoneLink"/>: records the waystones the host told its owner of.
    /// </summary>
    private sealed class RecordingLink : IWaystoneLink
    {
        public List<string> Sent { get; } = new();

        public void SendWaystoneDiscovered(string entryName)
        {
            Sent.Add(entryName);
        }
    }
}
