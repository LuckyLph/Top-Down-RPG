using UnityEngine.Tilemaps;

/// <summary>
/// Editor preview tile for the dual-grid brush: placed as a preview on the data tilemap, it tells
/// <see cref="DualGridRuleTile"/> whether a hovered cell would be filled or emptied.
/// </summary>
public class DualGridPreviewTile : TileBase
{
    public bool IsFilled { get; private set; }

    public static DualGridPreviewTile Filled => Create(isFilled: true);
    public static DualGridPreviewTile NotFilled => Create(isFilled: false);

    /// <summary>
    /// Creates a new, named preview tile instance that reads as filled or empty.
    /// </summary>
    private static DualGridPreviewTile Create(bool isFilled)
    {
        var dualGridPreviewTile = CreateInstance<DualGridPreviewTile>();
        dualGridPreviewTile.name = $"{(isFilled ? "Filled" : "Empty")} Dual Grid Preview Tile";
        dualGridPreviewTile.IsFilled = isFilled;
        return dualGridPreviewTile;
    }
}
