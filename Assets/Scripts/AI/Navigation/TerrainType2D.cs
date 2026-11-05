using UnityEngine;

/// <summary>
/// A terrain tag that navigation sources put on cells and movement profiles give rules for: the asset itself is the
/// tag. Read-only at runtime; tests build their own with <see cref="Create"/>.
/// </summary>
[CreateAssetMenu(menuName = "TopDownRPG/AI/Navigation/Terrain Type", fileName = "TerrainType2D")]
public class TerrainType2D : ScriptableObject
{
    /// <summary>
    /// Builds a terrain type in code named <paramref name="terrainName"/> ("terrain" when empty), for tests.
    /// </summary>
    public static TerrainType2D Create(string terrainName)
    {
        TerrainType2D terrain = CreateInstance<TerrainType2D>();
        terrain.name = string.IsNullOrWhiteSpace(terrainName) ? "terrain" : terrainName;
        return terrain;
    }
}
