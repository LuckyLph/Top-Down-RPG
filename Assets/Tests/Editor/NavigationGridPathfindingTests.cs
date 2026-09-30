using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

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
    }

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

        AttachTerrainSource(dataTilemap, null, null, groundTerrain);
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        PathResult result = navigationGrid.Pathfinder.FindPath(new PathRequest(new Vector3Int(0, 0, 0), new Vector3Int(31, 17, 0), allowPartial: false));
        int expanded = ((GridAStarPathfinder2D)navigationGrid.Pathfinder).LastExpandedCount;

        Assert.That(result.Success, Is.True);
        // Every cell inside the (0,0)-(31,17) parallelogram ties on cost; a search that widens across
        // the ties expands hundreds of them.
        Assert.That(expanded, Is.LessThanOrEqualTo(result.Cells.Count * 2));
    }

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

        AttachTerrainSource(dataTilemap, collisionTilemap, null, groundTerrain);
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        PathRequest request = new(new Vector3Int(0, 0, 0), new Vector3Int(1, 1, 0), allowPartial: false);
        PathResult result = navigationGrid.Pathfinder.FindPath(request);

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public void TerrainProfiles_CanAllowAndBlockSameCellDifferently()
    {
        root = CreateGridRoot("TerrainAccessRoot");
        fillTile = ScriptableObject.CreateInstance<Tile>();

        groundTerrain = CreateTerrain("ground");
        waterTerrain = CreateTerrain("water");
        landProfile = CreateProfile(defaultWalkable: false);
        landProfile.SetTerrainRule(groundTerrain, true, 10);
        amphibiousProfile = CreateProfile(defaultWalkable: false);
        amphibiousProfile.SetTerrainRule(groundTerrain, true, 10);
        amphibiousProfile.SetTerrainRule(waterTerrain, true, 16);

        Tilemap groundMap = CreateTilemapObject("Ground");
        Tilemap waterMap = CreateTilemapObject("Water");
        groundMap.SetTile(new Vector3Int(0, 0, 0), fillTile);
        waterMap.SetTile(new Vector3Int(1, 0, 0), fillTile);

        AttachTerrainSource(groundMap, null, null, groundTerrain);
        AttachTerrainSource(waterMap, null, null, waterTerrain);

        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(1, 0, 0), amphibiousProfile), Is.True);
        Assert.That(navigationGrid.IsCellWalkable(new Vector3Int(1, 0, 0), landProfile), Is.False);

        PathResult allowedPath = navigationGrid.Pathfinder.FindPath(new PathRequest(new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), false, amphibiousProfile));
        PathResult blockedPath = navigationGrid.Pathfinder.FindPath(new PathRequest(new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), false, landProfile));

        Assert.That(allowedPath.Success, Is.True);
        Assert.That(blockedPath.Success, Is.False);
    }

    [Test]
    public void AStar_PrefersCheaperTerrain_WhenMultipleRoutesExist()
    {
        root = CreateGridRoot("TerrainCostRoot");
        fillTile = ScriptableObject.CreateInstance<Tile>();

        groundTerrain = CreateTerrain("ground");
        waterTerrain = CreateTerrain("water");
        landProfile = CreateProfile(defaultWalkable: false);
        landProfile.SetTerrainRule(groundTerrain, true, 10);
        landProfile.SetTerrainRule(waterTerrain, true, 45);

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

        AttachTerrainSource(groundMap, null, null, groundTerrain);
        AttachTerrainSource(waterMap, null, null, waterTerrain);

        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        PathResult result = navigationGrid.Pathfinder.FindPath(new PathRequest(new Vector3Int(0, 1, 0), new Vector3Int(4, 1, 0), false, landProfile));

        Assert.That(result.Success, Is.True);
        Assert.That(result.Cells.Any(cell => cell.y != 1), Is.True, "Expected the cheaper route to detour around costly water cells.");
        Assert.That(result.Cells.Any(cell => cell.y == 1 && cell.x > 0 && cell.x < 4), Is.False, "Expected the chosen path to avoid the expensive water corridor.");
    }

    [Test]
    public void Overlap_UsesMostRestrictiveAccessAndHighestCost()
    {
        root = CreateGridRoot("TerrainOverlapRoot");
        fillTile = ScriptableObject.CreateInstance<Tile>();

        groundTerrain = CreateTerrain("ground");
        waterTerrain = CreateTerrain("water");
        landProfile = CreateProfile(defaultWalkable: false);
        landProfile.SetTerrainRule(groundTerrain, true, 10);
        landProfile.SetTerrainRule(waterTerrain, false, 25);
        amphibiousProfile = CreateProfile(defaultWalkable: false);
        amphibiousProfile.SetTerrainRule(groundTerrain, true, 10);
        amphibiousProfile.SetTerrainRule(waterTerrain, true, 35);

        Tilemap groundMap = CreateTilemapObject("Ground");
        Tilemap waterMap = CreateTilemapObject("Water");
        groundMap.SetTile(new Vector3Int(0, 0, 0), fillTile);
        groundMap.SetTile(new Vector3Int(1, 0, 0), fillTile);
        waterMap.SetTile(new Vector3Int(1, 0, 0), fillTile);

        AttachTerrainSource(groundMap, null, null, groundTerrain);
        AttachTerrainSource(waterMap, null, null, waterTerrain);

        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        Vector3Int overlappedCell = new(1, 0, 0);
        Assert.That(navigationGrid.IsCellWalkable(overlappedCell, landProfile), Is.False);
        Assert.That(navigationGrid.IsCellWalkable(overlappedCell, amphibiousProfile), Is.True);
        Assert.That(navigationGrid.MovementCost(Vector3Int.zero, overlappedCell, amphibiousProfile), Is.EqualTo(35));
    }

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

    [Test]
    public void AreCellsConnected_RespectsProfileRulesAndRuleChanges()
    {
        root = CreateGridRoot("ConnectivityProfileRoot");
        fillTile = ScriptableObject.CreateInstance<Tile>();

        groundTerrain = CreateTerrain("ground");
        waterTerrain = CreateTerrain("water");
        landProfile = CreateProfile(defaultWalkable: false);
        landProfile.SetTerrainRule(groundTerrain, true, 10);
        amphibiousProfile = CreateProfile(defaultWalkable: false);
        amphibiousProfile.SetTerrainRule(groundTerrain, true, 10);
        amphibiousProfile.SetTerrainRule(waterTerrain, true, 16);

        Tilemap groundMap = CreateTilemapObject("Ground");
        Tilemap waterMap = CreateTilemapObject("Water");
        groundMap.SetTile(new Vector3Int(0, 0, 0), fillTile);
        waterMap.SetTile(new Vector3Int(1, 0, 0), fillTile);
        groundMap.SetTile(new Vector3Int(2, 0, 0), fillTile);

        AttachTerrainSource(groundMap, null, null, groundTerrain);
        AttachTerrainSource(waterMap, null, null, waterTerrain);

        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();

        Vector3Int west = new(0, 0, 0);
        Vector3Int east = new(2, 0, 0);
        Assert.That(navigationGrid.AreCellsConnected(west, east, landProfile), Is.False);
        Assert.That(navigationGrid.AreCellsConnected(west, east, amphibiousProfile), Is.True);

        landProfile.SetTerrainRule(waterTerrain, true, 20);
        Assert.That(navigationGrid.AreCellsConnected(west, east, landProfile), Is.True, "Cached regions must refresh when profile rules change.");
    }

    [Test]
    public void MinimumTraversalCost_RefreshesWhenRulesChange()
    {
        groundTerrain = CreateTerrain("ground");
        waterTerrain = CreateTerrain("water");
        landProfile = CreateProfile(defaultWalkable: false);
        landProfile.SetTerrainRule(groundTerrain, true, 10);
        Assert.That(landProfile.GetMinimumTraversalCost(), Is.EqualTo(10));

        landProfile.SetTerrainRule(waterTerrain, true, 4);
        Assert.That(landProfile.GetMinimumTraversalCost(), Is.EqualTo(4));

        landProfile.SetTerrainRule(waterTerrain, false, 4);
        Assert.That(landProfile.GetMinimumTraversalCost(), Is.EqualTo(10));
    }

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

        AttachTerrainSource(dataTilemap, collisionTilemap, null, groundTerrain);
        navigationGrid = root.AddComponent<NavigationGrid2D>();
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

        AttachTerrainSource(dataTilemap, collisionTilemap, null, groundTerrain);
        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();
    }

    private GameObject CreateGridRoot(string name)
    {
        root = new GameObject(name);
        root.AddComponent<Grid>();
        return root;
    }

    private Tilemap CreateTilemapObject(string name)
    {
        GameObject tilemapObject = new(name);
        tilemapObject.transform.SetParent(root.transform);
        Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
        tilemapObject.AddComponent<TilemapRenderer>();
        return tilemap;
    }

    private NavigationTerrainSource2D AttachTerrainSource(Tilemap dataTilemap, Tilemap collisionTilemap, Tilemap renderTilemap, TerrainType2D terrainType)
    {
        NavigationTerrainSource2D source = dataTilemap.gameObject.AddComponent<NavigationTerrainSource2D>();
        source.Configure(dataTilemap, collisionTilemap, renderTilemap, terrainType);
        return source;
    }

    private static TerrainType2D CreateTerrain(string id)
    {
        TerrainType2D terrain = ScriptableObject.CreateInstance<TerrainType2D>();
        terrain.Configure(id);
        return terrain;
    }

    private static TerrainMovementProfile2D CreateProfile(bool defaultWalkable, int defaultCost = 10)
    {
        TerrainMovementProfile2D profile = ScriptableObject.CreateInstance<TerrainMovementProfile2D>();
        profile.Configure(defaultWalkable, defaultCost);
        return profile;
    }
}
