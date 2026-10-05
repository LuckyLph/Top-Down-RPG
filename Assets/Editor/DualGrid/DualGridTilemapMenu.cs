using UnityEditor;
using UnityEngine;

/// <summary>
/// GameObject menu entry that creates a dual-grid tilemap in the scene.
/// </summary>
public static class DualGridTilemapMenu
{
    /// <summary>
    /// Creates a dual-grid tilemap under the selected <see cref="Grid"/>, or under a new grid when none is selected,
    /// and selects it.
    /// </summary>
    [MenuItem("GameObject/2D Object/Tilemap/Dual Grid Tilemap ", false, 0)]
    private static void CreateDualGridTilemapMenu()
    {
        GameObject selected = Selection.activeGameObject;
        Grid selectedGrid = selected == null ? null : selected.GetComponent<Grid>();

        var newDualGridTilemapModule = DualGridTilemapModuleEditor.CreateNewDualGridTilemap(selectedGrid);

        Selection.activeGameObject = newDualGridTilemapModule.gameObject;
    }
}
