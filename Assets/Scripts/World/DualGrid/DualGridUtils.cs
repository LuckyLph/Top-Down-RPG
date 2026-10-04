using UnityEngine;

public static class DualGridUtils
{
    /// <summary>
    /// Gets the 4 render tile positions from a <paramref name="dataTilePosition"/>.
    /// </summary>
    /// <remarks>
    /// Assumes that the render tilemap offset is always (-0.5, -0.5).
    /// </remarks>
    public static Vector3Int[] GetRenderTilePositions(Vector3Int dataTilePosition)
    {
        return new Vector3Int[]
        {
            dataTilePosition + new Vector3Int(0, 0, 0),
            dataTilePosition + new Vector3Int(1, 0, 0),
            dataTilePosition + new Vector3Int(0, 1, 0),
            dataTilePosition + new Vector3Int(1, 1, 0)
        };
    }

    /// <summary>
    /// Gets the 4 data tile positions from a <paramref name="renderTilePosition"/>.
    /// </summary>
    /// <remarks>
    /// Assumes that the render tilemap offset is always (-0.5, -0.5).
    /// </remarks>
    public static Vector3Int[] GetDataTilePositions(Vector3Int renderTilePosition)
    {
        return new Vector3Int[]
        {
            renderTilePosition - new Vector3Int(0, 0, 0),
            renderTilePosition - new Vector3Int(1, 0, 0),
            renderTilePosition - new Vector3Int(0, 1, 0),
            renderTilePosition - new Vector3Int(1, 1, 0)
        };
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
