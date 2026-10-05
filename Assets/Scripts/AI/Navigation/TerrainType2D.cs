using UnityEngine;

/// <summary>
/// A terrain tag that navigation sources put on cells and movement profiles give rules for. Read-only at runtime;
/// tests build their own with <see cref="Create"/>.
/// </summary>
[CreateAssetMenu(menuName = "AI/Navigation/Terrain Type", fileName = "TerrainType2D")]
public class TerrainType2D : ScriptableObject
{
    [SerializeField] private string terrainId = "terrain";

    public string TerrainId => string.IsNullOrWhiteSpace(terrainId) ? name : terrainId;

    /// <summary>
    /// Builds a terrain type in code, for tests.
    /// </summary>
    public static TerrainType2D Create(string id)
    {
        TerrainType2D terrain = CreateInstance<TerrainType2D>();
        terrain.terrainId = string.IsNullOrWhiteSpace(id) ? "terrain" : id;
        return terrain;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Fills an empty terrain id with the asset name.
    /// </summary>
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(terrainId))
        {
            terrainId = name;
        }
    }
#endif
}
