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
    [Tooltip("Where terrain layers overlap, only the highest priority decides walkability and cost, like a road or a " +
        "bridge drawn over other terrain. Layers of equal priority combine: most restrictive walkability, highest cost.")]
    [SerializeField] private int priority;

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
    public int Priority => priority;

    /// <summary>
    /// True when this tilemap is the render tilemap of a dual-grid terrain, whose cells sit half a cell off the
    /// data cells and must not feed navigation.
    /// </summary>
    public bool IsDualGridRenderTilemap => DualGridUtils.IsRenderTilemap(transform);

    /// <summary>
    /// Tells the parent grid to rebuild its gizmo grid after an inspector change.
    /// </summary>
    private void OnValidate()
    {
        NavigationGrid2D grid = GetComponentInParent<NavigationGrid2D>();
        if (grid != null)
        {
            grid.MarkGizmoGridStale();
        }
    }

    /// <summary>
    /// Sets the terrain type and layer priority in code, for sources built by tests. The grid reads them on its next
    /// <see cref="NavigationGrid2D.BuildGrid"/>.
    /// </summary>
    public void Configure(TerrainType2D type, int layerPriority = 0)
    {
        terrainType = type;
        priority = layerPriority;
    }
}
