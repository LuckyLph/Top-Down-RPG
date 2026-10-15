using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The tile of a raised ground tilemap (<see cref="TerrainBlendRenderer"/>'s raised tilemap): a cell holding it is raised
/// ground, whose cliff face shows along its bottom edge, or, when it is a ramp, raised ground that slopes down to the
/// ground below there instead. Any other tile on that tilemap counts as raised ground without a ramp. Its sprite is a
/// swatch for the Tile Palette only; the terrain renderer draws the ground.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/World/Raised Ground Tile", fileName = "Tile_Raised")]
public class RaisedGroundTile : Tile
{
    [Tooltip("A ramp: raised ground that slopes down to the ground below along its bottom edge instead of showing a cliff face, and can be walked up and down there.")]
    [SerializeField] private bool ramp;

    public bool IsRamp => ramp;

    /// <summary>
    /// Creates a tile in memory, raised ground or a ramp, for tests.
    /// </summary>
    internal static RaisedGroundTile Create(bool isRamp)
    {
        RaisedGroundTile tile = CreateInstance<RaisedGroundTile>();
        tile.ramp = isRamp;
        return tile;
    }
}
