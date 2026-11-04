using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for <see cref="WorldPlayers"/>, the players of one world that its mobs consider: who it lists, following
/// players in and out of the world, a mob's perception ignoring a nearer player in another world, and a traveller left
/// out while its trip protects it (<see cref="TripProtection"/>).
/// </summary>
public class WorldPlayersTests
{
    private readonly List<Object> created = new();
    private PlayerRegistry registry;
    private PlayerWorlds playerWorlds;
    private TripProtection protection;
    private WorldDefinition home;
    private WorldDefinition elsewhere;

    /// <summary>
    /// Creates the registry, the record of the players' worlds, the trip protection and two worlds.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        registry = new PlayerRegistry();
        playerWorlds = new PlayerWorlds(registry);
        protection = new TripProtection(registry, playerWorlds, new ManualClock(), Track(GameplaySettings.Create()));
        home = Track(WorldDefinition.Create("Home", new Vector2Int(2048, 0), Vector2Int.one, new[] { Vector2Int.zero }));
        elsewhere = Track(WorldDefinition.Create("Elsewhere", new Vector2Int(4096, 0), Vector2Int.one, new[] { Vector2Int.zero }));
    }

    /// <summary>
    /// Destroys what the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        protection.Dispose();
        playerWorlds.Dispose();
        foreach (Object createdObject in created)
        {
            if (createdObject != null)
            {
                Object.DestroyImmediate(createdObject);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// It lists only the players already in its world, and follows players entering and leaving it, raising its
    /// events, until disposed.
    /// </summary>
    [Test]
    public void ItListsThePlayersInItsWorld_AndFollowsThemInAndOut()
    {
        PlayerHandle inside = AddPlayer(Vector2.zero, home);
        PlayerHandle outside = AddPlayer(Vector2.zero, elsewhere);
        PlayerHandle nowhere = AddPlayer(Vector2.zero, null);
        WorldPlayers players = new(registry, playerWorlds, protection, home);
        List<PlayerHandle> added = new();
        List<PlayerHandle> removed = new();
        players.PlayerAdded += added.Add;
        players.PlayerRemoved += removed.Add;

        Assert.That(players.Players, Is.EqualTo(new[] { inside }));
        Assert.That(players.Contains(outside), Is.False);
        Assert.That(players.AnyAlive, Is.True);

        playerWorlds.Set(nowhere, home);
        playerWorlds.Set(inside, elsewhere);
        registry.Remove(nowhere);

        Assert.That(added, Is.EqualTo(new[] { nowhere }));
        Assert.That(removed, Is.EqualTo(new[] { inside, nowhere }));
        Assert.That(players.Players, Is.Empty);
        Assert.That(players.AnyAlive, Is.False);

        players.Dispose();
        playerWorlds.Set(outside, home);
        Assert.That(players.Players, Is.Empty, "Once disposed it follows nobody.");
    }

    /// <summary>
    /// A mob reading its world's players targets the player in its world, though a player of another world stands
    /// nearer, and drops its target once that player leaves the world.
    /// </summary>
    [Test]
    public void AMobOfAWorld_ConsidersOnlyThePlayersInItsWorld()
    {
        PlayerHandle nearerElsewhere = AddPlayer(new Vector2(1f, 0f), elsewhere);
        PlayerHandle fartherHome = AddPlayer(new Vector2(4f, 0f), home);
        WorldPlayers players = new(registry, playerWorlds, protection, home);
        GameObject mob = Track(new GameObject("Mob"));
        MobPerception2D perception = mob.AddComponent<MobPerception2D>();
        MobConfig config = Track(MobConfig.Create(detectionRadius: 6f, loseTargetDistance: 8f));
        perception.Initialize(players, config);

        perception.Tick(0.2f);
        Assert.That(perception.CurrentTargetPlayer, Is.SameAs(fartherHome));

        playerWorlds.Set(fartherHome, elsewhere);
        perception.Tick(0.2f);
        Assert.That(perception.CurrentTargetPlayer, Is.Null, "A target that leaves the world is dropped.");
        Assert.That(registry.Contains(nearerElsewhere), Is.True);
        players.Dispose();
    }

    /// <summary>
    /// A traveller its trip protects is no player of its world for the mobs: a mob targeting it drops it, its world's
    /// players leave it out, and once its machine reports the trip's fade in it is listed again and the mob takes it
    /// back.
    /// </summary>
    [Test]
    public void AProtectedTraveller_IsLeftOutUntilItsProtectionEnds()
    {
        PlayerHandle traveller = AddPlayer(new Vector2(2f, 0f), home);
        WorldPlayers players = new(registry, playerWorlds, protection, home);
        GameObject mob = Track(new GameObject("Mob"));
        MobPerception2D perception = mob.AddComponent<MobPerception2D>();
        MobConfig config = Track(MobConfig.Create(detectionRadius: 6f, loseTargetDistance: 8f));
        perception.Initialize(players, config);
        perception.Tick(0.2f);
        Assert.That(perception.CurrentTargetPlayer, Is.SameAs(traveller));

        protection.Protect(traveller);
        perception.Tick(0.2f);
        Assert.That(players.Contains(traveller), Is.False);
        Assert.That(perception.CurrentTargetPlayer, Is.Null, "A mob drops a target its trip protects.");
        perception.Tick(0.2f);
        Assert.That(perception.CurrentTargetPlayer, Is.Null, "Nor does it take it again while protected.");

        protection.End(traveller, playerWorlds.TripOf(traveller));
        perception.Tick(0.2f);
        Assert.That(players.Players, Is.EqualTo(new[] { traveller }));
        Assert.That(perception.CurrentTargetPlayer, Is.SameAs(traveller));
        players.Dispose();
    }

    /// <summary>
    /// Registers a player at <paramref name="position"/> in <paramref name="world"/> (null for none).
    /// </summary>
    private PlayerHandle AddPlayer(Vector2 position, WorldDefinition world)
    {
        GameObject playerObject = Track(new GameObject("Player"));
        playerObject.transform.position = position;
        PlayerHandle handle = new(playerObject.transform, null);
        registry.Add(handle);
        playerWorlds.Set(handle, world);
        return handle;
    }

    /// <summary>
    /// Destroys <paramref name="createdObject"/> on teardown.
    /// </summary>
    private T Track<T>(T createdObject) where T : Object
    {
        created.Add(createdObject);
        return createdObject;
    }
}
