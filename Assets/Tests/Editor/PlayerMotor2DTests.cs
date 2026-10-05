using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for <see cref="PlayerMotor2D"/>: straight-line and grid-aware move orders, the active grid, arrival and
/// stop, aim facing, remote copies, speed scaling, stall time, allocations and the player physics layer setup.
/// </summary>
public class PlayerMotor2DTests
{
    private const float FixedDelta = 0.02f;
    private const float Speed = 5f;

    private readonly List<Object> createdObjects = new();
    private Tile fillTile;
    private ActiveNavigationGrid activeGrid;
    private PlayerMotor2D motor;
    private Rigidbody2D body;

    /// <summary>
    /// Creates a kinematic player body with a <see cref="PlayerMotor2D"/> on an empty active grid holder, using
    /// test control settings at the test move speed.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        fillTile = Track(ScriptableObject.CreateInstance<Tile>());
        activeGrid = new ActiveNavigationGrid();
        GameObject player = Track(new GameObject("Player"));
        body = player.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        motor = player.AddComponent<PlayerMotor2D>();
        motor.Construct(activeGrid);
        motor.Initialize(Track(TestPlayerControlSettings.Create(moveSpeed: Speed)));
    }

    /// <summary>
    /// Destroys every object and asset the test tracked.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
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
    /// With no active grid, a move order is a single straight waypoint, and the body heads for it at move speed,
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
    /// On the active grid, a move order past a wall builds a multi-waypoint path over walkable cells that ends at
    /// the exact destination.
    /// </summary>
    [Test]
    public void OnTheActiveGrid_PathsAroundAWall()
    {
        List<Vector3Int> cells = Rect(0, 0, 6, 4);
        cells.RemoveAll(cell => cell.x == 3 && cell.y <= 3);
        activeGrid.Set(CreateGrid(cells));
        Place(new Vector2(0.5f, 0.5f));

        Assert.That(motor.MoveTo(new Vector2(6.5f, 0.5f)), Is.True);

        IReadOnlyList<Vector2> waypoints = motor.Follower.Waypoints;
        Assert.That(waypoints.Count, Is.GreaterThan(1));
        Assert.That(waypoints[waypoints.Count - 1], Is.EqualTo(new Vector2(6.5f, 0.5f)));
        for (int i = 0; i < waypoints.Count; i++)
        {
            Assert.That(activeGrid.Current.IsCellWalkable(activeGrid.Current.WorldToCell(waypoints[i])), Is.True);
        }
    }

    /// <summary>
    /// On open ground, a clear straight line becomes a single waypoint at the exact destination.
    /// </summary>
    [Test]
    public void AClearStraightLine_SkipsAStar()
    {
        NavigationGrid2D grid = CreateGrid(Rect(0, 0, 8, 8));
        activeGrid.Set(grid);
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
        activeGrid.Set(CreateGrid(cells));
        Place(new Vector2(0.5f, 0.5f));

        Assert.That(motor.MoveTo(new Vector2(5.5f, 2.5f)), Is.True);

        IReadOnlyList<Vector2> waypoints = motor.Follower.Waypoints;
        Vector3Int last = activeGrid.Current.WorldToCell(waypoints[waypoints.Count - 1]);
        Assert.That(activeGrid.Current.IsCellWalkable(last), Is.True);
        Assert.That(last, Is.EqualTo(new Vector3Int(5, 1, 0)));
    }

    /// <summary>
    /// A destination far outside the map walks to the map's edge in that direction.
    /// </summary>
    [Test]
    public void ADestinationFarOutsideTheMap_WalksToTheEdgeTowardIt()
    {
        activeGrid.Set(CreateGrid(Rect(0, 0, 6, 2)));
        Place(new Vector2(0.5f, 1.5f));

        Assert.That(motor.MoveTo(new Vector2(60.5f, 1.5f)), Is.True);

        IReadOnlyList<Vector2> waypoints = motor.Follower.Waypoints;
        Assert.That(waypoints[waypoints.Count - 1], Is.EqualTo(new Vector2(6.5f, 1.5f)));
    }

    /// <summary>
    /// Each move order paths on whichever grid is active at the time, and with none active it falls back to a
    /// straight line.
    /// </summary>
    [Test]
    public void ANewActiveGrid_IsUsedForTheNextMove_AndAClearedOneFallsBackToStraightLines()
    {
        List<Vector3Int> walled = Rect(0, 0, 6, 4);
        walled.RemoveAll(cell => cell.x == 3 && cell.y <= 3);
        NavigationGrid2D walledGrid = CreateGrid(walled);
        NavigationGrid2D openGrid = CreateGrid(Rect(0, 0, 6, 4));
        Place(new Vector2(0.5f, 0.5f));
        Vector2 goal = new(6.5f, 0.5f);

        activeGrid.Set(walledGrid);
        motor.MoveTo(goal);
        Assert.That(motor.Follower.Waypoints.Count, Is.GreaterThan(1));

        activeGrid.Clear(walledGrid);
        activeGrid.Set(openGrid);
        motor.MoveTo(goal);
        Assert.That(motor.Follower.Waypoints, Is.EqualTo(new[] { goal }));
        Assert.That(motor.Follower.NavigationGrid, Is.SameAs(openGrid));

        activeGrid.Clear(openGrid);
        motor.MoveTo(goal);
        Assert.That(motor.Follower.NavigationGrid, Is.Null);
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
        Assert.That(motor.FacingDirection, Is.EqualTo(Vector2.left));
        Assert.That(body.linearVelocity, Is.EqualTo(Vector2.zero));

        motor.ShowRemoteMovement(Vector2.zero, Vector2.up);
        Assert.That(motor.CurrentMove, Is.EqualTo(Vector2.zero));
        Assert.That(motor.FacingDirection, Is.EqualTo(Vector2.up));
    }

    /// <summary>
    /// A speed scale slows the body in proportion; a zero scale holds it still without counting as stalled or
    /// dropping the move order, and the path resumes once the scale is back to one.
    /// </summary>
    [Test]
    public void ASpeedScale_SlowsTheBody_AndZeroHoldsItWithoutStalling()
    {
        Place(Vector2.zero);
        motor.MoveTo(new Vector2(0f, -10f));

        motor.SetSpeedScale(0.5f);
        motor.FixedTick(FixedDelta);
        Assert.That(body.linearVelocity, Is.EqualTo(new Vector2(0f, -Speed * 0.5f)));
        Assert.That(motor.CurrentMove, Is.EqualTo(Vector2.down));

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
        activeGrid.Set(CreateGrid(cells));
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
        NavigationGrid2D grid = root.AddComponent<NavigationGrid2D>();
        grid.BuildGrid();
        return grid;
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
