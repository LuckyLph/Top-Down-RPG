using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for <see cref="MobPathAgent2D"/> on small tilemap-built navigation grids: arrival at the final waypoint,
/// path smoothing that respects terrain costs, allocation-free path building, and stall tracking.
/// </summary>
public class MobPathAgent2DTests
{
    private readonly List<Object> createdObjects = new();
    private GameObject root;
    private Tile fillTile;
    private NavigationGrid2D navigationGrid;
    private MobConfig config;
    private Rigidbody2D body;
    private MobPathAgent2D pathAgent;

    /// <summary>
    /// Destroys the grid, mob and assets the test created.
    /// </summary>
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

    /// <summary>
    /// On a single-waypoint path the mob counts as arrived once within the arrival distance, even outside the
    /// smaller waypoint reach distance.
    /// </summary>
    [Test]
    public void FixedTick_UsesArrivalDistance_ForSingleWaypointPath()
    {
        CreateGridRoot();
        TerrainType2D ground = CreateTerrain("ground");
        AddTerrainLayer("Ground", ground, Rect(0, 0, 4, 0));
        BuildGridAndAgent(null);

        PlaceMob(new Vector2(0.5f, 0.5f));
        Vector2 goal = navigationGrid.CellToWorldCenter(new Vector3Int(4, 0, 0));
        Assert.That(pathAgent.BuildPathToWorld(goal, allowPartial: false), Is.True);
        Assert.That(pathAgent.Waypoints.Count, Is.EqualTo(1), "A straight corridor should smooth to a single waypoint.");

        // Inside arrivalDistance (0.15) but outside waypointReachDistance (0.05).
        PlaceMob(goal - new Vector2(0.1f, 0f));
        pathAgent.FixedTick();

        Assert.That(pathAgent.ReachedDestination, Is.True);
    }

    /// <summary>
    /// Reaching the corner waypoint of a two-waypoint path does not arrive, and on later ticks the final
    /// waypoint uses the arrival distance.
    /// </summary>
    [Test]
    public void FixedTick_KeepsArrivalDistance_OnTicksAfterReachingPenultimateWaypoint()
    {
        CreateGridRoot();
        TerrainType2D ground = CreateTerrain("ground");
        List<Vector3Int> lShape = Rect(0, 0, 4, 0);
        lShape.AddRange(Rect(4, 1, 4, 4));
        AddTerrainLayer("Ground", ground, lShape);
        BuildGridAndAgent(null);

        PlaceMob(new Vector2(0.5f, 0.5f));
        Vector2 goal = navigationGrid.CellToWorldCenter(new Vector3Int(4, 4, 0));
        Assert.That(pathAgent.BuildPathToWorld(goal, allowPartial: false), Is.True);
        Assert.That(pathAgent.Waypoints.Count, Is.EqualTo(2), "The L-shaped corridor should smooth to its corner and the goal.");

        PlaceMob(pathAgent.Waypoints[0]);
        pathAgent.FixedTick();
        Assert.That(pathAgent.ReachedDestination, Is.False);

        PlaceMob(goal - new Vector2(0f, 0.1f));
        pathAgent.FixedTick();

        Assert.That(pathAgent.ReachedDestination, Is.True);
    }

    /// <summary>
    /// Smoothing never shortcuts through costly water the pathfinder routed around: no waypoint lies in the water
    /// and the smoothed path costs the same as the optimal one.
    /// </summary>
    [Test]
    public void BuildPathToWorld_DoesNotSmoothAcrossTerrainThePathfinderAvoided()
    {
        CreateGridRoot();
        TerrainType2D ground = CreateTerrain("ground");
        TerrainType2D water = CreateTerrain("water");
        List<Vector3Int> waterCells = Rect(1, 1, 3, 1);
        AddTerrainLayer("Ground", ground, Rect(0, 0, 4, 2));
        AddTerrainLayer("Water", water, waterCells);

        TerrainMovementProfile2D wadingProfile = CreateProfile(
            new TerrainMovementProfile2D.TerrainRule(ground, true, 10),
            new TerrainMovementProfile2D.TerrainRule(water, true, 45));
        BuildGridAndAgent(wadingProfile);

        Vector3Int startCell = new(0, 1, 0);
        Vector3Int goalCell = new(4, 1, 0);
        PathResult optimal = navigationGrid.Pathfinder.FindPath(new PathRequest(startCell, goalCell, false, wadingProfile));
        int optimalCost = 0;
        for (int i = 1; i < optimal.Cells.Count; i++)
        {
            optimalCost += navigationGrid.MovementCost(optimal.Cells[i - 1], optimal.Cells[i], wadingProfile);
        }

        PlaceMob(navigationGrid.CellToWorldCenter(startCell));
        Assert.That(pathAgent.BuildPathToWorld(navigationGrid.CellToWorldCenter(goalCell), allowPartial: false), Is.True);

        int smoothedCost = 0;
        Vector3Int previous = startCell;
        foreach (Vector2 waypoint in pathAgent.Waypoints)
        {
            Vector3Int cell = navigationGrid.WorldToCell(waypoint);
            Assert.That(waterCells, Has.No.Member(cell));
            Assert.That(navigationGrid.TryGetLineCost(previous, cell, wadingProfile, out int segmentCost), Is.True);
            smoothedCost += segmentCost;
            previous = cell;
        }

        Assert.That(smoothedCost, Is.EqualTo(optimalCost), "Smoothing must not route through the costly water corridor.");
    }

