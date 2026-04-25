using UnityEngine;
using UnityEngine.Tilemaps;

public class NavigationTerrainSource2D : MonoBehaviour
{
    [SerializeField] private Tilemap dataTilemap;
    [SerializeField] private Tilemap collisionTilemap;
    [SerializeField] private Tilemap renderTilemap;
    [SerializeField] private TerrainType2D terrainType;

    public Tilemap DataTilemap => dataTilemap;
    public Tilemap CollisionTilemap => collisionTilemap;
    public Tilemap RenderTilemap => renderTilemap;
    public TerrainType2D TerrainType => terrainType;
    public bool IsConfigured => dataTilemap != null && terrainType != null;

    public void Configure(Tilemap dataMap, Tilemap collisionMap, Tilemap renderMap, TerrainType2D type)
    {
        dataTilemap = dataMap;
        collisionTilemap = collisionMap;
        renderTilemap = renderMap;
        terrainType = type;
    }

    private void Reset()
    {
        if (dataTilemap == null)
        {
            dataTilemap = GetComponent<Tilemap>();
        }
    }
}
