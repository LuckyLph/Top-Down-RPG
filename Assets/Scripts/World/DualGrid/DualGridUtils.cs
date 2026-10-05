using UnityEngine;

/// <summary>
/// Coordinate maths between a dual-grid data tilemap and its render tilemap, which is offset by half a cell so
/// each render tile sits on the shared corner of 4 data tiles.
/// </summary>
public static class DualGridUtils
{
    /// <summary>
    /// The number of render tiles around a data tile, and of data tiles around a render tile.
    /// </summary>
    public const int CornerCount = 4;

    private static readonly Vector3Int[] CornerOffsets =
    {
        new(0, 0, 0),
        new(1, 0, 0),
        new(0, 1, 0),
        new(1, 1, 0)
    };

    /// <summary>
    /// The render tile at one of the 4 corners (0 to <see cref="CornerCount"/> - 1) of a data tile.
    /// </summary>
    /// <remarks>
    /// Assumes that the render tilemap offset is always (-0.5, -0.5).
    /// </remarks>
    public static Vector3Int GetRenderTilePosition(Vector3Int dataTilePosition, int corner)
    {
        return dataTilePosition + CornerOffsets[corner];
    }

    /// <summary>
    /// One of the 4 data tiles (0 to <see cref="CornerCount"/> - 1) whose shared corner a render tile draws.
    /// </summary>
    /// <remarks>
    /// Assumes that the render tilemap offset is always (-0.5, -0.5).
    /// </remarks>
    public static Vector3Int GetDataTilePosition(Vector3Int renderTilePosition, int corner)
    {
        return renderTilePosition - CornerOffsets[corner];
    }

    /// <summary>
    /// True when <paramref name="tilemapTransform"/> is the render tilemap of a <see cref="DualGridTilemapModule"/>
    /// (its parent carries the module), whose cells sit half a cell off the data cells.
    /// </summary>
    public static bool IsRenderTilemap(Transform tilemapTransform)
    {
        Transform parent = tilemapTransform.parent;
        return parent != null && parent.TryGetComponent(out DualGridTilemapModule _);
    }

    /// <summary>
    /// Converts a valid data tile offset, used to offset a RenderTilePosition into a DataTilePosition,
    /// into a valid neighbor offset, used by Unity's Tilemap to calculate neighbors and apply rule tiles.
    /// </summary>
    /// <remarks>
    /// This is only valid for data tile offsets, aka, negative offsets.
    /// </remarks>
    public static Vector3Int ConvertDataTileOffsetToNeighborOffset(Vector3Int dataTileOffset)
    {
        return new Vector3Int(
            dataTileOffset.x == 0 ? 1 : dataTileOffset.x,
            dataTileOffset.y == 0 ? 1 : dataTileOffset.y,
            dataTileOffset.z
        );
    }
}
