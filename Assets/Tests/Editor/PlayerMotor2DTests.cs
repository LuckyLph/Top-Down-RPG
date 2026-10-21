using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for <see cref="PlayerMotor2D"/>: straight-line and grid-aware move orders on the navigation of the player's
/// world, arrival and stop, aim facing, remote copies, speed scaling, stall time, allocations and the player physics
/// layer setup.
/// </summary>
public class PlayerMotor2DTests
{
    private const float FixedDelta = 0.02f;
    private const float Speed = 5f;

    private readonly List<Object> createdObjects = new();
    private readonly List<WorldNavigation> navigations = new();
    private Tile fillTile;
    private PlayerWorlds playerWorlds;
    private Worlds worlds;
    private PlayerNavigation playerNavigation;
    private PlayerHandle handle;
    private PlayerMotor2D motor;
    private Rigidbody2D body;

    /// <summary>
    /// Creates a registered kinematic player body in no world yet, with a <see cref="PlayerMotor2D"/> using test
    /// control settings at the test move speed.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        fillTile = Track(ScriptableObject.CreateInstance<Tile>());
        PlayerRegistry players = new();
        playerWorlds = new PlayerWorlds(players);
        worlds = new Worlds();
        playerNavigation = new PlayerNavigation(players, playerWorlds, worlds);
        GameObject player = Track(new GameObject("Player"));
        body = player.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        motor = player.AddComponent<PlayerMotor2D>();
        motor.Construct(playerNavigation);
        motor.Initialize(Track(TestPlayerControlSettings.Create(moveSpeed: Speed)));
        handle = new PlayerHandle(player.transform, null);
        players.Add(handle);
    }

    /// <summary>
    /// Takes every world's pages out and destroys every object and asset the test tracked.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        playerWorlds.Dispose();
        foreach (WorldNavigation navigation in navigations)
        {
            navigation.Dispose();
        }

        navigations.Clear();
        foreach (Object created in createdObjects)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        createdObjects.Clear();
    }

    /// <summary>
    /// With no navigation, a move order is a single straight waypoint, and the body heads for it at move speed,
    /// facing the way it moves.
    /// </summary>
    [Test]
    public void WithoutAGrid_MovesInAStraightLineAtMoveSpeed_AndFacesTheWayItMoves()
    {
        Place(Vector2.zero);

        Assert.That(motor.MoveTo(new Vector2(0f, -10f)), Is.True);
        Assert.That(motor.Follower.Waypoints, Is.EqualTo(new[] { new Vector2(0f, -10f) }));

        motor.FixedTick(FixedDelta);

        Assert.That(body.linearVelocity, Is.EqualTo(new Vector2(0f, -Speed)));
        Assert.That(motor.CurrentMove, Is.EqualTo(Vector2.down));
        Assert.That(motor.FacingDirection, Is.EqualTo(Vector2.down));
    }

    /// <summary>
    /// On its world's navigation, a move order past a wall builds a multi-waypoint path over walkable cells that ends
    /// at the exact destination.
    /// </summary>
    [Test]
    public void OnItsWorldsNavigation_PathsAroundAWall()
    {
        List<Vector3Int> cells = Rect(0, 0, 6, 4);
        cells.RemoveAll(cell => cell.x == 3 && cell.y <= 3);
        INavigation2D navigation = EnterWorld(cells);
        Place(new Vector2(0.5f, 0.5f));

        Assert.That(motor.MoveTo(new Vector2(6.5f, 0.5f)), Is.True);

        IReadOnlyList<Vector2> waypoints = motor.Follower.Waypoints;
        Assert.That(waypoints.Count, Is.GreaterThan(1));
        Assert.That(waypoints[waypoints.Count - 1], Is.EqualTo(new Vector2(6.5f, 0.5f)));
        for (int i = 0; i < waypoints.Count; i++)
        {
            Assert.That(navigation.IsCellWalkable(navigation.WorldToCell(waypoints[i]), null), Is.True);
        }
    }

    /// <summary>
    /// On open ground, a clear straight line becomes a single waypoint at the exact destination.
    /// </summary>
    [Test]
    public void AClearStraightLine_SkipsAStar()
    {
        EnterWorld(Rect(0, 0, 8, 8));
        Place(new Vector2(0.5f, 0.5f));

        Assert.That(motor.MoveTo(new Vector2(7.2f, 3.9f)), Is.True);

        Assert.That(motor.Follower.Waypoints, Is.EqualTo(new[] { new Vector2(7.2f, 3.9f) }));
    }

    /// <summary>
    /// A destination on an unwalkable cell is replaced by the nearest walkable cell.
    /// </summary>
    [Test]
    public void AnUnwalkableDestination_EndsAtTheNearestWalkableCell()
    {
        List<Vector3Int> cells = Rect(0, 0, 6, 2);
        cells.RemoveAll(cell => cell.x >= 4 && cell.y == 2);
        INavigation2D navigation = EnterWorld(cells);
        Place(new Vector2(0.5f, 0.5f));

        Assert.That(motor.MoveTo(new Vector2(5.5f, 2.5f)), Is.True);

        IReadOnlyList<Vector2> waypoints = motor.Follower.Waypoints;
        Vector3Int last = navigation.WorldToCell(waypoints[waypoints.Count - 1]);
        Assert.That(navigation.IsCellWalkable(last, null), Is.True);
        Assert.That(last, Is.EqualTo(new Vector3Int(5, 1, 0)));
    }

    /// <summary>
    /// A destination beyond the loaded chunks walks to the ground's edge in that direction.
    /// </summary>
    [Test]
    public void ADestinationBeyondTheLoadedChunks_WalksToTheEdgeTowardIt()
    {
        EnterWorld(Rect(0, 0, 6, 2));
        Place(new Vector2(0.5f, 1.5f));

        Assert.That(motor.MoveTo(new Vector2(100.5f, 1.5f)), Is.True);

        IReadOnlyList<Vector2> waypoints = motor.Follower.Waypoints;
        Assert.That(waypoints[waypoints.Count - 1], Is.EqualTo(new Vector2(6.5f, 1.5f)));
    }

    /// <summary>
    /// Each move order paths on the navigation of whichever world the player is in at the time, and in no world it
    /// falls back to a straight line.
    /// </summary>
    [Test]
    public void ANewWorld_IsUsedForTheNextMove_AndNoWorldFallsBackToStraightLines()
    {
        List<Vector3Int> walled = Rect(0, 0, 6, 4);
        walled.RemoveAll(cell => cell.x == 3 && cell.y <= 3);
        Place(new Vector2(0.5f, 0.5f));
        Vector2 goal = new(6.5f, 0.5f);

        EnterWorld(walled, "Walled");
        motor.MoveTo(goal);
        Assert.That(motor.Follower.Waypoints.Count, Is.GreaterThan(1));

        INavigation2D open = EnterWorld(Rect(0, 0, 6, 4), "Open");
        motor.MoveTo(goal);
        Assert.That(motor.Follower.Waypoints, Is.EqualTo(new[] { goal }));
        Assert.That(motor.Follower.Navigation, Is.SameAs(open));

        playerWorlds.Set(handle, null);
        motor.MoveTo(goal);
        Assert.That(motor.Follower.Navigation, Is.Null);
        Assert.That(motor.Follower.Waypoints, Is.EqualTo(new[] { goal }));
    }

    /// <summary>
    /// Arriving stops the body and clears the move direction while keeping the last facing.
    /// </summary>
    [Test]
    public void ReachingTheDestination_StopsTheBody()
    {
        Place(Vector2.zero);
        motor.MoveTo(new Vector2(1f, 0f));
        motor.FixedTick(FixedDelta);
        Assert.That(motor.HasPath, Is.True);

        Place(new Vector2(0.95f, 0f));
        motor.FixedTick(FixedDelta);

        Assert.That(motor.ReachedDestination, Is.True);
        Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
        Assert.That(motor.CurrentMove, Is.EqualTo(Vector2.zero));
        Assert.That(motor.FacingDirection, Is.EqualTo(Vector2.right), "Facing stays where the player last walked.");
    }

    /// <summary>
    /// While aiming, the motor faces the aim direction even as it moves another way, and faces its movement again
    /// once aiming is released.
    /// </summary>
    [Test]
    public void Aiming_HoldsTheFacingWhileMoving_UntilReleased()
    {
        Place(Vector2.zero);
        motor.MoveTo(new Vector2(5f, 0f));

        motor.SetAim(true, Vector2.up);
        motor.FixedTick(FixedDelta);
        Assert.That(motor.FacingDirection, Is.EqualTo(Vector2.up));
        Assert.That(motor.CurrentMove, Is.EqualTo(Vector2.right));

        motor.SetAim(false, Vector2.zero);
        motor.FixedTick(FixedDelta);
        Assert.That(motor.FacingDirection, Is.EqualTo(Vector2.right));
    }

    /// <summary>
    /// Stop drops the path and zeroes the body's velocity and the move direction.
    /// </summary>
    [Test]
    public void Stop_DropsThePathAndZeroesVelocity()
    {
        Place(Vector2.zero);
        motor.MoveTo(new Vector2(5f, 0f));
        motor.FixedTick(FixedDelta);

        motor.Stop();

        Assert.That(motor.HasPath, Is.False);
        Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
        Assert.That(motor.CurrentMove, Is.EqualTo(Vector2.zero));
    }

    /// <summary>
    /// A copy that does not simulate movement refuses move orders, shows the replicated move and facing, and never
    /// moves its body.
    /// </summary>
    [Test]
    public void ARemoteCopy_ShowsReplicatedMovement_AndNeverMovesItsBody()
    {
        Place(Vector2.zero);
        motor.SetSimulatesMovement(false);

        Assert.That(motor.MoveTo(new Vector2(5f, 0f)), Is.False);
        motor.ShowRemoteMovement(Vector2.left, Vector2.left);
        motor.FixedTick(FixedDelta);

        Assert.That(motor.CurrentMove, Is.EqualTo(Vector2.left));
        Assert.That(motor.Velocity, Is.EqualTo(Vector2.left * Speed), "The copy moves at the replicated share of its own base speed.");
        Assert.That(motor.FacingDirection, Is.EqualTo(Vector2.left));
        Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));

        motor.ShowRemoteMovement(Vector2.zero, Vector2.up);
        Assert.That(motor.CurrentMove, Is.EqualTo(Vector2.zero));
        Assert.That(motor.FacingDirection, Is.EqualTo(Vector2.up));
    }

    /// <summary>
    /// A speed scale slows the body in proportion, and the move reports the actual speed over the base speed, so a
    /// remote copy shows the slowed gait; a zero scale holds it still without counting as stalled or dropping the move
    /// order, and the path resumes once the scale is back to one.
    /// </summary>
    [Test]
    public void ASpeedScale_SlowsTheBody_AndZeroHoldsItWithoutStalling()
    {
        Place(Vector2.zero);
        motor.MoveTo(new Vector2(0f, -10f));

        motor.SetSpeedScale(0.5f);
        motor.FixedTick(FixedDelta);
        Assert.That(body.linearVelocity, Is.EqualTo(new Vector2(0f, -Speed * 0.5f)));
        Assert.That(motor.CurrentMove, Is.EqualTo(new Vector2(0f, -0.5f)));
        Assert.That(motor.Velocity, Is.EqualTo(new Vector2(0f, -Speed * 0.5f)));

        motor.SetSpeedScale(0f);
        for (int i = 0; i < 5; i++)
        {
            motor.FixedTick(FixedDelta);
        }

        Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));
        Assert.That(motor.CurrentMove, Is.EqualTo(Vector2.zero));
        Assert.That(motor.StalledTime, Is.Zero, "A rooted player is not stuck, so its move order is kept.");
        Assert.That(motor.ReachedDestination, Is.False);

        motor.SetSpeedScale(1f);
        motor.FixedTick(FixedDelta);
        Assert.That(body.linearVelocity, Is.EqualTo(new Vector2(0f, -Speed)), "The path resumes once the root ends.");
    }

    /// <summary>
    /// Stalled time grows by each fixed tick's delta while the body does not move.
    /// </summary>
    [Test]
    public void StalledTime_GrowsWhileTheBodyDoesNotMove()
    {
        Place(Vector2.zero);
        motor.MoveTo(new Vector2(5f, 0f));

        for (int i = 0; i < 5; i++)
        {
            motor.FixedTick(FixedDelta);
        }

        Assert.That(motor.StalledTime, Is.EqualTo(5 * FixedDelta).Within(0.0001f));
    }

    /// <summary>
    /// Once warm, re-issuing a move order around a wall and ticking allocate no managed memory.
    /// </summary>
    [Test]
    public void MoveToAndFixedTick_DoNotAllocate_OnceWarm()
    {
        List<Vector3Int> cells = Rect(0, 0, 6, 4);
        cells.RemoveAll(cell => cell.x == 3 && cell.y <= 3);
        EnterWorld(cells);
        Place(new Vector2(0.5f, 0.5f));
        Vector2 goal = new(6.5f, 0.5f);
        motor.MoveTo(goal);
        motor.FixedTick(FixedDelta);

        TestDelegate moveAndTick = () =>
        {
            motor.MoveTo(goal);
            motor.FixedTick(FixedDelta);
        };

        Assert.That(moveAndTick, Is.Not.AllocatingGCMemory());
    }

    /// <summary>
    /// The project's physics settings let the Player layer pass through itself but not the Enemy or Obstacles
    /// layers, and leave enemies colliding with each other.
    /// </summary>
    [Test]
    public void PlayersPassThroughEachOther_ButNotWallsOrMobs()
    {
        int player = LayerMask.NameToLayer("Player");

        Assert.That(Physics2D.GetIgnoreLayerCollision(player, player), Is.True);
        Assert.That(Physics2D.GetIgnoreLayerCollision(player, LayerMask.NameToLayer("Enemy")), Is.False);
        Assert.That(Physics2D.GetIgnoreLayerCollision(player, LayerMask.NameToLayer("Obstacles")), Is.False);
        Assert.That(Physics2D.GetIgnoreLayerCollision(LayerMask.NameToLayer("Enemy"), LayerMask.NameToLayer("Enemy")), Is.False, "Mob collisions are unchanged.");
    }

    /// <summary>
    /// Moves the player body and its transform to <paramref name="position"/> at once.
    /// </summary>
    private void Place(Vector2 position)
    {
        body.position = position;
        body.transform.position = position;
    }

    /// <summary>
    /// Puts the player in a new loaded world at the origin whose navigation has a page for its chunk (0, 0), the only
    /// walkable ground there being <paramref name="cells"/>, and returns that navigation.
    /// </summary>
    private INavigation2D EnterWorld(IEnumerable<Vector3Int> cells, string worldName = "Motor")
    {
        WorldDefinition world = Track(WorldDefinition.Create(worldName, Vector2Int.zero, new Vector2Int(2, 1), new[] { Vector2Int.zero, Vector2Int.right }));
        WorldNavigation navigation = new(new NavigationPagePool());
        navigations.Add(navigation);
        Assert.That(navigation.AddChunk(CreateGrid(cells), Vector2Int.zero), Is.True);
        worlds.Add(new LoadedWorld(world, default, null, null, navigation));
        playerWorlds.Set(handle, world);
        return navigation;
    }

    /// <summary>
    /// Builds a tracked navigation grid whose only walkable ground is <paramref name="cells"/>.
    /// </summary>
    private NavigationGrid2D CreateGrid(IEnumerable<Vector3Int> cells)
    {
        GameObject root = Track(new GameObject("Grid"));
        root.AddComponent<Grid>();
        GameObject layer = new("Ground");
        layer.transform.SetParent(root.transform);
        Tilemap tilemap = layer.AddComponent<Tilemap>();
        layer.AddComponent<TilemapRenderer>();
        foreach (Vector3Int cell in cells)
        {
            tilemap.SetTile(cell, fillTile);
        }

        TerrainType2D ground = Track(TerrainType2D.Create("ground"));
        layer.AddComponent<NavigationTerrainSource2D>().Configure(ground);
        return root.AddComponent<NavigationGrid2D>();
    }

    /// <summary>
    /// Lists every cell of the rectangle from (<paramref name="minX"/>, <paramref name="minY"/>) to
    /// (<paramref name="maxX"/>, <paramref name="maxY"/>), bounds included.
    /// </summary>
    private static List<Vector3Int> Rect(int minX, int minY, int maxX, int maxY)
    {
        List<Vector3Int> cells = new();
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                cells.Add(new Vector3Int(x, y, 0));
            }
        }

        return cells;
    }

    /// <summary>
    /// Records <paramref name="created"/> for destruction on teardown and returns it.
    /// </summary>
    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
