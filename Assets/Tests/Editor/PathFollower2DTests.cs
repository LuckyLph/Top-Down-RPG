using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

public class PathFollower2DTests
{
    private const float WaypointReachDistance = 0.05f;
    private const float ArrivalDistance = 0.15f;
    private const float MoveSpeed = 2.5f;
    private const float FixedDelta = 0.02f;

    private readonly List<Object> createdObjects = new();
    private GameObject root;
    private Tile fillTile;
    private NavigationGrid2D navigationGrid;

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < createdObjects.Count; i++)
        {
            if (createdObjects[i] != null)
            {
                Object.DestroyImmediate(createdObjects[i]);
            }
        }

        createdObjects.Clear();
    }

    [Test]
    public void BuildPath_RoutesThroughTheGapInAWall_WithTheOldAgentsWaypoints()
    {
        CreateGridRoot();
        List<Vector3Int> cells = Rect(0, 0, 6, 4);
        cells.RemoveAll(cell => cell.x == 3 && cell.y <= 3);
        AddTerrainLayer("Ground", CreateTerrain("ground"), cells);
        BuildGrid();
        PathFollower2D follower = CreateFollower(null);

        Assert.That(follower.BuildPath(new Vector2(0.5f, 0.5f), new Vector2(6.5f, 0.5f), allowPartial: false), Is.True);

        Assert.That(follower.Waypoints, Is.EqualTo(new[] { new Vector2(2.5f, 4.5f), new Vector2(4.5f, 4.5f), new Vector2(6.5f, 0.5f) }));
        Assert.That(follower.IsPartialPath, Is.False);
        Assert.That(follower.ReachedResolvedGoal, Is.True);
    }

    [Test]
    public void BuildPath_TowardAnUnreachableGoal_EndsAtTheClosestCellWhenPartial_AndFailsWhenStrict()
    {
        CreateGridRoot();
        List<Vector3Int> cells = Rect(0, 0, 4, 2);
        cells.AddRange(Rect(7, 0, 8, 2));
        AddTerrainLayer("Ground", CreateTerrain("ground"), cells);
        BuildGrid();
        PathFollower2D follower = CreateFollower(null);
        Vector2 start = new(0.5f, 1.5f);
        Vector2 goal = new(8.5f, 1.5f);

        Assert.That(follower.CanReach(start, goal), Is.False);
        Assert.That(follower.BuildPath(start, goal, allowPartial: true), Is.True);
        Assert.That(follower.Waypoints, Is.EqualTo(new[] { new Vector2(4.5f, 1.5f) }));
        Assert.That(follower.IsPartialPath, Is.True);
        Assert.That(follower.ReachedResolvedGoal, Is.False);
        Assert.That(follower.HasGoalCell, Is.True);

        Assert.That(follower.BuildPath(start, goal, allowPartial: false), Is.False);
        Assert.That(follower.HasPath, Is.False);
        Assert.That(follower.ReachedDestination, Is.True);
        Assert.That(follower.HasGoalCell, Is.False, "A failed build clears the goal, as the mob agent always did.");
    }

    [Test]
    public void Tick_SteersTowardTheNextWaypointAtMoveSpeed_AndUsesArrivalDistanceForTheLastOne()
    {
        CreateGridRoot();
        List<Vector3Int> lShape = Rect(0, 0, 4, 0);
        lShape.AddRange(Rect(4, 1, 4, 4));
        AddTerrainLayer("Ground", CreateTerrain("ground"), lShape);
        BuildGrid();
        PathFollower2D follower = CreateFollower(null);
        Vector2 goal = navigationGrid.CellToWorldCenter(new Vector3Int(4, 4, 0));

        Assert.That(follower.BuildPath(new Vector2(0.5f, 0.5f), goal, allowPartial: false), Is.True);
        Assert.That(follower.Waypoints.Count, Is.EqualTo(2));

        Vector2 velocity = follower.Tick(new Vector2(0.5f, 0.5f), MoveSpeed, FixedDelta);
        Assert.That(velocity.x, Is.EqualTo(MoveSpeed).Within(0.0001f));
        Assert.That(velocity.y, Is.EqualTo(0f).Within(0.0001f));

        follower.Tick(follower.Waypoints[0], MoveSpeed, FixedDelta);
        Assert.That(follower.NextWaypointIndex, Is.EqualTo(1));
        Assert.That(follower.ReachedDestination, Is.False);

        Vector2 insideArrival = goal - new Vector2(0f, 0.1f);
        Assert.That(follower.Tick(insideArrival, MoveSpeed, FixedDelta), Is.EqualTo(Vector2.zero));
        Assert.That(follower.ReachedDestination, Is.True);
        Assert.That(follower.HasPath, Is.False);
    }

    [Test]
    public void StalledTime_GrowsByTheGivenDeltaWhileNotMoving_AndResetsOnProgress()
    {
        CreateGridRoot();
        AddTerrainLayer("Ground", CreateTerrain("ground"), Rect(0, 0, 4, 0));
        BuildGrid();
        PathFollower2D follower = CreateFollower(null);
        Vector2 start = new(0.5f, 0.5f);
        Assert.That(follower.BuildPath(start, navigationGrid.CellToWorldCenter(new Vector3Int(4, 0, 0)), allowPartial: false), Is.True);

        for (int i = 0; i < 10; i++)
        {
            follower.Tick(start, MoveSpeed, FixedDelta);
        }

        Assert.That(follower.StalledTime, Is.EqualTo(10 * FixedDelta).Within(0.0001f));

        follower.Tick(start + new Vector2(0.1f, 0f), MoveSpeed, FixedDelta);
        Assert.That(follower.StalledTime, Is.Zero);
    }

    [Test]
    public void BuildPathTickAndCanReach_DoNotAllocate_OnceWarmedUp()
    {
        CreateGridRoot();
        List<Vector3Int> lShape = Rect(0, 0, 4, 0);
        lShape.AddRange(Rect(4, 1, 4, 4));
        AddTerrainLayer("Ground", CreateTerrain("ground"), lShape);
        BuildGrid();
        PathFollower2D follower = CreateFollower(null);
        PathFollower2D shortcutFollower = CreateFollower(null, useStraightLineShortcut: true);
        Vector2 start = new(0.5f, 0.5f);
        Vector2 goal = navigationGrid.CellToWorldCenter(new Vector3Int(4, 4, 0));
        Vector2 straightGoal = navigationGrid.CellToWorldCenter(new Vector3Int(3, 0, 0));
        follower.BuildPath(start, goal, allowPartial: true);
        follower.Tick(start, MoveSpeed, FixedDelta);
        follower.CanReach(start, goal);
        shortcutFollower.BuildPath(start, straightGoal, allowPartial: true);

        TestDelegate buildPath = () => follower.BuildPath(start, goal, allowPartial: true);
        TestDelegate tick = () => follower.Tick(start, MoveSpeed, FixedDelta);
        TestDelegate canReach = () => follower.CanReach(start, goal);
        TestDelegate buildStraightPath = () => shortcutFollower.BuildPath(start, straightGoal, allowPartial: true);

        Assert.That(buildPath, Is.Not.AllocatingGCMemory());
        Assert.That(tick, Is.Not.AllocatingGCMemory());
        Assert.That(canReach, Is.Not.AllocatingGCMemory());
        Assert.That(buildStraightPath, Is.Not.AllocatingGCMemory());
        Assert.That(follower.Waypoints.Count, Is.EqualTo(2));
    }

    [Test]
    public void Unconfigured_BuildsNothing_AndReturnsZeroVelocity()
    {
        PathFollower2D follower = new();

        Assert.That(follower.IsConfigured, Is.False);
        Assert.That(follower.BuildPath(Vector2.zero, Vector2.one, allowPartial: true), Is.False);
        Assert.That(follower.CanReach(Vector2.zero, Vector2.one), Is.False);
        Assert.That(follower.Tick(Vector2.zero, MoveSpeed, FixedDelta), Is.EqualTo(Vector2.zero));
        Assert.That(follower.ReachedDestination, Is.True);
    }

    [Test]
    public void StraightLineShortcut_SkipsAStar_WhenTheLineIsClearAndOptimal()
    {
        CreateGridRoot();
        AddTerrainLayer("Ground", CreateTerrain("ground"), Rect(0, 0, 8, 6));
        BuildGrid();
        GridAStarPathfinder2D pathfinder = (GridAStarPathfinder2D)navigationGrid.Pathfinder;
        PathFollower2D follower = CreateFollower(null, useStraightLineShortcut: true);
        Vector2 start = new(0.3f, 0.7f);
        Vector2 goal = new(7.6f, 4.2f);

        pathfinder.FindPath(new PathRequest(new Vector3Int(0, 0, 0), new Vector3Int(8, 6, 0), false));
        int expandedBefore = pathfinder.LastExpandedCount;
        Assert.That(expandedBefore, Is.GreaterThan(0));

        Assert.That(follower.BuildPath(start, goal, allowPartial: false), Is.True);

        Assert.That(pathfinder.LastExpandedCount, Is.EqualTo(expandedBefore), "A* should not run for a clear straight line.");
        Assert.That(follower.Waypoints, Is.EqualTo(new[] { goal }));
        Assert.That(follower.IsPartialPath, Is.False);
        Assert.That(follower.ReachedResolvedGoal, Is.True);
        Assert.That(follower.GoalWasAdjusted, Is.False);
        Assert.That(follower.LastGoalCell, Is.EqualTo(navigationGrid.WorldToCell(goal)));
    }

    [Test]
    public void StraightLineShortcut_FallsBackToAStar_WhenAWallBlocksTheLine()
    {
        CreateGridRoot();
        List<Vector3Int> cells = Rect(0, 0, 6, 4);
        cells.RemoveAll(cell => cell.x == 3 && cell.y <= 3);
        AddTerrainLayer("Ground", CreateTerrain("ground"), cells);
        BuildGrid();
        PathFollower2D shortcutFollower = CreateFollower(null, useStraightLineShortcut: true);
        PathFollower2D plainFollower = CreateFollower(null);
        Vector2 start = new(0.5f, 0.5f);
        Vector2 goal = new(6.5f, 0.5f);

        Assert.That(shortcutFollower.BuildPath(start, goal, allowPartial: false), Is.True);
        plainFollower.BuildPath(start, goal, allowPartial: false);

        Assert.That(shortcutFollower.Waypoints, Is.EqualTo(plainFollower.Waypoints));
        Assert.That(shortcutFollower.Waypoints.Count, Is.EqualTo(3));
    }

    [Test]
    public void StraightLineShortcut_FallsBackToAStar_WhenTheLineCrossesCostlierTerrain()
    {
        CreateGridRoot();
        TerrainType2D ground = CreateTerrain("ground");
        TerrainType2D mud = CreateTerrain("mud");
        AddTerrainLayer("Ground", ground, Rect(0, 0, 6, 2));
        AddTerrainLayer("Mud", mud, Rect(3, 0, 3, 1));
        TerrainMovementProfile2D profile = CreateProfile();
        profile.SetTerrainRule(ground, true, 10);
        profile.SetTerrainRule(mud, true, 25);
        BuildGrid();
        PathFollower2D shortcutFollower = CreateFollower(profile, useStraightLineShortcut: true);
        PathFollower2D plainFollower = CreateFollower(profile);
        Vector2 start = navigationGrid.CellToWorldCenter(new Vector3Int(0, 1, 0));
        Vector2 goal = navigationGrid.CellToWorldCenter(new Vector3Int(6, 1, 0));

        Assert.That(shortcutFollower.BuildPath(start, goal, allowPartial: false), Is.True);
        plainFollower.BuildPath(start, goal, allowPartial: false);

        Assert.That(shortcutFollower.Waypoints, Is.EqualTo(plainFollower.Waypoints));
        Assert.That(shortcutFollower.Waypoints.Count, Is.GreaterThan(1), "The cheaper route detours around the mud.");
    }

    [Test]
    public void MatchesMobPathAgent2D_OnRandomGrids()
    {
        const int width = 14;
        const int height = 10;
        for (int seed = 0; seed < 8; seed++)
        {
            System.Random random = new(seed);
            TerrainMovementProfile2D profile = BuildRandomGrid(random, width, height);
            (MobPathAgent2D agent, Rigidbody2D body) = CreateMobAgent(profile);
            PathFollower2D follower = new();
            follower.Configure(navigationGrid, profile, new PathFollowerSettings2D(WaypointReachDistance, ArrivalDistance, 8));

            for (int query = 0; query < 25; query++)
            {
                Vector2 start = RandomWalkablePoint(random, profile, width, height);
                Vector2 goal = new(
                    (float)(random.NextDouble() * (width + 2) - 1),
                    (float)(random.NextDouble() * (height + 2) - 1));
                bool allowPartial = random.Next(2) == 0;
                string context = $"seed {seed}, query {query}, start {start}, goal {goal}, partial {allowPartial}";

                PlaceBody(body, start);
                Assert.That(follower.CanReach(start, goal), Is.EqualTo(agent.CanReachWorldTarget(goal)), context);
                bool agentBuilt = agent.BuildPathToWorld(goal, allowPartial);
                bool followerBuilt = follower.BuildPath(start, goal, allowPartial);
                Assert.That(followerBuilt, Is.EqualTo(agentBuilt), context);
                AssertSameState(agent, follower, context);

                Vector2 position = start;
                for (int tick = 0; tick < 400 && (agent.HasPath || follower.HasPath); tick++)
                {
                    PlaceBody(body, position);
                    agent.FixedTick();
                    Vector2 velocity = follower.Tick(position, MoveSpeed, Time.fixedDeltaTime);
                    Vector2 expectedMotorVelocity = follower.HasPath ? Vector2.ClampMagnitude(velocity, MoveSpeed) : Vector2.zero;
                    Assert.That(agent.GetComponent<MobMotor2D>().DesiredVelocity, Is.EqualTo(expectedMotorVelocity), context);
                    AssertSameState(agent, follower, context);

                    if (tick % 9 != 4)
                    {
                        position += velocity * Time.fixedDeltaTime;
                    }
                }
            }
        }
    }

    private static void AssertSameState(MobPathAgent2D agent, PathFollower2D follower, string context)
    {
        Assert.That(follower.HasPath, Is.EqualTo(agent.HasPath), context);
        Assert.That(follower.ReachedDestination, Is.EqualTo(agent.ReachedDestination), context);
        Assert.That(follower.IsPartialPath, Is.EqualTo(agent.IsPartialPath), context);
        Assert.That(follower.ReachedResolvedGoal, Is.EqualTo(agent.ReachedResolvedGoal), context);
        Assert.That(follower.GoalWasAdjusted, Is.EqualTo(agent.GoalWasAdjusted), context);
        Assert.That(follower.HasGoalCell, Is.EqualTo(agent.HasGoalCell), context);
        Assert.That(follower.LastGoalCell, Is.EqualTo(agent.LastGoalCell), context);
        Assert.That(follower.StalledTime, Is.EqualTo(agent.StalledTime), context);
        Assert.That(follower.Waypoints, Is.EqualTo(agent.Waypoints), context);
    }

    private PathFollower2D CreateFollower(TerrainMovementProfile2D profile, bool useStraightLineShortcut = false)
    {
        PathFollower2D follower = new();
        follower.Configure(
            navigationGrid,
            profile,
            new PathFollowerSettings2D(WaypointReachDistance, ArrivalDistance, useStraightLineShortcut: useStraightLineShortcut));
        return follower;
    }

    private TerrainMovementProfile2D BuildRandomGrid(System.Random random, int width, int height)
    {
        CreateGridRoot();
        TerrainType2D ground = CreateTerrain("ground");
        TerrainType2D mud = CreateTerrain("mud");
        List<Vector3Int> groundCells = new();
        List<Vector3Int> mudCells = new();
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (random.NextDouble() < 0.25)
                {
                    continue;
                }

                Vector3Int cell = new(x, y, 0);
                groundCells.Add(cell);
                if (random.NextDouble() < 0.2)
                {
                    mudCells.Add(cell);
                }
            }
        }

        AddTerrainLayer("Ground", ground, groundCells);
        AddTerrainLayer("Mud", mud, mudCells);
        TerrainMovementProfile2D profile = CreateProfile();
        profile.SetTerrainRule(ground, true, 10);
        profile.SetTerrainRule(mud, true, 25);
        BuildGrid();
        return profile;
    }

    private Vector2 RandomWalkablePoint(System.Random random, TerrainMovementProfile2D profile, int width, int height)
    {
        while (true)
        {
            Vector3Int cell = new(random.Next(width), random.Next(height), 0);
            if (navigationGrid.IsCellWalkable(cell, profile))
            {
                Vector2 jitter = new((float)(random.NextDouble() - 0.5) * 0.8f, (float)(random.NextDouble() - 0.5) * 0.8f);
                return (Vector2)navigationGrid.CellToWorldCenter(cell) + jitter;
            }
        }
    }

    private (MobPathAgent2D, Rigidbody2D) CreateMobAgent(TerrainMovementProfile2D profile)
    {
        MobConfig config = Track(ScriptableObject.CreateInstance<MobConfig>());
        config.waypointReachDistance = WaypointReachDistance;
        config.arrivalDistance = ArrivalDistance;
        config.moveSpeed = MoveSpeed;
        config.movementProfile = profile;

        GameObject mob = Track(new GameObject("Mob"));
        Rigidbody2D body = mob.AddComponent<Rigidbody2D>();
        MobMotor2D motor = mob.AddComponent<MobMotor2D>();
        MobPathAgent2D agent = mob.AddComponent<MobPathAgent2D>();
        motor.Initialize(config);
        agent.Initialize(navigationGrid, motor, config);
        return (agent, body);
    }

    private static void PlaceBody(Rigidbody2D body, Vector2 position)
    {
        body.position = position;
        body.transform.position = position;
    }

    private void CreateGridRoot()
    {
        root = Track(new GameObject("PathFollowerTestRoot"));
        root.AddComponent<Grid>();
        if (fillTile == null)
        {
            fillTile = Track(ScriptableObject.CreateInstance<Tile>());
        }
    }

    private void AddTerrainLayer(string name, TerrainType2D terrain, IEnumerable<Vector3Int> cells)
    {
        GameObject layerObject = new(name);
        layerObject.transform.SetParent(root.transform);
        Tilemap tilemap = layerObject.AddComponent<Tilemap>();
        layerObject.AddComponent<TilemapRenderer>();
        foreach (Vector3Int cell in cells)
        {
            tilemap.SetTile(cell, fillTile);
        }

        layerObject.AddComponent<NavigationTerrainSource2D>().Configure(tilemap, null, null, terrain);
    }

    private void BuildGrid()
    {
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();
    }

    private TerrainType2D CreateTerrain(string id)
    {
        TerrainType2D terrain = Track(ScriptableObject.CreateInstance<TerrainType2D>());
        terrain.Configure(id);
        return terrain;
    }

    private TerrainMovementProfile2D CreateProfile()
    {
        TerrainMovementProfile2D profile = Track(ScriptableObject.CreateInstance<TerrainMovementProfile2D>());
        profile.Configure(isWalkableByDefault: false, traversalCostByDefault: 10);
        return profile;
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
