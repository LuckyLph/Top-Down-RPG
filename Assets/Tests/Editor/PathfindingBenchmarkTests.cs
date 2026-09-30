using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Tilemaps;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using Random = System.Random;

// Opt-in: run by name to compare pathfinding cost before and after navigation changes.
// Results are logged to the console with a [PathBench] prefix.
[Explicit, Category("Benchmark")]
public class PathfindingBenchmarkTests
{
    private const int Size = 128;
    private const int Searches = 100;

    private readonly List<Object> createdObjects = new();
    private GameObject root;
    private NavigationGrid2D navigationGrid;
    private TerrainMovementProfile2D profile;
    private TerrainType2D ground;

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
    public void OpenField()
    {
        BuildGrid(_ => false);
        MeasureSearches("Open field", RandomPairs(Searches, requireConnected: true), allowPartial: false);
    }

    [Test]
    public void Maze()
    {
        // Full-height walls every 8 columns, each with a single 2-cell gap at a random height.
        Random random = new(7);
        int[] gapByColumn = new int[Size];
        for (int x = 4; x < Size; x += 8)
        {
            gapByColumn[x] = random.Next(1, Size - 3);
        }

        BuildGrid(cell => cell.x % 8 == 4 && (cell.y < gapByColumn[cell.x] || cell.y > gapByColumn[cell.x] + 1));
        MeasureSearches("Maze", RandomPairs(Searches, requireConnected: true), allowPartial: false);
    }

    [Test]
    public void UnreachableGoal_PartialAllowed()
    {
        // A full wall splits the map; every goal is on the far side, so each search floods its half.
        BuildGrid(cell => cell.x == Size / 2);
        List<(Vector3Int, Vector3Int)> pairs = new();
        Random random = new(11);
        for (int i = 0; i < 20; i++)
        {
            pairs.Add((new Vector3Int(random.Next(0, Size / 2), random.Next(0, Size), 0),
                new Vector3Int(random.Next(Size / 2 + 1, Size), random.Next(0, Size), 0)));
        }

        MeasureSearches("Unreachable goal (partial)", pairs, allowPartial: true);
    }

    [Test]
    public void BuildPathToWorld_Maze()
    {
        Random random = new(7);
        int[] gapByColumn = new int[Size];
        for (int x = 4; x < Size; x += 8)
        {
            gapByColumn[x] = random.Next(1, Size - 3);
        }

        BuildGrid(cell => cell.x % 8 == 4 && (cell.y < gapByColumn[cell.x] || cell.y > gapByColumn[cell.x] + 1));

        MobConfig config = Track(ScriptableObject.CreateInstance<MobConfig>());
        config.movementProfile = profile;
        GameObject mob = Track(new GameObject("BenchmarkMob"));
        Rigidbody2D body = mob.AddComponent<Rigidbody2D>();
        MobMotor2D motor = mob.AddComponent<MobMotor2D>();
        MobPathAgent2D agent = mob.AddComponent<MobPathAgent2D>();
        motor.Initialize(config);
        agent.Initialize(navigationGrid, motor, config);

        List<(Vector3Int, Vector3Int)> pairs = RandomPairs(Searches, requireConnected: true);
        Measure("BuildPathToWorld (maze)", pairs.Count, i =>
        {
            body.position = navigationGrid.CellToWorldCenter(pairs[i].Item1);
            agent.BuildPathToWorld(navigationGrid.CellToWorldCenter(pairs[i].Item2), allowPartial: true);
            return ((GridAStarPathfinder2D)navigationGrid.Pathfinder).LastExpandedCount;
        });
    }

    private void MeasureSearches(string label, List<(Vector3Int, Vector3Int)> pairs, bool allowPartial)
    {
        GridAStarPathfinder2D pathfinder = (GridAStarPathfinder2D)navigationGrid.Pathfinder;
        Measure(label, pairs.Count, i =>
        {
            PathResult result = pathfinder.FindPath(new PathRequest(pairs[i].Item1, pairs[i].Item2, allowPartial, profile));
            Assert.That(result.Success, Is.True);
            return pathfinder.LastExpandedCount;
        });
    }

    private static void Measure(string label, int count, Func<int, int> run)
    {
        run(0);

        long expanded = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            expanded += run(i);
        }

