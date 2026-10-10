using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="PropGridSnapping"/>: where blocking and walk-through props snap, that snapping a snapped
/// position changes nothing, and that the cells it predicts are the ones <see cref="NavigationGrid2D"/> blocks for the
/// placed props.
/// </summary>
public class PropGridSnappingTests
{
    private const string TreePath = "Assets/Prefabs/Props/Trees/Tree_Pine.prefab";
    private const string CottagePath = "Assets/Prefabs/Props/Buildings/Cottage.prefab";
    private const string GrassPath = "Assets/Prefabs/Props/Plants/Grass_Tall.prefab";

    private readonly List<Object> created = new();

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
    /// A blocking footprint is centred on the fewest whole cells that hold it (one, two, three or nine columns here)
    /// nearest the pointer, and stands on the bottom edge of the cell the pointer is in.
    /// </summary>
    [Test]
    public void Footprints_AreCentredOnTheCellsTheyNeed_AndStandOnABottomEdge()
    {
        Grid grid = CreateGrid(Vector3.zero);
        Vector2 pointer = new(2.7f, 3.4f);

        Assert.That(PropGridSnapping.SnapBlocking(grid, pointer, Footprint(0.6f, 0.4f)), Is.EqualTo(new Vector3(2.5f, 3f, 0f)));
        Assert.That(PropGridSnapping.SnapBlocking(grid, pointer, Footprint(1.5f, 0.6f)), Is.EqualTo(new Vector3(3f, 3f, 0f)));
        Assert.That(PropGridSnapping.SnapBlocking(grid, pointer, Footprint(2.2f, 0.9f)), Is.EqualTo(new Vector3(2.5f, 3f, 0f)));
        Assert.That(PropGridSnapping.SnapBlocking(grid, pointer, Footprint(8.4f, 2.5f)), Is.EqualTo(new Vector3(2.5f, 3f, 0f)));
        Assert.That(PropGridSnapping.CellsSpanned(1f, 1f), Is.EqualTo(1), "A footprint exactly one cell wide needs one cell.");
        Assert.That(PropGridSnapping.CellsSpanned(1.5f, 1f), Is.EqualTo(2));
    }

    /// <summary>
    /// A footprint off the root's centre and starting just below it is itself centred on its cells and put on the
    /// bottom edge, with the root following it.
    /// </summary>
    [Test]
    public void AnOffCentreFootprint_IsItselfCentredOnItsCells()
    {
        Grid grid = CreateGrid(Vector3.zero);
        Rect footprint = new(-0.73f, -0.0165f, 1.83f, 1.17f);

        Vector3 root = PropGridSnapping.SnapBlocking(grid, new Vector2(2.7f, 3.4f), footprint);

        Assert.That(root.x + footprint.center.x, Is.EqualTo(3f).Within(0.0001f), "Centred on columns 2 and 3.");
        Assert.That(root.y + footprint.yMin, Is.EqualTo(3f).Within(0.0001f), "Standing on row 3's bottom edge.");
        BoundsInt cells = PropGridSnapping.BlockedCells(grid, root, footprint);
        Assert.That(cells.position, Is.EqualTo(new Vector3Int(2, 3, 0)));
        Assert.That(cells.size, Is.EqualTo(new Vector3Int(2, 2, 1)));
    }

    /// <summary>
    /// Snapping an already snapped position leaves it where it is, for footprints of every width and any pointer.
    /// </summary>
    [Test]
    public void SnappingASnappedPosition_LeavesItWhereItIs()
    {
        Grid grid = CreateGrid(Vector3.zero);
        float[] widths = { 0.5f, 1f, 1.5f, 2.2f, 8.4f };
        Vector2[] pointers = { new(0f, 0f), new(2.7f, 3.4f), new(-1.49f, -0.99f), new(5.5f, 2.999f) };
        foreach (float width in widths)
        {
            foreach (Vector2 pointer in pointers)
            {
                Rect footprint = Footprint(width, 0.5f);
                Vector3 snapped = PropGridSnapping.SnapBlocking(grid, pointer, footprint);
                Assert.That(PropGridSnapping.SnapBlocking(grid, snapped, footprint), Is.EqualTo(snapped), $"width {width} at {pointer}");
            }
        }
    }

