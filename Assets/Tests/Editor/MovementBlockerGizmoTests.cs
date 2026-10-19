using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="MovementBlockerGizmo.BlockerMap"/> on a navigation grid built in the test: a 5 x 3 ground with a
/// wall at (1, 1) and water the profile cannot walk at (3, 1) and (4, 1).
/// </summary>
public class MovementBlockerGizmoTests
{
    private readonly List<Object> created = new();
    private NavigationGrid2D grid;
    private Tilemap collision;
    private TerrainMovementProfile2D profile;

    /// <summary>
    /// Builds the grid: ground everywhere, the water layer over it, the wall on a blocking tilemap.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        GameObject root = Track(new GameObject("Grid"));
        root.AddComponent<Grid>();
        Tile tile = Track(ScriptableObject.CreateInstance<Tile>());
        TerrainType2D ground = Track(TerrainType2D.Create("ground"));
        TerrainType2D water = Track(TerrainType2D.Create("water"));
        profile = Track(TerrainMovementProfile2D.Create(true, 10, new TerrainMovementProfile2D.TerrainRule(water, false, 10)));
        Tilemap groundMap = CreateTilemap(root, "Ground");
        Tilemap waterMap = CreateTilemap(root, "Water");
        collision = CreateTilemap(root, "Collision");
        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                groundMap.SetTile(new Vector3Int(x, y, 0), tile);
            }
        }

        waterMap.SetTile(new Vector3Int(3, 1, 0), tile);
        waterMap.SetTile(new Vector3Int(4, 1, 0), tile);
        collision.SetTile(new Vector3Int(1, 1, 0), tile);
        groundMap.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(ground);
        waterMap.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(water);
        grid = root.AddComponent<NavigationGrid2D>();
        grid.ConfigureBlockingTilemaps(collision);
        grid.BuildGrid();
    }

    /// <summary>
    /// Destroys every object the test created.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        foreach (Object instance in created)
        {
            if (instance != null)
            {
                Object.DestroyImmediate(instance);
            }
        }

        created.Clear();
    }

    /// <summary>
    /// The wall is a run of its own and the two water cells one run, centred on them in world space; every side where a
    /// walkable cell meets a blocked one or the bounds is outlined: 15 of the 16 sides along the bounds (the last one is
    /// the water's) and 9 around the wall and the water.
    /// </summary>
    [Test]
    public void Refresh_MergesBlockedCellsIntoRuns_AndOutlinesWhereWalkingStops()
    {
        MovementBlockerGizmo.BlockerMap map = new();

        map.Refresh(grid, profile);

        Assert.That(map.Runs.Count, Is.EqualTo(2));
        AssertRun(map.Runs[0], new Vector3Int(1, 1, 0), 1, NavigationGrid2D.Blocker.Wall, new Vector3(1.5f, 1.5f, 0f), new Vector3(1f, 1f, 0f));
        AssertRun(map.Runs[1], new Vector3Int(3, 1, 0), 2, NavigationGrid2D.Blocker.Terrain, new Vector3(4f, 1.5f, 0f), new Vector3(2f, 1f, 0f));
        Assert.That(map.Edges.PointCount, Is.EqualTo(48));
        Assert.That(map.Cliffs.PointCount, Is.Zero, "No raised ground, no cliff.");
    }

    /// <summary>
    /// Raised ground blocks no cell: a 2 x 1 plateau over (1, 2) and (2, 2), along the grid's top row, adds no run and
    /// no edge, only a cliff line along each of its 6 sides, once each: 2 below, 1 at each end and 2 along its back,
    /// where it meets the level world outside the bounds.
    /// </summary>
    [Test]
    public void Refresh_DrawsTheCliffAsLines_NotCells()
    {
        Tilemap raisedMap = CreateTilemap(grid.gameObject, "Raised");
        RaisedGroundTile raised = Track(RaisedGroundTile.Create(false));
        raisedMap.SetTile(new Vector3Int(1, 2, 0), raised);
        raisedMap.SetTile(new Vector3Int(2, 2, 0), raised);
        grid.ConfigureRaisedTilemap(raisedMap);
        grid.BuildGrid();
        MovementBlockerGizmo.BlockerMap map = new();

        map.Refresh(grid, profile);

        Assert.That(map.Runs.Count, Is.EqualTo(2), "the wall and the water only");
        Assert.That(map.Edges.PointCount, Is.EqualTo(48));
        Assert.That(map.Cliffs.PointCount, Is.EqualTo(12));
        System.ReadOnlySpan<Vector3> cliffs = map.Cliffs.Points;
        List<string> lines = new();
        for (int i = 0; i < cliffs.Length; i += 2)
        {
            lines.Add($"{cliffs[i].x:0.#},{cliffs[i].y:0.#}-{cliffs[i + 1].x:0.#},{cliffs[i + 1].y:0.#}");
        }

        Assert.That(lines, Is.EquivalentTo(new[] { "1,2-2,2", "2,2-3,2", "1,2-1,3", "3,2-3,3", "1,3-2,3", "2,3-3,3" }));
    }

    /// <summary>
    /// A cliff face two cells tall, drawn by a terrain renderer on the grid's object at its style's 64 px, blocks the
    /// cells it covers whole: a plateau over the column (2, 0) to (2, 2) fills (2, 0) and (2, 1) as
    /// <see cref="NavigationGrid2D.Blocker.CliffFace"/> runs of their own, beside the wall and the water, and leaves its
    /// top cell (2, 2) open.
    /// </summary>
    [Test]
    public void Refresh_FillsTheCellsACliffFaceCoversWhole()
    {
        Tilemap raisedMap = CreateTilemap(grid.gameObject, "Raised");
        RaisedGroundTile raised = Track(RaisedGroundTile.Create(false));
        for (int y = 0; y < 3; y++)
        {
            raisedMap.SetTile(new Vector3Int(2, y, 0), raised);
        }

        TerrainStyle style = Track(TerrainStyle.Create(null));
        SerializedObject settings = new(style);
        settings.FindProperty("cliffHeight").floatValue = 64f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        grid.gameObject.SetActive(false);
        TerrainBlendRenderer renderer = grid.gameObject.AddComponent<TerrainBlendRenderer>();
        renderer.Configure(style, raisedMap);
        renderer.enabled = false;
        grid.gameObject.SetActive(true);
        grid.ConfigureRaisedTilemap(raisedMap);
        grid.BuildGrid();
        MovementBlockerGizmo.BlockerMap map = new();

        map.Refresh(grid, profile);

        List<Vector3Int> faces = new();
        foreach (MovementBlockerGizmo.BlockedRun run in map.Runs)
        {
            if (run.Blocker == NavigationGrid2D.Blocker.CliffFace)
            {
                Assert.That(run.Length, Is.EqualTo(1));
                faces.Add(run.Start);
            }
        }

        Assert.That(faces, Is.EquivalentTo(new[] { new Vector3Int(2, 0, 0), new Vector3Int(2, 1, 0) }));
        Assert.That(map.Runs.Count, Is.EqualTo(4), "the wall, the water and the two face cells");
    }

    /// <summary>
    /// A refresh after the grid changed rebuilds the runs: with the wall gone only the water is left. Without a
    /// profile every terrain is walkable, so nothing is.
    /// </summary>
    [Test]
    public void Refresh_FollowsTheGridAndTheProfile()
    {
        MovementBlockerGizmo.BlockerMap map = new();
        map.Refresh(grid, profile);

        collision.SetTile(new Vector3Int(1, 1, 0), null);
        grid.RefreshCells(new[] { new Vector3Int(1, 1, 0) });
        map.Refresh(grid, profile);

        Assert.That(map.Runs.Count, Is.EqualTo(1));
        Assert.That(map.Runs[0].Blocker, Is.EqualTo(NavigationGrid2D.Blocker.Terrain));

        map.Refresh(grid, null);

        Assert.That(map.Runs, Is.Empty);
        Assert.That(map.Edges.PointCount, Is.EqualTo(32), "Only the 16 sides along the bounds.");
    }

    /// <summary>
    /// On a chunk's grid in a world, the side of a walkable cell along the chunk's border is outlined while the chunk
    /// across it is not loaded, and not once it is and its ground continues there.
    /// </summary>
    [Test]
    public void Refresh_OnAChunkOfAWorld_OutlinesABorderOnlyWhileTheChunkAcrossIsNotLoaded()
    {
        WorldNavigation world = new(new NavigationPagePool());
        try
        {
            NavigationGrid2D west = CreateOpenChunk(new Vector2Int(0, 0));
            NavigationGrid2D east = CreateOpenChunk(new Vector2Int(1, 0));
            world.AddChunk(west, new Vector2Int(0, 0));
            MovementBlockerGizmo.BlockerMap map = new();

            map.Refresh(west, null);
            Assert.That(map.Edges.PointCount, Is.EqualTo(ChunkCoords.Size * 4 * 2), "All four sides of the chunk.");

            world.AddChunk(east, new Vector2Int(1, 0));
            map.Refresh(west, null);
            Assert.That(map.Edges.PointCount, Is.EqualTo(ChunkCoords.Size * 3 * 2), "Not along the border with the loaded chunk.");
        }
        finally
        {
            world.Dispose();
        }
    }

    /// <summary>
    /// A grid with open ground over the cells of the chunk at <paramref name="coords"/>, not built.
    /// </summary>
    private NavigationGrid2D CreateOpenChunk(Vector2Int coords)
    {
        GameObject root = Track(new GameObject($"Chunk {coords}"));
        root.AddComponent<Grid>();
        Tile tile = Track(ScriptableObject.CreateInstance<Tile>());
        Tilemap groundMap = CreateTilemap(root, "Ground");
        foreach (Vector2Int cell in ChunkCoords.Bounds(coords).allPositionsWithin)
        {
            groundMap.SetTile(new Vector3Int(cell.x, cell.y, 0), tile);
        }

        groundMap.gameObject.AddComponent<NavigationTerrainSource2D>().Configure(Track(TerrainType2D.Create("ground")));
        return root.AddComponent<NavigationGrid2D>();
    }

    /// <summary>
    /// Checks every part of <paramref name="run"/>.
    /// </summary>
    private static void AssertRun(MovementBlockerGizmo.BlockedRun run, Vector3Int start, int length, NavigationGrid2D.Blocker blocker, Vector3 center, Vector3 size)
    {
        Assert.That(run.Start, Is.EqualTo(start));
        Assert.That(run.Length, Is.EqualTo(length));
        Assert.That(run.Blocker, Is.EqualTo(blocker));
        Assert.That(Vector3.Distance(run.Center, center), Is.LessThan(0.0001f), $"centre {run.Center}");
        Assert.That(Vector3.Distance(run.Size, size), Is.LessThan(0.0001f), $"size {run.Size}");
    }

    /// <summary>
    /// An empty tilemap under <paramref name="root"/>.
    /// </summary>
    private static Tilemap CreateTilemap(GameObject root, string name)
    {
        Tilemap tilemap = new GameObject(name, typeof(Tilemap)).GetComponent<Tilemap>();
        tilemap.transform.SetParent(root.transform, false);
        return tilemap;
    }

    /// <summary>
    /// Records <paramref name="instance"/> for destruction in <see cref="TearDown"/>.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