        stopwatch.Stop();

        // Separate pass so the allocation probes don't skew the timing. Mono's heap size only grows
        // between collections, so samples during which a GC ran are skipped.
        Recorder allocRecorder = Recorder.Get("GC.Alloc");
        allocRecorder.FilterToCurrentThread();
        long allocations = 0;
        long bytes = 0;
        int byteSamples = 0;
        for (int i = 0; i < count; i++)
        {
            int collections = GC.CollectionCount(0);
            long heapBefore = Profiler.GetMonoUsedSizeLong();
            allocRecorder.enabled = true;
            run(i);
            allocRecorder.enabled = false;
            long heapAfter = Profiler.GetMonoUsedSizeLong();

            allocations += allocRecorder.sampleBlockCount;
            if (GC.CollectionCount(0) == collections)
            {
                bytes += heapAfter - heapBefore;
                byteSamples++;
            }
        }

        allocRecorder.CollectFromAllThreads();

        string bytesText = byteSamples > 0 ? $"{bytes / (double)byteSamples / 1024:F1} KB" : "n/a";
        Debug.Log(
            $"[PathBench] {label}: {stopwatch.Elapsed.TotalMilliseconds / count:F3} ms/search, " +
            $"{allocations / (double)count:F0} allocs/search (~{bytesText}), {expanded / (double)count:F0} cells expanded/search");
    }

    private List<(Vector3Int, Vector3Int)> RandomPairs(int count, bool requireConnected)
    {
        Random random = new(1234);
        List<(Vector3Int, Vector3Int)> pairs = new();
        while (pairs.Count < count)
        {
            Vector3Int from = new(random.Next(Size), random.Next(Size), 0);
            Vector3Int to = new(random.Next(Size), random.Next(Size), 0);
            if (!navigationGrid.IsCellWalkable(from, profile) || !navigationGrid.IsCellWalkable(to, profile))
            {
                continue;
            }

            if (requireConnected && !navigationGrid.AreCellsConnected(from, to, profile))
            {
                continue;
            }

            pairs.Add((from, to));
        }

        return pairs;
    }

    private void BuildGrid(Func<Vector3Int, bool> isBlocked)
    {
        root = Track(new GameObject("PathBenchmarkRoot"));
        root.AddComponent<Grid>();
        Tile tile = Track(ScriptableObject.CreateInstance<Tile>());

        List<Vector3Int> groundCells = new();
        List<Vector3Int> blockedCells = new();
        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                Vector3Int cell = new(x, y, 0);
                groundCells.Add(cell);
                if (isBlocked(cell))
                {
                    blockedCells.Add(cell);
                }
            }
        }

        Tilemap groundMap = CreateTilemap("Ground", groundCells, tile);
        Tilemap collisionMap = CreateTilemap("Collision", blockedCells, tile);

        ground = Track(ScriptableObject.CreateInstance<TerrainType2D>());
        ground.Configure("ground");
        groundMap.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(groundMap, collisionMap, null, ground);

        // A handful of rules, like a profile covering several terrain types.
        profile = Track(ScriptableObject.CreateInstance<TerrainMovementProfile2D>());
        profile.Configure(isWalkableByDefault: false, traversalCostByDefault: 10);
        profile.SetTerrainRule(ground, true, 10);
        for (int i = 0; i < 4; i++)
        {
            TerrainType2D unused = Track(ScriptableObject.CreateInstance<TerrainType2D>());
            unused.Configure($"unused{i}");
            profile.SetTerrainRule(unused, true, 20 + i * 5);
        }

        navigationGrid = root.AddComponent<NavigationGrid2D>();
        navigationGrid.BuildGrid();
    }

    private Tilemap CreateTilemap(string name, List<Vector3Int> cells, TileBase tile)
    {
        GameObject tilemapObject = new(name);
        tilemapObject.transform.SetParent(root.transform);
        Tilemap tilemap = tilemapObject.AddComponent<Tilemap>();
        TileBase[] tiles = new TileBase[cells.Count];
        Array.Fill(tiles, tile);
        tilemap.SetTiles(cells.ToArray(), tiles);
        return tilemap;
    }

    private T Track<T>(T created) where T : Object
    {
        createdObjects.Add(created);
        return created;
    }
}