    /// <summary>
    /// The grid's own position shifts the cells props snap to.
    /// </summary>
    [Test]
    public void TheGridsPosition_ShiftsTheCells()
    {
        Grid grid = CreateGrid(new Vector3(0.5f, 0.25f, 0f));

        Assert.That(PropGridSnapping.SnapBlocking(grid, new Vector2(2.7f, 3.4f), Footprint(0.6f, 0.4f)), Is.EqualTo(new Vector3(3f, 3.25f, 0f)));
    }

    /// <summary>
    /// A prop prefab's footprint is the box collider on its root; a walk-through prop has none and only snaps to the
    /// pixel grid.
    /// </summary>
    [Test]
    public void Footprints_ComeFromTheRootCollider_AndWalkThroughPropsSnapToPixels()
    {
        Grid grid = CreateGrid(Vector3.zero);
        GameObject tree = AssetDatabase.LoadAssetAtPath<GameObject>(TreePath);
        GameObject grass = AssetDatabase.LoadAssetAtPath<GameObject>(GrassPath);

        BoxCollider2D box = tree.GetComponent<BoxCollider2D>();
        Assert.That(PropGridSnapping.TryGetFootprint(tree, out Rect footprint), Is.True);
        Assert.That(footprint.position, Is.EqualTo(box.offset - box.size * 0.5f));
        Assert.That(footprint.size, Is.EqualTo(box.size));
        Assert.That(PropGridSnapping.TryGetFootprint(grass, out _), Is.False);
        Assert.That(PropGridSnapping.Snap(grid, new Vector2(2.71f, 3.43f), grass), Is.EqualTo(new Vector3(2.71875f, 3.4375f, 0f)));
    }

