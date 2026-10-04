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

        foreach (Vector3Int renderPosition in DualGridUtils.GetRenderTilePositions(dataPosition))
        {
            Assert.That(DualGridUtils.GetDataTilePositions(renderPosition), Does.Contain(dataPosition));
        }
    }

    [Test]
    public void SetDataTile_FillsTheFourRenderCellsAroundIt()
    {
        module.SetDataTile(Vector3Int.zero);

        Assert.That(module.DataTilemap.GetTile(Vector3Int.zero), Is.SameAs(renderTile.DataTile));
        Assert.That(module.RenderTilemap.GetUsedTilesCount(), Is.EqualTo(1));
        foreach (Vector3Int renderPosition in DualGridUtils.GetRenderTilePositions(Vector3Int.zero))
        {
            Assert.That(module.RenderTilemap.GetTile(renderPosition), Is.SameAs(renderTile));
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
        foreach (Vector3Int renderPosition in DualGridUtils.GetRenderTilePositions(right))
        {
            Assert.That(module.RenderTilemap.HasTile(renderPosition), Is.True);
        }
    }

    [Test]
    public void RefreshRenderTilemap_RebuildsRenderCellsFromTheDataTilemap()
    {
        module.SetDataTile(Vector3Int.zero);
        module.RenderTilemap.ClearAllTiles();

        module.RefreshRenderTilemap();

        Assert.That(CountRenderTiles(), Is.EqualTo(4));
    }

    private int CountRenderTiles()
    {
        module.RenderTilemap.CompressBounds();
        int count = 0;
        foreach (Vector3Int position in module.RenderTilemap.cellBounds.allPositionsWithin)
        {
            if (module.RenderTilemap.HasTile(position))
            {
                count++;
            }
        }

        return count;
    }
}
