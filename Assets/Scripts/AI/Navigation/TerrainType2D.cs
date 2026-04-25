using UnityEngine;

[CreateAssetMenu(menuName = "AI/Navigation/Terrain Type", fileName = "TerrainType2D")]
public class TerrainType2D : ScriptableObject
{
    [SerializeField] private string terrainId = "terrain";

    public string TerrainId => string.IsNullOrWhiteSpace(terrainId) ? name : terrainId;

    public void Configure(string id)
    {
        terrainId = string.IsNullOrWhiteSpace(id) ? "terrain" : id;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(terrainId))
        {
            terrainId = name;
        }
    }
#endif
}
