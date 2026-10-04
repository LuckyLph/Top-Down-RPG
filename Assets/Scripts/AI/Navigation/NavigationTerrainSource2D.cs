using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Marks the tilemap on this GameObject as terrain of one <see cref="TerrainType2D"/> for the
/// <see cref="NavigationGrid2D"/> above it. On dual-grid terrain it belongs on the data tilemap (the one carrying the
/// <see cref="DualGridTilemapModule"/>), never on the half-cell-offset render tilemap below it.
/// </summary>
[RequireComponent(typeof(Tilemap))]
[DisallowMultipleComponent]
public class NavigationTerrainSource2D : MonoBehaviour
{
    [SerializeField] private TerrainType2D terrainType;

    private Tilemap dataTilemap;

    public Tilemap DataTilemap
    {
        get
        {
            if (dataTilemap == null)
            {
                dataTilemap = GetComponent<Tilemap>();
            }

            return dataTilemap;
        }
    }

    public TerrainType2D TerrainType => terrainType;

    /// <summary>
    /// True when this tilemap is the render tilemap of a dual-grid terrain, whose cells sit half a cell off the
    /// data cells and must not feed navigation.
    /// </summary>
    public bool IsDualGridRenderTilemap
    {
        get
        {
            Transform parent = transform.parent;
            return parent != null && parent.TryGetComponent(out DualGridTilemapModule _);
        }
    }

    public void Configure(TerrainType2D type)
    {
        terrainType = type;
    }
}
