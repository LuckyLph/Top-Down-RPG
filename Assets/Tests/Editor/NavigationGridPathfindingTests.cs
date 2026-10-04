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

    [Test]
    public void AStar_FailsWithoutSearching_WhenStrictGoalIsInAnotherRegion()
    {
        SetupNavigationGrid(addGapInBarrier: false);

        PathResult result = navigationGrid.Pathfinder.FindPath(new PathRequest(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), allowPartial: false));

        Assert.That(result.Success, Is.False);
        Assert.That(((GridAStarPathfinder2D)navigationGrid.Pathfinder).LastExpandedCount, Is.Zero);
    }

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

    [Test]
    public void AStar_IgnoresSearchCap_WhenGoalIsReachable()
    {
        SetupNavigationGrid(addGapInBarrier: true);
        navigationGrid.MaxSearchCells = 3;

        PathResult result = navigationGrid.Pathfinder.FindPath(new PathRequest(new Vector3Int(0, 0, 0), new Vector3Int(4, 4, 0), allowPartial: false));

        Assert.That(result.Success, Is.True);
        Assert.That(result.ReachedResolvedGoal, Is.True);
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

    private NavigationTerrainSource2D AttachTerrainSource(Tilemap dataTilemap, TerrainType2D terrainType)
    {
        NavigationTerrainSource2D source = dataTilemap.gameObject.AddComponent<NavigationTerrainSource2D>();
        source.Configure(terrainType);
        return source;
    }

    private static TerrainType2D CreateTerrain(string id)
    {
        return TerrainType2D.Create(id);
    }

    private static TerrainMovementProfile2D CreateProfile(bool defaultWalkable, params TerrainMovementProfile2D.TerrainRule[] rules)
    {
        return TerrainMovementProfile2D.Create(defaultWalkable, 10, rules);
    }

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
