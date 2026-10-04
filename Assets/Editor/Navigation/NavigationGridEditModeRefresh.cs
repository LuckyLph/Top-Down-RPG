using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Keeps built <see cref="NavigationGrid2D"/>s in step with tile painting outside Play Mode, so their gizmos show the
/// cells as they are now. In Play Mode the grids follow tile changes themselves.
/// </summary>
[InitializeOnLoad]
public static class NavigationGridEditModeRefresh
{
    static NavigationGridEditModeRefresh()
    {
        Tilemap.tilemapPositionsChanged += HandleTilemapPositionsChanged;
    }

    private static void HandleTilemapPositionsChanged(Tilemap tilemap, NativeArray<Vector3Int> positions)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        foreach (NavigationGrid2D grid in Object.FindObjectsByType<NavigationGrid2D>())
        {
            grid.HandleTilemapPositionsChanged(tilemap, positions);
        }
    }
}
