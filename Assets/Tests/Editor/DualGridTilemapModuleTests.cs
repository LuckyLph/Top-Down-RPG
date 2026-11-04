using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>
/// Tests for <see cref="DualGridTilemapModule"/> and <see cref="DualGridUtils"/>: how data cells map to the four
/// render cells around them, and how single, bulk, direct and full refresh writes keep the render tilemap in step.
/// </summary>
public class DualGridTilemapModuleTests
{
    private GameObject gridObject;
    private DualGridRuleTile renderTile;
    private DualGridTilemapModule module;

    /// <summary>
    /// Builds a grid with a data tilemap and a half-cell offset render tilemap under it, and a module painting a
    /// fresh <see cref="DualGridRuleTile"/>.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        gridObject = new GameObject("Grid", typeof(Grid));

        GameObject dataObject = new("DataTilemap", typeof(Tilemap));
        dataObject.transform.SetParent(gridObject.transform);

        GameObject renderObject = new("RenderTilemap", typeof(Tilemap));
        renderObject.transform.SetParent(dataObject.transform);
        renderObject.transform.localPosition = new Vector3(-0.5f, -0.5f, 0f);

        renderTile = ScriptableObject.CreateInstance<DualGridRuleTile>();
        renderTile.RefreshDataTile();

        module = dataObject.AddComponent<DualGridTilemapModule>();
        module.RenderTile = renderTile;
    }

    /// <summary>
    /// Destroys the grid hierarchy, the rule tile and its generated data tile.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(gridObject);
        Object.DestroyImmediate(renderTile.DataTile);
        Object.DestroyImmediate(renderTile);
    }

    /// <summary>
    /// A rule tile without its data tile sub-asset reports it once, as an exception naming the tile, and logs nothing.
    /// </summary>
    [Test]
    public void DataTile_WithoutItsSubAsset_ReportsOneClearError()
    {
        DualGridRuleTile bare = ScriptableObject.CreateInstance<DualGridRuleTile>();
        bare.name = "BareRuleTile";
        try
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => _ = bare.DataTile);
            StringAssert.Contains("BareRuleTile", error.Message);
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            Object.DestroyImmediate(bare);
        }
    }

    /// <summary>
    /// For every corner, mapping a data cell to its render cell and back returns the original data cell.
    /// </summary>
    [Test]
    public void RenderAndDataTilePositions_AreInverseCorners()
    {
        Vector3Int dataPosition = new(2, -3, 0);

        for (int corner = 0; corner < DualGridUtils.CornerCount; corner++)
        {
            Vector3Int renderPosition = DualGridUtils.GetRenderTilePosition(dataPosition, corner);
            Assert.That(DualGridUtils.GetDataTilePosition(renderPosition, corner), Is.EqualTo(dataPosition));
        }
    }

    /// <summary>
    /// Setting one data cell writes the data tile and paints the rule tile into exactly its four render cells.
    /// </summary>
    [Test]
    public void SetDataTile_FillsTheFourRenderCellsAroundIt()
    {
        module.SetDataTile(Vector3Int.zero);

        Assert.That(module.DataTilemap.GetTile(Vector3Int.zero), Is.SameAs(renderTile.DataTile));
        for (int corner = 0; corner < DualGridUtils.CornerCount; corner++)
        {
            Assert.That(module.RenderTilemap.GetTile(DualGridUtils.GetRenderTilePosition(Vector3Int.zero, corner)), Is.SameAs(renderTile));
        }

        Assert.That(CountRenderTiles(), Is.EqualTo(4));
    }

    /// <summary>
    /// Clearing one of two adjacent data cells removes only the render cells the other no longer shares.
    /// </summary>
    [Test]
    public void ClearDataTile_KeepsRenderCellsAnotherDataTileStillUses()
    {
        Vector3Int left = Vector3Int.zero;
        Vector3Int right = Vector3Int.right;
        module.SetDataTile(left);
        module.SetDataTile(right);
        Assert.That(CountRenderTiles(), Is.EqualTo(6));

        module.ClearDataTile(left);

        Assert.That(module.DataTilemap.HasTile(left), Is.False);
        Assert.That(CountRenderTiles(), Is.EqualTo(4));
        for (int corner = 0; corner < DualGridUtils.CornerCount; corner++)
        {
            Assert.That(module.RenderTilemap.HasTile(DualGridUtils.GetRenderTilePosition(right, corner)), Is.True);
        }
    }

    /// <summary>
    /// Painting a block in bulk produces the same render cells as painting it cell by cell, and clearing it in
    /// bulk empties the render tilemap.
    /// </summary>
    [Test]
    public void SetDataTiles_InBulk_MatchesPaintingOneCellAtATime()
    {
        List<Vector3Int> block = Block(0, 0, 20, 20);
        module.SetDataTiles(block);
        HashSet<Vector3Int> bulk = RenderCells();

        module.ClearDataTiles(block);
        Assert.That(CountRenderTiles(), Is.Zero, "Clearing every data cell in bulk empties the render tilemap.");

        foreach (Vector3Int cell in block)
        {
            module.SetDataTile(cell);
        }

        Assert.That(bulk.SetEquals(RenderCells()), Is.True);
        Assert.That(bulk.Count, Is.EqualTo(21 * 21));
    }

    /// <summary>
    /// Clearing part of a painted block in bulk removes only the render cells that no remaining data cell uses.
    /// </summary>
    [Test]
    public void ClearDataTiles_InBulk_RemovesOnlyRenderCellsNoDataTileUses()
    {
        module.SetDataTiles(Block(0, 0, 5, 5));

        module.ClearDataTiles(Block(0, 0, 2, 5));

        Assert.That(CountRenderTiles(), Is.EqualTo(4 * 6), "Data columns 2 to 4 keep render columns 2 to 5.");
        Assert.That(module.RenderTilemap.HasTile(new Vector3Int(2, 0, 0)), Is.True, "Render column 2 still draws data column 2.");
        Assert.That(module.RenderTilemap.HasTile(new Vector3Int(1, 0, 0)), Is.False);
    }

    /// <summary>
    /// A data tile written straight to the data tilemap is drawn once the module handles the tilemap's
    /// positions-changed notification.
    /// </summary>
    [Test]
    public void WritingTheDataTilemapDirectly_UpdatesTheRenderTilemap()
    {
        Vector3Int cell = new(3, 3, 0);
        module.DataTilemap.SetTile(cell, renderTile.DataTile);
        module.HandleTilemapPositionsChanged(module.DataTilemap, new Unity.Collections.NativeArray<Vector3Int>(new[] { cell }, Unity.Collections.Allocator.Temp));

        Assert.That(CountRenderTiles(), Is.EqualTo(4));
    }

    /// <summary>
    /// A full refresh repaints the render cells of every data cell after the render tilemap was wiped.
    /// </summary>
    [Test]
    public void RefreshRenderTilemap_RebuildsRenderCellsFromTheDataTilemap()
    {
        module.SetDataTile(Vector3Int.zero);
        module.RenderTilemap.ClearAllTiles();

        module.RefreshRenderTilemap();

        Assert.That(CountRenderTiles(), Is.EqualTo(4));
    }

    /// <summary>
    /// Lists the cells of the rectangle from (<paramref name="minX"/>, <paramref name="minY"/>) inclusive to
    /// (<paramref name="maxX"/>, <paramref name="maxY"/>) exclusive.
    /// </summary>
    private static List<Vector3Int> Block(int minX, int minY, int maxX, int maxY)
    {
        List<Vector3Int> cells = new();
        for (int x = minX; x < maxX; x++)
        {
            for (int y = minY; y < maxY; y++)
            {
                cells.Add(new Vector3Int(x, y, 0));
            }
        }

        return cells;
    }

    /// <summary>
    /// Collects every render tilemap cell that holds a tile, compressing its bounds first.
    /// </summary>
    private HashSet<Vector3Int> RenderCells()
    {
        HashSet<Vector3Int> cells = new();
        module.RenderTilemap.CompressBounds();
        foreach (Vector3Int position in module.RenderTilemap.cellBounds.allPositionsWithin)
        {
            if (module.RenderTilemap.HasTile(position))
            {
                cells.Add(position);
            }
        }

        return cells;
    }

    /// <summary>
    /// Counts the render tilemap cells that hold a tile.
    /// </summary>
    private int CountRenderTiles()
    {
        return RenderCells().Count;
    }
}