    /// <summary>
    /// For a tree and a cottage placed at their snapped positions, the cells predicted by
    /// <see cref="PropGridSnapping.BlockedCells"/> are exactly the cells the navigation grid blocks.
    /// </summary>
    [Test]
    public void PredictedCells_AreTheCellsTheNavigationGridBlocks()
    {
        NavigationGrid2D navigation = CreateGround(14, 8);
        Grid grid = navigation.GetComponent<Grid>();
        BoundsInt treeCells = PlaceSnapped(grid, TreePath, new Vector2(2.7f, 2.3f));
        BoundsInt cottageCells = PlaceSnapped(grid, CottagePath, new Vector2(8.2f, 3.6f));
        navigation.ConfigureObstacleLayers(LayerMask.GetMask("Obstacles"));

        navigation.BuildGrid();

        Assert.That(treeCells.size, Is.EqualTo(new Vector3Int(1, 1, 1)));
        Assert.That(cottageCells.size, Is.EqualTo(new Vector3Int(9, 3, 1)));
        for (int x = 0; x < 14; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                Vector3Int cell = new(x, y, 0);
                bool predicted = treeCells.Contains(cell) || cottageCells.Contains(cell);
                Assert.That(navigation.IsCellWalkable(cell), Is.EqualTo(!predicted), $"cell {cell}");
            }
        }
    }

    /// <summary>
    /// For every blocking prop, the root <see cref="PropGridSnapping.RootOnCells"/> gives for a bottom-left cell is
    /// already snapped, and the prop then blocks exactly <see cref="PropGridSnapping.CellsCovered"/> cells from that
    /// cell, on a grid at the origin and on a shifted one.
    /// </summary>
    [Test]
    public void RootOnCells_PutsEveryFootprintOnExactlyTheCellsItCovers()
    {
        Grid[] grids = { CreateGrid(Vector3.zero), CreateGrid(new Vector3(0.5f, 0.25f, 0f)) };
        Vector3Int[] corners = { new(0, 0, 0), new(3, -2, 0), new(-7, 5, 0) };
        int checkedProps = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Props" }))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (!PropGridSnapping.TryGetFootprint(prefab, out Rect footprint))
            {
                continue;
            }

            checkedProps++;
            Vector2Int covered = PropGridSnapping.CellsCovered(footprint, Vector2.one);
            foreach (Grid grid in grids)
            {
                foreach (Vector3Int corner in corners)
                {
                    Vector3 root = PropGridSnapping.RootOnCells(grid, corner, footprint);
                    Assert.That(Vector3.Distance(PropGridSnapping.SnapBlocking(grid, root, footprint), root), Is.LessThan(0.0001f), $"{prefab.name} at {corner}");
                    BoundsInt blocked = PropGridSnapping.BlockedCells(grid, root, footprint);
                    Assert.That(blocked.position, Is.EqualTo(corner), $"{prefab.name} at {corner}");
                    Assert.That(blocked.size, Is.EqualTo(new Vector3Int(covered.x, covered.y, 1)), $"{prefab.name} at {corner}");
                }
            }
        }

        Assert.That(checkedProps, Is.GreaterThan(10));
        Assert.That(PropGridSnapping.CellsCovered(Footprint(1.5f, 1f), Vector2.one), Is.EqualTo(new Vector2Int(2, 1)));
        Assert.That(PropGridSnapping.CellsCovered(Footprint(0.5f, 1.2f), Vector2.one), Is.EqualTo(new Vector2Int(1, 2)));
    }

    /// <summary>
    /// A footprint <paramref name="width"/> by <paramref name="height"/> centred on the root and standing on it.
    /// </summary>
    private static Rect Footprint(float width, float height)
    {
        return new Rect(-width * 0.5f, 0f, width, height);
    }

    /// <summary>
    /// Creates a grid of one-unit cells at <paramref name="position"/>.
    /// </summary>
    private Grid CreateGrid(Vector3 position)
    {
        GameObject gridObject = Track(new GameObject("Grid"));
        gridObject.transform.position = position;
        return gridObject.AddComponent<Grid>();
    }

    /// <summary>
    /// Builds an unbuilt navigation grid, on the same object as its <see cref="Grid"/>, over a
    /// <paramref name="width"/> x <paramref name="height"/> block of ground.
    /// </summary>
    private NavigationGrid2D CreateGround(int width, int height)
    {
        Grid grid = CreateGrid(Vector3.zero);
        GameObject groundObject = new("Ground");
        groundObject.transform.SetParent(grid.transform);
        Tilemap ground = groundObject.AddComponent<Tilemap>();
        Tile tile = Track(ScriptableObject.CreateInstance<Tile>());
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                ground.SetTile(new Vector3Int(x, y, 0), tile);
            }
        }

        groundObject.AddComponent<NavigationTerrainSource2D>().Configure(Track(TerrainType2D.Create("ground")));
        return grid.gameObject.AddComponent<NavigationGrid2D>();
    }

    /// <summary>
    /// Places the prefab at <paramref name="path"/> where <see cref="PropGridSnapping.Snap"/> puts it for
    /// <paramref name="pointer"/>, and returns the cells it predicts the prop blocks.
    /// </summary>
    private BoundsInt PlaceSnapped(Grid grid, string path, Vector2 pointer)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        GameObject instance = Track(Object.Instantiate(prefab));
        instance.transform.position = PropGridSnapping.Snap(grid, pointer, prefab);
        PropGridSnapping.TryGetFootprint(prefab, out Rect footprint);
        return PropGridSnapping.BlockedCells(grid, instance.transform.position, footprint);
    }

    /// <summary>
    /// Records <paramref name="instance"/> for destruction in <see cref="TearDown"/> and returns it.
    /// </summary>
    private T Track<T>(T instance) where T : Object
    {
        created.Add(instance);
        return instance;
    }
}
