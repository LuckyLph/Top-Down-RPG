using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

/// <summary>
/// Tests for <see cref="NavigationGrid2D"/> and its A* pathfinder: shortest and partial paths, search caps and
/// allocations, terrain rules, costs and layer priority, blocking tilemaps and obstacle colliders, edge cost, cell
/// refreshes and region connectivity, and the cliff of raised ground, which only a ramp crosses.
/// </summary>
public class NavigationGridPathfindingTests
{
    private GameObject root;
    private NavigationGrid2D navigationGrid;
    private Tile fillTile;
    private Tile blockTile;
    private TerrainType2D groundTerrain;
    private TerrainType2D waterTerrain;
    private TerrainMovementProfile2D landProfile;
    private TerrainMovementProfile2D amphibiousProfile;
    private DualGridRuleTile dualGridTile;

    /// <summary>
    /// Destroys the grid root and every tile, terrain type, movement profile and dual-grid tile the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (root != null)
        {
            Object.DestroyImmediate(root);
        }

        if (fillTile != null)
        {
            Object.DestroyImmediate(fillTile);
        }

        if (blockTile != null)
        {
            Object.DestroyImmediate(blockTile);
        }

        if (groundTerrain != null)
        {
            Object.DestroyImmediate(groundTerrain);
        }

        if (waterTerrain != null)
        {
            Object.DestroyImmediate(waterTerrain);
        }

        if (landProfile != null)
        {
            Object.DestroyImmediate(landProfile);
        }

        if (amphibiousProfile != null)
        {
            Object.DestroyImmediate(amphibiousProfile);
        }

