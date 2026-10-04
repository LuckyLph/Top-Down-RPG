using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;

public class DualGridTilemapModuleTests
{
    private GameObject gridObject;
    private DualGridRuleTile renderTile;
    private DualGridTilemapModule module;

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

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(gridObject);
        Object.DestroyImmediate(renderTile.DataTile);
        Object.DestroyImmediate(renderTile);
    }

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

    [Test]
    public void ClearDataTiles_InBulk_RemovesOnlyRenderCellsNoDataTileUses()
    {
        module.SetDataTiles(Block(0, 0, 5, 5));

        module.ClearDataTiles(Block(0, 0, 2, 5));

        Assert.That(CountRenderTiles(), Is.EqualTo(4 * 6), "Data columns 2 to 4 keep render columns 2 to 5.");
        Assert.That(module.RenderTilemap.HasTile(new Vector3Int(2, 0, 0)), Is.True, "Render column 2 still draws data column 2.");
        Assert.That(module.RenderTilemap.HasTile(new Vector3Int(1, 0, 0)), Is.False);
    }

    [Test]
    public void WritingTheDataTilemapDirectly_UpdatesTheRenderTilemap()
    {
        Vector3Int cell = new(3, 3, 0);
        module.DataTilemap.SetTile(cell, renderTile.DataTile);
        module.HandleTilemapPositionsChanged(module.DataTilemap, new Unity.Collections.NativeArray<Vector3Int>(new[] { cell }, Unity.Collections.Allocator.Temp));

        Assert.That(CountRenderTiles(), Is.EqualTo(4));
    }

    [Test]
    public void RefreshRenderTilemap_RebuildsRenderCellsFromTheDataTilemap()
    {
        module.SetDataTile(Vector3Int.zero);
        module.RenderTilemap.ClearAllTiles();

        module.RefreshRenderTilemap();

        Assert.That(CountRenderTiles(), Is.EqualTo(4));
    }

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

    private int CountRenderTiles()
    {
        return RenderCells().Count;
    }
}
