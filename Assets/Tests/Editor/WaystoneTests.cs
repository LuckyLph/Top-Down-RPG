using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for waystones (Docs/WorldStreaming.md: Death and respawn) on worlds built in memory: the discovery rules
/// (<see cref="WaystoneRules"/>, <see cref="WaystoneDiscovery"/>), the nearest-waystone choice, the discoveries kept per
/// player id in the <see cref="SessionSave"/> and round-tripping through the save, <see cref="PlayerIds"/>, the place a
/// dead player is saved at, and what this machine's player discovered (<see cref="KnownWaystones"/>) as the waystones'
/// markers show it (<see cref="WaystoneMarkers"/>, Docs/KnownGaps.md, Phase 3).
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

        MemorySaveFolder folder = new();
        Assert.That(new GameSave(folder, new ManualWallClock()).Write(session.ToData()), Is.True);
        Assert.That(folder.Contents, Does.Contain("waystones"));
        Assert.That(new GameSave(folder, new ManualWallClock()).TryLoadLatest(out GameSaveData data), Is.True);
        Assert.That(data.Version, Is.EqualTo(GameSaveData.CurrentVersion));

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
            Assert.That(link.Worlds, Is.EqualTo(new[] { world.WorldId }), "The link names the waystone's world.");

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

    /// <summary>
    /// What this machine's player discovered (Docs/KnownGaps.md, Phase 3): where game state is decided, the session's
    /// save under this install's player id, the default entry counting from the start, each discovery raising
    /// <see cref="KnownWaystones.Changed"/>; on a client, the save is not read but the list the host replicates, kept
    /// once each and only for the world it names, each list raising the change and no notice doing so.
    /// </summary>
    [Test]
    public void KnownWaystones_AreTheSavesWhereStateIsDecided_AndTheHostsListOnAClient()
    {
        FakePlayerIdSource ids = new() { LocalPlayerId = Alice };
        SessionSave session = new(new ManualClock());
        WaystoneNotices notices = new();
        KnownWaystones host = new(FixedGameAuthority.Authoritative, ids, session, notices);
        KnownWaystones client = new(new FixedGameAuthority(false), ids, session, notices);
        int hostChanges = 0;
        int clientChanges = 0;
        host.Changed += () => hostChanges++;
        client.Changed += () => clientChanges++;
        try
        {
            Assert.That(host.IsDiscovered(world, world.FindEntry("start")), Is.True, "The default entry counts from the start.");
            Assert.That(host.IsDiscovered(world, world.FindEntry("east")), Is.False);

            session.Discover(Alice, world.WorldId, "east");
            session.Discover(Bob, world.WorldId, "west");
            notices.Raise(new PlayerHandle(Track(new GameObject("Alice")).transform, null), "east");
            Assert.That(host.IsDiscovered(world, world.FindEntry("east")), Is.True);
            Assert.That(host.IsDiscovered(world, world.FindEntry("west")), Is.False, "Another player's discovery is not this machine's.");
            Assert.That(hostChanges, Is.EqualTo(1));

            Assert.That(client.IsDiscovered(world, world.FindEntry("east")), Is.False, "A client does not read the save.");
            Assert.That(clientChanges, Is.Zero, "A notice changes nothing on a client: its list does.");
            client.Replicate(new[]
            {
                new GameSaveData.Waystone(world.WorldId, "west"),
                new GameSaveData.Waystone(world.WorldId, "west"),
                new GameSaveData.Waystone(other.WorldId, "east")
            });
            Assert.That(clientChanges, Is.EqualTo(1));
            Assert.That(client.Discovered, Has.Count.EqualTo(2), "Each once.");
            Assert.That(client.IsDiscovered(world, world.FindEntry("west")), Is.True);
            Assert.That(client.IsDiscovered(world, world.FindEntry("east")), Is.False, "The other world's `east` is not this one's.");
            Assert.That(client.IsDiscovered(other, other.FindEntry("east")), Is.True);
        }
        finally
        {
            host.Dispose();
            client.Dispose();
        }
    }

    /// <summary>
    /// The markers of a loaded chunk show what this machine's player discovered: a waystone not yet discovered sleeps,
    /// its light inactive, and wakes, its awake sprite shown and its light let burn, once discovered; the default entry's
    /// is awake from the start; one naming a plain entry or an entry the world lacks sleeps. A chunk unloading takes its
    /// markers out.
    /// </summary>
    [Test]
    public void WaystoneMarkers_ShowWhatThisMachinesPlayerDiscovered()
    {
        FakePlayerIdSource ids = new() { LocalPlayerId = Alice };
        SessionSave session = new(new ManualClock());
        WaystoneNotices notices = new();
        KnownWaystones known = new(FixedGameAuthority.Authoritative, ids, session, notices);
        Worlds worlds = new();
        WaystoneMarkers markers = new(worlds, known);
        WaystoneMarker east = Marker("east");
        WaystoneMarker start = Marker("start");
        WaystoneMarker door = Marker("door");
        WaystoneMarker gone = Marker("gone");
        LoadedWorld loaded = new(world, east.gameObject.scene, null, null);
        markers.Start();
        try
        {
            worlds.Add(loaded);
            worlds.AddChunk(loaded, Vector2Int.zero, east.gameObject.scene);
            Assert.That(markers.Count, Is.EqualTo(4));
            Assert.That(east.IsAwake, Is.False);
            Assert.That(east.Glow.activeSelf, Is.False, "Its light is out until it is discovered.");
            Assert.That(start.IsAwake, Is.True, "The default entry counts as discovered.");
            Assert.That(door.IsAwake, Is.False, "A plain entry is no waystone.");
            Assert.That(gone.IsAwake, Is.False);

            session.Discover(Alice, world.WorldId, "east");
            notices.Raise(new PlayerHandle(Track(new GameObject("Alice")).transform, null), "east");
            Assert.That(east.IsAwake, Is.True);
            Assert.That(east.Visuals.sprite, Is.EqualTo(east.AwakeSprite));
            Assert.That(east.Glow.activeSelf, Is.True, "Discovered, its light may burn.");
            Assert.That(markers.ShowsAwake(world, "east"), Is.True);

            worlds.RemoveChunk(loaded, Vector2Int.zero);
            Assert.That(markers.Count, Is.Zero, "An unloading chunk takes its markers out.");
        }
        finally
        {
            markers.Dispose();
            known.Dispose();
        }
    }

    /// <summary>
    /// A waystone's marker as the prop writer makes it: a renderer, a sleeping and an awake sprite and an inactive light,
    /// naming <paramref name="entry"/>.
    /// </summary>
    private WaystoneMarker Marker(string entry)
    {
        GameObject root = Track(new GameObject($"Waystone_{entry}"));
        GameObject visuals = new("Visuals");
        visuals.transform.SetParent(root.transform, false);
        SpriteRenderer renderer = visuals.AddComponent<SpriteRenderer>();
        GameObject light = new("Light");
        light.transform.SetParent(root.transform, false);
        light.SetActive(false);
        Texture2D texture = Track(new Texture2D(4, 4));
        Sprite dormant = Track(Sprite.Create(texture, new Rect(0, 0, 2, 4), Vector2.zero));
        Sprite awake = Track(Sprite.Create(texture, new Rect(2, 0, 2, 4), Vector2.zero));
        renderer.sprite = dormant;
        WaystoneMarker marker = root.AddComponent<WaystoneMarker>();
        marker.Configure(renderer, dormant, awake, light);
        marker.SetEntry(entry);
        return marker;
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
        public List<string> Worlds { get; } = new();

        public void SendWaystoneDiscovered(string worldId, string entryName)
        {
            Worlds.Add(worldId);
            Sent.Add(entryName);
        }
    }
}