        if (dualGridTile != null)
        {
            Object.DestroyImmediate(dualGridTile.DataTile);
            Object.DestroyImmediate(dualGridTile);
        }
    }

    /// <summary>
    /// A* finds a complete, short path from corner to corner through the single gap in a wall.
    /// </summary>
    [Test]
    public void AStar_ReturnsShortestPath_OnSimpleBlockedGrid()
    {
        SetupNavigationGrid(addGapInBarrier: true);

        PathRequest request = new(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), allowPartial: false);
        PathResult result = navigationGrid.Pathfinder.FindPath(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.IsPartial, Is.False);
        Assert.That(result.Cells.Count, Is.LessThan(9));
        Assert.That(result.Cells.Any(cell => cell.x == 2 && cell.y == 2), Is.True);
    }

    /// <summary>
    /// A goal behind a solid wall fails a strict request, while a partial request returns a path that ends on the
    /// start's side of the wall.
    /// </summary>
    [Test]
    public void AStar_ReturnsPartial_WhenGoalUnreachableAndPartialAllowed()
    {
        SetupNavigationGrid(addGapInBarrier: false);

        PathRequest strictRequest = new(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), allowPartial: false);
        PathResult strictResult = navigationGrid.Pathfinder.FindPath(strictRequest);
        Assert.That(strictResult.Success, Is.False);

        PathRequest partialRequest = new(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), allowPartial: true);
        PathResult partialResult = navigationGrid.Pathfinder.FindPath(partialRequest);

        Assert.That(partialResult.Success, Is.True);
        Assert.That(partialResult.IsPartial, Is.True);
        Assert.That(partialResult.ReachedResolvedGoal, Is.False);
        Assert.That(partialResult.Cells.Count, Is.GreaterThan(0));
        Assert.That(partialResult.Cells[^1].x, Is.LessThanOrEqualTo(1));
    }

    /// <summary>
    /// After one warm-up search, finding a path into a reused cell list allocates no managed memory.
    /// </summary>
    [Test]
    public void AStar_DoesNotAllocate_OnceWarmedUp()
    {
        SetupNavigationGrid(addGapInBarrier: true);
        PathRequest request = new(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), allowPartial: false);
        List<Vector3Int> cells = new();
        navigationGrid.Pathfinder.FindPath(request, cells);

        TestDelegate search = () => navigationGrid.Pathfinder.FindPath(request, cells);
        Assert.That(search, Is.Not.AllocatingGCMemory());
        Assert.That(cells[^1], Is.EqualTo(new Vector3Int(4, 4, 0)));
    }

    /// <summary>
    /// A strict request to a goal in a different connectivity region fails without expanding any cells.
    /// </summary>
    [Test]
    public void AStar_FailsWithoutSearching_WhenStrictGoalIsInAnotherRegion()
    {
        SetupNavigationGrid(addGapInBarrier: false);

        PathResult result = navigationGrid.Pathfinder.FindPath(new PathRequest(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), allowPartial: false));

        Assert.That(result.Success, Is.False);
        Assert.That(((GridAStarPathfinder2D)navigationGrid.Pathfinder).LastExpandedCount, Is.Zero);
    }

    /// <summary>
    /// A partial search toward an unreachable goal stops after <see cref="NavigationGrid2D.MaxSearchCells"/>
    /// expansions and still returns a partial path.
    /// </summary>
    [Test]
    public void AStar_CapsPartialSearchTowardUnreachableGoal()
    {
        SetupNavigationGrid(addGapInBarrier: false);
        navigationGrid.MaxSearchCells = 3;
        GridAStarPathfinder2D pathfinder = (GridAStarPathfinder2D)navigationGrid.Pathfinder;

        PathResult partial = pathfinder.FindPath(new PathRequest(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), allowPartial: true));

        Assert.That(pathfinder.LastExpandedCount, Is.EqualTo(3));
        Assert.That(partial.Success, Is.True);
        Assert.That(partial.IsPartial, Is.True);
        Assert.That(partial.ReachedResolvedGoal, Is.False);
    }

    /// <summary>
    /// The search cell cap only applies toward unreachable goals, so a reachable goal is still found in full.
    /// </summary>
    [Test]
    public void AStar_IgnoresSearchCap_WhenGoalIsReachable()
    {
        SetupNavigationGrid(addGapInBarrier: true);
        navigationGrid.MaxSearchCells = 3;

        PathResult result = navigationGrid.Pathfinder.FindPath(new PathRequest(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), allowPartial: false));

        Assert.That(result.Success, Is.True);
        Assert.That(result.ReachedResolvedGoal, Is.True);
    }

    /// <summary>
    /// A goal outside the walkable grid is moved to a nearby walkable cell, and the partial path reaches that
    /// adjusted goal.
    /// </summary>
    [Test]
    public void AStar_ReturnsPartial_WhenGoalIsOutsideWalkableGrid_AndPartialAllowed()
    {
        SetupNavigationGrid(addGapInBarrier: true);

        PathRequest request = new(new Vector3Int(0, 0, 0), new Vector3Int(8, 8, 0), allowPartial: true);
        PathResult result = navigationGrid.Pathfinder.FindPath(request);

        Assert.That(result.Success, Is.True);
        Assert.That(result.IsPartial, Is.True);
        Assert.That(result.GoalWasAdjusted, Is.True);
        Assert.That(result.ReachedResolvedGoal, Is.True);
        Assert.That(navigationGrid.IsCellWalkable(result.Cells[^1]), Is.True);
    }

    /// <summary>
    /// On open ground with many equal-cost routes, A* expands at most twice as many cells as the path holds.
    /// </summary>
    [Test]
    public void AStar_ExpandsFewCellsBeyondThePath_OnOpenGround()
    {
        root = CreateGridRoot("NavGridOpenGroundRoot");
        Tilemap dataTilemap = CreateTilemapObject("GroundData");
        fillTile = ScriptableObject.CreateInstance<Tile>();
        groundTerrain = CreateTerrain("ground");
        for (int x = 0; x < 32; x++)
        {
            for (int y = 0; y < 32; y++)
            {
                dataTilemap.SetTile(new Vector3Int(x, y, 0), fillTile);
            }
        }

        AttachTerrainSource(dataTilemap, groundTerrain);
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        PathResult result = navigationGrid.Pathfinder.FindPath(new PathRequest(new Vector3Int(0, 0, 0), new Vector3Int(31, 17, 0), allowPartial: false));
        int expanded = ((GridAStarPathfinder2D)navigationGrid.Pathfinder).LastExpandedCount;

        Assert.That(result.Success, Is.True);
        // Every cell inside the (0,0)-(31,17) parallelogram ties on cost; a search that widens across
        // the ties expands hundreds of them.
        Assert.That(expanded, Is.LessThanOrEqualTo(result.Cells.Count * 2));
    }

    /// <summary>
    /// A diagonal step between two blocked orthogonal neighbors is not allowed, so the path fails.
    /// </summary>
    [Test]
    public void AStar_DoesNotCutCorners_OnDiagonal()
    {
        root = CreateGridRoot("NavGridCornerTestRoot");

        Tilemap dataTilemap = CreateTilemapObject("GroundData");
        Tilemap collisionTilemap = CreateTilemapObject("GroundCollision");

        fillTile = ScriptableObject.CreateInstance<Tile>();
        blockTile = ScriptableObject.CreateInstance<Tile>();
        groundTerrain = CreateTerrain("ground");

        dataTilemap.SetTile(new Vector3Int(0, 0, 0), fillTile);
        dataTilemap.SetTile(new Vector3Int(1, 0, 0), fillTile);
        dataTilemap.SetTile(new Vector3Int(0, 1, 0), fillTile);
        dataTilemap.SetTile(new Vector3Int(1, 1, 0), fillTile);

        collisionTilemap.SetTile(new Vector3Int(1, 0, 0), blockTile);
        collisionTilemap.SetTile(new Vector3Int(0, 1, 0), blockTile);

        AttachTerrainSource(dataTilemap, groundTerrain);
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.ConfigureBlockingTilemaps(collisionTilemap);
        navigationGrid.BuildGrid();

        PathRequest request = new(new Vector3Int(0, 0, 0), new Vector3Int(1, 1, 0), allowPartial: false);
        PathResult result = navigationGrid.Pathfinder.FindPath(request);

        Assert.That(result.Success, Is.False);
    }

    /// <summary>
    /// The same water cell is walkable and pathable for an amphibious profile but blocked for a land profile.
    /// </summary>
    [Test]
    public void TerrainProfiles_CanAllowAndBlockSameCellDifferently()
    {
        root = CreateGridRoot("TerrainAccessRoot");
        fillTile = ScriptableObject.CreateInstance<Tile>();

        groundTerrain = CreateTerrain("ground");
        waterTerrain = CreateTerrain("water");
        landProfile = CreateProfile(false, Rule(groundTerrain, true, 10));
        amphibiousProfile = CreateProfile(false, Rule(groundTerrain, true, 10), Rule(waterTerrain, true, 16));

        Tilemap groundMap = CreateTilemapObject("Ground");
        Tilemap waterMap = CreateTilemapObject("Water");
        groundMap.SetTile(new Vector3Int(0, 0, 0), fillTile);
        waterMap.SetTile(new Vector3Int(1, 0, 0), fillTile);

        AttachTerrainSource(groundMap, groundTerrain);
        AttachTerrainSource(waterMap, waterTerrain);

        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(1, 0, 0), amphibiousProfile), Is.True);
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(1, 0, 0), landProfile), Is.False);

        PathResult allowedPath = navigationGrid.Pathfinder.FindPath(new PathRequest(new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), false, amphibiousProfile));
        PathResult blockedPath = navigationGrid.Pathfinder.FindPath(new PathRequest(new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), false, landProfile));

        Assert.That(allowedPath.Success, Is.True);
        Assert.That(blockedPath.Success, Is.False);
    }

    /// <summary>
    /// A* detours over cheaper ground instead of crossing a straight but costly water corridor.
    /// </summary>
    [Test]
    public void AStar_PrefersCheaperTerrain_WhenMultipleRoutesExist()
    {
        root = CreateGridRoot("TerrainCostRoot");
        fillTile = ScriptableObject.CreateInstance<Tile>();

        groundTerrain = CreateTerrain("ground");
        waterTerrain = CreateTerrain("water");
        landProfile = CreateProfile(false, Rule(groundTerrain, true, 10), Rule(waterTerrain, true, 45));

        Tilemap groundMap = CreateTilemapObject("Ground");
        Tilemap waterMap = CreateTilemapObject("Water");

        for (int x = 0; x <= 4; x++)
        {
            for (int y = 0; y <= 2; y++)
            {
                groundMap.SetTile(new Vector3Int(x, y, 0), fillTile);
            }
        }

        for (int x = 1; x <= 3; x++)
        {
            waterMap.SetTile(new Vector3Int(x, 1, 0), fillTile);
        }

        AttachTerrainSource(groundMap, groundTerrain);
        AttachTerrainSource(waterMap, waterTerrain);

        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        PathResult result = navigationGrid.Pathfinder.FindPath(new PathRequest(new Vector3Int(0, 1, 0), new Vector3Int(4, 1, 0), false, landProfile));

        Assert.That(result.Success, Is.True);
        Assert.That(result.Cells.Any(cell => cell.y != 1), Is.True, "Expected the cheaper route to detour around costly water cells.");
        Assert.That(result.Cells.Any(cell => cell.y == 1 && cell.x > 0 && cell.x < 4), Is.False, "Expected the chosen path to avoid the expensive water corridor.");
    }

    /// <summary>
    /// A cell covered by two terrain layers of equal priority is blocked if either layer blocks it and costs the
    /// higher of the two traversal costs.
    /// </summary>
    [Test]
    public void Overlap_UsesMostRestrictiveAccessAndHighestCost()
    {
        root = CreateGridRoot("TerrainOverlapRoot");
        fillTile = ScriptableObject.CreateInstance<Tile>();

        groundTerrain = CreateTerrain("ground");
        waterTerrain = CreateTerrain("water");
        landProfile = CreateProfile(false, Rule(groundTerrain, true, 10), Rule(waterTerrain, false, 25));
        amphibiousProfile = CreateProfile(false, Rule(groundTerrain, true, 10), Rule(waterTerrain, true, 35));

        Tilemap groundMap = CreateTilemapObject("Ground");
        Tilemap waterMap = CreateTilemapObject("Water");
        groundMap.SetTile(new Vector3Int(0, 0, 0), fillTile);
        groundMap.SetTile(new Vector3Int(1, 0, 0), fillTile);
        waterMap.SetTile(new Vector3Int(1, 0, 0), fillTile);

        AttachTerrainSource(groundMap, groundTerrain);
        AttachTerrainSource(waterMap, waterTerrain);

        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        Vector3Int overlappedCell = new(1, 0, 0);
        Assert.That(navigationGrid.IsCellWalkable(overlappedCell, landProfile), Is.False);
        Assert.That(navigationGrid.IsCellWalkable(overlappedCell, amphibiousProfile), Is.True);
        Assert.That(navigationGrid.MovementCost(Vector3Int.zero, overlappedCell, amphibiousProfile), Is.EqualTo(35));
    }

    /// <summary>
    /// Calling <see cref="NavigationGrid2D.BuildGrid"/> again after editing the tilemaps picks up newly blocked
    /// and newly painted cells and recomputes connectivity.
    /// </summary>
    [Test]
    public void BuildGrid_AfterTheTilemapsChange_RebakesCellsAndRegions()
    {
        SetupNavigationGrid(addGapInBarrier: true);
        Vector3Int gap = new(2, 2, 0);
        Vector3Int west = new(0, 2, 0);
        Vector3Int east = new(4, 2, 0);
        Assert.That(navigationGrid.AreCellsConnected(west, east, null), Is.True);

        Tilemap collisionTilemap = navigationGrid.BlockingTilemaps[0];
        collisionTilemap.SetTile(gap, blockTile);
        Tilemap dataTilemap = navigationGrid.GetComponentInChildren<NavigationTerrainSource2D>().DataTilemap;
        dataTilemap.SetTile(new Vector3Int(6, 6, 0), fillTile);
        navigationGrid.BuildGrid();

        Assert.That(navigationGrid.IsCellWalkable(gap), Is.False);
        Assert.That(navigationGrid.AreCellsConnected(west, east, null), Is.False);
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(6, 6, 0)), Is.True);
        Assert.That(navigationGrid.Pathfinder.FindPath(new PathRequest(west, east, allowPartial: false)).Success, Is.False);
    }

    /// <summary>
    /// A tile on a blocking tilemap makes the cell unwalkable for every profile, whatever terrain lies under it.
    /// </summary>
    [Test]
    public void BlockingTilemap_BlocksTheCellForEveryTerrainLayer()
    {
        root = CreateGridRoot("BlockingLayersRoot");
        fillTile = ScriptableObject.CreateInstance<Tile>();
        blockTile = ScriptableObject.CreateInstance<Tile>();

        groundTerrain = CreateTerrain("ground");
        waterTerrain = CreateTerrain("water");
        amphibiousProfile = CreateProfile(false, Rule(groundTerrain, true, 10), Rule(waterTerrain, true, 16));

        Tilemap groundMap = CreateTilemapObject("Ground");
        Tilemap waterMap = CreateTilemapObject("Water");
        Tilemap collisionMap = CreateTilemapObject("Collision");
        Vector3Int wall = new(1, 0, 0);
        groundMap.SetTile(Vector3Int.zero, fillTile);
        groundMap.SetTile(wall, fillTile);
        waterMap.SetTile(wall, fillTile);
        collisionMap.SetTile(wall, blockTile);

        AttachTerrainSource(groundMap, groundTerrain);
        AttachTerrainSource(waterMap, waterTerrain);
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.ConfigureBlockingTilemaps(collisionMap);
        navigationGrid.BuildGrid();

        Assert.That(navigationGrid.IsCellWalkable(Vector3Int.zero), Is.True);
        Assert.That(navigationGrid.IsCellWalkable(wall), Is.False);
        Assert.That(navigationGrid.IsCellWalkable(wall, amphibiousProfile), Is.False);
    }

    /// <summary>
    /// The cliff of raised ground blocks every cell of the plateau's rim (raised ground beside level ground) but its
    /// ramp, so the top can be reached only over the ramp, and a path up goes over it; turning the ramp into raised
    /// ground on the raised tilemap reaches a built grid at once and cuts the top off. A plateau covers columns 2 to 7
    /// of rows 3 to 7 of a 10 x 10 ground, with a ramp in columns 4 and 5 of row 3.
    /// </summary>
    [Test]
    public void RaisedGround_ItsCliffBlocks_AndOnlyItsRampLeadsUp()
    {
        SetupOpenGround(10);
        Tilemap raisedMap = CreateTilemapObject("Raised");
        RaisedGroundTile raised = RaisedGroundTile.Create(false);
        RaisedGroundTile ramp = RaisedGroundTile.Create(true);
        try
        {
            for (int y = 3; y <= 7; y++)
            {
                for (int x = 2; x <= 7; x++)
                {
                    raisedMap.SetTile(new Vector3Int(x, y, 0), (x == 4 || x == 5) && y == 3 ? ramp : raised);
                }
            }

            navigationGrid.ConfigureRaisedTilemap(raisedMap);
            navigationGrid.BuildGrid();

            Vector3Int below = new(0, 0, 0);
            Vector3Int top = new(4, 5, 0);
            foreach (Vector3Int rim in new[] { new Vector3Int(2, 3, 0), new Vector3Int(3, 3, 0), new Vector3Int(6, 3, 0), new Vector3Int(2, 5, 0), new Vector3Int(7, 5, 0), new Vector3Int(4, 7, 0) })
            {
                Assert.That(navigationGrid.IsCellWalkable(rim), Is.False, $"the cliff at {rim}");
            }

            Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(4, 3, 0)), Is.True, "the ramp");
            Assert.That(navigationGrid.IsCellWalkable(top), Is.True, "the top");
            Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(4, 2, 0)), Is.True, "the ground below");
            PathResult path = navigationGrid.Pathfinder.FindPath(new PathRequest(below, top, allowPartial: false));
            Assert.That(path.Success, Is.True);
            Assert.That(path.Cells.Any(cell => cell.y == 3 && (cell.x == 4 || cell.x == 5)), Is.True, "The path goes up the ramp.");

            raisedMap.SetTile(new Vector3Int(4, 3, 0), raised);
            raisedMap.SetTile(new Vector3Int(5, 3, 0), raised);

            Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(4, 3, 0)), Is.False, "the ramp turned to cliff");
            Assert.That(navigationGrid.AreCellsConnected(below, top, null), Is.False, "The top is cut off.");
        }
        finally
        {
            Object.DestroyImmediate(raised);
            Object.DestroyImmediate(ramp);
        }
    }

    /// <summary>
    /// A collider on an obstacle layer blocks every cell it overlaps but not a cell it only touches on an edge, and its
    /// cells stay blocked when refreshed; colliders on other layers, triggers and a tilemap's own collider block
    /// nothing.
    /// </summary>
    [Test]
    public void ObstacleLayers_BlockTheCellsUnderAColliderOnThem()
    {
        int obstacles = LayerMask.NameToLayer("Obstacles");
        SetupOpenGround(5);
        CreateBox("Footprint", obstacles, new Vector2(2.5f, 2.3f), new Vector2(1.2f, 0.4f), isTrigger: false);
        CreateBox("CellSized", obstacles, new Vector2(0.5f, 4.5f), Vector2.one, isTrigger: false);
        CreateBox("OtherLayer", 0, new Vector2(4.5f, 0.5f), Vector2.one, isTrigger: false);
        CreateBox("Trigger", obstacles, new Vector2(4.5f, 4.5f), Vector2.one, isTrigger: true);
        CreateTilemapCollider(obstacles, new Vector3Int(0, 0, 0), new Vector3Int(3, 3, 0));
        navigationGrid.ConfigureObstacleLayers(1 << obstacles);

        navigationGrid.BuildGrid();

        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(1, 2, 0)), Is.False);
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(2, 2, 0)), Is.False);
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(3, 2, 0)), Is.False);
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(2, 1, 0)), Is.True);
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(2, 3, 0)), Is.True);
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(0, 4, 0)), Is.False);
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(1, 4, 0)), Is.True, "Touching a cell's edge does not block it.");
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(0, 3, 0)), Is.True, "Touching a cell's edge does not block it.");
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(4, 0, 0)), Is.True, "Other layers do not block.");
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(4, 4, 0)), Is.True, "Triggers do not block.");
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(0, 0, 0)), Is.True, "Tilemap colliders are left to blocking tilemaps.");
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(1, 1, 0)), Is.True, "A tilemap collider's bounds block nothing.");

        navigationGrid.RefreshCells(new[] { new Vector3Int(2, 2, 0) });
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(2, 2, 0)), Is.False, "A refresh keeps the obstacle's cell blocked.");
    }

    /// <summary>
    /// With no obstacle layers set, colliders block nothing.
    /// </summary>
    [Test]
    public void WithoutObstacleLayers_CollidersBlockNothing()
    {
        SetupOpenGround(3);
        CreateBox("Footprint", LayerMask.NameToLayer("Obstacles"), new Vector2(1.5f, 1.5f), Vector2.one, isTrigger: false);

        navigationGrid.BuildGrid();

        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(1, 1, 0)), Is.True);
    }

    /// <summary>
    /// A terrain source placed on a dual-grid render tilemap logs an error and contributes no cells; only the
    /// data tilemap's cells become walkable.
    /// </summary>
    [Test]
    public void TerrainSource_OnADualGridRenderTilemap_IsSkipped()
    {
        root = CreateGridRoot("DualGridSourceRoot");
        fillTile = ScriptableObject.CreateInstance<Tile>();
        groundTerrain = CreateTerrain("ground");

        Tilemap dataMap = CreateTilemapObject("Data");
        dualGridTile = ScriptableObject.CreateInstance<DualGridRuleTile>();
        dualGridTile.RefreshDataTile();
        dataMap.gameObject.AddComponent<DualGridTilemapModule>().RenderTile = dualGridTile;
        GameObject renderObject = new("Render");
        renderObject.transform.SetParent(dataMap.transform);
        Tilemap renderMap = renderObject.AddComponent<Tilemap>();

        dataMap.SetTile(Vector3Int.zero, fillTile);
        renderMap.SetTile(Vector3Int.zero, fillTile);
        renderMap.SetTile(new Vector3Int(1, 1, 0), fillTile);

        AttachTerrainSource(dataMap, groundTerrain);
        AttachTerrainSource(renderMap, groundTerrain);
        navigationGrid = root.AddComponent<NavigationGrid2D>();

        LogAssert.Expect(LogType.Error, new Regex("dual-grid render tilemap"));
        navigationGrid.BuildGrid();

        Assert.That(navigationGrid.IsCellWalkable(Vector3Int.zero), Is.True);
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(1, 1, 0)), Is.False);
    }

    /// <summary>
    /// A profile's edge cost makes cells on the edge of the ground dearer, so its paths move to the inner row,
    /// while a request without a profile still runs along the edge.
    /// </summary>
    [Test]
    public void EdgeCost_KeepsPathsOffTheEdgeOfTheGround()
    {
        root = CreateGridRoot("EdgeCostRoot");
        Tilemap dataTilemap = CreateTilemapObject("GroundData");
        fillTile = ScriptableObject.CreateInstance<Tile>();
        groundTerrain = CreateTerrain("ground");
        for (int x = 0; x < 10; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                dataTilemap.SetTile(new Vector3Int(x, y, 0), fillTile);
            }
        }

        AttachTerrainSource(dataTilemap, groundTerrain);
        landProfile = TerrainMovementProfile2D.Create(false, 10, 5, Rule(groundTerrain, true, 10));
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        Assert.That(navigationGrid.MovementCost(new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0), landProfile), Is.EqualTo(10));
        Assert.That(navigationGrid.MovementCost(new Vector3Int(1, 0, 0), new Vector3Int(2, 0, 0), landProfile), Is.EqualTo(15));

        Vector3Int start = new(0, 0, 0);
        Vector3Int goal = new(9, 0, 0);
        PathResult withoutEdgeCost = navigationGrid.Pathfinder.FindPath(new PathRequest(start, goal, allowPartial: false));
        Assert.That(withoutEdgeCost.Cells.All(cell => cell.y == 0), Is.True, "Without an edge cost the cheapest route runs along the edge.");

        PathResult withEdgeCost = navigationGrid.Pathfinder.FindPath(new PathRequest(start, goal, false, landProfile));
        Assert.That(withEdgeCost.Success, Is.True);
        Assert.That(withEdgeCost.Cells.Where(cell => cell.x >= 2 && cell.x <= 7).All(cell => cell.y == 1), Is.True,
            "With an edge cost the route moves to the inner row.");
    }

    /// <summary>
    /// A cell next to terrain the profile cannot walk counts as an edge for that profile only; a profile that can
    /// walk the terrain pays no edge cost there.
    /// </summary>
    [Test]
    public void EdgeCost_CountsCellsNextToTerrainTheProfileCannotWalk()
    {
        root = CreateGridRoot("EdgeCostTerrainRoot");
        fillTile = ScriptableObject.CreateInstance<Tile>();
        groundTerrain = CreateTerrain("ground");
        waterTerrain = CreateTerrain("water");
        landProfile = TerrainMovementProfile2D.Create(false, 10, 5, Rule(groundTerrain, true, 10), Rule(waterTerrain, false, 10));
        amphibiousProfile = TerrainMovementProfile2D.Create(false, 10, 5, Rule(groundTerrain, true, 10), Rule(waterTerrain, true, 10));

        Tilemap groundMap = CreateTilemapObject("Ground");
        Tilemap waterMap = CreateTilemapObject("Water");
        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                if (x == 2 && y == 2)
                {
                    waterMap.SetTile(new Vector3Int(x, y, 0), fillTile);
                }
                else
                {
                    groundMap.SetTile(new Vector3Int(x, y, 0), fillTile);
                }
            }
        }

        AttachTerrainSource(groundMap, groundTerrain);
        AttachTerrainSource(waterMap, waterTerrain);
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        Vector3Int besideWater = new(1, 1, 0);
        Assert.That(navigationGrid.MovementCost(new Vector3Int(0, 1, 0), besideWater, landProfile), Is.EqualTo(15));
        Assert.That(navigationGrid.MovementCost(new Vector3Int(0, 1, 0), besideWater, amphibiousProfile), Is.EqualTo(10));
    }

    /// <summary>
    /// Equal-priority layers keep the most restrictive rule, while a higher-priority bridge layer painted over
    /// water alone decides the cell's access and cost.
    /// </summary>
    [Test]
    public void Priority_TheTopLayerDecidesOverlappingCells()
    {
        root = CreateGridRoot("PriorityRoot");
        fillTile = ScriptableObject.CreateInstance<Tile>();
        groundTerrain = CreateTerrain("ground");
        waterTerrain = CreateTerrain("water");
        TerrainType2D bridgeTerrain = CreateTerrain("bridge");
        landProfile = CreateProfile(false, Rule(groundTerrain, true, 10), Rule(waterTerrain, false, 10), Rule(bridgeTerrain, true, 12));

        Tilemap groundMap = CreateTilemapObject("Ground");
        Tilemap waterMap = CreateTilemapObject("Water");
        Tilemap bridgeMap = CreateTilemapObject("Bridge");
        Vector3Int west = new(0, 0, 0);
        Vector3Int river = new(1, 0, 0);
        Vector3Int east = new(2, 0, 0);
        groundMap.SetTile(west, fillTile);
        groundMap.SetTile(river, fillTile);
        groundMap.SetTile(east, fillTile);
        waterMap.SetTile(river, fillTile);

        AttachTerrainSource(groundMap, groundTerrain);
        AttachTerrainSource(waterMap, waterTerrain);
        bridgeMap.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(bridgeTerrain, layerPriority: 1);
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        Assert.That(navigationGrid.IsCellWalkable(river, landProfile), Is.False, "Equal priorities keep the most restrictive rule.");

        bridgeMap.SetTile(river, fillTile);
        navigationGrid.RefreshCells(new[] { river });

        Assert.That(navigationGrid.IsCellWalkable(river, landProfile), Is.True, "The bridge on top decides the cell.");
        Assert.That(navigationGrid.MovementCost(west, river, landProfile), Is.EqualTo(12));
        Assert.That(navigationGrid.AreCellsConnected(west, east, landProfile), Is.True);
        Object.DestroyImmediate(bridgeTerrain);
    }

    /// <summary>
    /// Refreshing a single cell inside the bounds updates its walkability, connectivity and paths in place,
    /// bumps the grid version and leaves the bounds unchanged.
    /// </summary>
    [Test]
    public void RefreshCells_UpdatesOnlyThoseCells_AndBumpsTheVersion()
    {
        SetupNavigationGrid(addGapInBarrier: true);
        Vector3Int gap = new(2, 2, 0);
        Vector3Int west = new(0, 2, 0);
        Vector3Int east = new(4, 2, 0);
        int versionBefore = navigationGrid.Version;
        BoundsInt boundsBefore = navigationGrid.WalkableBounds;

        navigationGrid.BlockingTilemaps[0].SetTile(gap, blockTile);
        navigationGrid.RefreshCells(new[] { gap });

        Assert.That(navigationGrid.Version, Is.GreaterThan(versionBefore));
        Assert.That(navigationGrid.WalkableBounds, Is.EqualTo(boundsBefore));
        Assert.That(navigationGrid.IsCellWalkable(gap), Is.False);
        Assert.That(navigationGrid.AreCellsConnected(west, east, null), Is.False);
        Assert.That(navigationGrid.Pathfinder.FindPath(new PathRequest(west, east, allowPartial: false)).Success, Is.False);

        navigationGrid.BlockingTilemaps[0].SetTile(gap, null);
        navigationGrid.RefreshCells(new[] { gap });

        Assert.That(navigationGrid.Pathfinder.FindPath(new PathRequest(west, east, allowPartial: false)).Success, Is.True);
    }

    /// <summary>
    /// Refreshing a cell outside the current bounds rebuilds the grid so the bounds grow to include it.
    /// </summary>
    [Test]
    public void RefreshCells_OutsideTheBounds_RebuildsTheGrid()
    {
        SetupNavigationGrid(addGapInBarrier: true);
        Vector3Int outside = new(5, 0, 0);
        Tilemap dataTilemap = navigationGrid.GetComponentInChildren<NavigationTerrainSource2D>().DataTilemap;

        dataTilemap.SetTile(outside, fillTile);
        navigationGrid.RefreshCells(new[] { outside });

        Assert.That(navigationGrid.IsCellWalkable(outside), Is.True);
        Assert.That(navigationGrid.WalkableBounds.xMax, Is.EqualTo(6));
        Assert.That(navigationGrid.Pathfinder.FindPath(new PathRequest(Vector3Int.zero, outside, allowPartial: false)).Success, Is.True);
    }

    /// <summary>
    /// A dual-grid render tilemap configured as a blocking tilemap logs an error and blocks nothing.
    /// </summary>
    [Test]
    public void BlockingTilemap_OnADualGridRenderTilemap_IsIgnored()
    {
        root = CreateGridRoot("DualGridBlockerRoot");
        fillTile = ScriptableObject.CreateInstance<Tile>();
        groundTerrain = CreateTerrain("ground");
        Tilemap groundMap = CreateTilemapObject("Ground");
        groundMap.SetTile(Vector3Int.zero, fillTile);
        AttachTerrainSource(groundMap, groundTerrain);

        Tilemap wallData = CreateTilemapObject("Walls");
        dualGridTile = ScriptableObject.CreateInstance<DualGridRuleTile>();
        dualGridTile.RefreshDataTile();
        wallData.gameObject.AddComponent<DualGridTilemapModule>().RenderTile = dualGridTile;
        GameObject renderObject = new("WallsRender");
        renderObject.transform.SetParent(wallData.transform);
        Tilemap wallRender = renderObject.AddComponent<Tilemap>();
        wallRender.SetTile(Vector3Int.zero, fillTile);

        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.ConfigureBlockingTilemaps(wallRender);
        LogAssert.Expect(LogType.Error, new Regex("dual-grid render tilemap"));
        navigationGrid.BuildGrid();

        Assert.That(navigationGrid.IsCellWalkable(Vector3Int.zero), Is.True);
    }

    /// <summary>
    /// Outside play mode, painting a tile on a blocking tilemap updates an already built grid without an
    /// explicit refresh call.
    /// </summary>
    [Test]
    public void OutsidePlayMode_PaintingATile_UpdatesABuiltGrid()
    {
        SetupNavigationGrid(addGapInBarrier: true);
        Vector3Int gap = new(2, 2, 0);
        int versionBefore = navigationGrid.Version;

        navigationGrid.BlockingTilemaps[0].SetTile(gap, blockTile);

        Assert.That(navigationGrid.IsCellWalkable(gap), Is.False, "The editor listener refreshes the grid without a RefreshCells call.");
        Assert.That(navigationGrid.Version, Is.GreaterThan(versionBefore));
    }

    /// <summary>
    /// Connectivity splits at a solid wall, treats blocked cells as unconnected, and joins both sides once the
    /// wall has a gap.
    /// </summary>
    [Test]
    public void AreCellsConnected_FollowsBarrierLayout()
    {
        SetupNavigationGrid(addGapInBarrier: false);

        Assert.That(navigationGrid.AreCellsConnected(new Vector3Int(0, 0, 0), new Vector3Int(1, 4, 0), null), Is.True);
        Assert.That(navigationGrid.AreCellsConnected(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), null), Is.False);
        Assert.That(navigationGrid.AreCellsConnected(new Vector3Int(0, 0, 0), new Vector3Int(2, 0, 0), null), Is.False, "Blocked cells belong to no region.");

        TearDown();
        SetupNavigationGrid(addGapInBarrier: true);

        Assert.That(navigationGrid.AreCellsConnected(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), null), Is.True);
    }

    /// <summary>
    /// Connectivity is computed per profile, and a profile's cached regions are relabeled when its rules are
    /// edited through the inspector data.
    /// </summary>
    [Test]
    public void AreCellsConnected_RespectsProfileRules_AndRefreshesWhenTheProfileIsEdited()
    {
        root = CreateGridRoot("ConnectivityProfileRoot");
        fillTile = ScriptableObject.CreateInstance<Tile>();

        groundTerrain = CreateTerrain("ground");
        waterTerrain = CreateTerrain("water");
        landProfile = CreateProfile(false, Rule(groundTerrain, true, 10));
        amphibiousProfile = CreateProfile(false, Rule(groundTerrain, true, 10), Rule(waterTerrain, true, 16));

        Tilemap groundMap = CreateTilemapObject("Ground");
        Tilemap waterMap = CreateTilemapObject("Water");
        groundMap.SetTile(new Vector3Int(0, 0, 0), fillTile);
        waterMap.SetTile(new Vector3Int(1, 0, 0), fillTile);
        groundMap.SetTile(new Vector3Int(2, 0, 0), fillTile);

        AttachTerrainSource(groundMap, groundTerrain);
        AttachTerrainSource(waterMap, waterTerrain);

        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        Vector3Int west = new(0, 0, 0);
        Vector3Int east = new(2, 0, 0);
        Assert.That(navigationGrid.AreCellsConnected(west, east, landProfile), Is.False);
        Assert.That(navigationGrid.AreCellsConnected(west, east, amphibiousProfile), Is.True);

        EditRule(landProfile, waterTerrain, true, 20);
        Assert.That(navigationGrid.AreCellsConnected(west, east, landProfile), Is.True, "Cached regions must refresh when the profile is edited.");
    }

    /// <summary>
    /// A profile's minimum traversal cost follows rule edits, counting only walkable rules.
    /// </summary>
    [Test]
    public void MinimumTraversalCost_RefreshesWhenTheProfileIsEdited()
    {
        groundTerrain = CreateTerrain("ground");
        waterTerrain = CreateTerrain("water");
        landProfile = CreateProfile(false, Rule(groundTerrain, true, 10));
        Assert.That(landProfile.GetMinimumTraversalCost(), Is.EqualTo(10));

        EditRule(landProfile, waterTerrain, true, 4);
        Assert.That(landProfile.GetMinimumTraversalCost(), Is.EqualTo(4));

        EditRule(landProfile, waterTerrain, false, 4);
        Assert.That(landProfile.GetMinimumTraversalCost(), Is.EqualTo(10));
    }

    /// <summary>
    /// On a seeded random grid, connectivity answers match whether a strict A* search succeeds for every sampled
    /// pair of walkable cells.
    /// </summary>
    [Test]
    public void AreCellsConnected_AgreesWithAStar_OnRandomGrid()
    {
        root = CreateGridRoot("ConnectivityRandomRoot");
        Tilemap dataTilemap = CreateTilemapObject("DataTilemap");
        Tilemap collisionTilemap = CreateTilemapObject("CollisionTilemap");
        fillTile = ScriptableObject.CreateInstance<Tile>();
        blockTile = ScriptableObject.CreateInstance<Tile>();
        groundTerrain = CreateTerrain("ground");

        const int size = 12;
        System.Random random = new(1234);
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                dataTilemap.SetTile(new Vector3Int(x, y, 0), fillTile);
                if (random.NextDouble() < 0.35)
                {
                    collisionTilemap.SetTile(new Vector3Int(x, y, 0), blockTile);
                }
            }
        }

        AttachTerrainSource(dataTilemap, groundTerrain);
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.ConfigureBlockingTilemaps(collisionTilemap);
        navigationGrid.BuildGrid();

        int reachablePairs = 0;
        int unreachablePairs = 0;
        for (int i = 0; i < 300; i++)
        {
            Vector3Int from = new(random.Next(size), random.Next(size), 0);
            Vector3Int to = new(random.Next(size), random.Next(size), 0);
            if (!navigationGrid.IsCellWalkable(from) || !navigationGrid.IsCellWalkable(to))
            {
                continue;
            }

            bool aStarReaches = navigationGrid.Pathfinder.FindPath(new PathRequest(from, to, allowPartial: false)).Success;
            Assert.That(navigationGrid.AreCellsConnected(from, to, null), Is.EqualTo(aStarReaches), $"Mismatch between {from} and {to}.");

            if (aStarReaches)
            {
                reachablePairs++;
            }
            else
            {
                unreachablePairs++;
            }
        }

        Assert.That(reachablePairs, Is.GreaterThan(0));
        Assert.That(unreachablePairs, Is.GreaterThan(0), "Seeded grid should contain disconnected regions to make this test meaningful.");
    }

    /// <summary>
    /// Builds a 5x5 ground grid split by a wall at x = 2, with a gap at y = 2 when
    /// <paramref name="addGapInBarrier"/> is true.
    /// </summary>
    private void SetupNavigationGrid(bool addGapInBarrier)
    {
        root = CreateGridRoot("NavGridTestRoot");

        Tilemap dataTilemap = CreateTilemapObject("DataTilemap");
        Tilemap collisionTilemap = CreateTilemapObject("CollisionTilemap");

        fillTile = ScriptableObject.CreateInstance<Tile>();
        blockTile = ScriptableObject.CreateInstance<Tile>();
        groundTerrain = CreateTerrain("ground");

        for (int x = 0; x < 5; x++)
        {
            for (int y = 0; y < 5; y++)
            {
                dataTilemap.SetTile(new Vector3Int(x, y, 0), fillTile);
            }
        }

        for (int y = 0; y < 5; y++)
        {
            if (addGapInBarrier && y == 2)
            {
                continue;
            }

            collisionTilemap.SetTile(new Vector3Int(2, y, 0), blockTile);
        }

        AttachTerrainSource(dataTilemap, groundTerrain);
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.ConfigureBlockingTilemaps(collisionTilemap);
        navigationGrid.BuildGrid();
    }

    /// <summary>
    /// Builds a <paramref name="size"/> x <paramref name="size"/> ground grid with no blocking tilemaps, not built yet.
    /// </summary>
    private void SetupOpenGround(int size)
    {
        root = CreateGridRoot("OpenGroundRoot");
        Tilemap ground = CreateTilemapObject("Ground");
        fillTile = ScriptableObject.CreateInstance<Tile>();
        groundTerrain = CreateTerrain("ground");
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                ground.SetTile(new Vector3Int(x, y, 0), fillTile);
            }
        }

        AttachTerrainSource(ground, groundTerrain);
        navigationGrid = root.AddComponent<NavigationGrid2D>();
    }

    /// <summary>
    /// Creates a box collider of <paramref name="size"/> centred on <paramref name="center"/> on
    /// <paramref name="layer"/> under the root.
    /// </summary>
    private void CreateBox(string name, int layer, Vector2 center, Vector2 size, bool isTrigger)
    {
        GameObject box = new(name) { layer = layer };
        box.transform.SetParent(root.transform);
        box.transform.position = center;
        BoxCollider2D collider = box.AddComponent<BoxCollider2D>();
        collider.size = size;
        collider.isTrigger = isTrigger;
    }

    /// <summary>
    /// Creates a tilemap on <paramref name="layer"/> under the root, not listed as blocking, with a grid-collider tile
    /// at each of <paramref name="cells"/> and a <see cref="TilemapCollider2D"/> built from them.
    /// </summary>
    private void CreateTilemapCollider(int layer, params Vector3Int[] cells)
    {
        Tilemap tilemap = CreateTilemapObject("TilemapCollider");
        tilemap.gameObject.layer = layer;
        blockTile = ScriptableObject.CreateInstance<Tile>();
        blockTile.colliderType = Tile.ColliderType.Grid;
        foreach (Vector3Int cell in cells)
        {
            tilemap.SetTile(cell, blockTile);
        }

        TilemapCollider2D collider = tilemap.gameObject.AddComponent<TilemapCollider2D>();
        collider.ProcessTilemapChanges();
    }

    /// <summary>
    /// Creates the root object with a <see cref="Grid"/> and stores it as the test's root.
    /// </summary>
    private GameObject CreateGridRoot(string name)
    {
        root = new GameObject(name);
        root.AddComponent<Grid>();
        return root;
    }

    /// <summary>
    /// Creates an empty tilemap with a renderer under the root.
    /// </summary>
    private Tilemap CreateTilemapObject(string name)
    {
        GameObject tilemapObject = new(name);
        tilemapObject.transform.SetParent(root.transform);
        Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
        tilemapObject.AddComponent<TilemapRenderer>();
        return tilemap;
    }

    /// <summary>
    /// Marks every tile on <paramref name="dataTilemap"/> as <paramref name="terrainType"/> for the grid, at the
    /// default layer priority.
    /// </summary>
    private NavigationTerrainSource2D AttachTerrainSource(Tilemap dataTilemap, TerrainType2D terrainType)
    {
        NavigationTerrainSource2D source = dataTilemap.gameObject.AddComponent<NavigationTerrainSource2D>();
        source.Configure(terrainType);
        return source;
    }

    /// <summary>
    /// Creates an in-memory terrain type with the given id; the caller destroys it.
    /// </summary>
    private static TerrainType2D CreateTerrain(string id)
    {
        return TerrainType2D.Create(id);
    }

    /// <summary>
    /// Creates an in-memory movement profile with a default traversal cost of 10 and the given terrain rules.
    /// </summary>
    private static TerrainMovementProfile2D CreateProfile(bool defaultWalkable, params TerrainMovementProfile2D.TerrainRule[] rules)
    {
        return TerrainMovementProfile2D.Create(defaultWalkable, 10, rules);
    }

    /// <summary>
    /// Shorthand for a terrain rule giving <paramref name="terrain"/> its access and traversal cost.
    /// </summary>
    private static TerrainMovementProfile2D.TerrainRule Rule(TerrainType2D terrain, bool isWalkable, int traversalCost)
    {
        return new TerrainMovementProfile2D.TerrainRule(terrain, isWalkable, traversalCost);
    }

    /// <summary>
    /// Sets or adds the rule for <paramref name="terrain"/> the way the inspector does, through the serialized data.
    /// </summary>
    private static void EditRule(TerrainMovementProfile2D profile, TerrainType2D terrain, bool isWalkable, int traversalCost)
    {
        SerializedObject serialized = new(profile);
        SerializedProperty rules = serialized.FindProperty("terrainRules");
        SerializedProperty rule = null;
        for (int i = 0; i < rules.arraySize && rule == null; i++)
        {
            SerializedProperty candidate = rules.GetArrayElementAtIndex(i);
            if (candidate.FindPropertyRelative("terrainType").objectReferenceValue == terrain)
            {
                rule = candidate;
            }
        }

        if (rule == null)
        {
            rules.arraySize++;
            rule = rules.GetArrayElementAtIndex(rules.arraySize - 1);
            rule.FindPropertyRelative("terrainType").objectReferenceValue = terrain;
        }

        rule.FindPropertyRelative("isWalkable").boolValue = isWalkable;
        rule.FindPropertyRelative("traversalCost").intValue = traversalCost;
        serialized.ApplyModifiedProperties();
    }
}