    /// <summary>
    /// After one warm-up call, building the same path again allocates no GC memory.
    /// </summary>
    [Test]
    public void BuildPathToWorld_DoesNotAllocate_OnceWarmedUp()
    {
        CreateGridRoot();
        TerrainType2D ground = CreateTerrain("ground");
        List<Vector3Int> lShape = Rect(0, 0, 4, 0);
        lShape.AddRange(Rect(4, 1, 4, 4));
        AddTerrainLayer("Ground", ground, lShape);
        BuildGridAndAgent(null);

        PlaceMob(new Vector2(0.5f, 0.5f));
        Vector2 goal = navigationGrid.CellToWorldCenter(new Vector3Int(4, 4, 0));
        pathAgent.BuildPathToWorld(goal, allowPartial: true);

        TestDelegate buildPath = () => pathAgent.BuildPathToWorld(goal, allowPartial: true);
        Assert.That(buildPath, Is.Not.AllocatingGCMemory());
        Assert.That(pathAgent.Waypoints.Count, Is.EqualTo(2));
    }

    /// <summary>
    /// Stalled time grows by one fixed step per tick while the mob follows a path without moving, and resets to
    /// zero once it makes progress.
    /// </summary>
    [Test]
    public void StalledTime_Accumulates_WhileFollowingPathWithoutMoving_AndResetsOnProgress()
    {
        CreateGridRoot();
        TerrainType2D ground = CreateTerrain("ground");
        AddTerrainLayer("Ground", ground, Rect(0, 0, 4, 0));
        BuildGridAndAgent(null);

        PlaceMob(new Vector2(0.5f, 0.5f));
        Assert.That(pathAgent.BuildPathToWorld(navigationGrid.CellToWorldCenter(new Vector3Int(4, 0, 0)), allowPartial: false), Is.True);

        const int blockedTicks = 10;
        for (int i = 0; i < blockedTicks; i++)
        {
            pathAgent.FixedTick();
        }

        Assert.That(pathAgent.StalledTime, Is.EqualTo(blockedTicks * Time.fixedDeltaTime).Within(0.0001f));

        PlaceMob(new Vector2(0.6f, 0.5f));
        pathAgent.FixedTick();

        Assert.That(pathAgent.StalledTime, Is.Zero);
    }

    /// <summary>
    /// Creates the tracked grid root and the tile used to fill terrain layers.
    /// </summary>
    private void CreateGridRoot()
    {
        root = Track(new GameObject("MobPathAgentTestRoot"));
        root.AddComponent<Grid>();
        fillTile = Track(ScriptableObject.CreateInstance<Tile>());
    }

    /// <summary>
    /// Adds a tilemap layer under the grid root with <paramref name="cells"/> filled, registered as a navigation
    /// source of <paramref name="terrain"/>.
    /// </summary>
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

        layerObject.AddComponent<NavigationTerrainSource2D>().Configure(terrain);
    }

    /// <summary>
    /// Builds the navigation grid from the layers, then creates a mob whose motor and path agent use a config with
    /// a 0.05 waypoint reach distance, a 0.15 arrival distance and <paramref name="profile"/> (null for the
    /// default).
    /// </summary>
    private void BuildGridAndAgent(TerrainMovementProfile2D profile)
    {
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        config = Track(MobConfig.Create(waypointReachDistance: 0.05f, arrivalDistance: 0.15f, movementProfile: profile));

        GameObject mob = Track(new GameObject("Mob"));
        body = mob.AddComponent<Rigidbody2D>();
        MobMotor2D motor = mob.AddComponent<MobMotor2D>();
        pathAgent = mob.AddComponent<MobPathAgent2D>();
        motor.Initialize(config);
        pathAgent.Initialize(navigationGrid, motor, config);
    }

    /// <summary>
    /// Teleports the mob's rigidbody and transform to <paramref name="position"/>.
    /// </summary>
    private void PlaceMob(Vector2 position)
    {
        body.position = position;
        body.transform.position = position;
    }

    /// <summary>
    /// Creates a tracked terrain type with id <paramref name="id"/>.
    /// </summary>
    private TerrainType2D CreateTerrain(string id)
    {
        return Track(TerrainType2D.Create(id));
    }

    /// <summary>
    /// Creates a tracked movement profile where unlisted terrain is unwalkable (default cost 10) and
    /// <paramref name="rules"/> set the listed terrains.
    /// </summary>
    private TerrainMovementProfile2D CreateProfile(params TerrainMovementProfile2D.TerrainRule[] rules)
    {
        return Track(TerrainMovementProfile2D.Create(false, 10, rules));
    }

    /// <summary>
    /// Lists the cells of the rectangle from (<paramref name="minX"/>, <paramref name="minY"/>) to
    /// (<paramref name="maxX"/>, <paramref name="maxY"/>), both inclusive.
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
    /// Records <paramref name="created"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
