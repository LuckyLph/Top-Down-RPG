using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

public class PlayerMotor2DTests
{
    private const float FixedDelta = 0.02f;
    private const float Speed = 5f;

    private readonly List<Object> createdObjects = new();
    private Tile fillTile;
    private ActiveNavigationGrid activeGrid;
    private PlayerMotor2D motor;
    private Rigidbody2D body;

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

    [Test]
    public void AClearStraightLine_SkipsAStar()
    {
        NavigationGrid2D grid = CreateGrid(Rect(0, 0, 8, 8));
        activeGrid.Set(grid);
        Place(new Vector2(0.5f, 0.5f));

        Assert.That(motor.MoveTo(new Vector2(7.2f, 3.9f)), Is.True);

        Assert.That(motor.Follower.Waypoints, Is.EqualTo(new[] { new Vector2(7.2f, 3.9f) }));
    }

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

    [Test]
    public void ADestinationFarOutsideTheMap_WalksToTheEdgeTowardIt()
    {
        activeGrid.Set(CreateGrid(Rect(0, 0, 6, 2)));
        Place(new Vector2(0.5f, 1.5f));

        Assert.That(motor.MoveTo(new Vector2(60.5f, 1.5f)), Is.True);

        IReadOnlyList<Vector2> waypoints = motor.Follower.Waypoints;
        Assert.That(waypoints[waypoints.Count - 1], Is.EqualTo(new Vector2(6.5f, 1.5f)));
    }

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

    [Test]
    public void PlayersPassThroughEachOther_ButNotWallsOrMobs()
    {
        int player = LayerMask.NameToLayer("Player");

        Assert.That(Physics2D.GetIgnoreLayerCollision(player, player), Is.True);
        Assert.That(Physics2D.GetIgnoreLayerCollision(player, LayerMask.NameToLayer("Enemy")), Is.False);
        Assert.That(Physics2D.GetIgnoreLayerCollision(player, LayerMask.NameToLayer("Obstacles")), Is.False);
        Assert.That(Physics2D.GetIgnoreLayerCollision(LayerMask.NameToLayer("Enemy"), LayerMask.NameToLayer("Enemy")), Is.False, "Mob collisions are unchanged.");
    }

    private void Place(Vector2 position)
    {
        body.position = position;
        body.transform.position = position;
    }

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

        TerrainType2D ground = Track(ScriptableObject.CreateInstance<TerrainType2D>());
        ground.Configure("ground");
        layer.AddComponent<NavigationTerrainSource2D>().Configure(tilemap, null, null, ground);
        NavigationGrid2D grid = root.AddComponent<NavigationGrid2D>();
        grid.BuildGrid();
        return grid;
    }

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

    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
