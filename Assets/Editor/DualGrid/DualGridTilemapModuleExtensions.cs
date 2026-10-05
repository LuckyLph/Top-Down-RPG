using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Editor-only brush preview helpers for <see cref="DualGridTilemapModule"/>: they place editor preview tiles on the
/// data tilemap and mirror them onto the render tilemap without touching the real tiles.
/// </summary>
public static class DualGridTilemapModuleExtensions
{
    /// <summary>
    /// Shows <paramref name="tile"/> as an editor preview at the data cell <paramref name="position"/> and updates the
    /// preview render tiles around it.
    /// </summary>
    public static void SetEditorPreviewTile(this DualGridTilemapModule dualGridTilemapModule, Vector3Int position, TileBase tile)
    {
        dualGridTilemapModule.DataTilemap.SetEditorPreviewTile(position, tile);
        dualGridTilemapModule.UpdatePreviewRenderTiles(position);
    }

    /// <summary>
    /// Removes the editor preview at the data cell <paramref name="position"/> and updates the preview render tiles
    /// around it.
    /// </summary>
    public static void ClearEditorPreviewTile(this DualGridTilemapModule dualGridTilemapModule, Vector3Int position)
    {
        dualGridTilemapModule.DataTilemap.SetEditorPreviewTile(position, null);
        dualGridTilemapModule.UpdatePreviewRenderTiles(position);
    }

    /// <summary>
    /// Sets the render tile as a preview on the four render cells around <paramref name="previewDataTilePosition"/>
    /// when that data cell holds a filled <see cref="DualGridPreviewTile"/>, and clears those previews otherwise.
    /// </summary>
    public static void UpdatePreviewRenderTiles(this DualGridTilemapModule dualGridTilemapModule, Vector3Int previewDataTilePosition)
    {
        bool hasPreviewDataTile = dualGridTilemapModule.DataTilemap.HasEditorPreviewTile(previewDataTilePosition);
        bool isPreviewDataTileVisible = dualGridTilemapModule.DataTilemap.GetEditorPreviewTile<DualGridPreviewTile>(previewDataTilePosition) is DualGridPreviewTile previewTile && previewTile.IsFilled;

        for (int corner = 0; corner < DualGridUtils.CornerCount; corner++)
        {
            Vector3Int renderTilePosition = DualGridUtils.GetRenderTilePosition(previewDataTilePosition, corner);
            if (hasPreviewDataTile && isPreviewDataTileVisible)
            {
                SetPreviewRenderTile(dualGridTilemapModule, renderTilePosition);
            }
            else
            {
                UnsetPreviewRenderTile(dualGridTilemapModule, renderTilePosition);
            }
        }
    }

    /// <summary>
    /// Runs <see cref="UpdatePreviewRenderTiles"/> for every cell inside the data tilemap's cell bounds.
    /// </summary>
    public static void UpdateAllPreviewRenderTiles(this DualGridTilemapModule dualGridTilemapModule)
    {
        foreach (var position in dualGridTilemapModule.DataTilemap.cellBounds.allPositionsWithin)
        {
            dualGridTilemapModule.UpdatePreviewRenderTiles(position);
        }
    }

    /// <summary>
    /// Removes every editor preview tile from both the data and the render tilemap.
    /// </summary>
    public static void ClearAllPreviewTiles(this DualGridTilemapModule dualGridTilemapModule)
    {
        dualGridTilemapModule.DataTilemap.ClearAllEditorPreviewTiles();
        dualGridTilemapModule.RenderTilemap.ClearAllEditorPreviewTiles();
    }

    /// <summary>
    /// Shows the module's render tile as an editor preview at a render cell and refreshes it.
    /// </summary>
    private static void SetPreviewRenderTile(DualGridTilemapModule dualGridTilemapModule, Vector3Int previewRenderTilePosition)
    {
        dualGridTilemapModule.RenderTilemap.SetEditorPreviewTile(previewRenderTilePosition, dualGridTilemapModule.RenderTile);
        dualGridTilemapModule.RenderTilemap.RefreshTile(previewRenderTilePosition);
    }

    /// <summary>
    /// Removes the editor preview at a render cell and refreshes it.
    /// </summary>
    private static void UnsetPreviewRenderTile(DualGridTilemapModule dualGridTilemapModule, Vector3Int previewRenderTilePosition)
    {
        dualGridTilemapModule.RenderTilemap.SetEditorPreviewTile(previewRenderTilePosition, null);
        dualGridTilemapModule.RenderTilemap.RefreshTile(previewRenderTilePosition);
    }
}
