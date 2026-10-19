using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Tests for <see cref="CollisionTileGizmo.CollectCollisionCells"/> on a tilemap built in the test: which cells the gizmo
/// draws.
/// </summary>
public class CollisionTileGizmoTests
{
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
    /// Only cells holding a tile with a collider are listed, at negative cells and on two z layers alike, in the order z,
    /// then x, then y; a tile without a collider and a cell emptied again are left out, and the list's old contents go.
    /// </summary>
    [Test]
    public void CollectCollisionCells_ListsTheCellsWhoseTileHasACollider()
    {
        Tilemap tilemap = CreateTilemap();
        Tile wall = CreateTile(Tile.ColliderType.Grid);
        Tile floor = CreateTile(Tile.ColliderType.None);
        tilemap.SetTile(new Vector3Int(2, 1, 0), wall);
        tilemap.SetTile(new Vector3Int(-3, -2, 0), wall);
        tilemap.SetTile(new Vector3Int(1, -1, 1), wall);
        tilemap.SetTile(new Vector3Int(2, -1, 0), wall);
        tilemap.SetTile(new Vector3Int(0, 0, 0), wall);
        tilemap.SetTile(new Vector3Int(1, 0, 0), floor);
        tilemap.SetTile(new Vector3Int(4, 3, 0), wall);
        tilemap.SetTile(new Vector3Int(4, 3, 0), null);
        List<Vector3Int> cells = new() { new Vector3Int(9, 9, 9) };

        CollisionTileGizmo.CollectCollisionCells(tilemap, cells);

        Assert.That(cells, Is.EqualTo(new[]
        {
            new Vector3Int(-3, -2, 0),
            new Vector3Int(1, -1, 1),
            new Vector3Int(2, -1, 0),
            new Vector3Int(0, 0, 0),
            new Vector3Int(2, 1, 0),
        }));
    }

    /// <summary>
    /// An empty tilemap lists no cells.
    /// </summary>
    [Test]
    public void CollectCollisionCells_ListsNothingOnAnEmptyTilemap()
    {
        List<Vector3Int> cells = new() { Vector3Int.zero };

        CollisionTileGizmo.CollectCollisionCells(CreateTilemap(), cells);

        Assert.That(cells, Is.Empty);
    }

    /// <summary>
    /// A tilemap under a grid of its own.
    /// </summary>
    private Tilemap CreateTilemap()
    {
        Grid grid = Track(new GameObject("Grid")).AddComponent<Grid>();
        Tilemap tilemap = new GameObject("CollisionTilemap", typeof(Tilemap)).GetComponent<Tilemap>();
        tilemap.transform.SetParent(grid.transform, false);
        return tilemap;
    }

    /// <summary>
    /// A plain tile with <paramref name="colliderType"/>.
    /// </summary>
    private Tile CreateTile(Tile.ColliderType colliderType)
    {
        Tile tile = Track(ScriptableObject.CreateInstance<Tile>());
        tile.colliderType = colliderType;
        return tile;
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
