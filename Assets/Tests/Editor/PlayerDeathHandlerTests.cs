using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tests for <see cref="PlayerDeathHandler"/> and <see cref="PlayerRespawner"/> on a world built in memory, with a
/// manual clock (Docs/WorldStreaming.md: Death and respawn): a dead player waits the respawn delay, travels to the
/// nearest waystone it discovered in its world on a trip within it, and is restored once it stands there with the
/// waystone's chunk loaded and ready; a trip it is on comes first; nothing restarts when every player is dead; a player
/// in no world comes back where it stands; and only the authority respawns anyone.
/// </summary>
public class PlayerDeathHandlerTests
{
    private const float Delay = 3f;

    private readonly List<Object> created = new();
    private ManualClock clock;
    private PlayerRegistry players;
    private PlayerWorlds playerWorlds;
    private Worlds worlds;
    private TripArrivals arrivals;
    private SessionSave session;
    private FakePlayerIdSource ids;
    private PlayerIds playerIds;
    private FakeClientReadiness readiness;
    private NetworkTripAuthority trips;
    private List<TripApproval> approved;
    private WorldDefinition world;
    private PlayerDeathHandler handler;

    /// <summary>
    /// Builds a listed world at (2048, 0) of 2 x 1 chunks with the default entry `start` and the waystone `far`, the
    /// Gameplay pieces a respawn goes through, and a started death handler on the authority.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        world = Track(WorldDefinition.Create(
            "Deaths",
            new Vector2Int(2048, 0),
            new Vector2Int(2, 1),
            new[] { Vector2Int.zero, Vector2Int.right },
            new WorldDefinition.Entry("start", new Vector2(10.5f, 10.5f), true),
            new WorldDefinition.Entry("far", new Vector2(110.5f, 30.5f), true)));
        GameScenes gameScenes = Track(ScriptableObject.CreateInstance<GameScenes>());
        SerializedObject data = new(gameScenes);
        SerializedProperty listed = data.FindProperty("worlds");
        listed.arraySize = 1;
        listed.GetArrayElementAtIndex(0).objectReferenceValue = world;
        data.ApplyModifiedPropertiesWithoutUndo();

        clock = new ManualClock();
        players = new PlayerRegistry();
        playerWorlds = new PlayerWorlds(players);
        worlds = new Worlds();
        arrivals = new TripArrivals();
        session = new SessionSave(clock);
        ids = new FakePlayerIdSource();
        playerIds = new PlayerIds(players, ids, new FakeSessionStatus());
        readiness = new FakeClientReadiness();
        LocalTripAuthority local = new(new WorldExits(new Worlds()), playerWorlds, arrivals, new TripProtection(players, playerWorlds, clock, Track(GameplaySettings.Create())), gameScenes);
        trips = new NetworkTripAuthority(local, new FakeSessionStatus(), arrivals);
        approved = new List<TripApproval>();
        trips.Approved += approved.Add;
        PlayerRespawner respawner = new(players, playerWorlds, playerIds, session, trips, arrivals, worlds, readiness);
        handler = Handler(respawner, true);
    }

    /// <summary>
    /// Disposes what the test built and destroys what it created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        handler.Dispose();
        playerIds.Dispose();
        playerWorlds.Dispose();
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
    /// A dead player stays dead through the delay; then it travels to `start` within its world (one more trip, its
    /// arrival held so the entry streams, the approval raised for its machine), and stays dead until it stands there with
    /// the entry's chunk loaded and ready, when its health is restored where it stands.
    /// </summary>
    [Test]
    public void ADeadPlayer_TravelsToItsWaystoneAfterTheDelay_AndIsRestoredOnceItStandsThere()
    {
        PlayerHandle player = Player("Dies", new Vector2(40.5f, 20.5f));
        player.Health.ApplyDamage(player.Health.MaxHealth);

        clock.Time = Delay - 0.1f;
        handler.Tick();
        Assert.That(player.Health.IsDead, Is.True);
        Assert.That(approved, Is.Empty, "Nothing happens before the delay.");

        clock.Time = Delay;
        handler.Tick();
        Assert.That(approved, Has.Count.EqualTo(1));
        Assert.That(approved[0].World, Is.SameAs(world));
        Assert.That(approved[0].EntryName, Is.EqualTo("start"));
        Assert.That(playerWorlds.Get(player), Is.SameAs(world), "The respawn stays in the player's world.");
        Assert.That(playerWorlds.TripOf(player), Is.EqualTo(1), "A respawn is one more trip.");
        Assert.That(playerWorlds.EntryOf(player), Is.EqualTo("start"));
        Assert.That(arrivals.IsHeld(player), Is.True, "The waystone's chunks stream at once.");
        Assert.That(handler.IsTravellingToWaystone(player), Is.True);
        Assert.That(player.Health.IsDead, Is.True, "The player stays dead on its way.");

        player.Transform.position = world.ToWorld(new Vector2(11.1f, 10.5f));
        handler.Tick();
        Assert.That(player.Health.IsDead, Is.True, "The waystone's chunk is not loaded here yet.");

        LoadedWorld loaded = new(world, default, null, null);
        worlds.Add(loaded);
        worlds.AddChunk(loaded, Vector2Int.zero, default);
        readiness.NotReady.Add(ChunkId.Of(world, Vector2Int.zero));
        handler.Tick();
        Assert.That(player.Health.IsDead, Is.True, "The player's machine has not reported the waystone's chunk.");

        readiness.NotReady.Clear();
        handler.Tick();
        Assert.That(player.Health.IsDead, Is.False);
        Assert.That(player.Health.CurrentHealth, Is.EqualTo(player.Health.MaxHealth));
        Assert.That((Vector2)player.Transform.position, Is.EqualTo(world.ToWorld(new Vector2(11.1f, 10.5f))), "The restore moves nobody.");
        Assert.That(handler.PendingCount, Is.Zero);
    }

    /// <summary>
    /// A player that discovered `far` respawns there when it died nearer to it than to `start`; one that did not, at
    /// `start`, whatever its body's world position.
    /// </summary>
    [Test]
    public void ADeadPlayer_RespawnsAtTheNearestWaystoneItDiscovered()
    {
        ids.LocalPlayerId = "explorer";
        PlayerHandle explorer = Player("Explorer", new Vector2(100.5f, 30.5f));
        session.Discover("explorer", world.WorldId, "far");
        explorer.Health.ApplyDamage(explorer.Health.MaxHealth);

        ids.LocalPlayerId = "stayer";
        PlayerHandle stayer = Player("Stayer", new Vector2(100.5f, 32.5f));
        stayer.Health.ApplyDamage(stayer.Health.MaxHealth);

        clock.Time = Delay;
        handler.Tick();
        Assert.That(EntryOf(explorer), Is.EqualTo("far"));
        Assert.That(EntryOf(stayer), Is.EqualTo("start"), "Only what each player discovered counts.");
    }

    /// <summary>
    /// Every player dead at once restarts nothing: each travels to its own waystone after its own delay, and nothing
    /// else is asked of the game flow (no other approval than theirs).
    /// </summary>
    [Test]
    public void EveryPlayerDeadAtOnce_EachRespawnsOnItsOwn()
    {
        PlayerHandle first = Player("First", new Vector2(40.5f, 20.5f));
        PlayerHandle second = Player("Second", new Vector2(42.5f, 20.5f));
        first.Health.ApplyDamage(first.Health.MaxHealth);
        clock.Time = 1f;
        second.Health.ApplyDamage(second.Health.MaxHealth);
        Assert.That(players.AnyAlive, Is.False);

        clock.Time = Delay;
        handler.Tick();
        Assert.That(approved.ConvertAll(a => a.Player), Is.EqualTo(new[] { first }), "Each waits its own delay.");

        clock.Time = Delay + 1f;
        handler.Tick();
        Assert.That(approved.ConvertAll(a => a.Player), Is.EqualTo(new[] { first, second }));
        Assert.That(handler.PendingCount, Is.EqualTo(2), "Both travel to their waystone; nothing restarts.");
    }

    /// <summary>
    /// A player that dies on a trip waits for the trip to arrive before it travels to its waystone, measured from where
    /// it then stands.
    /// </summary>
    [Test]
    public void APlayerThatDiesOnATrip_RespawnsOnceTheTripArrived()
    {
        PlayerHandle player = Player("Traveller", new Vector2(40.5f, 20.5f));
        session.Discover(ids.LocalPlayerId, world.WorldId, "far");
        Vector2 tripEntry = world.ToWorld(new Vector2(100.5f, 20.5f));
        arrivals.Hold(player, new StreamInterest(world, tripEntry));
        player.Health.ApplyDamage(player.Health.MaxHealth);

        clock.Time = Delay + 5f;
        handler.Tick();
        Assert.That(approved, Is.Empty, "The trip under way comes first.");

        player.Transform.position = tripEntry;
        arrivals.Release(player);
        handler.Tick();
        Assert.That(EntryOf(player), Is.EqualTo("far"), "Measured from the trip's end.");
    }

    /// <summary>
    /// A dead player in no world comes back where it stands after the delay; one that leaves the registry is
    /// forgotten; on a machine that is not the authority nobody respawns.
    /// </summary>
    [Test]
    public void APlayerInNoWorld_ComesBackWhereItStands_AndOnlyTheAuthorityRespawns()
    {
        PlayerHandle lost = Player("Lost", new Vector2(40.5f, 20.5f));
        playerWorlds.Set(lost, null);
        PlayerHandle leaving = Player("Leaving", new Vector2(42.5f, 20.5f));
        lost.Health.ApplyDamage(lost.Health.MaxHealth);
        leaving.Health.ApplyDamage(leaving.Health.MaxHealth);
        players.Remove(leaving);

        clock.Time = Delay;
        handler.Tick();
        Assert.That(lost.Health.IsDead, Is.False);
        Assert.That(approved, Is.Empty);
        Assert.That(handler.PendingCount, Is.Zero, "The player that left is forgotten.");

        handler.Dispose();
        handler = Handler(new PlayerRespawner(players, playerWorlds, playerIds, session, trips, arrivals, worlds, readiness), false);
        PlayerHandle mirrored = Player("Mirrored", new Vector2(40.5f, 20.5f));
        mirrored.Health.ApplyDamage(mirrored.Health.MaxHealth);
        clock.Time = Delay * 3f;
        handler.Tick();
        Assert.That(handler.PendingCount, Is.Zero);
        Assert.That(mirrored.Health.IsDead, Is.True, "A client respawns nobody; the host does.");
    }

    private PlayerDeathHandler Handler(PlayerRespawner respawner, bool authoritative)
    {
        PlayerDeathHandler made = new(players, respawner, new FixedGameAuthority(authoritative), Track(GameplaySettings.Create(Delay)), clock);
        made.Start();
        return made;
    }

    private PlayerHandle Player(string name, Vector2 localPosition)
    {
        GameObject body = Track(new GameObject(name));
        body.transform.position = world.ToWorld(localPosition);
        PlayerHandle handle = new(body.transform, body.AddComponent<Health>());
        players.Add(handle);
        playerWorlds.Set(handle, world);
        return handle;
    }

    private string EntryOf(PlayerHandle player)
    {
        TripApproval approval = approved.Find(a => a.Player == player);
        return approval.Player == player ? approval.EntryName : null;
    }

    private T Track<T>(T item) where T : Object
    {
        created.Add(item);
        return item;
    }
}
